namespace Adventures.Identity;

/// <summary>A row from the "user_credentials" vault table (Sql/schema-users.sql).</summary>
/// <param name="UserId">The owning entity's id (entities.id, entity_type = "user").</param>
/// <param name="PasswordHash">Opaque hash string, format depends on <see cref="PasswordAlgorithm"/> (see <c>IPasswordHasher</c>).</param>
/// <param name="PasswordAlgorithm">
/// Currently always "pbkdf2-sha256" - recorded per-row so a future algorithm change doesn't
/// invalidate already-stored hashes. Not yet branched on by <c>UserAccountService</c>, since only
/// one algorithm (<c>Adventures.Security.PasswordHasher</c>) exists so far.
/// </param>
/// <param name="MustChangePassword">Forces a password-change flow on next login (e.g. a freshly-seeded tenant admin).</param>
/// <param name="FailedLoginAttempts">Consecutive failed attempts since the last successful login or lockout reset.</param>
/// <param name="LockedUntil">If set and in the future, login is blocked regardless of password correctness.</param>
/// <param name="LastLoginAt">Timestamp of the last successful login, or null if never logged in.</param>
public sealed record UserCredential(
    Guid UserId,
    string PasswordHash,
    string PasswordAlgorithm,
    bool MustChangePassword,
    int FailedLoginAttempts,
    DateTimeOffset? LockedUntil,
    DateTimeOffset? LastLoginAt);
