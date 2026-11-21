using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rge.Integrations.Application.Kotobee;
using Rge.Integrations.Application.Ports;
using Xunit;

namespace Rge.Integrations.Application.Tests;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores")]
public sealed class KotobeeDynamicSystemUnitTests
{
    private readonly IntelligentSheetSchemaAdapter _adapter = new();

    // =========================================================================
    // UNIT-001 to UNIT-025: INTELLIGENT SHEET SCHEMA ADAPTER (COLUMN DETECTION)
    // =========================================================================

    [Theory]
    [InlineData("ProductSku", "BookId", "LibraryId", "PublishingMode", "AccessDurationDays", "IsActive", "AllowAutoActivate")] // UNIT-001
    [InlineData("SKU", "BID", "LIBID", "MODE", "DAYS", "ACTIVE", "AUTO")]                                                    // UNIT-002
    [InlineData("ItemCode", "KotobeeBid", "ShelfId", "Platform", "Duration", "Enabled", "AutoActivate")]                     // UNIT-003
    [InlineData("product_sku", "book_id", "library_id", "publishing_mode", "duration_days", "status", "instant_grant")]     // UNIT-004
    [InlineData("Code", "EbookId", "Lib_Id", "DeliveryChannel", "AccessExpiry", "Is_Active", "Auto")]                         // UNIT-005
    public void SchemaAdapter_DetectsCanonicalAliases_Accurately(
        string skuCol, string bidCol, string libCol, string modeCol, string durCol, string activeCol, string autoCol)
    {
        var workbook = new AppsScriptWorkbookPayload(
            SpreadsheetId: "SHEET-UNIT-01",
            Title: "Catalog",
            ExportedAtUtc: DateTimeOffset.UtcNow,
            Sheets: [
                new AppsScriptSheetTab(
                    TabName: "Main",
                    Headers: [skuCol, bidCol, libCol, modeCol, durCol, activeCol, autoCol],
                    RowCount: 1,
                    Rows: [
                        new AppsScriptRowData(
                            RowNumber: 1,
                            Values: new Dictionary<string, string>
                            {
                                [skuCol] = "SKU-TEST-01",
                                [bidCol] = "BID-100",
                                [libCol] = "SHELF-99",
                                [modeCol] = "library",
                                [durCol] = "30",
                                [activeCol] = "true",
                                [autoCol] = "yes"
                            }
                        )
                    ]
                )
            ]
        );

        var result = _adapter.AdaptWorkbook(workbook);
        Assert.Single(result.Rules);

        var rule = result.Rules[0];
        Assert.Equal("SKU-TEST-01", rule.ProductSku);
        Assert.Equal("BID-100", rule.BookId);
        Assert.Equal("SHELF-99", rule.LibraryId);
        Assert.Equal(30, rule.AccessDurationDays);
        Assert.True(rule.IsActive);
        Assert.True(rule.AllowAutoActivate);
    }

    [Theory]
    [InlineData("true", true)]    // UNIT-006
    [InlineData("TRUE", true)]    // UNIT-007
    [InlineData("1", true)]       // UNIT-008
    [InlineData("yes", true)]     // UNIT-009
    [InlineData("YES", true)]     // UNIT-010
    [InlineData("false", false)]  // UNIT-011
    [InlineData("0", false)]      // UNIT-012
    [InlineData("no", false)]     // UNIT-013
    [InlineData("NO", false)]     // UNIT-014
    [InlineData("", false)]       // UNIT-015
    public void SchemaAdapter_ParsesBooleanStrings_Resiliently(string rawValue, bool expected)
    {
        var workbook = new AppsScriptWorkbookPayload(
            SpreadsheetId: "SHEET-BOOL-TEST",
            Title: "Bools",
            ExportedAtUtc: DateTimeOffset.UtcNow,
            Sheets: [
                new AppsScriptSheetTab(
                    TabName: "Tab",
                    Headers: ["sku", "bid", "active"],
                    RowCount: 1,
                    Rows: [
                        new AppsScriptRowData(
                            RowNumber: 1,
                            Values: new Dictionary<string, string>
                            {
                                ["sku"] = "SKU-BOOL",
                                ["bid"] = "B-1",
                                ["active"] = rawValue
                            }
                        )
                    ]
                )
            ]
        );

        var result = _adapter.AdaptWorkbook(workbook, defaultLibraryId: "TEST-LIB");
        Assert.Single(result.Rules);
        Assert.Equal(expected, result.Rules[0].IsActive);
    }

