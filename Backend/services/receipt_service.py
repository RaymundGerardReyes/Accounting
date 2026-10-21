"""
Backend/services/receipt_service.py
Authoritative Official Receipt (OR) and Acknowledgement Report generator,
Payment Method channel normalizer (QR Ph, Card VISA, GCash, Maya, Online Banking BPI),
and zero-dependency ISO-32000-1 PDF document builder for SMTP attachments and Portal copies.
"""

import os
import json
import hashlib
from datetime import datetime, timezone
from zoneinfo import ZoneInfo
from pathlib import Path
from typing import Dict, Any, Optional, List
from dotenv import load_dotenv

load_dotenv(Path(__file__).resolve().parent.parent.parent / ".env")

MANILA_TZ = ZoneInfo("Asia/Manila")
RECEIPTS_DIR = Path(__file__).resolve().parent.parent / "uploads" / "receipts"

# ==============================================================================
# 1. Payment Method Channel Catalog & Normalizer
# ==============================================================================

PAYMENT_METHOD_CATALOG: Dict[str, Dict[str, str]] = {
    "qrph": {
        "code": "qrph",
        "label": "QR Ph (Scan GQRPh Code to Pay)",
        "short_label": "QR Ph (GQRPh)",
        "category": "QR Ph",
        "rail": "InstaPay / PESONet National QR (QR Ph)",
    },
    "card": {
        "code": "card",
        "label": "Card (Debit / Credit)",
        "short_label": "Card",
        "category": "Card",
        "rail": "Card Payment Rail (Visa / Mastercard / JCB)",
    },
    "card_visa": {
        "code": "card_visa",
        "label": "Card (VISA)",
        "short_label": "Card (VISA)",
        "category": "Card",
        "rail": "VISA 3D Secure Card Rail",
    },
    "card_mastercard": {
        "code": "card_mastercard",
        "label": "Card (Mastercard)",
        "short_label": "Card (Mastercard)",
        "category": "Card",
        "rail": "Mastercard 3D Secure Card Rail",
    },
    "gcash": {
        "code": "gcash",
        "label": "E-Wallet (GCash)",
        "short_label": "GCash",
        "category": "E-Wallets",
        "rail": "GCash Instant Mobile Wallet",
    },
    "maya": {
        "code": "paymaya",
        "label": "E-Wallet (Maya)",
        "short_label": "Maya",
        "category": "E-Wallets",
        "rail": "Maya Instant Mobile Wallet",
    },
    "paymaya": {
        "code": "paymaya",
        "label": "E-Wallet (Maya)",
        "short_label": "Maya",
        "category": "E-Wallets",
        "rail": "Maya Instant Mobile Wallet",
    },
    "gcash_maya": {
        "code": "gcash_maya",
        "label": "E-Wallets (GCash / Maya)",
        "short_label": "GCash / Maya",
        "category": "E-Wallets",
        "rail": "GCash & Maya E-Wallet Rail",
    },
    "dob": {
        "code": "dob_bpi",
        "label": "Online Banking (BPI)",
        "short_label": "Online Banking (BPI)",
        "category": "Online Banking",
        "rail": "Direct Online Banking — BPI",
    },
    "dob_bpi": {
        "code": "dob_bpi",
        "label": "Online Banking (BPI)",
        "short_label": "Online Banking (BPI)",
        "category": "Online Banking",
        "rail": "Direct Online Banking — BPI",
    },
    "bpi": {
        "code": "dob_bpi",
        "label": "Online Banking (BPI)",
        "short_label": "Online Banking (BPI)",
        "category": "Online Banking",
        "rail": "Direct Online Banking — BPI",
    },
    "dob_ubp": {
        "code": "dob_ubp",
        "label": "Online Banking (UnionBank)",
        "short_label": "Online Banking (UnionBank)",
        "category": "Online Banking",
        "rail": "Direct Online Banking — UnionBank",
    },
    "hosted_checkout": {
        "code": "hosted_checkout",
        "label": "PayMongo Multi-Channel (QR Ph / Card / GCash / Maya / BPI)",
        "short_label": "Multi-Channel Checkout",
        "category": "All Channels",
        "rail": "PayMongo Hosted Checkout (QR Ph, Card, GCash, Maya, BPI)",
    },
}


def get_payment_method_info(raw_method: Optional[str]) -> Dict[str, str]:
    """
    Resolves any raw payment method string into structured channel metadata.
    Handles dynamic card suffixes like 'card_visa_4242' and neutral card fallbacks.
    """
    if not raw_method:
        return PAYMENT_METHOD_CATALOG["hosted_checkout"]

    cleaned = raw_method.strip().lower()
    if cleaned in PAYMENT_METHOD_CATALOG:
        return PAYMENT_METHOD_CATALOG[cleaned]

    if cleaned.startswith("card"):
        parts = cleaned.split("_")
        brand = "Card"
        last4 = None
        if len(parts) == 2:
            if parts[1].isdigit():
                last4 = parts[1]
                brand = "Card"
            elif parts[1].lower() in ("visa", "mastercard", "jcb", "amex"):
                brand = parts[1].upper()
            else:
                brand = parts[1].title()
        elif len(parts) >= 3:
            brand = parts[1].upper()
            if parts[2].isdigit():
                last4 = parts[2]

        if last4:
            label = f"Card ({brand} •••• {last4})" if brand != "Card" else f"Card (•••• {last4})"
            short_label = f"{brand} •••• {last4}"
        elif brand != "Card":
            label = f"Card ({brand})"
            short_label = brand
        else:
            label = "Card (Debit / Credit)"
            short_label = "Card"

        return {
            "code": cleaned,
            "label": label,
            "short_label": short_label,
            "category": "Card",
            "rail": f"{brand if brand != 'Card' else 'Debit / Credit'} 3D Secure Card Rail",
        }

    if "qr" in cleaned:
        return PAYMENT_METHOD_CATALOG["qrph"]
    if "gcash" in cleaned and "maya" in cleaned:
        return PAYMENT_METHOD_CATALOG["gcash_maya"]
    if "gcash" in cleaned:
        return PAYMENT_METHOD_CATALOG["gcash"]
    if "maya" in cleaned:
        return PAYMENT_METHOD_CATALOG["paymaya"]
    if "bpi" in cleaned or "dob" in cleaned or "bank" in cleaned:
        if "ubp" in cleaned or "union" in cleaned:
            return PAYMENT_METHOD_CATALOG["dob_ubp"]
        return PAYMENT_METHOD_CATALOG["dob_bpi"]

    return {
        "code": cleaned,
        "label": raw_method.strip(),
        "short_label": raw_method.strip(),
        "category": "PayMongo Checkout",
        "rail": "PayMongo Hosted Checkout Rail",
    }


