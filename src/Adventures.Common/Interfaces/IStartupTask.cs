namespace Adventures.Common.Interfaces;

/// <summary>
/// Work a feature needs done once at every start-up, after the store is seeded (R2 of the host reuse review): create a table that is missing, upgrade stored defaults a seed cannot supersede, make the first administrator.
/// A feature registers its own as <c>IStartupTask</c>; the host runs them all in registration order and lists none of them.
/// </summary>
public interface IStartupTask
{
    /// <summary>For the log.</summary>
    string Name { get; }

    /// <summary>False for a task that only makes sense on the new store (it is skipped when the host runs over the legacy BlogEngine tables).</summary>
    bool RunsInLegacyMode { get; }

    /// <summary>True when a failure is logged and start-up carries on (a host without the right to create a table, for one); false when it must stop start-up.</summary>
    bool ContinueOnFailure { get; }

    /// <summary>Runs in its own scope; <paramref name="services"/> resolves scoped services.</summary>
    Task RunAsync(IServiceProvider services, CancellationToken cancellationToken);
}
