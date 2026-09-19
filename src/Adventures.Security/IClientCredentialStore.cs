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
