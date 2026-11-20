namespace Adventures.Tools;

/// <summary>Finds the tool for a search key among the tools the caller may use. A developer may replace the implementation.</summary>
public interface IToolSelector
{
    /// <summary>Never returns null: the last answer is the <see cref="DefaultFallbackTool"/>.</summary>
    IAppTool Select(string searchKey, IEnumerable<IAppTool> available);
}
