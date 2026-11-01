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
        SELECT u.id, u.employee_code, u.full_name, u.email, u.department_id, u.cost_center_id, u.employee_group, u.status
        FROM users_finance u
        LEFT JOIN user_credentials c ON c.user_id = u.id
        WHERE LOWER(u.email) = LOWER(@identifier)
           OR LOWER(c.email) = LOWER(@identifier)
           OR LOWER(c.username) = LOWER(@identifier)
           OR LOWER(u.employee_code) = LOWER(@identifier)
           OR LOWER(u.id) = LOWER(@identifier)
        LIMIT 1;
        """;

    private const string UpsertUserFinanceSql = """
        INSERT INTO users_finance (id, employee_code, full_name, email, department_id, cost_center_id, employee_group, status)
        VALUES (@id, @employee_code, @full_name, @email, @department_id, @cost_center_id, @employee_group, @status)
        ON CONFLICT (id) DO UPDATE SET
            employee_code  = EXCLUDED.employee_code,
            full_name      = EXCLUDED.full_name,
            email          = EXCLUDED.email,
            department_id  = EXCLUDED.department_id,
            cost_center_id = EXCLUDED.cost_center_id,
            employee_group = EXCLUDED.employee_group,
            status         = EXCLUDED.status;
        """;

    private const string UpsertUserCredentialsSql = """
        INSERT INTO user_credentials (user_id, username, email, password_hash, failed_attempts, locked_until)
        VALUES (
            @user_id,
            @username,
            @email,
            public.crypt(@password, public.gen_salt('bf', 10)),
            0,
            NULL
        )
        ON CONFLICT (user_id) DO UPDATE SET
            username        = EXCLUDED.username,
            email           = EXCLUDED.email,
            password_hash   = CASE WHEN @password IS NOT NULL AND @password <> '' THEN public.crypt(@password, public.gen_salt('bf', 10)) ELSE user_credentials.password_hash END,
            failed_attempts = 0,
            locked_until    = NULL;
        """;

    private const string DeleteRoleAssignmentsForUserSql = """
        DELETE FROM role_assignments
        WHERE user_id = @user_id;
        """;

    private const string InsertRoleAssignmentSql = """
        INSERT INTO role_assignments (id, user_id, role, department_scope, is_active)
        VALUES (@id, @user_id, @role, @department_scope, 1)
        ON CONFLICT (user_id, role, department_scope) DO UPDATE SET
            is_active = 1;
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



    public async Task<Actor> FindOrProvisionActorAsync(
        string email,
        string fullName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);

        Actor? existing = await GetActorByEmailOrUsernameAsync(email, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        string userId = $"usr_oauth_{Guid.NewGuid():N}"[..18];
        string employeeCode = $"EMP-OAUTH-{System.Security.Cryptography.RandomNumberGenerator.GetInt32(1000, 9999)}";
        string displayName = string.IsNullOrWhiteSpace(fullName) ? email.Split('@')[0] : fullName;

        var roles = new List<RoleAssignment>
        {
            new($"ra_oauth_{userId}", userId, Role.Employee, "*")
        };

        return new Actor(
            id: userId,
            employeeCode: employeeCode,
            fullName: displayName,
            email: email,
            departmentId: "finance",
            costCenterId: "CC-FIN",
            employeeGroup: "management",
            status: "active",
            roleAssignments: roles);
    }

    public async Task UpsertUserCredentialsAndRolesAsync(
        string userId,
        string employeeCode,
        string fullName,
        string email,
        string username,
        string? password,
        string departmentId,
        string costCenterId,
        string employeeGroup,
        IReadOnlyCollection<string> roles,
        string departmentScope,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentNullException.ThrowIfNull(roles);

        await using DbConnection connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 1. Upsert users_finance
            await using (DbCommand cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = UpsertUserFinanceSql;
                AddParameter(cmd, "@id", userId);
                AddParameter(cmd, "@employee_code", employeeCode);
                AddParameter(cmd, "@full_name", fullName);
                AddParameter(cmd, "@email", email);
                AddParameter(cmd, "@department_id", departmentId);
                AddParameter(cmd, "@cost_center_id", costCenterId);
                AddParameter(cmd, "@employee_group", employeeGroup);
                AddParameter(cmd, "@status", isActive ? "active" : "suspended");
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 2. Upsert user_credentials
            await using (DbCommand cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = UpsertUserCredentialsSql;
                AddParameter(cmd, "@user_id", userId);
                AddParameter(cmd, "@username", username);
                AddParameter(cmd, "@email", email);
                AddParameter(cmd, "@password", string.IsNullOrWhiteSpace(password) ? "password123" : password);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 3. Delete existing role_assignments for user
            await using (DbCommand cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = DeleteRoleAssignmentsForUserSql;
                AddParameter(cmd, "@user_id", userId);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 4. Insert new active role assignments
            foreach (string roleName in roles)
            {
                if (string.IsNullOrWhiteSpace(roleName))
                {
                    continue;
                }

                await using DbCommand cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = InsertRoleAssignmentSql;
                string roleId = $"ra_{userId}_{roleName}_{Guid.NewGuid():N}";
                if (roleId.Length > 36)
                {
                    roleId = roleId[..36];
                }

                AddParameter(cmd, "@id", roleId);
                AddParameter(cmd, "@user_id", userId);
                AddParameter(cmd, "@role", roleName.Trim().ToLowerInvariant());
                AddParameter(cmd, "@department_scope", departmentScope);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        DbParameter param = command.CreateParameter();
        param.ParameterName = name;
        param.Value = value ?? DBNull.Value;
        command.Parameters.Add(param);
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
