using System.Globalization;
using System.Net;
using System.Text;
using Rge.Receipts.Domain;
using Rge.Receipts.Infrastructure.Pdf;

namespace Rge.Receipts.Infrastructure.Mail.EmailTemplates;

/// <summary>
/// Builds a responsive, email-client-compatible HTML email template implementing
/// the "Quiet Financial Precision" minimalist corporate aesthetic.
/// Compliant with BIR RMC No. 77-2024 and RA 11976 (Ease of Paying Taxes Act).
/// All dynamic values are strictly HTML-encoded to protect against XSS injection.
/// </summary>
public static class ReceiptEmailHtmlBuilder
{
    public static string BuildReceiptHtml(OfficialReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return BuildReceiptHtml(ReceiptLayout.ToViewModel(receipt));
    }

    public static string BuildReceiptHtml(ReceiptDocumentViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);

        string safeIssuerName = Encode(vm.IssuerRegisteredName);
        string safeTin = Encode(vm.IssuerTin);
        string safePermit = Encode(vm.BirPermitNumber);
        string safeIssuerAddress = Encode(vm.IssuerAddress);

        string safeDocumentTitle = Encode(vm.DocumentTitle);
        string safeDocumentType = Encode(vm.DocumentType);
        string safeStatusBadge = Encode(vm.StatusBadge);

        string safeOrNumber = Encode(vm.OfficialReceiptNumber);
        string safeAckNumber = Encode(vm.TreasuryAcknowledgementNumber);
        string safePaymentDate = Encode(vm.FormattedPaymentDate);
        string safePaymentTime = Encode(vm.FormattedPaymentTime);
        string safeTimeZone = Encode(vm.FormattedTimeZoneLabel);
        string safeCustomerDateTime = Encode(vm.FormattedCustomerDateTime);
        string safeReceiptRef = Encode(vm.CustomerReceiptReference);
        string safePaymentMethod = Encode(vm.PaymentMethod);

        string safeCustomerName = Encode(vm.CustomerName);
        string safeCustomerEmail = Encode(vm.CustomerEmail);
        string safeOrderNumber = Encode(vm.OrderNumber);

        string safeVatableSales = Encode(vm.FormattedVatableSales);
        string safeVatAmount = Encode(vm.FormattedVatAmount);
        string safeExemptSales = Encode(vm.FormattedVatExemptSales);
        string safeZeroRatedSales = Encode(vm.FormattedZeroRatedSales);
        string safeGrossAmount = Encode(vm.FormattedGrossAmount);

        string safeAuditDigest = Encode(vm.AuditDigest);
        string safeLegalNote = Encode(vm.LegalComplianceNote);

        var sb = new StringBuilder(4096);

