import uuid
import json
from datetime import datetime, timezone
from sqlalchemy import (
    Column,
    String,
    BigInteger,
    Text,
    DateTime,
    ForeignKey,
    UniqueConstraint,
    Index,
)
from sqlalchemy.orm import relationship
from database import Base, SessionLocal, engine

def utc_now():
    return datetime.now(timezone.utc)

class Order(Base):
    __tablename__ = "orders"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    user_id = Column(String(64), nullable=False, default="acme_admin")
    order_number = Column(String(64), unique=True, nullable=False, index=True)
    title = Column(String(255), nullable=False)
    category = Column(String(32), nullable=False)  # 'product_order' | 'department_invoice'
    reference_code = Column(String(64), nullable=False)
    amount_minor = Column(BigInteger, nullable=False)  # PHP centavos (> 0)
    currency = Column(String(3), nullable=False, default="PHP")
    status = Column(String(32), nullable=False, default="pending", index=True)  # pending | paid | cancelled
    metadata_json = Column(Text, nullable=True)
    created_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)
    updated_at = Column(DateTime(timezone=True), default=utc_now, onupdate=utc_now, nullable=False)

    payment_attempts = relationship("PaymentAttempt", back_populates="order", cascade="all, delete-orphan")
    fulfillment_records = relationship("FulfillmentOutbox", back_populates="order", cascade="all, delete-orphan")

    @property
    def metadata_dict(self):
        if self.metadata_json:
            try:
                return json.loads(self.metadata_json)
            except Exception:
                return {}
        return {}

    @metadata_dict.setter
    def metadata_dict(self, value):
        self.metadata_json = json.dumps(value or {})

    def to_dict(self):
        return {
            "id": self.id,
            "user_id": self.user_id,
            "order_number": self.order_number,
            "title": self.title,
            "category": self.category,
            "reference_code": self.reference_code,
            "amount_minor": self.amount_minor,
            "amount": self.amount_minor / 100.0,
            "currency": self.currency,
            "status": self.status,
            "metadata": self.metadata_dict,
            "created_at": self.created_at.isoformat() if self.created_at else None,
            "updated_at": self.updated_at.isoformat() if self.updated_at else None,
        }

class PaymentAttempt(Base):
    __tablename__ = "payment_attempts"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    order_id = Column(String(36), ForeignKey("orders.id", ondelete="CASCADE"), nullable=False, index=True)
    provider = Column(String(32), nullable=False, default="paymongo")
    payment_method = Column(String(32), nullable=False, default="hosted_checkout")
    provider_payment_id = Column(String(128), nullable=True, index=True)  # PayMongo Checkout Session ID (cs_...)
    amount_minor = Column(BigInteger, nullable=False)
    currency = Column(String(3), nullable=False, default="PHP")
    status = Column(String(32), nullable=False, default="pending", index=True)
    # Statuses: 'pending' | 'requires_action' | 'processing' | 'succeeded' | 'failed' | 'cancelled' | 'expired' | 'unknown'
    idempotency_key = Column(String(128), nullable=False)
    checkout_url = Column(Text, nullable=True)
    failure_code = Column(String(64), nullable=True)
    failure_message = Column(Text, nullable=True)
    expires_at = Column(DateTime(timezone=True), nullable=True)
    paid_at = Column(DateTime(timezone=True), nullable=True)
    created_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)
    updated_at = Column(DateTime(timezone=True), default=utc_now, onupdate=utc_now, nullable=False)

    order = relationship("Order", back_populates="payment_attempts")
    fulfillment_records = relationship("FulfillmentOutbox", back_populates="payment_attempt", cascade="all, delete-orphan")

    __table_args__ = (
        UniqueConstraint("provider", "idempotency_key", name="uq_payment_attempts_provider_idempotency"),
    )

    def to_dict(self):
        return {
            "id": self.id,
            "order_id": self.order_id,
            "provider": self.provider,
            "payment_method": self.payment_method,
            "provider_payment_id": self.provider_payment_id,
            "amount_minor": self.amount_minor,
            "amount": self.amount_minor / 100.0,
            "currency": self.currency,
            "status": self.status,
            "idempotency_key": self.idempotency_key,
            "checkout_url": self.checkout_url,
            "failure_code": self.failure_code,
            "failure_message": self.failure_message,
            "expires_at": self.expires_at.isoformat() if self.expires_at else None,
            "paid_at": self.paid_at.isoformat() if self.paid_at else None,
            "created_at": self.created_at.isoformat() if self.created_at else None,
            "updated_at": self.updated_at.isoformat() if self.updated_at else None,
        }

