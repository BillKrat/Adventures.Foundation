namespace Adventures.Tools;

/// <summary>Every tool the features declared, and which of them a caller may use.</summary>
public sealed class ToolCatalog
{
    private readonly IReadOnlyList<IAppTool> _tools;

    public ToolCatalog(IEnumerable<IAppTool> tools)
    {
        _tools = tools.ToList();
        var duplicate = _tools.GroupBy(tool => tool.Name, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Two tools are named '{duplicate.Key}'.", nameof(tools));
        }
    }

    public IReadOnlyList<IAppTool> All => _tools;

    public IAppTool? Find(string name) => _tools.FirstOrDefault(tool => string.Equals(tool.Name, name, StringComparison.Ordinal));

    /// <summary>The tools whose scope is among the caller's <paramref name="scopes"/> and whose plug-in (any one of them, when it has any) is among the <paramref name="enabledPlugins"/>.</summary>
    public IReadOnlyList<IAppTool> Available(IReadOnlySet<string> scopes, IReadOnlySet<string> enabledPlugins) =>
        _tools.Where(tool => scopes.Contains(tool.RequiredScope) && (tool.PluginKeys.Count == 0 || tool.PluginKeys.Any(enabledPlugins.Contains))).ToList();
}
