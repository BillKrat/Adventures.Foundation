namespace Adventures.Client;

public sealed class SignInViewModel : ScreenViewModel
{
    private readonly ApiSession _session;
    private string _userName = string.Empty;
    private string _password = string.Empty;

    public SignInViewModel(ApiSession session)
    {
        _session = session;
        SignInCommand = new AsyncCommand(SignInAsync, () => !string.IsNullOrWhiteSpace(UserName) && !string.IsNullOrEmpty(Password), Report);
    }

    public AsyncCommand SignInCommand { get; }

    public string UserName
    {
        get => _userName;
        set
        {
            if (Set(ref _userName, value))
            {
                SignInCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Held only while the person types; cleared after every attempt whatever the outcome.</summary>
    public string Password
    {
        get => _password;
        set
        {
            if (Set(ref _password, value))
            {
                SignInCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool MustChangePassword => _session.MustChangePassword;

    public event EventHandler? SignedIn;

    /// <summary>Shows a line on the sign-in screen (for example why the person was sent back); null clears it.</summary>
    public void ShowMessage(string? message) => Error = message;

    public async Task SignInAsync()
    {
        Error = null;
        IsBusy = true;
        try
        {
            if (await _session.SignInAsync(UserName.Trim(), Password).ConfigureAwait(true))
            {
                SignedIn?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Error = "Sign-in was refused. Check the user name and password.";
            }
        }
        catch (Exception error)
        {
            Report(error);
        }
        finally
        {
            Password = string.Empty;
            IsBusy = false;
        }
    }
}
