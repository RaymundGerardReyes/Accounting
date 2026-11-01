using Rge.BuildingBlocks.Application.Abstractions;
using Rge.BuildingBlocks.SharedKernel.Primitives;
using Rge.BuildingBlocks.SharedKernel.Results;
using Rge.Identity.Application.Ports;
using Rge.Identity.Domain;

namespace Rge.Identity.Application.UpsertCredentials;

public sealed class UpsertCredentialsHandler : ICommandHandler<UpsertCredentialsCommand, UpsertCredentialsResponse>
{
    private readonly IRoleStore _roleStore;
    private readonly IActorCache _actorCache;
    private readonly IClock _clock;

    public UpsertCredentialsHandler(
        IRoleStore roleStore,
        IActorCache actorCache,
        IClock? clock = null)
    {
        _roleStore = roleStore ?? throw new ArgumentNullException(nameof(roleStore));
        _actorCache = actorCache ?? throw new ArgumentNullException(nameof(actorCache));
        _clock = clock ?? SystemClock.Instance;
    }

    public async Task<Result<UpsertCredentialsResponse>> HandleAsync(
        UpsertCredentialsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.UserId))
        {
            return Result.Failure<UpsertCredentialsResponse>(
                Error.Validation("Identity.Credentials.MissingUserId", "user_id is required."));
        }

        if (string.IsNullOrWhiteSpace(command.FullName))
        {
            return Result.Failure<UpsertCredentialsResponse>(
                Error.Validation("Identity.Credentials.MissingFullName", "full_name is required."));
        }

        if (string.IsNullOrWhiteSpace(command.Email) || !command.Email.Contains('@', StringComparison.Ordinal))
        {
            return Result.Failure<UpsertCredentialsResponse>(
                Error.Validation("Identity.Credentials.InvalidEmail", "A valid email is required."));
        }

        var normalizedRoles = new List<string>();
        if (command.Roles is not null && command.Roles.Count > 0)
        {
            foreach (string role in command.Roles)
            {
                if (string.IsNullOrWhiteSpace(role))
                {
                    continue;
                }

                string trimmedRole = role.Trim().ToLowerInvariant();
                if (!Role.IsValidRoleName(trimmedRole))
                {
                    return Result.Failure<UpsertCredentialsResponse>(
                        Error.Validation("Identity.Credentials.InvalidRole", $"Role '{role}' is not a valid RBAC role."));
                }

                if (!normalizedRoles.Contains(trimmedRole, StringComparer.OrdinalIgnoreCase))
                {
                    normalizedRoles.Add(trimmedRole);
                }
            }
        }

        if (normalizedRoles.Count == 0)
        {
            normalizedRoles.Add(Role.Employee);
        }

        string userId = command.UserId.Trim();
        string employeeCode = !string.IsNullOrWhiteSpace(command.EmployeeCode) ? command.EmployeeCode.Trim() : userId;
        string fullName = command.FullName.Trim();
        string email = command.Email.Trim().ToLowerInvariant();
        string username = !string.IsNullOrWhiteSpace(command.Username) ? command.Username.Trim().ToLowerInvariant() : email.Split('@')[0];
        string departmentId = !string.IsNullOrWhiteSpace(command.DepartmentId) ? command.DepartmentId.Trim().ToLowerInvariant() : "finance";
        string costCenterId = !string.IsNullOrWhiteSpace(command.CostCenterId) ? command.CostCenterId.Trim().ToUpperInvariant() : "CC-FIN";
        string employeeGroup = !string.IsNullOrWhiteSpace(command.EmployeeGroup) ? command.EmployeeGroup.Trim().ToLowerInvariant() : "management";
        string departmentScope = !string.IsNullOrWhiteSpace(command.DepartmentScope) ? command.DepartmentScope.Trim().ToLowerInvariant() : RoleAssignment.CorporateWideScope;
        bool isActive = command.IsActive ?? true;

        await _roleStore.UpsertUserCredentialsAndRolesAsync(
            userId: userId,
            employeeCode: employeeCode,
            fullName: fullName,
            email: email,
            username: username,
            password: command.Password,
            departmentId: departmentId,
            costCenterId: costCenterId,
            employeeGroup: employeeGroup,
            roles: normalizedRoles,
            departmentScope: departmentScope,
            isActive: isActive,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // Immediately invalidate ActorCache so that role and permission changes take effect on the next request
        _actorCache.Invalidate(userId);

        DateTimeOffset nowUtc = _clock.UtcNow;
        return Result.Success(new UpsertCredentialsResponse(
            UserId: userId,
            FullName: fullName,
            Email: email,
            Roles: normalizedRoles,
            UpdatedAt: nowUtc));
    }

    private sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new();
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
