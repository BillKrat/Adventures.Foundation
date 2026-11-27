using System.Collections.ObjectModel;
using Adventures.Client;
using Adventures.Entities;

namespace Adventures.Browser.Client;

/// <summary>The schemas of one area, as the list shows them.</summary>
public sealed record SchemaAreaViewModel(string Name, IReadOnlyList<SchemaSummary> Schemas);

/// <summary>Browsing the schemas of one signed-in domain: the list by area with a filter, one schema's detail, and the way back through followed links.</summary>
public sealed class SchemaBrowserViewModel : ScreenViewModel
{
    private readonly IBrowserApi _api;
    private readonly Stack<SchemaDetail> _back = new();
    private IReadOnlyList<SchemaSummary> _all = [];
    private string _filter = string.Empty;
    private string _siteTitle;
    private string? _notice;
    private SchemaDetail? _detail;
    private int _opening;

    public SchemaBrowserViewModel(IBrowserApi api, string domain)
    {
        _api = api;
        Domain = domain;
        _siteTitle = domain;
        RefreshCommand = new AsyncCommand(LoadAsync, onError: Report);
        BackCommand = new AsyncCommand(() => { GoBack(); return Task.CompletedTask; }, () => CanGoBack);
        SignOutCommand = new AsyncCommand(() => { _api.SignOut(Domain); SignedOut?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; });
        OpenCommand = new ParameterCommand(OpenAsync);
    }

    public string Domain { get; }

    /// <summary>The site that answered; it names the other domain when the address reached a site of another name.</summary>
    public string SiteTitle
    {
        get => _siteTitle;
        private set => Set(ref _siteTitle, value);
    }

    /// <summary>Something the person should know that is not a failure of what they asked, such as schemas the site could not read.</summary>
    public string? Notice
    {
        get => _notice;
        private set => Set(ref _notice, value);
    }

    public ObservableCollection<SchemaAreaViewModel> Areas { get; } = [];

    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value ?? string.Empty))
            {
                ShowList();
            }
        }
    }

    public SchemaDetail? Detail
    {
        get => _detail;
        private set => Set(ref _detail, value);
    }

    public bool CanGoBack => _back.Count > 0;

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand BackCommand { get; }

    public AsyncCommand SignOutCommand { get; }

    /// <summary>Opens the schema whose id is the parameter.</summary>
    public ParameterCommand OpenCommand { get; }

    public event EventHandler? SignedOut;

    /// <summary>The short name of a schema a link points at; its id when the list has no such schema.</summary>
    public string NameOf(string schemaId) => _all.FirstOrDefault(s => s.Id == schemaId)?.Name ?? schemaId;

    public async Task LoadAsync()
    {
        Error = null;
        IsBusy = true;
        try
        {
            var context = await _api.GetContextAsync(Domain).ConfigureAwait(true);
            var answered = context.SiteDomain;
            SiteTitle = string.IsNullOrWhiteSpace(answered) || string.Equals(answered, Domain, StringComparison.OrdinalIgnoreCase)
                ? context.SiteName ?? Domain
                : $"{context.SiteName ?? answered} (answered as {answered})";
            var list = await _api.GetSchemasAsync(Domain).ConfigureAwait(true);
            _all = list.Schemas;
            Notice = list.Unreadable.Count == 0 ? null : $"The site could not read {list.Unreadable.Count} schema(s): {string.Join(", ", list.Unreadable)}";
            ShowList();
        }
        catch (Exception error)
        {
            Report(error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task OpenAsync(string schemaId)
    {
        Error = null;
        IsBusy = true;
        var request = ++_opening;
        try
        {
            var schema = await _api.GetSchemaAsync(Domain, schemaId).ConfigureAwait(true);
            if (request != _opening)
            {
                return; // the person has asked for something else since
            }

            if (schema is null)
            {
                Error = $"The site has no schema {schemaId}.";
                return;
            }

            if (Detail is { } current && current.Id != schema.Id)
            {
                _back.Push(current);
            }

            Detail = schema;
            BackCommand.RaiseCanExecuteChanged();
        }
        catch (Exception error)
        {
            Report(error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void GoBack()
    {
        if (_back.TryPop(out var previous))
        {
            _opening++;
            Detail = previous;
            Error = null;
        }

        BackCommand.RaiseCanExecuteChanged();
    }

    private void ShowList()
    {
        var wanted = _filter.Trim();
        var shown = wanted.Length == 0
            ? _all
            : _all.Where(s => s.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase) || s.Area.Contains(wanted, StringComparison.OrdinalIgnoreCase)).ToList();
        Areas.Clear();
        foreach (var area in shown.GroupBy(s => s.Area, StringComparer.OrdinalIgnoreCase))
        {
            Areas.Add(new SchemaAreaViewModel(area.Key.Length == 0 ? "(no area)" : area.Key, area.ToList()));
        }
    }
}
