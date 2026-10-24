// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
namespace Adventures.Security;

/// <summary>Configuration-bound definition of a single machine-to-machine client, used to seed the in-memory store.</summary>
public sealed class M2MClientOptions
{
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Plaintext secret as configured (e.g. via user-secrets); hashed immediately when seeding the store.</summary>
    public string Secret { get; set; } = string.Empty;

    public List<string> Scopes { get; set; } = [];
}

/// <summary>Configuration section ("M2MClients") listing the registered machine-to-machine clients.</summary>
public sealed class M2MClientsOptions
{
    public const string SectionName = "M2MClients";

    public List<M2MClientOptions> Clients { get; set; } = [];
}
