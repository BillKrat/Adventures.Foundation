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
}
