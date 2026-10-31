using Rge.BuildingBlocks.SharedKernel.Guards;
using Rge.BuildingBlocks.SharedKernel.Primitives;

namespace Rge.Finance.Domain.Vendors;

/// <summary>
/// Entity representing a tax code and withholding tax definition (VAT, EWT, Non-VAT, Exempt).
/// </summary>
public sealed class TaxCode : Entity<string>
{
    public string Code => Id;
    public string Description { get; private set; }
    public string TaxType { get; private set; }
    public long RateBps { get; private set; }
    public bool RequiresBir2307 { get; private set; }
    public bool CpaApproved { get; private set; }
    public string? ApprovedById { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public TaxCode(
        string code,
        string description,
        string taxType,
        long rateBps,
        bool requiresBir2307,
        bool cpaApproved,
        string? approvedById,
        DateTimeOffset nowUtc) : base(Guard.AgainstNullOrWhiteSpace(code).Trim().ToUpperInvariant())
    {
        Description = Guard.AgainstNullOrWhiteSpace(description);
        TaxType = Guard.AgainstNullOrWhiteSpace(taxType).ToUpperInvariant();
        RateBps = rateBps;
        RequiresBir2307 = requiresBir2307;
        CpaApproved = cpaApproved;
        ApprovedById = approvedById;
        CreatedAtUtc = nowUtc;
    }

    public void ApproveByCpa(string cpaUserId)
    {
        CpaApproved = true;
        ApprovedById = Guard.AgainstNullOrWhiteSpace(cpaUserId);
    }
}
