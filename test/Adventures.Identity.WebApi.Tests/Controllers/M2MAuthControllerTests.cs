using Adventures.Identity.WebApi;
// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Security;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Xunit;

namespace Adventures.Identity.WebApi.Tests;

public class M2MAuthControllerTests
{
    private static readonly ClientSecretHasher Hasher = new();

    private static IJwtTokenService BuildTokenService() =>
        new JwtTokenService(Options.Create(new JwtTokenOptions
        {
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            SigningKey = "0123456789abcdef0123456789abcdef",
            AccessTokenLifetime = TimeSpan.FromMinutes(30),
        }));

    private static IClientCredentialStore BuildClientStore() =>
        new InMemoryClientCredentialStore(
            Options.Create(new M2MClientsOptions
            {
                Clients =
                [
                    new M2MClientOptions
                    {
                        ClientId = "mcp-host",
                        Secret = "host-secret",
                        Scopes = ["mcp.postgres.query", "mcp.filesearch.search"],
                    },
                    new M2MClientOptions
                    {
                        ClientId = "mcp-server-postgres",
                        Secret = "postgres-secret",
                        Scopes = ["mcp.postgres.query"],
                    },
                ],
            }),
            Hasher);

    private static M2MAuthController BuildController() =>
        new(BuildTokenService(), BuildClientStore(), Hasher);

    [Fact]
    public void Token_ReturnsOkWithScopedToken_WhenCredentialsAreValid()
    {
        var controller = BuildController();

        var result = controller.Token(new M2MTokenRequest("mcp-host", "host-secret", Scope: null));

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<M2MTokenResponse>(okResult.Value);
        Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));
        Assert.Equal("mcp.postgres.query mcp.filesearch.search", response.Scope);
    }

    [Fact]
    public void Token_ReturnsUnauthorized_WhenSecretIsWrong()
    {
        var controller = BuildController();

        var result = controller.Token(new M2MTokenRequest("mcp-host", "wrong-secret", Scope: null));

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public void Token_ReturnsUnauthorized_WhenClientIsUnknown()
    {
        var controller = BuildController();

        var result = controller.Token(new M2MTokenRequest("unknown-client", "any-secret", Scope: null));

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public void Token_GrantsOnlyRequestedScope_WhenScopeIsRestricted()
    {
        var controller = BuildController();

        var result = controller.Token(new M2MTokenRequest("mcp-host", "host-secret", Scope: "mcp.postgres.query"));

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<M2MTokenResponse>(okResult.Value);
        Assert.Equal("mcp.postgres.query", response.Scope);
    }

    [Fact]
    public void Token_ReturnsUnauthorized_WhenClientRequestsScopeItIsNotAllowed()
    {
        var controller = BuildController();

        var result = controller.Token(new M2MTokenRequest("mcp-server-postgres", "postgres-secret", Scope: "mcp.filesearch.search"));

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public void Token_GrantsOnlyIntersectionOfRequestedAndAllowedScopes()
    {
        var controller = BuildController();

        var result = controller.Token(new M2MTokenRequest(
            "mcp-server-postgres", "postgres-secret", Scope: "mcp.postgres.query mcp.filesearch.search"));

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<M2MTokenResponse>(okResult.Value);
        Assert.Equal("mcp.postgres.query", response.Scope);
    }
}
