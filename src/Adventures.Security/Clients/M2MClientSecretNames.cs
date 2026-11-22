using System.Text;

namespace Adventures.Security;

/// <summary>
/// The configuration name of a machine client's secret, by purpose: <c>ClientSecret:ClaudeDiagnostics</c> for the client <c>claude-diagnostics</c> (in an environment variable
/// <c>ClientSecret__ClaudeDiagnostics</c>). A name tells you what the secret is for and stays right when clients are added; the older <c>M2MClients:Clients:3:Secret</c> is bound by
/// position and silently re-points if a client is inserted.
/// </summary>
public static class M2MClientSecretNames
{
    public const string Section = "ClientSecret";

    /// <summary>The key for a client id: letters and digits kept, each word started with a capital, separators (<c>- . _</c> and the like) dropped.</summary>
    public static string KeyFor(string clientId)
    {
        var name = new StringBuilder();
        var startOfWord = true;
        foreach (var character in clientId)
        {
            if (!char.IsLetterOrDigit(character))
            {
                startOfWord = true;
                continue;
            }

            name.Append(startOfWord ? char.ToUpperInvariant(character) : character);
            startOfWord = false;
        }

        return $"{Section}:{name}";
    }
}
