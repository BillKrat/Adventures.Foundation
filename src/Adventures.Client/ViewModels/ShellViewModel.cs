namespace Adventures.Client;

/// <summary>
/// The window's one moving part: which screen is showing. Sign in, then the list; opening or adding an entry shows the edit form; saving or cancelling
/// returns to the list; a lapsed session or signing out returns to sign-in. The view just displays <see cref="Current"/> through a data template.
/// </summary>
public sealed class ShellViewModel : ObservableObject
{
    private readonly ApiSession _session;
    private object _current;

    public ShellViewModel(ApiSession session, SignInViewModel signIn, EntityListViewModel list, EntityEditViewModel edit)
    {
        _session = session;
        SignIn = signIn;
        List = list;
        Edit = edit;
        _current = signIn;
        SignOutCommand = new AsyncCommand(() => { Show(SignIn); return Task.CompletedTask; }, () => _session.IsSignedIn);

        signIn.SignedIn += async (_, _) =>
        {
            Show(List);
            await list.LoadAsync().ConfigureAwait(true);
        };
        list.EditRequested += async (_, id) =>
        {
            Show(Edit);
            await edit.LoadAsync(id).ConfigureAwait(true);
        };
        edit.Saved += async (_, _) =>
        {
            Show(List);
            await list.LoadAsync().ConfigureAwait(true);
        };
        edit.Closed += (_, _) => Show(List);
        list.SessionExpired += (_, _) => Show(SignIn, "Your session has ended. Sign in again.");
        edit.SessionExpired += (_, _) => Show(SignIn, "Your session has ended. Sign in again.");
    }

    public SignInViewModel SignIn { get; }

    public EntityListViewModel List { get; }

    public EntityEditViewModel Edit { get; }

    public AsyncCommand SignOutCommand { get; }

    public object Current
    {
        get => _current;
        private set => Set(ref _current, value);
    }

    private void Show(object screen, string? signInMessage = null)
    {
        if (ReferenceEquals(screen, SignIn))
        {
            _session.SignOut();
            SignIn.ShowMessage(signInMessage);
        }

        Current = screen;
        SignOutCommand.RaiseCanExecuteChanged();
    }
}
