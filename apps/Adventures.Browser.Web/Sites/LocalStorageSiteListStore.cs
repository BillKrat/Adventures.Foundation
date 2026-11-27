using System.Text.Json;
using Adventures.Browser.Client;
using Microsoft.JSInterop;

namespace Adventures.Browser.Web;

/// <summary>
/// The person's configured domains, in the browser's own storage on their device: never on a server and never in the application's files. Storage that is
/// blocked or holds something else reads as an empty list.
/// </summary>
public sealed class LocalStorageSiteListStore(IJSRuntime browser) : ISiteListStore
{
    public const string Key = "adventures.browser.domains";

    public async Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var stored = await browser.InvokeAsync<string?>("localStorage.getItem", cancellationToken, Key);
            var domains = string.IsNullOrWhiteSpace(stored) ? [] : JsonSerializer.Deserialize<string?[]>(stored) ?? [];
            return domains.Select(DomainName.Normalize).OfType<string>().Distinct().ToList();
        }
        catch (Exception error) when (error is JSException or JsonException)
        {
            return [];
        }
    }

    public async Task SaveAsync(IReadOnlyList<string> domains, CancellationToken cancellationToken = default)
    {
        try
        {
            await browser.InvokeVoidAsync("localStorage.setItem", cancellationToken, Key, JsonSerializer.Serialize(domains));
        }
        catch (JSException error)
        {
            throw new InvalidOperationException("This browser does not let the page keep your list of domains; it will be forgotten when you leave.", error);
        }
    }
}
