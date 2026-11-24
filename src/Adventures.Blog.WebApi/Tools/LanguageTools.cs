using Adventures.Tools;

namespace Adventures.Blog.WebApi;

/// <summary>Language files as tools: read the keys to translate and propose a file for the administrator to review (never applied by the tool).</summary>
public static class LanguageTools
{
    public const string Scope = AuthorLanguagesController.RequiredScope;

    public static IReadOnlyList<IAppTool> All { get; } =
    [
        new AppTool(
            "get_language_catalog",
            "Every text key a language file can translate, with its English text (the screens' words and the server's own words such as admin menu names). Keep the keys exactly; texts may hold {0} style placeholders that a translation must keep. A note says when the list is incomplete.",
            """{ "type": "object", "properties": {} }""",
            Scope, "GET", "api/author/languages/catalog"),
        new AppTool(
            "propose_language_file",
            "Proposes a language file for the site: a table of text per key in the given language. It is only a PROPOSAL: it changes nothing a visitor sees until an administrator reviews it on Admin > Languages and applies it. Asking again for the same language replaces the earlier proposal. Texts are plain text (no HTML), at most 300 characters, no empty ones; keep {0} placeholders.",
            """
            { "type": "object",
              "properties": {
                "language": { "type": "string", "description": "A language code of two or three letters with an optional region, for example de, fr or pt-br." },
                "labels": { "type": "object", "additionalProperties": { "type": "string" }, "description": "Key to translated text, using the keys from get_language_catalog." } },
              "required": ["language", "labels"] }
            """,
            Scope, "PUT", "api/author/languages/{language}"),
    ];
}
