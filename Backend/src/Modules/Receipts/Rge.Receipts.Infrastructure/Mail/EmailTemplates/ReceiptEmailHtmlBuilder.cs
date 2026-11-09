using System.Globalization;
using System.Net;
using System.Text;
using Rge.Receipts.Domain;
using Rge.Receipts.Infrastructure.Pdf;

namespace Rge.Receipts.Infrastructure.Mail.EmailTemplates;

/// <summary>
/// Builds a responsive, email-client-compatible HTML email template mirroring
/// the BIR Official Receipt and Treasury Acknowledgement layout.
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
        string safeIssuedPst = Encode(vm.FormattedIssuedAtPst);
        string safeIssuedUtc = Encode(vm.FormattedIssuedAtUtc);
        string safePaymentMethod = Encode(vm.PaymentMethod);

        string safeCustomerName = Encode(vm.CustomerName);
        string safeCustomerEmail = Encode(vm.CustomerEmail);
        string safeOrderNumber = Encode(vm.OrderNumber);
        string safeOrderId = Encode(vm.OrderId);
        string safePaymentAttemptId = Encode(vm.PaymentAttemptId);

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
<body style="margin: 0; padding: 0; background-color: #f8fafc; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; -webkit-font-smoothing: antialiased;">
  <table role="presentation" cellpadding="0" cellspacing="0" width="100%" bgcolor="#f8fafc" style="border-collapse: collapse;">
    <tr>
      <td align="center" style="padding: 24px 12px;">
        <table role="presentation" cellpadding="0" cellspacing="0" width="640" style="max-width: 640px; width: 100%; background-color: #ffffff; border: 1px solid #e2e8f0; border-collapse: separate; border-spacing: 0; border-radius: 6px; overflow: hidden;">
          <!-- 1. Header Brand Bar -->
          <tr>
            <td bgcolor="#0f172a" style="background-color: #0f172a; padding: 16px 20px;">
              <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="border-collapse: collapse;">
                <tr>
                  <td style="color: #ffffff; font-size: 16px; font-weight: 700; line-height: 1.3;">
                    {{safeIssuerName}}
                  </td>
                </tr>
                <tr>
                  <td style="color: #94a3b8; font-size: 11px; padding-top: 4px; line-height: 1.4;">
                    VAT Reg. TIN: {{safeTin}} | BIR Permit No: {{safePermit}}
                  </td>
                </tr>
                <tr>
                  <td style="color: #94a3b8; font-size: 11px; padding-top: 2px; line-height: 1.4;">
                    {{safeIssuerAddress}}
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <!-- Main Content Container -->
          <tr>
            <td style="padding: 20px;">
              <p style="font-size: 13px; color: #1e293b; margin: 0 0 14px 0; line-height: 1.4;">
                Dear {{safeCustomerName}},
              </p>

              <!-- 2. Status Badge & Document Title -->
              <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="border-collapse: collapse; margin-bottom: 16px;">
                <tr>
                  <td valign="top" style="vertical-align: top;">
                    <div style="font-size: 16px; font-weight: 700; color: #0f172a; line-height: 1.3;">
                      {{safeDocumentTitle}}
                    </div>
                    <div style="font-size: 11px; color: #64748b; padding-top: 2px;">
                      {{safeDocumentType}}
                    </div>
                  </td>
                  <td align="right" valign="top" style="vertical-align: top;">
                    <table role="presentation" cellpadding="0" cellspacing="0" style="border-collapse: collapse;">
                      <tr>
                        <td style="background-color: #dcfce7; border: 1px solid #86efac; border-radius: 4px; padding: 5px 10px; color: #15803d; font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.5px; white-space: nowrap;">
                          {{safeStatusBadge}}
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>

              <!-- 3. Two-Column Metadata Cards -->
              <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="border-collapse: collapse; margin-bottom: 20px;">
                <tr>
                  <!-- Left Card: Receipt & Accounting Details -->
                  <td width="48%" valign="top" style="vertical-align: top; background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 4px; padding: 12px;">
                    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="border-collapse: collapse;">
                      <tr>
                        <td colspan="2" style="font-size: 11px; font-weight: 700; color: #334155; padding-bottom: 8px; border-bottom: 1px solid #e2e8f0; text-transform: uppercase;">
                          RECEIPT &amp; ACCOUNTING DETAILS
                        </td>
                      </tr>
                      <tr>
                        <td style="font-size: 11px; color: #64748b; padding-top: 6px; width: 45%;">Official Receipt No:</td>
                        <td style="font-size: 11px; font-weight: 700; color: #0f172a; padding-top: 6px; text-align: right;">{{safeOrNumber}}</td>
                      </tr>
                      <tr>
                        <td style="font-size: 11px; color: #64748b; padding-top: 4px;">Treasury Ack No:</td>
                        <td style="font-size: 11px; font-weight: 700; color: #0f172a; padding-top: 4px; text-align: right;">{{safeAckNumber}}</td>
                      </tr>
                      <tr>
                        <td style="font-size: 11px; color: #64748b; padding-top: 4px;">Date Issued (PST):</td>
                        <td style="font-size: 11px; font-weight: 600; color: #0f172a; padding-top: 4px; text-align: right;">{{safeIssuedPst}}</td>
                      </tr>
                      <tr>
                        <td style="font-size: 11px; color: #64748b; padding-top: 4px;">Date Issued (UTC):</td>
                        <td style="font-size: 11px; font-weight: 600; color: #0f172a; padding-top: 4px; text-align: right;">{{safeIssuedUtc}}</td>
                      </tr>
                      <tr>
                        <td style="font-size: 11px; color: #64748b; padding-top: 4px;">Payment Method:</td>
                        <td style="font-size: 11px; font-weight: 700; color: #0f172a; padding-top: 4px; text-align: right;">{{safePaymentMethod}}</td>
                      </tr>
                    </table>
                  </td>

                  <td width="4%">&nbsp;</td>

                  <!-- Right Card: Customer & Order Information -->
                  <td width="48%" valign="top" style="vertical-align: top; background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 4px; padding: 12px;">
                    <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="border-collapse: collapse;">
                      <tr>
                        <td colspan="2" style="font-size: 11px; font-weight: 700; color: #334155; padding-bottom: 8px; border-bottom: 1px solid #e2e8f0; text-transform: uppercase;">
                          CUSTOMER &amp; ORDER INFORMATION
                        </td>
                      </tr>
                      <tr>
                        <td style="font-size: 11px; color: #64748b; padding-top: 6px; width: 45%;">Received From:</td>
                        <td style="font-size: 11px; font-weight: 700; color: #0f172a; padding-top: 6px; text-align: right;">
                          {{safeCustomerName}}
                        </td>
                      </tr>
                      <tr>
                        <td style="font-size: 11px; color: #64748b; padding-top: 4px;">Customer Email:</td>
                        <td style="font-size: 11px; font-weight: 600; color: #0f172a; padding-top: 4px; text-align: right;">{{safeCustomerEmail}}</td>
                      </tr>
                      <tr>
                        <td style="font-size: 11px; color: #64748b; padding-top: 4px;">Order Number:</td>
                        <td style="font-size: 11px; font-weight: 700; color: #0f172a; padding-top: 4px; text-align: right;">{{safeOrderNumber}}</td>
                      </tr>
                      <tr>
                        <td style="font-size: 11px; color: #64748b; padding-top: 4px;">Order ID:</td>
                        <td style="font-size: 11px; font-weight: 600; color: #0f172a; padding-top: 4px; text-align: right;">{{safeOrderId}}</td>
                      </tr>
                      <tr>
                        <td style="font-size: 11px; color: #64748b; padding-top: 4px;">Payment Attempt ID:</td>
                        <td style="font-size: 11px; font-weight: 600; color: #0f172a; padding-top: 4px; text-align: right;">{{safePaymentAttemptId}}</td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>

              <!-- 4. Styled Line-Item Table -->
              <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="border-collapse: collapse; border: 1px solid #e2e8f0; margin-bottom: 16px;">
                <thead>
                  <tr style="background-color: #f1f5f9;">
                    <th align="left" style="padding: 8px 10px; font-size: 11px; font-weight: 700; color: #0f172a; border-bottom: 1px solid #e2e8f0;">Item Description</th>
                    <th align="center" style="padding: 8px 10px; font-size: 11px; font-weight: 700; color: #0f172a; border-bottom: 1px solid #e2e8f0; width: 45px;">Qty</th>
                    <th align="right" style="padding: 8px 10px; font-size: 11px; font-weight: 700; color: #0f172a; border-bottom: 1px solid #e2e8f0; width: 100px;">Unit Price</th>
                    <th align="right" style="padding: 8px 10px; font-size: 11px; font-weight: 700; color: #0f172a; border-bottom: 1px solid #e2e8f0; width: 100px;">Amount</th>
                    <th align="center" style="padding: 8px 10px; font-size: 11px; font-weight: 700; color: #0f172a; border-bottom: 1px solid #e2e8f0; width: 120px;">Tax Classification</th>
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
                  <tr>
                    <td align="left" style="padding: 8px 10px; font-size: 11px; color: #1e293b; border-bottom: 1px solid #e2e8f0;">{{safeDesc}}</td>
                    <td align="center" style="padding: 8px 10px; font-size: 11px; color: #1e293b; border-bottom: 1px solid #e2e8f0;">{{safeQty}}</td>
                    <td align="right" style="padding: 8px 10px; font-size: 11px; color: #1e293b; border-bottom: 1px solid #e2e8f0;">{{safeUnit}}</td>
                    <td align="right" style="padding: 8px 10px; font-size: 11px; font-weight: 600; color: #0f172a; border-bottom: 1px solid #e2e8f0;">{{safeAmt}}</td>
                    <td align="center" style="padding: 8px 10px; font-size: 11px; color: #64748b; border-bottom: 1px solid #e2e8f0;">{{safeTax}}</td>
                  </tr>
