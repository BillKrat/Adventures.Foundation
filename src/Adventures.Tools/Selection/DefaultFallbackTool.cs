namespace Adventures.Tools;

/// <summary>The last link of the chain: it handles every key, so a search always ends with an answer (the best guess).</summary>
public sealed class DefaultFallbackTool() : AppTool("fallback", "The best guess when no tool matches the request.", "{}", "none", "GET", string.Empty)
{
    public override bool CanHandle(string searchKey) => true;
}
