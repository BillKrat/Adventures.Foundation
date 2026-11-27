using Adventures.Entities;

namespace Adventures.Browser.Client;

public enum SiteSignInResult
{
    SignedIn,

    /// <summary>The name or password was refused, or the browser API does not serve that domain. It does not say which.</summary>
    Refused,

    /// <summary>The site wants the password changed before it answers anything; do that on the site.</summary>
    MustChangePassword,
}

/// <summary>The schema browser API: a separate session for each domain.</summary>
public interface IBrowserApi
{
    bool IsSignedIn(string domain);

    Task<SiteSignInResult> SignInAsync(string domain, string userName, string password, CancellationToken cancellationToken = default);

    void SignOut(string domain);

    Task<SchemaBrowserContext> GetContextAsync(string domain, CancellationToken cancellationToken = default);

    Task<SchemaCatalogList> GetSchemasAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>One schema, or null when the site has none with this id.</summary>
    Task<SchemaDetail?> GetSchemaAsync(string domain, string schemaId, CancellationToken cancellationToken = default);
}
