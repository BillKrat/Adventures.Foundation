using Adventures.Entities;
using Adventures.Collaboration;

namespace Adventures.Blog;

/// <summary>What the migration did (or, in a dry run, would do) to one post.</summary>
public sealed record MigratedPost(string Id, string Title, string Approval, bool ItemExisted);

/// <summary>The result of <see cref="CollaborationMigration.RunAsync"/>.</summary>
public sealed record MigrationReport(bool Applied, IReadOnlyList<MigratedPost> Posts);

/// <summary>Moves Collaboration-category posts into collaboration items (see <see cref="CollaborationController.Migrate"/>).</summary>
public sealed class CollaborationMigration(
    IPostBll posts, IEntityBll<Category> categories, ICollaborationBll items, EntitySchemaOf<CollaborationItem> schema)
{
    private static readonly string[] CopiedValues = ["Title", "Content", "Author", "Approval", "ApprovedBy", "ApprovedAt"];

    public async Task<MigrationReport> RunAsync(bool apply, CancellationToken cancellationToken)
    {
        var category = (await categories.ListAsync(cancellationToken))
            .FirstOrDefault(c => string.Equals(c.GetValue("Name") as string, PostApproval.CollaborationCategory, StringComparison.OrdinalIgnoreCase));
        var moved = new List<MigratedPost>();
        if (category?.EntityId is null)
        {
            return new MigrationReport(apply, moved);
        }

        var categoryId = category.EntityId.Value;
        foreach (var post in await posts.ListAsync(cancellationToken))
        {
            if (post.EntityId is null
                || !post.GetValues("Categories").Any(v => string.Equals(v?.ToString(), categoryId, StringComparison.Ordinal))
                || !string.IsNullOrEmpty(post.GetValue("RevisionOf") as string)
                || post.GetValue("IsPublished") is true)
            {
                continue;
            }

            var id = post.EntityId.Value;
            var existed = await items.GetAsync(id, cancellationToken) is not null;
            moved.Add(new MigratedPost(id, post.GetValue("Title") as string ?? string.Empty, post.GetValue("Approval") as string ?? string.Empty, existed));
            if (!apply)
            {
                continue;
            }

            if (!existed)
            {
                await items.CreateAsync(ToItem(post, id), cancellationToken);
            }

            await posts.DeleteAsync(id, cancellationToken);
        }

        return new MigrationReport(apply, moved);
    }

    private CollaborationItem ToItem(Post post, string id)
    {
        var item = new CollaborationItem(schema.Schema);
        item.Set("Id", Guid.NewGuid().ToString(), Guid.Parse(id));
        item.Set("Kind", Guid.NewGuid().ToString(), CollaborationKinds.Developer);
        foreach (var field in CopiedValues)
        {
            if (post.GetValue(field) is { } value && !(value is string text && text.Length == 0))
            {
                item.Set(field, Guid.NewGuid().ToString(), value);
            }
        }

        // a draft post is dated by when it was written
        item.Set("DateCreated", Guid.NewGuid().ToString(), post.GetValue("DatePublished") ?? DateTimeOffset.UtcNow);
        if (string.IsNullOrEmpty(item.GetValue("Approval") as string))
        {
            item.Set("Approval", Guid.NewGuid().ToString(), PostApproval.NeedsReview);
        }

        return item;
    }
}
