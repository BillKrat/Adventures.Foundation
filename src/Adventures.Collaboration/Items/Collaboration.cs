// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
using Adventures.MEF;
using Adventures.Common.Exceptions;

namespace Adventures.Collaboration;

/// <summary>
/// One item of the collaboration channel: a review request or a research question between the people and AIs working on the
/// project. Not a post: it is never public and never published, so no public query can return it. Its schema ships in
/// Sql/seed/seed-015-collaboration.nq. The conversation under it is the same shared conversation posts use (see <see cref="IConversation"/>).
/// </summary>
public sealed class CollaborationItem(EntitySchema schema) : DynamicEntity(schema);

/// <summary>Where a collaboration item lives in the store.</summary>
public static class CollaborationConstants
{
    public const string SeedName = "015-collaboration";

    /// <summary>Seed 017 adds the Research kind fields: Status, Sources and Findings.</summary>
    public const string ResearchSeedName = "017-research";
    public const string DefaultGraph = "https://global-webnet.com/graph/collaboration";
    public const string SchemaIri = "https://schema.global-webnet.com/collaboration/CollaborationItem";
    public const string BaseIri = "https://global-webnet.com/id/collaboration/";
    public const string TypeIri = "https://global-webnet.com/ontology/collaboration#CollaborationItem";

    // The conversation under an item (and under a post): the Comment entity. Its schema keeps the content-model IRIs it shipped with.
    public const string CommentSchemaIri = "https://schema.global-webnet.com/content/Comment";
    public const string CommentBaseIri = "https://global-webnet.com/id/comment/";
    public const string CommentTypeIri = "https://global-webnet.com/ontology/content#Comment";

    /// <summary>Seed 014 adds the Comment schema: the conversation under a post.</summary>
    public const string CommentsSeedName = "014-comments";
}

/// <summary>The kinds of collaboration item; a kind selects the review rules (a Developer item is approved, a Research item is not).</summary>
public static class CollaborationKinds
{
    public const string Developer = "Developer";
    public const string Research = "Research";

    public static readonly IReadOnlyList<string> All = [Developer, Research];
}

/// <summary>The states of a Research item: it is open, its findings were accepted into a post, or it was closed without. A Developer item uses the review states instead.</summary>
public static class ResearchStatus
{
    public const string Open = "Open";
    public const string Accepted = "Accepted into a post";
    public const string Closed = "Closed";

    public static readonly IReadOnlyList<string> All = [Open, Accepted, Closed];
}

/// <summary>Business layer of collaboration items: the generic rules plus the review state, which only the server changes.</summary>
public interface ICollaborationBll : IEntityBll<CollaborationItem>
{
    /// <summary>
    /// Sets the review state of a Developer item to one of <see cref="PostApproval.All"/>; an approval records who and when, any other state
    /// clears both. False when the item does not exist; refused for a Research item (it has no approval) or an unknown state.
    /// </summary>
    Task<bool> SetApprovalAsync(string itemId, string status, string? approvedBy, CancellationToken cancellationToken = default);

    /// <summary>Sets the state of a Research item to one of <see cref="ResearchStatus.All"/>. False when the item does not exist; refused for a Developer item (it is reviewed) or an unknown state.</summary>
    Task<bool> SetStatusAsync(string itemId, string status, CancellationToken cancellationToken = default);
}

