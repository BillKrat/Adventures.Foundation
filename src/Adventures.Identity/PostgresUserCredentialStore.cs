using Adventures.Data;

namespace Adventures.Identity;

/// <summary>
/// PostgreSQL-backed <see cref="IUserCredentialStore"/>, operating against the "user_credentials"
/// vault table (Sql/schema-users.sql in Adventures.Data). Routes all SQL through
/// <see cref="ISqlExecutor"/>, matching the rest of this codebase's convention, so it's
/// unit-testable without a live PostgreSQL instance.
/// </summary>
public sealed class PostgresUserCredentialStore(ISqlExecutor executor) : IUserCredentialStore
{
    private readonly ISqlExecutor _executor = executor ?? throw new ArgumentNullException(nameof(executor));

    public Task<UserCredential?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT user_id AS UserId, password_hash AS PasswordHash, password_algorithm AS PasswordAlgorithm,
                   must_change_password AS MustChangePassword, failed_login_attempts AS FailedLoginAttempts,
                   locked_until AS LockedUntil, last_login_at AS LastLoginAt
            FROM user_credentials
            WHERE user_id = @UserId
            """;

        return _executor.QuerySingleOrDefaultAsync<UserCredential>(sql, new { UserId = userId }, cancellationToken);
    }

    public Task RecordSuccessfulLoginAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE user_credentials
            SET failed_login_attempts = 0, locked_until = NULL, last_login_at = @Now, updated_at = @Now
            WHERE user_id = @UserId
            """;

        return _executor.ExecuteAsync(sql, new { UserId = userId, Now = DateTimeOffset.UtcNow }, cancellationToken);
    }

    public Task RecordFailedLoginAsync(Guid userId, int lockoutThreshold, TimeSpan lockoutDuration, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        const string sql = """
            UPDATE user_credentials
            SET failed_login_attempts = failed_login_attempts + 1,
                locked_until = CASE WHEN failed_login_attempts + 1 >= @LockoutThreshold THEN @LockedUntilIfTripped ELSE locked_until END,
                updated_at = @Now
            WHERE user_id = @UserId
            """;

        return _executor.ExecuteAsync(
            sql,
            new
            {
                UserId = userId,
                LockoutThreshold = lockoutThreshold,
                LockedUntilIfTripped = now + lockoutDuration,
                Now = now,
            },
            cancellationToken);
    }
}
