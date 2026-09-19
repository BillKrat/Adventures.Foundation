using Adventures.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventures.Security.Tests;

public class InMemoryClientCredentialStoreTests
{
    private readonly ClientSecretHasher _hasher = new();

    private InMemoryClientCredentialStore CreateStore(params M2MClientOptions[] clients)
    {
        var options = new M2MClientsOptions { Clients = clients.ToList() };
        return new InMemoryClientCredentialStore(Options.Create(options), _hasher);
    }

    [Fact]
    public void FindByClientId_ReturnsClient_WhenRegistered()
    {
        var store = CreateStore(new M2MClientOptions
        {
            ClientId = "mcp-host",
            Secret = "s3cr3t",
            Scopes = ["mcp.postgres.query", "mcp.filesearch.search"],
        });

        var client = store.FindByClientId("mcp-host");

        Assert.NotNull(client);
        Assert.Equal("mcp-host", client!.ClientId);
        Assert.Contains("mcp.postgres.query", client.AllowedScopes);
        Assert.Contains("mcp.filesearch.search", client.AllowedScopes);
    }

    [Fact]
    public void FindByClientId_HashesSecret_NotStoredAsPlaintext()
    {
        var store = CreateStore(new M2MClientOptions { ClientId = "mcp-host", Secret = "s3cr3t" });

        var client = store.FindByClientId("mcp-host");

        Assert.NotNull(client);
        Assert.NotEqual("s3cr3t", client!.HashedSecret);
        Assert.True(_hasher.Verify("s3cr3t", client.HashedSecret));
    }

    [Fact]
    public void FindByClientId_ReturnsNull_ForUnknownClient()
    {
        var store = CreateStore(new M2MClientOptions { ClientId = "mcp-host", Secret = "s3cr3t" });

        Assert.Null(store.FindByClientId("unknown-client"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FindByClientId_ReturnsNull_ForNullOrEmptyClientId(string? clientId)
    {
        var store = CreateStore(new M2MClientOptions { ClientId = "mcp-host", Secret = "s3cr3t" });

        Assert.Null(store.FindByClientId(clientId!));
    }

    [Fact]
    public void Constructor_SkipsEntriesMissingClientIdOrSecret()
    {
        var store = CreateStore(
            new M2MClientOptions { ClientId = "", Secret = "s3cr3t" },
            new M2MClientOptions { ClientId = "no-secret-client", Secret = "" },
            new M2MClientOptions { ClientId = "valid-client", Secret = "s3cr3t" });

        Assert.Null(store.FindByClientId("no-secret-client"));
        Assert.NotNull(store.FindByClientId("valid-client"));
    }
}
