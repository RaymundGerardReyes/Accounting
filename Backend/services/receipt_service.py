"""
Backend/services/receipt_service.py
Authoritative Official Receipt (OR) and Payment Acknowledgement Report service.

Refactored into 10 logical components while preserving backward-compatible
public interfaces:
  1. PaymentMethodResolver
  2. PaymentVerificationService
  3. ReceiptNumberingAllocator
  4. TaxCalculationEngine
  5. CanonicalReceiptAssembler
  6. ReceiptRepository
  7. VectorPdfDocumentEngine (Multi-page ISO-32000-1 %PDF-1.4)
  8. ArtifactLifecycleManager
  9. DeliveryResolutionService
 10. AuditDigestEngine
"""

from __future__ import annotations

import hashlib
import json
import logging
import os
import re
import tempfile
from datetime import datetime, timezone
from decimal import Decimal, InvalidOperation, ROUND_HALF_UP
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple
from zoneinfo import ZoneInfo

try:
    from dotenv import load_dotenv
    load_dotenv(Path(__file__).resolve().parent.parent.parent / ".env")
except ImportError:
    pass

logger = logging.getLogger(__name__)

MANILA_TZ = ZoneInfo("Asia/Manila")
RECEIPTS_DIR = Path(__file__).resolve().parent.parent / "uploads" / "receipts"
EMAIL_REGEX = re.compile(r"^[^@\s]+@[^@\s]+\.[^@\s]+$")


# ==============================================================================
# COMPONENT 1: Payment Method Catalog & Resolver
# ==============================================================================

PAYMENT_METHOD_CATALOG: Dict[str, Dict[str, Any]] = {
    "qrph": {
        "code": "qrph",
        "label": "QR Ph",
        "short_label": "QR Ph",
        "category": "QR Ph",
        "rail": "InstaPay / PESONet National QR (QR Ph)",
        "confirmed": True,
    },
    "card": {
        "code": "card",
        "label": "Card (Debit / Credit)",
        "short_label": "Card",
        "category": "Card",
        "rail": "Card Payment Rail",
        "confirmed": True,
    },
    "card_visa": {
        "code": "card_visa",
        "label": "Card (VISA)",
        "short_label": "VISA",
        "category": "Card",
        "rail": "VISA Card Payment Rail",
        "confirmed": True,
    },
    "card_mastercard": {
        "code": "card_mastercard",
        "label": "Card (Mastercard)",
        "short_label": "Mastercard",
        "category": "Card",
        "rail": "Mastercard Card Payment Rail",
        "confirmed": True,
    },
    "card_jcb": {
        "code": "card_jcb",
        "label": "Card (JCB)",
        "short_label": "JCB",
        "category": "Card",
        "rail": "JCB Card Payment Rail",
        "confirmed": True,
    },
    "card_amex": {
        "code": "card_amex",
        "label": "Card (American Express)",
        "short_label": "Amex",
        "category": "Card",
        "rail": "American Express Card Payment Rail",
        "confirmed": True,
    },
    "gcash": {
        "code": "gcash",
        "label": "GCash",
        "short_label": "GCash",
        "category": "E-Wallets",
        "rail": "GCash Mobile Wallet Rail",
        "confirmed": True,
    },
    "maya": {
        "code": "paymaya",
        "label": "Maya",
        "short_label": "Maya",
        "category": "E-Wallets",
        "rail": "Maya Mobile Wallet Rail",
        "confirmed": True,
    },
    "paymaya": {
        "code": "paymaya",
        "label": "Maya",
        "short_label": "Maya",
        "category": "E-Wallets",
        "rail": "Maya Mobile Wallet Rail",
        "confirmed": True,
    },
    "gcash_maya": {
        "code": "gcash_maya",
        "label": "E-Wallet Checkout (GCash / Maya Capability)",
        "short_label": "E-Wallet (Unconfirmed)",
        "category": "E-Wallets",
        "rail": "PayMongo E-Wallet Checkout",
        "confirmed": False,  # Combined capability is not a single confirmed channel
    },
    "dob": {
        "code": "dob",
        "label": "Direct Online Banking",
        "short_label": "Online Banking",
        "category": "Online Banking",
        "rail": "Direct Online Banking Rail",
        "confirmed": True,
    },
    "dob_bpi": {
        "code": "dob_bpi",
        "label": "Online Banking (BPI)",
        "short_label": "BPI",
        "category": "Online Banking",
        "rail": "Direct Online Banking - BPI",
        "confirmed": True,
    },
    "bpi": {
        "code": "dob_bpi",
        "label": "Online Banking (BPI)",
        "short_label": "BPI",
        "category": "Online Banking",
        "rail": "Direct Online Banking - BPI",
        "confirmed": True,
    },
    "dob_ubp": {
        "code": "dob_ubp",
        "label": "Online Banking (UnionBank)",
        "short_label": "UnionBank",
        "category": "Online Banking",
        "rail": "Direct Online Banking - UnionBank",
        "confirmed": True,
    },
    "ubp": {
        "code": "dob_ubp",
        "label": "Online Banking (UnionBank)",
        "short_label": "UnionBank",
        "category": "Online Banking",
        "rail": "Direct Online Banking - UnionBank",
        "confirmed": True,
    },
    "unionbank": {
        "code": "dob_ubp",
        "label": "Online Banking (UnionBank)",
        "short_label": "UnionBank",
        "category": "Online Banking",
        "rail": "Direct Online Banking - UnionBank",
        "confirmed": True,
    },
    "hosted_checkout": {
        "code": "hosted_checkout",
        "label": "Unconfirmed Channel (Hosted Checkout)",
        "short_label": "Unconfirmed",
        "category": "Hosted Checkout",
        "rail": "PayMongo Hosted Checkout",
        "confirmed": False,
    },
    "unknown": {
        "code": "unknown",
        "label": "Unavailable / Unconfirmed",
        "short_label": "Unconfirmed",
        "category": "Unconfirmed",
        "rail": "Unconfirmed Payment Rail",
        "confirmed": False,
    },
}

KNOWN_CARD_BRANDS = {
    "visa": ("card_visa", "VISA"),
    "mastercard": ("card_mastercard", "MASTERCARD"),
    "mc": ("card_mastercard", "MASTERCARD"),
    "jcb": ("card_jcb", "JCB"),
    "amex": ("card_amex", "American Express"),
    "american_express": ("card_amex", "American Express"),
}


def get_payment_method_info(raw_method: Optional[str]) -> Dict[str, Any]:
    """
    Resolves a raw payment method string into structured channel metadata.
    Never marks an unknown or combined capability method as confirmed, and
    never guesses BPI for unrecognized online banks.
    """
    if not raw_method or not isinstance(raw_method, str) or not raw_method.strip():
        return dict(PAYMENT_METHOD_CATALOG["unknown"])

    cleaned = raw_method.strip().lower()
    if cleaned in PAYMENT_METHOD_CATALOG:
        return dict(PAYMENT_METHOD_CATALOG[cleaned])

    # Handle structured card strings: card_visa_4242, card_4242, card_mastercard, etc.
    if cleaned == "card" or cleaned.startswith("card_"):
        parts = [p for p in cleaned.split("_") if p]
        brand_key: Optional[str] = None
        last4: Optional[str] = None

        for token in parts[1:]:
            if len(token) == 4 and token.isdigit() and last4 is None:
                last4 = token
            elif token in KNOWN_CARD_BRANDS and brand_key is None:
                brand_key = token
            elif not token.isdigit() and brand_key is None:
                # Unrecognized brand token: do not invent a known brand
                brand_key = "generic"

        brand_label = KNOWN_CARD_BRANDS[brand_key][1] if brand_key in KNOWN_CARD_BRANDS else "Card"
        if last4:
            if brand_label != "Card":
                label = f"Card ({brand_label} •••• {last4})"
                short_label = f"{brand_label} •••• {last4}"
            else:
                label = f"Card (•••• {last4})"
                short_label = f"Card •••• {last4}"
        else:
            if brand_label != "Card":
                label = f"Card ({brand_label})"
                short_label = brand_label
            else:
                label = "Card (Debit / Credit)"
                short_label = "Card"

        return {
            "code": cleaned,
            "label": label,
            "short_label": short_label,
            "category": "Card",
            "rail": f"{brand_label} Card Payment Rail" if brand_label != "Card" else "Card Payment Rail",
            "confirmed": True,
            "last4": last4,
            "card_brand": brand_label,
        }

    # Handle online banking variants safely without defaulting unknown banks to BPI
    if cleaned.startswith("dob_") or cleaned in ("online_banking", "bank_transfer"):
        if cleaned in ("dob_bpi", "bpi"):
            return dict(PAYMENT_METHOD_CATALOG["dob_bpi"])
        if cleaned in ("dob_ubp", "dob_unionbank"):
            return dict(PAYMENT_METHOD_CATALOG["dob_ubp"])
        return {
            "code": "dob",
            "label": "Direct Online Banking",
            "short_label": "Online Banking",
            "category": "Online Banking",
            "rail": "Direct Online Banking Rail",
            "confirmed": True,
        }

    if cleaned in ("qr_ph", "gqrph"):
        return dict(PAYMENT_METHOD_CATALOG["qrph"])

    # Unrecognized method string: explicitly mark as unconfirmed
    return {
        "code": "unknown",
        "label": "Unavailable / Unconfirmed",
        "short_label": "Unconfirmed",
        "category": "Unconfirmed",
        "rail": "Unconfirmed Payment Rail",
        "confirmed": False,
        "raw_reported_method": raw_method.strip()[:64],
    }


