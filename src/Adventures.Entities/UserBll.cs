using Adventures.Common.Exceptions;

namespace Adventures.Entities;

/// <summary>Implementation of <see cref="IUserBll"/> - see that interface for the contract and rationale.</summary>
public sealed class UserBll(IEntityRepository<User> users) : EntityBll<User>(users), IUserBll
{
    /// <summary>
    /// UserName is meant to be a system-wide unique lookup key, but nothing enforces that yet - this returns the
    /// first match, same as the old Postgres-backed system's own documented-but-unenforced convention. Scans the
    /// whole list: fine for the in-memory store, to be replaced by a Dal query before real data volumes.
    /// </summary>
    public async Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        var all = await ListAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(user =>
            string.Equals(user.GetValue("UserName")?.ToString(), userName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The user name is the sign-in name and the lookup key: letters, digits, dot, dash and underscore (3 to 64), and nobody else may already have it.</summary>
    public override async Task<User> CreateAsync(User entity, CancellationToken cancellationToken = default)
    {
        var name = entity.GetValue("UserName")?.ToString()?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            throw new BusinessRuleException("A user name is required.");
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9._-]{3,64}$"))
        {
            throw new BusinessRuleException("The user name may use letters, digits, dot, dash and underscore only, 3 to 64 characters, no spaces.");
        }

        if (await FindByUserNameAsync(name, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new BusinessRuleException("That user name is already taken.");
        }

        entity.Set("UserName", Guid.NewGuid().ToString(), name);
        return await base.CreateAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> DeleteAsync(string userId, string currentUserId, CancellationToken cancellationToken = default)
    {
        if (string.Equals(userId, currentUserId, StringComparison.Ordinal))
        {
            throw new BusinessRuleException("You cannot delete your own account.");
        }

        return Repository.DeleteAsync(userId, cancellationToken);
    }

    public override Task<int> DeleteManyAsync(IReadOnlyCollection<string> entityIds, CancellationToken cancellationToken = default) =>
        throw new BusinessRuleException("Deleting users requires the id of the user performing the deletion.");

    /// <summary>A user is only ever deleted by someone else; without an acting user the rule cannot be checked.</summary>
    public override Task<bool> DeleteAsync(string entityId, CancellationToken cancellationToken = default) =>
        throw new BusinessRuleException("Deleting a user requires the id of the user performing the deletion.");
}
