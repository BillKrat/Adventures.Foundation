using Adventures.Common.Exceptions;

namespace Adventures.Entities;

/// <summary>One named capability: what it is called in code and on the Roles screen, and what it allows.</summary>
public sealed record Right(string Name, string Description);

/// <summary>
/// The rights of the application: the capabilities code checks (never role names). A role is data, a named bundle of these (the
/// <c>Rights</c> field of a role, comma separated; <c>*</c> means every right and belongs to the Admin role). Names are stable: they are
/// stored in role data and used by policies and menu entries. An <c>.any</c> right implies its <c>.own</c> twin (see <see cref="Expand"/>).
/// </summary>
public static class Rights
{
    public const string All = "*";

    public const string AdminAccess = "admin.access";
    public const string PostsRead = "posts.read";
    public const string PostsCreate = "posts.create";
    public const string PostsEditOwn = "posts.edit.own";
    public const string PostsEditAny = "posts.edit.any";
    public const string PostsDeleteOwn = "posts.delete.own";
    public const string PostsDeleteAny = "posts.delete.any";
    public const string PostsPublish = "posts.publish";
    public const string PagesRead = "pages.read";
    public const string PagesEdit = "pages.edit";
    public const string PagesPublish = "pages.publish";
    public const string TaxonomyManage = "taxonomy.manage";
    public const string MediaUpload = "media.upload";
    public const string CollaborationRead = "collaboration.read";
    public const string CollaborationWrite = "collaboration.write";
    public const string CollaborationReview = "collaboration.review";
    public const string DecisionsRead = "decisions.read";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string PluginsManage = "plugins.manage";
    public const string SettingsManage = "settings.manage";
    public const string DiagnosticsRead = "diagnostics.read";
    public const string DiagnosticsAi = "diagnostics.ai";

    public static readonly IReadOnlyList<Right> Catalog =
    [
        new(AdminAccess, "See the admin area at all."),
        new(PostsRead, "See the list of posts and open them."),
        new(PostsCreate, "Write a new post."),
        new(PostsEditOwn, "Change a post you wrote."),
        new(PostsEditAny, "Change any post (includes your own)."),
        new(PostsDeleteOwn, "Delete a post you wrote."),
        new(PostsDeleteAny, "Delete any post (includes your own)."),
        new(PostsPublish, "Publish and unpublish posts."),
        new(PagesRead, "See the list of pages and open them."),
        new(PagesEdit, "Create, change and delete pages."),
        new(PagesPublish, "Publish and unpublish pages."),
        new(TaxonomyManage, "Manage categories and tags."),
        new(MediaUpload, "Upload images."),
        new(CollaborationRead, "Read collaboration items and their conversations."),
        new(CollaborationWrite, "Create and change your own collaboration items; comment."),
        new(CollaborationReview, "Approve, request changes, record decisions and accept research."),
        new(DecisionsRead, "See the Decisions page."),
        new(UsersManage, "Add people, set passwords, assign roles."),
        new(RolesManage, "Change roles and what they allow."),
        new(PluginsManage, "Switch plug-ins on and off."),
        new(SettingsManage, "Change the site settings."),
        new(DiagnosticsRead, "See the exception log and request diagnostics."),
        new(DiagnosticsAi, "Use Ask Claude on an exception (it costs money and sends a scrubbed report out)."),
    ];

    private static readonly HashSet<string> Known = Catalog.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>The name of every right.</summary>
    public static IReadOnlySet<string> AllNames => Known;

    public static bool IsKnown(string name) => Known.Contains(name);

    /// <summary>Reads the stored text of a role (comma separated names, or <c>*</c>) into a set; unknown names are refused.</summary>
    public static IReadOnlySet<string> Parse(string? stored)
    {
        var names = (stored ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Contains(All, StringComparer.Ordinal))
        {
            return Known;
        }

        var unknown = names.Where(name => !Known.Contains(name)).ToList();
        if (unknown.Count > 0)
        {
            throw new BusinessRuleException($"Unknown right: {string.Join(", ", unknown)}.");
        }

        return Expand(names);
    }

    /// <summary>Adds what a right implies: an <c>.any</c> right carries its <c>.own</c> twin.</summary>
    public static IReadOnlySet<string> Expand(IEnumerable<string> names)
    {
        var set = names.ToHashSet(StringComparer.Ordinal);
        if (set.Contains(PostsEditAny))
        {
            set.Add(PostsEditOwn);
        }

        if (set.Contains(PostsDeleteAny))
        {
            set.Add(PostsDeleteOwn);
        }

        return set;
    }
}