def _resolve_source_dict(source: Dict[str, Any]) -> Optional[str]:
    """
    Extracts a canonical payment method code from a PayMongo payment source object.
    """
    if not isinstance(source, dict):
        return None
    stype = str(source.get("type") or "").strip().lower()
    if not stype:
        return None

    if stype == "card":
        raw_brand = str(source.get("brand") or "").strip().lower()
        raw_last4 = str(source.get("last4") or "").strip()
        brand = raw_brand if raw_brand in KNOWN_CARD_BRANDS else ""
        if brand == "mc":
            brand = "mastercard"
        elif brand == "american_express":
            brand = "amex"
        last4 = raw_last4 if (len(raw_last4) == 4 and raw_last4.isdigit()) else ""

        if brand and last4:
            return f"card_{brand}_{last4}"
        if brand:
            return f"card_{brand}"
        if last4:
            return f"card_{last4}"
        return "card"

    if stype in ("dob", "online_banking", "brankas"):
        bank = str(
            source.get("provider")
            or source.get("bank_code")
            or source.get("bank")
            or ""
        ).strip().lower()
        if bank in ("bpi", "dob_bpi"):
            return "dob_bpi"
        if bank in ("ubp", "unionbank", "dob_ubp"):
            return "dob_ubp"
        return "dob"

    if stype in ("maya", "paymaya"):
        return "paymaya"
    if stype in ("qrph", "qr_ph"):
        return "qrph"
    if stype == "gcash":
        return "gcash"
    return stype


def extract_payment_method_from_paymongo(
    resource_attrs: Dict[str, Any],
    fallback: Optional[str] = None,
) -> str:
    """
    Inspects a PayMongo checkout_session or payment attributes dictionary to extract
    the confirmed payment method used by the customer.
    """
    if not isinstance(resource_attrs, dict):
        return fallback or "unknown"

    # 1. Inspect nested payments array on checkout_session (only paid entries first)
    payments = resource_attrs.get("payments")
    if isinstance(payments, list) and len(payments) > 0:
        paid_entry = next(
            (
                p for p in payments
                if isinstance(p, dict)
                and (
                    (isinstance(p.get("attributes"), dict) and p["attributes"].get("status") == "paid")
                    or p.get("status") == "paid"
                )
            ),
            None,
        )
        candidate_entry = paid_entry or (payments[-1] if isinstance(payments[-1], dict) else None)
        if isinstance(candidate_entry, dict):
            p_attrs = candidate_entry.get("attributes", candidate_entry)
            if isinstance(p_attrs, dict):
                source = p_attrs.get("source")
                if isinstance(source, dict):
                    resolved = _resolve_source_dict(source)
                    if resolved:
                        return resolved

    # 2. Inspect direct source attribute on a payment resource
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
            return "dob"
        if pm_clean in ("maya", "paymaya"):
            return "paymaya"
        if pm_clean in ("qrph", "qr_ph"):
            return "qrph"
        if pm_clean == "gcash":
            return "gcash"
        return pm_clean

    # 4. Inspect metadata.payment_method only if specific
    meta = resource_attrs.get("metadata")
    if isinstance(meta, dict):
        meta_pm = meta.get("payment_method")
        if isinstance(meta_pm, str) and meta_pm.strip():
            cleaned_meta = meta_pm.strip().lower()
            if cleaned_meta not in ("hosted_checkout", "unknown"):
                return cleaned_meta

    return fallback or "unknown"


def build_supported_channels_matrix(pm_code: str, is_payment_settled: bool, is_method_confirmed: bool) -> List[Dict[str, Any]]:
    """
    Constructs the supported payment rails list without marking multiple channels
    as simultaneously settled or marking a channel settled when payment is pending.
    """
    active_settled = bool(is_payment_settled and is_method_confirmed)

    card_badge = "Card"
    card_ch_code = "card"
    if pm_code.startswith("card_visa"):
        card_badge = "VISA"
        card_ch_code = "card_visa"
    elif pm_code.startswith("card_mastercard"):
        card_badge = "Mastercard"
        card_ch_code = "card_mastercard"
    elif pm_code.startswith("card_jcb"):
        card_badge = "JCB"
        card_ch_code = "card_jcb"

    is_qrph = active_settled and pm_code == "qrph"
    is_card = active_settled and (pm_code == "card" or pm_code.startswith("card_"))
    is_gcash = active_settled and pm_code == "gcash"
    is_maya = active_settled and pm_code in ("paymaya", "maya")
    is_bpi = active_settled and pm_code in ("dob_bpi", "bpi")
    is_ubp = active_settled and pm_code in ("dob_ubp", "ubp", "unionbank")

    channels = [
        {
            "code": "qrph",
            "name": "QR Ph (InstaPay / PESONet)",
            "badge": "QR Ph",
            "selected": is_qrph,
            "settled": is_qrph,
        },
        {
            "code": card_ch_code,
            "name": "Debit / Credit Card",
            "badge": card_badge,
            "selected": is_card,
            "settled": is_card,
        },
        {
            "code": "gcash",
            "name": "E-Wallet (GCash)",
            "badge": "GCash",
            "selected": is_gcash,
            "settled": is_gcash,
        },
        {
            "code": "paymaya",
            "name": "E-Wallet (Maya)",
            "badge": "Maya",
            "selected": is_maya,
            "settled": is_maya,
        },
        {
            "code": "dob_bpi",
            "name": "Online Banking (BPI)",
            "badge": "BPI",
            "selected": is_bpi,
            "settled": is_bpi,
        },
    ]

    if is_ubp:
        channels.append(
            {
                "code": "dob_ubp",
                "name": "Online Banking (UnionBank)",
                "badge": "UnionBank",
                "selected": True,
                "settled": True,
            }
        )
    return channels


# ==============================================================================
# COMPONENT 2: Customer & Payer Normalization
# ==============================================================================

def format_philippine_phone(raw_phone: Optional[str]) -> Optional[str]:
    """
    Normalizes Philippine mobile numbers into '+63 9XX XXX XXXX' format.
    """
    if not raw_phone:
        return None
    raw_str = str(raw_phone).strip()
    if not raw_str:
        return None
    digits = "".join(c for c in raw_str if c.isdigit())

    if digits.startswith("639") and len(digits) == 12:
        return f"+63 {digits[2:5]} {digits[5:8]} {digits[8:12]}"
    if digits.startswith("09") and len(digits) == 11:
        return f"+63 {digits[1:4]} {digits[4:7]} {digits[7:11]}"
    if digits.startswith("9") and len(digits) == 10:
        return f"+63 {digits[0:3]} {digits[3:6]} {digits[6:10]}"

    return raw_str


def extract_customer_info_from_paymongo(
    resource_attrs: Dict[str, Any],
    fallback_meta: Optional[Dict[str, Any]] = None,
) -> Dict[str, Any]:
    """
    Extracts customer billing details from PayMongo attributes or order metadata,
    tracking whether the payer details came from an authoritative provider billing record.
    """
    meta = fallback_meta if isinstance(fallback_meta, dict) else {}
    cust_name: Optional[str] = None
    cust_email: Optional[str] = None
    cust_phone: Optional[str] = None
    provider_verified_payer = False

    if isinstance(resource_attrs, dict):
        billing = resource_attrs.get("billing")
        if isinstance(billing, dict):
            cust_name = billing.get("name")
            cust_email = billing.get("email")
            cust_phone = billing.get("phone")
            if cust_name or cust_email:
                provider_verified_payer = True

        payments = resource_attrs.get("payments")
        if isinstance(payments, list):
            for p in payments:
                if isinstance(p, dict):
                    p_attrs = p.get("attributes", p)
                    if isinstance(p_attrs, dict):
                        p_billing = p_attrs.get("billing")
                        if isinstance(p_billing, dict):
                            cust_name = cust_name or p_billing.get("name")
                            cust_email = cust_email or p_billing.get("email")
                            cust_phone = cust_phone or p_billing.get("phone")
                            if p_billing.get("name") or p_billing.get("email"):
                                provider_verified_payer = True

        customer = resource_attrs.get("customer")
        if isinstance(customer, dict):
            first = str(customer.get("first_name") or "").strip()
            last = str(customer.get("last_name") or "").strip()
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
        (cust_name.strip() if isinstance(cust_name, str) and cust_name.strip() else None)
        or meta.get("customer_name")
        or meta.get("name")
        or "Not Provided"
    )
    final_email = (
        (cust_email.strip() if isinstance(cust_email, str) and cust_email.strip() else None)
        or meta.get("customer_email")
        or meta.get("email")
        or meta.get("billing_email")
    )
    final_phone = format_philippine_phone(
        (str(cust_phone).strip() if cust_phone else None)
        or meta.get("customer_phone")
        or meta.get("customer_mobile")
        or meta.get("phone")
    )

    return {
        "customer_name": final_name,
        "customer_email": final_email,
        "customer_phone": final_phone,
        "payer_verified": bool(provider_verified_payer),
    }


# ==============================================================================
# COMPONENT 3: Payment Verification & Authoritative State Service
# ==============================================================================

