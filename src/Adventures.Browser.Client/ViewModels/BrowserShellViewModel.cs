using System.Collections.ObjectModel;
using Adventures.Client;

namespace Adventures.Browser.Client;

/// <summary>
/// The application's one window or page: the person's configured domains, and for the chosen one either its sign-in or its schema browser. Each signed-in
/// domain keeps its own browser, so switching between domains loses nothing. The view displays <see cref="Current"/> through a template per view model.
/// </summary>
public sealed class BrowserShellViewModel : ScreenViewModel
{
    private readonly IBrowserApi _api;
    private readonly ISiteListStore _store;
    private readonly Dictionary<string, SchemaBrowserViewModel> _browsers = new(StringComparer.OrdinalIgnoreCase);
    private string _newDomain = string.Empty;
    private string? _selected;
    private ScreenViewModel? _current;

    public BrowserShellViewModel(IBrowserApi api, ISiteListStore store)
    {
        _api = api;
        _store = store;
        AddCommand = new AsyncCommand(AddAsync, () => !string.IsNullOrWhiteSpace(NewDomain), Report);
        RemoveCommand = new AsyncCommand(RemoveAsync, () => SelectedDomain is not null, Report);
    }

    public ObservableCollection<string> Domains { get; } = [];

    public string NewDomain
    {
        get => _newDomain;
        set
        {
            if (Set(ref _newDomain, value))
            {
                AddCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? SelectedDomain
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                RemoveCommand.RaiseCanExecuteChanged();
                Opened = ShowAsync(value);
            }
        }
    }

    /// <summary>The loading that choosing a domain started, so a caller can wait for it.</summary>
    public Task Opened { get; private set; } = Task.CompletedTask;

    public ScreenViewModel? Current
    {
        get => _current;
        private set => Set(ref _current, value);
    }

    public AsyncCommand AddCommand { get; }

    public AsyncCommand RemoveCommand { get; }

    /// <summary>An unforeseen failure on any screen, for the application to log.</summary>
    public event EventHandler<Exception>? Failed;

    public async Task LoadAsync()
    {
        Error = null;
        try
        {
            var domains = await _store.LoadAsync().ConfigureAwait(true);
            Domains.Clear();
            foreach (var domain in domains)
            {
                Domains.Add(domain);
            }

            SelectedDomain = Domains.FirstOrDefault();
        }
        catch (Exception error)
        {
            Show(error);
        }
    }

    public async Task AddAsync()
    {
        Error = null;
        if (DomainName.Normalize(NewDomain) is not { } domain)
        {
            Error = "Type a domain, such as example.com.";
            return;
        }

        if (!Domains.Contains(domain))
        {
            await _store.SaveAsync([.. Domains, domain]).ConfigureAwait(true);
            Domains.Add(domain);
        }

        NewDomain = string.Empty;
        SelectedDomain = domain;
    }

    public async Task RemoveAsync()
    {
        Error = null;
        if (SelectedDomain is not { } domain)
        {
            return;
        }

        await _store.SaveAsync(Domains.Where(d => d != domain).ToList()).ConfigureAwait(true);
        _api.SignOut(domain);
        _browsers.Remove(domain);
        Domains.Remove(domain);
        SelectedDomain = Domains.FirstOrDefault();
    }

    private async Task ShowAsync(string? domain)
    {
        if (domain is null)
        {
            Current = null;
            return;
        }

        if (!_api.IsSignedIn(domain))
        {
            _browsers.Remove(domain);
            Current = SignIn(domain);
            return;
        }

        if (_browsers.TryGetValue(domain, out var open))
        {
            Current = open;
            return;
        }

        var browser = Browser(domain);
        Current = browser;
        await browser.LoadAsync().ConfigureAwait(true);
    }

    private SiteSignInViewModel SignIn(string domain, string? message = null)
    {
        var signIn = new SiteSignInViewModel(_api, domain, message);
        signIn.Faulted += (_, error) => Failed?.Invoke(this, error);
        signIn.SignedIn += (_, _) =>
        {
            if (SelectedDomain == domain)
            {
                _browsers.Remove(domain);
                Opened = ShowAsync(domain);
            }
        };
        return signIn;
    }

    private SchemaBrowserViewModel Browser(string domain)
    {
        var browser = new SchemaBrowserViewModel(_api, domain);
        _browsers[domain] = browser;
        browser.Faulted += (_, error) => Failed?.Invoke(this, error);
        browser.SignedOut += (_, _) => Leave(browser, null);
        browser.SessionExpired += (_, _) => Leave(browser, "Your session has ended. Sign in again.");
        return browser;
    }

    private void Leave(SchemaBrowserViewModel browser, string? message)
    {
        _api.SignOut(browser.Domain);
        _browsers.Remove(browser.Domain);
        if (SelectedDomain == browser.Domain)
        {
            Current = SignIn(browser.Domain, message);
        }
    }

    // A failure of the domain list itself (its file or storage): the store's own sentence is meant for the person.
    private void Show(Exception error)
    {
        if (error is InvalidOperationException)
        {
            Error = error.Message;
        }
        else
        {
            Report(error);
            Failed?.Invoke(this, error);
        }
    }
}
