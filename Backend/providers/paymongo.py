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
        self.secret_key = os.getenv("PAYMONGO_SECRET_KEY", "").strip()
        self.webhook_secret = os.getenv("PAYMONGO_WEBHOOK_SECRET", "").strip() or DEFAULT_SIMULATOR_SECRET
        self.api_base_url = "https://api.paymongo.com/v1"
        self.is_simulator = not bool(self.secret_key)

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
                            "email": "customer@example.com",
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
                async with httpx.AsyncClient(timeout=15.0) as client:
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
                # Log or rethrow as appropriate
                raise RuntimeError(f"PayMongo API error: {str(e)}") from e

        # Local Sandbox Simulator Fallback
        sim_id = f"cs_sim_{uuid.uuid4().hex[:16]}"
        attempt_id = metadata.get("payment_attempt_id") if metadata else None
        target_id = attempt_id or order_id
        sim_checkout_url = f"http://localhost:3000/payment/sandbox/{target_id}?session_id={sim_id}&order_id={order_id}"
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

                    # If payments list exists, inspect the latest payment
                    if payments:
                        latest_payment = payments[-1]
                        payment_attrs = latest_payment.get("attributes", {})
                        p_status = payment_attrs.get("status")
                        if p_status == "paid":
                            internal_status = "succeeded"
                            paid_timestamp = payment_attrs.get("paid_at")
                            if paid_timestamp:
                                paid_at = datetime.fromtimestamp(paid_timestamp, tz=timezone.utc)
                        elif p_status == "failed":
                            internal_status = "failed"
                            failure_code = payment_attrs.get("failed_code")
                            failure_message = payment_attrs.get("failed_message")
                    elif session_status == "paid":
                        internal_status = "succeeded"
                        paid_at = datetime.now(timezone.utc)
                    elif session_status == "expired":
                        internal_status = "expired"
                    elif session_status == "cancelled":
                        internal_status = "cancelled"

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
                        raw_response=data,
                    )
            except Exception as e:
                raise RuntimeError(f"Failed to retrieve checkout session: {str(e)}") from e

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

