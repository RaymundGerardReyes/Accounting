import os
import json
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Optional, Dict, Any, List
from sqlalchemy.orm import Session
from sqlalchemy.exc import IntegrityError
from models import Order, PaymentAttempt, WebhookEvent, FulfillmentOutbox, utc_now
from providers.base import PaymentProvider
from providers.paymongo import PayMongoProvider
from services.receipt_service import (
    get_payment_method_info,
    extract_payment_method_from_paymongo,
    build_receipt_and_acknowledgement,
    persist_receipt_on_order,
    generate_receipt_pdf_bytes,
    resolve_recipient_email,
    format_philippine_phone,
)

DATA_DIR = Path(__file__).resolve().parent.parent / "data"

VALID_TRANSITIONS = {
    "pending": {"requires_action", "processing", "failed", "unknown"},
    "requires_action": {"processing", "succeeded", "failed", "cancelled", "expired", "unknown"},
    "processing": {"succeeded", "failed", "unknown"},
    "unknown": {"succeeded", "failed", "expired", "requires_action"},
    # Terminal states have no valid transitions:
    "succeeded": set(),
    "failed": set(),
    "cancelled": set(),
    "expired": set(),
}

class PaymentOrchestrator:
    def __init__(self, provider: Optional[PaymentProvider] = None):
        self.provider = provider or PayMongoProvider()

    def create_order(
        self,
        db: Session,
        title: str,
        category: str,
        reference_code: str,
        amount_minor: Optional[int] = None,
        quantity: int = 1,
        user_id: str = "acme_admin",
        metadata: Optional[Dict[str, Any]] = None,
    ) -> Order:
        """
        Creates an order with authoritative server-side pricing.
        """
        if amount_minor is None or amount_minor <= 0:
            # Authoritative pricing lookup
            if category == "product_order":
                product_data_file = DATA_DIR / "product_data.json"
                if product_data_file.exists():
                    with open(product_data_file, "r") as f:
                        p_data = json.load(f)
                    matching = next((p for p in p_data.get("productData", []) if p["product"].lower() == reference_code.lower()), None)
                    if matching:
                        unit_price = matching["unitCost"]
                        amount_minor = int(unit_price * quantity * 100)
                    else:
                        amount_minor = 10000 * quantity  # Default ₱100.00
                else:
                    amount_minor = 10000 * quantity
            elif category == "department_invoice":
                # Check department configuration for default allocation
                dept_defaults = {
                    "manufacturing": 4500000,  # ₱45,000.00
                    "assembly": 2800000,       # ₱28,000.00
                    "qa": 1500000,             # ₱15,000.00
                    "logistics": 1000000,      # ₱10,000.00
                }
                cfg_file = DATA_DIR / "configuration.json"
                matched_budget = None
                if cfg_file.exists():
                    try:
                        with open(cfg_file, "r") as f:
                            cfg_data = json.load(f)
                        dept = next((d for d in cfg_data.get("departments", []) if d.get("id", "").lower() == reference_code.lower() or d.get("name", "").lower() == reference_code.lower()), None)
                        if dept and "budget" in dept:
                            matched_budget = int((dept["budget"] / 10) * 100)  # 10% monthly settlement in centavos
                    except Exception:
                        pass
                amount_minor = matched_budget or dept_defaults.get(reference_code.lower(), 2500000)
            else:
                amount_minor = 10000

        order_count = db.query(Order).count() + 1
        prefix = "ORD" if category == "product_order" else "INV"
        order_number = f"{prefix}-{datetime.now().strftime('%Y%m')}-{order_count:04d}"

        meta = dict(metadata or {})
        if meta.get("customer_phone"):
            meta["customer_phone"] = format_philippine_phone(meta["customer_phone"]) or meta["customer_phone"]

        order = Order(
            id=str(uuid.uuid4()),
            user_id=user_id,
            order_number=order_number,
            title=title,
            category=category,
            reference_code=reference_code,
            amount_minor=amount_minor,
            currency="PHP",
            status="pending",
            metadata_json=json.dumps(meta),
        )
        db.add(order)
        db.commit()
        db.refresh(order)
        return order

    async def initiate_payment_attempt(
        self,
        db: Session,
        order_id: str,
        idempotency_key: str,
        payment_method: str = "hosted_checkout",
        success_url: Optional[str] = None,
        cancel_url: Optional[str] = None,
    ) -> PaymentAttempt:
        """
        Initiates a payment attempt, ensuring idempotency and strict state rules.
        """
        order = db.query(Order).filter(Order.id == order_id).first()
        if not order:
            raise ValueError(f"Order '{order_id}' not found.")

        if order.status == "paid":
            raise ValueError(f"Order '{order.order_number}' is already fully paid.")

        if order.status == "cancelled":
            raise ValueError(f"Order '{order.order_number}' has been cancelled and cannot accept payment attempts.")

        # Check existing attempt by idempotency key
        existing_attempt = (
            db.query(PaymentAttempt)
            .filter(
                PaymentAttempt.provider == "paymongo",
                PaymentAttempt.idempotency_key == idempotency_key,
            )
            .first()
        )
        if existing_attempt:
            if existing_attempt.order_id != order.id:
                raise ValueError(f"Idempotency key '{idempotency_key}' is already in use for another order.")
            # Idempotent return: don't create a new session
            return existing_attempt

        # Default URLs
        if not success_url:
            success_url = f"http://localhost:3000/payment/return?order_id={order.id}"
        if not cancel_url:
            cancel_url = f"http://localhost:3000/checkout?order_id={order.id}&status=cancelled"

        # Normalize payment method
        pm_info = get_payment_method_info(payment_method)
        normalized_pm = pm_info["code"]

        # Create record in 'pending' state
        attempt = PaymentAttempt(
            id=str(uuid.uuid4()),
            order_id=order.id,
            provider="paymongo",
            payment_method=normalized_pm,
            amount_minor=order.amount_minor,
            currency=order.currency,
            status="pending",
            idempotency_key=idempotency_key,
        )
        db.add(attempt)
        db.commit()
        db.refresh(attempt)

        try:
            # Call PayMongo provider (or sandbox simulator)
            meta_dict = order.metadata_dict if hasattr(order, "metadata_dict") else {}
            session_metadata = {
                "payment_attempt_id": attempt.id,
                "payment_method": normalized_pm,
                "order_id": order.id,
                "order_number": order.order_number,
            }
            if meta_dict.get("customer_name"):
                session_metadata["customer_name"] = meta_dict["customer_name"]
            if meta_dict.get("customer_email"):
                session_metadata["customer_email"] = meta_dict["customer_email"]
            if meta_dict.get("customer_phone"):
                session_metadata["customer_phone"] = meta_dict["customer_phone"]

            session_result = await self.provider.create_checkout_session(
                order_id=order.id,
                order_number=order.order_number,
                title=order.title,
                amount_minor=order.amount_minor,
                currency=order.currency,
                success_url=success_url,
                cancel_url=cancel_url,
                payment_methods=["qrph", "gcash", "paymaya", "dob", "card"],
                metadata=session_metadata,
            )

            attempt.provider_payment_id = session_result.provider_payment_id
            attempt.checkout_url = session_result.checkout_url
            attempt.status = session_result.status
            db.commit()
            db.refresh(attempt)
            return attempt
        except Exception as e:
            # Network uncertainty or gateway error
            attempt.status = "unknown"
            attempt.failure_message = str(e)
            db.commit()
            db.refresh(attempt)
            raise e

    def process_webhook(
        self,
        db: Session,
        raw_body: bytes,
        signature_header: str,
    ) -> Dict[str, Any]:
        """
        Verifies HMAC signature, deduplicates event, and processes state transitions atomically.
        """
        # 1. Verify HMAC-SHA256 signature
        if not self.provider.verify_webhook_signature(raw_body, signature_header):
            raise PermissionError("Invalid Paymongo-Signature HMAC signature.")

        # 2. Parse payload
        try:
            payload = json.loads(raw_body.decode("utf-8"))
        except Exception as e:
            raise ValueError(f"Malformed JSON in webhook body: {str(e)}")

        event_data = payload.get("data", {})
        provider_event_id = event_data.get("id") or str(uuid.uuid4())
        event_type = event_data.get("attributes", {}).get("type", "unknown")

        # 3. Webhook Deduplication (Rule 2)
        existing_event = (
            db.query(WebhookEvent)
            .filter(
                WebhookEvent.provider == "paymongo",
                WebhookEvent.provider_event_id == provider_event_id,
            )
            .first()
        )
        if existing_event:
            return {"received": True, "duplicate": True, "event_id": provider_event_id}

        # Store incoming event
        webhook_record = WebhookEvent(
            id=str(uuid.uuid4()),
            provider="paymongo",
            provider_event_id=provider_event_id,
            event_type=event_type,
            processing_status="received",
            payload=raw_body.decode("utf-8"),
        )
        db.add(webhook_record)
        try:
            db.commit()
        except IntegrityError:
            db.rollback()
            return {"received": True, "duplicate": True, "event_id": provider_event_id}

        # 4. Resolve PaymentAttempt & Order
        resource_data = event_data.get("attributes", {}).get("data", {})
        resource_id = resource_data.get("id")  # checkout session ID (cs_...) or payment ID
        resource_attrs = resource_data.get("attributes", {})
        metadata = resource_attrs.get("metadata", {})

        attempt = None
        # Try finding by provider_payment_id
        if resource_id:
            attempt = db.query(PaymentAttempt).filter(PaymentAttempt.provider_payment_id == resource_id).first()

        # Try finding by payment_attempt_id in metadata
        if not attempt and "payment_attempt_id" in metadata:
            attempt = db.query(PaymentAttempt).filter(PaymentAttempt.id == metadata["payment_attempt_id"]).first()

        # Try finding by order_id in metadata
        if not attempt and "order_id" in metadata:
            attempt = (
                db.query(PaymentAttempt)
                .filter(PaymentAttempt.order_id == metadata["order_id"])
                .order_by(PaymentAttempt.created_at.desc())
                .first()
            )

        if not attempt:
            webhook_record.processing_status = "ignored"
            db.commit()
            return {"received": True, "status": "unmatched_attempt", "event_id": provider_event_id}

        # Extract & normalize payment method from PayMongo webhook
        extracted_pm = extract_payment_method_from_paymongo(resource_attrs, fallback=attempt.payment_method)
        if extracted_pm:
            attempt.payment_method = extracted_pm

        # 5. Apply State Machine Rules
        order = db.query(Order).filter(Order.id == attempt.order_id).first()

        # Extract and persist customer billing info into order metadata
        from services.receipt_service import extract_customer_info_from_paymongo
        cust_info = extract_customer_info_from_paymongo(resource_attrs, order.metadata_dict if order else None)
        if order:
            o_meta = dict(order.metadata_dict or {})
            if cust_info.get("customer_name") and cust_info["customer_name"] != "Valued Customer":
                o_meta["customer_name"] = cust_info["customer_name"]
            if cust_info.get("customer_email") and not o_meta.get("customer_email"):
                o_meta["customer_email"] = cust_info["customer_email"]
            if cust_info.get("customer_phone") and not o_meta.get("customer_phone"):
                o_meta["customer_phone"] = cust_info["customer_phone"]
            o_meta["latest_provider_attrs"] = resource_attrs
            order.metadata_dict = o_meta
            db.add(order)

        paid_event_types = {
            "checkout_session.payment.paid",
            "payment.paid",
            "payment_intent.succeeded",
        }
        failed_event_types = {
            "payment.failed",
            "payment_intent.payment_failed",
            "checkout_session.payment.failed",
            "source.failed",
        }
        expired_event_types = {
            "checkout_session.expired",
        }
        cancelled_event_types = {
            "payment.canceled",
            "payment_intent.cancelled",
            "checkout_session.cancelled",
        }

        terminal_states = {"succeeded", "failed", "cancelled", "expired"}

        if event_type in paid_event_types:
            # Rule 1 & Rule 5: Terminal states & duplicate success handling
            if attempt.status == "succeeded":
                # Already succeeded, idempotent no-op
                pass
            elif order and order.status == "paid":
                # Order already paid by another attempt: flag for review
                attempt.status = "duplicate_paid_review"
                attempt.paid_at = utc_now()
            else:
                attempt.status = "succeeded"
                attempt.paid_at = utc_now()
                if order:
                    order.status = "paid"
                    order.updated_at = utc_now()

                # Rule 2: Outbox fulfillment record
                outbox_entry = FulfillmentOutbox(
                    id=str(uuid.uuid4()),
                    order_id=attempt.order_id,
                    payment_attempt_id=attempt.id,
                    event_type="order.paid",
                    status="pending",
                )
                db.add(outbox_entry)

        elif event_type in failed_event_types:
            # Check if failure is due to expired source
            failed_code_val = resource_attrs.get("failed_code") or resource_attrs.get("failure_code") or "PAYMENT_FAILED"
            failed_msg_val = resource_attrs.get("failed_message") or resource_attrs.get("failure_message") or "Payment processing failed."
            is_source_expired = (
                "expired" in str(failed_code_val).lower()
                or "expired" in str(failed_msg_val).lower()
                or "has expired status" in str(failed_msg_val).lower()
            )

            # Rule 1: Never overwrite an existing terminal state
            if attempt.status not in terminal_states:
                if is_source_expired:
                    attempt.status = "expired"
                    attempt.failure_code = "SOURCE_EXPIRED"
                    attempt.failure_message = failed_msg_val or "Payment source has expired. Please initiate a fresh checkout session."
                    attempt.checkout_url = None
                else:
                    attempt.status = "failed"
                    attempt.failure_code = failed_code_val
                    attempt.failure_message = failed_msg_val

        elif event_type in expired_event_types:
            # Rule 1: Never overwrite an existing terminal state
            if attempt.status not in terminal_states:
                attempt.status = "expired"
                attempt.failure_code = resource_attrs.get("failed_code") or "SESSION_EXPIRED"
                attempt.failure_message = resource_attrs.get("failed_message") or "Checkout session expired before completion."
                attempt.checkout_url = None

        elif event_type in cancelled_event_types:
            # Rule 1: Never overwrite an existing terminal state
            if attempt.status not in terminal_states:
                attempt.status = "cancelled"
                attempt.failure_code = resource_attrs.get("failed_code") or "SESSION_CANCELLED"
                attempt.failure_message = resource_attrs.get("failed_message") or "Payment checkout was cancelled."
                attempt.checkout_url = None

        webhook_record.processing_status = "processed"
        webhook_record.processed_at = utc_now()
        db.commit()

        # Process outbox fulfillment immediately
        self.process_fulfillment_outbox(db)

        return {"received": True, "status": attempt.status, "event_id": provider_event_id}

    def process_fulfillment_outbox(self, db: Session, retry_failed: bool = False) -> int:
        """
        Processes pending fulfillment records, updating Audit Trail in configuration.json and database.
        Optionally retries failed records if retry_failed is True.
        """
        query = db.query(FulfillmentOutbox)
        if retry_failed:
            target_outbox = (
                query.filter(FulfillmentOutbox.status.in_(["pending", "failed"]))
                .order_by(FulfillmentOutbox.created_at.asc())
                .all()
            )
        else:
            target_outbox = (
                query.filter(FulfillmentOutbox.status == "pending")
                .order_by(FulfillmentOutbox.created_at.asc())
                .all()
            )
        count = 0
        config_file = DATA_DIR / "configuration.json"

        for item in target_outbox:
            try:
                order = db.query(Order).filter(Order.id == item.order_id).first()
                if not order:
                    item.status = "failed"
                    item.processed_at = utc_now()
                    continue

                if config_file.exists():
                    try:
                        with open(config_file, "r") as f:
                            cfg_data = json.load(f)
                        
                        logs = cfg_data.get("auditLogs", [])
                        max_id = max([l.get("id", 0) for l in logs if isinstance(l.get("id"), int)], default=0)
                        new_log = {
                            "id": max_id + 1,
                            "action": f"Payment received: ₱{order.amount_minor / 100:,.2f} for {order.order_number} ({order.title})",
                            "user": "PayMongo Gateway",
                            "time": "Just now",
                            "severity": "success",
                        }
                        logs.insert(0, new_log)
                        cfg_data["auditLogs"] = logs[:20]  # keep top 20
                        
                        with open(config_file, "w") as f:
                            json.dump(cfg_data, f, indent=4)
                    except Exception as log_err:
                        print(f"Failed to update audit log in configuration.json: {log_err}")

                # Generate authoritative Official Receipt & Acknowledgement Report and dispatch via SMTP
                try:
                    from services.email_service import EmailService

                    attempt = db.query(PaymentAttempt).filter(PaymentAttempt.id == item.payment_attempt_id).first()
                    if not attempt:
                        attempt = (
                            db.query(PaymentAttempt)
                            .filter(PaymentAttempt.order_id == order.id)
                            .order_by(PaymentAttempt.created_at.desc())
                            .first()
                        )

                    if attempt:
                        meta = order.metadata_dict if hasattr(order, "metadata_dict") else {}
                        customer_email = resolve_recipient_email(meta)

                        receipt_data = build_receipt_and_acknowledgement(
                            order=order,
                            attempt=attempt,
                            outbox_status="completed",
                        )

                        smtp_delivery_result = {
                            "status": "skipped",
                            "recipient": customer_email,
                            "sent_at": None,
                            "message": "SMTP not configured or recipient email not found",
                        }

                        email_svc = EmailService()
                        if email_svc.is_configured() and customer_email:
                            try:
                                pdf_bytes = generate_receipt_pdf_bytes(receipt_data)
                                pdf_filename = receipt_data["pdf_filename"]
                                paid_at_str = receipt_data.get("paid_at_manila") or "Just now"

                                receipt_html = email_svc.render_payment_receipt_html(
                                    order_number=order.order_number,
                                    title=order.title,
                                    amount_php=receipt_data["amount_php"],
                                    payment_method=receipt_data["payment_method_label"],
                                    paid_at=paid_at_str,
                                    reference_id=item.payment_attempt_id,
                                    official_receipt_number=receipt_data["official_receipt_number"],
                                    acknowledgement_number=receipt_data["acknowledgement_number"],
                                    provider_payment_id=receipt_data["provider_payment_id"],
                                    vatable_sales_php=receipt_data["vatable_sales_php"],
                                    vat_amount_php=receipt_data["vat_amount_php"],
                                    verification_hash=receipt_data["verification_hash"],
                                    pdf_filename=pdf_filename,
                                    payment_rail=receipt_data["payment_rail"],
                                )

                                attachments = [
                                    {
                                        "filename": pdf_filename,
                                        "content": pdf_bytes,
                                        "subtype": "pdf",
                                    }
                                ]

                                dispatch_res = email_svc.send_email(
                                    to_email=customer_email,
                                    subject=f"[Official Receipt & Acknowledgement] Payment Confirmed - {order.order_number}",
                                    html_body=receipt_html,
                                    attachments=attachments,
                                )

                                if dispatch_res.get("success"):
                                    smtp_delivery_result = {
                                        "status": "sent",
                                        "recipient": customer_email,
                                        "sent_at": utc_now().isoformat(),
                                        "message": dispatch_res.get("message", "Dispatched with PDF attachment"),
                                        "latency_ms": dispatch_res.get("latency_ms", 0),
                                    }
                                else:
                                    smtp_delivery_result = {
                                        "status": "failed",
                                        "recipient": customer_email,
                                        "sent_at": None,
                                        "message": dispatch_res.get("message", "Dispatch failed"),
                                        "details": dispatch_res.get("details"),
                                    }
                            except Exception as email_err:
                                smtp_delivery_result = {
                                    "status": "failed",
                                    "recipient": customer_email,
                                    "sent_at": None,
                                    "message": str(email_err),
                                }
                                print(f"Payment receipt email dispatch notice: {email_err}")

                        receipt_data["smtp_delivery"] = smtp_delivery_result
                        persist_receipt_on_order(db, order, attempt, receipt_data)
                except Exception as outbox_receipt_err:
                    print(f"Outbox receipt generation notice: {outbox_receipt_err}")

                item.status = "completed"
                item.processed_at = utc_now()
                count += 1
            except Exception as item_err:
                item.status = "failed"
                item.processed_at = utc_now()
                print(f"Failed to process outbox item {item.id}: {item_err}")

        db.commit()
        return count

    async def reconcile_payment_attempt(self, db: Session, attempt_id: str) -> Dict[str, Any]:
        """
        Queries PayMongo provider for authoritative settlement status and commits state transitions.
        """
        attempt = db.query(PaymentAttempt).filter(PaymentAttempt.id == attempt_id).first()
        if not attempt:
            raise ValueError(f"Payment attempt '{attempt_id}' not found.")

        if not attempt.provider_payment_id:
            return {"attempt_id": attempt.id, "status": attempt.status, "reconciled": False, "reason": "No provider_payment_id"}

        # Rule 1: Terminal states (succeeded, failed, expired, cancelled) are immutable
        if attempt.status in ("succeeded", "failed", "expired", "cancelled"):
            return {
                "attempt_id": attempt.id,
                "status": attempt.status,
                "reconciled": True,
                "action": f"already_{attempt.status}",
            }

        provider_status = await self.provider.get_checkout_session(attempt.provider_payment_id)
        changed = False

        order = db.query(Order).filter(Order.id == attempt.order_id).first()
        if order and (provider_status.customer_name or provider_status.customer_email or provider_status.customer_phone):
            o_meta = dict(order.metadata_dict or {})
            if provider_status.customer_name and provider_status.customer_name != "Valued Customer":
                o_meta["customer_name"] = provider_status.customer_name
            if provider_status.customer_email and not o_meta.get("customer_email"):
                o_meta["customer_email"] = provider_status.customer_email
            if provider_status.customer_phone and not o_meta.get("customer_phone"):
                o_meta["customer_phone"] = provider_status.customer_phone
            if provider_status.raw_response and isinstance(provider_status.raw_response, dict):
                attrs = provider_status.raw_response.get("attributes", provider_status.raw_response)
                o_meta["latest_provider_attrs"] = attrs
            order.metadata_dict = o_meta
            db.add(order)

        if provider_status.status == "succeeded":
            if provider_status.payment_method:
                attempt.payment_method = provider_status.payment_method
            if order and order.status == "paid" and attempt.status != "succeeded":
                attempt.status = "duplicate_paid_review"
                attempt.paid_at = provider_status.paid_at or utc_now()
            else:
                attempt.status = "succeeded"
                attempt.paid_at = provider_status.paid_at or utc_now()
                if order:
                    order.status = "paid"
                    order.updated_at = utc_now()
                # Outbox
                outbox = FulfillmentOutbox(
                    id=str(uuid.uuid4()),
                    order_id=attempt.order_id,
                    payment_attempt_id=attempt.id,
                    event_type="order.paid",
                    status="pending",
                )
                db.add(outbox)
            changed = True
        elif provider_status.status in ("failed", "expired", "cancelled") and attempt.status not in ("succeeded", "failed", "expired", "cancelled"):
            attempt.status = provider_status.status
            attempt.failure_code = provider_status.failure_code
            attempt.failure_message = provider_status.failure_message
            if provider_status.status in ("expired", "cancelled"):
                attempt.checkout_url = None
            changed = True

        db.commit()
        if changed:
            self.process_fulfillment_outbox(db)

        return {
            "attempt_id": attempt.id,
            "status": attempt.status,
            "provider_status": provider_status.status,
            "changed": changed,
        }

    async def reconcile_all_unresolved(self, db: Session) -> List[Dict[str, Any]]:
        """
        Reconciles all attempts currently in non-terminal states.
        """
        unresolved = (
            db.query(PaymentAttempt)
            .filter(PaymentAttempt.status.in_(["pending", "requires_action", "processing", "unknown"]))
            .all()
        )
        results = []
        for att in unresolved:
            try:
                res = await self.reconcile_payment_attempt(db, att.id)
                results.append(res)
            except Exception as e:
                results.append({"attempt_id": att.id, "status": att.status, "error": str(e)})
        return results

