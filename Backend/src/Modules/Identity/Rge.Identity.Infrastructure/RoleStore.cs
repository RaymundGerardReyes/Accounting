using System.Data;

using System.Data.Common;

using Rge.Identity.Application.Ports;

using Rge.Identity.Domain;



namespace Rge.Identity.Infrastructure;



/// <summary>

/// PostgreSQL repository for querying FinanceUser (users_finance) and RoleAssignment (role_assignments).

/// </summary>

public sealed class RoleStore : IRoleStore

{

    private const string GetUserByIdSql = """

        SELECT id, employee_code, full_name, email, department_id, cost_center_id, employee_group, status

        FROM users_finance

        WHERE id = @user_id

        LIMIT 1;

        """;



    private const string GetUserByIdentifierSql = """

        SELECT id, employee_code, full_name, email, department_id, cost_center_id, employee_group, status

        FROM users_finance

        WHERE LOWER(email) = LOWER(@identifier) OR LOWER(employee_code) = LOWER(@identifier)

        LIMIT 1;

        """;



    private const string GetActiveRolesForUserSql = """

        SELECT id, user_id, role, department_scope, is_active

        FROM role_assignments

        WHERE user_id = @user_id AND is_active = 1;

        """;



    private const string ListUsersSql = """

        SELECT id, employee_code, full_name, email, department_id, cost_center_id, employee_group, status

        FROM users_finance

        ORDER BY full_name ASC;

        """;



    private const string ListAllActiveRolesSql = """

        SELECT id, user_id, role, department_scope, is_active

        FROM role_assignments

        WHERE is_active = 1;

        """;



    private readonly IIdentityDbConnectionFactory _connectionFactory;



    public RoleStore(IIdentityDbConnectionFactory connectionFactory)

    {

        _connectionFactory = connectionFactory;

    }



    public async Task<Actor?> GetActorByIdAsync(

        string userId,

        CancellationToken cancellationToken = default)

    {

        await using DbConnection connection = await _connectionFactory

            .OpenConnectionAsync(cancellationToken)

            .ConfigureAwait(false);



        UserRow? user = await QuerySingleUserAsync(connection, GetUserByIdSql, "@user_id", userId, cancellationToken)

            .ConfigureAwait(false);



        if (user is null)

        {

            return null;

        }



        IReadOnlyList<RoleAssignment> roles = await QueryRolesForUserAsync(connection, user.Id, cancellationToken)

            .ConfigureAwait(false);



        return MapToActor(user, roles);

    }



    public async Task<Actor?> GetActorByEmailOrUsernameAsync(

        string identifier,

        CancellationToken cancellationToken = default)

    {

        await using DbConnection connection = await _connectionFactory

            .OpenConnectionAsync(cancellationToken)

            .ConfigureAwait(false);



        UserRow? user = await QuerySingleUserAsync(connection, GetUserByIdentifierSql, "@identifier", identifier, cancellationToken)

            .ConfigureAwait(false);



        if (user is null)

        {

            return null;

        }



        IReadOnlyList<RoleAssignment> roles = await QueryRolesForUserAsync(connection, user.Id, cancellationToken)

            .ConfigureAwait(false);



        return MapToActor(user, roles);

    }



    public async Task<IReadOnlyList<RoleAssignment>> GetActiveRolesForUserAsync(

        string userId,

        CancellationToken cancellationToken = default)

    {

        await using DbConnection connection = await _connectionFactory

            .OpenConnectionAsync(cancellationToken)

            .ConfigureAwait(false);



        return await QueryRolesForUserAsync(connection, userId, cancellationToken).ConfigureAwait(false);

    }



    public async Task<IReadOnlyList<Actor>> ListFinanceUsersAsync(

        CancellationToken cancellationToken = default)

    {

        await using DbConnection connection = await _connectionFactory

            .OpenConnectionAsync(cancellationToken)

            .ConfigureAwait(false);



        List<UserRow> users = [];

        await using (DbCommand userCmd = connection.CreateCommand())

        {

            userCmd.CommandText = ListUsersSql;

            await using DbDataReader reader = await userCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))

            {

                users.Add(ReadUserRow(reader));

            }

        }



        Dictionary<string, List<RoleAssignment>> rolesByUser = new(StringComparer.OrdinalIgnoreCase);

        await using (DbCommand roleCmd = connection.CreateCommand())

        {

            roleCmd.CommandText = ListAllActiveRolesSql;

            await using DbDataReader reader = await roleCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))

            {

                RoleAssignment assignment = ReadRoleAssignment(reader);

                if (!rolesByUser.TryGetValue(assignment.UserId, out List<RoleAssignment>? list))

                {

                    list = [];

                    rolesByUser[assignment.UserId] = list;

                }

                list.Add(assignment);

            }

        }



        return users

            .Select(u => MapToActor(

                u,

                rolesByUser.TryGetValue(u.Id, out List<RoleAssignment>? r) ? r : []))

            .ToList();

    }



    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "SQL query string is guaranteed to be a const string defined in this class.")]
    private static async Task<UserRow?> QuerySingleUserAsync(

        DbConnection connection,

        string sql,

        string paramName,

        string paramValue,

        CancellationToken cancellationToken)

    {

        await using DbCommand command = connection.CreateCommand();

        command.CommandText = sql;

        DbParameter param = command.CreateParameter();

        param.ParameterName = paramName;

        param.Value = paramValue;

        command.Parameters.Add(param);



        await using DbDataReader reader = await command

            .ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)

            .ConfigureAwait(false);



        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)

            ? ReadUserRow(reader)

            : null;

    }



    private static async Task<IReadOnlyList<RoleAssignment>> QueryRolesForUserAsync(

        DbConnection connection,

        string userId,

        CancellationToken cancellationToken)

    {

        List<RoleAssignment> roles = [];

        await using DbCommand command = connection.CreateCommand();

        command.CommandText = GetActiveRolesForUserSql;

        DbParameter param = command.CreateParameter();

        param.ParameterName = "@user_id";

        param.Value = userId;

        command.Parameters.Add(param);



        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))

        {

            roles.Add(ReadRoleAssignment(reader));

        }



        return roles;

    }



    private static UserRow ReadUserRow(DbDataReader reader) =>

        new(

            Id: reader.GetString(0),

            EmployeeCode: reader.GetString(1),

            FullName: reader.GetString(2),

            Email: reader.GetString(3),

            DepartmentId: reader.GetString(4),

            CostCenterId: reader.GetString(5),

            EmployeeGroup: reader.GetString(6),

            Status: reader.GetString(7));



    private static RoleAssignment ReadRoleAssignment(DbDataReader reader) =>

        new(

            id: reader.GetString(0),

            userId: reader.GetString(1),

            roleName: reader.GetString(2),

            departmentScope: reader.GetString(3),

            isActive: reader.GetInt32(4) == 1);



    private static Actor MapToActor(UserRow row, IEnumerable<RoleAssignment> roles) =>

        new(

            id: row.Id,

            employeeCode: row.EmployeeCode,

            fullName: row.FullName,

            email: row.Email,

            departmentId: row.DepartmentId,

            costCenterId: row.CostCenterId,

            employeeGroup: row.EmployeeGroup,

            status: row.Status,

            roleAssignments: roles);



    private sealed record UserRow(

        string Id,

        string EmployeeCode,

        string FullName,

        string Email,

        string DepartmentId,

        string CostCenterId,

        string EmployeeGroup,

        string Status);

}
