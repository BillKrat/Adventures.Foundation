// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Identity;

/// <summary>
/// Verifies human-user logins against the entity store + credential vault (Adventures.Data) and,
/// on success, issues a JWT (via Adventures.Security's IJwtTokenService) whose <c>sub</c> claim is
/// the user's entities.id UUID - never a password or secret. This is the "once a user
/// authenticates, credentials never get passed around again" design goal: an MCP server or any
/// other caller that needs to check permissions or fetch profile data looks the UUID up itself
/// against this same vault, rather than receiving a password.
/// </summary>
public interface IUserAccountService
{
    Task<LoginResult> LoginAsync(string tenant, string username, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes a user's own password: verifies <paramref name="currentPassword"/> (a wrong one counts toward the same lockout as
    /// a failed login), enforces <see cref="PasswordPolicy"/> on <paramref name="newPassword"/>, stores the new hash, and clears
    /// the must-change flag on both the credential row and the user entity's status.
    /// </summary>
    Task<ChangePasswordResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
}
