using System.Reflection;
using Rge.Audit.Domain;
using Rge.BuildingBlocks.SharedKernel.Money;
using Rge.CostAllocation.Domain;
using Rge.Documents.Domain;
using Rge.Finance.Domain.CashAdvances;
using Rge.Identity.Domain;
using Rge.Integrations.Domain;
using Rge.Payments.Domain.Orders;
using Rge.Receipts.Domain;
using Xunit;

namespace Rge.Architecture.Tests;

/// <summary>
/// Enforces Clean Architecture layer dependency rules, strict Modular Monolith boundaries,
/// and the Zero-Float Financial Invariant at build/CI time.
/// </summary>
public sealed class ModularMonolithArchitectureTests
{
    private static readonly Assembly[] DomainAssemblies =
    [
        typeof(Money).Assembly,
        typeof(Order).Assembly,
        typeof(OfficialReceipt).Assembly,
        typeof(CashAdvance).Assembly,
        typeof(Actor).Assembly,
        typeof(AuditEvent).Assembly,
        typeof(DocumentReference).Assembly,
        typeof(CostDepartment).Assembly,
        typeof(GoogleFormSubmission).Assembly
    ];

    [Fact]
    public void Domain_Assemblies_Must_Only_Reference_SharedKernel_And_System_Libraries()
    {
        foreach (Assembly domainAssembly in DomainAssemblies)
        {
            AssemblyName[] references = domainAssembly.GetReferencedAssemblies();

            foreach (AssemblyName reference in references)
            {
                string refName = reference.Name ?? string.Empty;

                bool isSystemOrSharedKernel =
                    refName.StartsWith("System", StringComparison.Ordinal) ||
                    refName.StartsWith("netstandard", StringComparison.Ordinal) ||
                    refName.StartsWith("mscorlib", StringComparison.Ordinal) ||
                    string.Equals(refName, "Rge.BuildingBlocks.SharedKernel", StringComparison.Ordinal);

                Assert.True(
                    isSystemOrSharedKernel,
                    $"Architecture violation: Domain assembly '{domainAssembly.GetName().Name}' illegally references '{refName}'. Domain projects may only reference Rge.BuildingBlocks.SharedKernel.");
            }
        }
    }

    [Fact]
    public void Domain_And_Money_Types_Must_Enforce_Zero_Float_Invariant()
    {
        foreach (Assembly domainAssembly in DomainAssemblies)
        {
            foreach (Type type in domainAssembly.GetExportedTypes())
            {
                foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    Assert.False(
                        prop.PropertyType == typeof(float) || prop.PropertyType == typeof(double) ||
                        prop.PropertyType == typeof(float?) || prop.PropertyType == typeof(double?),
                        $"Zero-Float Invariant violation: Property '{type.FullName}.{prop.Name}' uses floating-point type '{prop.PropertyType.Name}'. Use 'long' minor units (centavos) or 'Money' instead.");
                }

                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    Assert.False(
                        field.FieldType == typeof(float) || field.FieldType == typeof(double) ||
                        field.FieldType == typeof(float?) || field.FieldType == typeof(double?),
                        $"Zero-Float Invariant violation: Field '{type.FullName}.{field.Name}' uses floating-point type '{field.FieldType.Name}'. Use 'long' minor units (centavos) or 'Money' instead.");
                }
            }
        }
    }

    [Fact]
    public void Modules_Must_Never_Reference_Another_Modules_Internal_Projects()
    {
        string[] moduleNames =
        [
            "Payments", "Receipts", "Finance", "Identity",
            "Audit", "CostAllocation", "Documents", "Integrations"
        ];

        Assembly[] loadedRgeAssemblies = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("Rge.", StringComparison.Ordinal) == true)
            .ToArray();

        foreach (Assembly assembly in loadedRgeAssemblies)
        {
            string asmName = assembly.GetName().Name ?? string.Empty;
            string? owningModule = moduleNames.FirstOrDefault(m => asmName.StartsWith($"Rge.{m}.", StringComparison.Ordinal));

            if (owningModule is null)
            {
                continue;
            }

            foreach (AssemblyName refAsm in assembly.GetReferencedAssemblies())
            {
                string refName = refAsm.Name ?? string.Empty;
                string? targetModule = moduleNames.FirstOrDefault(m => refName.StartsWith($"Rge.{m}.", StringComparison.Ordinal));

                if (targetModule is not null && !string.Equals(owningModule, targetModule, StringComparison.Ordinal))
                {
                    // Cross-module reference is ONLY permitted if the target project is Rge.<Module>.Contracts
                    Assert.True(
                        refName.EndsWith(".Contracts", StringComparison.Ordinal),
                        $"Modular Monolith boundary violation: '{asmName}' directly references internal module assembly '{refName}'. Cross-module communication is only permitted via '*.Contracts'.");
                }
            }
        }
    }
}

