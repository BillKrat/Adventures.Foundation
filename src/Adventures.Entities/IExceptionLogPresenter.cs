namespace Adventures.Entities;

/// <summary>
/// What the administrator's exception screen needs: the grid (newest first), the active count behind the warning icon,
/// and purge. Everything else comes from <see cref="IEntityPresenter{TEntity}"/>.
/// </summary>
public interface IExceptionLogPresenter : IEntityPresenter<ExceptionLogEntry>
{
    /// <summary>
    /// The grid including purged entries: every row has a <c>State</c> value, "Active" or "Purged" (a purge is a soft
    /// delete, so the history stays readable). Newest first.
    /// </summary>
    Task<EntityGridModel> GetHistoryGridAsync(CancellationToken cancellationToken = default);

    /// <summary>How many entries exist in all, purged ones included (the exception log has history worth opening).</summary>
    Task<int> CountTotalAsync(CancellationToken cancellationToken = default);

    /// <summary>Number of entries not yet purged.</summary>
    Task<int> CountActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Tombstones every active entry; returns how many.</summary>
    Task<int> PurgeAsync(CancellationToken cancellationToken = default);
}
