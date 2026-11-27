using Adventures.Client;

namespace Adventures.Browser.Client;

/// <summary>Signing in to one configured domain, with the person's account on that site.</summary>
public sealed class SiteSignInViewModel : ScreenViewModel
{
    private readonly IBrowserApi _api;
    private string _userName = string.Empty;
    private string _password = string.Empty;

    public SiteSignInViewModel(IBrowserApi api, string domain, string? message = null)
    {
        _api = api;
        Domain = domain;
        Error = message;
        SignInCommand = new AsyncCommand(SignInAsync, () => !string.IsNullOrWhiteSpace(UserName) && !string.IsNullOrEmpty(Password), Report);
    }

    public string Domain { get; }

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

    public event EventHandler? SignedIn;

    public async Task SignInAsync()
    {
        Error = null;
        IsBusy = true;
        try
        {
            switch (await _api.SignInAsync(Domain, UserName.Trim(), Password).ConfigureAwait(true))
            {
                case SiteSignInResult.SignedIn:
                    SignedIn?.Invoke(this, EventArgs.Empty);
                    break;
                case SiteSignInResult.MustChangePassword:
                    Error = $"Change your password on {Domain} first, then sign in again.";
                    break;
                default:
                    Error = "Sign-in was refused. Check the domain, the user name and the password.";
                    break;
            }
        }
        catch (Exception error)
        {
            // a 401 here is the sign-in itself, never a lapsed session
            if (error is ApiException refusal)
            {
                Error = refusal.Message;
            }
            else
            {
                Report(error);
            }
        }
        finally
        {
            Password = string.Empty;
            IsBusy = false;
        }
    }
}
