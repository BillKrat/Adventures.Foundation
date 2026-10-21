namespace Adventures.Identity;

/// <summary>Outcome of <see cref="IUserAccountService.ChangePasswordAsync"/>.</summary>
public enum ChangePasswordResult
{
    Succeeded,
    InvalidCurrentPassword,
    AccountLocked,

    /// <summary>The new password broke <see cref="PasswordPolicy"/> (too short, or the same as the current one).</summary>
    NewPasswordRejected,

    /// <summary>No such user, or one with no credential row.</summary>
    NotFound,
}

/// <summary>The rule for a new password, shared so the API can report it to the client.</summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 12;

    public static string Description => $"At least {MinimumLength} characters, and different from the current password.";

    public static bool IsAcceptable(string newPassword, string currentPassword) =>
        newPassword is { Length: >= MinimumLength } && !string.Equals(newPassword, currentPassword, StringComparison.Ordinal);
}
