using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace Adventures.Blog;

/// <summary>One text the server produces, with its English default. Declared with <see cref="ServerTextServiceCollectionExtensions.AddServerText(IServiceCollection, string, string)"/>.</summary>
public sealed record ServerTextDeclaration(string Key, string English);

/// <summary>Every text the server declared, with its English default: what the language screen offers as keys to translate, and the fallback for a key a language file leaves out.</summary>
public interface IServerTextCatalog
{
    IReadOnlyDictionary<string, string> Defaults { get; }
}

public sealed class ServerTextCatalog(IEnumerable<ServerTextDeclaration> declarations) : IServerTextCatalog
{
    public IReadOnlyDictionary<string, string> Defaults { get; } = declarations
        .GroupBy(d => d.Key, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.Last().English, StringComparer.Ordinal);
}

/// <summary>The site language of this request and its table, read once (the store is asynchronous; <c>IStringLocalizer</c> is not).</summary>
public interface IRequestLanguage
{
    string Language { get; }

    IReadOnlyDictionary<string, string> Table { get; }

    /// <summary>Reads the tenant's site language and its table. Safe to call again; a failure leaves English in place.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);
}

public sealed class RequestLanguage(ILanguageFiles files, ISiteSettings settings) : IRequestLanguage
{
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    public string Language { get; private set; } = "en";

    public IReadOnlyDictionary<string, string> Table { get; private set; } = Empty;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var language = (await settings.GetPublicAsync(cancellationToken).ConfigureAwait(false)).Language;
            Table = await files.GetAsync(language, cancellationToken).ConfigureAwait(false);
            Language = language;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // a text lookup must never take a request down: English defaults answer
            Language = "en";
            Table = Empty;
        }
    }
}

/// <summary>
/// The standard <see cref="IStringLocalizer"/> over the request's language table: the table's text, else the declared English default, else the key itself
/// (marked not found). <c>{0}</c> style arguments are filled in after the text is chosen, so a translation can move them.
/// </summary>
public sealed class ServerStringLocalizer(IRequestLanguage language, IServerTextCatalog catalog) : IStringLocalizer
{
    public LocalizedString this[string name] => Find(name, []);

    public LocalizedString this[string name, params object[] arguments] => Find(name, arguments);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        catalog.Defaults.Keys.Union(language.Table.Keys, StringComparer.Ordinal).Select(key => Find(key, []));

    private LocalizedString Find(string name, object[] arguments)
    {
        var found = language.Table.TryGetValue(name, out var text) && !string.IsNullOrWhiteSpace(text);
        if (!found && catalog.Defaults.TryGetValue(name, out var english))
        {
            text = english;
            found = true;
        }

        if (!found)
        {
            return new LocalizedString(name, name, resourceNotFound: true);
        }

        return new LocalizedString(name, arguments.Length == 0 ? text! : string.Format(CultureInfo.CurrentCulture, text!, arguments));
    }
}

public sealed class ServerStringLocalizerOf<T>(IRequestLanguage language, IServerTextCatalog catalog) : IStringLocalizer<T>
{
    private readonly ServerStringLocalizer _inner = new(language, catalog);

    public LocalizedString this[string name] => _inner[name];

    public LocalizedString this[string name, params object[] arguments] => _inner[name, arguments];

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => _inner.GetAllStrings(includeParentCultures);
}

public static class ServerTextServiceCollectionExtensions
{
    /// <summary>Registers the request language, the catalog and the standard localizers (<c>IStringLocalizer</c> and <c>IStringLocalizer&lt;T&gt;</c>) over the tenant's language file. Needs <see cref="ILanguageFiles"/> and <see cref="ISiteSettings"/>.</summary>
    public static IServiceCollection AddServerText(this IServiceCollection services)
    {
        services.TryAddSingleton<IServerTextCatalog, ServerTextCatalog>();
        services.TryAddScoped<IRequestLanguage, RequestLanguage>();
        services.TryAddScoped<IStringLocalizer, ServerStringLocalizer>();
        services.TryAddScoped(typeof(IStringLocalizer<>), typeof(ServerStringLocalizerOf<>));
        return services;
    }

    /// <summary>Declares a text the server produces, with its English default (the fallback, and what the language screen lists to translate).</summary>
    public static IServiceCollection AddServerText(this IServiceCollection services, string key, string english)
    {
        services.AddSingleton(new ServerTextDeclaration(key, english));
        return services;
    }
}
