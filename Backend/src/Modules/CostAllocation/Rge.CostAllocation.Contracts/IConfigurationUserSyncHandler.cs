namespace Rge.CostAllocation.Contracts;

/// <summary>
/// Service contract to synchronize users defined in Configuration into the platform's
/// authoritative identity and RBAC role store.
/// </summary>
public interface IConfigurationUserSyncHandler
{
    Task SyncConfigurationUsersAsync(object usersPayload, CancellationToken cancellationToken = default);
}