def extract_payment_method_from_paymongo(
    resource_attrs: Dict[str, Any],
    fallback: Optional[str] = None,
) -> str:
    """
    Inspects a PayMongo checkout_session or payment attributes payload to extract
    the actual payment method used by the customer (e.g., qrph, card_visa, gcash,
    paymaya, dob_bpi).
    """
    if not isinstance(resource_attrs, dict):
        return fallback or "hosted_checkout"

    # 1. Inspect nested payments array on checkout_session
    payments = resource_attrs.get("payments")
    if isinstance(payments, list) and len(payments) > 0:
        paid_entry = next(
            (
                p for p in payments
                if isinstance(p, dict)
                and (p.get("attributes", {}).get("status") == "paid" or p.get("status") == "paid")
            ),
            payments[-1] if isinstance(payments[-1], dict) else None,
        )
        if isinstance(paid_entry, dict):
            p_attrs = paid_entry.get("attributes", paid_entry)
            source = p_attrs.get("source") if isinstance(p_attrs, dict) else None
            if isinstance(source, dict):
                resolved = _resolve_source_dict(source)
                if resolved:
                    return resolved

    # 2. Inspect direct source attribute on payment resource
    direct_source = resource_attrs.get("source")
    if isinstance(direct_source, dict):
        resolved = _resolve_source_dict(direct_source)
        if resolved:
            return resolved

    # 3. Inspect payment_method_used on checkout_session
    pm_used = resource_attrs.get("payment_method_used")
    if isinstance(pm_used, str) and pm_used.strip():
        pm_clean = pm_used.strip().lower()
        if pm_clean == "card":
            return "card"
        if pm_clean == "dob":
            return "dob_bpi"
        if pm_clean == "maya":
            return "paymaya"
        return pm_clean

    # 4. Inspect metadata.payment_method if present
    meta = resource_attrs.get("metadata")
    if isinstance(meta, dict):
        meta_pm = meta.get("payment_method")
        if isinstance(meta_pm, str) and meta_pm.strip() and meta_pm.strip().lower() != "hosted_checkout":
            return meta_pm.strip().lower()

    return fallback or "hosted_checkout"


def _resolve_source_dict(source: Dict[str, Any]) -> Optional[str]:
    stype = str(source.get("type") or "").strip().lower()
    if not stype:
        return None
    if stype == "card":
        brand = str(source.get("brand") or "").strip().lower()
        last4 = str(source.get("last4") or "").strip()
        if brand and last4 and last4.isdigit():
            return f"card_{brand}_{last4}"
        if brand:
            return f"card_{brand}"
        if last4 and last4.isdigit():
            return f"card_{last4}"
        return "card"
    if stype in ("dob", "online_banking", "brankas"):
        bank = str(source.get("provider") or source.get("bank_code") or source.get("bank") or "bpi").strip().lower()
        if "ubp" in bank or "union" in bank:
            return "dob_ubp"
        return "dob_bpi"
    if stype == "maya":
        return "paymaya"
    return stype


def format_philippine_phone(raw_phone: Optional[str]) -> Optional[str]:
    """
    Normalizes and formats Philippine mobile numbers into standard '+63 9XX XXX XXXX' format.
    """
    if not raw_phone:
        return None
    raw_str = str(raw_phone).strip()
    digits = "".join(c for c in raw_str if c.isdigit())

    if digits.startswith("63") and len(digits) == 12:
        return f"+63 {digits[2:5]} {digits[5:8]} {digits[8:12]}"
    elif digits.startswith("09") and len(digits) == 11:
        return f"+63 {digits[1:4]} {digits[4:7]} {digits[7:11]}"
    elif digits.startswith("9") and len(digits) == 10:
        return f"+63 {digits[0:3]} {digits[3:6]} {digits[6:10]}"

    return raw_str


def extract_customer_info_from_paymongo(
    resource_attrs: Dict[str, Any],
    fallback_meta: Optional[Dict[str, Any]] = None,
) -> Dict[str, Optional[str]]:
    """
    Extracts customer billing information (name, email, phone) from PayMongo checkout_session,
    payment, or webhook attributes, falling back to existing order metadata.
    """
    meta = fallback_meta or {}
    cust_name = None
    cust_email = None
    cust_phone = None

    if isinstance(resource_attrs, dict):
        billing = resource_attrs.get("billing")
        if isinstance(billing, dict):
            cust_name = billing.get("name")
            cust_email = billing.get("email")
            cust_phone = billing.get("phone")

        payments = resource_attrs.get("payments")
        if isinstance(payments, list) and len(payments) > 0:
            for p in payments:
                if isinstance(p, dict):
                    p_attrs = p.get("attributes", p)
                    if isinstance(p_attrs, dict):
                        p_billing = p_attrs.get("billing")
                        if isinstance(p_billing, dict):
                            cust_name = cust_name or p_billing.get("name")
                            cust_email = cust_email or p_billing.get("email")
                            cust_phone = cust_phone or p_billing.get("phone")

        customer = resource_attrs.get("customer")
        if isinstance(customer, dict):
            first = str(customer.get("first_name", "")).strip()
            last = str(customer.get("last_name", "")).strip()
            full = f"{first} {last}".strip()
            cust_name = cust_name or (full if full else None)
            cust_email = cust_email or customer.get("email")
            cust_phone = cust_phone or customer.get("phone")

        res_meta = resource_attrs.get("metadata")
        if isinstance(res_meta, dict):
            cust_name = cust_name or res_meta.get("customer_name") or res_meta.get("name")
            cust_email = cust_email or res_meta.get("customer_email") or res_meta.get("email")
            cust_phone = cust_phone or res_meta.get("customer_phone") or res_meta.get("phone")

    final_name = (
        (cust_name and cust_name.strip())
        or meta.get("customer_name")
        or meta.get("name")
        or "Valued Customer"
    )
    final_email = (
        (cust_email and cust_email.strip())
        or meta.get("customer_email")
        or meta.get("email")
        or meta.get("billing_email")
    )
    final_phone = format_philippine_phone(
        (cust_phone and str(cust_phone).strip())
        or meta.get("customer_phone")
        or meta.get("customer_mobile")
        or meta.get("phone")
    )

    return {
        "customer_name": final_name,
        "customer_email": final_email,
        "customer_phone": final_phone,
    }


