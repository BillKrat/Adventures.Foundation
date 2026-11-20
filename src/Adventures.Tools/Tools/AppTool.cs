namespace Adventures.Tools;

/// <summary>
/// A tool described as data. <see cref="CanHandle"/> is virtual: the default matches by exact name, by every word of the key appearing in the
/// name or description (a grep), and by a small misspelling of the name (fuzzy); a tool author overrides it for something better.
/// </summary>
public class AppTool(string name, string description, string inputSchema, string requiredScope, string method, string pathTemplate, IReadOnlyList<string>? pluginKeys = null) : IAppTool
{
    private const int FuzzyDistance = 2;
    private const int FuzzyMinimumKeyLength = 6;

    public string Name { get; } = name;

    public string Description { get; } = description;

    public string InputSchema { get; } = inputSchema;

    public string RequiredScope { get; } = requiredScope;

    public IReadOnlyList<string> PluginKeys { get; } = pluginKeys ?? [];

    public string Method { get; } = method;

    public string PathTemplate { get; } = pathTemplate;

    public virtual bool CanHandle(string searchKey)
    {
        var key = Normalise(searchKey);
        if (key.Length == 0)
        {
            return false;
        }

        var name = Normalise(Name);
        if (key == name)
        {
            return true;
        }

        var haystack = $"{name} {Normalise(Description)}";
        if (key.Split(' ').All(word => haystack.Contains(word, StringComparison.Ordinal)))
        {
            return true;
        }

        return key.Length >= FuzzyMinimumKeyLength && Distance(key, name) <= FuzzyDistance;
    }

    private static string Normalise(string? text) =>
        string.Join(' ', (text ?? string.Empty).ToLowerInvariant().Replace('_', ' ').Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static int Distance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            previous = current;
        }

        return previous[b.Length];
    }
}
