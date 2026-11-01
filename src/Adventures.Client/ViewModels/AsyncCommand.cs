using System.Windows.Input;

namespace Adventures.Client;

/// <summary>
/// An <see cref="ICommand"/> for an async action. It refuses a second start while one is running (a double click cannot save twice), and hands anything
/// the action throws to <paramref name="onError"/> instead of letting it escape from an async-void handler and end the application.
/// </summary>
public sealed class AsyncCommand(Func<Task> run, Func<bool>? canExecute = null, Action<Exception>? onError = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool IsRunning { get; private set; }

    public bool CanExecute(object? parameter) => !IsRunning && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        try
        {
            await RunAsync().ConfigureAwait(true);
        }
        catch (Exception error) when (onError is not null)
        {
            onError(error);
        }
    }

    public async Task RunAsync()
    {
        if (!CanExecute(null))
        {
            return;
        }

        IsRunning = true;
        RaiseCanExecuteChanged();
        try
        {
            await run().ConfigureAwait(true);
        }
        finally
        {
            IsRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
