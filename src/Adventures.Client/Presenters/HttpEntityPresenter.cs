using System.Net;
using System.Net.Http.Json;
using Adventures.Entities;

namespace Adventures.Client;

/// <summary>
/// <see cref="IEntityPresenter{TEntity}"/> over the API's grid and form contract (the endpoints the Angular admin already uses). A view model binds to the
/// interface exactly as it would to the server-side presenter; only the registration differs. Business rules, rights and validation stay on the server
/// (AppArch ch. 17: keep sensitive logic on its own tier; validate again at the trust boundary), so this class holds no rule of its own.
/// </summary>
public sealed class HttpEntityPresenter<TEntity>(HttpClient http, string route) : IEntityPresenter<TEntity>
    where TEntity : DynamicEntity
{
    private readonly string _route = route.Trim('/');

    public async Task<EntityFormModel?> GetFormAsync(string id, CancellationToken cancellationToken = default) =>
        await ReadOrNullAsync<EntityFormModel>(await http.GetAsync($"{_route}/{Uri.EscapeDataString(id)}", cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    public async Task<EntityFormModel?> GetNewFormAsync(CancellationToken cancellationToken = default) =>
        await ReadOrNullAsync<EntityFormModel>(await http.GetAsync($"{_route}/new", cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<EntityDataModel>> ListAsync(CancellationToken cancellationToken = default) =>
        (await GetGridAsync(cancellationToken).ConfigureAwait(false)).Rows;

    public Task<EntityGridModel> GetGridAsync(CancellationToken cancellationToken = default) => GetGridAsync(new EntityGridQuery(), cancellationToken);

    public async Task<EntityGridModel> GetGridAsync(EntityGridQuery query, CancellationToken cancellationToken = default)
    {
        var q = query.Normalized();
        var parts = new List<string> { $"page={q.Page}", $"pageSize={q.PageSize}" };
        if (q.Sort is not null)
        {
            parts.Add($"sort={Uri.EscapeDataString(q.Sort)}");
            parts.Add($"descending={(q.Descending ? "true" : "false")}");
        }

        if (q.Filter is not null)
        {
            parts.Add($"filter={Uri.EscapeDataString(q.Filter)}");
            parts.Add($"filterValue={Uri.EscapeDataString(q.FilterValue!)}");
        }

        using var response = await http.GetAsync($"{_route}?{string.Join('&', parts)}", cancellationToken).ConfigureAwait(false);
        return await ReadOrNullAsync<EntityGridModel>(response, cancellationToken).ConfigureAwait(false)
            ?? throw new ApiException(HttpStatusCode.NotFound, "The server has no such list.");
    }

    public async Task<EntityFormModel> CreateAsync(EntityDataModel data, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(_route, data, cancellationToken).ConfigureAwait(false);
        return await ReadOrNullAsync<EntityFormModel>(response, cancellationToken).ConfigureAwait(false)
            ?? throw new ApiException(response.StatusCode, "The server did not return the new entry.");
    }

    public async Task<EntityFormModel?> UpdateAsync(string id, EntityDataModel data, CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync($"{_route}/{Uri.EscapeDataString(id)}", data, cancellationToken).ConfigureAwait(false);
        return await ReadOrNullAsync<EntityFormModel>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync($"{_route}/{Uri.EscapeDataString(id)}", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        return response.IsSuccessStatusCode ? true : throw await ApiErrors.FromAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T?> ReadOrNullAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await ApiErrors.FromAsync(response, cancellationToken).ConfigureAwait(false);
            }

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
        }
    }
}
