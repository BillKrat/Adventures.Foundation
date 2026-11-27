using System.IO;
using System.Windows;
using Adventures.Browser.Client;
using Adventures.Browser.Desktop.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Browser.Desktop;

/// <summary>Composition root: the only place that names concrete types. Everything below it depends on the abstractions.</summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            FileLog.Write("unhandled", args.Exception);
            MessageBox.Show("Something went wrong and was written to the log. The window stays open.", "Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            var settings = new BrowserSettingsFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Adventures.Browser", "settings.json"));
            var defaults = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: false).Build();
            var environment = new ConfigurationBuilder().AddEnvironmentVariables("ADVENTURES_").Build();

            // The browser API address: the environment variable ADVENTURES_Api__BaseUrl, else the person's settings file, else appsettings.json.
            var address = environment["Api:BaseUrl"] ?? settings.Read().ApiBaseUrl ?? defaults["Api:BaseUrl"] ?? throw new InvalidOperationException("Api:BaseUrl is not set.");

            // A plain-http address to a remote host is refused by AddAdventuresBrowserClient.
            _services = new ServiceCollection()
                .AddAdventuresBrowserClient(new Uri(address))
                .AddSingleton<ISiteListStore>(settings)
                .AddSingleton<BrowserShellViewModel>()
                .BuildServiceProvider(validateScopes: true);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or UriFormatException)
        {
            MessageBox.Show(error.Message, "Cannot start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var shell = _services.GetRequiredService<BrowserShellViewModel>();
        shell.Failed += (_, error) => FileLog.Write("faulted", error);
        new MainWindow { DataContext = shell }.Show();
        await shell.LoadAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