def verify_payment_and_reconcile(order: Any, attempt: Any) -> Dict[str, Any]:
    """
    Separates payment verification from receipt presentation.
    Validates payment attempt status, order correlation, currency, and minor-unit amounts.
    """
    att_status = str(getattr(attempt, "status", "") or "").strip().lower()
    ord_status = str(getattr(order, "status", "") or "").strip().lower()

    att_order_id = getattr(attempt, "order_id", None)
    ord_id = getattr(order, "id", None)
    order_mismatch = (
        att_order_id is not None
        and ord_id is not None
        and str(att_order_id) != str(ord_id)
    )

    att_amount_raw = getattr(attempt, "amount_minor", None)
    ord_amount_raw = getattr(order, "amount_minor", None)

    if att_amount_raw is None and ord_amount_raw is None:
        raise ValueError("Missing authoritative amount_minor on both PaymentAttempt and Order.")

    att_amount = int(att_amount_raw) if att_amount_raw is not None else None
    ord_amount = int(ord_amount_raw) if ord_amount_raw is not None else None

    if (att_amount is not None and att_amount < 0) or (ord_amount is not None and ord_amount < 0):
        raise ValueError("Transaction amount_minor cannot be negative.")

    amount_mismatch = (
        att_amount is not None
        and ord_amount is not None
        and att_amount != ord_amount
    )

    att_currency = str(getattr(attempt, "currency", None) or "PHP").strip().upper()
    ord_currency = str(getattr(order, "currency", None) or att_currency).strip().upper()
    currency_mismatch = att_currency != ord_currency

    authoritative_amount_minor = att_amount if att_amount is not None else (ord_amount if ord_amount is not None else 0)

    # Determine authoritative payment state
    if order_mismatch or amount_mismatch or currency_mismatch:
        state_code = "reconciliation_error"
        payment_status = "RECONCILIATION MISMATCH"
        is_confirmed_paid = False
    elif att_status in ("failed", "cancelled", "canceled", "expired") or ord_status in ("failed", "cancelled", "canceled"):
        state_code = "failed"
        payment_status = "PAYMENT FAILED" if att_status == "failed" else "PAYMENT CANCELLED"
        is_confirmed_paid = False
    elif att_status == "refunded" or ord_status == "refunded":
        state_code = "refunded"
        payment_status = "PAYMENT REFUNDED"
        is_confirmed_paid = False
    elif att_status == "disputed" or ord_status == "disputed":
        state_code = "disputed"
        payment_status = "PAYMENT DISPUTED"
        is_confirmed_paid = False
    elif att_status in ("succeeded", "paid") or (att_status == "" and ord_status == "paid"):
        state_code = "paid"
        payment_status = "PAYMENT CONFIRMED"
        is_confirmed_paid = True
    else:
        state_code = "pending"
        payment_status = "PAYMENT PENDING"
        is_confirmed_paid = False

    return {
        "state_code": state_code,
        "payment_status": payment_status,
        "is_confirmed_paid": is_confirmed_paid,
        "amount_minor": authoritative_amount_minor,
        "currency": att_currency,
        "order_mismatch": order_mismatch,
        "amount_mismatch": amount_mismatch,
        "currency_mismatch": currency_mismatch,
    }


# ==============================================================================
# COMPONENT 4: Tax & Monetary Calculation Engine (Exact Decimal Arithmetic)
# ==============================================================================

def minor_to_decimal_php(amount_minor: int) -> Decimal:
    return (Decimal(int(amount_minor)) / Decimal("100")).quantize(
        Decimal("0.01"), rounding=ROUND_HALF_UP
    )


def format_minor_php(amount_minor: int) -> str:
    return f"{minor_to_decimal_php(amount_minor):,.2f}"


def parse_php_to_minor(val: Any) -> Optional[int]:
    if val is None:
        return None
    if isinstance(val, int):
        return val
    try:
        cleaned = str(val).replace("PHP", "").replace("₱", "").replace(",", "").strip()
        dec = Decimal(cleaned).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)
        return int(dec * 100)
    except (InvalidOperation, ValueError):
        return None


def compute_tax_breakdown(amount_minor: int, raw_tax_class: Optional[str]) -> Dict[str, Any]:
    """
    Computes exact integer-centavo VAT breakdown using Decimal ROUND_HALF_UP.
    """
    tax_class = str(raw_tax_class or "vatable").strip().lower()
    vatable_minor = 0
    vat_minor = 0
    vat_exempt_minor = 0
    zero_rated_minor = 0
    non_vat_minor = 0

    if tax_class in ("exempt", "vat_exempt"):
        tax_classification = "vat_exempt"
        tax_classification_label = "VAT-Exempt Sales"
        tax_rate_label = "0% (Exempt)"
        vat_exempt_minor = amount_minor
    elif tax_class in ("zero", "zero_rated"):
        tax_classification = "zero_rated"
        tax_classification_label = "Zero-Rated Sales (0% VAT)"
        tax_rate_label = "0% (Zero-Rated)"
        zero_rated_minor = amount_minor
    elif tax_class in ("non_vat", "nonvat"):
        tax_classification = "non_vat"
        tax_classification_label = "Non-VAT Sales"
        tax_rate_label = "Non-VAT"
        non_vat_minor = amount_minor
    else:
        tax_classification = "vatable"
        tax_classification_label = "VATable Sales (12% Inclusive)"
        tax_rate_label = "12%"
        vatable_dec = (Decimal(amount_minor) / Decimal("1.12")).quantize(
            Decimal("1"), rounding=ROUND_HALF_UP
        )
        vatable_minor = int(vatable_dec)
        vat_minor = amount_minor - vatable_minor

    return {
        "tax_classification": tax_classification,
        "tax_classification_label": tax_classification_label,
        "tax_rate_label": tax_rate_label,
        "vatable_sales_minor": vatable_minor,
        "vatable_sales_php": format_minor_php(vatable_minor),
        "vat_amount_minor": vat_minor,
        "vat_amount_php": format_minor_php(vat_minor),
        "vat_exempt_sales_minor": vat_exempt_minor,
        "vat_exempt_sales_php": format_minor_php(vat_exempt_minor),
        "zero_rated_sales_minor": zero_rated_minor,
        "zero_rated_sales_php": format_minor_php(zero_rated_minor),
        "non_vat_sales_minor": non_vat_minor,
        "non_vat_sales_php": format_minor_php(non_vat_minor),
    }


def normalize_and_validate_line_items(
    raw_items: Any,
    order: Any,
    total_amount_minor: int,
    default_quantity: int,
    tax_classification: str,
) -> List[Dict[str, Any]]:
    """
    Validates and normalizes all line items without dropping rows or using
    floating-point division.
    """
    qty = max(1, int(default_quantity))
    normalized: List[Dict[str, Any]] = []

    if isinstance(raw_items, list) and len(raw_items) > 0:
        for idx, item in enumerate(raw_items, start=1):
            if not isinstance(item, dict):
                continue
            item_qty = max(1, int(item.get("quantity") or 1))
            ref_code = str(item.get("reference_code") or getattr(order, "reference_code", None) or f"ITEM-{idx:03d}")
            title = str(item.get("title") or item.get("description") or getattr(order, "title", "Settlement Item"))

            line_minor = item.get("amount_minor")
            if line_minor is None:
                line_minor = parse_php_to_minor(item.get("amount_php"))
            unit_minor = item.get("unit_price_minor")
            if unit_minor is None:
                unit_minor = parse_php_to_minor(item.get("unit_price_php"))

            if line_minor is None and unit_minor is not None:
                line_minor = int(unit_minor) * item_qty
            elif line_minor is None:
                line_minor = total_amount_minor if len(raw_items) == 1 else 0

            if unit_minor is None:
                unit_dec = (Decimal(int(line_minor)) / Decimal(item_qty)).quantize(
                    Decimal("1"), rounding=ROUND_HALF_UP
                )
                unit_minor = int(unit_dec)

            normalized.append(
                {
                    "reference_code": ref_code,
                    "title": title,
                    "quantity": item_qty,
                    "unit_price_minor": int(unit_minor),
                    "unit_price_php": format_minor_php(int(unit_minor)),
                    "amount_minor": int(line_minor),
                    "amount_php": format_minor_php(int(line_minor)),
                    "tax_classification": str(item.get("tax_classification") or tax_classification),
                }
            )

    if not normalized:
        unit_dec = (Decimal(total_amount_minor) / Decimal(qty)).quantize(
            Decimal("1"), rounding=ROUND_HALF_UP
        )
        unit_minor = int(unit_dec)
        normalized.append(
            {
                "reference_code": str(getattr(order, "reference_code", None) or "PROD-001"),
                "title": str(getattr(order, "title", None) or "Settlement Item"),
                "quantity": qty,
                "unit_price_minor": unit_minor,
                "unit_price_php": format_minor_php(unit_minor),
                "amount_minor": total_amount_minor,
                "amount_php": format_minor_php(total_amount_minor),
                "tax_classification": tax_classification,
            }
        )

    return normalized


# ==============================================================================
# COMPONENT 5: Receipt Numbering Component (Collision-Safe & Idempotent)
# ==============================================================================

