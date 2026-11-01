using System.Collections.ObjectModel;
using Adventures.Entities;

namespace Adventures.Client;

public sealed record ColumnViewModel(string Name, string Label);

/// <summary>One grid row as display text, in column order. The id travels beside it so a selection can open the entry.</summary>
public sealed record RowViewModel(string EntityId, string[] Cells);

/// <summary>
/// A paged, schema-driven list of one kind of entity. The columns come from the schema the presenter returns, so the same screen lists people, roles or
/// anything else the server describes; it never names a field.
/// </summary>
public sealed class EntityListViewModel : ScreenViewModel
{
    private readonly IEntityPresenter<DynamicRecord> _presenter;
    private IReadOnlyList<ColumnViewModel> _columns = [];
    private int _page = 1;
    private int _totalPages = 1;
    private bool _canCreate;
    private RowViewModel? _selected;

    public EntityListViewModel(IEntityPresenter<DynamicRecord> presenter)
    {
        _presenter = presenter;
        LoadCommand = new AsyncCommand(LoadAsync, onError: Report);
        NextCommand = new AsyncCommand(() => GoToAsync(Page + 1), () => Page < TotalPages, Report);
        PreviousCommand = new AsyncCommand(() => GoToAsync(Page - 1), () => Page > 1, Report);
        NewCommand = new AsyncCommand(() => { EditRequested?.Invoke(this, null); return Task.CompletedTask; }, () => CanCreate);
        EditCommand = new AsyncCommand(() => { EditRequested?.Invoke(this, Selected?.EntityId); return Task.CompletedTask; }, () => Selected is not null);
    }

    public ObservableCollection<RowViewModel> Rows { get; } = [];

    public IReadOnlyList<ColumnViewModel> Columns
    {
        get => _columns;
        private set => Set(ref _columns, value);
    }

    public int Page
    {
        get => _page;
        private set => Set(ref _page, value);
    }

    public int TotalPages
    {
        get => _totalPages;
        private set => Set(ref _totalPages, value);
    }

    public bool CanCreate
    {
        get => _canCreate;
        private set => Set(ref _canCreate, value);
    }

    public RowViewModel? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                EditCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncCommand LoadCommand { get; }

    public AsyncCommand NextCommand { get; }

    public AsyncCommand PreviousCommand { get; }

    public AsyncCommand NewCommand { get; }

    public AsyncCommand EditCommand { get; }

    /// <summary>Raised with the entry to open, or null for a new one.</summary>
    public event EventHandler<string?>? EditRequested;

    public Task LoadAsync() => GoToAsync(Page);

    private async Task GoToAsync(int page)
    {
        Error = null;
        IsBusy = true;
        try
        {
            var grid = await _presenter.GetGridAsync(new EntityGridQuery(Page: Math.Max(1, page))).ConfigureAwait(true);
            var fields = grid.Schema.Fields.Where(f => f.IsListVisible).OrderBy(f => f.Order).ToList();
            Columns = fields.Select(f => new ColumnViewModel(f.Name, f.Label ?? f.Name)).ToList();
            Rows.Clear();
            foreach (var row in grid.Rows)
            {
                Rows.Add(new RowViewModel(row.EntityId, fields.Select(f => CellText(row, f.Name)).ToArray()));
            }

            Page = grid.Paging?.Page ?? 1;
            TotalPages = grid.Paging?.TotalPages ?? 1;
            CanCreate = grid.CanCreate;
            Selected = null;
        }
        catch (Exception error)
        {
            Report(error);
        }
        finally
        {
            IsBusy = false;
            NextCommand.RaiseCanExecuteChanged();
            PreviousCommand.RaiseCanExecuteChanged();
            NewCommand.RaiseCanExecuteChanged();
        }
    }

    private static string CellText(EntityDataModel row, string field) =>
        row.Display is not null && row.Display.TryGetValue(field, out var shown) ? shown
        : row.Values.TryGetValue(field, out var value) ? value ?? string.Empty
        : string.Empty;
}
