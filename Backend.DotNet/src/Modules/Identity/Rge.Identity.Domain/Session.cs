using Rge.BuildingBlocks.SharedKernel.Guards;

using Rge.BuildingBlocks.SharedKernel.Primitives;



namespace Rge.Identity.Domain;



/// <summary>

/// Represents an authenticated server-side user session.

/// Only the SHA-256 TokenHash is stored; the raw 256-bit token resides only in the client's HttpOnly cookie.

/// </summary>

public sealed class Session : Entity<string>

{

    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(8);



    public string UserId { get; private set; }

    public string TokenHash { get; }

    public string? IpAddress { get; }

    public string? UserAgent { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public string? ActAsRole { get; private set; }

    public string? ActAsDepartmentScope { get; private set; }



    public Session(

        string id,

        string userId,

        string tokenHash,

        DateTimeOffset createdAtUtc,

        DateTimeOffset expiresAtUtc,

        string? ipAddress = null,

        string? userAgent = null,

        DateTimeOffset? revokedAtUtc = null,

        string? actAsRole = null,

        string? actAsDepartmentScope = null)

        : base(Guard.AgainstNullOrWhiteSpace(id))

    {

        UserId = Guard.AgainstNullOrWhiteSpace(userId);

        TokenHash = Guard.AgainstNullOrWhiteSpace(tokenHash);

        CreatedAtUtc = createdAtUtc;

        ExpiresAtUtc = expiresAtUtc;

        IpAddress = ipAddress;

        UserAgent = userAgent;

        RevokedAtUtc = revokedAtUtc;

        ActAsRole = actAsRole;

        ActAsDepartmentScope = actAsDepartmentScope;

    }



    public bool IsActive(DateTimeOffset nowUtc) =>

        RevokedAtUtc is null && ExpiresAtUtc > nowUtc;



    public void Revoke(DateTimeOffset nowUtc)

    {

        RevokedAtUtc ??= nowUtc;

    }



    public void ApplyDevActAsOverride(string? roleName, string? departmentScope)

    {

        ActAsRole = string.IsNullOrWhiteSpace(roleName) ? null : new Role(roleName).Name;

        ActAsDepartmentScope = string.IsNullOrWhiteSpace(departmentScope) ? null : departmentScope.Trim();

    }



    public void SwitchDevPersona(string targetUserId)

    {

        UserId = Guard.AgainstNullOrWhiteSpace(targetUserId);

        ActAsRole = null;

        ActAsDepartmentScope = null;

    }

}
