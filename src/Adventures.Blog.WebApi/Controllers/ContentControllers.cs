// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
using Adventures.Security;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Adventures.Blog;
using Adventures.Collaboration;

namespace Adventures.Blog.WebApi;

/// <summary>Pages for administrators (the six standard admin actions); the page rules live in <see cref="IPageBll"/>.</summary>
[ApiController]
[Route("api/pages")]
[AdminRights(Read = Rights.PagesRead, Create = Rights.PagesEdit, Update = Rights.PagesEdit, Delete = Rights.PagesEdit)]
public sealed class PagesController(IEntityPresenter<Page> presenter) : AdminEntityControllerBase<Page>(presenter)
{
    /// <summary>The id of the page with this slug, so the public reader can offer Edit and Delete to a signed-in administrator (the public API carries no ids).</summary>
    [HttpGet("by-slug/{slug}")]
    public async Task<IActionResult> BySlug(string slug, CancellationToken cancellationToken) =>
        await SlugLookup.IdOfAsync(presenter, slug, cancellationToken) is { } id ? Ok(new { id }) : NotFound();
}

/// <summary>Categories for administrators.</summary>
[ApiController]
[Route("api/categories")]
[AdminRights(Read = Rights.PostsRead, Create = Rights.TaxonomyManage, Update = Rights.TaxonomyManage, Delete = Rights.TaxonomyManage)]
public sealed class CategoriesController(IEntityPresenter<Category> presenter) : AdminEntityControllerBase<Category>(presenter);

/// <summary>Tags for administrators.</summary>
[ApiController]
[Route("api/tags")]
[AdminRights(Read = Rights.PostsRead, Create = Rights.TaxonomyManage, Update = Rights.TaxonomyManage, Delete = Rights.TaxonomyManage)]
public sealed class TagsController(IEntityPresenter<Tag> presenter) : AdminEntityControllerBase<Tag>(presenter);

/// <summary>
/// Posts for administrators: the six standard actions plus <c>publish</c> and <c>unpublish</c>. The publish state is not a form
/// field (the presenter refuses it): it changes only here, so who may publish is decided in one place. Every form lists the
/// action that applies to the post current state.
/// </summary>
[ApiController]
[Route("api/posts")]
[AdminRights(Read = Rights.PostsRead, Create = Rights.PostsCreate, Update = Rights.PostsEditAny, Delete = Rights.PostsDeleteAny)]
public sealed class PostsController(IEntityPresenter<Post> presenter, IPostBll posts, IUserPresenter users) : AdminEntityControllerBase<Post>(presenter)
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

    protected override EntityFormModel Decorate(EntityFormModel form, string collectionPath)
    {
        var decorated = base.Decorate(form, collectionPath);
        if (string.IsNullOrEmpty(form.Entity.EntityId))
        {
            return decorated;
        }

        var link = $"{collectionPath}/{Uri.EscapeDataString(form.Entity.EntityId)}";
        var published = form.Entity.Values.TryGetValue("IsPublished", out var state) && string.Equals(state, "true", StringComparison.OrdinalIgnoreCase);
        if (form.Entity.Values.TryGetValue("RevisionOf", out var revises) && !string.IsNullOrEmpty(revises))
        {
            // a proposed revision of a published post: the actions are to apply it to the original or throw it away, never to publish it
            return decorated with
            {
                Actions =
                [
                    new EntityAction("apply-revision", "Apply revision", $"{link}/apply-revision", "Replace the published post with this revision? The published address and date stay the same."),
                    new EntityAction("discard-revision", "Discard revision", $"{link}/discard-revision", "Throw this revision away?"),
                ],
            };
        }

        var actions = new List<EntityAction>
        {
            published
                ? new EntityAction("unpublish", "Unpublish", $"{link}/unpublish", "Take this post back to a draft?")
                : new EntityAction("publish", "Publish", $"{link}/publish", "Make this post public?"),
        };

        // a post under review (the collaboration channel) can be approved, or sent back with a request for changes; both are recorded
        var approval = form.Entity.Values.GetValueOrDefault("Approval");
        if (!string.IsNullOrEmpty(approval))
        {
            if (approval != PostApproval.Approved)
            {
                actions.Add(new EntityAction("approve", "Approve", $"{link}/approve", null));
            }

            if (approval != PostApproval.ChangesRequested)
            {
                actions.Add(new EntityAction("request-changes", "Request changes", $"{link}/request-changes", null));
            }
        }

        return decorated with { Actions = actions };
    }

    /// <summary>Records the approval of a post under review: who, and when.</summary>
    [RequireRight(Rights.CollaborationReview)]
    [HttpPost("{id}/approve")]
    public Task<IActionResult> Approve(string id, CancellationToken cancellationToken) =>
        GuardedAsync(async () => await posts.SetApprovalAsync(id, PostApproval.Approved, User.Identity?.Name, cancellationToken) ? (IActionResult)NoContent() : NotFound());

    /// <summary>Sends a post under review back to its author with a request for changes (the questions go in chat); clears any earlier approval.</summary>
    [RequireRight(Rights.CollaborationReview)]
    [HttpPost("{id}/request-changes")]
    public Task<IActionResult> RequestChanges(string id, CancellationToken cancellationToken) =>
        GuardedAsync(async () => await posts.SetApprovalAsync(id, PostApproval.ChangesRequested, User.Identity?.Name, cancellationToken) ? (IActionResult)NoContent() : NotFound());

    /// <summary>The id of the post with this slug (see <see cref="PagesController.BySlug"/>).</summary>
    [HttpGet("by-slug/{slug}")]
    public async Task<IActionResult> BySlug(string slug, CancellationToken cancellationToken) =>
        await SlugLookup.IdOfAsync(presenter, slug, cancellationToken) is { } id ? Ok(new { id }) : NotFound();

    [RequireRight(Rights.PostsEditAny)]
    [HttpPost("{id}/apply-revision")]
    public Task<IActionResult> ApplyRevision(string id, CancellationToken cancellationToken) =>
        GuardedAsync(async () => await posts.ApplyRevisionAsync(id, cancellationToken) ? (IActionResult)NoContent() : NotFound());

    [RequireRight(Rights.PostsEditAny)]
    [HttpPost("{id}/discard-revision")]
    public Task<IActionResult> DiscardRevision(string id, CancellationToken cancellationToken) =>
        GuardedAsync(async () => await posts.DiscardRevisionAsync(id, cancellationToken) ? (IActionResult)NoContent() : NotFound());

    [RequireRight(Rights.PostsPublish)]
    [HttpPost("{id}/publish")]
    public async Task<IActionResult> Publish(string id, CancellationToken cancellationToken) =>
        await posts.PublishAsync(id, cancellationToken) ? NoContent() : NotFound();

    [RequireRight(Rights.PostsPublish)]
    [HttpPost("{id}/unpublish")]
    public async Task<IActionResult> Unpublish(string id, CancellationToken cancellationToken) =>
        await posts.UnpublishAsync(id, cancellationToken) ? NoContent() : NotFound();
}

/// <summary>Finds an entity id by its Slug field (case-insensitive) in an admin collection.</summary>
internal static class SlugLookup
{
    public static async Task<string?> IdOfAsync<TEntity>(IEntityPresenter<TEntity> presenter, string slug, CancellationToken cancellationToken)
        where TEntity : DynamicEntity =>
        (await presenter.ListAsync(cancellationToken)).FirstOrDefault(entity =>
            entity.Values.TryGetValue("Slug", out var value) && string.Equals(value, slug, StringComparison.OrdinalIgnoreCase))?.EntityId;
}
