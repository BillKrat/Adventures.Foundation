// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
using Adventures.Common.Exceptions;

namespace Adventures.Blog;

/// <summary>What an author (a person at the keyboard or an AI over MCP) supplies for a post: Markdown content, category and tag names (created when missing).</summary>
public sealed record DraftPost(
    string Title,
    string? Content = null,
    string? Description = null,
    string? Slug = null,
    IReadOnlyList<string>? Categories = null,
    IReadOnlyList<string>? Tags = null,
    bool? EnableComments = null);

/// <summary>One post as the authoring surface reports it.</summary>
public sealed record DraftSummary(
    string Id, string Title, string Slug, bool IsPublished, string? DatePublished, IReadOnlyList<string> Categories, IReadOnlyList<string> Tags, string? RevisionOf = null, string? Approval = null);

/// <summary>A category or tag as the authoring surface reports it.</summary>
public sealed record TaxonomyTerm(string Id, string Name);

/// <summary>
/// The drafts-only way to write content: create a post as a draft under a fixed author, revise it while it is still a draft, list
/// the author own posts, and file them under categories and tags that are created by name when they do not exist. There is
/// deliberately no way here to publish, to change a published post, or to touch another author post: those stay administrator
/// actions in the admin. The host decides who may call it (a scope-protected controller, an MCP tool); this holds the rules.
/// </summary>
public interface IContentAuthoring
{
    Task<DraftSummary> CreateDraftAsync(string authorUserName, DraftPost draft, CancellationToken cancellationToken = default);

    /// <summary>Revises a draft the author owns; null when no such post. Throws <see cref="BusinessRuleException"/> for a published post or someone else post.</summary>
    Task<DraftSummary?> UpdateDraftAsync(string authorUserName, string postId, DraftPost draft, CancellationToken cancellationToken = default);

