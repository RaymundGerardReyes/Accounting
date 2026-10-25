using Rge.BuildingBlocks.SharedKernel.Guards;

using Rge.BuildingBlocks.SharedKernel.Primitives;



namespace Rge.Identity.Domain;



/// <summary>

/// Represents an OAuth 2.0 PKCE state record with a 10-minute TTL and single-use consumption invariant.

/// </summary>

public sealed class OAuthState : Entity<string>

{

    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);



    public string Provider { get; }

    public string CodeVerifier { get; }

    public string RedirectUri { get; }

    public DateTimeOffset ExpiresAtUtc { get; }

    public DateTimeOffset? ConsumedAtUtc { get; private set; }



    public OAuthState(

        string state,

        string provider,

        string codeVerifier,

        string redirectUri,

        DateTimeOffset expiresAtUtc,

        DateTimeOffset? consumedAtUtc = null)

        : base(Guard.AgainstNullOrWhiteSpace(state))

    {

        Provider = Guard.AgainstNullOrWhiteSpace(provider).ToLowerInvariant();

        CodeVerifier = Guard.AgainstNullOrWhiteSpace(codeVerifier);

        RedirectUri = Guard.AgainstNullOrWhiteSpace(redirectUri);

        ExpiresAtUtc = expiresAtUtc;

        ConsumedAtUtc = consumedAtUtc;

    }



    public bool CanConsume(DateTimeOffset nowUtc) =>

        ConsumedAtUtc is null && ExpiresAtUtc > nowUtc;



    public void MarkConsumed(DateTimeOffset nowUtc)

    {

        if (!CanConsume(nowUtc))

        {

            throw new InvalidOperationException("OAuth state is expired or already consumed.");

        }



        ConsumedAtUtc = nowUtc;

    }

}