# ==============================================================================
# 2. Official Receipt & Acknowledgement Report Builder
# ==============================================================================

def resolve_recipient_email(order_meta: Optional[Dict[str, Any]] = None, default_smtp_user: Optional[str] = None) -> Optional[str]:
    """
    Resolves the target recipient email for dispatching the Official Receipt & Acknowledgement PDF.
    """
    if not isinstance(order_meta, dict):
        order_meta = {}
    candidates = [
        order_meta.get("customer_email"),
        order_meta.get("email"),
        order_meta.get("billing_email"),
        os.getenv("DEFAULT_RECEIPT_EMAIL", "").strip(),
    ]
    smtp_user = (default_smtp_user or os.getenv("SMTP_USER", "")).strip()

    for c in candidates:
        if isinstance(c, str) and "@" in c.strip():
            addr = c.strip()
            domain = addr.split("@")[-1].lower()
            if domain in ("acme.com", "example.com") and smtp_user and "@" in smtp_user:
                smtp_domain = smtp_user.split("@")[-1].lower()
                if smtp_domain not in ("acme.com", "example.com"):
                    return smtp_user
            return addr

    if smtp_user and "@" in smtp_user:
        return smtp_user
    return None


def build_receipt_and_acknowledgement(
    order: Any,
    attempt: Any,
    outbox_status: str = "completed",
    smtp_delivery: Optional[Dict[str, Any]] = None,
) -> Dict[str, Any]:
    """
    Constructs the authoritative Official Receipt (OR) and Acknowledgement Report (ACK)
    data dictionary for a paid order / succeeded payment attempt.
    """
    meta = order.metadata_dict if hasattr(order, "metadata_dict") else {}
    if not isinstance(meta, dict):
        meta = {}
    existing_receipts = meta.get("receipt_records", {}) if isinstance(meta, dict) else {}
    att_id = getattr(attempt, "id", None) or "att_direct"
    existing = existing_receipts.get(att_id, {}) if isinstance(existing_receipts, dict) else {}

    paid_dt: datetime = (
        getattr(attempt, "paid_at", None)
        or getattr(order, "updated_at", None)
        or datetime.now(timezone.utc)
    )
    if paid_dt.tzinfo is None:
        paid_dt = paid_dt.replace(tzinfo=timezone.utc)
    manila_dt = paid_dt.astimezone(MANILA_TZ)
    utc_dt = paid_dt.astimezone(timezone.utc)

    order_num_str = str(getattr(order, "order_number", "ORD-0000"))
    order_parts = order_num_str.split("-")
    seq_part = order_parts[-1] if order_parts else "0001"
    att_suffix = str(att_id).replace("-", "")[:4].upper()
    ym_str = manila_dt.strftime("%Y%m")

    or_number = existing.get("official_receipt_number") or f"OR-{ym_str}-{seq_part}-{att_suffix}"
    ack_number = existing.get("acknowledgement_number") or f"ACK-{ym_str}-{seq_part}-{att_suffix}"

    pm_method_val = getattr(attempt, "payment_method", None) or "hosted_checkout"
    pm_info = get_payment_method_info(pm_method_val)
    pm_code = pm_info["code"]

    amount_minor = int(getattr(attempt, "amount_minor", None) or getattr(order, "amount_minor", 0) or 0)
    amount_php_val = amount_minor / 100.0

    tax_class = str(
        meta.get("tax_classification")
        or meta.get("tax_type")
        or ("vatable" if getattr(order, "category", "") == "product_order" else "vatable")
    ).strip().lower()

    non_vat_minor = 0
    if tax_class in ("exempt", "vat_exempt"):
        tax_classification = "vat_exempt"
        tax_classification_label = "VAT-Exempt Sales"
        tax_rate_label = "0% (Exempt)"
        vatable_minor = 0
        vat_minor = 0
        vat_exempt_minor = amount_minor
        zero_rated_minor = 0
    elif tax_class in ("zero", "zero_rated"):
        tax_classification = "zero_rated"
        tax_classification_label = "Zero-Rated Sales (0% VAT)"
        tax_rate_label = "0% (Zero-Rated)"
        vatable_minor = 0
        vat_minor = 0
        vat_exempt_minor = 0
        zero_rated_minor = amount_minor
    elif tax_class in ("non_vat", "nonvat"):
        tax_classification = "non_vat"
        tax_classification_label = "Non-VAT Sales"
        tax_rate_label = "Non-VAT"
        vatable_minor = 0
        vat_minor = 0
        vat_exempt_minor = 0
        zero_rated_minor = 0
        non_vat_minor = amount_minor
    else:
        tax_classification = "vatable"
        tax_classification_label = "VATable Sales (12% Inclusive)"
        tax_rate_label = "12%"
        vatable_minor = int(round(amount_minor / 1.12))
        vat_minor = amount_minor - vatable_minor
        vat_exempt_minor = 0
        zero_rated_minor = 0

    quantity = int(meta.get("quantity", 1) or 1)
    unit_price_val = amount_php_val / max(1, quantity)

    hash_input = f"{or_number}|{ack_number}|{order_num_str}|{att_id}|{amount_minor}|{utc_dt.isoformat()}"
    verification_hash = hashlib.sha256(hash_input.encode("utf-8")).hexdigest()

    card_badge = "Card"
    if "mastercard" in pm_code:
        card_badge = "Mastercard"
    elif "visa" in pm_code:
        card_badge = "VISA"
    elif "jcb" in pm_code:
        card_badge = "JCB"

    card_ch_code = "card_visa" if "visa" in pm_code else ("card_mastercard" if "mastercard" in pm_code else "card")
    is_channel_confirmed = pm_code != "hosted_checkout"

    supported_channels = [
        {
            "code": "qrph",
            "name": "Scan GQRPh code to pay",
            "badge": "QR Ph",
            "selected": pm_code == "qrph",
            "settled": pm_code == "qrph",
        },
        {
            "code": card_ch_code,
            "name": "Debit / Credit Card (Visa / Mastercard)",
            "badge": card_badge,
            "selected": pm_code.startswith("card"),
            "settled": pm_code.startswith("card"),
        },
        {
            "code": "gcash",
            "name": "E-Wallet (GCash)",
            "badge": "GCash",
            "selected": pm_code in ("gcash", "gcash_maya"),
            "settled": pm_code in ("gcash", "gcash_maya"),
        },
        {
            "code": "paymaya",
            "name": "E-Wallet (Maya)",
            "badge": "Maya",
            "selected": pm_code in ("paymaya", "maya", "gcash_maya"),
            "settled": pm_code in ("paymaya", "maya", "gcash_maya"),
        },
        {
            "code": "dob_bpi",
            "name": "Online Banking (BPI)",
            "badge": "BPI",
            "selected": pm_code.startswith("dob") or pm_code == "bpi",
            "settled": pm_code.startswith("dob") or pm_code == "bpi",
        },
    ]

    real_provider_payment_id = getattr(attempt, "provider_payment_id", None)
    provider_pm_id = real_provider_payment_id or f"att_{str(att_id)[:16]}"

    actual_outbox_status = outbox_status
    att_outbox = getattr(attempt, "fulfillment_records", None)
    if att_outbox and len(att_outbox) > 0:
        actual_outbox_status = att_outbox[0].status
    elif getattr(order, "fulfillment_records", None) and len(order.fulfillment_records) > 0:
        actual_outbox_status = order.fulfillment_records[0].status

    ledger_posted = bool(
        meta.get("ledger_posted")
        or meta.get("gl_posted")
        or meta.get("journal_entry_id")
        or meta.get("posted_to_gl")
    )
    if ledger_posted:
        ledger_status = "POSTED TO ACME GENERAL LEDGER"
        ack_status = "VERIFIED & POSTED TO ACME GENERAL LEDGER"
    elif getattr(order, "status", None) == "paid" or getattr(attempt, "status", None) == "succeeded":
        ledger_status = "PENDING LEDGER POSTING"
        ack_status = "VERIFIED & PENDING GENERAL LEDGER POSTING"
    else:
        ledger_status = "UNSETTLED"
        ack_status = "AWAITING SETTLEMENT"

    payment_status = (
        "PAYMENT CONFIRMED"
        if (getattr(order, "status", None) == "paid" or getattr(attempt, "status", None) == "succeeded")
        else "PAYMENT PENDING"
    )
    receipt_status = "RECEIPT GENERATED"

    raw_line_items = meta.get("line_items")
    if isinstance(raw_line_items, list) and len(raw_line_items) > 0:
        line_items = raw_line_items
    else:
        line_items = [
            {
                "reference_code": getattr(order, "reference_code", "PROD-001"),
                "title": getattr(order, "title", "Settlement Item"),
                "quantity": quantity,
                "unit_price_php": f"{unit_price_val:,.2f}",
                "amount_php": f"{amount_php_val:,.2f}",
                "tax_classification": tax_classification,
            }
        ]

    resolved_smtp = smtp_delivery or existing.get("smtp_delivery") or {
        "status": "pending",
        "recipient": resolve_recipient_email(meta),
        "sent_at": None,
        "message": "Ready for SMTP dispatch",
    }
    cust_info = extract_customer_info_from_paymongo(
        meta.get("latest_provider_attrs") or {},
        fallback_meta=meta,
    )
    customer_name = cust_info.get("customer_name") or "Valued Customer"
    customer_email = cust_info.get("customer_email") or meta.get("customer_email") or meta.get("email") or meta.get("billing_email")
    customer_phone = format_philippine_phone(cust_info.get("customer_phone") or meta.get("customer_phone") or meta.get("phone"))

    pdf_filename = f"Acme_OR_ACK_{order.order_number}.pdf"

    return {
        "official_receipt_number": or_number,
        "acknowledgement_number": ack_number,
        "document_title": "Official Receipt & Treasury Acknowledgement Report",
        "document_type": "Payment Acknowledgement Receipt",
        "issuer": {
            "company_name": "Acme Manufacturing Inc.",
            "department": "Corporate Accounting & Treasury Operations",
            "tin": "000-842-915-000 VAT",
            "address": "Acme Corporate Center, Makati City, Metro Manila, Philippines",
        },
        "order_id": order.id,
        "order_number": order.order_number,
        "order_title": order.title,
        "category": order.category,
        "category_label": "Product Line Order" if order.category == "product_order" else "Department Cost Center Settlement",
        "reference_code": order.reference_code,
        "quantity": quantity,
        "unit_price_php": f"{unit_price_val:,.2f}",
        "payment_attempt_id": attempt.id,
        "provider": "PayMongo Philippines",
        "provider_payment_id": provider_pm_id,
        "payment_method_code": pm_code,
        "payment_method_label": pm_info["label"],
        "payment_method_short": pm_info["short_label"],
        "payment_method_category": pm_info["category"],
        "payment_rail": pm_info["rail"],
        "payment_method_confirmed": is_channel_confirmed,
        "supported_channels": supported_channels,
        "amount_minor": amount_minor,
        "currency": attempt.currency or "PHP",
        "amount_php": f"{amount_php_val:,.2f}",
        "tax_classification": tax_classification,
        "tax_classification_label": tax_classification_label,
        "tax_rate_label": tax_rate_label,
        "vatable_sales_minor": vatable_minor,
        "vatable_sales_php": f"{vatable_minor / 100.0:,.2f}",
        "vat_amount_minor": vat_minor,
        "vat_amount_php": f"{vat_minor / 100.0:,.2f}",
        "vat_exempt_sales_minor": vat_exempt_minor,
        "vat_exempt_sales_php": f"{vat_exempt_minor / 100.0:,.2f}",
        "zero_rated_sales_minor": zero_rated_minor,
        "zero_rated_sales_php": f"{zero_rated_minor / 100.0:,.2f}",
        "non_vat_sales_minor": non_vat_minor,
        "non_vat_sales_php": f"{non_vat_minor / 100.0:,.2f}",
        "gateway_fee_php": "0.00",
        "paid_at_iso": utc_dt.isoformat(),
        "paid_at_manila": manila_dt.strftime("%b %d, %Y, %I:%M:%S %p PHT"),
        "paid_at_utc": utc_dt.strftime("%Y-%m-%d %H:%M:%S UTC"),
        "payment_status": payment_status,
        "receipt_status": receipt_status,
        "ledger_status": ledger_status,
        "acknowledgement_status": ack_status,
        "outbox_status": actual_outbox_status,
        "verification_hash": verification_hash,
        "sha256_audit_digest": verification_hash,
        "audit_digest_type": "SHA-256",
        "portal_copy_available": True,
        "pdf_filename": pdf_filename,
        "pdf_url": f"/api/v1/payments/{attempt.id}/receipt/pdf",
        "pdf_download_url": f"/api/v1/payments/{attempt.id}/receipt/pdf?download=1",
        "smtp_delivery": resolved_smtp,
        "customer": {
            "name": customer_name,
            "email": customer_email or "Not Provided",
            "phone": customer_phone or "Not Provided",
        },
        "customer_name": customer_name,
        "customer_email": customer_email,
        "customer_phone": customer_phone,
        "line_items": line_items,
        "tax_compliance_note": "Electronic Payment Settlement Record. Pursuant to BIR RMC No. 77-2024, official commercial VAT Invoices are issued by Corporate Accounting upon customer tax identification verification.",
    }


