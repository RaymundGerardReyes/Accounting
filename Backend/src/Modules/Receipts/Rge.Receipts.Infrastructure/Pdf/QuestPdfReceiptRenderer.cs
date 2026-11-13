using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Rge.Receipts.Application.Ports;
using Rge.Receipts.Domain;

namespace Rge.Receipts.Infrastructure.Pdf;

public sealed class QuestPdfReceiptRenderer : IPdfRenderer
{
    private const int MaxCacheEntries = 1024;
    private static readonly byte[] Pdf14HeaderBytes = Encoding.ASCII.GetBytes("%PDF-1.4");

    private readonly ConcurrentDictionary<string, byte[]> _pdfCache = new(StringComparer.Ordinal);

    static QuestPdfReceiptRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.EnableDebugging = false;

        // Pre-warm QuestPDF typography pipeline and Skia native caches
        try
        {
            var warmupDoc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.DefaultTextStyle(x => x.FontFamily(Fonts.Arial));
                    page.Content().Text(x =>
                    {
                        x.Span("Warmup").FontFamily(Fonts.Arial);
                        x.Span("sha256:v2:").FontFamily(Fonts.CourierNew);
                    });
                });
            });
            _ = warmupDoc.GeneratePdf();
        }
        catch
        {
            // Fallback for environments where warmup is skipped
        }
    }

    public byte[] RenderOfficialReceiptPdf(OfficialReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        if (_pdfCache.TryGetValue(receipt.Id, out byte[]? cachedBytes))
        {
            return cachedBytes.ToArray();
        }

        if (_pdfCache.Count >= MaxCacheEntries)
        {
            _pdfCache.Clear();
        }

        byte[] generated = _pdfCache.GetOrAdd(receipt.Id, _ => GeneratePdfBytes(receipt));
        return generated.ToArray();
    }

    private static byte[] GeneratePdfBytes(OfficialReceipt receipt)
    {
        ReceiptDocumentViewModel vm = ReceiptLayout.ToViewModel(receipt);

        Document document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(ReceiptDesignTokens.PageMargin);
                page.PageColor(ReceiptDesignTokens.PageBackground);
                page.DefaultTextStyle(style => style
                    .FontFamily(ReceiptDesignTokens.PrimaryFontFamily)
                    .FontSize(ReceiptDesignTokens.BodySize)
                    .FontColor(ReceiptDesignTokens.PrimaryText));

                page.Header().Element(header => ComposeHeader(header, vm));
                page.Content().Element(content => ComposeContent(content, vm));
                page.Footer().Element(footer => ComposeFooter(footer, vm));
            });
        });

        byte[] pdfBytes = document.GeneratePdf();
        NormalizePdf14Header(pdfBytes);
        return pdfBytes;
    }

    /// <summary>
    /// ZONE 01 — CORPORATE IDENTITY
    /// Editorial corporate header with dominant company identity on the left and a restrained
    /// emerald payment confirmation pill on the right, separated by a thin neutral divider.
    /// </summary>
    private static void ComposeHeader(IContainer container, ReceiptDocumentViewModel vm)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(brand =>
                {
                    brand.Item().Text(vm.IssuerRegisteredName)
                        .FontSize(ReceiptDesignTokens.CompanyNameSize)
                        .Bold()
                        .FontColor(ReceiptDesignTokens.PrimaryText);

                    brand.Item().PaddingTop(2f).Text(vm.IssuerDepartment)
                        .FontSize(ReceiptDesignTokens.DepartmentSize)
                        .SemiBold()
                        .FontColor(ReceiptDesignTokens.SecondaryText);

                    brand.Item().PaddingTop(ReceiptDesignTokens.SpaceCompact).Text(text =>
                    {
                        text.DefaultTextStyle(s => s
                            .FontSize(ReceiptDesignTokens.IssuerMetadataSize)
                            .FontColor(ReceiptDesignTokens.SecondaryText));

                        text.Span($"TIN: {vm.IssuerTin}");
                        text.Span("  ·  ");
                        text.Span($"BIR Permit: {vm.BirPermitNumber}");
                        text.Span("  ·  ");
                        text.Span(vm.IssuerAddress);
                    });
                });

                row.AutoItem().AlignRight().AlignTop().PaddingLeft(12f)
                    .Background(ReceiptDesignTokens.SuccessBackground)
                    .CornerRadius(ReceiptDesignTokens.StatusPillRadius)
                    .PaddingHorizontal(10f)
                    .PaddingVertical(4f)
                    .Text(string.IsNullOrWhiteSpace(vm.StatusBadgeText) ? "PAID" : vm.StatusBadgeText)
                    .FontSize(ReceiptDesignTokens.StatusBadgeSize)
                    .Bold()
                    .FontColor(ReceiptDesignTokens.SuccessText);
            });

            column.Item()
                .PaddingTop(12f)
                .PaddingBottom(ReceiptDesignTokens.SectionSpacing)
                .LineHorizontal(ReceiptDesignTokens.DividerThickness)
                .LineColor(ReceiptDesignTokens.Border);
        });
    }

    private static void ComposeContent(IContainer container, ReceiptDocumentViewModel vm)
    {
        container.Column(column =>
        {
            column.Spacing(ReceiptDesignTokens.SectionSpacing);

            // ZONE 02 — PAYMENT HERO
            column.Item().Element(c => ComposePaymentHero(c, vm));

            // ZONE 03 — TRANSACTION INFORMATION (References, Payment Info, Customer Info)
            column.Item().Element(c => ComposeTransactionReferencePanel(c, vm));
            column.Item().Element(c => ComposePartyAndPaymentDetails(c, vm));

            // ZONE 04 — ACCOUNTING DETAILS (Particulars Table & Financial Summary)
            column.Item().Element(c => ComposeParticularsSection(c, vm));
            column.Item().Element(c => ComposeFinancialSummary(c, vm));

            // ZONE 05 — AUTHENTICATION & VERIFICATION
            column.Item().Element(c => ComposeAuthenticationSection(c, vm));
        });
    }

    /// <summary>
    /// ZONE 02 — PAYMENT HERO
    /// Clean left-aligned editorial layout without duplicate reference numbers.
    /// </summary>
    private static void ComposePaymentHero(IContainer container, ReceiptDocumentViewModel vm)
    {
        container.Column(hero =>
        {
            hero.Item().Text(vm.DocumentTitle)
                .FontSize(ReceiptDesignTokens.DocumentTitleSize)
                .SemiBold()
                .FontColor(ReceiptDesignTokens.SecondaryText);

            hero.Item().PaddingTop(4f).Text(vm.FormattedGrossAmount)
                .FontSize(ReceiptDesignTokens.HeroAmountSize)
                .Bold()
                .FontColor(ReceiptDesignTokens.PrimaryText);

            hero.Item().PaddingTop(4f).Text(text =>
            {
                text.DefaultTextStyle(s => s
                    .FontSize(ReceiptDesignTokens.HeroTimestampSize)
                    .FontColor(ReceiptDesignTokens.SecondaryText));

                text.Span("Payment confirmed  ·  Issued ");
                text.Span(vm.FormattedIssuedAtPst);
            });
        });
    }

    /// <summary>
    /// ZONE 03A — TRANSACTION REFERENCE PANEL
    /// Dedicated primary reference panel with 8pt rounded corners and a 2x2 grid.
    /// </summary>
    private static void ComposeTransactionReferencePanel(IContainer container, ReceiptDocumentViewModel vm)
    {
        container
            .Background(ReceiptDesignTokens.MutedSurface)
            .Border(ReceiptDesignTokens.DividerThickness)
            .BorderColor(ReceiptDesignTokens.Border)
            .CornerRadius(ReceiptDesignTokens.PrimaryPanelRadius)
            .Padding(ReceiptDesignTokens.PanelPadding)
            .Column(panel =>
            {
                panel.Item().PaddingBottom(8f).Text("TRANSACTION REFERENCES")
                    .FontSize(ReceiptDesignTokens.SectionHeadingSize)
                    .Bold()
                    .FontColor(ReceiptDesignTokens.PrimaryText);

                panel.Item().Row(row =>
                {
                    row.RelativeItem().PaddingRight(8f)
                        .Element(c => AddMetadataField(c, "OFFICIAL RECEIPT NO.", vm.OfficialReceiptNumber));
                    row.RelativeItem().PaddingLeft(8f)
                        .Element(c => AddMetadataField(c, "TREASURY ACK NO.", vm.TreasuryAcknowledgementNumber));
                });

                panel.Item().PaddingTop(8f).Row(row =>
                {
                    row.RelativeItem().PaddingRight(8f)
                        .Element(c => AddMetadataField(c, "ORDER NUMBER", vm.OrderNumber));
                    row.RelativeItem().PaddingLeft(8f)
                        .Element(c => AddMetadataField(c, "ORDER ID", vm.OrderId));
                });
            });
    }

    /// <summary>
    /// ZONE 03B — PAYMENT & CUSTOMER INFORMATION
    /// Distinct sections separated by whitespace and subtle horizontal dividers rather than nested cards.
    /// </summary>
    private static void ComposePartyAndPaymentDetails(IContainer container, ReceiptDocumentViewModel vm)
    {
        container.Column(section =>
        {
            section.Spacing(12f);

            section.Item().Row(row =>
            {
                // Payment Information Column
                row.RelativeItem().PaddingRight(12f).Column(payment =>
                {
                    payment.Item().PaddingBottom(6f).Text("PAYMENT INFORMATION")
                        .FontSize(ReceiptDesignTokens.SectionHeadingSize)
                        .Bold()
                        .FontColor(ReceiptDesignTokens.PrimaryText);

                    payment.Item().LineHorizontal(ReceiptDesignTokens.DividerThickness)
                        .LineColor(ReceiptDesignTokens.Border);

                    payment.Item().PaddingTop(8f).Column(fields =>
                    {
                        fields.Spacing(6f);
                        fields.Item().Element(c => AddMetadataField(c, "PAYMENT METHOD", vm.PaymentMethod));
                        fields.Item().Element(c => AddMetadataField(c, "PAYMENT ATTEMPT ID", vm.PaymentAttemptId));
                        fields.Item().Element(c => AddMetadataField(c, "ISSUED AT (LOCAL)", vm.FormattedIssuedAtPst));
                        fields.Item().Element(c => AddMetadataField(c, "ISSUED AT (UTC)", vm.FormattedIssuedAtUtc));
                    });
                });

                // Customer Information Column
                row.RelativeItem().PaddingLeft(12f).Column(customer =>
                {
                    customer.Item().PaddingBottom(6f).Row(headerRow =>
                    {
                        headerRow.AutoItem().Width(3f).Height(10f)
                            .Background(ReceiptDesignTokens.Accent);
                        headerRow.RelativeItem().PaddingLeft(6f).Text("CUSTOMER INFORMATION")
                            .FontSize(ReceiptDesignTokens.SectionHeadingSize)
                            .Bold()
                            .FontColor(ReceiptDesignTokens.PrimaryText);
                    });

                    customer.Item().LineHorizontal(ReceiptDesignTokens.DividerThickness)
                        .LineColor(ReceiptDesignTokens.Border);

                    customer.Item().PaddingTop(8f).Column(fields =>
                    {
                        fields.Spacing(6f);
                        fields.Item().Element(c => AddMetadataField(c, "RECEIVED FROM", vm.CustomerName));
                        fields.Item().Element(c => AddMetadataField(c, "EMAIL ADDRESS", vm.CustomerEmail));
                    });
                });
            });
        });
    }

    /// <summary>
    /// Flexible stacked label/value layout that prevents horizontal clipping on long IDs or emails.
    /// </summary>
    private static void AddMetadataField(IContainer container, string label, string? value)
    {
        string displayValue = string.IsNullOrWhiteSpace(value) ? "—" : value;

        container.Column(field =>
        {
            field.Item().Text(label)
                .FontSize(ReceiptDesignTokens.MetadataLabelSize)
                .FontColor(ReceiptDesignTokens.SecondaryText);

            field.Item().PaddingTop(2f).Text(InsertZeroWidthSpacesForLongTokens(displayValue, 28))
                .FontSize(ReceiptDesignTokens.MetadataValueSize)
                .SemiBold()
                .FontColor(ReceiptDesignTokens.PrimaryText);
        });
    }

    /// <summary>
    /// ZONE 04A — PARTICULARS TABLE
    /// Preserves all 5 legacy columns with balanced proportions, safe wrapping, and editorial horizontal rules.
    /// </summary>
    private static void ComposeParticularsSection(IContainer container, ReceiptDocumentViewModel vm)
    {
        container.Column(section =>
        {
            section.Item().PaddingBottom(6f).Text("PARTICULARS")
                .FontSize(ReceiptDesignTokens.SectionHeadingSize)
                .Bold()
                .FontColor(ReceiptDesignTokens.PrimaryText);

            section.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3.4f); // Description
                    columns.RelativeColumn(0.6f); // Quantity
                    columns.RelativeColumn(1.2f); // Unit Price
                    columns.RelativeColumn(1.2f); // Amount
                    columns.RelativeColumn(1.5f); // Tax Classification
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCellStyle).Text("Description");
                    header.Cell().Element(HeaderCellStyle).AlignCenter().Text("Qty");
                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Unit Price");
                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Amount");
                    header.Cell().Element(HeaderCellStyle).AlignRight().Text("Tax Classification");

                    static IContainer HeaderCellStyle(IContainer cell) =>
                        cell.BorderBottom(ReceiptDesignTokens.StrongDividerThickness)
                            .BorderColor(ReceiptDesignTokens.StrongBorder)
                            .PaddingVertical(ReceiptDesignTokens.TableRowVerticalPadding)
                            .PaddingHorizontal(ReceiptDesignTokens.TableCellHorizontalPadding)
                            .DefaultTextStyle(s => s
                                .FontSize(ReceiptDesignTokens.TableHeaderSize)
                                .Bold()
                                .FontColor(ReceiptDesignTokens.SecondaryText));
                });

                foreach (ReceiptLineItemViewModel item in vm.LineItems)
                {
                    table.Cell().Element(BodyCellStyle)
                        .Text(InsertZeroWidthSpacesForLongTokens(item.Description, 36));

                    table.Cell().Element(BodyCellStyle).AlignCenter()
                        .Text(item.Quantity.ToString(CultureInfo.InvariantCulture));

                    table.Cell().Element(BodyCellStyle).AlignRight()
                        .Text(item.UnitPrice);

                    table.Cell().Element(BodyCellStyle).AlignRight()
                        .Text(item.Amount)
                        .SemiBold();

                    table.Cell().Element(BodyCellStyle).AlignRight()
                        .Text(item.TaxClassification)
                        .FontSize(ReceiptDesignTokens.MetadataLabelSize)
                        .FontColor(ReceiptDesignTokens.SecondaryText);
                }

                static IContainer BodyCellStyle(IContainer cell) =>
                    cell.BorderBottom(ReceiptDesignTokens.DividerThickness)
                        .BorderColor(ReceiptDesignTokens.Border)
                        .PaddingVertical(ReceiptDesignTokens.TableRowVerticalPadding)
                        .PaddingHorizontal(ReceiptDesignTokens.TableCellHorizontalPadding)
                        .DefaultTextStyle(s => s
                            .FontSize(ReceiptDesignTokens.TableCellSize)
                            .FontColor(ReceiptDesignTokens.PrimaryText));
            });
        });
    }

    /// <summary>
    /// ZONE 04B — FINANCIAL SUMMARY
    /// Right-aligned summary with vertically aligned currency values and prominent total.
    /// Does not recalculate VAT or alter ViewModel money strings.
    /// </summary>
    private static void ComposeFinancialSummary(IContainer container, ReceiptDocumentViewModel vm)
    {
        container.AlignRight().Width(ReceiptDesignTokens.SummaryBlockWidth).Column(summary =>
        {
            summary.Item().PaddingBottom(6f).Text("FINANCIAL SUMMARY")
                .FontSize(ReceiptDesignTokens.SectionHeadingSize)
                .Bold()
                .FontColor(ReceiptDesignTokens.PrimaryText);

            summary.Item().Element(c => AddSummaryRow(c, "VATable Sales", vm.FormattedVatableSales, isTotal: false));
            summary.Item().Element(c => AddSummaryRow(c, "12% VAT", vm.FormattedVatAmount, isTotal: false));
            summary.Item().Element(c => AddSummaryRow(c, "VAT-Exempt Sales", vm.FormattedVatExemptSales, isTotal: false));
            summary.Item().Element(c => AddSummaryRow(c, "Zero-Rated Sales", vm.FormattedZeroRatedSales, isTotal: false));

            summary.Item().PaddingVertical(6f)
                .LineHorizontal(ReceiptDesignTokens.DividerThickness)
                .LineColor(ReceiptDesignTokens.StrongBorder);

            summary.Item().Element(c => AddSummaryRow(c, "TOTAL PAID", vm.FormattedGrossAmount, isTotal: true));
        });
    }

    private static void AddSummaryRow(IContainer container, string label, string value, bool isTotal)
    {
        container.PaddingVertical(isTotal ? 4f : 2.5f).Row(row =>
        {
            var labelText = row.RelativeItem().AlignMiddle().Text(label)
                .FontSize(isTotal ? ReceiptDesignTokens.TotalLabelSize : ReceiptDesignTokens.SummaryLabelSize)
                .FontColor(isTotal ? ReceiptDesignTokens.PrimaryText : ReceiptDesignTokens.SecondaryText);
            if (isTotal)
            {
                labelText.Bold();
            }

            var valueText = row.ConstantItem(ReceiptDesignTokens.SummaryAmountColumnWidth).AlignRight().AlignMiddle().Text(value)
                .FontSize(isTotal ? ReceiptDesignTokens.TotalValueSize : ReceiptDesignTokens.SummaryValueSize)
                .FontColor(ReceiptDesignTokens.PrimaryText);
            if (isTotal)
            {
                valueText.Bold();
            }
            else
            {
                valueText.SemiBold();
            }
        });
    }

    /// <summary>
    /// ZONE 05 — AUTHENTICATION AND VERIFICATION
    /// Technical audit digest panel with safe wrapping for 64+ character SHA-256 hex strings
    /// and exact preservation of the domain's legal compliance wording.
    /// </summary>
    private static void ComposeAuthenticationSection(IContainer container, ReceiptDocumentViewModel vm)
    {
        container
            .Background(ReceiptDesignTokens.MutedSurface)
            .Border(ReceiptDesignTokens.DividerThickness)
            .BorderColor(ReceiptDesignTokens.Border)
            .CornerRadius(ReceiptDesignTokens.SecondaryPanelRadius)
            .Padding(ReceiptDesignTokens.PanelPadding)
            .Column(auth =>
            {
                auth.Item().Text("AUTHENTICATION & VERIFICATION")
                    .FontSize(ReceiptDesignTokens.SectionHeadingSize)
                    .Bold()
                    .FontColor(ReceiptDesignTokens.PrimaryText);

                auth.Item().PaddingTop(6f).Text("SHA-256 AUDIT DIGEST")
                    .FontSize(ReceiptDesignTokens.MetadataLabelSize)
                    .SemiBold()
                    .FontColor(ReceiptDesignTokens.SecondaryText);

                auth.Item().PaddingTop(2f)
                    .Text(InsertZeroWidthSpacesForLongTokens(vm.AuditDigest, 32))
                    .FontFamily(ReceiptDesignTokens.MonospaceFontFamily)
                    .FontSize(ReceiptDesignTokens.AuditDigestSize)
                    .FontColor(ReceiptDesignTokens.PrimaryText);

                if (!string.IsNullOrWhiteSpace(vm.LegalComplianceNote))
                {
                    auth.Item().PaddingTop(8f).Text(vm.LegalComplianceNote)
                        .FontSize(ReceiptDesignTokens.ComplianceNoteSize)
                        .FontColor(ReceiptDesignTokens.SecondaryText);
                }
            });
    }

    /// <summary>
    /// FOOTER
    /// Explicit top spacing, thin top border, issuer registered name on the left, and page numbering on the right.
    /// </summary>
    private static void ComposeFooter(IContainer container, ReceiptDocumentViewModel vm)
    {
        container.PaddingTop(10f).Column(footer =>
        {
            footer.Item()
                .LineHorizontal(ReceiptDesignTokens.DividerThickness)
                .LineColor(ReceiptDesignTokens.Border);

            footer.Item().PaddingTop(6f).Row(row =>
            {
                row.RelativeItem().Text(vm.IssuerRegisteredName)
                    .FontSize(ReceiptDesignTokens.FooterSize)
                    .FontColor(ReceiptDesignTokens.SecondaryText);

                row.AutoItem().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(s => s
                        .FontSize(ReceiptDesignTokens.FooterSize)
                        .FontColor(ReceiptDesignTokens.SecondaryText));

                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });
    }

    /// <summary>
    /// Inserts zero-width spaces (\u200B) into unbroken single tokens (such as 64-char SHA-256 digests,
    /// long UUID/attempt strings, or unspaced hostile strings) so QuestPDF wraps cleanly without
    /// clipping, overflowing, or altering copy-pasted text semantics.
    /// </summary>
    private static string InsertZeroWidthSpacesForLongTokens(string? input, int maxUnbrokenLength)
    {
        if (string.IsNullOrEmpty(input) || input.Length <= maxUnbrokenLength)
        {
            return input ?? string.Empty;
        }

        var sb = new StringBuilder(input.Length + (input.Length / maxUnbrokenLength) * 2);
        int currentRun = 0;

        foreach (char c in input)
        {
            sb.Append(c);
            if (char.IsWhiteSpace(c) || c == '-' || c == '/' || c == '.')
            {
                currentRun = 0;
            }
            else
            {
                currentRun++;
                if (currentRun >= maxUnbrokenLength)
                {
                    sb.Append('\u200B');
                    currentRun = 0;
                }
            }
        }

        return sb.ToString();
    }

    private static void NormalizePdf14Header(byte[] pdfBytes)
    {
        if (pdfBytes.Length >= Pdf14HeaderBytes.Length &&
            pdfBytes[0] == (byte)'%' &&
            pdfBytes[1] == (byte)'P' &&
            pdfBytes[2] == (byte)'D' &&
            pdfBytes[3] == (byte)'F' &&
            pdfBytes[4] == (byte)'-')
        {
            Buffer.BlockCopy(Pdf14HeaderBytes, 0, pdfBytes, 0, Pdf14HeaderBytes.Length);
        }
    }
}
