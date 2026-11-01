namespace Adventures.Client;

/// <summary>
/// What every screen shares: a busy flag, one message line, and one place that turns a failure into something a person may read. A refused call shows the
/// server's own one-line reason, a lapsed session raises <see cref="SessionExpired"/>, a dead network gets a plain sentence, and anything unforeseen is
/// raised on <see cref="Faulted"/> for the host to log while the person sees a generic line (no stack trace, host name or token ever reaches the screen).
/// </summary>
public abstract class ScreenViewModel : ObservableObject
{
    private bool _isBusy;
    private string? _error;

    public bool IsBusy
    {
        get => _isBusy;
        protected set => Set(ref _isBusy, value);
    }

    public string? Error
    {
        get => _error;
        protected set => Set(ref _error, value);
    }

    public event EventHandler? SessionExpired;

    public event EventHandler<Exception>? Faulted;

    protected void Report(Exception exception)
    {
        switch (exception)
        {
            case ApiException { IsUnauthorized: true }:
                Error = "Your session has ended. Sign in again.";
                SessionExpired?.Invoke(this, EventArgs.Empty);
                break;
            case ApiException api:
                Error = api.Message;
                break;
            case HttpRequestException or TaskCanceledException:
                Error = "The server could not be reached. Check the connection and try again.";
                break;
            default:
                Error = "Something went wrong. Try again, and tell an administrator if it keeps happening.";
                Faulted?.Invoke(this, exception);
                break;
        }
    }
}