    [Theory]
    [InlineData("365", 365)]       // UNIT-016
    [InlineData("0", null)]        // UNIT-017 (0 days = lifetime)
    [InlineData("", null)]         // UNIT-018
    [InlineData("lifetime", null)] // UNIT-019
    [InlineData("null", null)]     // UNIT-020
    [InlineData("7", 7)]           // UNIT-021
    [InlineData("180", 180)]       // UNIT-022
    [InlineData("90 days", 90)]    // UNIT-023
    [InlineData("30d", 30)]        // UNIT-024
    [InlineData("-1", null)]       // UNIT-025
    public void SchemaAdapter_ParsesAccessDuration_Safely(string rawDuration, int? expectedDays)
    {
        var workbook = new AppsScriptWorkbookPayload(
            SpreadsheetId: "SHEET-DUR-TEST",
            Title: "Durations",
            ExportedAtUtc: DateTimeOffset.UtcNow,
            Sheets: [
                new AppsScriptSheetTab(
                    TabName: "Tab",
                    Headers: ["sku", "bid", "duration"],
                    RowCount: 1,
                    Rows: [
                        new AppsScriptRowData(
                            RowNumber: 1,
                            Values: new Dictionary<string, string>
                            {
                                ["sku"] = "SKU-DUR",
                                ["bid"] = "B-2",
                                ["duration"] = rawDuration
                            }
                        )
                    ]
                )
            ]
        );

        var result = _adapter.AdaptWorkbook(workbook, defaultLibraryId: "TEST-LIB");
        Assert.Single(result.Rules);
        Assert.Equal(expectedDays, result.Rules[0].AccessDurationDays);
    }

    // =========================================================================
    // UNIT-026 to UNIT-050: MULTI-TAB RELATIONAL STITCHING & DYNAMIC COLUMNS
    // =========================================================================

    [Fact]
    public void SchemaAdapter_JoinsAcrossTabs_UsingSkuKey()          // UNIT-026 to UNIT-035
    {
        var workbook = new AppsScriptWorkbookPayload(
            SpreadsheetId: "SHEET-RELATIONAL-01",
            Title: "Products and Ebooks",
            ExportedAtUtc: DateTimeOffset.UtcNow,
            Sheets: [
                new AppsScriptSheetTab(
                    TabName: "Products",
                    Headers: ["sku", "title", "category"],
                    RowCount: 2,
                    Rows: [
                        new AppsScriptRowData(1, new() { ["sku"] = "CPA-REV", ["title"] = "CPA Reviewer", ["category"] = "Accounting" }),
                        new AppsScriptRowData(2, new() { ["sku"] = "TAX-GUIDE", ["title"] = "Taxation Guide", ["category"] = "Law" })
                    ]
                ),
                new AppsScriptSheetTab(
                    TabName: "KotobeeBooks",
                    Headers: ["sku", "bid", "shelf_id", "active"],
                    RowCount: 2,
                    Rows: [
                        new AppsScriptRowData(1, new() { ["sku"] = "CPA-REV", ["bid"] = "BID-CPA-01", ["shelf_id"] = "SHELF-1", ["active"] = "true" }),
                        new AppsScriptRowData(2, new() { ["sku"] = "TAX-GUIDE", ["bid"] = "BID-TAX-02", ["shelf_id"] = "SHELF-1", ["active"] = "true" })
                    ]
                )
            ]
        );

        var result = _adapter.AdaptWorkbook(workbook);
        Assert.Equal(2, result.Rules.Count);

        var cpaRule = result.Rules.First(r => r.ProductSku == "CPA-REV");
        Assert.Equal("BID-CPA-01", cpaRule.BookId);
        Assert.Equal("SHELF-1", cpaRule.LibraryId);
        Assert.True(cpaRule.ExtraColumns.ContainsKey("title"));
        Assert.Equal("CPA Reviewer", cpaRule.ExtraColumns["title"]);
    }