""");
        }

        sb.Append(CultureInfo.InvariantCulture, $$"""
                </tbody>
              </table>

              <!-- 5. Right-Aligned VAT Breakdown Summary Card -->
              <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="border-collapse: collapse; margin-bottom: 20px;">
                <tr>
                  <td>&nbsp;</td>
                  <td width="260" align="right" style="width: 260px;">
                    <table role="presentation" cellpadding="0" cellspacing="0" width="260" style="background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 4px; padding: 12px; border-collapse: collapse;">
                      <tr>
                        <td style="font-size: 11px; color: #475569; padding-bottom: 4px;">VATable Sales:</td>
                        <td align="right" style="font-size: 11px; color: #1e293b; padding-bottom: 4px; text-align: right;">{{safeVatableSales}}</td>
                      </tr>
                      <tr>
                        <td style="font-size: 11px; color: #475569; padding-bottom: 4px;">12% Value-Added Tax:</td>
                        <td align="right" style="font-size: 11px; color: #1e293b; padding-bottom: 4px; text-align: right;">{{safeVatAmount}}</td>
                      </tr>
                      <tr>
                        <td style="font-size: 11px; color: #475569; padding-bottom: 4px;">VAT-Exempt Sales:</td>
                        <td align="right" style="font-size: 11px; color: #1e293b; padding-bottom: 4px; text-align: right;">{{safeExemptSales}}</td>
                      </tr>
                      <tr>
                        <td style="font-size: 11px; color: #475569; padding-bottom: 6px;">Zero-Rated Sales:</td>
                        <td align="right" style="font-size: 11px; color: #1e293b; padding-bottom: 6px; text-align: right;">{{safeZeroRatedSales}}</td>
                      </tr>
                      <tr>
                        <td colspan="2" style="border-top: 1px solid #cbd5e1; padding-top: 6px;"></td>
                      </tr>
                      <tr>
                        <td style="font-size: 12px; font-weight: 700; color: #0f172a;">TOTAL AMOUNT PAID:</td>
                        <td align="right" style="font-size: 13px; font-weight: 700; color: #0f172a; text-align: right;">{{safeGrossAmount}}</td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>

              <!-- 6. Shaded Cryptographic SHA-256 Digest Footer -->
              <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="background-color: #f1f5f9; border: 1px solid #e2e8f0; border-radius: 4px; padding: 12px; border-collapse: collapse;">
                <tr>
                  <td style="font-size: 11px; color: #475569; font-weight: 700; width: 90px; vertical-align: top;">Audit Digest:</td>
                  <td style="font-family: Consolas, 'Courier New', monospace; font-size: 10.5px; color: #0f172a; word-break: break-all; line-height: 1.4;">
                    {{safeAuditDigest}}
                  </td>
                </tr>
                <tr>
                  <td colspan="2" style="font-size: 10px; color: #64748b; padding-top: 8px; line-height: 1.4;">
                    {{safeLegalNote}} &bull; Official BIR-compliant electronic receipt PDF is attached (<code>OfficialReceipt-{{safeOrNumber}}.pdf</code>).
                  </td>
                </tr>
              </table>
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
