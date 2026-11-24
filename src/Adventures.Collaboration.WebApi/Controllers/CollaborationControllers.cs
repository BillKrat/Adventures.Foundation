// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Common.Exceptions;
using Adventures.Entities;
using Adventures.Security;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Adventures.Collaboration;
using Adventures.MEF;

namespace Adventures.Collaboration.WebApi;

/// <summary>
/// Collaboration items for administrators: the six standard admin actions plus <c>approve</c> and <c>request-changes</c> on a Developer
/// item. The review state is not a form field (the presenter refuses it): it changes only here, so who may review is decided in one
/// place. A collaboration item is never public, so nothing here can publish it.
/// </summary>
[ApiController]
[Route("api/collaboration")]
[AdminRights(Read = Rights.CollaborationRead, Create = Rights.CollaborationWrite, Update = Rights.CollaborationWrite, Delete = Rights.CollaborationWrite)]
[RequiresPlugin(CollaborationPlugins.DeveloperCollaboration, CollaborationPlugins.ResearchCollaboration)]
public sealed class CollaborationController(IEntityPresenter<CollaborationItem> presenter, ICollaborationBll items, IUserPresenter users, ICollaborationMigration migration, IPlugins plugins)
    : AdminEntityControllerBase<CollaborationItem>(presenter)
{
    /// <summary>The signed-in user is the author unless the form names one.</summary>
    protected override async Task<EntityDataModel> PrepareForCreateAsync(EntityDataModel request, CancellationToken cancellationToken)
    {
        if (request.Values.TryGetValue("Author", out var author) && !string.IsNullOrEmpty(author))
        {
            return request;
        }

        var me = User.Identity?.Name is { Length: > 0 } name ? await users.GetFormByUserNameAsync(name, cancellationToken) : null;
        return me is null ? request : request with { Values = new Dictionary<string, string?>(request.Values) { ["Author"] = me.Entity.EntityId } };
    }

    private static readonly string[] Remembered = ["Kind", "Branch", "Repos"];

    /// <summary>
    /// What the form of every item shares: Kind is a choice of the known kinds, Kind, Branch and Repositories start from the value last used
    /// (the browser remembers it), and the Author comes first.
    /// </summary>
    private static EntityFormModel Shape(EntityFormModel form) =>
        form with
        {
            Schema = form.Schema with
            {
                Fields = form.Schema.Fields
                    .Select(field => field with
                    {
                        Choices = field.Name == "Kind" ? CollaborationKinds.All : field.Choices,
                        Remember = Remembered.Contains(field.Name),
                        Order = field.Name == "Author" ? -1 : field.Order,
                    })
                    .OrderBy(field => field.Order)
                    .ToList(),
            },
        };

    /// <summary>
    /// The kinds whose plug-in is switched off stay in the Kind dropdown but cannot be chosen (the browser shows them disabled), so the
    /// refusal comes when the person picks the kind, not after the whole form is filled in and saved.
    /// </summary>
    protected override async Task<EntityFormModel> DecorateAsync(EntityFormModel form, string collectionPath, CancellationToken cancellationToken)
    {
        var decorated = Decorate(form, collectionPath);
        var enabled = await plugins.EnabledKeysAsync(cancellationToken);
        var off = CollaborationKinds.All.Where(kind => CollaborationPlugins.ForKind(kind) is { } key && !enabled.Contains(key)).ToList();
        return off.Count == 0
            ? decorated
            : decorated with
            {
                Schema = decorated.Schema with
                {
                    Fields = decorated.Schema.Fields.Select(field => field.Name == "Kind" ? field with { DisabledChoices = off } : field).ToList(),
                },
            };
    }

    /// <summary>The grid can be filtered by Kind (all kinds when no filter is chosen).</summary>
    protected override EntityGridModel DecorateGrid(EntityGridModel grid) =>
        grid with { Filters = [new GridFilter("Kind", "Kind", CollaborationKinds.All)] };

    private static readonly string[] DeveloperOnly = ["Branch", "Repos", "Approval", "ApprovedBy", "ApprovedAt"];
    private static readonly string[] ResearchOnly = ["Sources", "Findings", "Status"];

    /// <summary>
    /// The form of a saved item shows only the fields of its kind (a Developer item has no Sources, a Research item has no Branch), and offers
    /// the actions of its kind: Approve and Request changes for Developer, Accept into a post, Close and Reopen for Research. A new item
    /// (no kind yet) shows every field.
    /// </summary>
    protected override EntityFormModel Decorate(EntityFormModel form, string collectionPath)
    {
        var decorated = Shape(base.Decorate(form, collectionPath));
        var kind = form.Entity.Values.GetValueOrDefault("Kind");
        if (string.IsNullOrEmpty(form.Entity.EntityId) || string.IsNullOrEmpty(kind))
        {
            return decorated;
        }

        var hidden = kind == CollaborationKinds.Research ? DeveloperOnly : ResearchOnly;
        decorated = decorated with { Schema = decorated.Schema with { Fields = decorated.Schema.Fields.Where(field => !hidden.Contains(field.Name)).ToList() } };
        var link = $"{collectionPath}/{Uri.EscapeDataString(form.Entity.EntityId)}";
        var actions = new List<EntityAction>();
        if (kind == CollaborationKinds.Developer)
        {
            var approval = form.Entity.Values.GetValueOrDefault("Approval");
            if (approval == PostApproval.ChangesRequested)
            {
                // the change request is out: the next move is the reply, so the only action is to take the request back
                actions.Add(new EntityAction("withdraw-changes", "Cancel change request", $"{link}/withdraw-changes", null));
            }
            else
            {
                if (approval != PostApproval.Approved)
                {
                    actions.Add(new EntityAction("approve", "Approve", $"{link}/approve", null));
                }

                actions.Add(new EntityAction("request-changes", "Request changes", $"{link}/request-changes", null));
            }
        }
        else if (kind == CollaborationKinds.Research)
        {
            var status = form.Entity.Values.GetValueOrDefault("Status");
            if (status != ResearchStatus.Accepted)
            {
                actions.Add(new EntityAction("accept", "Accept into a post", $"{link}/accept", null));
            }

            if (status != ResearchStatus.Closed)
            {
                actions.Add(new EntityAction("close", "Close", $"{link}/close", null));
            }

            if (status != ResearchStatus.Open)
            {
                actions.Add(new EntityAction("reopen", "Reopen", $"{link}/reopen", null));
            }
        }

        return decorated with { Actions = actions };
    }

    /// <summary>Takes a change request back: the item returns to Needs review.</summary>
    [RequireRight(Rights.CollaborationWrite)]
    [HttpPost("{id}/withdraw-changes")]
    public Task<IActionResult> WithdrawChanges(string id, CancellationToken cancellationToken) =>
        GuardedAsync(async () => await items.SetApprovalAsync(id, PostApproval.NeedsReview, null, cancellationToken) ? (IActionResult)NoContent() : NotFound());

    [RequireRight(Rights.CollaborationReview)]
    [HttpPost("{id}/accept")]
    public Task<IActionResult> Accept(string id, CancellationToken cancellationToken) => SetStatusAsync(id, ResearchStatus.Accepted, cancellationToken);

    [RequireRight(Rights.CollaborationReview)]
    [HttpPost("{id}/close")]
    public Task<IActionResult> Close(string id, CancellationToken cancellationToken) => SetStatusAsync(id, ResearchStatus.Closed, cancellationToken);

    [RequireRight(Rights.CollaborationReview)]
    [HttpPost("{id}/reopen")]
    public Task<IActionResult> Reopen(string id, CancellationToken cancellationToken) => SetStatusAsync(id, ResearchStatus.Open, cancellationToken);

    private Task<IActionResult> SetStatusAsync(string id, string status, CancellationToken cancellationToken) =>
        GuardedAsync(async () => await items.SetStatusAsync(id, status, cancellationToken) ? (IActionResult)NoContent() : NotFound());
    [RequireRight(Rights.CollaborationReview)]
    [HttpPost("{id}/approve")]
    public Task<IActionResult> Approve(string id, CancellationToken cancellationToken) =>
        GuardedAsync(async () => await items.SetApprovalAsync(id, PostApproval.Approved, User.Identity?.Name, cancellationToken) ? (IActionResult)NoContent() : NotFound());

    [RequireRight(Rights.CollaborationReview)]
    [HttpPost("{id}/request-changes")]
    public Task<IActionResult> RequestChanges(string id, CancellationToken cancellationToken) =>
        GuardedAsync(async () => await items.SetApprovalAsync(id, PostApproval.ChangesRequested, User.Identity?.Name, cancellationToken) ? (IActionResult)NoContent() : NotFound());

    /// <summary>
    /// One-time move of the posts filed under the Collaboration category into collaboration items, keeping each id (so its conversation,
    /// decisions and review state carry over). A dry run unless <c>apply=true</c>; safe to run again (an item that already exists is not
    /// created twice, and a post is only taken out of the Posts list once its item exists).
    /// </summary>
    [RequireRight(Rights.SettingsManage)]
    [HttpPost("migrate")]
    public Task<IActionResult> Migrate([FromQuery] bool apply, CancellationToken cancellationToken) =>
        GuardedAsync(async () => (IActionResult)Ok(await migration.RunAsync(apply, cancellationToken)));
}