/// <summary>The collaboration rules: a new item has a kind (Developer unless said otherwise), a creation date, and a Developer item starts as needing review.</summary>
public sealed class CollaborationBll(IEntityDal<CollaborationItem> items, IPlugins? plugins = null) : EntityBll<CollaborationItem>(items), ICollaborationBll
{
    public override async Task<CollaborationItem> CreateAsync(CollaborationItem entity, CancellationToken cancellationToken = default)
    {
        var kind = entity.GetValue("Kind") as string;
        if (string.IsNullOrWhiteSpace(kind))
        {
            kind = CollaborationKinds.Developer;
            entity.Set("Kind", Guid.NewGuid().ToString(), kind);
        }
        else if (!CollaborationKinds.All.Contains(kind, StringComparer.Ordinal))
        {
            throw new BusinessRuleException($"The kind must be one of: {string.Join(", ", CollaborationKinds.All)}.");
        }

        if (plugins is not null && CollaborationPlugins.ForKind(kind) is { } pluginKey && !await plugins.IsEnabledAsync(pluginKey, cancellationToken).ConfigureAwait(false))
        {
            var title = CollaborationPlugins.Definitions.First(p => p.Key == pluginKey).Title;
            throw new BusinessRuleException($"The {title} plug-in is switched off; switch it on in Admin > Plugins to add items of this kind.");
        }

        if (entity.GetValue("DateCreated") is null)
        {
            entity.Set("DateCreated", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        }

        if (kind == CollaborationKinds.Developer && string.IsNullOrEmpty(entity.GetValue("Approval") as string))
        {
            entity.Set("Approval", Guid.NewGuid().ToString(), PostApproval.NeedsReview);
        }
        else if (kind == CollaborationKinds.Research && string.IsNullOrEmpty(entity.GetValue("Status") as string))
        {
            entity.Set("Status", Guid.NewGuid().ToString(), ResearchStatus.Open);
        }

        return await base.CreateAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SetStatusAsync(string itemId, string status, CancellationToken cancellationToken = default)
    {
        if (!ResearchStatus.All.Contains(status, StringComparer.Ordinal))
        {
            throw new BusinessRuleException($"The status must be one of: {string.Join(", ", ResearchStatus.All)}.");
        }

        var item = await GetAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return false;
        }

        if (!string.Equals(item.GetValue("Kind") as string, CollaborationKinds.Research, StringComparison.Ordinal))
        {
            throw new BusinessRuleException("Only a Research item has a status; a Developer item is reviewed.");
        }

        item.Set("Status", Guid.NewGuid().ToString(), status);
        await Dal.UpdateAsync(item, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> SetApprovalAsync(string itemId, string status, string? approvedBy, CancellationToken cancellationToken = default)
    {
        if (!PostApproval.All.Contains(status, StringComparer.Ordinal))
        {
            throw new BusinessRuleException($"The review state must be one of: {string.Join(", ", PostApproval.All)}.");
        }

        var item = await GetAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return false;
        }

        if (!string.Equals(item.GetValue("Kind") as string, CollaborationKinds.Developer, StringComparison.Ordinal))
        {
            throw new BusinessRuleException("Only a Developer item is reviewed; a Research item has no approval.");
        }

        item.Set("Approval", Guid.NewGuid().ToString(), status);
        if (status == PostApproval.Approved)
        {
            item.Set("ApprovedBy", Guid.NewGuid().ToString(), approvedBy ?? string.Empty);
            item.Set("ApprovedAt", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        }
        else
        {
            item.Clear("ApprovedBy");
            item.Clear("ApprovedAt");
        }

        await Dal.UpdateAsync(item, cancellationToken).ConfigureAwait(false);
        return true;
    }
}

/// <summary>The presenter of collaboration items: the review state is shown but never accepted from a form (the server sets it).</summary>
public sealed class CollaborationPresenter(ICollaborationBll bll, EntitySchemaOf<CollaborationItem> schema, IEntityRelationProvider? relations = null)
    : EntityPresenter<CollaborationItem>(bll, schema.Schema, s => new CollaborationItem(s), relations)
{
    private static readonly HashSet<string> Managed = new(["Approval", "ApprovedBy", "ApprovedAt", "DateCreated", "Status"], StringComparer.OrdinalIgnoreCase);

    protected override IReadOnlySet<string> ServerManagedFields => Managed;

    protected override IReadOnlySet<string> ReadOnlyFields => Managed;

    /// <summary>Newest first, so the items waiting for review are at the top.</summary>
    protected override (string Field, bool Descending)? DefaultSort => ("DateCreated", true);
}

/// <summary>One collaboration item as the authoring surface reports it.</summary>
public sealed record CollaborationSummary(
    string Id, string Title, string Kind, string? Approval, string? Branch, string? Repos, string? DateCreated, string? Status = null);

/// <summary>What an author supplies for a collaboration item.</summary>
public sealed record CollaborationDraft(
    string? Title = null, string? Content = null, string? Kind = null, string? Branch = null, string? Repos = null, string? Sources = null, string? Findings = null);

/// <summary>One collaboration item with its text; a body longer than the cap is cut and <see cref="Truncated"/> says so.</summary>
public sealed record CollaborationDetail(CollaborationSummary Summary, string? Content, string? Sources, string? Findings, bool Truncated);

/// <summary>
/// The way an AI over MCP writes collaboration items: create one, revise its own, list its own, read and add to the conversation.
/// There is no route to approve (the human does that in the admin), to change an item written by someone else, or to touch a post.
/// </summary>
public interface ICollaborationAuthoring
{
    Task<CollaborationSummary> CreateAsync(string authorUserName, CollaborationDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Revises an item the author owns (a revised Developer item goes back to needing review); null when no such item.</summary>
    Task<CollaborationSummary?> UpdateAsync(string authorUserName, string itemId, CollaborationDraft draft, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CollaborationSummary>> ListMineAsync(string authorUserName, CancellationToken cancellationToken = default);

    /// <summary>Reads the text of an item the author owns (size-capped); null when no such item.</summary>
    Task<CollaborationDetail?> GetAsync(string authorUserName, string itemId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConversationEntry>> ListCommentsAsync(string authorUserName, string itemId, CancellationToken cancellationToken = default);

    Task<ConversationEntry> AddCommentAsync(string authorUserName, string itemId, string text, CancellationToken cancellationToken = default);
}

/// <summary>Implementation of <see cref="ICollaborationAuthoring"/> over the collaboration rules and the shared conversation.</summary>
public sealed class CollaborationAuthoring(
    ICollaborationBll items, EntitySchemaOf<CollaborationItem> schema, IUserPresenter users, IConversation conversation) : ICollaborationAuthoring
{
    public async Task<CollaborationSummary> CreateAsync(string authorUserName, CollaborationDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (string.IsNullOrWhiteSpace(draft.Title))
        {
            throw new BusinessRuleException("A title is required.");
        }

        var authorId = await AuthorIdAsync(authorUserName, cancellationToken).ConfigureAwait(false);
        var item = new CollaborationItem(schema.Schema);
        item.Set("Id", Guid.NewGuid().ToString(), Guid.NewGuid());
        item.Set("Title", Guid.NewGuid().ToString(), draft.Title.Trim());
        item.Set("Author", Guid.NewGuid().ToString(), authorId);
        Apply(item, draft);
        return Summarise(await items.CreateAsync(item, cancellationToken).ConfigureAwait(false));
    }

    public async Task<CollaborationSummary?> UpdateAsync(string authorUserName, string itemId, CollaborationDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var authorId = await AuthorIdAsync(authorUserName, cancellationToken).ConfigureAwait(false);
        var item = await items.GetAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return null;
        }

        EnsureOwn(item, authorId);
        if (!string.IsNullOrWhiteSpace(draft.Title))
        {
            item.Set("Title", Guid.NewGuid().ToString(), draft.Title.Trim());
        }

        // the kind is fixed when the item is created
        Apply(item, draft with { Kind = null });
        if (string.Equals(item.GetValue("Kind") as string, CollaborationKinds.Developer, StringComparison.Ordinal))
        {
            // the author changed it: an earlier approval or request for changes no longer describes it
            item.Set("Approval", Guid.NewGuid().ToString(), PostApproval.NeedsReview);
            item.Clear("ApprovedBy");
            item.Clear("ApprovedAt");
        }

        await items.UpdateAsync(item, cancellationToken).ConfigureAwait(false);
        return Summarise(item);
    }

    public async Task<IReadOnlyList<CollaborationSummary>> ListMineAsync(string authorUserName, CancellationToken cancellationToken = default)
    {
        var authorId = await AuthorIdAsync(authorUserName, cancellationToken).ConfigureAwait(false);
        return (await items.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(i => string.Equals(i.GetValue("Author") as string, authorId, StringComparison.Ordinal))
            .Select(Summarise)
            .OrderByDescending(s => s.DateCreated, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The most characters of any one text field a read returns.</summary>
    public const int MaxBodyCharacters = 100_000;

    public async Task<CollaborationDetail?> GetAsync(string authorUserName, string itemId, CancellationToken cancellationToken = default)
    {
        var authorId = await AuthorIdAsync(authorUserName, cancellationToken).ConfigureAwait(false);
        var item = await items.GetAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return null;
        }

        EnsureOwn(item, authorId);
        var truncated = false;
        string? Text(string field)
        {
            var value = item.GetValue(field) as string;
            if (value is { } v && v.Length > MaxBodyCharacters)
            {
                truncated = true;
                return v[..MaxBodyCharacters];
            }

            return value;
        }

        var (content, sources, findings) = (Text("Content"), Text("Sources"), Text("Findings"));
        return new CollaborationDetail(Summarise(item), content, sources, findings, truncated);
    }

    public async Task<IReadOnlyList<ConversationEntry>> ListCommentsAsync(string authorUserName, string itemId, CancellationToken cancellationToken = default)
    {
        await EnsureOwnAsync(authorUserName, itemId, cancellationToken).ConfigureAwait(false);
        return await conversation.ListAsync(itemId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConversationEntry> AddCommentAsync(string authorUserName, string itemId, string text, CancellationToken cancellationToken = default)
    {
        await EnsureOwnAsync(authorUserName, itemId, cancellationToken).ConfigureAwait(false);
        return await conversation.AddAsync(itemId, authorUserName, CommentKinds.Comment, text, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureOwnAsync(string authorUserName, string itemId, CancellationToken cancellationToken)
    {
        var authorId = await AuthorIdAsync(authorUserName, cancellationToken).ConfigureAwait(false);
        var item = await items.GetAsync(itemId, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException("The collaboration item does not exist.");
        EnsureOwn(item, authorId);
    }

    private static void EnsureOwn(CollaborationItem item, string authorId)
    {
        if (!string.Equals(item.GetValue("Author") as string, authorId, StringComparison.Ordinal))
        {
            throw new BusinessRuleException("This item was written by someone else; it cannot be used here.");
        }
    }

    private static void Apply(CollaborationItem item, CollaborationDraft draft)
    {
        void SetIf(string field, string? value)
        {
            if (value is not null)
            {
                item.Set(field, Guid.NewGuid().ToString(), value);
            }
        }

        SetIf("Content", draft.Content);
        SetIf("Kind", draft.Kind);
        SetIf("Branch", draft.Branch);
        SetIf("Repos", draft.Repos);
        SetIf("Sources", draft.Sources);
        SetIf("Findings", draft.Findings);
    }

    private async Task<string> AuthorIdAsync(string authorUserName, CancellationToken cancellationToken)
    {
        var form = await users.GetFormByUserNameAsync(authorUserName, cancellationToken).ConfigureAwait(false);
        return form?.Entity.EntityId ?? throw new BusinessRuleException($"The author account '{authorUserName}' does not exist.");
    }

    private static CollaborationSummary Summarise(CollaborationItem item) => new(
        item.EntityId?.Value ?? string.Empty,
        item.GetValue("Title") as string ?? string.Empty,
        item.GetValue("Kind") as string ?? CollaborationKinds.Developer,
        item.GetValue("Approval") as string,
        item.GetValue("Branch") as string,
        item.GetValue("Repos") as string,
        item.GetValue("DateCreated") is DateTimeOffset created ? created.ToString("O") : null,
        item.GetValue("Status") as string);
}
