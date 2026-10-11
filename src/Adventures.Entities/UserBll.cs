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

    public Task<bool> DeleteAsync(string userId, string currentUserId, CancellationToken cancellationToken = default)
    {
        if (string.Equals(userId, currentUserId, StringComparison.Ordinal))
        {
            throw new BusinessRuleException("You cannot delete your own account.");
        }

        return Repository.DeleteAsync(userId, cancellationToken);
    }

    /// <summary>A user is only ever deleted by someone else; without an acting user the rule cannot be checked.</summary>
    public override Task<bool> DeleteAsync(string entityId, CancellationToken cancellationToken = default) =>
        throw new BusinessRuleException("Deleting a user requires the id of the user performing the deletion.");
}
