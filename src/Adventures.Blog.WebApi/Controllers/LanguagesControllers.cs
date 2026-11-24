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

    /// <summary>The proposed files waiting for review (Claude proposes through MCP; nothing is served until an administrator applies one).</summary>
    [HttpGet("proposals")]
    public async Task<ActionResult<IReadOnlyList<LanguageSummary>>> Proposals(CancellationToken cancellationToken) => Ok(await files.ListProposalsAsync(cancellationToken));

    [HttpGet("{language}/proposal")]
    public async Task<IActionResult> ReadProposal(string language, CancellationToken cancellationToken) =>
        await files.GetProposalAsync(language, cancellationToken) is { } table ? Ok(new PublicLabels(language.Trim().ToLowerInvariant(), table)) : NotFound();

    /// <summary>Makes the proposal the served file of its language.</summary>
    [HttpPost("{language}/proposal/apply")]
    public Task<IActionResult> ApplyProposal(string language, CancellationToken cancellationToken) =>
        GuardedAsync(async () => await files.ApplyProposalAsync(language, cancellationToken) ? NoContent() : NotFound());

    [HttpDelete("{language}/proposal")]
    public Task<IActionResult> DiscardProposal(string language, CancellationToken cancellationToken) =>
        GuardedAsync(async () => await files.DiscardProposalAsync(language, cancellationToken) ? NoContent() : NotFound());

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

/// <summary>Every key a language file can translate with its English text, and a note when that list is incomplete.</summary>
public sealed record LanguageCatalog(IReadOnlyDictionary<string, string> Keys, string? Note);

/// <summary>
/// What Claude may do with language files, through MCP: read the keys to translate and PROPOSE a file. A proposal is checked like any file but never served:
/// an administrator reviews it on Admin > Languages and applies or discards it. There is no route here that applies a file.
/// </summary>
[ApiController]
[Route("api/author/languages")]
[Authorize(Policy = "Scope:" + RequiredScope)]
public sealed class AuthorLanguagesController(ILanguageFiles files, IServerTextCatalog catalog) : ApiControllerBase
{
    public const string RequiredScope = AuthoringController.RequiredScope;

    /// <summary>The server's texts with their English defaults, over which the stored English table (the screens' keys, saved once from Admin > Languages) is laid.</summary>
    [HttpGet("catalog")]
    public async Task<ActionResult<LanguageCatalog>> Catalog(CancellationToken cancellationToken)
    {
        var keys = new Dictionary<string, string>(catalog.Defaults, StringComparer.Ordinal);
        var english = await files.GetAsync("en", cancellationToken);
        foreach (var (key, text) in english)
        {
            keys[key] = text;
        }

        var note = english.Count == 0
            ? "The list holds only the server's texts. Ask the administrator to open Admin > Languages, choose Load the English texts and save them as the language en, so the screens' own keys are listed here too."
            : null;
        return Ok(new LanguageCatalog(keys, note));
    }

    [HttpPut("{language}")]
    public Task<IActionResult> Propose(string language, [FromBody] LanguageBody body, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            await files.ProposeAsync(language, body.Labels!, cancellationToken);
            return NoContent();
        });
}
