// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Data;

using Adventures.Identity;

namespace Adventures.Identity.Postgres;

/// <summary>
/// PostgreSQL-backed <see cref="IUserCredentialStore"/>, operating against the "user_credentials"
/// vault table (Sql/schema-users.sql in Adventures.Data.Postgres). Routes all SQL through
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

    public Task CreateOrResetAsync(Guid userId, string passwordHash, string passwordAlgorithm, bool mustChangePassword, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO user_credentials (user_id, password_hash, password_algorithm, must_change_password)
            VALUES (@UserId, @PasswordHash, @PasswordAlgorithm, @MustChangePassword)
            ON CONFLICT (user_id) DO UPDATE SET
                password_hash = EXCLUDED.password_hash,
                password_algorithm = EXCLUDED.password_algorithm,
                must_change_password = EXCLUDED.must_change_password,
                failed_login_attempts = 0,
                locked_until = NULL,
                updated_at = now()
            """;

        return _executor.ExecuteAsync(
            sql,
            new { UserId = userId, PasswordHash = passwordHash, PasswordAlgorithm = passwordAlgorithm, MustChangePassword = mustChangePassword },
            cancellationToken);
    }

    public Task SetPasswordAsync(Guid userId, string passwordHash, string passwordAlgorithm, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE user_credentials
            SET password_hash = @PasswordHash, password_algorithm = @PasswordAlgorithm, must_change_password = false,
                failed_login_attempts = 0, locked_until = NULL, updated_at = @Now
            WHERE user_id = @UserId
            """;

        return _executor.ExecuteAsync(
            sql,
            new { UserId = userId, PasswordHash = passwordHash, PasswordAlgorithm = passwordAlgorithm, Now = DateTimeOffset.UtcNow },
            cancellationToken);
    }
}
