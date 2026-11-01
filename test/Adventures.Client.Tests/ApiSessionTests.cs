using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using Adventures.Security;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Adventures.Client.Tests;

public class ApiSessionTests
{
    private static string Token(string name, string role)
    {
        var key = new SymmetricSecurityKey(new byte[32]);
        var jwt = new JwtSecurityToken("issuer", "audience", [new Claim(ClaimTypes.Name, name), new Claim(WellKnownClaims.Roles, role)],
            expires: DateTime.UtcNow.AddHours(1), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static (ApiSession Session, AmbientPrincipal Principal, StubHandler Handler) Build(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var principal = new AmbientPrincipal();
        return (new ApiSession(StubHandler.Client(handler), principal), principal, handler);
    }

    [Fact]
    public async Task SignIn_posts_the_credentials_and_records_who_signed_in()
    {
        var (session, principal, handler) = Build(_ => StubHandler.Json(new { accessToken = Token("tester", "TenantAdmin"), expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1), mustChangePassword = true }));

        Assert.True(await session.SignInAsync("tester", "secret-pw"));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/auth/token", request.Url);
        Assert.Contains("\"userName\":\"tester\"", request.Body);
        Assert.True(session.IsSignedIn);
        Assert.True(session.MustChangePassword);
        Assert.Equal("tester", principal.Principal.FindFirst(ClaimTypes.Name)?.Value);
        Assert.True(principal.Principal.HasClaim(WellKnownClaims.Roles, "TenantAdmin"));
    }

    [Fact]
    public async Task Later_calls_carry_the_bearer_token()
    {
        var token = Token("tester", "Reader");
        var (session, _, handler) = Build(request => request.RequestUri!.AbsolutePath.EndsWith("token", StringComparison.Ordinal)
            ? StubHandler.Json(new { accessToken = token, expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1), mustChangePassword = false })
            : StubHandler.Empty(HttpStatusCode.OK));
        await session.SignInAsync("tester", "pw");
        var http = StubHandler.Client(handler);
        http.DefaultRequestHeaders.Authorization = null;

        // the session owns the header of the client it was built with; build a second session on the same client to inspect it
        var shared = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        var second = new ApiSession(shared, new AmbientPrincipal());
        await second.SignInAsync("tester", "pw");
        await shared.GetAsync("api/anything");

        Assert.Equal($"Bearer {token}", handler.Requests.Last().Authorization);
    }

    [Fact]
    public async Task Refused_credentials_return_false_and_leave_nobody_signed_in()
    {
        var (session, principal, handler) = Build(_ => StubHandler.Empty(HttpStatusCode.Unauthorized));

        Assert.False(await session.SignInAsync("tester", "wrong"));

        Assert.False(session.IsSignedIn);
        Assert.False(principal.Principal.Identity!.IsAuthenticated);
        Assert.Null(handler.Requests.Single().Authorization);
    }

    [Fact]
    public async Task A_validation_refusal_surfaces_the_field_messages_only()
    {
        var (session, _, _) = Build(_ => StubHandler.Json(new { title = "One or more validation errors occurred.", errors = new Dictionary<string, string[]> { ["Password"] = ["Password is required."] }, trace = "SECRET-STACK" }, HttpStatusCode.BadRequest));

        var error = await Assert.ThrowsAsync<ApiException>(() => session.SignInAsync("tester", ""));

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal(["Password is required."], error.Errors["Password"]);
        Assert.DoesNotContain("SECRET-STACK", error.Message);
    }

    [Fact]
    public async Task SignOut_forgets_the_person_and_the_token()
    {
        var (session, principal, handler) = Build(_ => StubHandler.Json(new { accessToken = Token("tester", "Reader"), expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1), mustChangePassword = false }));
        await session.SignInAsync("tester", "pw");

        session.SignOut();

        Assert.False(session.IsSignedIn);
        Assert.False(principal.Principal.Identity!.IsAuthenticated);
        Assert.Null(session.ExpiresAtUtc);
    }
}
