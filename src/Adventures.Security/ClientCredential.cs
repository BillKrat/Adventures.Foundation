namespace Adventures.Security;

/// <summary>
/// Represents a registered machine-to-machine client (e.g. an MCP server or the MCP host itself)
/// allowed to request an access token via the OAuth2 client-credentials grant.
/// </summary>
/// <param name="ClientId">The public identifier for the client (e.g. "mcp-host", "mcp-server-postgres").</param>
/// <param name="HashedSecret">The client secret, already hashed - never store or compare plaintext secrets.</param>
/// <param name="AllowedScopes">The set of scopes this client is permitted to request/hold.</param>
public sealed record ClientCredential(string ClientId, string HashedSecret, IReadOnlyCollection<string> AllowedScopes);