class WebhookEvent(Base):
    __tablename__ = "webhook_events"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    provider = Column(String(32), nullable=False, default="paymongo")
    provider_event_id = Column(String(128), nullable=False, index=True)
    event_type = Column(String(128), nullable=False)
    processing_status = Column(String(32), nullable=False, default="received")  # received | processed | ignored | failed
    received_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)
    processed_at = Column(DateTime(timezone=True), nullable=True)
    payload = Column(Text, nullable=False)

    __table_args__ = (
        UniqueConstraint("provider", "provider_event_id", name="uq_webhook_events_provider_event"),
    )

    def to_dict(self):
        payload_data = None
        if self.payload:
            try:
                payload_data = json.loads(self.payload)
            except Exception:
                payload_data = self.payload
        return {
            "id": self.id,
            "provider": self.provider,
            "provider_event_id": self.provider_event_id,
            "event_type": self.event_type,
            "processing_status": self.processing_status,
            "received_at": self.received_at.isoformat() if self.received_at else None,
            "processed_at": self.processed_at.isoformat() if self.processed_at else None,
            "payload": payload_data,
        }

class FulfillmentOutbox(Base):
    __tablename__ = "fulfillment_outbox"

    id = Column(String(36), primary_key=True, default=lambda: str(uuid.uuid4()))
    order_id = Column(String(36), ForeignKey("orders.id", ondelete="CASCADE"), nullable=False)
    payment_attempt_id = Column(String(36), ForeignKey("payment_attempts.id", ondelete="CASCADE"), nullable=False)
    event_type = Column(String(64), nullable=False, default="order.paid")
    status = Column(String(32), nullable=False, default="pending", index=True)  # pending | completed | failed
    created_at = Column(DateTime(timezone=True), default=utc_now, nullable=False)
    processed_at = Column(DateTime(timezone=True), nullable=True)

    order = relationship("Order", back_populates="fulfillment_records")
    payment_attempt = relationship("PaymentAttempt", back_populates="fulfillment_records")

    def to_dict(self):
        return {
            "id": self.id,
            "order_id": self.order_id,
            "payment_attempt_id": self.payment_attempt_id,
            "event_type": self.event_type,
            "status": self.status,
            "created_at": self.created_at.isoformat() if self.created_at else None,
            "processed_at": self.processed_at.isoformat() if self.processed_at else None,
        }

def init_db():
    # Ensure finance_models are registered on Base.metadata before create_all
    import finance_models  # noqa: F401
    Base.metadata.create_all(bind=engine)
    db = SessionLocal()
    try:
        # Check if seed orders exist
        if db.query(Order).count() == 0:
            seed_orders = [
                Order(
                    order_number="ORD-2025-001",
                    title="Batch Production Run: Product A (1,000 Units)",
                    category="product_order",
                    reference_code="Product A",
                    amount_minor=12500000,  # ₱125,000.00 in centavos
                    currency="PHP",
                    status="pending",
                    metadata_json=json.dumps({"unit_price": 125, "quantity": 1000, "product": "Product A"})
                ),
                Order(
                    order_number="ORD-2025-002",
                    title="High-Yield Inventory Order: Product B (2,100 Units)",
                    category="product_order",
                    reference_code="Product B",
                    amount_minor=18700000,  # ₱187,000.00
                    currency="PHP",
                    status="pending",
                    metadata_json=json.dumps({"unit_price": 89.05, "quantity": 2100, "product": "Product B"})
                ),
                Order(
                    order_number="INV-MFG-2025-01",
                    title="Manufacturing Plant Utilities & Raw Materials Settlement",
                    category="department_invoice",
                    reference_code="manufacturing",
                    amount_minor=4500000,  # ₱45,000.00
                    currency="PHP",
                    status="pending",
                    metadata_json=json.dumps({"department": "Manufacturing", "cost_center": "CC-001", "period": "June 2025"})
                ),
                Order(
                    order_number="INV-ASY-2025-01",
                    title="Assembly Line Fasteners & Tooling Supply Settlement",
                    category="department_invoice",
                    reference_code="assembly",
                    amount_minor=2800000,  # ₱28,000.00
                    currency="PHP",
                    status="pending",
                    metadata_json=json.dumps({"department": "Assembly", "cost_center": "CC-002", "period": "June 2025"})
                ),
                Order(
                    order_number="INV-QA-2025-01",
                    title="QA Testing Standards & Certification Equipment Fee",
                    category="department_invoice",
                    reference_code="qa",
                    amount_minor=1500000,  # ₱15,000.00
                    currency="PHP",
                    status="pending",
                    metadata_json=json.dumps({"department": "QA", "cost_center": "CC-003", "period": "June 2025"})
                ),
            ]
            db.add_all(seed_orders)
            db.commit()
        finance_models.seed_finance_db(db)
    finally:
        db.close()


from finance_models import (  # noqa: E402,F401
    FinanceUser,
    RoleAssignment,
    ApprovalMatrix,
    NumberSequence,
    HolidayCalendar,
    AuditEvent,
    FinancialPolicy,
    PolicyVersion,
    PolicyRule,
    PeriodCounter,
    ExceptionRequest,
    CashAdvance,
    ApprovalHistory,
    Disbursement,
    AdvanceLedgerEntry,
    DocumentReference,
    ExpenseReport,
    ExpenseLine,
    SettlementApplication,
    ClosingSnapshot,
    Vendor,
    VendorBankChange,
    TaxCode,
    PurchaseRequest,
    PurchaseRequestLine,
    AccountPayable,
    PaymentRecord,
    ExceptionRegister,
    seed_finance_db,
)

