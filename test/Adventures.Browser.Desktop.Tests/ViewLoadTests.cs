using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Adventures.Browser.Client;
using Adventures.Browser.Desktop;
using Adventures.Entities;
using Xunit;

namespace Adventures.Browser.Desktop.Tests;

/// <summary>
/// Each screen must actually render. A view whose code-behind is missing builds cleanly but shows nothing (found by the human's first run), so this loads
/// the real window with the app's data templates, shows each screen and checks that it produced controls bound to the view model.
/// </summary>
public class ViewLoadTests
{
    private sealed class Api : IBrowserApi
    {
        public bool SignedIn { get; set; }

        public bool IsSignedIn(string domain) => SignedIn;

        public Task<SiteSignInResult> SignInAsync(string domain, string userName, string password, CancellationToken cancellationToken = default) => Task.FromResult(SiteSignInResult.SignedIn);

        public void SignOut(string domain) => SignedIn = false;

        public Task<SchemaBrowserContext> GetContextAsync(string domain, CancellationToken cancellationToken = default) => Task.FromResult(new SchemaBrowserContext("One", domain));

        public Task<SchemaCatalogList> GetSchemasAsync(string domain, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SchemaCatalogList([new("urn:role", "identity", "Role", 2), new("urn:user", "identity", "User", 5)], []));

        public Task<SchemaDetail?> GetSchemaAsync(string domain, string schemaId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SchemaDetail?>(new SchemaDetail(schemaId, "identity", "Role", "Name",
                [new("Name", "Name", "string", "urn:p:name", true, false, true, null), new("Owner", "Owner", "ref:urn:user", "urn:p:owner", false, false, true, "urn:user")],
                [new("People", "People", "urn:user", "urn:p:role", true)],
                [new("urn:user", "User", "Role")]));
    }

    private sealed class Store : ISiteListStore
    {
        public Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(["one.example"]);

        public Task SaveAsync(IReadOnlyList<string> domains, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // WPF needs a single-threaded apartment, and the one Application (with the data templates) belongs to the thread that made it: every test body runs on
    // this one long-lived STA thread.
    private static readonly System.Collections.Concurrent.BlockingCollection<(Action Body, TaskCompletionSource Done)> Work = [];

    private static readonly Thread StaThread = StartSta();

    private static Thread StartSta()
    {
        var thread = new Thread(() =>
        {
            foreach (var (body, done) in Work.GetConsumingEnumerable())
            {
                try
                {
                    body();
                    done.SetResult();
                }
                catch (Exception error)
                {
                    done.SetException(error);
                }
            }
        })
        { IsBackground = true, Name = "WPF STA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    }

    private static void OnSta(Action body)
    {
        _ = StaThread;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Work.Add((body, done));
        done.Task.GetAwaiter().GetResult();
    }

    private static IEnumerable<T> Find<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T hit)
        {
            yield return hit;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            foreach (var further in Find<T>(VisualTreeHelper.GetChild(root, i)))
            {
                yield return further;
            }
        }
    }

    private static Window Show(Window window)
    {
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static void EnsureApplication()
    {
        var app = Application.Current ?? new App();
        if (app.Resources.Count == 0)
        {
            ((App)app).InitializeComponent();
        }

        // the first window shown becomes the main window, and closing it would shut the app down for every later test
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    [Fact]
    public void The_window_lists_the_domains_and_shows_the_sign_in_of_the_chosen_one()
    {
        OnSta(() =>
        {
            EnsureApplication();
            var shell = new BrowserShellViewModel(new Api(), new Store());
            shell.LoadAsync().GetAwaiter().GetResult();

            var window = Show(new MainWindow { DataContext = shell });

            Assert.Equal("one.example", Assert.Single(Find<ListBox>(window)).SelectedItem);
            Assert.Single(Find<PasswordBox>(window));
            var buttons = Find<Button>(window).Select(b => b.Content as string).ToList();
            Assert.Contains("Add", buttons);
            Assert.Contains("Remove", buttons);
            Assert.Contains("Sign in", buttons);
            Assert.Contains(Find<TextBlock>(window), t => t.Text == "Sign in to one.example");
            window.Close();
        });
    }

    [Fact]
    public void The_schema_screen_lists_the_schemas_and_shows_one_with_its_links()
    {
        OnSta(() =>
        {
            EnsureApplication();
            var shell = new BrowserShellViewModel(new Api { SignedIn = true }, new Store());
            shell.LoadAsync().GetAwaiter().GetResult();
            shell.Opened.GetAwaiter().GetResult();
            var browser = Assert.IsType<SchemaBrowserViewModel>(shell.Current);
            browser.OpenAsync("urn:role").GetAwaiter().GetResult();

            var window = Show(new MainWindow { DataContext = shell });

            var grid = Assert.Single(Find<DataGrid>(window));
            Assert.Equal(2, grid.Items.Count);
            var links = Find<Button>(window).Where(b => b.Command == browser.OpenCommand && b.IsVisible).Select(b => (string?)b.CommandParameter).ToList();
            Assert.Equal(1, links.Count(l => l == "urn:role"));        // the list
            Assert.Equal(4, links.Count(l => l == "urn:user"));        // the list, the pointer field, the child collection, "used by"
            Assert.DoesNotContain(null, links);                         // a field that points nowhere shows no link
            window.Close();
        });
    }

    [Fact]
    public void The_sign_in_box_is_emptied_when_another_domain_is_chosen()
    {
        OnSta(() =>
        {
            EnsureApplication();
            var api = new Api();
            var view = new Views.SignInView { DataContext = new SiteSignInViewModel(api, "one.example") };
            var window = Show(new Window { Content = view, Width = 500, Height = 400 });
            var box = Assert.Single(Find<PasswordBox>(window));
            box.Password = "typed for one";

            view.DataContext = new SiteSignInViewModel(api, "two.example");

            Assert.Empty(box.Password);
            Assert.Empty(((SiteSignInViewModel)view.DataContext).Password);
            window.Close();
        });
    }
}
