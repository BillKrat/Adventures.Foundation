// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using System.Security.Claims;
using System.Text.Json.Nodes;
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

        var claims = new[] { new Claim(ClaimTypes.Name, account.Username), new Claim(WellKnownClaims.Roles, string.Join(' ', account.Roles)) };
        var issued = tokenService.IssueToken(account.Id.ToString(), claims);

        var mustChangePassword = credential.MustChangePassword || account.Status == UserStatus.MustChangePassword;
        return LoginResult.Success(issued.AccessToken, issued.ExpiresAtUtc, mustChangePassword);
    }

    public async Task<ChangePasswordResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);

        var credential = await credentialStore.GetAsync(userId, cancellationToken);
        if (credential is null)
        {
            return ChangePasswordResult.NotFound;
        }

        if (credential.LockedUntil is { } lockedUntil && lockedUntil > DateTimeOffset.UtcNow)
        {
            return ChangePasswordResult.AccountLocked;
        }

        if (!passwordHasher.Verify(currentPassword, credential.PasswordHash))
        {
            await credentialStore.RecordFailedLoginAsync(userId, LockoutThreshold, LockoutDuration, cancellationToken);
            return ChangePasswordResult.InvalidCurrentPassword;
        }

        if (!PasswordPolicy.IsAcceptable(newPassword, currentPassword))
        {
            return ChangePasswordResult.NewPasswordRejected;
        }

        await credentialStore.SetPasswordAsync(userId, passwordHasher.Hash(newPassword), credential.PasswordAlgorithm, cancellationToken);
        await ClearMustChangeStatusAsync(userId, cancellationToken);
        return ChangePasswordResult.Succeeded;
    }

    /// <summary>
    /// The user entity carries its own "must_change_password" status, and login reports a change as required if either it or the
    /// credential flag is set - so after a change the status goes back to "active" too (a disabled account is left alone).
    /// </summary>
    private async Task ClearMustChangeStatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var entity = await entityRepository.GetAsync(userId, cancellationToken);
        if (entity is null
            || JsonNode.Parse(entity.StandardFieldsJson) is not JsonObject fields
            || fields["status"]?.GetValue<string>() != "must_change_password")
        {
            return;
        }

        fields["status"] = "active";
        await entityRepository.UpdateAsync(entity with { StandardFieldsJson = fields.ToJsonString() }, cancellationToken);
    }
}
