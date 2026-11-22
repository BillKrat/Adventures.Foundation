using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventures.Security.AspNetCore.Tests;

/// <summary>
/// A machine client's secret is named by its purpose (<c>ClientSecret:ClaudeDiagnostics</c>, in an environment <c>ClientSecret__ClaudeDiagnostics</c>), not by its position in a list
/// (<c>M2MClients:Clients:3:Secret</c>), which silently re-points when a client is inserted. The old position-based name still works until it is removed, and the new name wins when both exist.
/// </summary>
public class M2MClientSecretNameTests
{
    private static M2MClientsOptions Resolve(Dictionary<string, string?> settings)
    {
        settings["M2MClients:Clients:0:ClientId"] = "claude-diagnostics";
        settings["M2MClients:Clients:1:ClientId"] = "claude-author";
        settings["M2MClients:Clients:2:ClientId"] = "mcp-host";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddM2MClients(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<M2MClientsOptions>>().Value;
    }

    [Theory]
    [InlineData("claude-diagnostics", "ClaudeDiagnostics")]
    [InlineData("claude-author", "ClaudeAuthor")]
    [InlineData("mcp-server-postgres", "McpServerPostgres")]
    [InlineData("a.b_c-d", "ABCD")]
    public void TheNameOfAClientsSecret_IsItsClientIdInPascalCase(string clientId, string expected) =>
        Assert.Equal("ClientSecret:" + expected, M2MClientSecretNames.KeyFor(clientId));

    [Fact]
    public void ASecretNamedByPurpose_IsAppliedToItsClient()
    {
        var options = Resolve(new() { ["ClientSecret:ClaudeAuthor"] = "named-secret" });

        Assert.Equal("named-secret", options.Clients.Single(c => c.ClientId == "claude-author").Secret);
        Assert.Equal(string.Empty, options.Clients.Single(c => c.ClientId == "claude-diagnostics").Secret);   // no secret set = the client stays inactive
    }

    [Fact]
    public void TheOldPositionBasedSecret_StillWorks()
    {
        var options = Resolve(new() { ["M2MClients:Clients:0:Secret"] = "old-secret" });

        Assert.Equal("old-secret", options.Clients.Single(c => c.ClientId == "claude-diagnostics").Secret);
    }

    [Fact]
    public void WhenBothAreSet_TheNamedSecretWins()
    {
        var options = Resolve(new() { ["M2MClients:Clients:1:Secret"] = "old-secret", ["ClientSecret:ClaudeAuthor"] = "named-secret" });

        Assert.Equal("named-secret", options.Clients.Single(c => c.ClientId == "claude-author").Secret);
    }

    [Fact]
    public void ANamedSecretOfAnotherClient_IsNeverAppliedToThisOne()
    {
        var options = Resolve(new() { ["ClientSecret:ClaudeDiagnostics"] = "diag-secret" });

        Assert.Equal("diag-secret", options.Clients.Single(c => c.ClientId == "claude-diagnostics").Secret);
        Assert.Equal(string.Empty, options.Clients.Single(c => c.ClientId == "claude-author").Secret);
        Assert.Equal(string.Empty, options.Clients.Single(c => c.ClientId == "mcp-host").Secret);
    }

    [Fact]
    public void ABlankNamedSecret_DoesNotEraseTheOldOne()
    {
        var options = Resolve(new() { ["M2MClients:Clients:1:Secret"] = "old-secret", ["ClientSecret:ClaudeAuthor"] = "  " });

        Assert.Equal("old-secret", options.Clients.Single(c => c.ClientId == "claude-author").Secret);
    }
}
