using System.Windows.Input;

namespace Adventures.Browser.Client;

/// <summary>
/// An <see cref="ICommand"/> for an async action on one text value, such as opening the schema a link names. The action reports its own failures; a
/// call without a value does nothing.
/// </summary>
public sealed class ParameterCommand(Func<string, Task> run) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => parameter is string { Length: > 0 };

    public async void Execute(object? parameter)
    {
        if (parameter is string { Length: > 0 } value)
        {
            await run(value).ConfigureAwait(true);
        }
    }
}
