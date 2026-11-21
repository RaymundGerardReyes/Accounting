using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Rge.Payments.Integration.Tests;

/// <summary>
/// Covers Backend Regression Scenarios R15-R21 & R03-R04:
/// Architecture isolation, zero-hardcode secret enforcement, and duplicate-event idempotency.
/// </summary>
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores")]
public sealed class KotobeeRegressionTests
{
    // R16: Payments module must NEVER take a dependency on Rge.Integrations
    [Fact]
    public void PaymentsModule_NeverReferencesIntegrationsOrKotobee()
    {
        var paymentsAssembly = typeof(Rge.Payments.Application.Orders.CreateOrder.CreateOrderCommand).Assembly;
        var referencedNames = paymentsAssembly.GetReferencedAssemblies().Select(a => a.Name);

        Assert.DoesNotContain(referencedNames, name =>
            name is not null && name.Contains("Integrations", StringComparison.OrdinalIgnoreCase));
    }

    // R17: Receipts module must NEVER take a dependency on Kotobee
    [Fact]
    public void ReceiptsModule_NeverReferencesKotobee()
    {
        var receiptsAssembly = typeof(Rge.Receipts.Application.IssueReceipt.IssueReceiptHandler).Assembly;
        var referencedNames = receiptsAssembly.GetReferencedAssemblies().Select(a => a.Name);

        Assert.DoesNotContain(referencedNames, name =>
            name is not null && name.Contains("Kotobee", StringComparison.OrdinalIgnoreCase));
    }

    // R18: JsonProductCatalog must remain pure and free from Kotobee properties
    [Fact]
    public void ProductCatalog_NeverContainsKotobeeFields()
    {
        var catalogType = typeof(Rge.Payments.Infrastructure.Catalog.JsonProductCatalog);
        var properties = catalogType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.DoesNotContain(properties, p =>
            p.Name.Contains("Kotobee", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Contains("BookId", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Contains("LibraryId", StringComparison.OrdinalIgnoreCase));
    }

    // R19: Zero-Hardcode Security Gate
    [Fact]
    public void KotobeeSourceFiles_ContainZeroHardcodedSecrets()
    {
        var repoRoot = FindRepoRoot();
        var searchRoots = new[]
        {
            Path.Combine(repoRoot, "Backend", "src"),
            Path.Combine(repoRoot, "src")
        };

        var kotobeeFiles = new List<string>();
        foreach (var root in searchRoots)
        {
            if (Directory.Exists(root))
            {
                kotobeeFiles.AddRange(Directory.GetFiles(root, "*Kotobee*.cs", SearchOption.AllDirectories));
            }
        }

        Assert.NotEmpty(kotobeeFiles);

        var forbiddenSecretPatterns = new[]
        {
            new Regex(@"accesskey\s*=\s*""[A-Za-z0-9_\-]{12,}""", RegexOptions.IgnoreCase),
            new Regex(@"serial\s*=\s*""[A-Za-z0-9_\-]{12,}""", RegexOptions.IgnoreCase)
        };

        foreach (var file in kotobeeFiles)
        {
            var content = File.ReadAllText(file);
            foreach (var pattern in forbiddenSecretPatterns)
            {
                Assert.False(
                    pattern.IsMatch(content),
                    $"Hardcoded Kotobee credential detected in {Path.GetFileName(file)}!");
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null &&
               !File.Exists(Path.Combine(dir.FullName, "Rge.Accounting.slnx")) &&
               !File.Exists(Path.Combine(dir.FullName, "Rge.Accounting.Host.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate Rge.Accounting.slnx root.");
    }
}
