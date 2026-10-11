namespace Adventures.Entities;

/// <summary>Implementation of <see cref="IExceptionLogPresenter"/> - see that interface for the contract.</summary>
public sealed class ExceptionLogPresenter(IExceptionLogBll bll, EntitySchemaOf<ExceptionLogEntry> schema)
    : EntityPresenter<ExceptionLogEntry>(bll, schema.Schema, s => new ExceptionLogEntry(s)), IExceptionLogPresenter
{
    private readonly IExceptionLogBll _bll = bll ?? throw new ArgumentNullException(nameof(bll));

    /// <summary>Newest first: the administrator wants what just happened at the top.</summary>
    public override async Task<EntityGridModel> GetGridAsync(CancellationToken cancellationToken = default)
    {
        var entries = await _bll.ListAsync(cancellationToken).ConfigureAwait(false);
        var newestFirst = entries.OrderByDescending(entry => entry.GetValue("OccurredAt") is DateTimeOffset at ? at : DateTimeOffset.MinValue);
        return EntityGridModel.From(Schema, newestFirst);
    }

    public Task<int> CountActiveAsync(CancellationToken cancellationToken = default) => _bll.CountActiveAsync(cancellationToken);

    public Task<int> PurgeAsync(CancellationToken cancellationToken = default) => _bll.PurgeAsync(cancellationToken);
}
