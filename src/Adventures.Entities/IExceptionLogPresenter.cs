namespace Adventures.Entities;

/// <summary>
/// What the administrator's exception screen needs: the grid (newest first), the active count behind the warning icon,
/// and purge. Everything else comes from <see cref="IEntityPresenter{TEntity}"/>.
/// </summary>
public interface IExceptionLogPresenter : IEntityPresenter<ExceptionLogEntry>
{
    /// <summary>Number of entries not yet purged.</summary>
    Task<int> CountActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Tombstones every active entry; returns how many.</summary>
    Task<int> PurgeAsync(CancellationToken cancellationToken = default);
}
