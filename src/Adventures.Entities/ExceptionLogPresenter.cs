namespace Adventures.Entities;

/// <summary>Implementation of <see cref="IExceptionLogPresenter"/> - see that interface for the contract.</summary>
public sealed class ExceptionLogPresenter(IExceptionLogBll bll, EntitySchemaOf<ExceptionLogEntry> schema)
    : EntityPresenter<ExceptionLogEntry>(bll, schema.Schema, s => new ExceptionLogEntry(s)), IExceptionLogPresenter
{
    private readonly IExceptionLogBll _bll = bll ?? throw new ArgumentNullException(nameof(bll));

    protected override IReadOnlySet<string> ListHiddenFields { get; } = new HashSet<string>(["Id", "StackTrace"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Newest first: the administrator wants what just happened at the top.</summary>
    public override async Task<EntityGridModel> GetGridAsync(CancellationToken cancellationToken = default)
    {
        var entries = await _bll.ListAsync(cancellationToken).ConfigureAwait(false);
        var newestFirst = entries.OrderByDescending(entry => entry.GetValue("OccurredAt") is DateTimeOffset at ? at : DateTimeOffset.MinValue);
        return EntityGridModel.From(Schema, newestFirst, ReadOnlyFields, ListHiddenFields);
    }

    public const string StateField = "State";

    public async Task<EntityGridModel> GetHistoryGridAsync(CancellationToken cancellationToken = default)
    {
        var all = await _bll.ListIncludingPurgedAsync(cancellationToken).ConfigureAwait(false);
        var activeIds = (await _bll.ListAsync(cancellationToken).ConfigureAwait(false))
            .Select(entry => entry.EntityId?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var newestFirst = all.OrderByDescending(entry => entry.GetValue("OccurredAt") is DateTimeOffset at ? at : DateTimeOffset.MinValue);
        var grid = EntityGridModel.From(Schema, newestFirst, ReadOnlyFields, ListHiddenFields);

        var rows = grid.Rows
            .Select(row => new EntityDataModel(
                row.EntityId,
                new Dictionary<string, string?>(row.Values, StringComparer.OrdinalIgnoreCase)
                {
                    [StateField] = activeIds.Contains(row.EntityId) ? "Active" : "Purged",
                }))
            .ToArray();
        var fields = grid.Schema.Fields.Append(new EntityFieldSchema(StateField, "String", false, Order: grid.Schema.Fields.Count)).ToArray();
        return new EntityGridModel(new EntitySchemaModel(grid.Schema.SchemaIri, fields), rows);
    }

    public Task<int> CountTotalAsync(CancellationToken cancellationToken = default) => _bll.CountIncludingPurgedAsync(cancellationToken);

    public Task<int> CountActiveAsync(CancellationToken cancellationToken = default) => _bll.CountActiveAsync(cancellationToken);

    public Task<int> PurgeAsync(CancellationToken cancellationToken = default) => _bll.PurgeAsync(cancellationToken);
}
