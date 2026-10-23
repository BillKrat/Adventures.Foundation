namespace Adventures.Entities;

/// <summary>One named site setting (SiteTitle, PostsPerPage, ...); its schema ships in Sql/seed/seed-011-site-settings.nq.</summary>
public sealed class SiteSetting(EntitySchema schema) : DynamicEntity(schema);

/// <summary>The settings a visitor's browser needs: no secrets, nothing about the server.</summary>
public sealed record PublicSiteSettings(string Title, string Tagline, int PostsPerPage);

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
        return new PublicSiteSettings(string.IsNullOrWhiteSpace(title) ? DefaultTitle : title.Trim(), values.GetValueOrDefault("Tagline")?.Trim() ?? string.Empty, perPage);
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
}