/// <summary>The collaboration authoring surface for Claude (scope <c>content.draft</c>), next to the posts one: create, revise and list its own items and use their conversation.</summary>
[ApiController]
[Route("api/author/collaboration")]
[Authorize(Policy = "Scope:" + RequiredScope)]
[RequiresPlugin(CollaborationPlugins.DeveloperCollaboration, CollaborationPlugins.ResearchCollaboration)]
public sealed class CollaborationAuthoringController(ICollaborationAuthoring authoring, IConfiguration configuration) : ApiControllerBase
{
    /// <summary>The scope a machine client needs; the same one the posts authoring surface uses (a test keeps the two equal).</summary>
    public const string RequiredScope = Adventures.Security.Scopes.ContentDraft;

    private string Author => configuration["Authoring:AuthorUserName"] ?? "Claude";

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CollaborationSummary>>> Mine(CancellationToken cancellationToken) =>
        Ok(await authoring.ListMineAsync(Author, cancellationToken));

    /// <summary>The text of one of the author's own items (summary, content, sources, findings), each field size-capped; another author's item is refused.</summary>
    [HttpGet("{id}")]
    public Task<IActionResult> Show(string id, CancellationToken cancellationToken) =>
        GuardedAsync(async () => await authoring.GetAsync(Author, id, cancellationToken) is { } detail ? Ok(detail) : NotFound());

    [HttpPost]
    public Task<IActionResult> Create([FromBody] CollaborationDraft draft, CancellationToken cancellationToken) =>
        GuardedAsync(async () => (IActionResult)StatusCode(StatusCodes.Status201Created, await authoring.CreateAsync(Author, draft, cancellationToken)));

    [HttpPut("{id}")]
    public Task<IActionResult> Update(string id, [FromBody] CollaborationDraft draft, CancellationToken cancellationToken) =>
        GuardedAsync(async () => await authoring.UpdateAsync(Author, id, draft, cancellationToken) is { } updated ? Ok(updated) : NotFound());

    [HttpGet("{id}/comments")]
    public Task<IActionResult> Comments(string id, CancellationToken cancellationToken) =>
        GuardedAsync(async () => (IActionResult)Ok(await authoring.ListCommentsAsync(Author, id, cancellationToken)));

    [HttpPost("{id}/comments")]
    public Task<IActionResult> AddComment(string id, [FromBody] NewConversationEntry entry, CancellationToken cancellationToken) =>
        GuardedAsync(async () => (IActionResult)StatusCode(StatusCodes.Status201Created, await authoring.AddCommentAsync(Author, id, entry.Text ?? string.Empty, cancellationToken)));
}
