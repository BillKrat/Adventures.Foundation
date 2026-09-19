using System.Security.Claims;
using Adventures.Data;
using Adventures.Security;

namespace Adventures.Identity;

/// <summary>
/// Default <see cref="IUserAccountService"/> implementation, composing <see cref="IEntityRepository"/>
/// (find the user entity by tenant+username), <see cref="IUserCredentialStore"/> (the vault row),
/// <see cref="IPasswordHasher"/> (verify), and <see cref="IJwtTokenService"/> (issue).
/// </summary>
public sealed class UserAccountService(
    IEntityRepository entityRepository,
    IUserCredentialStore credentialStore,
    IPasswordHasher passwordHasher,
    IJwtTokenService tokenService) : IUserAccountService
{
    // Not yet configurable - revisit if a real tenant ever needs a different policy. 5 attempts /
    // 15 minutes is a common, unremarkable default for this class of lockout.
    private const int LockoutThreshold = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<LoginResult> LoginAsync(string tenant, string username, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var entity = await entityRepository.FindByStandardFieldAsync(tenant, "user", "username", username, cancellationToken);
        if (entity is null)
        {
            return LoginResult.Failure(LoginFailureReason.InvalidCredentials);
        }

        var account = UserAccount.FromEntity(entity);

        var credential = await credentialStore.GetAsync(account.Id, cancellationToken);
        if (credential is null)
        {
            // Entity exists but was never provisioned with a vault row - must not be
            // distinguishable from a wrong password, so this is NOT a 500/exception case.
            return LoginResult.Failure(LoginFailureReason.InvalidCredentials);
        }

        if (credential.LockedUntil is { } lockedUntil && lockedUntil > DateTimeOffset.UtcNow)
        {
            return LoginResult.Failure(LoginFailureReason.AccountLocked);
        }

        if (account.Status == UserStatus.Disabled)
        {
            return LoginResult.Failure(LoginFailureReason.AccountDisabled);
        }

        if (!passwordHasher.Verify(password, credential.PasswordHash))
        {
            await credentialStore.RecordFailedLoginAsync(account.Id, LockoutThreshold, LockoutDuration, cancellationToken);
            return LoginResult.Failure(LoginFailureReason.InvalidCredentials);
        }

        await credentialStore.RecordSuccessfulLoginAsync(account.Id, cancellationToken);

        var claims = new[] { new Claim(ClaimTypes.Name, account.Username), new Claim("roles", string.Join(' ', account.Roles)) };
        var issued = tokenService.IssueToken(account.Id.ToString(), claims);

        var mustChangePassword = credential.MustChangePassword || account.Status == UserStatus.MustChangePassword;
        return LoginResult.Success(issued.AccessToken, issued.ExpiresAtUtc, mustChangePassword);
    }
}