    [Theory]
    [InlineData("SKU-1", "BID-1", "CustomField1", "CustomValue1")]   // UNIT-036
    [InlineData("SKU-2", "BID-2", "Instructor", "Prof. Santos")]     // UNIT-037
    [InlineData("SKU-3", "BID-3", "Edition", "2026-v2")]             // UNIT-038
    [InlineData("SKU-4", "BID-4", "Notes", "Special bundle")]        // UNIT-039
    [InlineData("SKU-5", "BID-5", "Batch", "BATCH-NOV")]             // UNIT-040
    public void SchemaAdapter_PreservesUnmappedColumns_InExtraColumns(
        string sku, string bid, string extraHeader, string extraValue)
    {
        var workbook = new AppsScriptWorkbookPayload(
            SpreadsheetId: "SHEET-EXTRA-01",
            Title: "Extras",
            ExportedAtUtc: DateTimeOffset.UtcNow,
            Sheets: [
                new AppsScriptSheetTab(
                    TabName: "Sheet1",
                    Headers: ["sku", "bid", extraHeader],
                    RowCount: 1,
                    Rows: [
                        new AppsScriptRowData(1, new() { ["sku"] = sku, ["bid"] = bid, [extraHeader] = extraValue })
                    ]
                )
            ]
        );

        var result = _adapter.AdaptWorkbook(workbook, defaultLibraryId: "TEST-LIB");
        Assert.Single(result.Rules);
        Assert.True(result.Rules[0].ExtraColumns.TryGetValue(extraHeader, out var val));
        Assert.Equal(extraValue, val);
    }

    [Fact]
    public void SchemaAdapter_Throws_WhenWorkbookHasZeroSheets()      // UNIT-041
    {
        var emptyWorkbook = new AppsScriptWorkbookPayload("ID", "Empty", DateTimeOffset.UtcNow, []);
        Assert.Throws<InvalidOperationException>(() => _adapter.AdaptWorkbook(emptyWorkbook));
    }

    [Theory]
    [InlineData("SKU-A", "   ")]                                     // UNIT-042: Empty BookId
    [InlineData("   ", "BID-1")]                                     // UNIT-043: Empty SKU
    [InlineData(null, "BID-1")]                                      // UNIT-044: Null SKU
    [InlineData("SKU-A", null)]                                      // UNIT-045: Null BookId
    public void SchemaAdapter_SkipsRows_WithMissingRequiredKeys(string? sku, string? bid)
    {
        var workbook = new AppsScriptWorkbookPayload(
            SpreadsheetId: "SHEET-MISSING-KEYS",
            Title: "Sheet",
            ExportedAtUtc: DateTimeOffset.UtcNow,
            Sheets: [
                new AppsScriptSheetTab(
                    TabName: "Tab",
                    Headers: ["sku", "bid"],
                    RowCount: 1,
                    Rows: [
                        new AppsScriptRowData(1, new()
                        {
                            ["sku"] = sku ?? string.Empty,
                            ["bid"] = bid ?? string.Empty
                        })
                    ]
                )
            ]
        );

        var result = _adapter.AdaptWorkbook(workbook, defaultLibraryId: "TEST-LIB");
        Assert.Empty(result.Rules);
    }

    [Theory]
    [InlineData("library", "DEFAULT-LIB-1")]                         // UNIT-046
    [InlineData("cloud", null)]                                      // UNIT-047
    public void SchemaAdapter_UsesDefaultLibraryId_WhenOmittedInRow(string mode, string? defaultLib)
    {
        var workbook = new AppsScriptWorkbookPayload(
            SpreadsheetId: "SHEET-DEFAULTS",
            Title: "Defaults",
            ExportedAtUtc: DateTimeOffset.UtcNow,
            Sheets: [
                new AppsScriptSheetTab(
                    TabName: "Tab",
                    Headers: ["sku", "bid"],
                    RowCount: 1,
                    Rows: [
                        new AppsScriptRowData(1, new() { ["sku"] = "SKU-DEF", ["bid"] = "B-DEF" })
                    ]
                )
            ]
        );

        var result = _adapter.AdaptWorkbook(workbook, defaultPublishingMode: mode, defaultLibraryId: defaultLib);
        Assert.Single(result.Rules);
        Assert.Equal(defaultLib, result.Rules[0].LibraryId);
    }

    // =========================================================================
    // UNIT-051 to UNIT-075: MASS-WIPE GUARD (>50% DROP REJECTION)
    // =========================================================================

