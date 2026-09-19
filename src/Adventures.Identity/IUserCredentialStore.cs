namespace Adventures.Identity;

/// <summary>
/// Access to the "user_credentials" vault table (Sql/schema-users.sql) - password hash, lockout
/// state, login timestamps. Deliberately a separate interface from <c>Adventures.Data.IEntityRepository</c>:
/// only this store's implementation, and the identity service composing it, should ever see a
/// password hash. Everything else in the system only ever sees a user's entities.id.
/// </summary>
public interface IUserCredentialStore
{
    /// <summary>Retrieves the credential row for a user, or null if none exists (e.g. the entity exists but was never provisioned with a vault row).</summary>
    Task<UserCredential?> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Resets failed_login_attempts to 0, clears any lockout, and stamps last_login_at to now. Call after a successful login.</summary>
    Task RecordSuccessfulLoginAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments failed_login_attempts by one, and sets locked_until (now + <paramref name="lockoutDuration"/>)
    /// if the new count reaches <paramref name="lockoutThreshold"/>. Call after a failed password check.
    /// </summary>
    Task RecordFailedLoginAsync(Guid userId, int lockoutThreshold, TimeSpan lockoutDuration, CancellationToken cancellationToken = default);
}
