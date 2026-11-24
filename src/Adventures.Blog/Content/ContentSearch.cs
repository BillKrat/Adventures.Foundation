using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Adventures.Common.Exceptions;
using Adventures.Entities;

namespace Adventures.Blog;

/// <summary>One search hit: a post or a page with a short plain-text snippet.</summary>
public sealed record SearchHit(string Kind, string Slug, string Title, string Snippet, string? DatePublished);

public sealed record SearchResult(string Query, int Total, int Page, int PageSize, IReadOnlyList<SearchHit> Hits);

/// <summary>
/// Basic search of the blog's own content: published posts and pages only (no draft, no proposed revision), every word must be found somewhere
/// (title, tags and categories, description, text), a title counts for more than the text. Matching is plain case-insensitive text, never a pattern, on the
/// text with markup removed, so the query cannot name a tag or an attribute and nothing but words ever comes back.
/// </summary>
public static partial class ContentSearch
{
    public const int MinQueryLength = 2;
    public const int MaxQueryLength = 100;
    public const int MaxTerms = 6;
    public const int SnippetLength = 180;
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 25;

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptBlocks();

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex Tags();

    [GeneratedRegex(@"!?\[([^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLinks();

    [GeneratedRegex(@"(?m)^\s{0,3}(#{1,6}|>|[-*+])\s+", RegexOptions.CultureInvariant)]
    private static partial Regex LineMarkers();

    [GeneratedRegex(@"[`*~]+", RegexOptions.CultureInvariant)]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    public static SearchResult Run(string? query, IEnumerable<EntityDataModel> posts, IEnumerable<EntityDataModel> pages, int page, int pageSize)
    {
        var text = query?.Trim() ?? string.Empty;
        if (text.Length < MinQueryLength)
        {
            throw new BusinessRuleException($"Type at least {(MinQueryLength == 2 ? "two" : MinQueryLength.ToString(CultureInfo.InvariantCulture))} letters to search.");
        }

        if (text.Length > MaxQueryLength)
        {
            throw new BusinessRuleException($"A search can be at most {MaxQueryLength} characters.");
        }

        var terms = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(MaxTerms).ToList();
        var size = Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);
        var number = Math.Max(1, page);

        var scored = new List<(int Score, string Date, SearchHit Hit)>();
        foreach (var row in posts.Where(r => IsLive(r) && string.IsNullOrEmpty(Value(r, "RevisionOf"))))
        {
            Consider(scored, row, "post", terms, Term(row, "Tags") + " " + Term(row, "Categories"));
        }

        foreach (var row in pages.Where(IsLive))
        {
            Consider(scored, row, "page", terms, Value(row, "Keywords") ?? string.Empty);
        }

        var ordered = scored.OrderByDescending(s => s.Score).ThenByDescending(s => s.Date, StringComparer.Ordinal).ThenBy(s => s.Hit.Title, StringComparer.OrdinalIgnoreCase).ToList();
        return new SearchResult(text, ordered.Count, number, size, ordered.Skip((number - 1) * size).Take(size).Select(s => s.Hit).ToList());
    }

    private static void Consider(List<(int, string, SearchHit)> scored, EntityDataModel row, string kind, List<string> terms, string labels)
    {
        var title = Value(row, "Title") ?? string.Empty;
        var description = PlainText(Value(row, "Description") ?? string.Empty);
        var body = PlainText(Value(row, "Content") ?? string.Empty);
        var score = 0;
        foreach (var term in terms)
        {
            var points = 0;
            points = Math.Max(points, Has(title, term) ? 5 : 0);
            points = Math.Max(points, Has(labels, term) ? 3 : 0);
            points = Math.Max(points, Has(description, term) ? 2 : 0);
            points = Math.Max(points, Has(body, term) ? 1 : 0);
            if (points == 0)
            {
                return;   // every word must be found
            }

            score += points;
        }

        var date = Value(row, "DatePublished");
        scored.Add((score, date ?? string.Empty, new SearchHit(kind, Value(row, "Slug") ?? string.Empty, title, Snippet(body, description, terms), date)));
    }

    private static bool IsLive(EntityDataModel row) => string.Equals(Value(row, "IsPublished"), "true", StringComparison.OrdinalIgnoreCase);

    private static string? Value(EntityDataModel row, string field) => row.Values.GetValueOrDefault(field);

    private static string Term(EntityDataModel row, string field) => row.Display?.GetValueOrDefault(field) ?? string.Empty;

    private static bool Has(string haystack, string term) => haystack.Contains(term, StringComparison.OrdinalIgnoreCase);

    /// <summary>Words only: markup, Markdown marks and link addresses removed, entities decoded, white space collapsed.</summary>
    public static string PlainText(string content)
    {
        var text = ScriptBlocks().Replace(content, " ");
        text = Tags().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        text = MarkdownLinks().Replace(text, "$1");
        text = LineMarkers().Replace(text, string.Empty);
        text = Emphasis().Replace(text, string.Empty);
        return Whitespace().Replace(text, " ").Trim();
    }

    /// <summary>About <see cref="SnippetLength"/> characters around the first place a word is found in the text; the description (or the start of the text) when the words are only in the title or labels.</summary>
    private static string Snippet(string body, string description, List<string> terms)
    {
        var at = terms.Select(t => body.IndexOf(t, StringComparison.OrdinalIgnoreCase)).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
        if (at < 0)
        {
            return description.Length > 0 ? Cut(description) : Cut(body);
        }

        var start = Math.Max(0, at - SnippetLength / 3);
        var length = Math.Min(SnippetLength, body.Length - start);
        var piece = new StringBuilder();
        piece.Append(start > 0 ? "…" : string.Empty).Append(body.AsSpan(start, length));
        if (start + length < body.Length)
        {
            piece.Append('…');
        }

        return piece.ToString();
    }

    private static string Cut(string text) => text.Length <= SnippetLength ? text : text[..SnippetLength].TrimEnd() + "…";
}

/// <summary>Searches the tenant's published posts and pages.</summary>
public interface IContentSearcher
{
    Task<SearchResult> SearchAsync(string? query, int page, int pageSize, CancellationToken cancellationToken = default);
}

public sealed class ContentSearcher(IEntityPresenter<Post> posts, IEntityPresenter<Page> pages) : IContentSearcher
{
    public async Task<SearchResult> SearchAsync(string? query, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        // validate before reading anything, so a refused query costs nothing
        _ = ContentSearch.Run(query, [], [], 1, 1);
        return ContentSearch.Run(query, await AllAsync(posts, cancellationToken).ConfigureAwait(false), await AllAsync(pages, cancellationToken).ConfigureAwait(false), page, pageSize);
    }

    private static async Task<IReadOnlyList<EntityDataModel>> AllAsync<T>(IEntityPresenter<T> presenter, CancellationToken cancellationToken)
        where T : DynamicEntity
    {
        var all = new List<EntityDataModel>();
        for (var page = 1; ; page++)
        {
            var grid = await presenter.GetGridAsync(new EntityGridQuery(page, EntityGridQuery.MaxPageSize), cancellationToken).ConfigureAwait(false);
            all.AddRange(grid.Rows);
            if (grid.Paging is null || page >= grid.Paging.TotalPages)
            {
                return all;
            }
        }
    }
}