def allocate_receipt_identifiers(
    order_number: str,
    attempt_id: str,
    manila_dt: datetime,
    is_confirmed_paid: bool,
    existing_record: Optional[Dict[str, Any]] = None,
) -> Dict[str, Optional[str]]:
    """
    Generates deterministic, collision-resistant identifiers using an 8-character
    SHA-256 token of (order_number, attempt_id) rather than a 4-char prefix slice.
    Only issues an Official Receipt (OR-...) when payment is confirmed.
    """
    if isinstance(existing_record, dict):
        ex_or = existing_record.get("official_receipt_number")
        ex_ack = existing_record.get("acknowledgement_number")
        if is_confirmed_paid and ex_or and not str(ex_or).startswith("PND-"):
            return {
                "official_receipt_number": str(ex_or),
                "acknowledgement_number": str(ex_ack or ex_or.replace("OR-", "ACK-", 1)),
            }
        if not is_confirmed_paid and ex_ack:
            return {
                "official_receipt_number": ex_or,
                "acknowledgement_number": str(ex_ack),
            }

    order_parts = str(order_number or "ORD-0001").split("-")
    seq_part = order_parts[-1] if order_parts else "0001"
    ym_str = manila_dt.strftime("%Y%m")

    # Collision-resistant 8-character uppercase hex token derived from full attempt_id
    digest_suffix = hashlib.sha256(f"{order_number}:{attempt_id}".encode("utf-8")).hexdigest()[:8].upper()

    if is_confirmed_paid:
        return {
            "official_receipt_number": f"OR-{ym_str}-{seq_part}-{digest_suffix}",
            "acknowledgement_number": f"ACK-{ym_str}-{seq_part}-{digest_suffix}",
        }

    return {
        "official_receipt_number": None,
        "acknowledgement_number": f"PND-{ym_str}-{seq_part}-{digest_suffix}",
    }


# ==============================================================================
# COMPONENT 9: Safe Email Recipient Resolution
# ==============================================================================

def resolve_recipient_email(
    order_meta: Optional[Dict[str, Any]] = None,
    default_smtp_user: Optional[str] = None,
) -> Optional[str]:
    """
    Resolves the customer recipient email safely.
    Never silently substitutes an internal SMTP account for a customer's address
    unless ALLOW_INTERNAL_EMAIL_FALLBACK=true is explicitly enabled.
    """
    if not isinstance(order_meta, dict):
        order_meta = {}

    allow_internal_fallback = os.getenv("ALLOW_INTERNAL_EMAIL_FALLBACK", "false").strip().lower() in ("1", "true", "yes")
    smtp_user = (default_smtp_user or os.getenv("SMTP_USER", "")).strip()

    customer_candidates = [
        order_meta.get("customer_email"),
        order_meta.get("email"),
        order_meta.get("billing_email"),
    ]

    for candidate in customer_candidates:
        if isinstance(candidate, str):
            addr = candidate.strip()
            if EMAIL_REGEX.match(addr):
                domain = addr.split("@")[-1].lower()
                if domain in ("acme.com", "example.com", "localhost"):
                    if allow_internal_fallback and EMAIL_REGEX.match(smtp_user):
                        return smtp_user
                    continue
                return addr

    default_receipt_email = os.getenv("DEFAULT_RECEIPT_EMAIL", "").strip()
    if EMAIL_REGEX.match(default_receipt_email):
        return default_receipt_email

    if allow_internal_fallback and EMAIL_REGEX.match(smtp_user):
        return smtp_user

    return None


# ==============================================================================
# COMPONENT 10: Deterministic Audit Digest Engine
# ==============================================================================

