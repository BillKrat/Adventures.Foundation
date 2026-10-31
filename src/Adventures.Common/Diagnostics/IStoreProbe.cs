namespace Adventures.Common.Diagnostics;

/// <summary>
/// A cheap reachability check of the data store, so the health probes depend on this contract and never on a particular store
/// (N-Quads, PostgreSQL, SQL Server). Throws when the store cannot be reached; the caller turns that into a failed probe result.
/// </summary>
public interface IStoreProbe
{
    Task CheckAsync(CancellationToken cancellationToken = default);
}
