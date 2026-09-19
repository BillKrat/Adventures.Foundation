namespace Adventures.Identity;

/// <summary>
/// Deliberately coarse: unknown-username and wrong-password both map to
/// <see cref="InvalidCredentials"/> so a caller can't use the failure reason to enumerate which
/// usernames exist. <see cref="AccountDisabled"/>/<see cref="AccountLocked"/> are surfaced
/// distinctly since, unlike "does this username exist," disclosing those doesn't help an attacker.
/// </summary>
public enum LoginFailureReason
{
    None,
    InvalidCredentials,
    AccountDisabled,
    AccountLocked,
}

/// <summary>Outcome of <see cref="IUserAccountService.LoginAsync"/>.</summary>
public sealed record LoginResult(bool Succeeded, string? AccessToken, DateTimeOffset? ExpiresAtUtc, bool MustChangePassword, LoginFailureReason FailureReason)
{
    public static LoginResult Success(string accessToken, DateTimeOffset expiresAtUtc, bool mustChangePassword) =>
        new(true, accessToken, expiresAtUtc, mustChangePassword, LoginFailureReason.None);

    public static LoginResult Failure(LoginFailureReason reason) =>
        new(false, null, null, false, reason);
}
