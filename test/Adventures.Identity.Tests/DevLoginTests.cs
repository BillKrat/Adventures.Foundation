using Adventures.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Identity.Tests;

/// <summary>
/// The Legacy-mode developer sign-in must be impossible anywhere but a developer machine. Each gate has its own test that fails if that gate is removed.
/// </summary>
public class DevLoginTests
{
    private sealed class Origin(bool isLocal) : IRequestOrigin
    {
        public bool IsLocal { get; } = isLocal;
    }

    private static async Task<(LoginResult Result, FakeJwtTokenService Tokens)> TryAsync(
        bool enabled = true, bool development = true, bool local = true, string user = "Admin", string password = "Password")
    {
        var tokens = new FakeJwtTokenService();
        var result = await new DevLoginAccountService(new DevLoginOptions(enabled, development), new Origin(local), tokens).LoginAsync("any", user, password);
        return (result, tokens);
    }

    [Fact]
    public async Task Admin_with_Password_signs_in_when_every_gate_holds_and_gets_the_Admin_role()
    {
        var (result, tokens) = await TryAsync(user: "admin");

        Assert.True(result.Succeeded);
        Assert.False(result.MustChangePassword);
        var call = Assert.Single(tokens.IssueTokenCalls);
        Assert.Equal(DevLoginOptions.SubjectId.ToString(), call.Subject);
        Assert.Contains(call.Claims, c => c.Type == WellKnownClaims.Roles && c.Value == "Admin");
    }

    [Fact]
    public async Task Switched_off_it_refuses_like_a_wrong_password()
    {
        var (result, tokens) = await TryAsync(enabled: false);

        Assert.False(result.Succeeded);
        Assert.Equal(LoginFailureReason.InvalidCredentials, result.FailureReason);
        Assert.Empty(tokens.IssueTokenCalls);
    }

    [Fact]
    public async Task Outside_Development_it_refuses_even_when_switched_on()
    {
        var (result, tokens) = await TryAsync(development: false);

        Assert.False(result.Succeeded);
        Assert.Empty(tokens.IssueTokenCalls);
    }

    [Fact]
    public async Task From_another_machine_it_refuses_even_in_Development_and_switched_on()
    {
        var (result, tokens) = await TryAsync(local: false);

        Assert.False(result.Succeeded);
        Assert.Empty(tokens.IssueTokenCalls);
    }

    [Theory]
    [InlineData("Admin", "password")]
    [InlineData("Admin", "Password ")]
    [InlineData("Claude", "Password")]
    [InlineData("Admin", "")]
    public async Task Any_other_account_or_password_is_refused(string user, string password)
    {
        var (result, _) = await TryAsync(user: user, password: password.Length == 0 ? "x" : password);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void AddDevLogin_replaces_the_account_service_and_carries_the_gates_it_was_given()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUserAccountService, UserAccountService>();
        services.AddDevLogin(enabled: true, isDevelopment: false);

        var options = Assert.IsType<DevLoginOptions>(services.Single(d => d.ServiceType == typeof(DevLoginOptions)).ImplementationInstance);
        Assert.False(options.IsDevelopment);
        Assert.Equal(typeof(DevLoginAccountService), services.Last(d => d.ServiceType == typeof(IUserAccountService)).ImplementationType);
        Assert.Single(services, d => d.ServiceType == typeof(IUserAccountService));
    }

    [Fact]
    public async Task There_is_no_stored_password_to_change()
    {
        var service = new DevLoginAccountService(new DevLoginOptions(true, true), new Origin(true), new FakeJwtTokenService());

        Assert.Equal(ChangePasswordResult.NotFound, await service.ChangePasswordAsync(Guid.NewGuid(), "a", "b"));
    }
}
