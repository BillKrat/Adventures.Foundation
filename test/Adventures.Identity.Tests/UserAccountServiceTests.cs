using Adventures.Data;
using Xunit;

namespace Adventures.Identity.Tests;

public class UserAccountServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static Entity SampleUserEntity(string status = "active", string[]? roles = null) => new(
        Id: UserId,
        Tenant: "acme.com",
        Org: "my-blog",
        EntityType: "user",
        StandardFieldsJson: $$"""
            {"username":"admin","email":"admin@acme.com","display_name":"Admin","status":"{{status}}","roles":{{System.Text.Json.JsonSerializer.Serialize(roles ?? ["TenantAdmin"])}}}
            """,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow,
        RowVersion: 1);

    private static UserCredential SampleCredential(bool mustChangePassword = false, DateTimeOffset? lockedUntil = null) => new(
        UserId: UserId,
        PasswordHash: "210000:salt:hash",
        PasswordAlgorithm: "pbkdf2-sha256",
        MustChangePassword: mustChangePassword,
        FailedLoginAttempts: 0,
        LockedUntil: lockedUntil,
        LastLoginAt: null);

    private static (UserAccountService Service, FakeEntityRepository Entities, FakeUserCredentialStore Credentials, FakePasswordHasher Hasher, FakeJwtTokenService Tokens) CreateSut()
    {
        var entities = new FakeEntityRepository();
        var credentials = new FakeUserCredentialStore();
        var hasher = new FakePasswordHasher();
        var tokens = new FakeJwtTokenService();
        return (new UserAccountService(entities, credentials, hasher, tokens), entities, credentials, hasher, tokens);
    }

    [Fact]
    public async Task LoginAsync_ReturnsInvalidCredentials_WhenUserNotFound()
    {
        var (service, entities, _, _, _) = CreateSut();
        entities.EntityToReturn = null;

        var result = await service.LoginAsync("acme.com", "nobody", "whatever");

        Assert.False(result.Succeeded);
        Assert.Equal(LoginFailureReason.InvalidCredentials, result.FailureReason);
    }

    [Fact]
    public async Task LoginAsync_PassesTenantAndUsernameThroughToRepository()
    {
        var (service, entities, _, _, _) = CreateSut();
        entities.EntityToReturn = null;

        await service.LoginAsync("acme.com", "admin", "whatever");

        Assert.Single(entities.FindCalls);
        Assert.Equal(("acme.com", "user", "username", "admin"), entities.FindCalls[0]);
    }

    [Fact]
    public async Task LoginAsync_ReturnsInvalidCredentials_WhenNoCredentialRowExists()
    {
        var (service, entities, credentials, _, _) = CreateSut();
        entities.EntityToReturn = SampleUserEntity();
        credentials.CredentialToReturn = null;

        var result = await service.LoginAsync("acme.com", "admin", "Password");

        Assert.False(result.Succeeded);
        Assert.Equal(LoginFailureReason.InvalidCredentials, result.FailureReason);
    }

    [Fact]
    public async Task LoginAsync_ReturnsAccountLocked_WhenLockedUntilInFuture()
    {
        var (service, entities, credentials, _, _) = CreateSut();
        entities.EntityToReturn = SampleUserEntity();
        credentials.CredentialToReturn = SampleCredential(lockedUntil: DateTimeOffset.UtcNow.AddMinutes(5));

        var result = await service.LoginAsync("acme.com", "admin", "Password");

        Assert.False(result.Succeeded);
        Assert.Equal(LoginFailureReason.AccountLocked, result.FailureReason);
    }

    [Fact]
    public async Task LoginAsync_AllowsLogin_WhenLockedUntilInPast()
    {
        var (service, entities, credentials, hasher, _) = CreateSut();
        entities.EntityToReturn = SampleUserEntity();
        credentials.CredentialToReturn = SampleCredential(lockedUntil: DateTimeOffset.UtcNow.AddMinutes(-5));
        hasher.VerifyResult = true;

        var result = await service.LoginAsync("acme.com", "admin", "Password");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task LoginAsync_ReturnsAccountDisabled_WhenStatusDisabled()
    {
        var (service, entities, credentials, _, _) = CreateSut();
        entities.EntityToReturn = SampleUserEntity(status: "disabled");
        credentials.CredentialToReturn = SampleCredential();

        var result = await service.LoginAsync("acme.com", "admin", "Password");

        Assert.False(result.Succeeded);
        Assert.Equal(LoginFailureReason.AccountDisabled, result.FailureReason);
    }

    [Fact]
    public async Task LoginAsync_ReturnsInvalidCredentials_AndRecordsFailedLogin_WhenPasswordWrong()
    {
        var (service, entities, credentials, hasher, _) = CreateSut();
        entities.EntityToReturn = SampleUserEntity();
        credentials.CredentialToReturn = SampleCredential();
        hasher.VerifyResult = false;

        var result = await service.LoginAsync("acme.com", "admin", "wrong-password");

        Assert.False(result.Succeeded);
        Assert.Equal(LoginFailureReason.InvalidCredentials, result.FailureReason);
        Assert.Single(credentials.FailedLoginCalls);
        Assert.Equal(UserId, credentials.FailedLoginCalls[0].UserId);
        Assert.Empty(credentials.SuccessfulLoginCalls);
    }

    [Fact]
    public async Task LoginAsync_ReturnsSuccessWithToken_AndRecordsSuccessfulLogin_WhenPasswordCorrect()
    {
        var (service, entities, credentials, hasher, tokens) = CreateSut();
        entities.EntityToReturn = SampleUserEntity();
        credentials.CredentialToReturn = SampleCredential();
        hasher.VerifyResult = true;
        tokens.TokenToReturn = new Adventures.Security.IssuedToken("real-token", DateTimeOffset.UtcNow.AddMinutes(30));

        var result = await service.LoginAsync("acme.com", "admin", "Password");

        Assert.True(result.Succeeded);
        Assert.Equal("real-token", result.AccessToken);
        Assert.Single(credentials.SuccessfulLoginCalls);
        Assert.Empty(credentials.FailedLoginCalls);
    }

    [Fact]
    public async Task LoginAsync_IssuesTokenWithUserEntityIdAsSubject_NeverThePassword()
    {
        var (service, entities, credentials, hasher, tokens) = CreateSut();
        entities.EntityToReturn = SampleUserEntity();
        credentials.CredentialToReturn = SampleCredential();
        hasher.VerifyResult = true;

        await service.LoginAsync("acme.com", "admin", "Password");

        Assert.Single(tokens.IssueTokenCalls);
        Assert.Equal(UserId.ToString(), tokens.IssueTokenCalls[0].Subject);
    }

    [Theory]
    [InlineData(true, "active")]
    [InlineData(false, "must_change_password")]
    public async Task LoginAsync_SurfacesMustChangePassword_FromEitherCredentialFlagOrEntityStatus(bool credentialFlag, string status)
    {
        var (service, entities, credentials, hasher, _) = CreateSut();
        entities.EntityToReturn = SampleUserEntity(status: status);
        credentials.CredentialToReturn = SampleCredential(mustChangePassword: credentialFlag);
        hasher.VerifyResult = true;

        var result = await service.LoginAsync("acme.com", "admin", "Password");

        Assert.True(result.Succeeded);
        Assert.True(result.MustChangePassword);
    }

    [Theory]
    [InlineData("", "admin", "Password")]
    [InlineData("acme.com", "", "Password")]
    [InlineData("acme.com", "admin", "")]
    public async Task LoginAsync_ThrowsForBlankArguments(string tenant, string username, string password)
    {
        var (service, _, _, _, _) = CreateSut();

        await Assert.ThrowsAsync<ArgumentException>(() => service.LoginAsync(tenant, username, password));
    }
}
