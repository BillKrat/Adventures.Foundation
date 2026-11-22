// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
namespace Adventures.Blog;

/// <summary>One named site setting (SiteTitle, PostsPerPage, ...); its schema ships in Sql/seed/seed-011-site-settings.nq.</summary>
public sealed class SiteSetting(EntitySchema schema) : DynamicEntity(schema);

/// <summary>The settings a visitor's browser needs: no secrets, nothing about the server.</summary>
/// <param name="CommentsCloseAfterDays">Comments close on a post this many days after it was published; 0 never.</param>
/// <param name="CommentsModerate">True (the default): a new visitor comment waits for approval. False: it is published at once.</param>
/// <param name="TrackingScriptUrl">One https script address (an analytics service) for the public pages, or empty. Only a host the operator allowed is ever served.</param>
/// <param name="FeedItems">How many of the newest posts the RSS feed lists (1 to 100).</param>
/// <param name="Language">The language code of the site (en unless set): the language file of that code supplies the button and screen texts.</param>
/// <param name="ColorMode">"dark" or "light": the site default a visitor sees until they use the header toggle themselves.</param>
public sealed record PublicSiteSettings(string Title, string Tagline, int PostsPerPage, bool CommentsEnabled = true, string ColorMode = "dark", string Language = "en", int CommentsCloseAfterDays = 0, int FeedItems = 20, bool CommentsModerate = true, string TrackingScriptUrl = "");

/// <summary>Reads the site settings with safe defaults, so a missing or mistyped row never breaks the site.</summary>
public interface ISiteSettings
{
    Task<PublicSiteSettings> GetPublicAsync(CancellationToken cancellationToken = default);

    /// <summary>The time zone the legacy BlogEngine dates were written in (an IANA or Windows id); UTC when unset or unknown.</summary>
    Task<string> GetLegacyTimeZoneAsync(CancellationToken cancellationToken = default);
}

/// <summary>Implementation over the ordinary presenter; the first row with a name wins, names compare ignoring case.</summary>
/// <summary>Operator choices for the site (configuration, not an administrator's screen).</summary>
public sealed class SiteOptions
{
    /// <summary>The hosts a tracking script may come from (for example plausible.io). Empty means no tracking script is ever served.</summary>
    public string[] AllowedScriptHosts { get; set; } = [];
}

public sealed class SiteSettings(IEntityPresenter<SiteSetting> presenter, SiteOptions? options = null) : ISiteSettings
{
    public const string DefaultTitle = "AI Research Blog";
    public const int DefaultPostsPerPage = 10;
    public const int MaxPostsPerPage = 50;
    public const int MaxCloseAfterDays = 3650;
    public const int DefaultFeedItems = 20;
    public const int MaxFeedItems = 100;

    public async Task<PublicSiteSettings> GetPublicAsync(CancellationToken cancellationToken = default)
    {
        var values = await ValuesAsync(cancellationToken).ConfigureAwait(false);
        var title = values.GetValueOrDefault("SiteTitle");
        var perPage = int.TryParse(values.GetValueOrDefault("PostsPerPage"), out var parsed) ? Math.Clamp(parsed, 1, MaxPostsPerPage) : DefaultPostsPerPage;
        // Comments are on unless the setting says false (a missing or mistyped row never closes them by accident).
        var commentsEnabled = !string.Equals(values.GetValueOrDefault("CommentsEnabled")?.Trim(), "false", StringComparison.OrdinalIgnoreCase);
        // Moderated unless the setting says exactly false: a typo never publishes a visitor's text unreviewed.
        var moderate = !string.Equals(values.GetValueOrDefault("CommentsModerate")?.Trim(), "false", StringComparison.OrdinalIgnoreCase);
        // 0 (never) unless the setting is a positive number of days; a typo never closes comments.
        var closeAfter = int.TryParse(values.GetValueOrDefault("CommentsCloseAfterDays")?.Trim(), out var days) ? Math.Clamp(days, 0, MaxCloseAfterDays) : 0;
        // The feed lists 20 unless the setting is a number from 1 to 100.
        var feedItems = int.TryParse(values.GetValueOrDefault("FeedItems")?.Trim(), out var items) && items > 0 ? Math.Min(items, MaxFeedItems) : DefaultFeedItems;
        // English unless the setting is a language code (a typo never loads a missing table).
        var language = LanguageFiles.Normalise(values.GetValueOrDefault("Language")) ?? "en";
        // Dark (how the site always started) unless the setting says light.
        var colorMode = string.Equals(values.GetValueOrDefault("ColorMode")?.Trim(), "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
        return new PublicSiteSettings(string.IsNullOrWhiteSpace(title) ? DefaultTitle : title.Trim(), values.GetValueOrDefault("Tagline")?.Trim() ?? string.Empty, perPage, commentsEnabled, colorMode, language, closeAfter, feedItems, moderate, AllowedScriptUrl(values.GetValueOrDefault("TrackingScriptUrl"), options?.AllowedScriptHosts));
    }

    /// <summary>
    /// The tracking address to serve: one https address, no sign-in part, no odd port, whose host the operator allowed; otherwise empty. A script runs with the
    /// full power of the page (it can read an administrator's sign-in), so it is never free-form code and never from a host nobody vouched for.
    /// </summary>
    public static string AllowedScriptUrl(string? value, IReadOnlyCollection<string>? allowedHosts)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || allowedHosts is null || allowedHosts.Count == 0 || text.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '<' or '>' or '\\' or '`'))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0 || !uri.IsDefaultPort
            || !allowedHosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return uri.AbsoluteUri;
    }

    public async Task<string> GetLegacyTimeZoneAsync(CancellationToken cancellationToken = default)
    {
        var zone = (await ValuesAsync(cancellationToken).ConfigureAwait(false)).GetValueOrDefault("TimeZone")?.Trim();
        if (string.IsNullOrEmpty(zone))
        {
            return "UTC";
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(zone);
            return zone;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return "UTC";
        }
    }

    private async Task<Dictionary<string, string?>> ValuesAsync(CancellationToken cancellationToken)
    {
        var rows = await presenter.ListAsync(cancellationToken).ConfigureAwait(false);
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (row.Values.GetValueOrDefault("Name") is { Length: > 0 } name)
            {
                values.TryAdd(name.Trim(), row.Values.GetValueOrDefault("Value"));
            }
        }

        return values;
    }
}

