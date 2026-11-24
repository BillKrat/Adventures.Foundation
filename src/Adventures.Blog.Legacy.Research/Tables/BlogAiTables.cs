// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Blog.Legacy.Research;

/// <summary>One column of a legacy table: its name, its N-Quad field type and whether the legacy column is NOT NULL.</summary>
/// <param name="Type">An N-Quad schema type name: Guid, String, Text, bool, int, double or DateTimeOffset.</param>
public sealed record BlogAiColumn(string Name, string Type, bool Required);

/// <summary>A legacy table that may be read row by row and that gets a schema in the legacy mirror layer.</summary>
/// <param name="Wave">The migration wave (see docs/Claude-decision-2026-10-legacy-table-migration-plan.md).</param>
/// <param name="KeyColumns">The columns rows are paged by and identified by: the legacy identity column, or the columns that together are unique when the table has none. Default: the first column.</param>
/// <param name="MaskNameColumn">With <paramref name="MaskValueColumn"/>: a row whose name column looks like a secret (password, key, token...) is returned with its value masked, by the server, before it leaves the database.</param>
public sealed record BlogAiTable(
    string Name, int Wave, IReadOnlyList<BlogAiColumn> Columns, IReadOnlyList<string>? KeyColumns = null, string? MaskNameColumn = null, string? MaskValueColumn = null)
{
    public IReadOnlyList<string> Key => KeyColumns ?? [Columns[0].Name];

    /// <summary>The first key column (the legacy identity column for the tables that have one).</summary>
    public string OrderBy => Key[0];

    /// <summary>The identity of one row: its key values joined, the input for a stable derived id.</summary>
    public string IdentityOf(IReadOnlyDictionary<string, object?> row) => string.Join("|", Key.Select(column => row[column]));
}

/// <summary>
/// The wave 1 legacy tables (Users, Posts, Pages), column for column as the live BlogEngine.NET 3.3.6 + fork database has them, minus the
/// secret and binary columns on purpose: <c>be_Users.Password</c> and the fork's <c>be_Users.Comment</c> are not listed, so neither the
/// row tool nor the generated schemas can carry them (the SELECT-only login has no grant on them either). This one list is the single
/// source for the paged <c>blogai_rows</c> tool and for the generated legacy schemas (<see cref="LegacySchemaSeed"/>), so the two
/// cannot drift apart; the column names are checked against the live database by <c>blogai_columns</c>.
/// </summary>
public static class BlogAiTables
{
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 500;

    /// <summary>The schema version stamped on every mirror schema: BlogEngine.NET release, then the fork's last schema upgrade script.</summary>
    public const string SchemaVersion = "blogengine-3.3.6+gwn-2026.09.26";

    // "Name:Type" with a trailing "!" when the legacy column is NOT NULL; the first column of a table is its identity column.
    private static BlogAiTable T(string name, int wave, params string[] columns) =>
        new(name, wave, columns.Select(c =>
        {
            var required = c.EndsWith('!');
            var parts = c.TrimEnd('!').Split(':');
            return new BlogAiColumn(parts[0], parts[1], required);
        }).ToList());