    /// <summary>
    /// Proposes changes to one of the author own PUBLISHED posts without touching what readers see: a draft copy carrying the changes,
    /// marked as a revision of the post. One open revision per post (asking again updates it). An administrator applies or discards it in
    /// the admin. Null when no such post; refused for a draft (edit it directly), someone else post.
    /// </summary>
    Task<DraftSummary?> ProposeRevisionAsync(string authorUserName, string publishedPostId, DraftPost changes, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DraftSummary>> ListMineAsync(string authorUserName, CancellationToken cancellationToken = default);

    /// <summary>The conversation under one of the author own posts (oldest first). Refused for a post that is someone else's.</summary>
    Task<IReadOnlyList<ConversationEntry>> ListCommentsAsync(string authorUserName, string postId, CancellationToken cancellationToken = default);

    /// <summary>Adds a plain comment (never a decision: the human records those) to the conversation under one of the author own posts.</summary>
    Task<ConversationEntry> AddCommentAsync(string authorUserName, string postId, string text, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaxonomyTerm>> ListCategoriesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaxonomyTerm>> ListTagsAsync(CancellationToken cancellationToken = default);

    /// <summary>The existing category with this name (any case) or a new one.</summary>
    Task<TaxonomyTerm> EnsureCategoryAsync(string name, CancellationToken cancellationToken = default);

    Task<TaxonomyTerm> EnsureTagAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>Implementation of <see cref="IContentAuthoring"/> over the ordinary presenters, so every rule of the admin editors (draft by default, slug, pointers must exist) applies here too.</summary>
public sealed class ContentAuthoring(
    IEntityPresenter<Post> posts,
    IEntityPresenter<Category> categories,
    IEntityPresenter<Tag> tags,
    IUserPresenter users,
    IPostBll postBll,
    IConversation? conversation = null) : IContentAuthoring
{
    public async Task<IReadOnlyList<ConversationEntry>> ListCommentsAsync(string authorUserName, string postId, CancellationToken cancellationToken = default)
    {
        await EnsureOwnPostAsync(authorUserName, postId, cancellationToken).ConfigureAwait(false);
        return await Conversation().ListAsync(postId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConversationEntry> AddCommentAsync(string authorUserName, string postId, string text, CancellationToken cancellationToken = default)
    {
        await EnsureOwnPostAsync(authorUserName, postId, cancellationToken).ConfigureAwait(false);
        return await Conversation().AddAsync(postId, authorUserName, CommentKinds.Comment, text, cancellationToken).ConfigureAwait(false);
    }

    private IConversation Conversation() => conversation ?? throw new InvalidOperationException("The conversation service is not registered.");

    private async Task EnsureOwnPostAsync(string authorUserName, string postId, CancellationToken cancellationToken)
    {
        var authorId = await AuthorIdAsync(authorUserName, cancellationToken).ConfigureAwait(false);
        var existing = await posts.GetFormAsync(postId, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException("The post does not exist.");
        if (!string.Equals(existing.Entity.Values.GetValueOrDefault("Author"), authorId, StringComparison.Ordinal))
        {
            throw new BusinessRuleException("This post was written by someone else; its conversation cannot be used here.");
        }
    }

    public async Task<DraftSummary> CreateDraftAsync(string authorUserName, DraftPost draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var authorId = await AuthorIdAsync(authorUserName, cancellationToken).ConfigureAwait(false);
        var values = await ValuesAsync(draft, cancellationToken).ConfigureAwait(false);
        values["Author"] = authorId;
        var created = await posts.CreateAsync(new EntityDataModel(string.Empty, values), cancellationToken).ConfigureAwait(false);
        // a draft filed under Collaboration goes to the human for review
        if (InCollaboration(created.Entity))
        {
            await postBll.SetApprovalAsync(created.Entity.EntityId, PostApproval.NeedsReview, null, cancellationToken).ConfigureAwait(false);
            created = await posts.GetFormAsync(created.Entity.EntityId, cancellationToken).ConfigureAwait(false) ?? created;
        }

        return Summarise(created.Entity, created.Schema);
    }

    public async Task<DraftSummary?> UpdateDraftAsync(string authorUserName, string postId, DraftPost draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var authorId = await AuthorIdAsync(authorUserName, cancellationToken).ConfigureAwait(false);
        var existing = await posts.GetFormAsync(postId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return null;
        }

        if (!string.Equals(existing.Entity.Values.GetValueOrDefault("Author"), authorId, StringComparison.Ordinal))
        {
            throw new BusinessRuleException("This post was written by someone else; it cannot be changed here.");
        }

        if (string.Equals(existing.Entity.Values.GetValueOrDefault("IsPublished"), "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException("This post is published. Published posts are changed in the admin; write a new draft instead.");
        }

        var values = await ValuesAsync(draft, cancellationToken).ConfigureAwait(false);
        var updated = await posts.UpdateAsync(postId, new EntityDataModel(postId, values), cancellationToken).ConfigureAwait(false);
        if (updated is not null && InCollaboration(updated.Entity) && updated.Entity.Values.GetValueOrDefault("Approval") != PostApproval.NeedsReview)
        {
            // the author changed a collaboration draft: an earlier approval or a request for changes no longer describes it
            await postBll.SetApprovalAsync(postId, PostApproval.NeedsReview, null, cancellationToken).ConfigureAwait(false);
            updated = await posts.GetFormAsync(postId, cancellationToken).ConfigureAwait(false) ?? updated;
        }

        return updated is null ? null : Summarise(updated.Entity, updated.Schema);
    }

    public async Task<DraftSummary?> ProposeRevisionAsync(string authorUserName, string publishedPostId, DraftPost changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var authorId = await AuthorIdAsync(authorUserName, cancellationToken).ConfigureAwait(false);
        var original = await posts.GetFormAsync(publishedPostId, cancellationToken).ConfigureAwait(false);
        if (original is null)
        {
            return null;
        }

        if (!string.Equals(original.Entity.Values.GetValueOrDefault("Author"), authorId, StringComparison.Ordinal))
        {
            throw new BusinessRuleException("This post was written by someone else; a revision cannot be proposed here.");
        }

        if (!string.Equals(original.Entity.Values.GetValueOrDefault("IsPublished"), "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException("This post is still a draft: change it directly instead of proposing a revision.");
        }

        // start from the published post so a revision that names only the content keeps the title, description, categories and tags
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in new[] { "Title", "Content", "Description", "EnableComments", "Categories", "Tags" })
        {
            if (original.Entity.Values.GetValueOrDefault(field) is { Length: > 0 } current)
            {
                values[field] = current;
            }
        }

        foreach (var (name, value) in await ValuesAsync(changes, cancellationToken).ConfigureAwait(false))
        {
            if (!name.Equals("Slug", StringComparison.OrdinalIgnoreCase))
            {
                values[name] = value; // the published slug is stable: a revision never changes the address
            }
        }

        var grid = await posts.GetGridAsync(new EntityGridQuery(PageSize: EntityGridQuery.MaxPageSize), cancellationToken).ConfigureAwait(false);
        var open = grid.Rows.FirstOrDefault(row => string.Equals(row.Values.GetValueOrDefault("RevisionOf"), publishedPostId, StringComparison.Ordinal));
        string revisionId;
        if (open is not null)
        {
            revisionId = open.EntityId;
            await posts.UpdateAsync(revisionId, new EntityDataModel(revisionId, values), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            values["Author"] = authorId;
            revisionId = (await posts.CreateAsync(new EntityDataModel(string.Empty, values), cancellationToken).ConfigureAwait(false)).Entity.EntityId;
            await postBll.MarkRevisionAsync(revisionId, publishedPostId, cancellationToken).ConfigureAwait(false);
        }

        var saved = await posts.GetFormAsync(revisionId, cancellationToken).ConfigureAwait(false);
        return saved is null ? null : Summarise(saved.Entity, saved.Schema);
    }

    public async Task<IReadOnlyList<DraftSummary>> ListMineAsync(string authorUserName, CancellationToken cancellationToken = default)
    {
        var authorId = await AuthorIdAsync(authorUserName, cancellationToken).ConfigureAwait(false);
        var all = await posts.GetGridAsync(new EntityGridQuery(PageSize: EntityGridQuery.MaxPageSize, Sort: "DatePublished", Descending: true), cancellationToken).ConfigureAwait(false);
        return all.Rows
            .Where(row => string.Equals(row.Values.GetValueOrDefault("Author"), authorId, StringComparison.Ordinal))
            .Select(row => Summarise(row, all.Schema))
            .ToList();
    }

    public async Task<IReadOnlyList<TaxonomyTerm>> ListCategoriesAsync(CancellationToken cancellationToken = default) =>
        await TermsAsync(categories, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<TaxonomyTerm>> ListTagsAsync(CancellationToken cancellationToken = default) =>
        await TermsAsync(tags, cancellationToken).ConfigureAwait(false);

    public Task<TaxonomyTerm> EnsureCategoryAsync(string name, CancellationToken cancellationToken = default) =>
        EnsureAsync(categories, name, cancellationToken);

    public Task<TaxonomyTerm> EnsureTagAsync(string name, CancellationToken cancellationToken = default) =>
        EnsureAsync(tags, name, cancellationToken);

    private async Task<string> AuthorIdAsync(string authorUserName, CancellationToken cancellationToken)
    {
        var form = await users.GetFormByUserNameAsync(authorUserName, cancellationToken).ConfigureAwait(false);
        return form?.Entity.EntityId ?? throw new BusinessRuleException($"The author account '{authorUserName}' does not exist.");
    }

    private async Task<Dictionary<string, string?>> ValuesAsync(DraftPost draft, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(draft.Title))
        {
            values["Title"] = draft.Title;
        }

        if (draft.Content is not null)
        {
            values["Content"] = draft.Content;
        }

        if (draft.Description is not null)
        {
            values["Description"] = draft.Description;
        }

        if (draft.Slug is not null)
        {
            values["Slug"] = draft.Slug;
        }

        if (draft.EnableComments is { } comments)
        {
            values["EnableComments"] = comments ? "true" : "false";
        }

        if (draft.Categories is not null)
        {
            var ids = new List<string>();
            foreach (var name in draft.Categories.Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                ids.Add((await EnsureAsync(categories, name, cancellationToken).ConfigureAwait(false)).Id);
            }

            values["Categories"] = string.Join(',', ids.Distinct(StringComparer.Ordinal));
        }

        if (draft.Tags is not null)
        {
            var ids = new List<string>();
            foreach (var name in draft.Tags.Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                ids.Add((await EnsureAsync(tags, name, cancellationToken).ConfigureAwait(false)).Id);
            }

            values["Tags"] = string.Join(',', ids.Distinct(StringComparer.Ordinal));
        }

        return values;
    }

    private static async Task<IReadOnlyList<TaxonomyTerm>> TermsAsync<T>(IEntityPresenter<T> presenter, CancellationToken cancellationToken)
        where T : DynamicEntity =>
        (await presenter.ListAsync(cancellationToken).ConfigureAwait(false))
            .Select(entity => new TaxonomyTerm(entity.EntityId, entity.Values.GetValueOrDefault("Name") ?? string.Empty))
            .OrderBy(term => term.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static async Task<TaxonomyTerm> EnsureAsync<T>(IEntityPresenter<T> presenter, string name, CancellationToken cancellationToken)
        where T : DynamicEntity
    {
        var trimmed = name.Trim();
        var existing = (await TermsAsync(presenter, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(term => string.Equals(term.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var created = await presenter.CreateAsync(
            new EntityDataModel(string.Empty, new Dictionary<string, string?> { ["Name"] = trimmed }), cancellationToken).ConfigureAwait(false);
        return new TaxonomyTerm(created.Entity.EntityId, trimmed);
    }

    private static DraftSummary Summarise(EntityDataModel row, EntitySchemaModel schema)
    {
        _ = schema;
        return new DraftSummary(
            row.EntityId,
            row.Values.GetValueOrDefault("Title") ?? string.Empty,
            row.Values.GetValueOrDefault("Slug") ?? string.Empty,
            string.Equals(row.Values.GetValueOrDefault("IsPublished"), "true", StringComparison.OrdinalIgnoreCase),
            row.Values.GetValueOrDefault("DatePublished"),
            Names(row, "Categories"),
            Names(row, "Tags"),
            row.Values.GetValueOrDefault("RevisionOf") is { Length: > 0 } of ? of : null,
            row.Values.GetValueOrDefault("Approval") is { Length: > 0 } approval ? approval : null);
    }

    private static bool InCollaboration(EntityDataModel post) =>
        Names(post, "Categories").Contains(PostApproval.CollaborationCategory, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> Names(EntityDataModel row, string field) =>
        row.Display?.GetValueOrDefault(field) is { Length: > 0 } text
            ? text.Split(", ", StringSplitOptions.RemoveEmptyEntries)
            : [];
}
