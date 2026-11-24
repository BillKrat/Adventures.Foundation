using Adventures.Data;
using Xunit;

namespace Adventures.Identity.Tests;

/// <summary>Setting a new password without the old one (only after a mailed single-use link): replaces the hash, clears lockout and the must-change flags, refuses a weak password, a missing or a disabled account.</summary>
public class PasswordResetterTests
{
    private const string NewPassword = "a-much-longer-passphrase";
    private static readonly Guid UserId = Guid.NewGuid();

    private static Entity UserEntity(string status) => new(
        Id: UserId, Tenant: "acme.com", Org: "my-blog", EntityType: "user",
        StandardFieldsJson: $$"""{"username":"admin","email":"admin@acme.com","status":"{{status}}","roles":["Administrator"]}""",
        CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow, RowVersion: 1);

    private static (UserAccountService Service, FakeEntityRepository Entities, FakeUserCredentialStore Credentials) CreateSut(Entity? entity)
    {
        var entities = new FakeEntityRepository { EntityToReturn = entity };
        var credentials = new FakeUserCredentialStore();
        return (new UserAccountService(entities, credentials, new FakePasswordHasher(), new FakeJwtTokenService()), entities, credentials);
    }

    [Fact]
    public async Task ReplacesThePassword_ClearsTheLockoutAndDoesNotForceAnotherChange()
    {
        var (service, entities, credentials) = CreateSut(UserEntity("must_change_password"));

        var outcome = await service.ResetAsync("acme.com", "admin", NewPassword);

        Assert.Equal(ResetOutcome.Reset, outcome);
        var call = Assert.Single(credentials.CreateOrResetCalls);
        Assert.Equal((UserId, "hashed:" + NewPassword, "pbkdf2-sha256", false), call);
        Assert.Contains("\"status\":\"active\"", Assert.Single(entities.UpdateCalls).StandardFieldsJson);
    }

    [Fact]
    public async Task ARejectedPasswordChangesNothing()
    {
        var (service, _, credentials) = CreateSut(UserEntity("active"));

        Assert.Equal(ResetOutcome.Rejected, await service.ResetAsync("acme.com", "admin", "short"));
        Assert.Empty(credentials.CreateOrResetCalls);
    }

    [Fact]
    public async Task AMissingAccountChangesNothing()
    {
        var (service, _, credentials) = CreateSut(null);

        Assert.Equal(ResetOutcome.NotFound, await service.ResetAsync("acme.com", "nobody", NewPassword));
        Assert.Empty(credentials.CreateOrResetCalls);
    }

    [Fact]
    public async Task ADisabledAccountIsNotResetByMail()
    {
        var (service, _, credentials) = CreateSut(UserEntity("disabled"));

        Assert.Equal(ResetOutcome.NotFound, await service.ResetAsync("acme.com", "admin", NewPassword));
        Assert.Empty(credentials.CreateOrResetCalls);
    }
}
