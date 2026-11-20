namespace Adventures.Tools;

/// <summary>
/// Chain of Responsibility: the tools are asked in order and the first that handles the key wins. When none does, an optional
/// <see cref="IToolGuesser"/> may pick one of the offered tools; otherwise the <see cref="DefaultFallbackTool"/> answers.
/// </summary>
public sealed class ToolSelector(IToolGuesser? guesser = null) : IToolSelector
{
    private readonly DefaultFallbackTool _fallback = new();

    public IAppTool Select(string searchKey, IEnumerable<IAppTool> available)
    {
        var tools = available.Where(tool => tool is not DefaultFallbackTool).ToList();
        if (string.IsNullOrWhiteSpace(searchKey))
        {
            return _fallback;
        }

        foreach (var tool in tools)
        {
            if (tool.CanHandle(searchKey))
            {
                return tool;
            }
        }

        var guess = guesser?.Guess(searchKey, tools);
        return guess is not null && tools.Contains(guess) ? guess : _fallback;
    }
}
