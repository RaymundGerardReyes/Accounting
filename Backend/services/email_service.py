"""
Backend/services/email_service.py
Google Email SMTP Notification Service supporting STARTTLS, credential verification,
live email dispatch, and responsive HTML transactional email templates.
"""

import os
import smtplib
import ssl
import time
from email.mime.multipart import MIMEMultipart
from email.mime.text import MIMEText
from typing import Dict, Any, Optional, List


class EmailService:
    def __init__(self):
        self.host = os.getenv("SMTP_HOST", "smtp.gmail.com").strip()
        self.port = int(os.getenv("SMTP_PORT", "587"))
        self.user = os.getenv("SMTP_USER", "").strip()
        self.password = os.getenv("SMTP_APP_PASSWORD", "").strip() or os.getenv("SMTP_PASSWORD", "").strip()
        self.sender_name = os.getenv("SMTP_SENDER_NAME", "Acme Accounting & Treasury").strip()
        self.use_tls = os.getenv("SMTP_USE_TLS", "true").lower() in ("true", "1", "yes")

    def is_configured(self) -> bool:
        return bool(self.user and self.password)

    def test_connection(
        self,
        host: Optional[str] = None,
        port: Optional[int] = None,
        user: Optional[str] = None,
        password: Optional[str] = None,
        use_tls: Optional[bool] = None,
    ) -> Dict[str, Any]:
        """
        Tests the SMTP server handshake and authentication without sending an email.
        Measures round-trip latency in milliseconds.
        """
        target_host = (host or self.host).strip()
        target_port = port if port is not None else self.port
        target_user = (user or self.user).strip()
        target_pass = (password or self.password).strip()
        tls_flag = use_tls if use_tls is not None else self.use_tls

        if not target_user or not target_pass:
            return {
                "success": False,
                "status": "unconfigured",
                "message": "SMTP username or App Password is not provided.",
                "latency_ms": 0,
            }

        start_time = time.perf_counter()
        server = None
        try:
            context = ssl.create_default_context()
            if target_port == 465 or not tls_flag:
                server = smtplib.SMTP_SSL(target_host, target_port, timeout=8.0, context=context)
            else:
                server = smtplib.SMTP(target_host, target_port, timeout=8.0)
                server.ehlo()
                server.starttls(context=context)
                server.ehlo()

            server.login(target_user, target_pass)
            latency_ms = round((time.perf_counter() - start_time) * 1000, 2)
            server.quit()
            return {
                "success": True,
                "status": "connected",
                "message": f"Successfully authenticated with {target_host}:{target_port}",
                "latency_ms": latency_ms,
                "host": target_host,
                "port": target_port,
                "user": target_user,
            }
        except smtplib.SMTPAuthenticationError as e:
            latency_ms = round((time.perf_counter() - start_time) * 1000, 2)
            return {
                "success": False,
                "status": "authentication_failed",
                "message": "Authentication failed. For Google Gmail, generate a 16-character App Password at myaccount.google.com/apppasswords.",
                "details": str(e),
                "latency_ms": latency_ms,
            }
        except Exception as e:
            latency_ms = round((time.perf_counter() - start_time) * 1000, 2)
            return {
                "success": False,
                "status": "connection_error",
                "message": f"Failed to connect to SMTP server: {str(e)}",
                "details": str(e),
                "latency_ms": latency_ms,
            }
        finally:
            if server:
                try:
                    server.close()
                except Exception:
                    pass

    def send_email(
        self,
        to_email: str,
        subject: str,
        html_body: str,
        text_body: Optional[str] = None,
        cc: Optional[List[str]] = None,
    ) -> Dict[str, Any]:
        """
        Dispatches an email through the configured SMTP server.
        """
        if not self.is_configured():
            return {
                "success": False,
                "status": "unconfigured",
                "message": "SMTP service is not configured. Set SMTP_USER and SMTP_APP_PASSWORD in Backend/.env.",
            }

        start_time = time.perf_counter()
        server = None
        try:
            msg = MIMEMultipart("alternative")
            msg["Subject"] = subject
            msg["From"] = f"{self.sender_name} <{self.user}>"
            msg["To"] = to_email
            recipients = [to_email]

            if cc:
                msg["Cc"] = ", ".join(cc)
                recipients.extend(cc)

            plain_text = text_body or "Please view this email in an HTML-compatible client."
            msg.attach(MIMEText(plain_text, "plain", "utf-8"))
            msg.attach(MIMEText(html_body, "html", "utf-8"))

            context = ssl.create_default_context()
            if self.port == 465 or not self.use_tls:
                server = smtplib.SMTP_SSL(self.host, self.port, timeout=10.0, context=context)
            else:
                server = smtplib.SMTP(self.host, self.port, timeout=10.0)
                server.ehlo()
                server.starttls(context=context)
                server.ehlo()

            server.login(self.user, self.password)
            server.sendmail(self.user, recipients, msg.as_string())
            latency_ms = round((time.perf_counter() - start_time) * 1000, 2)
            server.quit()

            return {
                "success": True,
                "status": "sent",
                "message": f"Email successfully dispatched to {to_email}",
                "latency_ms": latency_ms,
                "recipient": to_email,
            }
        except Exception as e:
            latency_ms = round((time.perf_counter() - start_time) * 1000, 2)
            return {
                "success": False,
                "status": "send_failed",
                "message": f"Failed to dispatch email: {str(e)}",
                "details": str(e),
                "latency_ms": latency_ms,
            }
        finally:
            if server:
                try:
                    server.close()
                except Exception:
                    pass

    # =========================================================================
    # Pre-built Responsive HTML Email Templates
    # =========================================================================

    def render_payment_receipt_html(
        self,
        order_number: str,
        title: str,
        amount_php: str,
        payment_method: str,
        paid_at: str,
        reference_id: str,
    ) -> str:
        return f"""<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <style>
    body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f8f9fa; margin: 0; padding: 24px; color: #18181b; }}
    .card {{ max-width: 560px; margin: 0 auto; background: #ffffff; border-radius: 8px; border: 1px solid #e4e4e7; padding: 32px; }}
    .header {{ border-bottom: 2px solid #18181b; padding-bottom: 16px; margin-bottom: 24px; }}
    .title {{ font-size: 20px; font-weight: 700; margin: 0; }}
    .subtitle {{ font-size: 13px; color: #71717a; margin-top: 4px; }}
    .amount-box {{ background: #f4f4f5; border-radius: 6px; padding: 16px; text-align: center; margin: 24px 0; }}
    .amount-val {{ font-size: 28px; font-weight: 800; font-family: monospace; color: #09090b; }}
    .details-table {{ width: 100%; border-collapse: collapse; margin-bottom: 24px; }}
    .details-table td {{ padding: 8px 0; font-size: 13px; border-bottom: 1px solid #f4f4f5; }}
    .details-table td.label {{ color: #71717a; width: 40%; }}
    .details-table td.value {{ font-weight: 600; text-align: right; }}
    .footer {{ font-size: 11px; color: #a1a1aa; text-align: center; border-top: 1px solid #f4f4f5; padding-top: 16px; }}
    .badge {{ display: inline-block; padding: 2px 8px; font-size: 11px; font-weight: 600; border-radius: 4px; background: #e0f2fe; color: #0369a1; }}
  </style>
</head>
<body>
  <div class="card">
    <div class="header">
      <div class="title">Payment Receipt & Confirmation</div>
      <div class="subtitle">Acme Cost Accounting & Live Payment Platform</div>
    </div>
    <div class="amount-box">
      <div style="font-size: 12px; color: #71717a; text-transform: uppercase; letter-spacing: 0.05em;">Total Amount Paid</div>
      <div class="amount-val">₱{amount_php}</div>
      <div style="margin-top: 6px;"><span class="badge">Payment Succeeded</span></div>
    </div>
    <table class="details-table">
      <tr><td class="label">Order / Invoice</td><td class="value">{order_number}</td></tr>
      <tr><td class="label">Description</td><td class="value">{title}</td></tr>
      <tr><td class="label">Payment Channel</td><td class="value">{payment_method.upper()}</td></tr>
      <tr><td class="label">Payment Date</td><td class="value">{paid_at}</td></tr>
      <tr><td class="label">Transaction Reference</td><td class="value" style="font-family: monospace;">{reference_id}</td></tr>
    </table>
    <div class="footer">
      This is an automated receipt generated by Acme Accounting & Treasury.<br>
      Live Payment Rail: PayMongo Hosted Checkout.
    </div>
  </div>
</body>
</html>"""

    def render_advance_notification_html(
        self,
        advance_number: str,
        employee_name: str,
        amount_php: str,
        purpose: str,
        status: str,
    ) -> str:
        return f"""<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <style>
    body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f8f9fa; margin: 0; padding: 24px; color: #18181b; }}
    .card {{ max-width: 560px; margin: 0 auto; background: #ffffff; border-radius: 8px; border: 1px solid #e4e4e7; padding: 32px; }}
    .header {{ border-bottom: 2px solid #18181b; padding-bottom: 16px; margin-bottom: 24px; }}
    .title {{ font-size: 20px; font-weight: 700; margin: 0; }}
    .details-table {{ width: 100%; border-collapse: collapse; margin-bottom: 24px; }}
    .details-table td {{ padding: 8px 0; font-size: 13px; border-bottom: 1px solid #f4f4f5; }}
    .details-table td.label {{ color: #71717a; width: 40%; }}
    .details-table td.value {{ font-weight: 600; text-align: right; }}
    .footer {{ font-size: 11px; color: #a1a1aa; text-align: center; border-top: 1px solid #f4f4f5; padding-top: 16px; }}
  </style>
</head>
<body>
  <div class="card">
    <div class="header">
      <div class="title">Cash Advance Status Update</div>
      <div style="font-size: 13px; color: #71717a; margin-top: 4px;">CPA-Audited Finance Operations</div>
    </div>
    <table class="details-table">
      <tr><td class="label">Advance Number</td><td class="value">{advance_number}</td></tr>
      <tr><td class="label">Employee</td><td class="value">{employee_name}</td></tr>
      <tr><td class="label">Amount</td><td class="value">₱{amount_php}</td></tr>
      <tr><td class="label">Purpose</td><td class="value">{purpose}</td></tr>
      <tr><td class="label">Current Status</td><td class="value" style="text-transform: uppercase;">{status}</td></tr>
    </table>
    <div class="footer">Acme Corporate Treasury &middot; Disbursed with verified bank evidence.</div>
  </div>
</body>
</html>"""