    /// <summary>Wave 1: users, posts, pages and their taxonomy. Its mirror schemas are seeds 009 and 010 (applied; never regenerated from a longer list).</summary>
    public static readonly IReadOnlyList<BlogAiTable> Wave1 =
    [
        T("be_Blogs", 1, "BlogRowId:int!", "BlogId:Guid!", "BlogName:String!", "Hostname:String!", "IsAnyTextBeforeHostnameAccepted:bool!",
            "StorageContainerName:String!", "VirtualPath:String!", "IsPrimary:bool!", "IsActive:bool!", "IsSiteAggregation:bool!"),
        T("be_Users", 1, "UserID:int!", "BlogID:Guid!", "UserName:String!", "LastLoginTime:DateTimeOffset", "EmailAddress:String"),
        T("be_Roles", 1, "RoleID:int!", "BlogID:Guid!", "Role:String!"),
        T("be_UserRoles", 1, "UserRoleID:int!", "BlogID:Guid!", "UserName:String!", "Role:String!"),
        T("be_Rights", 1, "RightRowId:int!", "BlogId:Guid!", "RightName:String!"),
        T("be_RightRoles", 1, "RightRoleRowId:int!", "BlogId:Guid!", "RightName:String!", "Role:String!"),
        T("be_Profiles", 1, "ProfileID:int!", "BlogID:Guid!", "UserName:String", "SettingName:String", "SettingValue:Text"),
        T("be_Posts", 1, "PostRowID:int!", "BlogID:Guid!", "PostID:Guid!", "Title:String", "Description:Text", "PostContent:Text", "DateCreated:DateTimeOffset",
            "DateModified:DateTimeOffset", "Author:String", "IsPublished:bool", "IsCommentEnabled:bool", "Raters:int", "Rating:double", "Slug:String", "IsDeleted:bool!"),
        T("be_Pages", 1, "PageRowID:int!", "BlogID:Guid!", "PageID:Guid!", "Title:String", "Description:Text", "PageContent:Text", "Keywords:Text",
            "DateCreated:DateTimeOffset", "DateModified:DateTimeOffset", "IsPublished:bool", "IsFrontPage:bool", "Parent:Guid", "ShowInList:bool", "Slug:String", "IsDeleted:bool!", "SortOrder:int!"),
        T("be_Categories", 1, "CategoryRowID:int!", "BlogID:Guid!", "CategoryID:Guid!", "CategoryName:String", "Description:String", "ParentID:Guid"),
        T("be_PostCategory", 1, "PostCategoryID:int!", "BlogID:Guid!", "PostID:Guid!", "CategoryID:Guid!"),
        T("be_PostTag", 1, "PostTagID:int!", "BlogID:Guid!", "PostID:Guid!", "Tag:String"),
    ];

    /// <summary>
    /// Wave 2: the blog settings and the custom fields. A setting whose name looks like a secret is returned masked (see <see cref="SecretNamePatterns"/>);
    /// be_CustomFields has no identity column, so its four key columns page it. Mirror schemas: seed 012.
    /// </summary>
    public static readonly IReadOnlyList<BlogAiTable> Wave2 =
    [
        T("be_Settings", 2, "SettingRowId:int!", "BlogId:Guid!", "SettingName:String!", "SettingValue:Text") with { MaskNameColumn = "SettingName", MaskValueColumn = "SettingValue" },
        T("be_CustomFields", 2, "CustomType:String!", "ObjectId:String!", "BlogId:Guid!", "Key:String!", "Value:Text!", "Attribute:String") with { KeyColumns = ["CustomType", "ObjectId", "BlogId", "Key"] },
    ];

    /// <summary>Every table the row tool may read.</summary>
    public static readonly IReadOnlyList<BlogAiTable> All = [.. Wave1, .. Wave2];

    /// <summary>A setting name containing one of these (any case) has its value masked by the server.</summary>
    public static readonly IReadOnlyList<string> SecretNamePatterns = ["password", "pwd", "secret", "token", "key", "connectionstring"];

    public static BlogAiTable? Find(string name) => All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// The paged SELECT for one table. Table and column names come only from <see cref="All"/>, never from the caller, so there is nothing
    /// to inject; the page position is passed as parameters.
    /// </summary>
    public static string PageSql(BlogAiTable table) =>
        $"SELECT {string.Join(", ", table.Columns.Select(c => SelectExpression(table, c)))} FROM dbo.[{table.Name}] ORDER BY {string.Join(", ", table.Key.Select(k => $"[{k}]"))} OFFSET @offset ROWS FETCH NEXT @limit ROWS ONLY";

    // the value column of a table with a secret-name rule is masked in SQL, so a secret never leaves the database
    private static string SelectExpression(BlogAiTable table, BlogAiColumn column)
    {
        if (table.MaskValueColumn is null || table.MaskNameColumn is null || column.Name != table.MaskValueColumn)
        {
            return $"[{column.Name}]";
        }

        var secret = string.Join(" OR ", SecretNamePatterns.Select(p => $"[{table.MaskNameColumn}] LIKE N'%{p}%'"));
        return $"CASE WHEN {secret} THEN N'[masked]' ELSE [{column.Name}] END AS [{column.Name}]";
    }
}