/// <summary>
/// The presenter of the site settings: the four known settings are edited (their value only), never renamed, created or deleted,
/// because the site reads them by name and falls back to a default for a missing one (renaming SiteTitle would silently lose the title).
/// </summary>
public sealed class SiteSettingPresenter(IEntityBll<SiteSetting> bll, EntitySchemaOf<SiteSetting> schema, IEntityRelationProvider? relations = null)
    : EntityPresenter<SiteSetting>(bll, schema.Schema, s => new SiteSetting(s), relations)
{
    private static readonly HashSet<string> Fixed = new(["Name", "Description"], StringComparer.OrdinalIgnoreCase);

    protected override IReadOnlySet<string> ReadOnlyFields => Fixed;

    protected override bool CanCreate => false;

    protected override EntityPermissions PermissionsFor(SiteSetting entity) => new(CanUpdate: true, CanDelete: false);

    /// <summary>
    /// The settings the site reads, with the value each starts at. A seed writes outside every tenant's graph, so a setting added by a later
    /// seed (or a tenant that never had any) has no row to edit; the screen creates the missing ones for the tenant being administered.
    /// </summary>
    private static readonly (string Name, string Value, string Description)[] Known =
    [
        ("SiteTitle", SiteSettings.DefaultTitle, "The name shown in the header and the browser tab."),
        ("Tagline", string.Empty, "A short line under the title (optional)."),
        ("PostsPerPage", SiteSettings.DefaultPostsPerPage.ToString(System.Globalization.CultureInfo.InvariantCulture), "How many posts the list shows on a page (1 to 50)."),
        ("TimeZone", "UTC", "The time zone the legacy BlogEngine dates were written in (for the migration)."),
        ("ColorMode", "dark", "The look every visitor starts with: dark or light (a visitor who uses the toggle in the header keeps their own choice in their browser)."),
        ("Language", "en", "The language of the site: a language code such as en, de or pt-br. The texts come from the language file of that code (English where it has none)."),
        ("CommentsCloseAfterDays", "0", "Close comments on a post this many days after it was published (0 keeps them open for ever)."),
        ("FeedItems", "20", "How many of the newest posts the RSS feed lists (1 to 100)."),
        ("CommentsModerate", "true", "true holds every new visitor comment until a moderator approves it. false publishes comments at once (the name, text length, closing and rate limits still apply); only for a blog whose visitors you trust."),
        ("TrackingScriptUrl", string.Empty, "One https address of an analytics script to load on the public pages (for example https://plausible.io/js/script.js). Only hosts the operator allowed in Site:AllowedScriptHosts work; leave empty for none."),
        ("CommentsEnabled", "true", "Visitor comments on the whole site: true shows the comment form where a post allows comments, false hides it and refuses new comments (existing comments are kept)."),
    ];

    public override async Task<EntityGridModel> GetGridAsync(EntityGridQuery query, CancellationToken cancellationToken = default)
    {
        await EnsureKnownAsync(cancellationToken).ConfigureAwait(false);
        return await base.GetGridAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public override async Task<EntityGridModel> GetGridAsync(CancellationToken cancellationToken = default)
    {
        await EnsureKnownAsync(cancellationToken).ConfigureAwait(false);
        return await base.GetGridAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureKnownAsync(CancellationToken cancellationToken)
    {
        var present = (await ListAsync(cancellationToken).ConfigureAwait(false))
            .Select(row => row.Values.GetValueOrDefault("Name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value, description) in Known.Where(setting => !present.Contains(setting.Name)))
        {
            await CreateAsync(
                new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["Name"] = name, ["Value"] = value, ["Description"] = description }),
                cancellationToken).ConfigureAwait(false);
        }
    }
}
