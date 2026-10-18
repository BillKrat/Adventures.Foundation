namespace Adventures.Entities;

/// <summary>
/// An error that happened in the browser, reported to the server so it reaches the exception log like any server
/// exception (no silent errors). The message and stack are the client's own text; the signature groups repeats by message.
/// </summary>
public sealed class ClientReportedException(string message, string? clientStack) : Exception(message)
{
    /// <summary>The browser's stack text, shown where a .NET stack trace would be.</summary>
    public override string? StackTrace => clientStack;
}
