using Adventures.Common.Exceptions;

namespace Adventures.Entities;

/// <summary>
/// Who is a platform administrator: a switch on the person, not a role and not tied to any tenant. The platform right (<see cref="Rights.PlatformManage"/>) comes from
/// this switch alone and counts in every tenant, so no tenant administrator can grant it to themselves by editing roles or assignments. The store is append-only
/// (each change is a new statement, the latest one wins) so the history of who switched whom is kept.
/// </summary>
public interface IPlatformAdministrators
{
    /// <summary>Whether the person (by id) is a platform administrator now.</summary>
    Task<bool> IsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>The ids of every current platform administrator.</summary>
    Task<IReadOnlySet<string>> AllAsync(CancellationToken cancellationToken = default);

    /// <summary>Records the switch for the person, on or off, and who made the change. No rule is applied here; see <see cref="PlatformAdministratorBll"/>.</summary>
    Task SetAsync(string userId, bool on, string changedBy, CancellationToken cancellationToken = default);
}

/// <summary>The rules around the switch. The host calls it from an endpoint that already needs the platform right.</summary>
public sealed class PlatformAdministratorBll(IPlatformAdministrators store, IUserBll users)
{
    /// <summary>The user name that receives the switch when nobody has it yet.</summary>
    public const string FirstAdministratorUserName = "Admin";

    /// <summary>
    /// Switches the person on or off. The last platform administrator cannot be switched off (nobody could switch anyone on again). Throws
    /// <see cref="BusinessRuleException"/> for that, and returns false when the person does not exist.
    /// </summary>
    public async Task<bool> SetAsync(string userId, bool on, string changedBy, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (await users.GetAsync(userId, cancellationToken).ConfigureAwait(false) is null)
        {
            return false;
        }

        var current = await store.AllAsync(cancellationToken).ConfigureAwait(false);
        if (current.Contains(userId) == on)
        {
            return true;
        }

        if (!on && current.Count <= 1)
        {
            throw new BusinessRuleException("This is the last platform administrator. Make someone else one first, so the platform is never left without.");
        }

        await store.SetAsync(userId, on, changedBy, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Startup rule: when nobody is a platform administrator yet, the person named <see cref="FirstAdministratorUserName"/> becomes one. Does nothing when someone already is
    /// (so a person switched off on purpose stays off) or when that person does not exist. Returns whether it granted the switch.
    /// </summary>
    public async Task<bool> EnsureFirstAsync(CancellationToken cancellationToken = default)
    {
        if ((await store.AllAsync(cancellationToken).ConfigureAwait(false)).Count > 0
            || await users.FindByUserNameAsync(FirstAdministratorUserName, cancellationToken).ConfigureAwait(false) is not { EntityId: { } id })
        {
            return false;
        }

        await store.SetAsync(id.Value, true, "startup", cancellationToken).ConfigureAwait(false);
        return true;
    }
}
