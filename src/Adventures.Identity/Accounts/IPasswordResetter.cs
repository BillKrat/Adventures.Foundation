namespace Adventures.Identity;

/// <summary>What <see cref="IPasswordResetter"/> did.</summary>
public enum ResetOutcome
{
    /// <summary>The password was replaced, the lockout cleared and any "must change" flag removed.</summary>
    Reset,

    /// <summary>No such account in the tenant, or the account is disabled; nothing changed.</summary>
    NotFound,

    /// <summary>The new password does not meet <see cref="PasswordPolicy"/>; nothing changed.</summary>
    Rejected,
}

/// <summary>
/// Sets a new password for an account WITHOUT the current one. Only for a caller that has already proved ownership some other way (a mailed single-use link to a verified
/// address); never expose it directly to a request.
/// </summary>
public interface IPasswordResetter
{
    Task<ResetOutcome> ResetAsync(string tenant, string username, string newPassword, CancellationToken cancellationToken = default);
}
