using Adventures.Data;
using Xunit;

namespace Adventures.Identity.Tests;

public class ChangePasswordTests
{
    private const string NewPassword = "a-much-longer-passphrase";
    private static readonly Guid UserId = Guid.NewGuid();

    private static Entity UserEntity(string status) => new(
        Id: UserId,
        Tenant: "acme.com",
        Org: "my-blog",
        EntityType: "user",
        StandardFieldsJson: $$"""{"username":"admin","email":"admin@acme.com","status":"{{status}}","roles":["Administrator"]}""",
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow,
        RowVersion: 1);

    private static UserCredential Credential(DateTimeOffset? lockedUntil = null) => new(
        UserId, "210000:salt:hash", "pbkdf2-sha256", MustChangePassword: true, FailedLoginAttempts: 0, lockedUntil, LastLoginAt: null);

    private static (UserAccountService Service, FakeEntityRepository Entities, FakeUserCredentialStore Credentials, FakePasswordHasher Hasher) CreateSut(string status = "must_change_password")
    {
        var entities = new FakeEntityRepository { EntityToReturn = UserEntity(status) };
        var credentials = new FakeUserCredentialStore { CredentialToReturn = Credential() };
        var hasher = new FakePasswordHasher();
        return (new UserAccountService(entities, credentials, hasher, new FakeJwtTokenService()), entities, credentials, hasher);
    }

    [Fact]
    public async Task ChangePasswordAsync_StoresNewHashAndClearsEntityStatus_WhenCurrentPasswordIsRight()
    {
        var (service, entities, credentials, _) = CreateSut();

        var result = await service.ChangePasswordAsync(UserId, "Admin", NewPassword);

        Assert.Equal(ChangePasswordResult.Succeeded, result);
        var call = Assert.Single(credentials.SetPasswordCalls);
        Assert.Equal((UserId, "hashed:" + NewPassword, "pbkdf2-sha256"), call);
        var updated = Assert.Single(entities.UpdateCalls);
        Assert.Contains("\"status\":\"active\"", updated.StandardFieldsJson);
        Assert.Contains("\"Administrator\"", updated.StandardFieldsJson);
    }

    [Fact]
    public async Task ChangePasswordAsync_LeavesEntityAlone_WhenStatusIsAlreadyActive()
    {
        var (service, entities, _, _) = CreateSut(status: "active");

        var result = await service.ChangePasswordAsync(UserId, "Admin", NewPassword);

        Assert.Equal(ChangePasswordResult.Succeeded, result);
        Assert.Empty(entities.UpdateCalls);
    }

    [Fact]
    public async Task ChangePasswordAsync_RecordsFailedAttempt_WhenCurrentPasswordIsWrong()
    {
        var (service, _, credentials, hasher) = CreateSut();
        hasher.VerifyResult = false;

        var result = await service.ChangePasswordAsync(UserId, "wrong", NewPassword);

        Assert.Equal(ChangePasswordResult.InvalidCurrentPassword, result);
        Assert.Single(credentials.FailedLoginCalls);
        Assert.Empty(credentials.SetPasswordCalls);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("Admin")]
    public async Task ChangePasswordAsync_Rejects_AWeakNewPassword(string newPassword)
    {
        var (service, _, credentials, _) = CreateSut();

        var result = await service.ChangePasswordAsync(UserId, "Admin", newPassword);

        Assert.Equal(ChangePasswordResult.NewPasswordRejected, result);
        Assert.Empty(credentials.SetPasswordCalls);
    }

    [Fact]
    public async Task ChangePasswordAsync_Rejects_ReusingTheCurrentPassword()
    {
        var (service, _, credentials, _) = CreateSut();

        var result = await service.ChangePasswordAsync(UserId, NewPassword, NewPassword);

        Assert.Equal(ChangePasswordResult.NewPasswordRejected, result);
        Assert.Empty(credentials.SetPasswordCalls);
    }

    [Fact]
    public async Task ChangePasswordAsync_ReturnsAccountLocked_WithoutCheckingThePassword()
    {
        var (service, _, credentials, hasher) = CreateSut();
        credentials.CredentialToReturn = Credential(lockedUntil: DateTimeOffset.UtcNow.AddMinutes(10));

        var result = await service.ChangePasswordAsync(UserId, "Admin", NewPassword);

        Assert.Equal(ChangePasswordResult.AccountLocked, result);
        Assert.Empty(hasher.VerifyCalls);
    }

    [Fact]
    public async Task ChangePasswordAsync_ReturnsNotFound_WhenThereIsNoCredentialRow()
    {
        var (service, _, credentials, _) = CreateSut();
        credentials.CredentialToReturn = null;

        var result = await service.ChangePasswordAsync(UserId, "Admin", NewPassword);

        Assert.Equal(ChangePasswordResult.NotFound, result);
    }
}
