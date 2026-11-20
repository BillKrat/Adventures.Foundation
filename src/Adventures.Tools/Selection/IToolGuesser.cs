namespace Adventures.Tools;

/// <summary>An optional better guess for the fallback, for example one that asks an AI when Claude is configured. It may only choose from the tools offered.</summary>
public interface IToolGuesser
{
    IAppTool? Guess(string searchKey, IReadOnlyList<IAppTool> available);
}
