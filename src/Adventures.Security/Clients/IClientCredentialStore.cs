// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Security;

/// <summary>
/// Looks up registered machine-to-machine clients for the client-credentials grant.
/// The in-memory implementation is a placeholder for local/dev use; a database-backed
/// implementation is expected to replace it without changing this contract.
/// </summary>
public interface IClientCredentialStore
{
    /// <summary>Finds a registered client by its client_id, or null if none is registered.</summary>
    ClientCredential? FindByClientId(string clientId);
}
