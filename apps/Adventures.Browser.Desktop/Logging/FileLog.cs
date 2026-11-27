using System.IO;

namespace Adventures.Browser.Desktop.Logging;

/// <summary>
/// Where unexpected failures go: a daily text file under the local profile of the signed-in Windows user. The person sees a generic line; the detail is
/// here for an administrator (no silent errors). Nothing in this app passes a token or a password to an exception message, so none reaches the file.
/// </summary>
internal static class FileLog
{
    public static string Directory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Adventures.Browser.Desktop", "logs");

    public static void Write(string source, Exception error)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.AppendAllText(Path.Combine(Directory, $"desktop-{DateTime.UtcNow:yyyyMMdd}.log"), $"{DateTime.UtcNow:O} [{source}] {error}{Environment.NewLine}");
        }
        catch (Exception logFailure) when (logFailure is IOException or UnauthorizedAccessException)
        {
            // a full or locked disk must not turn one failure into two
        }
    }
}
