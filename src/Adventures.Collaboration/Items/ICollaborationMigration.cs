namespace Adventures.Collaboration;

/// <summary>What the migration did (or, in a dry run, would do) to one post.</summary>
public sealed record MigratedPost(string Id, string Title, string Approval, bool ItemExisted);

/// <summary>The result of <see cref="ICollaborationMigration.RunAsync"/>.</summary>
public sealed record MigrationReport(bool Applied, IReadOnlyList<MigratedPost> Posts);

/// <summary>
/// Moves the posts that were once the collaboration channel into collaboration items. The collaboration controllers expose it without knowing where
/// the posts come from: the blog registers the implementation, so a host with no blog simply has nothing to migrate.
/// </summary>
public interface ICollaborationMigration
{
    Task<MigrationReport> RunAsync(bool apply, CancellationToken cancellationToken);
}
