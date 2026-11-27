using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Adventures.Client;
using Adventures.Entities;
using Adventures.Browser.Desktop;
using Adventures.Browser.Desktop.Views;
using Xunit;

namespace Adventures.Browser.Desktop.Tests;

/// <summary>
/// Each screen must actually render. A view whose code-behind is missing builds cleanly but shows nothing (found by the human's first run: "New" gave a
/// blank window), so this loads the real window with the app's data templates, shows each screen and checks that it produced controls.
/// </summary>
public class ViewLoadTests
{
    private static readonly EntitySchemaModel Schema = new("urn:t",
    [
        new EntityFieldSchema("Id", "Guid", false, "Id", true, 0),
        new EntityFieldSchema("UserName", "String", true, "User name", false, 1),
        new EntityFieldSchema("Role", "String", false, "Role", false, 2, Choices: ["Admin", "Editor"]),
    ]);

    private sealed class FakePresenter : IEntityPresenter<DynamicRecord>
    {
        public Task<EntityFormModel?> GetNewFormAsync(CancellationToken c = default) =>
            Task.FromResult<EntityFormModel?>(new EntityFormModel(Schema, new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["UserName"] = null }), new EntityPermissions(true, true)));

        public Task<EntityFormModel?> GetFormAsync(string id, CancellationToken c = default) => GetNewFormAsync(c);

        public Task<EntityGridModel> GetGridAsync(EntityGridQuery q, CancellationToken c = default) =>
            Task.FromResult(new EntityGridModel(Schema, [new EntityDataModel("u1", new Dictionary<string, string?> { ["Id"] = "u1", ["UserName"] = "Ann" })], new GridPaging(1, 25, 1, null, false), CanCreate: true));

        public Task<EntityGridModel> GetGridAsync(CancellationToken c = default) => GetGridAsync(new EntityGridQuery(), c);

        public async Task<IReadOnlyList<EntityDataModel>> ListAsync(CancellationToken c = default) => (await GetGridAsync(c)).Rows;

        public Task<EntityFormModel> CreateAsync(EntityDataModel d, CancellationToken c = default) => throw new NotSupportedException();

        public Task<EntityFormModel?> UpdateAsync(string id, EntityDataModel d, CancellationToken c = default) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string id, CancellationToken c = default) => throw new NotSupportedException();
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

    private static int Count<T>(DependencyObject root)
        where T : DependencyObject
    {
        var found = root is T ? 1 : 0;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            found += Count<T>(VisualTreeHelper.GetChild(root, i));
        }

        return found;
    }

    private static Window Show(object screen)
    {
        var app = Application.Current ?? new App();
        if (app.Resources.Count == 0)
        {
            ((App)app).InitializeComponent();
        }

        // the first window shown becomes the main window, and closing it would shut the app down for every later test
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new Window { Content = new ContentControl { Content = screen }, Width = 800, Height = 500 };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    [Fact]
    public void The_edit_screen_renders_its_fields_and_buttons()
    {
        OnSta(() =>
        {
            var edit = new EntityEditViewModel(new FakePresenter());
            edit.LoadAsync(null).GetAwaiter().GetResult();

            var window = Show(edit);

            Assert.Equal(2, Count<Button>(window));
            Assert.True(Count<TextBox>(window) >= 3, "one text box per field");
            Assert.Equal(3, Count<ComboBox>(window));
            window.Close();
        });
    }

    [Fact]
    public void The_list_screen_renders_a_grid_with_the_servers_columns()
    {
        OnSta(() =>
        {
            var list = new EntityListViewModel(new FakePresenter());
            list.LoadAsync().GetAwaiter().GetResult();

            var window = Show(list);

            Assert.Equal(1, Count<DataGrid>(window));
            var grid = (DataGrid)Find<DataGrid>(window)!;
            Assert.Equal(3, grid.Columns.Count);
            window.Close();
        });
    }

    [Fact]
    public void The_sign_in_screen_renders_its_inputs()
    {
        OnSta(() =>
        {
            var session = new ApiSession(new HttpClient { BaseAddress = new Uri("https://localhost/") }, new Adventures.Security.AmbientPrincipal());
            var window = Show(new SignInViewModel(session));

            Assert.Equal(1, Count<PasswordBox>(window));
            Assert.True(Count<TextBox>(window) >= 1);
            window.Close();
        });
    }

    private static DependencyObject? Find<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T)
        {
            return root;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (Find<T>(VisualTreeHelper.GetChild(root, i)) is { } hit)
            {
                return hit;
            }
        }

        return null;
    }
}
