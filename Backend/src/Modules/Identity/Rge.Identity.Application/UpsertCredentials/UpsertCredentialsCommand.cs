using Rge.BuildingBlocks.Application.Abstractions;

namespace Rge.Identity.Application.UpsertCredentials;

public sealed record UpsertCredentialsCommand(
    string UserId,
    string? EmployeeCode,
    string FullName,
    string Email,
    string? Username,
    string? Password,
    string? DepartmentId,
    string? CostCenterId,
    string? EmployeeGroup,
    IReadOnlyCollection<string>? Roles,
    string? DepartmentScope,
    bool? IsActive) : ICommand<UpsertCredentialsResponse>;

public sealed record UpsertCredentialsResponse(
    string UserId,
    string FullName,
    string Email,
    IReadOnlyCollection<string> Roles,
    DateTimeOffset UpdatedAt);
