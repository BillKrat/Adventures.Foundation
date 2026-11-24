// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Blog.Legacy.Research;

/// <summary>One parameter of a catalogue query. <see cref="Kind"/> is "int", "string" or "guid"; a null <see cref="Default"/> makes it required.</summary>
public sealed record BlogAiParameter(string Name, string Kind, string Description, object? Default = null, bool Required = false);

/// <summary>A fixed, parameterised, SELECT-only query the legacy-data tools may run. Nothing outside this list can reach the database.</summary>
public sealed record BlogAiQuery(string Name, string Description, string Sql, IReadOnlyList<BlogAiParameter> Parameters);

/// <summary>
/// The whole surface of the legacy BlogEngine.NET database (BlogAI) that the MCP server exposes, as named read-only queries.
/// Deliberately a short allowlist rather than free SQL: the SQL login is SELECT-only too, but a query that is not in this list
/// cannot be asked for at all. What is left out on purpose: password hashes (<c>be_Users.Password</c>), the values in
/// <c>be_DataStoreSettings</c> (extension settings hold keys, for example the reCAPTCHA private key), the profile and notify tables,
/// file contents, and the shared <c>AspNet*</c>/<c>Triples</c> tables that belong to other applications. Settings whose name looks
/// like a secret are filtered out.
/// </summary>
public static class BlogAiCatalog
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 500;

    private static BlogAiParameter Limit => new("limit", "int", $"How many rows (1-{MaxLimit}, default {DefaultLimit}).", DefaultLimit);

    private static BlogAiParameter Table => new("table", "string", "A table name such as be_Posts (letters, digits, underscore).", Required: true);

    public static readonly IReadOnlyList<BlogAiQuery> Queries =
    [
        new("blogai_tables",
            "Every table in the legacy database with its row count (includes tables of other applications sharing the database).",
            """
            SELECT t.name AS tableName, SUM(p.rows) AS rowTotal
            FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
            GROUP BY t.name ORDER BY t.name
            """,
            []),

        new("blogai_columns",
            "The columns of one table: name, type, length, nullability.",
            """
            SELECT COLUMN_NAME AS columnName, DATA_TYPE AS dataType, CHARACTER_MAXIMUM_LENGTH AS maxLength, IS_NULLABLE AS isNullable
            FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table ORDER BY ORDINAL_POSITION
            """,
            [Table]),

        new("blogai_foreign_keys",
            "Every foreign key: the child table and column, and the parent table and column it points at.",
            """
            SELECT OBJECT_NAME(fk.parent_object_id) AS childTable, COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS childColumn,
                   OBJECT_NAME(fk.referenced_object_id) AS parentTable, COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS parentColumn
            FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            ORDER BY childTable, childColumn
            """,
            []),

        new("blogai_blogs",
            "The blogs (BlogEngine multi-blog): id, name, host name, whether primary and active.",
            """
            SELECT BlogId AS blogId, BlogName AS blogName, Hostname AS hostname, IsPrimary AS isPrimary, IsActive AS isActive,
                   StorageContainerName AS storageContainerName, VirtualPath AS virtualPath
            FROM be_Blogs ORDER BY BlogRowId
            """,
            []),

        new("blogai_posts",
            "Posts without their body (the body is blogai_post): id, title, slug, dates, author, published, deleted, comments enabled, body length. Newest first.",
            """
            SELECT TOP (@limit) PostID AS postId, BlogID AS blogId, Title AS title, Slug AS slug, DateCreated AS dateCreated, DateModified AS dateModified,
                   Author AS author, IsPublished AS isPublished, IsDeleted AS isDeleted, IsCommentEnabled AS isCommentEnabled,
                   LEN(PostContent) AS contentLength
            FROM be_Posts WHERE (@includeDeleted = 1 OR IsDeleted = 0) ORDER BY DateCreated DESC
            """,
            [Limit, new BlogAiParameter("includeDeleted", "int", "1 to include soft-deleted posts (default 0).", 0)]),

        new("blogai_post",
            "One post in full (the body as stored, usually HTML) by post id or slug.",
            """
            SELECT TOP (1) PostID AS postId, BlogID AS blogId, Title AS title, Slug AS slug, Description AS description, PostContent AS content,
                   DateCreated AS dateCreated, DateModified AS dateModified, Author AS author, IsPublished AS isPublished,
                   IsDeleted AS isDeleted, IsCommentEnabled AS isCommentEnabled, Raters AS raters, Rating AS rating
            FROM be_Posts WHERE (@id IS NOT NULL AND PostID = TRY_CONVERT(uniqueidentifier, @id)) OR (@slug IS NOT NULL AND Slug = @slug)
            """,
            [new("id", "string", "The post id (a GUID); or give slug.", null), new("slug", "string", "The post slug; or give id.", null)]),

        new("blogai_post_taxonomy",
            "The categories and tags of one post.",
            """
            SELECT 'category' AS kind, c.CategoryName AS name FROM be_PostCategory pc JOIN be_Categories c ON c.CategoryID = pc.CategoryID
              WHERE pc.PostID = TRY_CONVERT(uniqueidentifier, @id)
            UNION ALL
            SELECT 'tag', Tag FROM be_PostTag WHERE PostID = TRY_CONVERT(uniqueidentifier, @id)
            """,
            [new("id", "string", "The post id (a GUID).", Required: true)]),

        new("blogai_pages",
            "Pages without their body: id, title, slug, parent, published, front page, shown in list, dates.",
            """
            SELECT TOP (@limit) PageID AS pageId, BlogID AS blogId, Title AS title, Slug AS slug, Parent AS parentId, IsPublished AS isPublished,
                   IsFrontPage AS isFrontPage, ShowInList AS showInList, IsDeleted AS isDeleted, DateCreated AS dateCreated,
                   DateModified AS dateModified, LEN(PageContent) AS contentLength
            FROM be_Pages ORDER BY DateCreated DESC
            """,
            [Limit]),

        new("blogai_page",
            "One page in full (the body as stored, usually HTML) by page id or slug.",
            """
            SELECT TOP (1) PageID AS pageId, BlogID AS blogId, Title AS title, Slug AS slug, Description AS description, PageContent AS content,
                   Keywords AS keywords, Parent AS parentId, IsPublished AS isPublished, IsFrontPage AS isFrontPage, ShowInList AS showInList,
                   IsDeleted AS isDeleted, DateCreated AS dateCreated, DateModified AS dateModified
            FROM be_Pages WHERE (@id IS NOT NULL AND PageID = TRY_CONVERT(uniqueidentifier, @id)) OR (@slug IS NOT NULL AND Slug = @slug)
            """,
            [new("id", "string", "The page id (a GUID); or give slug.", null), new("slug", "string", "The page slug; or give id.", null)]),

        new("blogai_categories",
            "Categories with parent and the number of posts in each.",
            """
            SELECT c.CategoryID AS categoryId, c.CategoryName AS name, c.Description AS description, c.ParentID AS parentId,
                   (SELECT COUNT(*) FROM be_PostCategory pc WHERE pc.CategoryID = c.CategoryID) AS postCount
            FROM be_Categories c ORDER BY c.CategoryName
            """,
            []),

        new("blogai_tags",
            "Tags with the number of posts using each, most used first.",
            "SELECT TOP (@limit) Tag AS tag, COUNT(*) AS postCount FROM be_PostTag GROUP BY Tag ORDER BY COUNT(*) DESC, Tag",
            [Limit]),

        new("blogai_comments",
            "Comments, newest first (author, approval and spam flags, a 300-character excerpt). Optionally for one post. Email and IP are not returned.",
            """
            SELECT TOP (@limit) PostCommentID AS commentId, PostID AS postId, ParentCommentID AS parentCommentId, CommentDate AS commentDate,
                   Author AS author, Website AS website, LEFT(Comment, 300) AS excerpt, IsApproved AS isApproved, IsSpam AS isSpam, IsDeleted AS isDeleted
            FROM be_PostComment WHERE @postId IS NULL OR PostID = TRY_CONVERT(uniqueidentifier, @postId) ORDER BY CommentDate DESC
            """,
            [Limit, new("postId", "string", "Only this post's comments (a GUID).", null)]),

        new("blogai_users",
            "Users: name, e-mail and last login. Never the password hash.",
            "SELECT UserName AS userName, EmailAddress AS emailAddress, LastLoginTime AS lastLoginTime FROM be_Users ORDER BY UserName",
            []),

        new("blogai_roles",
            "Roles, the rights each role holds, and which users are in which role.",
            """
            SELECT 'role' AS kind, [Role] AS a, NULL AS b FROM be_Roles
            UNION ALL SELECT 'right', [Role], RightName FROM be_RightRoles
            UNION ALL SELECT 'member', UserName, [Role] FROM be_UserRoles
            ORDER BY kind, a, b
            """,
            []),

        new("blogai_settings",
            "Blog settings (name and value, values cut at 500 characters). Names that look like secrets (password, key, secret, token) are left out.",
            """
            SELECT TOP (@limit) BlogId AS blogId, SettingName AS name, LEFT(SettingValue, 500) AS value FROM be_Settings
            WHERE SettingName NOT LIKE '%pass%' AND SettingName NOT LIKE '%secret%' AND SettingName NOT LIKE '%key%' AND SettingName NOT LIKE '%token%'
            ORDER BY SettingName
            """,
            [Limit]),

        new("blogai_extensions",
            "Which extensions have saved settings (type and id only; the values are not returned because they can hold keys).",
            "SELECT ExtensionType AS extensionType, ExtensionId AS extensionId FROM be_DataStoreSettings ORDER BY ExtensionType, ExtensionId",
            []),

        new("blogai_custom_fields",
            "Custom fields attached to posts, pages and other objects (type, object, key, value cut at 300 characters).",
            "SELECT TOP (@limit) CustomType AS customType, ObjectId AS objectId, [Key] AS [key], LEFT([Value], 300) AS value, Attribute AS attribute FROM be_CustomFields ORDER BY CustomType, ObjectId",
            [Limit]),

        new("blogai_files",
            "The database file store: directories and files with sizes and dates (never the contents).",
            """
            SELECT TOP (@limit) 'directory' AS kind, FullPath AS path, NULL AS size, LastModify AS lastModify FROM be_FileStoreDirectory
            UNION ALL SELECT 'file', FullPath, Size, LastModify FROM be_FileStoreFiles ORDER BY path
            """,
            [Limit]),

        new("blogai_count",
            "The row count of one table.",
            "SELECT SUM(p.rows) AS rowTotal FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1) WHERE t.name = @table",
            [Table]),
    ];

    public static BlogAiQuery? Find(string name) => Queries.FirstOrDefault(q => q.Name == name);
}
