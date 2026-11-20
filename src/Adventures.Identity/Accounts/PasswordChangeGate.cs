using Adventures.Data;

namespace Adventures.Identity;

/// <summary>Answers, from the store, whether a person must still change their password before doing anything else.</summary>
public interface IPasswordChangeGate
{
    /// <summary>True while the credential flag or the user's status says the password must be changed; false once changed, and for an unknown or disabled account.</summary>
    Task<bool> IsPendingAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The same rule login uses (credential flag or "must_change_password" status), read live so a token issued before the change stops being held back the
/// moment the password is changed, with no second sign-in.
/// </summary>
public sealed class PasswordChangeGate(IEntityRecordRepository entities, IUserCredentialStore credentials) : IPasswordChangeGate
{
    public async Task<bool> IsPendingAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var credential = await credentials.GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (credential is null)
        {
            return false;
        }

        var entity = await entities.GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (entity is null || entity.EntityType != "user")
        {
            return false;
        }

        var status = UserAccount.FromEntity(entity).Status;
        return status != UserStatus.Disabled && (credential.MustChangePassword || status == UserStatus.MustChangePassword);
    }
}
