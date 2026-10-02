using Adventures.Common.Interfaces;

namespace Adventures.Entities;

/// <summary>
/// The business-rules layer for <see cref="User"/> - storage-agnostic, depends only on
/// <see cref="IEntityRepository{TEntity}"/> (the Dal; no separate wrapper class, same reasoning as
/// <see cref="ISchemaBll"/>). Kept separate from its implementation (<see cref="UserBll"/>) so a
/// versioned or alternate implementation can be swapped in via DI without touching callers.
/// Auto-registered by <c>Adventures.Ioc</c>'s reflection scan via <see cref="IBll"/>/
/// <see cref="Adventures.Ioc.Interfaces.IScopedLifetime"/>, resolved the same way
/// <c>MockBll</c> is resolved in the MvpVm tests.
/// </summary>
public interface IUserBll : IBll
{
    Task<User?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default);

    Task<User> CreateAsync(User user, CancellationToken cancellationToken = default);

    Task<User?> GetAsync(string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default);

    Task UpdateAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>Tombstones <paramref name="userId"/>. Throws if it is <paramref name="currentUserId"/> - the one business rule: you cannot delete your own account.</summary>
    Task<bool> DeleteAsync(string userId, string currentUserId, CancellationToken cancellationToken = default);
}
