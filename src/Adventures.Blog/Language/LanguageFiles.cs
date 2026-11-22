using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Adventures.Common.Exceptions;
using Adventures.Entities;

namespace Adventures.Blog;

/// <summary>One stored language file as the row is kept: a language code and its table of label texts as JSON. Schema in Sql/seed/seed-026-language-files.nq.</summary>
public sealed class LanguageFile(EntitySchema schema) : DynamicEntity(schema);

/// <summary>Where language files live in the store, and the limits a file must keep.</summary>
public static partial class LanguageConstants
{
    public const string SeedName = "026-language-files";
    public const string DefaultGraph = "https://global-webnet.com/graph/i18n";
    public const string SchemaIri = "https://schema.global-webnet.com/i18n/LanguageFile";
    public const string BaseIri = "https://global-webnet.com/id/language-file/";
    public const string TypeIri = "https://global-webnet.com/ontology/i18n#LanguageFile";

    /// <summary>The most texts a file may hold (the screens have far fewer keys than this).</summary>
    public const int MaxKeys = 500;

    /// <summary>The longest single text.</summary>
    public const int MaxTextLength = 300;

    [GeneratedRegex("^[a-z]{2,3}(-[a-z0-9]{2,8})?$", RegexOptions.CultureInvariant)]
    internal static partial Regex CodePattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{0,40}$", RegexOptions.CultureInvariant)]
    internal static partial Regex KeyPattern();
}

/// <summary>A stored language: its code and how many texts it holds.</summary>
public sealed record LanguageSummary(string Language, int Count);

/// <summary>The language tables of a tenant: one per language code, replaced as a whole, checked before they are kept.</summary>
public interface ILanguageFiles
{
    /// <summary>The table for a code (key to text); empty when the code is not a language code or nothing is stored for it.</summary>
    Task<IReadOnlyDictionary<string, string>> GetAsync(string language, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LanguageSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores the table for a code, replacing any earlier one. A business-rule error (and nothing stored) for a bad code, key or text, too many texts, or no table.</summary>
    Task SaveAsync(string language, IReadOnlyDictionary<string, string> labels, CancellationToken cancellationToken = default);

    /// <summary>Removes the file of a code; false when none was stored.</summary>
    Task<bool> RemoveAsync(string language, CancellationToken cancellationToken = default);
}

public sealed class LanguageFiles(IEntityBll<LanguageFile> store, EntitySchemaOf<LanguageFile> schema) : ILanguageFiles
{
    public async Task<IReadOnlyDictionary<string, string>> GetAsync(string language, CancellationToken cancellationToken = default)
    {
        var code = Normalise(language);
        if (code is null)
        {
            return new Dictionary<string, string>();
        }

        var row = (await StoredAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(f => Is(f, code));
        return row is null ? new Dictionary<string, string>() : TableOf(row);
    }

    public async Task<IReadOnlyList<LanguageSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        (await StoredAsync(cancellationToken).ConfigureAwait(false))
            .Select(f => new LanguageSummary(f.GetValue("Language") as string ?? string.Empty, TableOf(f).Count))
            .OrderBy(s => s.Language, StringComparer.Ordinal)
            .ToList();

    public async Task SaveAsync(string language, IReadOnlyDictionary<string, string> labels, CancellationToken cancellationToken = default)
    {
        var code = Normalise(language) ?? throw new BusinessRuleException("A language code is two or three letters, optionally with a region, such as de or pt-br.");
        var clean = Check(labels);
        var json = JsonSerializer.Serialize(clean);

        var row = (await StoredAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(f => Is(f, code));
        if (row is not null)
        {
            row.Set("Labels", Guid.NewGuid().ToString(), json);
            await store.UpdateAsync(row, cancellationToken).ConfigureAwait(false);
            return;
        }

        var created = new LanguageFile(schema.Schema);
        created.Set("Id", Guid.NewGuid().ToString(), Guid.NewGuid());
        created.Set("Language", Guid.NewGuid().ToString(), code);
        created.Set("Labels", Guid.NewGuid().ToString(), json);
        await store.CreateAsync(created, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RemoveAsync(string language, CancellationToken cancellationToken = default)
    {
        var code = Normalise(language);
        var row = code is null ? null : (await StoredAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(f => Is(f, code));
        if (row?.EntityId is null)
        {
            return false;
        }

        await store.DeleteAsync(row.EntityId.Value, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<List<LanguageFile>> StoredAsync(CancellationToken cancellationToken) =>
        (await store.ListAsync(cancellationToken).ConfigureAwait(false)).Where(f => f.EntityId is not null).ToList();

    private static bool Is(LanguageFile row, string code) => string.Equals(row.GetValue("Language") as string, code, StringComparison.Ordinal);

    /// <summary>The code in stored form (lower case), or null when it is not a language code.</summary>
    internal static string? Normalise(string? language)
    {
        var code = language?.Trim().ToLowerInvariant();
        return code is not null && LanguageConstants.CodePattern().IsMatch(code) ? code : null;
    }

    private static Dictionary<string, string> TableOf(LanguageFile row)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(row.GetValue("Labels") as string ?? "{}") ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static Dictionary<string, string> Check(IReadOnlyDictionary<string, string>? labels)
    {
        if (labels is null)
        {
            throw new BusinessRuleException("A language file needs a table of texts.");
        }

        if (labels.Count > LanguageConstants.MaxKeys)
        {
            throw new BusinessRuleException($"A language file holds at most {LanguageConstants.MaxKeys.ToString(CultureInfo.InvariantCulture)} texts.");
        }

        var clean = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, text) in labels)
        {
            if (!LanguageConstants.KeyPattern().IsMatch(key))
            {
                throw new BusinessRuleException($"'{key}' is not a text key (letters and digits, starting with a letter).");
            }

            var trimmed = text?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                throw new BusinessRuleException($"The text for '{key}' is empty.");
            }

            if (trimmed.Length > LanguageConstants.MaxTextLength)
            {
                throw new BusinessRuleException($"The text for '{key}' is longer than {LanguageConstants.MaxTextLength.ToString(CultureInfo.InvariantCulture)} characters.");
            }

            if (trimmed.Any(char.IsControl))
            {
                throw new BusinessRuleException($"The text for '{key}' holds a control character.");
            }

            clean[key] = trimmed;
        }

        return clean;
    }
}
