using Microsoft.Extensions.Options;

namespace Adventures.Security;

/// <summary>
/// In-memory, configuration-seeded implementation of <see cref="IClientCredentialStore"/>.
/// Secrets are hashed once at construction time (never held in plaintext). Intended as a
/// placeholder for local/dev use; a database-backed store should replace this without
/// requiring changes to <see cref="IClientCredentialStore"/> consumers.
/// </summary>
public sealed class InMemoryClientCredentialStore : IClientCredentialStore
{
    private readonly Dictionary<string, ClientCredential> _clientsById;

    public InMemoryClientCredentialStore(IOptions<M2MClientsOptions> options, IClientSecretHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(hasher);

        _clientsById = options.Value.Clients
            .Where(c => !string.IsNullOrWhiteSpace(c.ClientId) && !string.IsNullOrWhiteSpace(c.Secret))
            .ToDictionary(
                c => c.ClientId,
                c => new ClientCredential(c.ClientId, hasher.Hash(c.Secret), c.Scopes.AsReadOnly()),
                StringComparer.Ordinal);
    }

    public ClientCredential? FindByClientId(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        return _clientsById.TryGetValue(clientId, out var client) ? client : null;
    }
}
