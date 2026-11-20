using Adventures.Tools;

namespace Adventures.Blog.WebApi;

/// <summary>The drafts-only authoring surface (<see cref="AuthoringController"/>) described as tools: posts, their conversation, categories and tags.</summary>
public static class AuthoringTools
{
    public const string Scope = AuthoringController.RequiredScope;

    public static IReadOnlyList<IAppTool> All { get; } =
    [
        new AppTool(
            "list_my_posts",
            "The posts the authoring account has written (id, title, slug, whether published, categories, tags), newest first.",
            """{ "type": "object", "properties": {} }""",
            Scope, "GET", "api/author/posts"),
        new AppTool(
            "create_draft_post",
            "Creates a blog post as a DRAFT under the authoring account. It can never publish: an administrator reviews and publishes in the admin. Content is Markdown. Categories and tags are given by name and are created when they do not exist.",
            """
            { "type": "object",
              "properties": {
                "title": { "type": "string" },
                "content": { "type": "string", "description": "The post body in Markdown." },
                "description": { "type": "string", "description": "One-sentence summary." },
                "slug": { "type": "string", "description": "Optional URL slug; made from the title when omitted." },
                "categories": { "type": "array", "items": { "type": "string" }, "description": "For example Help, Development log, MCP." },
                "tags": { "type": "array", "items": { "type": "string" }, "description": "By feature, for example Pages, Posts, Roles, MCP." },
                "enableComments": { "type": "boolean" } },
              "required": ["title"] }
            """,
            Scope, "POST", "api/author/posts"),
        new AppTool(
            "update_draft_post",
            "Revises one of the authoring account's own posts while it is still a draft; only the fields given change. A published post, or someone else's, is refused.",
            """
            { "type": "object",
              "properties": {
                "id": { "type": "string", "description": "The post id from list_my_posts or create_draft_post." },
                "title": { "type": "string" }, "content": { "type": "string" }, "description": { "type": "string" }, "slug": { "type": "string" },
                "categories": { "type": "array", "items": { "type": "string" } }, "tags": { "type": "array", "items": { "type": "string" } },
                "enableComments": { "type": "boolean" } },
              "required": ["id", "title"] }
            """,
            Scope, "PUT", "api/author/posts/{id}"),
        new AppTool(
            "propose_revision",
            "Proposes changes to one of the authoring account's own PUBLISHED posts without changing what readers see: it creates a hidden draft copy carrying the changes, marked as a revision of the post (asking again updates the same revision). An administrator applies or discards it in the admin. Name only what changes; everything else carries over from the published post, and the published address never changes.",
            """
            { "type": "object",
              "properties": {
                "id": { "type": "string", "description": "The published post id from list_my_posts." },
                "title": { "type": "string" }, "content": { "type": "string", "description": "The new Markdown body." }, "description": { "type": "string" },
                "categories": { "type": "array", "items": { "type": "string" } }, "tags": { "type": "array", "items": { "type": "string" } },
                "enableComments": { "type": "boolean" } },
              "required": ["id"] }
            """,
            Scope, "POST", "api/author/posts/{id}/revision"),
        new AppTool(
            "list_post_comments",
            "The conversation under one of the authoring account's own posts, oldest first: who wrote each entry, when, whether it is a Comment or a Decision the human recorded, and the text. Read it at the start of a session to see the human's answers and decisions.",
            """{ "type": "object", "properties": { "id": { "type": "string", "description": "The post id from list_my_posts." } }, "required": ["id"] }""",
            Scope, "GET", "api/author/posts/{id}/comments"),
        new AppTool(
            "add_post_comment",
            "Adds a plain comment (an answer, a question, a note) to the conversation under one of the authoring account's own posts. It can never record a decision: the human does that.",
            """{ "type": "object", "properties": { "id": { "type": "string", "description": "The post id from list_my_posts." }, "text": { "type": "string", "description": "Markdown, up to 8000 characters." } }, "required": ["id", "text"] }""",
            Scope, "POST", "api/author/posts/{id}/comments"),
        new AppTool(
            "list_categories",
            "Every category (id and name).",
            """{ "type": "object", "properties": {} }""",
            Scope, "GET", "api/author/categories"),
        new AppTool(
            "list_tags",
            "Every tag (id and name).",
            """{ "type": "object", "properties": {} }""",
            Scope, "GET", "api/author/tags"),
        new AppTool(
            "create_category",
            "Finds the category with this name (any case) or creates it.",
            """{ "type": "object", "properties": { "name": { "type": "string" } }, "required": ["name"] }""",
            Scope, "POST", "api/author/categories"),
        new AppTool(
            "create_tag",
            "Finds the tag with this name (any case) or creates it.",
            """{ "type": "object", "properties": { "name": { "type": "string" } }, "required": ["name"] }""",
            Scope, "POST", "api/author/tags"),
    ];
}