        sb.Append(CultureInfo.InvariantCulture, $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <meta http-equiv="X-UA-Compatible" content="IE=edge">
  <title>{{safeDocumentTitle}} - {{safeOrNumber}}</title>
</head>
<body style="margin: 0; padding: 0; background-color: #F6F6F4; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; -webkit-font-smoothing: antialiased; color: #0A0A0A;">
  <table role="presentation" cellpadding="0" cellspacing="0" width="100%" bgcolor="#F6F6F4" style="border-collapse: collapse;">
    <tr>
      <td align="center" style="padding: 32px 12px;">
        <table role="presentation" cellpadding="0" cellspacing="0" width="600" style="max-width: 600px; width: 100%; background-color: #FFFFFF; border: 1px solid #E5E7EB; border-collapse: separate; border-spacing: 0; border-radius: 8px; overflow: hidden; box-shadow: 0 1px 3px rgba(0,0,0,0.04);">
          
          <!-- 1. Firm Brand Header (Clean White Surface, Quiet Precision) -->
          <tr>
            <td style="padding: 28px 28px 20px 28px; border-bottom: 1px solid #E5E7EB;">
              <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="border-collapse: collapse;">
                <tr>
                  <td valign="top" style="vertical-align: top;">
                    <div style="font-size: 16px; font-weight: 800; color: #0A0A0A; letter-spacing: 0.5px; line-height: 1.2;">
                      R.G.E.
                    </div>
                    <div style="font-size: 13px; font-weight: 700; color: #0A0A0A; letter-spacing: 0.25px; padding-top: 1px; line-height: 1.3;">
                      ACCOUNTING FIRM INC.
                    </div>
                    <div style="font-size: 10px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px; color: #71717A; padding-top: 4px;">
                      Corporate Accounting &amp; Treasury
                    </div>
                    <div style="font-size: 10px; color: #71717A; padding-top: 4px; line-height: 1.4;">
                      VAT Reg. TIN: {{safeTin}} &bull; BIR Permit No: {{safePermit}}
                    </div>
                    <div style="font-size: 10px; color: #71717A; padding-top: 2px; line-height: 1.4;">
                      {{safeIssuerAddress}}
                    </div>
                  </td>
                  <td align="right" valign="top" style="vertical-align: top;">
                    <!-- Emerald Payment Confirmation Pill -->
                    <table role="presentation" cellpadding="0" cellspacing="0" style="border-collapse: collapse;">
                      <tr>
                        <td style="background-color: #dcfce7; border: 1px solid #86efac; border-radius: 9999px; padding: 4px 10px; color: #15803d; font-size: 10px; font-weight: 700; letter-spacing: 0.5px; text-transform: uppercase; white-space: nowrap;">
                          <span style="display: inline-block; width: 6px; height: 6px; background-color: #15803d; border-radius: 50%; vertical-align: middle; margin-right: 5px;"></span>{{safeStatusBadge}}
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <!-- 2. Hero Payment Section -->
          <tr>
            <td style="padding: 24px 28px 20px 28px; border-bottom: 1px solid #E5E7EB;">
              <div style="font-size: 10.5px; font-weight: 700; text-transform: uppercase; letter-spacing: 1.5px; color: #71717A; margin-bottom: 6px;">
                {{safeDocumentTitle}}
              </div>
              <div style="font-size: 38px; font-weight: 800; color: #0A0A0A; letter-spacing: -0.5px; line-height: 1.1; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, monospace;">
                {{safeGrossAmount}}
              </div>
              <div style="font-size: 12px; color: #52525B; padding-top: 6px; line-height: 1.4;">
                Payment successfully confirmed &bull; Issued: {{safeCustomerDateTime}}
              </div>
            </td>
          </tr>

          <!-- 3. Reference Summary Panel (Single Muted Container) -->
          <tr>
            <td style="padding: 20px 28px; border-bottom: 1px solid #E5E7EB;">
              <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background-color: #FAFAFA; border: 1px solid #E5E7EB; border-radius: 6px; border-collapse: collapse;">
                <tr>
                  <td width="33.3%" style="padding: 12px 14px; border-right: 1px solid #E5E7EB; vertical-align: top;">
                    <div style="font-size: 10px; text-transform: uppercase; letter-spacing: 0.5px; color: #71717A; font-weight: 600;">
                      Official Receipt No.
                    </div>
                    <div style="font-size: 12px; font-weight: 700; font-family: Consolas, 'Courier New', monospace; color: #0A0A0A; padding-top: 3px;">
                      {{safeOrNumber}}
                    </div>
                  </td>
                  <td width="33.3%" style="padding: 12px 14px; border-right: 1px solid #E5E7EB; vertical-align: top;">
                    <div style="font-size: 10px; text-transform: uppercase; letter-spacing: 0.5px; color: #71717A; font-weight: 600;">
                      Acknowledgement No.
                    </div>
                    <div style="font-size: 12px; font-weight: 700; font-family: Consolas, 'Courier New', monospace; color: #0A0A0A; padding-top: 3px;">
                      {{safeAckNumber}}
                    </div>
                  </td>
                  <td width="33.3%" style="padding: 12px 14px; vertical-align: top;">
                    <div style="font-size: 10px; text-transform: uppercase; letter-spacing: 0.5px; color: #71717A; font-weight: 600;">
                      Order Reference
                    </div>
                    <div style="font-size: 12px; font-weight: 700; font-family: Consolas, 'Courier New', monospace; color: #0A0A0A; padding-top: 3px;">
                      {{safeOrderNumber}}
                    </div>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <!-- 4. Payer & Payment Details Grid -->
          <tr>
            <td style="padding: 20px 28px; border-bottom: 1px solid #E5E7EB;">
              <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="border-collapse: collapse;">
                <tr>
                  <td width="50%" valign="top" style="vertical-align: top; padding-right: 14px;">
                    <div style="font-size: 10.5px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.75px; color: #0A0A0A; margin-bottom: 8px;">
                      Customer Information
                    </div>
                    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="border-collapse: collapse;">
                      <tr>
                        <td style="font-size: 11px; color: #71717A; padding: 3px 0;">Customer Name:</td>
                        <td align="right" style="font-size: 12px; font-weight: 600; color: #0A0A0A; padding: 3px 0;">
                          <span style="display: inline-block; width: 6px; height: 6px; background-color: #E11D48; border-radius: 50%; vertical-align: middle; margin-right: 4px;"></span>
                          {{safeCustomerName}}
                        </td>
                      </tr>
                      <tr>
                        <td style="font-size: 11px; color: #71717A; padding: 3px 0;">Customer Email:</td>
                        <td align="right" style="font-size: 11px; color: #0A0A0A; padding: 3px 0;">{{safeCustomerEmail}}</td>
                      </tr>
                      <tr>
                        <td style="font-size: 11px; color: #71717A; padding: 3px 0;">Order Reference:</td>
                        <td align="right" style="font-size: 11px; font-family: Consolas, monospace; color: #52525B; padding: 3px 0;">{{safeOrderNumber}}</td>
                      </tr>
                    </table>
                  </td>
                  <td width="50%" valign="top" style="vertical-align: top; padding-left: 14px; border-left: 1px solid #E5E7EB;">
                    <div style="font-size: 10.5px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.75px; color: #0A0A0A; margin-bottom: 8px;">
                      Payment Details
                    </div>
                    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="border-collapse: collapse;">
                      <tr>
                        <td style="font-size: 11px; color: #71717A; padding: 3px 0;">Payment Method:</td>
                        <td align="right" style="font-size: 12px; font-weight: 700; color: #0A0A0A; padding: 3px 0;">{{safePaymentMethod}}</td>
                      </tr>
                      <tr>
                        <td style="font-size: 11px; color: #71717A; padding: 3px 0;">Payment Provider:</td>
                        <td align="right" style="font-size: 11px; font-weight: 600; color: #0A0A0A; padding: 3px 0;">PayMongo</td>
                      </tr>
                      <tr>
                        <td style="font-size: 11px; color: #71717A; padding: 3px 0;">Payment Date &amp; Time:</td>
                        <td align="right" style="font-size: 11px; color: #0A0A0A; padding: 3px 0;">{{safePaymentDate}}, {{safePaymentTime}} ({{safeTimeZone}})</td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <!-- 5. Payment Particulars Table (Clean Minimal Table with Subtle Horizontal Dividers) -->
          <tr>
            <td style="padding: 20px 28px 12px 28px;">
              <div style="font-size: 10.5px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.75px; color: #0A0A0A; margin-bottom: 10px;">
                Payment Particulars
              </div>
              <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="border-collapse: collapse;">
                <thead>
                  <tr style="border-bottom: 1px solid #0A0A0A;">
                    <th align="left" style="padding: 6px 0; font-size: 10.5px; font-weight: 700; color: #0A0A0A; text-transform: uppercase; letter-spacing: 0.5px;">Description</th>
                    <th align="center" style="padding: 6px 8px; font-size: 10.5px; font-weight: 700; color: #0A0A0A; text-transform: uppercase; letter-spacing: 0.5px; width: 40px;">Qty</th>
                    <th align="right" style="padding: 6px 8px; font-size: 10.5px; font-weight: 700; color: #0A0A0A; text-transform: uppercase; letter-spacing: 0.5px; width: 95px;">Unit Price</th>
                    <th align="right" style="padding: 6px 8px; font-size: 10.5px; font-weight: 700; color: #0A0A0A; text-transform: uppercase; letter-spacing: 0.5px; width: 95px;">Amount</th>
                    <th align="right" style="padding: 6px 0; font-size: 10.5px; font-weight: 700; color: #0A0A0A; text-transform: uppercase; letter-spacing: 0.5px; width: 110px;">Tax Classification</th>
                  </tr>
                </thead>
                <tbody>
""");

        foreach (var item in vm.LineItems)
        {
            string safeDesc = Encode(item.Description);
            string safeQty = item.Quantity.ToString(CultureInfo.InvariantCulture);
            string safeUnit = Encode(item.UnitPrice);
            string safeAmt = Encode(item.Amount);
            string safeTax = Encode(item.TaxClassification);

            sb.Append(CultureInfo.InvariantCulture, $$"""
                  <tr style="border-bottom: 1px solid #E5E7EB;">
                    <td align="left" style="padding: 10px 0; font-size: 12px; color: #0A0A0A;">{{safeDesc}}</td>
                    <td align="center" style="padding: 10px 8px; font-size: 12px; color: #52525B;">{{safeQty}}</td>
                    <td align="right" style="padding: 10px 8px; font-size: 12px; color: #52525B;">{{safeUnit}}</td>
                    <td align="right" style="padding: 10px 8px; font-size: 12px; font-weight: 700; color: #0A0A0A;">{{safeAmt}}</td>
                    <td align="right" style="padding: 10px 0; font-size: 11px; color: #71717A;">{{safeTax}}</td>
                  </tr>
""");
        }

        sb.Append(CultureInfo.InvariantCulture, $$"""
                </tbody>
              </table>
            </td>
          </tr>

          <!-- 6. Financial Breakdown Summary -->
          <tr>
            <td style="padding: 12px 28px 24px 28px; border-bottom: 1px solid #E5E7EB;">
              <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="border-collapse: collapse;">
                <tr>
                  <td>&nbsp;</td>
                  <td width="260" align="right" style="width: 260px;">
                    <table role="presentation" cellpadding="0" cellspacing="0" width="260" style="border-collapse: collapse;">
                      <tr>
                        <td style="font-size: 11px; color: #71717A; padding: 3px 0;">VATable Sales:</td>
                        <td align="right" style="font-size: 11.5px; color: #0A0A0A; padding: 3px 0; text-align: right;">{{safeVatableSales}}</td>
                      </tr>
                      <tr>
                        <td style="font-size: 11px; color: #71717A; padding: 3px 0;">12% Value-Added Tax:</td>
                        <td align="right" style="font-size: 11.5px; color: #0A0A0A; padding: 3px 0; text-align: right;">{{safeVatAmount}}</td>
                      </tr>
                      <tr>
                        <td style="font-size: 11px; color: #71717A; padding: 3px 0;">VAT-Exempt Sales:</td>
                        <td align="right" style="font-size: 11.5px; color: #0A0A0A; padding: 3px 0; text-align: right;">{{safeExemptSales}}</td>
                      </tr>
                      <tr>
                        <td style="font-size: 11px; color: #71717A; padding: 3px 0;">Zero-Rated Sales:</td>
                        <td align="right" style="font-size: 11.5px; color: #0A0A0A; padding: 3px 0; text-align: right;">{{safeZeroRatedSales}}</td>
                      </tr>
                      <tr>
                        <td style="font-size: 11px; color: #71717A; padding: 3px 0;">Processing Fee:</td>
                        <td align="right" style="font-size: 11.5px; color: #0A0A0A; padding: 3px 0; text-align: right;">&#8369;0.00</td>
                      </tr>
                      <tr>
                        <td colspan="2" style="border-top: 1px solid #0A0A0A; padding-top: 8px; margin-top: 4px;"></td>
                      </tr>
                      <tr>
                        <td style="font-size: 12px; font-weight: 700; color: #0A0A0A; text-transform: uppercase;">TOTAL AMOUNT PAID:</td>
                        <td align="right" style="font-size: 16px; font-weight: 800; color: #0A0A0A; text-align: right; font-family: monospace, sans-serif;">{{safeGrossAmount}}</td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <!-- 7. Authentication, Verification & Confirmation Guidance -->
          <tr>
            <td style="padding: 20px 28px 24px 28px;">
              <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background-color: #FAFAFA; border: 1px solid #E5E7EB; border-radius: 6px; padding: 16px 18px; margin-bottom: 16px;">
                <tr>
                  <td>
                    <div style="font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.5px; color: #15803d; margin-bottom: 4px;">
                      Payment Confirmed
                    </div>
                    <div style="font-size: 12px; color: #0A0A0A; line-height: 1.4;">
                      Your payment has been successfully recorded. A formal, printable PDF official receipt is attached to this email for your accounting and tax records.
                    </div>
                    <div style="margin-top: 12px; padding-top: 10px; border-top: 1px solid #E5E7EB;">
                      <span style="font-size: 10.5px; font-weight: 600; text-transform: uppercase; color: #71717A; letter-spacing: 0.5px;">Receipt Reference:</span>
                      <span style="font-size: 12px; font-weight: 700; font-family: Consolas, monospace; color: #0A0A0A; margin-left: 6px;">{{safeReceiptRef}}</span>
                      <div style="font-size: 10px; color: #71717A; margin-top: 2px;">Keep this receipt reference for future inquiries or treasury verification.</div>
                    </div>
                  </td>
                </tr>
              </table>

              <div style="font-size: 10px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.5px; color: #71717A; margin-bottom: 4px;">
                Authentication &amp; Verification
              </div>
              <div style="font-size: 10px; color: #71717A; margin-bottom: 2px;">
                Audit Digest:
              </div>
              <div style="font-family: Consolas, 'Courier New', monospace; font-size: 10px; color: #0A0A0A; word-break: break-all; background-color: #FAFAFA; border: 1px solid #E5E7EB; border-radius: 4px; padding: 8px 10px; line-height: 1.4;">
                {{safeAuditDigest}}
              </div>
              <div style="font-size: 10px; color: #71717A; padding-top: 8px; line-height: 1.4;">
                {{safeLegalNote}} &bull; Certified electronic issuance under RA 11976 (Ease of Paying Taxes Act).
              </div>
            </td>
          </tr>

          <!-- 8. Footer (Restrained Corporate) -->
          <tr>
            <td style="background-color: #FAFAFA; border-top: 1px solid #E5E7EB; padding: 18px 28px; text-align: center;">
              <div style="font-size: 11px; font-weight: 700; color: #0A0A0A;">
                {{safeIssuerName}}
              </div>
              <div style="font-size: 10px; color: #71717A; padding-top: 3px; line-height: 1.4;">
                {{safeIssuerAddress}} &bull; VAT Reg. TIN: {{safeTin}} &bull; BIR Permit No: {{safePermit}}
              </div>
              <div style="font-size: 10px; color: #94A3B8; padding-top: 6px;">
                This automated financial record was issued upon settlement confirmation.
              </div>
            </td>
          </tr>

        </table>
      </td>
    </tr>
  </table>
</body>
</html>
""");

        return sb.ToString();
    }

    private static string Encode(string? value) =>
        WebUtility.HtmlEncode(value ?? string.Empty);
}
