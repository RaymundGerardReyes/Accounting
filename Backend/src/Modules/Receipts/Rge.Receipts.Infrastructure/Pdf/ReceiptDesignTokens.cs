namespace Rge.Receipts.Infrastructure.Pdf;

/// <summary>
/// Centralized design tokens for the "Quiet Financial Precision" receipt design system.
/// Combines Swiss editorial typography, institutional layout discipline, and restrained fintech accents.
/// </summary>
internal static class ReceiptDesignTokens
{
    // Color Palette
    public const string PageBackground = "#FFFFFF";
    public const string PrimaryText = "#0A0A0A";       // Obsidian Ink
    public const string SecondaryText = "#71717A";     // Slate Gray
    public const string MutedSurface = "#F9FAFB";      // Cloud Surface
    public const string Border = "#E5E7EB";            // Soft Divider
    public const string StrongBorder = "#0A0A0A";      // Editorial Table Header Rule
    public const string SuccessText = "#15803D";       // Financial Emerald
    public const string SuccessBackground = "#DCFCE7"; // Emerald Pill Surface
    public const string Accent = "#E11D48";            // Restrained Coral

    // Typography Families
    public const string PrimaryFontFamily = "Arial";
    public const string MonospaceFontFamily = "Courier New";

    // Typography Scale (pt)
    public const float CompanyNameSize = 15.5f;
    public const float DepartmentSize = 8f;
    public const float IssuerMetadataSize = 7.5f;
    public const float StatusBadgeSize = 8f;
    public const float DocumentTitleSize = 9f;
    public const float HeroAmountSize = 30f;
    public const float HeroTimestampSize = 8f;
    public const float SectionHeadingSize = 8.5f;
    public const float BodySize = 9f;
    public const float MetadataLabelSize = 7.5f;
    public const float MetadataValueSize = 8.5f;
    public const float TableHeaderSize = 8f;
    public const float TableCellSize = 8.5f;
    public const float SummaryLabelSize = 8.5f;
    public const float SummaryValueSize = 9f;
    public const float TotalLabelSize = 9.5f;
    public const float TotalValueSize = 14f;
    public const float AuditDigestSize = 7f;
    public const float ComplianceNoteSize = 7.5f;
    public const float FooterSize = 7.5f;

    // 4-Point Spacing System (pt)
    public const float PageMargin = 38f;
    public const float SpaceCompact = 4f;
    public const float SpaceMetadata = 8f;
    public const float PanelPadding = 12f;
    public const float SectionSpacing = 16f;
    public const float MajorZoneSpacing = 20f;
    public const float TableRowVerticalPadding = 6f;
    public const float TableCellHorizontalPadding = 4f;
    public const float DividerThickness = 0.5f;
    public const float StrongDividerThickness = 1f;

    // Geometry & Radii (pt)
    public const float PrimaryPanelRadius = 8f;
    public const float SecondaryPanelRadius = 6f;
    public const float StatusPillRadius = 12f;
    public const float SummaryBlockWidth = 260f;
    public const float SummaryAmountColumnWidth = 115f;
}
