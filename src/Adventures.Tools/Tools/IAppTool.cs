namespace Adventures.Tools;

/// <summary>
/// One thing an application can do, declared by the feature that owns it. A tool is a description of an API call (name, input, the right the API
/// enforces, method and path), not code that runs it, so every client can use the same list. A tool is also responsible for being found:
/// <see cref="CanHandle"/> says whether it matches a search key.
/// </summary>
public interface IAppTool
{
    string Name { get; }

    string Description { get; }

    /// <summary>JSON Schema of the input, as text.</summary>
    string InputSchema { get; }

    /// <summary>The scope the API demands on the caller token for this call (the same name the API enforces).</summary>
    string RequiredScope { get; }

    /// <summary>The plug-ins this tool belongs to: it is offered while any one of them is on. Empty means it always is.</summary>
    IReadOnlyList<string> PluginKeys { get; }

    string Method { get; }

    /// <summary>The route, with {placeholders} for values taken from the input.</summary>
    string PathTemplate { get; }

    /// <summary>True when this tool matches <paramref name="searchKey"/>. Return true or false with any logic you like (grep, fuzzy, RegEx, your own code).</summary>
    bool CanHandle(string searchKey);
}
