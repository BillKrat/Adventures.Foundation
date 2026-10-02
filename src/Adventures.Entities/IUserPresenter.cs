namespace Adventures.Entities;

/// <summary>
/// Adds exactly two things beyond the generic <see cref="IEntityPresenter{TEntity}"/> of
/// <see cref="User"/>: looking a user up by UserName (the system-wide lookup key), and the guarded
/// delete that refuses to let a user delete their own account. The guarded <see cref="DeleteAsync"/>
/// here is a same-named overload of the base's ungated one (different arity - no <c>new</c>/override
/// conflict), not a replacement for it. Kept separate from its implementation
/// (<see cref="UserPresenter"/>) so a versioned/alternate implementation can be swapped in via DI -
/// same pattern as <see cref="ISchemaBll"/>/<see cref="IUserBll"/>.
/// </summary>
public interface IUserPresenter : IEntityPresenter<User>
{
    Task<EntityFormModel?> GetFormByUserNameAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="InvalidOperationException"/> if <paramref name="id"/> equals <paramref name="currentUserId"/> - see <see cref="IUserBll.DeleteAsync"/>.</summary>
    Task<bool> DeleteAsync(string id, string currentUserId, CancellationToken cancellationToken = default);
}
