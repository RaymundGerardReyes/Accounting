import os
import base64
import hmac
import hashlib
import time
import uuid
from typing import Optional, Dict, Any, List
from datetime import datetime, timezone
import httpx
from providers.base import PaymentProvider, CheckoutSessionResult, PaymentStatusResult

DEFAULT_SIMULATOR_SECRET = "whsk_test_simulated_secret_12345"

class PayMongoProvider(PaymentProvider):
    def __init__(self):
        self._secret_key: Optional[str] = None
        self._webhook_secret: Optional[str] = None
        self.api_base_url = "https://api.paymongo.com/v1"

    @property
    def secret_key(self) -> str:
        if self._secret_key is not None:
            return self._secret_key.strip()
        return os.getenv("PAYMONGO_SECRET_KEY", "").strip()

    @secret_key.setter
    def secret_key(self, val: Optional[str]) -> None:
        self._secret_key = val

    @property
    def webhook_secret(self) -> str:
        if self._webhook_secret is not None:
            return self._webhook_secret.strip()
        return os.getenv("PAYMONGO_WEBHOOK_SECRET", "").strip() or DEFAULT_SIMULATOR_SECRET

    @webhook_secret.setter
    def webhook_secret(self, val: Optional[str]) -> None:
        self._webhook_secret = val

    @property
    def is_simulator(self) -> bool:
        return not bool(self.secret_key)

    def _get_auth_header(self) -> Dict[str, str]:
        token = base64.b64encode(f"{self.secret_key}:".encode("utf-8")).decode("utf-8")
        return {
            "Authorization": f"Basic {token}",
            "Content-Type": "application/json",
            "Accept": "application/json",
        }

    async def create_checkout_session(
        self,
        order_id: str,
        order_number: str,
        title: str,
        amount_minor: int,
        currency: str,
        success_url: str,
        cancel_url: str,
        payment_methods: Optional[List[str]] = None,
        metadata: Optional[Dict[str, Any]] = None,
    ) -> CheckoutSessionResult:
        if payment_methods is None:
            payment_methods = ["qrph", "gcash", "paymaya", "dob", "card"]

        # If live/test secret key is provided, call real PayMongo API
        if not self.is_simulator:
            headers = self._get_auth_header()
            payload = {
                "data": {
                    "attributes": {
                        "billing": {
                            "name": "Customer",
                            "email": os.getenv("SMTP_USER", "").strip() or "customer@example.com",
                        },
                        "send_email_receipt": True,
                        "show_description": True,
                        "show_line_items": True,
                        "description": f"{order_number}: {title}",
                        "line_items": [
                            {
                                "currency": currency,
                                "amount": amount_minor,
                                "name": title,
                                "quantity": 1,
                            }
                        ],
                        "payment_method_types": payment_methods,
                        "success_url": success_url,
                        "cancel_url": cancel_url,
                        "metadata": {
                            "order_id": order_id,
                            "order_number": order_number,
                            **(metadata or {}),
                        },
                    }
                }
            }

            try:
                async with httpx.AsyncClient(timeout=10.0) as client:
                    resp = await client.post(
                        f"{self.api_base_url}/checkout_sessions",
                        headers=headers,
                        json=payload,
                    )
                    resp.raise_for_status()
                    data = resp.json()["data"]
                    session_id = data["id"]
                    checkout_url = data["attributes"]["checkout_url"]
                    return CheckoutSessionResult(
                        provider_payment_id=session_id,
                        checkout_url=checkout_url,
                        status="requires_action",
                        raw_response=data,
                    )
            except Exception as e:
                print(f"PayMongo API notice (falling back to Local Sandbox Simulator): {e}")

        # Local Sandbox Simulator Fallback
        sim_id = f"cs_sim_{uuid.uuid4().hex[:16]}"
        attempt_id = metadata.get("payment_attempt_id") if metadata else None
        preferred_method = (metadata.get("payment_method") if metadata else None) or "qrph"
        target_id = attempt_id or order_id
        sim_checkout_url = (
            f"http://localhost:3000/payment/sandbox/{target_id}"
            f"?session_id={sim_id}&order_id={order_id}&method={preferred_method}"
        )
        return CheckoutSessionResult(
            provider_payment_id=sim_id,
            checkout_url=sim_checkout_url,
            status="requires_action",
            raw_response={
                "id": sim_id,
                "type": "checkout_session",
                "attributes": {
                    "checkout_url": sim_checkout_url,
                    "payment_method_types": payment_methods,
                    "status": "active",
                    "mode": "simulator",
                },
            },
        )

    async def get_checkout_session(self, provider_payment_id: str) -> PaymentStatusResult:
        if not self.is_simulator and not provider_payment_id.startswith("cs_sim_"):
            headers = self._get_auth_header()
            try:
                async with httpx.AsyncClient(timeout=10.0) as client:
                    resp = await client.get(
                        f"{self.api_base_url}/checkout_sessions/{provider_payment_id}",
                        headers=headers,
                    )
                    resp.raise_for_status()
                    data = resp.json()["data"]
                    attrs = data.get("attributes", {})
                    payments = attrs.get("payments", [])
                    session_status = attrs.get("status", "active")

                    internal_status = "requires_action"
                    paid_at = None
                    failure_code = None
                    failure_message = None

                    # Check if any payment succeeded
                    paid_payment = next(
                        (p for p in payments if p.get("attributes", {}).get("status") == "paid"),
                        None
                    )

                    if paid_payment:
                        internal_status = "succeeded"
                        p_attrs = paid_payment.get("attributes", {})
                        paid_timestamp = p_attrs.get("paid_at")
                        if paid_timestamp:
                            paid_at = datetime.fromtimestamp(paid_timestamp, tz=timezone.utc)
                        else:
                            paid_at = datetime.now(timezone.utc)
                    elif session_status == "paid":
                        internal_status = "succeeded"
                        paid_at = datetime.now(timezone.utc)
                    elif session_status == "expired":
                        internal_status = "expired"
                        failure_code = "SESSION_EXPIRED"
                        failure_message = "Checkout session expired before payment completion."
                    elif session_status == "cancelled":
                        internal_status = "cancelled"
                        failure_code = "SESSION_CANCELLED"
                        failure_message = "Checkout session was cancelled."
                    elif payments:
                        latest_payment = payments[-1]
                        payment_attrs = latest_payment.get("attributes", {})
                        p_status = payment_attrs.get("status")
                        if p_status == "failed":
                            internal_status = "failed"
                            failure_code = payment_attrs.get("failed_code") or payment_attrs.get("failure_code") or "PAYMENT_FAILED"
                            failure_message = payment_attrs.get("failed_message") or payment_attrs.get("failure_message") or "Payment processing failed."

                    from services.receipt_service import extract_payment_method_from_paymongo
                    resolved_pm = extract_payment_method_from_paymongo(attrs)

                    line_items = attrs.get("line_items", [{}])
                    amount_minor = line_items[0].get("amount", 0) if line_items else 0
                    currency = line_items[0].get("currency", "PHP") if line_items else "PHP"

                    return PaymentStatusResult(
                        provider_payment_id=provider_payment_id,
                        status=internal_status,
                        paid_at=paid_at,
                        amount_minor=amount_minor,
                        currency=currency,
                        failure_code=failure_code,
                        failure_message=failure_message,
                        payment_method=resolved_pm,
                        raw_response=data,
                    )
            except Exception as e:
                print(f"PayMongo get_checkout_session notice (falling back to simulator state): {e}")

        # Simulated checkout session status lookup
        return PaymentStatusResult(
            provider_payment_id=provider_payment_id,
            status="requires_action",
            paid_at=None,
            amount_minor=0,
            currency="PHP",
            raw_response={"id": provider_payment_id, "simulator": True},
        )

    def verify_webhook_signature(self, raw_body: bytes, signature_header: str) -> bool:
        """
        Paymongo-Signature format:
        t=<timestamp>,te=<test_mode_signature>,li=<live_mode_signature>
        """
        if not signature_header:
            return False

        secret = self.webhook_secret
        if not secret:
            return False

        parts = {}
        for item in signature_header.split(","):
            if "=" in item:
                k, v = item.strip().split("=", 1)
                parts[k.strip()] = v.strip()

        timestamp = parts.get("t")
        if not timestamp:
            return False

        # Signed payload is "{timestamp}.{raw_body}"
        signed_payload = f"{timestamp}.".encode("utf-8") + raw_body
        expected_sig = hmac.new(
            secret.encode("utf-8"),
            signed_payload,
            hashlib.sha256,
        ).hexdigest()

        # Check against te (test mode) or li (live mode)
        te_sig = parts.get("te")
        li_sig = parts.get("li")

        valid = False
        if te_sig and hmac.compare_digest(expected_sig, te_sig):
            valid = True
        elif li_sig and hmac.compare_digest(expected_sig, li_sig):
            valid = True

        return valid

    def sign_simulated_payload(self, raw_body: bytes, is_live: bool = False) -> str:
        """
        Helper for generating valid Paymongo-Signature headers during testing and local simulation.
        """
        timestamp = str(int(time.time()))
        signed_payload = f"{timestamp}.".encode("utf-8") + raw_body
        signature = hmac.new(
            self.webhook_secret.encode("utf-8"),
            signed_payload,
            hashlib.sha256,
        ).hexdigest()

        if is_live:
            return f"t={timestamp},li={signature}"
        return f"t={timestamp},te={signature}"

