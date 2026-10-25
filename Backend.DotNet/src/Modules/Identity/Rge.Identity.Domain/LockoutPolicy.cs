namespace Rge.Identity.Domain;



/// <summary>

/// Domain rules for credential lockout after consecutive failed login attempts.

/// Mirrors the PostgreSQL fn_authenticate_user stored function invariant.

/// </summary>

public static class LockoutPolicy

{

    public const int MaxFailedAttempts = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);



    public static bool IsLockedOut(DateTimeOffset? lockedUntilUtc, DateTimeOffset nowUtc) =>

        lockedUntilUtc.HasValue && lockedUntilUtc.Value > nowUtc;



    public static DateTimeOffset? ComputeLockoutExpiry(int consecutiveFailures, DateTimeOffset nowUtc) =>

        consecutiveFailures >= MaxFailedAttempts

            ? nowUtc.Add(LockoutDuration)

            : null;

}