def persist_receipt_on_order(
    db: Any,
    order: Any,
    attempt: Any,
    receipt_data: Dict[str, Any],
) -> None:
    """
    Saves the generated Official Receipt & Acknowledgement Report metadata into order.metadata_json
    and writes the PDF copy to disk under Backend/uploads/receipts/.
    """
    meta = dict(order.metadata_dict or {})
    records = dict(meta.get("receipt_records") or {})
    records[attempt.id] = receipt_data
    meta["receipt_records"] = records
    meta["latest_receipt"] = receipt_data
    order.metadata_dict = meta
    db.add(order)
    db.commit()

    try:
        RECEIPTS_DIR.mkdir(parents=True, exist_ok=True)
        pdf_bytes = generate_receipt_pdf_bytes(receipt_data)
        pdf_path = RECEIPTS_DIR / receipt_data["pdf_filename"]
        with open(pdf_path, "wb") as f:
            f.write(pdf_bytes)
    except Exception as e:
        print(f"Notice: Could not write archival PDF to disk: {e}")


# ==============================================================================
# 3. Deterministic ISO-32000-1 Vector PDF Generator (%PDF-1.4)
#    Synchronized with page.tsx Light Card & Emerald (#12A579) Design System
# ==============================================================================

def _pdf_escape(text: str) -> str:
    """
    Sanitizes text for standard Type1 WinAnsiEncoding PDF strings.
    Replaces Unicode Peso sign (₱) with 'PHP ' and bullets with '-' to prevent
    WinAnsi encoding artifacts in PDF viewers.
    """
    if not text:
        return ""
    cleaned = (
        str(text)
        .replace("₱", "PHP ")
        .replace("•", "*")
        .replace("—", "-")
        .replace("–", "-")
        .replace("·", "-")
    )
    cleaned = cleaned.encode("latin-1", errors="replace").decode("latin-1")
    return cleaned.replace("\\", "\\\\").replace("(", "\\(").replace(")", "\\)")


