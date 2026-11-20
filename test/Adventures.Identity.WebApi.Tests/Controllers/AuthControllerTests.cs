using Adventures.Identity.WebApi;
// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Identity;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Adventures.Identity.WebApi.Tests;

public class AuthControllerTests
{
    private static IConfiguration BuildConfiguration(string? tenant = "global-webnet.com") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Tenant"] = tenant,
            })
            .Build();

    [Fact]
    public async Task Token_ReturnsOkWithAccessToken_WhenCredentialsAreValid()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var accountService = new FakeUserAccountService(LoginResult.Success("fake-token", expiresAt, mustChangePassword: true));
        var controller = new AuthController(accountService, BuildConfiguration(), NoTenant);

        var result = await controller.Token(new TokenRequest("Admin", "Password"));

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var tokenResponse = Assert.IsType<TokenResponse>(okResult.Value);
        Assert.Equal("fake-token", tokenResponse.AccessToken);
        Assert.Equal(expiresAt, tokenResponse.ExpiresAtUtc);
        Assert.True(tokenResponse.MustChangePassword);
        Assert.Equal("global-webnet.com", accountService.LastTenant);
        Assert.Equal("Admin", accountService.LastUsername);
        Assert.Equal("Password", accountService.LastPassword);
    }

    [Theory]
    [InlineData("wrong-user", "Password")]
    [InlineData("Admin", "WrongPassword")]
    public async Task Token_ReturnsUnauthorized_WhenCredentialsAreInvalid(string userName, string password)
    {
        var accountService = new FakeUserAccountService(LoginResult.Failure(LoginFailureReason.InvalidCredentials));
        var controller = new AuthController(accountService, BuildConfiguration(), NoTenant);

        var result = await controller.Token(new TokenRequest(userName, password));

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("Admin", "")]
    [InlineData("  ", "Password")]
    [InlineData(null, null)]
    public async Task Token_ReturnsBadRequestNotAServerError_WhenCredentialsAreBlank(string? userName, string? password)
    {
        var accountService = new FakeUserAccountService(LoginResult.Failure(LoginFailureReason.InvalidCredentials));
        var controller = new AuthController(accountService, BuildConfiguration(), NoTenant);

        var result = await controller.Token(new TokenRequest(userName, password));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Equal(400, problem.Status);
        Assert.Null(accountService.LastUsername);
    }

    /// <summary>A request whose host the server matched to no tenant: sign-in keeps using the configured tenant exactly as before.</summary>
    private static readonly Adventures.WebApi.IRequestTenant NoTenant = Tenant(null, Adventures.Entities.TenantMatch.None);

    private sealed class FixedTenant(Adventures.Entities.CurrentTenant current) : Adventures.WebApi.IRequestTenant
    {
        public Adventures.Entities.CurrentTenant Current { get; } = current;
    }

    private static Adventures.WebApi.IRequestTenant Tenant(string? domain, Adventures.Entities.TenantMatch match) =>
        new FixedTenant(new Adventures.Entities.CurrentTenant(domain is null ? null : "id", domain, domain, "api.host", match));

    /// <summary>T1: one deployed app serves many domains, so a person signs in to the tenant of the host the server saw, not to one tenant from configuration.</summary>
    [Fact]
    public async Task Token_SignsInToTheTenantOfTheRequestHost_WhenTheServerMatchedOne()
    {
        var accountService = new FakeUserAccountService(LoginResult.Failure(LoginFailureReason.InvalidCredentials));
        var controller = new AuthController(accountService, BuildConfiguration(tenant: "configured.example"), Tenant("Other.example", Adventures.Entities.TenantMatch.Domain));

        await controller.Token(new TokenRequest("Admin", "Password"));

        Assert.Equal("other.example", accountService.LastTenant);
    }

    [Fact]
    public async Task Token_UsesConfiguredTenant_DefaultingToGlobalWebnetWhenUnset()
    {
        var accountService = new FakeUserAccountService(LoginResult.Failure(LoginFailureReason.InvalidCredentials));
        var controller = new AuthController(accountService, BuildConfiguration(tenant: null), NoTenant);

        await controller.Token(new TokenRequest("Admin", "Password"));

        Assert.Equal("global-webnet.com", accountService.LastTenant);
    }

    [Fact]
    public void WhoAmI_ReturnsUnknown_WhenNoAuthenticatedUserIsSet()
    {
        var accountService = new FakeUserAccountService(LoginResult.Failure(LoginFailureReason.InvalidCredentials));
        var controller = new AuthController(accountService, BuildConfiguration(), NoTenant);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext(),
        };

        var result = controller.WhoAmI();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("unknown", okResult.Value);
    }

    private static AuthController ControllerSignedInAs(FakeUserAccountService accountService, string? subject)
    {
        var claims = subject is null ? [] : new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, subject) };
        return new AuthController(accountService, BuildConfiguration(), NoTenant)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "test")),
                },
            },
        };
    }

    [Fact]
    public async Task ChangePassword_ReturnsNoContent_AndPassesTheTokenSubject()
    {
        var userId = Guid.NewGuid();
        var accountService = new FakeUserAccountService(LoginResult.Failure(LoginFailureReason.None)) { ChangeResult = ChangePasswordResult.Succeeded };

        var result = await ControllerSignedInAs(accountService, userId.ToString())
            .ChangePassword(new ChangePasswordRequest("old-password", "a-much-longer-passphrase"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal((userId, "old-password", "a-much-longer-passphrase"), accountService.LastChange);
    }

    [Theory]
    [InlineData(ChangePasswordResult.InvalidCurrentPassword, "CurrentPassword")]
    [InlineData(ChangePasswordResult.NewPasswordRejected, "NewPassword")]
    [InlineData(ChangePasswordResult.AccountLocked, "CurrentPassword")]
    public async Task ChangePassword_ReturnsAFieldedBadRequest_WhenTheServiceRefuses(ChangePasswordResult outcome, string field)
    {
        var accountService = new FakeUserAccountService(LoginResult.Failure(LoginFailureReason.None)) { ChangeResult = outcome };

        var result = await ControllerSignedInAs(accountService, Guid.NewGuid().ToString())
            .ChangePassword(new ChangePasswordRequest("old-password", "a-much-longer-passphrase"), CancellationToken.None);

        var problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result).Value);
        Assert.Contains(field, problem.Errors.Keys);
    }

    [Theory]
    [InlineData(null, "new-password-long")]
    [InlineData("old", "")]
    [InlineData("  ", "  ")]
    public async Task ChangePassword_ReturnsBadRequest_WhenAFieldIsBlank(string? current, string? replacement)
    {
        var accountService = new FakeUserAccountService(LoginResult.Failure(LoginFailureReason.None));

        var result = await ControllerSignedInAs(accountService, Guid.NewGuid().ToString())
            .ChangePassword(new ChangePasswordRequest(current, replacement), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null(accountService.LastChange);
    }

    [Fact]
    public async Task ChangePassword_ReturnsUnauthorized_WhenTheTokenHasNoUserId()
    {
        var accountService = new FakeUserAccountService(LoginResult.Failure(LoginFailureReason.None));

        var result = await ControllerSignedInAs(accountService, "not-a-guid")
            .ChangePassword(new ChangePasswordRequest("old-password", "a-much-longer-passphrase"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Null(accountService.LastChange);
    }

    private sealed class FakeUserAccountService(LoginResult result) : IUserAccountService
    {
        public ChangePasswordResult ChangeResult { get; set; } = ChangePasswordResult.NotFound;

        public (Guid UserId, string Current, string New)? LastChange { get; private set; }

        public Task<ChangePasswordResult> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
        {
            LastChange = (userId, currentPassword, newPassword);
            return Task.FromResult(ChangeResult);
        }

        public string? LastTenant { get; private set; }

        public string? LastUsername { get; private set; }

        public string? LastPassword { get; private set; }

        public Task<LoginResult> LoginAsync(string tenant, string username, string password, CancellationToken cancellationToken = default)
        {
            LastTenant = tenant;
            LastUsername = username;
            LastPassword = password;
            return Task.FromResult(result);
        }
    }
}
