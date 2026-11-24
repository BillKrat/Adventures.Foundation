using Adventures.Blog;
using Adventures.Common.Exceptions;
using Adventures.Collaboration;
using Adventures.Entities;
using Adventures.Security;
using Adventures.WebApi;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.Blog.WebApi;

/// <summary>One post or page as the dashboard needs it: no content, just what the counts and the draft lists read.</summary>
public sealed record ContentRow(string Id, string Title, bool IsPublished, bool IsRevision);

public sealed record DashboardItem(string Id, string Title);

/// <summary>A recent visitor comment for the dashboard: who, on which post, its state and a short excerpt. No e-mail address leaves with it.</summary>
public sealed record DashboardComment(string Id, string Author, string PostTitle, string Status, string Created, string Excerpt);

/// <summary>What the admin Dashboard shows (the legacy BlogEngine dashboard, trimmed for the beta): counts, the drafts, and what waits for a person.</summary>
public sealed record DashboardModel(
    int PublishedPosts,
    int DraftPosts,
    int PublishedPages,
    int DraftPages,
    int Exceptions,
    int CollaborationWaiting,
    int CommentsPending,
    IReadOnlyList<DashboardItem> DraftPostList,
    IReadOnlyList<DashboardItem> DraftPageList,
    IReadOnlyList<DashboardComment> LatestComments);

/// <summary>Builds the dashboard numbers from plain rows, so the rules (a proposed revision is not a post; drafts are capped) are tested without a store.</summary>
public static class DashboardSummary
{
    public const int ListLimit = 8;
    public const int LatestCommentsLimit = 5;
    public const int ExcerptLength = 140;

    public static DashboardModel Build(
        IEnumerable<ContentRow> posts, IEnumerable<ContentRow> pages, int exceptions, int collaborationWaiting, int commentsPending,
        IEnumerable<ModerationComment>? latestComments = null)
    {
        var realPosts = posts.Where(post => !post.IsRevision).ToList();
        var pageList = pages.ToList();
        return new DashboardModel(
            realPosts.Count(post => post.IsPublished),
            realPosts.Count(post => !post.IsPublished),
            pageList.Count(page => page.IsPublished),
            pageList.Count(page => !page.IsPublished),
            exceptions,
            collaborationWaiting,
            commentsPending,
            Drafts(realPosts),
            Drafts(pageList),
            Latest(latestComments ?? []));
    }

    /// <summary>
    /// The dashboard as one person may see it (beta readiness: admin access alone, a beta tester, must not read draft titles, reader comments or the exception count).
    /// Each figure and list needs the right to see what it counts; a figure the person may not see is zero and a list is empty.
    /// </summary>
    public static DashboardModel Restrict(DashboardModel model, IReadOnlySet<string> rights)
    {
        bool Has(string right) => rights.Contains(Rights.All) || rights.Contains(right);
        var posts = Has(Rights.PostsRead);
        var pages = Has(Rights.PagesRead);
        var comments = Has(Rights.CommentsModerate);
        return model with
        {
            PublishedPosts = posts ? model.PublishedPosts : 0,
            DraftPosts = posts ? model.DraftPosts : 0,
            PublishedPages = pages ? model.PublishedPages : 0,
            DraftPages = pages ? model.DraftPages : 0,
            Exceptions = Has(Rights.DiagnosticsRead) ? model.Exceptions : 0,
            CollaborationWaiting = Has(Rights.CollaborationRead) ? model.CollaborationWaiting : 0,
            CommentsPending = comments ? model.CommentsPending : 0,
            DraftPostList = posts ? model.DraftPostList : [],
            DraftPageList = pages ? model.DraftPageList : [],
            LatestComments = comments ? model.LatestComments : [],
        };
    }

    /// <summary>The first few comments (the caller passes them newest first), each shortened to an excerpt.</summary>
    public static IReadOnlyList<DashboardComment> Latest(IEnumerable<ModerationComment> comments) =>
        comments.Take(LatestCommentsLimit).Select(c => new DashboardComment(c.Id, c.Author, c.PostTitle, c.Status, c.Created, Excerpt(c.Body))).ToList();

    private static string Excerpt(string body)
    {
        var text = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= ExcerptLength ? text : text[..ExcerptLength].TrimEnd() + "…";
    }

    private static IReadOnlyList<DashboardItem> Drafts(IEnumerable<ContentRow> rows) =>
        rows.Where(row => !row.IsPublished).Take(ListLimit).Select(row => new DashboardItem(row.Id, row.Title)).ToList();
}

/// <summary>The admin Dashboard: one read-only call. Administrators only.</summary>
[ApiController]
[Route("api/admin/dashboard")]
[RequireRight(Rights.AdminAccess)]
public sealed class DashboardController(
    IEntityBll<Post> posts, IPageBll pages, AdminCounts counts, IVisitorComments comments, IRightsAuthorizer authorizer) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardModel>> Get(CancellationToken cancellationToken)
    {
        var postRows = (await posts.ListAsync(cancellationToken)).Select(ToRow);
        var pageRows = (await pages.ListAsync(cancellationToken)).Select(ToRow);
        var rights = await authorizer.RightsOfAsync(User, cancellationToken);
        return Ok(DashboardSummary.Restrict(DashboardSummary.Build(
            postRows,
            pageRows,
            await counts.GetAsync("exceptions", cancellationToken),
            await counts.GetAsync("collaboration", cancellationToken),
            commentsPending: await counts.GetAsync("comments", cancellationToken),
            latestComments: await comments.ListAsync(null, cancellationToken)), rights));
    }

    private static ContentRow ToRow(DynamicEntity entity) => new(
        entity.EntityId?.Value ?? string.Empty,
        entity.GetValue("Title") as string ?? string.Empty,
        entity.GetValue("IsPublished") is true,
        !string.IsNullOrEmpty(entity.GetValue("RevisionOf") as string));
}

public sealed record QuickDraftRequest(string? Title, string? Content);

/// <summary>
/// The Dashboard's Quick draft: a title and some text become a DRAFT post of the signed-in person (Markdown, like the authoring surface). It cannot
/// publish; the person opens the draft in the editor to finish it. Needs the right to write a post.
/// </summary>
[ApiController]
[Route("api/admin/dashboard/quick-draft")]
[RequireRight(Rights.PostsCreate)]
public sealed class QuickDraftController(IContentAuthoring authoring) : ApiControllerBase
{
    public const int MaxTitle = 200;
    public const int MaxContent = 20000;

    [HttpPost]
    public Task<IActionResult> Create([FromBody] QuickDraftRequest request, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            var author = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(author))
            {
                return Forbid();
            }

            var title = request.Title?.Trim() ?? string.Empty;
            if (title.Length is 0 or > MaxTitle)
            {
                throw new BusinessRuleException($"The title must be between 1 and {MaxTitle} characters.");
            }

            var content = request.Content?.Trim() ?? string.Empty;
            if (content.Length > MaxContent)
            {
                throw new BusinessRuleException($"The text can be at most {MaxContent} characters; open the editor for a longer post.");
            }

            return StatusCode(StatusCodes.Status201Created, await authoring.CreateDraftAsync(author, new DraftPost(title, content), cancellationToken));
        });
}