    [Theory]
    [InlineData(100, 49)]                                            // UNIT-051: 51% drop -> reject
    [InlineData(100, 40)]                                            // UNIT-052: 60% drop -> reject
    [InlineData(100, 10)]                                            // UNIT-053: 90% drop -> reject
    [InlineData(100, 0)]                                             // UNIT-054: 100% drop -> reject
    [InlineData(50, 24)]                                             // UNIT-055: 52% drop -> reject
    [InlineData(200, 99)]                                            // UNIT-056: >50% drop -> reject
    [InlineData(10, 4)]                                              // UNIT-057: 60% drop -> reject
    public void MassWipeGuard_Rejects_DropsGreaterThan50Percent(int previousCount, int newCount)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DynamicSheetKotobeeCatalogProvider.ValidateMassWipeGuard(newCount, previousCount));
        Assert.Contains("Mass wipe prevented", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(100, 50)]                                            // UNIT-058: 50% drop exactly -> allowed
    [InlineData(100, 51)]                                            // UNIT-059: 49% drop -> allowed
    [InlineData(100, 80)]                                            // UNIT-060: 20% drop -> allowed
    [InlineData(100, 100)]                                           // UNIT-061: Same count -> allowed
    [InlineData(100, 150)]                                           // UNIT-062: Count increased -> allowed
    [InlineData(100, 500)]                                           // UNIT-063: Massive expansion -> allowed
    public void MassWipeGuard_Allows_SafeRowCounts(int previousCount, int newCount)
    {
        // Should not throw
        DynamicSheetKotobeeCatalogProvider.ValidateMassWipeGuard(newCount, previousCount);
    }

    [Theory]
    [InlineData(null, 10)]                                           // UNIT-064: First sync -> allowed
    [InlineData(0, 5)]                                               // UNIT-065: Zero previous -> allowed
    public void MassWipeGuard_Allows_InitialPopulation(int? previousCount, int newCount)
    {
        // Should not throw
        DynamicSheetKotobeeCatalogProvider.ValidateMassWipeGuard(newCount, previousCount);
    }

    // =========================================================================
    // UNIT-076 to UNIT-100: DYNAMIC CATALOG PROVIDER & BUNDLE MAPPING
    // =========================================================================

    [Fact]
    public void DynamicCatalogProvider_FallsBackToAppSettings_WhenNoSnapshot() // UNIT-076
    {
        var optionsMonitor = new TestOptionsMonitor<KotobeeOptions>(new KotobeeOptions
        {
            DefaultLibraryId = "APP-LIB",
            ProductMappings = new()
            {
                ["SKU-FALLBACK"] = [new KotobeeProductBookMapping("B-FB")]
            }
        });

        var provider = new DynamicSheetKotobeeCatalogProvider(optionsMonitor, snapshotStore: null);
        var resolved = provider.ResolveBooksForOrder(["SKU-FALLBACK"]);

        Assert.Single(resolved);
        Assert.Equal("B-FB", resolved[0].BookId);
        Assert.Equal("APP-LIB", resolved[0].LibraryId);
    }

    [Fact]
    public async Task DynamicCatalogProvider_UsesLkgSnapshot_WhenAvailable() // UNIT-077
    {
        var optionsMonitor = new TestOptionsMonitor<KotobeeOptions>(new KotobeeOptions());
        var snapshotStore = new InMemorySheetSnapshotStore();

        var snapshotMappings = new Dictionary<string, List<KotobeeProductBookMapping>>
        {
            ["SKU-DYNAMIC"] = [new KotobeeProductBookMapping("B-DYN-01", "DYN-SHELF", 90)]
        };

        await snapshotStore.SaveSnapshotAsync(new KotobeeSheetCatalogSnapshot(
            Id: Guid.NewGuid(),
            SpreadsheetId: "SHEET-DYN-1",
            SheetRange: "Books!A1:Z50",
            ContentSha256: "hash123",
            ActiveRowCount: 1,
            MappingsJson: JsonSerializer.Serialize(snapshotMappings),
            SyncedBy: "unit-test",
            SyncedAtUtc: DateTimeOffset.UtcNow,
            IsCurrent: true
        ));

        var provider = new DynamicSheetKotobeeCatalogProvider(optionsMonitor, snapshotStore);
        var resolved = provider.ResolveBooksForOrder(["SKU-DYNAMIC"]);

        Assert.Single(resolved);
        Assert.Equal("B-DYN-01", resolved[0].BookId);
        Assert.Equal("DYN-SHELF", resolved[0].LibraryId);
        Assert.Equal(90, resolved[0].AccessDurationDays);
    }

    [Fact]
    public void DynamicCatalogProvider_ExpandsBundles_AndDeduplicates() // UNIT-078 to UNIT-085
    {
        var optionsMonitor = new TestOptionsMonitor<KotobeeOptions>(new KotobeeOptions
        {
            DefaultLibraryId = "MAIN-LIB",
            ProductMappings = new()
            {
                ["BUNDLE-MASTER"] = [
                    new KotobeeProductBookMapping("B-01"),
                    new KotobeeProductBookMapping("B-02"),
                    new KotobeeProductBookMapping("B-03")
                ],
                ["STANDALONE-1"] = [
                    new KotobeeProductBookMapping("B-01") // Overlaps with bundle
                ]
            }
        });

        var provider = new DynamicSheetKotobeeCatalogProvider(optionsMonitor);
        var resolved = provider.ResolveBooksForOrder(["BUNDLE-MASTER", "STANDALONE-1"]);

        // Deduplication guarantees B-01 is only resolved once
        Assert.Equal(3, resolved.Count);
        Assert.Contains(resolved, r => r.BookId == "B-01");
        Assert.Contains(resolved, r => r.BookId == "B-02");
        Assert.Contains(resolved, r => r.BookId == "B-03");
    }

    [Theory]
    [InlineData("SKU-01", "BID-01", "LIB-1", 30)]                    // UNIT-086
    [InlineData("SKU-02", "BID-02", "LIB-2", 60)]                    // UNIT-087
    [InlineData("SKU-03", "BID-03", null, null)]                     // UNIT-088
    [InlineData("SKU-04", "BID-04", "LIB-4", null)]                  // UNIT-089
    [InlineData("SKU-05", "BID-05", null, 365)]                      // UNIT-090
    public void RulesToProductMappings_TransformsAccurately(string sku, string bid, string? libId, int? duration)
    {
        var rules = new List<AdaptedDynamicBookRule>
        {
            new("Tab", 1, sku, "library", libId, bid, duration, IsActive: true, AllowAutoActivate: true, new())
        };

        var mappings = DynamicSheetKotobeeCatalogProvider.RulesToProductMappings(rules, defaultLibraryId: "DEF-LIB");
        Assert.True(mappings.TryGetValue(sku, out var list));
        Assert.Single(list);
        Assert.Equal(bid, list[0].BookId);
        Assert.Equal(libId ?? "DEF-LIB", list[0].LibraryId);
        Assert.Equal(duration, list[0].AccessDurationDays);
    }

    [Fact]
    public void RulesToProductMappings_FiltersInactiveOrDisallowedAuto() // UNIT-091 to UNIT-100
    {
        var rules = new List<AdaptedDynamicBookRule>
        {
            new("Tab", 1, "SKU-INACTIVE", "library", "LIB", "BID-1", null, IsActive: false, AllowAutoActivate: true, new()),
            new("Tab", 2, "SKU-NO-AUTO", "library", "LIB", "BID-2", null, IsActive: true, AllowAutoActivate: false, new()),
            new("Tab", 3, "SKU-VALID", "library", "LIB", "BID-3", null, IsActive: true, AllowAutoActivate: true, new())
        };

        var mappings = DynamicSheetKotobeeCatalogProvider.RulesToProductMappings(rules, "DEF-LIB");
        Assert.False(mappings.ContainsKey("SKU-INACTIVE"));
        Assert.False(mappings.ContainsKey("SKU-NO-AUTO"));
        Assert.True(mappings.ContainsKey("SKU-VALID"));
        Assert.Single(mappings["SKU-VALID"]);
    }

    private sealed class InMemorySheetSnapshotStore : IKotobeeSheetSnapshotStore
    {
        private readonly List<KotobeeSheetCatalogSnapshot> _snapshots = [];

        public Task SaveSnapshotAsync(KotobeeSheetCatalogSnapshot snapshot, CancellationToken ct = default)
        {
            _snapshots.Add(snapshot);
            return Task.CompletedTask;
        }

        public Task<KotobeeSheetCatalogSnapshot?> GetLatestCurrentSnapshotAsync(CancellationToken ct = default)
        {
            return Task.FromResult(_snapshots.LastOrDefault(s => s.IsCurrent));
        }

        public Task<IReadOnlyList<KotobeeSheetCatalogSnapshot>> GetAllSnapshotsAsync(int limit = 50, CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<KotobeeSheetCatalogSnapshot>>(_snapshots.Take(limit).ToList());
        }
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T currentValue) => CurrentValue = currentValue;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