def generate_receipt_pdf_bytes(receipt: Dict[str, Any]) -> bytes:
    """
    Generates a crisp, single-page corporate Official Receipt & Acknowledgement Report
    PDF document (612 x 792 pt US Letter) that mirrors the exact light Card UI and
    color palette of Frontend/app/payment/result/[paymentId]/page.tsx:
      - Card Surface: #FFFFFF (1.000, 1.000, 1.000)
      - Subtle Muted Surface: #FAFAFA (0.980, 0.980, 0.980) / #F4F4F5 (0.957, 0.957, 0.961)
      - Hairline Border: #E4E4E7 (0.894, 0.894, 0.906)
      - Primary Foreground Text: #18181B (0.094, 0.094, 0.106)
      - Muted Foreground Text: #71717A (0.443, 0.443, 0.478)
      - Emerald Accent: #12A579 (0.071, 0.647, 0.475) / Deep Emerald Text #0D7C5A (0.051, 0.486, 0.353)
      - Soft Emerald Pill Fill: #ECFDF5 (0.925, 0.992, 0.961)
    """
    cmds: List[str] = []

    def rect_fill(x: float, y: float, w: float, h: float, r: float, g: float, b: float):
        cmds.append(f"{r:.3f} {g:.3f} {b:.3f} rg {x:.1f} {y:.1f} {w:.1f} {h:.1f} re f")

    def rect_stroke(x: float, y: float, w: float, h: float, r: float, g: float, b: float, lw: float = 0.8):
        cmds.append(f"{lw:.2f} w {r:.3f} {g:.3f} {b:.3f} RG {x:.1f} {y:.1f} {w:.1f} {h:.1f} re S")

    def line(x1: float, y1: float, x2: float, y2: float, r: float = 0.894, g: float = 0.894, b: float = 0.906, lw: float = 0.6):
        cmds.append(f"{lw:.2f} w {r:.3f} {g:.3f} {b:.3f} RG {x1:.1f} {y1:.1f} m {x2:.1f} {y2:.1f} l S")

    def text(x: float, y: float, s: str, font: str = "F1", size: float = 9.5, r: float = 0.094, g: float = 0.094, b: float = 0.106):
        esc = _pdf_escape(s)
        cmds.append(f"BT /{font} {size:.1f} Tf {r:.3f} {g:.3f} {b:.3f} rg {x:.1f} {y:.1f} Td ({esc}) Tj ET")

    # 0. Outer Receipt Card Frame (#FFFFFF surface, #E4E4E7 border, top #12A579 emerald accent bar)
    rect_fill(28, 62, 556, 704, 1.000, 1.000, 1.000)
    rect_stroke(28, 62, 556, 704, 0.894, 0.894, 0.906, 0.9)
    rect_fill(28, 762, 556, 4, 0.071, 0.647, 0.475)

    # 1. Clean Light Editorial Header (Mirrors page.tsx CardHeader)
    text(44, 740, "ACME MANUFACTURING INC. - CORPORATE ACCOUNTING & TREASURY", "F2", 9.0, 0.443, 0.443, 0.478)
    text(44, 722, "OFFICIAL RECEIPT & PAYMENT ACKNOWLEDGEMENT REPORT", "F2", 13.5, 0.094, 0.094, 0.106)
    text(44, 708, "TIN: 000-842-915-000 VAT  |  Authoritative Portal & SMTP Copy  |  PayMongo Live Rail", "F1", 8.0, 0.443, 0.443, 0.478)

    # Soft Emerald Status Pill in Top Right (Matches bg-[#12A579]/10 text-[#0d7c5a] border-[#12A579]/30)
    rect_fill(436, 724, 132, 24, 0.925, 0.992, 0.961)
    rect_stroke(436, 724, 132, 24, 0.071, 0.647, 0.475, 0.8)
    text(448, 733, "PAID & CONFIRMED", "F2", 9.0, 0.051, 0.486, 0.353)

    line(28, 698, 584, 698, 0.894, 0.894, 0.906, 0.8)

    # 2. Document Control Bar (bg-muted/40 #F4F4F5 fill, #E4E4E7 border)
    rect_fill(40, 650, 532, 38, 0.957, 0.957, 0.961)
    rect_stroke(40, 650, 532, 38, 0.894, 0.894, 0.906, 0.7)

    text(52, 674, "OFFICIAL RECEIPT NO.", "F2", 7.2, 0.443, 0.443, 0.478)
    text(52, 659, str(receipt.get("official_receipt_number", "")), "F4", 10.5, 0.094, 0.094, 0.106)

    line(296, 655, 296, 683, 0.894, 0.894, 0.906, 0.7)

    text(310, 674, "ACKNOWLEDGEMENT REPORT NO.", "F2", 7.2, 0.443, 0.443, 0.478)
    text(310, 659, str(receipt.get("acknowledgement_number", "")), "F4", 10.5, 0.094, 0.094, 0.106)

    # 3. Section I: Official Receipt & Settlement Summary
    text(40, 632, "SECTION I - PAYMENT ACKNOWLEDGEMENT & SETTLEMENT SUMMARY", "F2", 9.0, 0.094, 0.094, 0.106)
    line(40, 626, 572, 626, 0.894, 0.894, 0.906, 0.8)

    # Prominent Total Amount Box on Right (#FAFAFA surface, #E4E4E7 border)
    rect_fill(386, 546, 186, 72, 0.980, 0.980, 0.980)
    rect_stroke(386, 546, 186, 72, 0.894, 0.894, 0.906, 0.8)
    text(400, 600, "TOTAL AMOUNT PAID (PHP)", "F2", 7.8, 0.443, 0.443, 0.478)
    text(400, 575, f"PHP {receipt.get('amount_php', '0.00')}", "F2", 18.0, 0.094, 0.094, 0.106)
    tax_note = f"{receipt.get('tax_classification_label', '12% VAT Inclusive')}  |  Fee: PHP 0.00"
    text(400, 555, tax_note[:36], "F1", 7.5, 0.443, 0.443, 0.478)

    # Left-side key-value rows including customer billing details
    cust_name = str(receipt.get("customer_name") or "Valued Customer")
    cust_email = str(receipt.get("customer_email") or "Not Provided")
    cust_phone = str(receipt.get("customer_phone") or "Not Provided")

    rows_left = [
        ("Order / Invoice Ref:", str(receipt.get("order_number", ""))),
        ("Order Title:", str(receipt.get("order_title", ""))[:38]),
        ("Customer / Payer:", f"{cust_name[:24]} ({cust_phone})"),
        ("Email Address:", f"{cust_email[:34]}"),
        ("Category / Code:", f"{receipt.get('category_label', '')} ({receipt.get('reference_code', '')})"),
        ("Settlement Date (PHT):", str(receipt.get("paid_at_manila", ""))),
    ]
    y_cursor = 612
    for lbl, val in rows_left:
        text(44, y_cursor, lbl, "F2", 8.0, 0.443, 0.443, 0.478)
        text(155, y_cursor, val, "F1", 8.2, 0.094, 0.094, 0.106)
        y_cursor -= 14

    # 4. Payment Method & PayMongo Channel Verification Matrix
    rect_fill(40, 444, 532, 92, 0.980, 0.980, 0.980)
    rect_stroke(40, 444, 532, 92, 0.894, 0.894, 0.906, 0.8)
    text(50, 520, "PAYMENT METHOD & PAYMONGO GATEWAY RAIL VERIFICATION", "F2", 8.2, 0.094, 0.094, 0.106)

    text(50, 502, "Settled Payment Method:", "F2", 8.2, 0.443, 0.443, 0.478)
    text(170, 502, str(receipt.get("payment_method_label", "")), "F2", 9.2, 0.051, 0.486, 0.353)

    text(50, 486, "Gateway Provider & Rail:", "F2", 8.2, 0.443, 0.443, 0.478)
    text(170, 486, f"{receipt.get('provider', 'PayMongo Philippines')} - {receipt.get('payment_rail', '')}", "F1", 8.2, 0.094, 0.094, 0.106)

    text(50, 470, "Session / Payment ID:", "F2", 8.2, 0.443, 0.443, 0.478)
    text(170, 470, f"{receipt.get('provider_payment_id', '')}  (Attempt: {receipt.get('payment_attempt_id', '')})", "F3", 7.8, 0.094, 0.094, 0.106)

    # Channel pills strip (Soft Emerald tint for selected rail, neutral for others)
    channels = receipt.get("supported_channels", [])
    ch_x = 50
    for ch in channels:
        is_sel = bool(ch.get("selected"))
        box_w = 96
        if is_sel:
            rect_fill(ch_x, 450, box_w, 14, 0.925, 0.992, 0.961)
            rect_stroke(ch_x, 450, box_w, 14, 0.071, 0.647, 0.475, 0.7)
            text(ch_x + 5, 454, f"[X] {ch.get('badge', '')}", "F2", 7.2, 0.051, 0.486, 0.353)
        else:
            rect_fill(ch_x, 450, box_w, 14, 1.000, 1.000, 1.000)
            rect_stroke(ch_x, 450, box_w, 14, 0.894, 0.894, 0.906, 0.5)
            text(ch_x + 5, 454, f"[ ] {ch.get('badge', '')}", "F1", 7.2, 0.443, 0.443, 0.478)
        ch_x += 103

    # 5. Itemized Accounting & Tax Breakdown Table (Mirrors page.tsx bg-muted/40 table header)
    text(40, 424, "SECTION II - ITEMIZED ORDER & TAX BREAKDOWN", "F2", 9.0, 0.094, 0.094, 0.106)
    line(40, 418, 572, 418, 0.894, 0.894, 0.906, 0.8)

    # Light Table Header (#F4F4F5 fill, #E4E4E7 border, #18181B bold text — NO harsh black bar!)
    rect_fill(40, 396, 532, 18, 0.957, 0.957, 0.961)
    rect_stroke(40, 396, 532, 18, 0.894, 0.894, 0.906, 0.7)
    text(48, 402, "REF / CODE", "F2", 7.5, 0.094, 0.094, 0.106)
    text(135, 402, "PARTICULARS / DESCRIPTION", "F2", 7.5, 0.094, 0.094, 0.106)
    text(365, 402, "QTY", "F2", 7.5, 0.094, 0.094, 0.106)
    text(410, 402, "UNIT COST", "F2", 7.5, 0.094, 0.094, 0.106)
    text(495, 402, "AMOUNT (PHP)", "F2", 7.5, 0.094, 0.094, 0.106)

    # Item Row (#FFFFFF surface, #E4E4E7 border)
    rect_fill(40, 374, 532, 22, 1.000, 1.000, 1.000)
    rect_stroke(40, 374, 532, 22, 0.894, 0.894, 0.906, 0.6)
    text(48, 382, str(receipt.get("reference_code", ""))[:16], "F3", 8.0, 0.443, 0.443, 0.478)
    text(135, 382, str(receipt.get("order_title", ""))[:40], "F1", 8.5, 0.094, 0.094, 0.106)
    text(368, 382, str(receipt.get("quantity", 1)), "F3", 8.5, 0.094, 0.094, 0.106)
    text(410, 382, f"PHP {receipt.get('unit_price_php', '0.00')}", "F3", 8.0, 0.443, 0.443, 0.478)
    text(495, 382, f"PHP {receipt.get('amount_php', '0.00')}", "F4", 8.5, 0.094, 0.094, 0.106)

    # Breakdown Summary Rows adapted to tax classification
    tax_c = receipt.get("tax_classification", "vatable")
    if tax_c == "vat_exempt":
        breakdown_rows = [
            ("VAT-Exempt Sales (Net of VAT):", f"PHP {receipt.get('vat_exempt_sales_php', receipt.get('amount_php', '0.00'))}", "F1"),
            ("Value-Added Tax (0% Exempt):", "PHP 0.00", "F1"),
            ("PayMongo Gateway Processing Fee:", f"PHP {receipt.get('gateway_fee_php', '0.00')} (Covered)", "F1"),
            ("TOTAL AMOUNT SETTLED & RECEIPTED:", f"PHP {receipt.get('amount_php', '0.00')}", "F2"),
        ]
    elif tax_c == "zero_rated":
        breakdown_rows = [
            ("Zero-Rated Sales (0% VAT):", f"PHP {receipt.get('zero_rated_sales_php', receipt.get('amount_php', '0.00'))}", "F1"),
            ("Value-Added Tax (0%):", "PHP 0.00", "F1"),
            ("PayMongo Gateway Processing Fee:", f"PHP {receipt.get('gateway_fee_php', '0.00')} (Covered)", "F1"),
            ("TOTAL AMOUNT SETTLED & RECEIPTED:", f"PHP {receipt.get('amount_php', '0.00')}", "F2"),
        ]
    elif tax_c == "non_vat":
        breakdown_rows = [
            ("Non-VAT Sales:", f"PHP {receipt.get('non_vat_sales_php', receipt.get('amount_php', '0.00'))}", "F1"),
            ("Value-Added Tax (Non-VAT):", "PHP 0.00", "F1"),
            ("PayMongo Gateway Processing Fee:", f"PHP {receipt.get('gateway_fee_php', '0.00')} (Covered)", "F1"),
            ("TOTAL AMOUNT SETTLED & RECEIPTED:", f"PHP {receipt.get('amount_php', '0.00')}", "F2"),
        ]
    else:
        breakdown_rows = [
            ("Vatable Sales (Net of 12% VAT):", f"PHP {receipt.get('vatable_sales_php', '0.00')}", "F1"),
            ("12% Value-Added Tax (VAT):", f"PHP {receipt.get('vat_amount_php', '0.00')}", "F1"),
            ("PayMongo Gateway Processing Fee:", f"PHP {receipt.get('gateway_fee_php', '0.00')} (Covered)", "F1"),
            ("TOTAL AMOUNT SETTLED & RECEIPTED:", f"PHP {receipt.get('amount_php', '0.00')}", "F2"),
        ]

    by = 356
    for b_lbl, b_val, b_font in breakdown_rows:
        is_total = b_font == "F2"
        lbl_color = (0.094, 0.094, 0.106) if is_total else (0.443, 0.443, 0.478)
        val_color = (0.051, 0.486, 0.353) if is_total else (0.094, 0.094, 0.106)
        text(306, by, b_lbl, b_font, 8.5, *lbl_color)
        text(485, by, b_val, "F4" if is_total else "F3", 8.5, *val_color)
        line(306, by - 4, 572, by - 4, 0.894, 0.894, 0.906, 0.5)
        by -= 16

    # 6. Section III: Treasury & Portal Acknowledgement Report
    text(40, 274, "SECTION III - TREASURY ACKNOWLEDGEMENT REPORT & AUDIT TRAIL", "F2", 9.0, 0.094, 0.094, 0.106)
    line(40, 268, 572, 268, 0.894, 0.894, 0.906, 0.8)

    rect_fill(40, 148, 532, 112, 0.980, 0.980, 0.980)
    rect_stroke(40, 148, 532, 112, 0.894, 0.894, 0.906, 0.8)

    smtp_info = receipt.get("smtp_delivery") or {}
    smtp_recipient = smtp_info.get("recipient") or os.getenv("SMTP_USER") or "Configured Portal Account"

    cust_display = f"{cust_name[:24]} ({cust_phone})" if cust_phone != "Not Provided" else cust_name[:28]
    ack_lines = [
        ("Acknowledgement Report ID:", f"{receipt.get('acknowledgement_number', '')}  |  Ledger: {receipt.get('ledger_status', 'PENDING POSTING')}"),
        ("Customer & Payer Record:", f"{cust_display}  |  {cust_email[:32]}"),
        ("Outbox Delivery & Audit:", f"Outbox Status: {str(receipt.get('outbox_status', 'completed')).upper()}  |  Rail: PayMongo Verified"),
        ("Portal Copy Synchronization:", f"Available in Acme Portal (/payment/result/{receipt.get('payment_attempt_id', '')})"),
        ("SMTP Email Dispatch Target:", f"{smtp_recipient}  (Attachment: {receipt.get('pdf_filename', '')})"),
        ("SHA-256 Audit Digest:", str(receipt.get("verification_hash", ""))),
    ]
    ay = 244
    for a_lbl, a_val in ack_lines:
        text(50, ay, a_lbl, "F2", 8.0, 0.443, 0.443, 0.478)
        text(185, ay, a_val, "F3" if "SHA-256" in a_lbl or "ID" in a_lbl else "F1", 8.0, 0.094, 0.094, 0.106)
        ay -= 15

    # 7. Official Certification & Card Footer
    rect_fill(40, 74, 532, 62, 0.957, 0.957, 0.961)
    rect_stroke(40, 74, 532, 62, 0.894, 0.894, 0.906, 0.6)
    text(50, 120, "CERTIFICATION OF PAYMENT ACKNOWLEDGEMENT & AUDIT TRAIL", "F2", 8.0, 0.094, 0.094, 0.106)
    text(
        50,
        106,
        "This document confirms electronic payment settlement via PayMongo Philippines for internal treasury records.",
        "F1",
        7.6,
        0.443,
        0.443,
        0.478,
    )
    text(
        50,
        94,
        "Official commercial VAT Invoices are issued separately pursuant to BIR RMC No. 77-2024 upon tax profile verification.",
        "F1",
        7.6,
        0.443,
        0.443,
        0.478,
    )
    text(
        50,
        82,
        "Both the SMTP PDF attachment and Portal Copy share identical cryptographic SHA-256 audit digests for internal control.",
        "F1",
        7.6,
        0.443,
        0.443,
        0.478,
    )

    text(415, 110, "Digitally Signed & Verified", "F2", 8.0, 0.051, 0.486, 0.353)
    line(410, 98, 558, 98, 0.894, 0.894, 0.906, 0.6)
    text(415, 86, "Acme Automated Treasury Engine", "F1", 7.5, 0.443, 0.443, 0.478)

    text(
        130,
        44,
        "Acme Manufacturing Inc. - Unified Cost Accounting & Live PayMongo Orchestration Platform",
        "F1",
        7.5,
        0.443,
        0.443,
        0.478,
    )

    content_stream = "\n".join(cmds).encode("latin-1")

    # Assemble ISO-32000-1 PDF 1.4 objects with exact byte offsets
    objects: List[bytes] = []
    objects.append(b"1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
    objects.append(b"2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n")
    objects.append(
        b"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] "
        b"/Resources << /Font << /F1 4 0 R /F2 5 0 R /F3 6 0 R /F4 7 0 R >> >> "
        b"/Contents 8 0 R >>\nendobj\n"
    )
    objects.append(b"4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n")
    objects.append(b"5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n")
    objects.append(b"6 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>\nendobj\n")
    objects.append(b"7 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Courier-Bold /Encoding /WinAnsiEncoding >>\nendobj\n")
    objects.append(
        f"8 0 obj\n<< /Length {len(content_stream)} >>\nstream\n".encode("latin-1")
        + content_stream
        + b"\nendstream\nendobj\n"
    )

    pdf_header = b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n"
    body_parts: List[bytes] = [pdf_header]
    offsets: List[int] = []
    current_offset = len(pdf_header)

    for obj_bytes in objects:
        offsets.append(current_offset)
        body_parts.append(obj_bytes)
        current_offset += len(obj_bytes)

    xref_offset = current_offset
    xref_lines = [f"xref\n0 {len(objects) + 1}\n", "0000000000 65535 f \n"]
    for off in offsets:
        xref_lines.append(f"{off:010d} 00000 n \n")

    trailer = (
        "".join(xref_lines)
        + f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\nstartxref\n{xref_offset}\n%%EOF\n"
    ).encode("latin-1")

    body_parts.append(trailer)
    return b"".join(body_parts)
