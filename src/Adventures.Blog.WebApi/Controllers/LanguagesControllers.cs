using Adventures.Blog;
using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.Blog.WebApi;

/// <summary>A language file as the screen saves it: the table of texts by key.</summary>
public sealed record LanguageBody(Dictionary<string, string>? Labels);

/// <summary>The language the site shows and its table of texts (empty when the language has no file: the client then keeps English).</summary>
public sealed record PublicLabels(string Language, IReadOnlyDictionary<string, string> Labels);

/// <summary>
/// The language files for administrators: which languages are stored, one table to read, replace or remove. The files are per tenant and change what every
/// visitor reads on the screens, so they need the site-settings right.
/// </summary>
[ApiController]
[Route("api/languages")]
[RequireRight(Rights.SettingsManage)]
public sealed class LanguagesController(ILanguageFiles files, IServerTextCatalog catalog) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LanguageSummary>>> List(CancellationToken cancellationToken) => Ok(await files.ListAsync(cancellationToken));

    /// <summary>The texts the server itself produces, with their English defaults: keys a language file can translate.</summary>
    [HttpGet("catalog")]
    public ActionResult<IReadOnlyDictionary<string, string>> Catalog() => Ok(catalog.Defaults);

    [HttpGet("{language}")]
    public async Task<IActionResult> Read(string language, CancellationToken cancellationToken)
    {
        var table = await files.GetAsync(language, cancellationToken);
        return table.Count == 0 ? NotFound() : Ok(new PublicLabels(language.Trim().ToLowerInvariant(), table));
    }

    [HttpPut("{language}")]
    public Task<IActionResult> Save(string language, [FromBody] LanguageBody body, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            await files.SaveAsync(language, body.Labels!, cancellationToken);
            return NoContent();
        });

    [HttpDelete("{language}")]
    public Task<IActionResult> Remove(string language, CancellationToken cancellationToken) =>
        GuardedAsync(async () => await files.RemoveAsync(language, cancellationToken) ? NoContent() : NotFound());
}

/// <summary>The texts of the site language as a visitor's browser reads them at start-up: anonymous and read-only, button and screen words only.</summary>
[ApiController]
[Route("api/public/labels")]
[AllowAnonymous]
public sealed class PublicLabelsController(ISiteSettings settings, ILanguageFiles files) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PublicLabels>> Get(CancellationToken cancellationToken)
    {
        var language = (await settings.GetPublicAsync(cancellationToken)).Language;
        return Ok(new PublicLabels(language, await files.GetAsync(language, cancellationToken)));
    }
}
