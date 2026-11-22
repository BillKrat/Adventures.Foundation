// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
namespace Adventures.Blog;

/// <summary>One named site setting (SiteTitle, PostsPerPage, ...); its schema ships in Sql/seed/seed-011-site-settings.nq.</summary>
public sealed class SiteSetting(EntitySchema schema) : DynamicEntity(schema);

/// <summary>The settings a visitor's browser needs: no secrets, nothing about the server.</summary>
/// <param name="Language">The language code of the site (en unless set): the language file of that code supplies the button and screen texts.</param>
/// <param name="ColorMode">"dark" or "light": the site default a visitor sees until they use the header toggle themselves.</param>
public sealed record PublicSiteSettings(string Title, string Tagline, int PostsPerPage, bool CommentsEnabled = true, string ColorMode = "dark", string Language = "en");

/// <summary>Reads the site settings with safe defaults, so a missing or mistyped row never breaks the site.</summary>
public interface ISiteSettings
{
    Task<PublicSiteSettings> GetPublicAsync(CancellationToken cancellationToken = default);

    /// <summary>The time zone the legacy BlogEngine dates were written in (an IANA or Windows id); UTC when unset or unknown.</summary>
    Task<string> GetLegacyTimeZoneAsync(CancellationToken cancellationToken = default);
}

/// <summary>Implementation over the ordinary presenter; the first row with a name wins, names compare ignoring case.</summary>
public sealed class SiteSettings(IEntityPresenter<SiteSetting> presenter) : ISiteSettings
{
    public const string DefaultTitle = "AI Research Blog";
    public const int DefaultPostsPerPage = 10;
    public const int MaxPostsPerPage = 50;

    public async Task<PublicSiteSettings> GetPublicAsync(CancellationToken cancellationToken = default)
    {
        var values = await ValuesAsync(cancellationToken).ConfigureAwait(false);
        var title = values.GetValueOrDefault("SiteTitle");
        var perPage = int.TryParse(values.GetValueOrDefault("PostsPerPage"), out var parsed) ? Math.Clamp(parsed, 1, MaxPostsPerPage) : DefaultPostsPerPage;
        // Comments are on unless the setting says false (a missing or mistyped row never closes them by accident).
        var commentsEnabled = !string.Equals(values.GetValueOrDefault("CommentsEnabled")?.Trim(), "false", StringComparison.OrdinalIgnoreCase);
        // English unless the setting is a language code (a typo never loads a missing table).
        var language = LanguageFiles.Normalise(values.GetValueOrDefault("Language")) ?? "en";
        // Dark (how the site always started) unless the setting says light.
        var colorMode = string.Equals(values.GetValueOrDefault("ColorMode")?.Trim(), "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
        return new PublicSiteSettings(string.IsNullOrWhiteSpace(title) ? DefaultTitle : title.Trim(), values.GetValueOrDefault("Tagline")?.Trim() ?? string.Empty, perPage, commentsEnabled, colorMode, language);
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