def compute_canonical_audit_digest(payload: Dict[str, Any]) -> str:
    """
    Computes a versioned, canonical JSON SHA-256 integrity digest covering all
    authoritative financial and identity fields.
    Note: This is a tamper-evident integrity digest (sha256:v2), not an asymmetric
    cryptographic digital signature.
    """
    canonical_subset = {
        "digest_version": "sha256:v2",
        "official_receipt_number": payload.get("official_receipt_number"),
        "acknowledgement_number": payload.get("acknowledgement_number"),
        "order_id": str(payload.get("order_id") or ""),
        "order_number": str(payload.get("order_number") or ""),
        "payment_attempt_id": str(payload.get("payment_attempt_id") or ""),
        "provider_payment_id": payload.get("provider_payment_id"),
        "payment_status": payload.get("payment_status"),
        "amount_minor": int(payload.get("amount_minor") or 0),
        "currency": str(payload.get("currency") or "PHP"),
        "tax_classification": payload.get("tax_classification"),
        "vatable_sales_minor": int(payload.get("vatable_sales_minor") or 0),
        "vat_amount_minor": int(payload.get("vat_amount_minor") or 0),
        "vat_exempt_sales_minor": int(payload.get("vat_exempt_sales_minor") or 0),
        "zero_rated_sales_minor": int(payload.get("zero_rated_sales_minor") or 0),
        "non_vat_sales_minor": int(payload.get("non_vat_sales_minor") or 0),
        "paid_at_iso": payload.get("paid_at_iso"),
        "line_items": [
            {
                "reference_code": item.get("reference_code"),
                "title": item.get("title"),
                "quantity": int(item.get("quantity") or 1),
                "unit_price_minor": int(item.get("unit_price_minor") or 0),
                "amount_minor": int(item.get("amount_minor") or 0),
            }
            for item in (payload.get("line_items") or [])
            if isinstance(item, dict)
        ],
    }
    serialized = json.dumps(canonical_subset, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(serialized.encode("utf-8")).hexdigest()


# ==============================================================================
# COMPONENT 5 & 6: Canonical Receipt Assembler & Persistence Repository
# ==============================================================================

def _resolve_latest_fulfillment_status(order: Any, attempt: Any, default_status: str) -> str:
    records = getattr(attempt, "fulfillment_records", None) or getattr(order, "fulfillment_records", None)
    if not records or not isinstance(records, (list, tuple)) or len(records) == 0:
        return default_status
    sorted_records = sorted(
        records,
        key=lambda r: (
            getattr(r, "updated_at", None) or getattr(r, "created_at", None) or datetime.min.replace(tzinfo=timezone.utc),
            getattr(r, "id", 0) or 0,
        ),
    )
    latest = sorted_records[-1]
    return str(getattr(latest, "status", None) or default_status)


def build_receipt_and_acknowledgement(
    order: Any,
    attempt: Any,
    outbox_status: str = "pending",
    smtp_delivery: Optional[Dict[str, Any]] = None,
) -> Dict[str, Any]:
    """
    Constructs or retrieves the canonical Official Receipt (OR) and Acknowledgement
    Report (ACK) snapshot.
    - Preserves already-issued historical receipts without mutating their core
      financial data, timestamps, or identifiers.
    - Separates payment verification from receipt issuance.
    """
    meta = order.metadata_dict if hasattr(order, "metadata_dict") and isinstance(order.metadata_dict, dict) else {}
    existing_receipts = meta.get("receipt_records") if isinstance(meta.get("receipt_records"), dict) else {}
    att_id = str(getattr(attempt, "id", None) or "att_direct")
    existing = existing_receipts.get(att_id) if isinstance(existing_receipts, dict) else None

    verification = verify_payment_and_reconcile(order, attempt)
    is_confirmed_paid = verification["is_confirmed_paid"]

    # Preserve historical issued snapshot if already finalized for a paid transaction
    if (
        isinstance(existing, dict)
        and existing.get("payment_status") == "PAYMENT CONFIRMED"
        and existing.get("official_receipt_number")
        and existing.get("verification_hash")
    ):
        snapshot = dict(existing)
        if smtp_delivery is not None:
            snapshot["smtp_delivery"] = smtp_delivery
        return snapshot

    paid_dt: Optional[datetime] = getattr(attempt, "paid_at", None) if is_confirmed_paid else None
    if paid_dt is None:
        if isinstance(existing, dict) and existing.get("paid_at_iso"):
            try:
                paid_dt = datetime.fromisoformat(existing["paid_at_iso"])
            except ValueError:
                paid_dt = None
    if paid_dt is None:
        paid_dt = getattr(attempt, "updated_at", None) or getattr(order, "updated_at", None) or datetime.now(timezone.utc)
    if paid_dt.tzinfo is None:
        paid_dt = paid_dt.replace(tzinfo=timezone.utc)

    manila_dt = paid_dt.astimezone(MANILA_TZ)
    utc_dt = paid_dt.astimezone(timezone.utc)

    order_num_str = str(getattr(order, "order_number", None) or "ORD-0000")
    identifiers = allocate_receipt_identifiers(
        order_number=order_num_str,
        attempt_id=att_id,
        manila_dt=manila_dt,
        is_confirmed_paid=is_confirmed_paid,
        existing_record=existing,
    )
    or_number = identifiers["official_receipt_number"]
    ack_number = identifiers["acknowledgement_number"]

    # Extract and normalize payment method without inventing certainty
    provider_attrs = meta.get("latest_provider_attrs") if isinstance(meta.get("latest_provider_attrs"), dict) else {}
    raw_attempt_pm = getattr(attempt, "payment_method", None)
    extracted_pm = extract_payment_method_from_paymongo(provider_attrs, fallback=raw_attempt_pm)
    pm_info = get_payment_method_info(extracted_pm)
    pm_code = pm_info["code"]
    is_channel_confirmed = bool(pm_info.get("confirmed", False))

    amount_minor = verification["amount_minor"]
    amount_php_str = format_minor_php(amount_minor)

    raw_tax_class = (
        meta.get("tax_classification")
        or meta.get("tax_type")
        or "vatable"
    )
    tax_data = compute_tax_breakdown(amount_minor, raw_tax_class)

    raw_qty = meta.get("quantity")
    quantity = int(raw_qty) if (raw_qty is not None and int(raw_qty) > 0) else 1

    line_items = normalize_and_validate_items_with_reconciliation(
        raw_items=meta.get("line_items"),
        order=order,
        total_amount_minor=amount_minor,
        default_quantity=quantity,
        tax_classification=tax_data["tax_classification"],
    )

    unit_price_minor = line_items[0]["unit_price_minor"]
    unit_price_php_str = line_items[0]["unit_price_php"]

    supported_channels = build_supported_channels_matrix(
        pm_code=pm_code,
        is_payment_settled=is_confirmed_paid,
        is_method_confirmed=is_channel_confirmed,
    )

    # Separate real provider references from internal attempt ID
    real_provider_payment_id = getattr(attempt, "provider_payment_id", None)
    if isinstance(real_provider_payment_id, str) and not real_provider_payment_id.strip():
        real_provider_payment_id = None
    real_checkout_session_id = (
        getattr(attempt, "provider_session_id", None)
        or getattr(attempt, "checkout_session_id", None)
        or meta.get("checkout_session_id")
    )

    actual_outbox_status = _resolve_latest_fulfillment_status(order, attempt, outbox_status)

    # Require explicit accounting journal reference or verified gl_posted timestamp
    journal_entry_id = meta.get("journal_entry_id") or meta.get("gl_reference")
    ledger_posted = bool(journal_entry_id or meta.get("ledger_posted") is True or meta.get("gl_posted") is True)

    if ledger_posted and is_confirmed_paid:
        ledger_status = "POSTED TO ACME GENERAL LEDGER"
        ack_status = "VERIFIED & POSTED TO ACME GENERAL LEDGER"
    elif is_confirmed_paid:
        ledger_status = "PENDING LEDGER POSTING"
        ack_status = "VERIFIED & PENDING GENERAL LEDGER POSTING"
    else:
        ledger_status = "UNSETTLED"
        ack_status = "AWAITING SETTLEMENT"

    # Separate receipt data preparation from persisted artifact state
    persisted_artifact_status = (
        existing.get("artifact_status")
        if isinstance(existing, dict) and existing.get("artifact_status")
        else "pending_generation"
    )
    if is_confirmed_paid:
        receipt_status = "RECEIPT ISSUED" if persisted_artifact_status == "generated" else "ISSUED (PDF PENDING)"
        document_title = "Official Receipt & Treasury Acknowledgement Report"
        document_type = "Official Receipt"
    else:
        receipt_status = "NOT ISSUED (PAYMENT UNCONFIRMED)"
        document_title = "Payment Status & Acknowledgement Advice"
        document_type = "Pending Payment Acknowledgement"

    cust_info = extract_customer_info_from_paymongo(provider_attrs, fallback_meta=meta)
    customer_name = cust_info["customer_name"]
    customer_email = cust_info["customer_email"]
    customer_phone = cust_info["customer_phone"]
    payer_verified = cust_info["payer_verified"]

    recipient_email = resolve_recipient_email(meta)
    resolved_smtp = smtp_delivery or (existing.get("smtp_delivery") if isinstance(existing, dict) else None) or {
        "status": "pending" if (is_confirmed_paid and recipient_email) else "not_applicable",
        "recipient": recipient_email,
        "sent_at": None,
        "message": "Queued for dispatch" if (is_confirmed_paid and recipient_email) else "Awaiting confirmed payment or valid recipient",
    }

    safe_att_token = hashlib.sha256(att_id.encode("utf-8")).hexdigest()[:8].upper()
    pdf_filename = f"Acme_OR_ACK_{order_num_str}_{safe_att_token}.pdf"

    receipt_payload: Dict[str, Any] = {
        "official_receipt_number": or_number,
        "acknowledgement_number": ack_number,
        "document_title": document_title,
        "document_type": document_type,
        "issuer": {
            "company_name": "R.G.E Accounting Firm Inc.",
            "department": "Corporate Accounting & Treasury Operations",
            "tin": "000-842-915-000 VAT",
            "address": "R.G.E Corporate Center, Makati City, Metro Manila, Philippines",
        },
        "order_id": getattr(order, "id", None),
        "order_number": order_num_str,
        "order_title": getattr(order, "title", "Settlement Item"),
        "category": getattr(order, "category", "product_order"),
        "category_label": (
            "Product Line Order"
            if getattr(order, "category", "product_order") == "product_order"
            else "Department Cost Center Settlement"
        ),
        "reference_code": getattr(order, "reference_code", "PROD-001"),
        "quantity": quantity,
        "unit_price_minor": unit_price_minor,
        "unit_price_php": unit_price_php_str,
        "payment_attempt_id": att_id,
        "provider": "PayMongo Philippines",
        "provider_payment_id": real_provider_payment_id,
        "provider_checkout_session_id": real_checkout_session_id,
        "payment_method_code": pm_code,
        "payment_method_label": pm_info["label"],
        "payment_method_short": pm_info["short_label"],
        "payment_method_category": pm_info["category"],
        "payment_rail": pm_info["rail"],
        "payment_method_confirmed": is_channel_confirmed,
        "supported_channels": supported_channels,
        "amount_minor": amount_minor,
        "currency": verification["currency"],
        "amount_php": amount_php_str,
        **tax_data,
        "gateway_fee_minor": 0,
        "gateway_fee_php": "0.00",
        "paid_at_iso": utc_dt.isoformat() if is_confirmed_paid else None,
        "paid_at_manila": manila_dt.strftime("%b %d, %Y, %I:%M:%S %p PHT") if is_confirmed_paid else "Unsettled / Pending",
        "paid_at_utc": utc_dt.strftime("%Y-%m-%d %H:%M:%S UTC") if is_confirmed_paid else "Unsettled / Pending",
        "payment_status": verification["payment_status"],
        "payment_state_code": verification["state_code"],
        "is_confirmed_paid": is_confirmed_paid,
        "reconciliation_flags": {
            "order_mismatch": verification["order_mismatch"],
            "amount_mismatch": verification["amount_mismatch"],
            "currency_mismatch": verification["currency_mismatch"],
        },
        "receipt_status": receipt_status,
        "artifact_status": persisted_artifact_status,
        "ledger_status": ledger_status,
        "journal_entry_id": journal_entry_id,
        "acknowledgement_status": ack_status,
        "outbox_status": actual_outbox_status,
        "portal_copy_available": persisted_artifact_status == "generated",
        "pdf_filename": pdf_filename,
        "pdf_url": f"/api/v1/payments/{att_id}/receipt/pdf",
        "pdf_download_url": f"/api/v1/payments/{att_id}/receipt/pdf?download=1",
        "smtp_delivery": resolved_smtp,
        "customer": {
            "name": customer_name,
            "email": customer_email or "Not Provided",
            "phone": customer_phone or "Not Provided",
            "verified": payer_verified,
        },
        "customer_name": customer_name,
        "customer_email": customer_email,
        "customer_phone": customer_phone,
        "payer_verified": payer_verified,
        "line_items": line_items,
        "tax_compliance_note": (
            "Electronic Payment Settlement Record. Pursuant to BIR RMC No. 77-2024, "
            "official commercial VAT Invoices are issued by Corporate Accounting upon "
            "customer tax identification verification."
        ),
    }

    digest = compute_canonical_audit_digest(receipt_payload)
    receipt_payload["verification_hash"] = digest
    receipt_payload["sha256_audit_digest"] = digest
    receipt_payload["audit_digest_type"] = "SHA-256 (sha256:v2 Canonical Snapshot Digest)"

    return receipt_payload


def normalize_and_validate_items_with_reconciliation(
    raw_items: Any,
    order: Any,
    total_amount_minor: int,
    default_quantity: int,
    tax_classification: str,
) -> List[Dict[str, Any]]:
    items = normalize_and_validate_line_items(
        raw_items=raw_items,
        order=order,
        total_amount_minor=total_amount_minor,
        default_quantity=default_quantity,
        tax_classification=tax_classification,
    )
    items_sum = sum(int(i["amount_minor"]) for i in items)
    if items_sum != total_amount_minor and len(items) == 1:
        items[0]["amount_minor"] = total_amount_minor
        items[0]["amount_php"] = format_minor_php(total_amount_minor)
    return items


# ==============================================================================
# COMPONENT 8: Artifact Lifecycle Manager & Reliable Persistence
# ==============================================================================

def persist_receipt_on_order(
    db: Any,
    order: Any,
    attempt: Any,
    receipt_data: Dict[str, Any],
) -> Dict[str, Any]:
    """
    Generates and atomically writes the PDF artifact FIRST, updates the receipt's
    artifact_status truthfully, and then commits the canonical receipt record
    to the database.
    """
    att_id = str(getattr(attempt, "id", None) or receipt_data.get("payment_attempt_id") or "att_direct")
    updated_receipt = dict(receipt_data)

    try:
        RECEIPTS_DIR.mkdir(parents=True, exist_ok=True)
        pdf_bytes = generate_receipt_pdf_bytes(updated_receipt)
        target_path = RECEIPTS_DIR / str(updated_receipt["pdf_filename"])

        with tempfile.NamedTemporaryFile(
            mode="wb",
            dir=str(RECEIPTS_DIR),
            prefix="tmp_receipt_",
            suffix=".pdf",
            delete=False,
        ) as tmp_file:
            tmp_file.write(pdf_bytes)
            tmp_file.flush()
            os.fsync(tmp_file.fileno())
            tmp_path = Path(tmp_file.name)

        os.replace(str(tmp_path), str(target_path))
        updated_receipt["artifact_status"] = "generated"
        updated_receipt["portal_copy_available"] = True
        if updated_receipt.get("is_confirmed_paid"):
            updated_receipt["receipt_status"] = "RECEIPT GENERATED"
    except Exception as exc:
        logger.error(
            "Failed to generate or write receipt PDF artifact for order=%s attempt=%s: %s",
            getattr(order, "order_number", "unknown"),
            att_id,
            exc,
            exc_info=True,
        )
        updated_receipt["artifact_status"] = "failed"
        updated_receipt["portal_copy_available"] = False
        if updated_receipt.get("is_confirmed_paid"):
            updated_receipt["receipt_status"] = "ISSUED (PDF ARTIFACT FAILED)"

    meta = dict(order.metadata_dict or {}) if hasattr(order, "metadata_dict") else {}
    records = dict(meta.get("receipt_records") or {})
    records[att_id] = updated_receipt
    meta["receipt_records"] = records
    if updated_receipt.get("is_confirmed_paid") or "latest_receipt" not in meta:
        meta["latest_receipt"] = updated_receipt
    order.metadata_dict = meta

    try:
        db.add(order)
        db.commit()
    except Exception as db_exc:
        if hasattr(db, "rollback"):
            db.rollback()
        logger.error(
            "Database commit failed while persisting receipt for order=%s attempt=%s: %s",
            getattr(order, "order_number", "unknown"),
            att_id,
            db_exc,
            exc_info=True,
        )
        raise

    return updated_receipt


# ==============================================================================
# COMPONENT 7: Dynamic Multi-Page ISO-32000-1 Vector PDF Engine (%PDF-1.4)
# ==============================================================================

def _pdf_escape(text: Any) -> str:
    """
    Sanitizes text for standard Type1 WinAnsiEncoding PDF strings without
    corrupting financial identifiers.
    """
    if text is None:
        return ""
    cleaned = (
        str(text)
        .replace("₱", "PHP ")
        .replace("•", "-")
        .replace("—", "-")
        .replace("–", "-")
        .replace("·", "-")
    )
    cleaned = cleaned.encode("latin-1", errors="replace").decode("latin-1")
    return cleaned.replace("\\", "\\\\").replace("(", "\\(").replace(")", "\\)")


def _estimate_text_width(text: str, font: str, size: float) -> float:
    """
    Computes accurate string width in points for standard PDF Type1 fonts:
    F1 (Helvetica), F2 (Helvetica-Bold), F3 (Courier), F4 (Courier-Bold).
    """
    if not text:
        return 0.0
    if font in ("F3", "F4"):
        return len(text) * size * 0.60
    width_units = 0.0
    for ch in text:
        if ch in "il.,:;|!I'":
            width_units += 0.28
        elif ch in "MW@#%&":
            width_units += 0.85
        elif ch.isupper():
            width_units += 0.68
        elif ch.isdigit():
            width_units += 0.56
        elif ch == " ":
            width_units += 0.28
        else:
            width_units += 0.52
    if font == "F2":
        width_units *= 1.05
    return width_units * size


def _wrap_text(text: str, font: str, size: float, max_width: float) -> List[str]:
    """
    Wraps long descriptions or identifiers across multiple lines so text never
    clips or overlaps adjacent columns.
    """
    raw = str(text or "").strip()
    if not raw:
        return [""]
    words = raw.split(" ")
    lines: List[str] = []
    current = ""

    for word in words:
        # Break up unbroken long tokens (such as hashes or long references)
        while _estimate_text_width(word, font, size) > max_width and len(word) > 4:
            split_idx = max(1, int(len(word) * (max_width / _estimate_text_width(word, font, size))))
            chunk = word[:split_idx]
            if current:
                lines.append(current)
                current = ""
            lines.append(chunk)
            word = word[split_idx:]

        candidate = f"{current} {word}".strip() if current else word
        if _estimate_text_width(candidate, font, size) <= max_width:
            current = candidate
        else:
            if current:
                lines.append(current)
            current = word

    if current:
        lines.append(current)
    return lines or [""]


class _PdfCanvasBuilder:
    """
    Manages multi-page 612 x 792 pt US Letter pages, vector primitives, and
    consistent border radii aligned with the web design system.
    """

    KAPPA = 0.55228475

    def __init__(self) -> None:
        self.pages: List[List[str]] = []
        self.new_page()

    @property
    def cmds(self) -> List[str]:
        return self.pages[-1]

    def new_page(self) -> None:
        self.pages.append([])
        # Subtle page background (#FAFAFA)
        self.rect_fill(0, 0, 612, 792, 0.980, 0.980, 0.980)

    def rect_fill(self, x: float, y: float, w: float, h: float, r: float, g: float, b: float) -> None:
        self.cmds.append(f"{r:.3f} {g:.3f} {b:.3f} rg {x:.1f} {y:.1f} {w:.1f} {h:.1f} re f")

    def rounded_box(
        self,
        x: float,
        y: float,
        w: float,
        h: float,
        r_tl: float = 6.0,
        r_tr: Optional[float] = None,
        r_br: Optional[float] = None,
        r_bl: Optional[float] = None,
        fill_rgb: Optional[Tuple[float, float, float]] = None,
        stroke_rgb: Optional[Tuple[float, float, float]] = None,
        lw: float = 0.75,
    ) -> None:
        tr = r_tl if r_tr is None else r_tr
        br = r_tl if r_br is None else r_br
        bl = r_tl if r_bl is None else r_bl

        max_r = min(w / 2.0, h / 2.0)
        tl = min(max(0.0, r_tl), max_r)
        tr = min(max(0.0, tr), max_r)
        br = min(max(0.0, br), max_r)
        bl = min(max(0.0, bl), max_r)
        k = self.KAPPA

        ops: List[str] = []
        if fill_rgb is not None:
            ops.append(f"{fill_rgb[0]:.3f} {fill_rgb[1]:.3f} {fill_rgb[2]:.3f} rg")
        if stroke_rgb is not None:
            ops.append(f"{lw:.2f} w {stroke_rgb[0]:.3f} {stroke_rgb[1]:.3f} {stroke_rgb[2]:.3f} RG")

        ops.append(f"{x + bl:.2f} {y:.2f} m")
        ops.append(f"{x + w - br:.2f} {y:.2f} l")
        if br > 0:
            ops.append(f"{x + w - br + br * k:.2f} {y:.2f} {x + w:.2f} {y + br - br * k:.2f} {x + w:.2f} {y + br:.2f} c")
        ops.append(f"{x + w:.2f} {y + h - tr:.2f} l")
        if tr > 0:
            ops.append(f"{x + w:.2f} {y + h - tr + tr * k:.2f} {x + w - tr + tr * k:.2f} {y + h:.2f} {x + w - tr:.2f} {y + h:.2f} c")
        ops.append(f"{x + tl:.2f} {y + h:.2f} l")
        if tl > 0:
            ops.append(f"{x + tl - tl * k:.2f} {y + h:.2f} {x:.2f} {y + h - tl + tl * k:.2f} {x:.2f} {y + h - tl:.2f} c")
        ops.append(f"{x:.2f} {y + bl:.2f} l")
        if bl > 0:
            ops.append(f"{x:.2f} {y + bl - bl * k:.2f} {x + bl - bl * k:.2f} {y:.2f} {x + bl:.2f} {y:.2f} c")
        ops.append("h")

        if fill_rgb is not None and stroke_rgb is not None:
            ops.append("B")
        elif fill_rgb is not None:
            ops.append("f")
        elif stroke_rgb is not None:
            ops.append("S")

        self.cmds.append(" ".join(ops))

    def line(
        self,
        x1: float,
        y1: float,
        x2: float,
        y2: float,
        rgb: Tuple[float, float, float] = (0.894, 0.894, 0.906),
        lw: float = 0.6,
    ) -> None:
        self.cmds.append(f"{lw:.2f} w {rgb[0]:.3f} {rgb[1]:.3f} {rgb[2]:.3f} RG {x1:.1f} {y1:.1f} m {x2:.1f} {y2:.1f} l S")

    def text(
        self,
        x: float,
        y: float,
        s: str,
        font: str = "F1",
        size: float = 9.0,
        rgb: Tuple[float, float, float] = (0.094, 0.094, 0.106),
    ) -> None:
        esc = _pdf_escape(s)
        self.cmds.append(f"BT /{font} {size:.1f} Tf {rgb[0]:.3f} {rgb[1]:.3f} {rgb[2]:.3f} rg {x:.1f} {y:.1f} Td ({esc}) Tj ET")

    def text_right(
        self,
        right_x: float,
        y: float,
        s: str,
        font: str = "F1",
        size: float = 9.0,
        rgb: Tuple[float, float, float] = (0.094, 0.094, 0.106),
    ) -> None:
        w = _estimate_text_width(str(s), font, size)
        self.text(max(36.0, right_x - w), y, s, font=font, size=size, rgb=rgb)


def generate_receipt_pdf_bytes(receipt: Dict[str, Any]) -> bytes:
    """
    Generates a valid, multi-page-capable ISO-32000-1 PDF document (612 x 792 pt)
    with a restrained financial-document hierarchy, exact currency alignment,
    full line-item rendering, and truthful status indicators.
    """
    pdf = _PdfCanvasBuilder()

    # Palette Tokens (Aligned with Part 2.3 WCAG AA Palette)
    C_SURFACE = (1.000, 1.000, 1.000)       # #FFFFFF
    C_MUTED = (0.957, 0.957, 0.961)         # #F4F4F5
    C_SUBTLE = (0.980, 0.980, 0.980)        # #FAFAFA
    C_BORDER = (0.894, 0.894, 0.906)        # #E4E4E7
    C_TEXT_PRIMARY = (0.094, 0.094, 0.106)  # #18181B
    C_TEXT_SECOND = (0.322, 0.322, 0.357)   # #52525B
    C_TEXT_MUTED = (0.443, 0.443, 0.478)    # #71717A

    C_SUCCESS = (0.082, 0.502, 0.239)       # #15803D
    C_SUCCESS_BG = (0.941, 0.992, 0.957)    # #F0FDF4
    C_WARNING = (0.573, 0.251, 0.055)       # #92400E
    C_WARNING_BG = (1.000, 0.984, 0.922)    # #FFFBEB
    C_DANGER = (0.725, 0.110, 0.110)        # #B91C1C
    C_DANGER_BG = (0.996, 0.949, 0.949)     # #FEF2F2

    # Point-based Border Radius Scale (Aligned with Part 2.2)
    R_CARD = 12.0
    R_PANEL = 6.0
    R_COMPACT = 3.0
    R_PILL = 10.0

    issuer = receipt.get("issuer") if isinstance(receipt.get("issuer"), dict) else {}
    company_name = str(issuer.get("company_name") or "R.G.E Accounting Firm Inc.")
    department = str(issuer.get("department") or "Corporate Accounting & Treasury Operations")
    tin_str = str(issuer.get("tin") or "000-842-915-000 VAT")

    payment_status = str(receipt.get("payment_status") or "PAYMENT PENDING").upper()
    is_paid = payment_status == "PAYMENT CONFIRMED"
    is_failed = "FAILED" in payment_status or "CANCELLED" in payment_status or "MISMATCH" in payment_status

    if is_paid:
        badge_bg, badge_fg, badge_text = C_SUCCESS_BG, C_SUCCESS, "[OK] Payment Confirmed"
    elif is_failed:
        badge_bg, badge_fg, badge_text = C_DANGER_BG, C_DANGER, f"[!] {payment_status.title()}"
    else:
        badge_bg, badge_fg, badge_text = C_WARNING_BG, C_WARNING, "[...] Payment Pending"

    currency = str(receipt.get("currency") or "PHP").strip().upper()
    amount_php_str = str(receipt.get("amount_php") or "0.00")
    formatted_total = f"{currency} {amount_php_str}"

    # Outer Document Card on Page 1
    pdf.rounded_box(32, 36, 548, 720, r_tl=R_CARD, fill_rgb=C_SURFACE, stroke_rgb=C_BORDER, lw=0.85)

    # 3.1 Issuer & Payment Confirmation Header
    pdf.text(52, 730, company_name, font="F2", size=12.0, rgb=C_TEXT_PRIMARY)
    pdf.text(52, 716, f"{department}  |  TIN: {tin_str}", font="F1", size=8.0, rgb=C_TEXT_SECOND)
    doc_title = str(receipt.get("document_title") or "Official Receipt & Payment Acknowledgement")
    pdf.text(52, 698, doc_title, font="F2", size=14.0, rgb=C_TEXT_PRIMARY)

    # Status Pill
    pill_w = max(124.0, _estimate_text_width(badge_text, "F2", 8.5) + 22.0)
    pill_x = 560.0 - pill_w
    pdf.rounded_box(pill_x, 718, pill_w, 20, r_tl=R_PILL, fill_rgb=badge_bg, stroke_rgb=badge_fg, lw=0.7)
    pdf.text(pill_x + 10, 725, badge_text, font="F2", size=8.5, rgb=badge_fg)

    # 3.2 Amount Paid (Dominant Visual Element, Single Clean Currency Prefix)
    pdf.text(52, 672, "AMOUNT PAID" if is_paid else "AMOUNT DUE / ATTEMPTED", font="F2", size=7.5, rgb=C_TEXT_MUTED)
    pdf.text(52, 646, formatted_total, font="F2", size=24.0, rgb=C_TEXT_PRIMARY)
    pdf.line(52, 632, 560, 632, rgb=C_BORDER, lw=0.75)

    # 3.3 Receipt Identifiers Panel
    pdf.rounded_box(52, 586, 508, 36, r_tl=R_PANEL, fill_rgb=C_MUTED, stroke_rgb=C_BORDER, lw=0.6)
    or_display = str(receipt.get("official_receipt_number") or "Not Issued (Awaiting Settlement)")
    ack_display = str(receipt.get("acknowledgement_number") or "Unavailable")
    ord_display = str(receipt.get("order_number") or "Unavailable")

    pdf.text(64, 610, "Official Receipt No.", font="F1", size=7.2, rgb=C_TEXT_SECOND)
    pdf.text(64, 595, or_display, font="F4", size=9.0, rgb=C_TEXT_PRIMARY)
    pdf.line(232, 592, 232, 616, rgb=C_BORDER, lw=0.6)

    pdf.text(244, 610, "Acknowledgement No.", font="F1", size=7.2, rgb=C_TEXT_SECOND)
    pdf.text(244, 595, ack_display, font="F4", size=9.0, rgb=C_TEXT_PRIMARY)
    pdf.line(412, 592, 412, 616, rgb=C_BORDER, lw=0.6)

    pdf.text(424, 610, "Order Reference", font="F1", size=7.2, rgb=C_TEXT_SECOND)
    pdf.text(424, 595, ord_display, font="F4", size=9.0, rgb=C_TEXT_PRIMARY)

    # 3.4 Transaction Details & 3.5 Customer Information (2-Column Panel Grid)
    pdf.rounded_box(52, 504, 248, 72, r_tl=R_PANEL, fill_rgb=C_SUBTLE, stroke_rgb=C_BORDER, lw=0.6)
    pdf.text(62, 562, "Transaction Details", font="F2", size=8.5, rgb=C_TEXT_PRIMARY)
    pdf.text(62, 548, "Date & Time (Asia/Manila):", font="F1", size=7.2, rgb=C_TEXT_MUTED)
    pdf.text(62, 538, str(receipt.get("paid_at_manila") or "Unsettled / Pending"), font="F1", size=8.0, rgb=C_TEXT_PRIMARY)

    pm_confirmed = bool(receipt.get("payment_method_confirmed", True))
    pm_label = str(receipt.get("payment_method_label") or "Unconfirmed") if pm_confirmed else "Unconfirmed / Unavailable"
    provider_ref = str(receipt.get("provider_payment_id") or "Unavailable")
    pdf.text(62, 524, f"Method: {pm_label[:34]}", font="F1", size=7.8, rgb=C_TEXT_PRIMARY)
    pdf.text(62, 512, f"Provider Ref: {provider_ref[:32]}", font="F3", size=7.4, rgb=C_TEXT_SECOND)

    pdf.rounded_box(312, 504, 248, 72, r_tl=R_PANEL, fill_rgb=C_SUBTLE, stroke_rgb=C_BORDER, lw=0.6)
    payer_verified = bool(receipt.get("payer_verified", False))
    payer_tag = "Verified Payer" if payer_verified else "Provided Payer Info"
    pdf.text(322, 562, f"Customer & Payer ({payer_tag})", font="F2", size=8.5, rgb=C_TEXT_PRIMARY)

    cust_name = str(receipt.get("customer_name") or "Not Provided")
    cust_email = str(receipt.get("customer_email") or "Not Provided")
    cust_phone = str(receipt.get("customer_phone") or "Not Provided")
    pdf.text(322, 546, f"Name: {cust_name[:34]}", font="F1", size=8.0, rgb=C_TEXT_PRIMARY)
    pdf.text(322, 532, f"Email: {cust_email[:36]}", font="F1", size=7.8, rgb=C_TEXT_SECOND)
    pdf.text(322, 518, f"Phone: {cust_phone[:30]}", font="F1", size=7.8, rgb=C_TEXT_SECOND)

    # 3.6 Itemized Transaction Table (Supports Wrapping & Multi-Page Overflow)
    cursor_y = 488.0

    def draw_table_header(y_top: float) -> float:
        pdf.rounded_box(52, y_top - 18, 508, 18, r_tl=R_COMPACT, fill_rgb=C_MUTED, stroke_rgb=C_BORDER, lw=0.6)
        pdf.text(60, y_top - 12, "Description / Reference", font="F2", size=7.8, rgb=C_TEXT_PRIMARY)
        pdf.text_right(365, y_top - 12, "Qty", font="F2", size=7.8, rgb=C_TEXT_PRIMARY)
        pdf.text_right(455, y_top - 12, "Unit Price", font="F2", size=7.8, rgb=C_TEXT_PRIMARY)
        pdf.text_right(552, y_top - 12, "Line Total", font="F2", size=7.8, rgb=C_TEXT_PRIMARY)
        return y_top - 20.0

    cursor_y = draw_table_header(cursor_y)

    raw_items = receipt.get("line_items")
    if not isinstance(raw_items, list) or len(raw_items) == 0:
        raw_items = [
            {
                "reference_code": receipt.get("reference_code") or "PROD-001",
                "title": receipt.get("order_title") or "Settlement Item",
                "quantity": receipt.get("quantity") or 1,
                "unit_price_php": receipt.get("unit_price_php") or amount_php_str,
                "amount_php": amount_php_str,
            }
        ]

    for item in raw_items:
        if not isinstance(item, dict):
            continue
        ref_code = str(item.get("reference_code") or "ITEM")
        title = str(item.get("title") or "Settlement Item")
        qty_str = str(item.get("quantity") or 1)
        u_price = f"{currency} {item.get('unit_price_php') or '0.00'}"
        l_total = f"{currency} {item.get('amount_php') or '0.00'}"

        wrapped_title = _wrap_text(f"{title} ({ref_code})", "F1", 8.0, max_width=260.0)
        row_h = max(20.0, len(wrapped_title) * 11.0 + 8.0)

        if cursor_y - row_h < 76.0:
            pdf.new_page()
            pdf.rounded_box(32, 36, 548, 720, r_tl=R_CARD, fill_rgb=C_SURFACE, stroke_rgb=C_BORDER, lw=0.85)
            pdf.text(52, 730, f"{company_name} - Continuation Sheet ({or_display})", font="F2", size=9.5, rgb=C_TEXT_SECOND)
            cursor_y = draw_table_header(714.0)

        line_y = cursor_y - 11.0
        for idx, w_line in enumerate(wrapped_title):
            pdf.text(60, line_y - (idx * 11.0), w_line, font="F1", size=8.0, rgb=C_TEXT_PRIMARY)

        pdf.text_right(365, line_y, qty_str, font="F3", size=8.0, rgb=C_TEXT_PRIMARY)
        pdf.text_right(455, line_y, u_price, font="F3", size=8.0, rgb=C_TEXT_SECOND)
        pdf.text_right(552, line_y, l_total, font="F4", size=8.2, rgb=C_TEXT_PRIMARY)

        cursor_y -= row_h
        pdf.line(52, cursor_y, 560, cursor_y, rgb=C_BORDER, lw=0.45)

    # Ensure enough vertical space for Tax Summary + Secondary Verification Section
    if cursor_y < 235.0:
        pdf.new_page()
        pdf.rounded_box(32, 36, 548, 720, r_tl=R_CARD, fill_rgb=C_SURFACE, stroke_rgb=C_BORDER, lw=0.85)
        pdf.text(52, 730, f"{company_name} - Summary & Verification ({or_display})", font="F2", size=9.5, rgb=C_TEXT_SECOND)
        cursor_y = 708.0

    # 3.7 Payment & Tax Summary
    cursor_y -= 16.0
    pdf.text(52, cursor_y, "Payment & Tax Summary", font="F2", size=9.0, rgb=C_TEXT_PRIMARY)
    tax_label = str(receipt.get("tax_classification_label") or "VATable Sales (12% Inclusive)")
    pdf.text_right(560, cursor_y, tax_label, font="F1", size=7.8, rgb=C_TEXT_SECOND)
    cursor_y -= 6.0
    pdf.line(52, cursor_y, 560, cursor_y, rgb=C_BORDER, lw=0.6)

    tax_class = str(receipt.get("tax_classification") or "vatable")
    if tax_class == "vat_exempt":
        tax_rows = [
            ("VAT-Exempt Sales", f"{currency} {receipt.get('vat_exempt_sales_php', amount_php_str)}"),
            ("VAT Amount (0% Exempt)", f"{currency} 0.00"),
        ]
    elif tax_class == "zero_rated":
        tax_rows = [
            ("Zero-Rated Sales", f"{currency} {receipt.get('zero_rated_sales_php', amount_php_str)}"),
            ("VAT Amount (0% Zero-Rated)", f"{currency} 0.00"),
        ]
    elif tax_class == "non_vat":
        tax_rows = [
            ("Non-VAT Sales", f"{currency} {receipt.get('non_vat_sales_php', amount_php_str)}"),
            ("VAT Amount (Non-VAT)", f"{currency} 0.00"),
        ]
    else:
        tax_rows = [
            ("VATable Sales (Excl. VAT)", f"{currency} {receipt.get('vatable_sales_php', '0.00')}"),
            ("VAT Amount (12%)", f"{currency} {receipt.get('vat_amount_php', '0.00')}"),
        ]
    tax_rows.append(("Processing Fee", f"{currency} {receipt.get('gateway_fee_php', '0.00')}"))

    for r_label, r_val in tax_rows:
        cursor_y -= 14.0
        pdf.text(52, cursor_y, r_label, font="F1", size=8.0, rgb=C_TEXT_SECOND)
        pdf.text_right(560, cursor_y, r_val, font="F3", size=8.2, rgb=C_TEXT_PRIMARY)

    cursor_y -= 8.0
    pdf.line(52, cursor_y, 560, cursor_y, rgb=C_BORDER, lw=0.75)
    cursor_y -= 15.0
    pdf.text(52, cursor_y, "Total Amount Paid" if is_paid else "Total Amount", font="F2", size=9.5, rgb=C_TEXT_PRIMARY)
    pdf.text_right(560, cursor_y, formatted_total, font="F4", size=10.5, rgb=C_TEXT_PRIMARY)

    # 3.8 Compact Secondary Verification & Audit Details
    ver_box_top = cursor_y - 16.0
    ver_box_h = 84.0
    pdf.rounded_box(52, ver_box_top - ver_box_h, 508, ver_box_h, r_tl=R_PANEL, fill_rgb=C_SUBTLE, stroke_rgb=C_BORDER, lw=0.6)

    vy = ver_box_top - 13.0
    pdf.text(62, vy, "TREASURY & VERIFICATION RECORD", font="F2", size=7.8, rgb=C_TEXT_SECOND)
    pdf.text(
        62,
        vy - 13.0,
        f"Payment State: {payment_status}   |   Receipt State: {receipt.get('receipt_status', 'N/A')}",
        font="F1",
        size=7.4,
        rgb=C_TEXT_PRIMARY,
    )
    pdf.text(
        62,
        vy - 25.0,
        f"Ledger State: {receipt.get('ledger_status', 'UNSETTLED')}   |   Email State: {str((receipt.get('smtp_delivery') or {}).get('status', 'pending')).upper()}",
        font="F1",
        size=7.4,
        rgb=C_TEXT_SECOND,
    )
    pdf.text(
        62,
        vy - 37.0,
        f"Attempt ID: {receipt.get('payment_attempt_id', 'N/A')}   |   Provider ID: {provider_ref}",
        font="F3",
        size=7.2,
        rgb=C_TEXT_SECOND,
    )
    digest_val = str(receipt.get("verification_hash") or "")
    pdf.text(
        62,
        vy - 49.0,
        f"SHA-256 Digest (sha256:v2): {digest_val}",
        font="F3",
        size=6.8,
        rgb=C_TEXT_MUTED,
    )
    pdf.text(
        62,
        vy - 62.0,
        "Integrity digest verifies snapshot consistency. Pursuant to BIR RMC No. 77-2024.",
        font="F1",
        size=6.8,
        rgb=C_TEXT_MUTED,
    )

    # Stamp Page Numbers across all generated pages
    total_pages = len(pdf.pages)
    for idx, page_cmds in enumerate(pdf.pages, start=1):
        footer_text = _pdf_escape(f"{company_name}  |  TIN: {tin_str}  |  Page {idx} of {total_pages}")
        page_cmds.append(
            f"BT /F1 7.2 Tf {C_TEXT_MUTED[0]:.3f} {C_TEXT_MUTED[1]:.3f} {C_TEXT_MUTED[2]:.3f} rg 52.0 44.0 Td ({footer_text}) Tj ET"
        )

    # Assemble Multi-Page ISO-32000-1 PDF Objects
    objects: List[bytes] = []
    # Obj 1: Catalog, Obj 2: Pages, Obj 3..6: Fonts
    objects.append(b"1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n")
    page_obj_ids = [7 + (i * 2) for i in range(total_pages)]
    kids_ref = " ".join(f"{pid} 0 R" for pid in page_obj_ids)
    objects.append(f"2 0 obj\n<< /Type /Pages /Kids [{kids_ref}] /Count {total_pages} >>\nendobj\n".encode("latin-1"))
    objects.append(b"3 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n")
    objects.append(b"4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n")
    objects.append(b"5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>\nendobj\n")
    objects.append(b"6 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Courier-Bold /Encoding /WinAnsiEncoding >>\nendobj\n")

    for i, page_cmds in enumerate(pdf.pages):
        page_id = 7 + (i * 2)
        content_id = page_id + 1
        stream_bytes = "\n".join(page_cmds).encode("latin-1")
        objects.append(
            f"{page_id} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] "
            f"/Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R /F4 6 0 R >> >> "
            f"/Contents {content_id} 0 R >>\nendobj\n".encode("latin-1")
        )
        objects.append(
            f"{content_id} 0 obj\n<< /Length {len(stream_bytes)} >>\nstream\n".encode("latin-1")
            + stream_bytes
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