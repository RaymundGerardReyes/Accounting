from abc import ABC, abstractmethod
from dataclasses import dataclass
from typing import Optional, Dict, Any, List
from datetime import datetime

@dataclass
class CheckoutSessionResult:
    provider_payment_id: str
    checkout_url: str
    status: str
    raw_response: Dict[str, Any]

@dataclass
class PaymentStatusResult:
    provider_payment_id: str
    status: str
    paid_at: Optional[datetime]
    amount_minor: int
    currency: str
    failure_code: Optional[str] = None
    failure_message: Optional[str] = None
    payment_method: Optional[str] = None
    raw_response: Optional[Dict[str, Any]] = None

class PaymentProvider(ABC):
    @abstractmethod
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
        pass

    @abstractmethod
    async def get_checkout_session(self, provider_payment_id: str) -> PaymentStatusResult:
        pass

    @abstractmethod
    def verify_webhook_signature(self, raw_body: bytes, signature_header: str) -> bool:
        pass

