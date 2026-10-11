namespace Adventures.Entities;

/// <summary>
/// The business-rules layer for <see cref="User"/> - storage-agnostic, depends only on
/// <see cref="IEntityRepository{TEntity}"/> (the Dal; no separate wrapper class, same reasoning as
/// <see cref="ISchemaBll"/>). Adds exactly two things to the generic <see cref="IEntityBll{TEntity}"/>: the UserName
/// lookup, and the one business rule - a user can only be deleted by someone else, so the plain
/// <see cref="IEntityBll{TEntity}.DeleteAsync"/> (which has no acting user) is refused. Kept separate from its
/// implementation (<see cref="UserBll"/>) so a versioned or alternate implementation can be swapped in via DI
/// without touching callers. Auto-registered by <c>Adventures.Ioc</c>'s reflection scan via <c>IBll</c>.
/// </summary>
public interface IUserBll : IEntityBll<User>
{
    Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>Tombstones <paramref name="userId"/>. Throws <see cref="Adventures.Common.Exceptions.BusinessRuleException"/> if it is <paramref name="currentUserId"/> - you cannot delete your own account.</summary>
    Task<bool> DeleteAsync(string userId, string currentUserId, CancellationToken cancellationToken = default);
}
