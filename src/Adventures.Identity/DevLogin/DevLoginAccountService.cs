using System.Security.Claims;
using Adventures.Security;

namespace Adventures.Identity;

/// <summary>
/// The developer-testing sign-in of Legacy mode: the account Admin with the password Password, accepted only on a developer machine. It exists
/// so a decoupled infrastructure (no vault, no user store) can still be signed in to; it is not a feature and never a way into a deployed site.
/// It works only when every gate holds: the host registered it (Legacy mode only), <see cref="Enabled"/> was switched on by configuration that is
/// never deployed, the host environment is Development, and the request comes from this machine. Any failed gate answers like a wrong password.
/// </summary>
public sealed record DevLoginOptions(bool Enabled, bool IsDevelopment)
{
    public const string UserName = "Admin";

    /// <summary>Deliberately weak and public: that is why the gates exist.</summary>
    public const string Password = "Password";

    public const string Roles = "Admin";

    /// <summary>The configuration key that turns it on; user-secrets or the Development settings file only, never a deployed setting.</summary>
    public const string SettingName = "Legacy:DevLogin";

    /// <summary>The subject of the issued token (a fixed id so rights resolve the same way every time).</summary>
    public static readonly Guid SubjectId = new("0d3e1a52-7b4c-4e0a-9f5b-2c1d6a8e4b10");
}

public sealed class DevLoginAccountService(DevLoginOptions options, IRequestOrigin origin, IJwtTokenService tokens) : IUserAccountService
{
    public Task<LoginResult> LoginAsync(string tenant, string username, string password, CancellationToken cancellationToken = default)
    {
        var allowed = options.Enabled
            && options.IsDevelopment
            && origin.IsLocal
            && string.Equals(username, DevLoginOptions.UserName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(password, DevLoginOptions.Password, StringComparison.Ordinal);
        if (!allowed)
        {
            return Task.FromResult(LoginResult.Failure(LoginFailureReason.InvalidCredentials));
        }

        var claims = new[] { new Claim(ClaimTypes.Name, DevLoginOptions.UserName), new Claim(WellKnownClaims.Roles, DevLoginOptions.Roles) };
        var issued = tokens.IssueToken(DevLoginOptions.SubjectId.ToString(), claims);
        return Task.FromResult(LoginResult.Success(issued.AccessToken, issued.ExpiresAtUtc, mustChangePassword: false));
    }

    /// <summary>There is no stored password to change.</summary>
    public Task<ChangePasswordResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default) =>
        Task.FromResult(ChangePasswordResult.NotFound);
}
