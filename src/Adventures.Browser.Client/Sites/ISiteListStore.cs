namespace Adventures.Browser.Client;

/// <summary>
/// Where an application keeps the domains the person configured: a file for the desktop, the browser's own storage for the web client. It holds domain
/// names only, never a user name, password or token.
/// </summary>
public interface ISiteListStore
{
    Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyList<string> domains, CancellationToken cancellationToken = default);
}
