using System.Windows;
using Adventures.Client;
using Adventures.Entities;
using Adventures.Browser.Desktop.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Browser.Desktop;

/// <summary>Composition root: the only place that names concrete types. Everything below it depends on the abstractions.</summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            FileLog.Write("unhandled", args.Exception);
            MessageBox.Show("Something went wrong and was written to the log. The window stays open.", "Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables("ADVENTURES_")
            .Build();

        try
        {
            // Api:BaseUrl may be overridden by the environment variable ADVENTURES_Api__BaseUrl. A plain-http address to a remote host is refused by AddAdventuresClient.
            _services = new ServiceCollection()
                .AddAdventuresClient(new Uri(configuration["Api:BaseUrl"] ?? throw new InvalidOperationException("Api:BaseUrl is not set.")))
                .AddRemotePresenter<DynamicRecord>(configuration["Api:UsersRoute"] ?? "api/users")
                .AddSingleton<SignInViewModel>()
                .AddSingleton<EntityListViewModel>()
                .AddSingleton<EntityEditViewModel>()
                .AddSingleton<ShellViewModel>()
                .BuildServiceProvider(validateScopes: true);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or UriFormatException)
        {
            MessageBox.Show(error.Message, "Cannot start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        ScreenViewModel[] screens = [_services.GetRequiredService<SignInViewModel>(), _services.GetRequiredService<EntityListViewModel>(), _services.GetRequiredService<EntityEditViewModel>()];
        foreach (var screen in screens)
        {
            screen.Faulted += (_, error) => FileLog.Write("faulted", error);
        }

        new MainWindow { DataContext = _services.GetRequiredService<ShellViewModel>() }.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
