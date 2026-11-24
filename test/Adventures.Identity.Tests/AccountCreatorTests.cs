using System.Text.Json;
using Adventures.Data;
using Xunit;

namespace Adventures.Identity.Tests;

/// <summary>The first account of a fresh site: Admin with a known default password (as BlogEngine ships it), the all-rights sign-in role, and a flag that forces a new password at the first sign-in. Never overwrites an account that already exists.</summary>
public sealed class AccountCreatorTests
{
    private readonly FakeEntityRepository _entities = new();
    private readonly FakeUserCredentialStore _credentials = new();
    private readonly FakePasswordHasher _hasher = new();

    private IAccountCreator Creator() => new UserProvisioning(_entities, _credentials, _hasher);

    [Fact]
    public async Task ANewAccountGetsTheGivenPassword_TheGivenRoles_AndMustChangeItAtTheFirstSignIn()
    {
        var created = await Creator().CreateIfMissingAsync("photos.example.org", "Admin", "Admin", ["TenantAdmin"], "admin@photos.example.org", "Administrator");

        Assert.True(created);
        var entity = Assert.Single(_entities.CreateCalls);
        Assert.Equal(("photos.example.org", "user"), (entity.Tenant, entity.EntityType));
        using var json = JsonDocument.Parse(entity.StandardFieldsJson);
        Assert.Equal("Admin", json.RootElement.GetProperty("username").GetString());
        Assert.Equal("must_change_password", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("TenantAdmin", Assert.Single(json.RootElement.GetProperty("roles").EnumerateArray()).GetString());
        var credential = Assert.Single(_credentials.CreateOrResetCalls);
        Assert.Equal((entity.Id, "hashed:Admin", true), (credential.UserId, credential.Hash, credential.MustChange));
    }

    [Fact]
    public async Task AnExistingAccount_IsNeverTouched_SoARerunCannotResetAPassword()
    {
        _entities.EntityToReturn = new Entity(Guid.NewGuid(), "photos.example.org", "default", "user", """{"username":"Admin","roles":["TenantAdmin"]}""", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1);

        var created = await Creator().CreateIfMissingAsync("photos.example.org", "Admin", "Admin", ["TenantAdmin"], null, null);

        Assert.False(created);
        Assert.Empty(_entities.CreateCalls);
        Assert.Empty(_credentials.CreateOrResetCalls);
    }

    [Theory]
    [InlineData("", "Admin")]
    [InlineData("tenant", "")]
    [InlineData(" ", "x")]
    public async Task ATenantAndAUserNameAndAPassword_AreRequired(string tenant, string userName)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Creator().CreateIfMissingAsync(tenant, userName, "Admin", [], null, null));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Creator().CreateIfMissingAsync("t", "Admin", " ", [], null, null));
    }

    [Fact]
    public async Task TheSameNameInAnotherTenant_IsAnotherAccount()
    {
        // the fake repository answers per call, so a second tenant is simply asked for again
        await Creator().CreateIfMissingAsync("a.example.org", "Admin", "Admin", ["TenantAdmin"], null, null);
        await Creator().CreateIfMissingAsync("b.example.org", "Admin", "Admin", ["TenantAdmin"], null, null);

        Assert.Equal(["a.example.org", "b.example.org"], _entities.CreateCalls.Select(c => c.Tenant));
        Assert.Equal(2, _credentials.CreateOrResetCalls.Select(c => c.UserId).Distinct().Count());
        Assert.Equal(("a.example.org", "b.example.org"), (_entities.FindCalls[0].Tenant, _entities.FindCalls[1].Tenant));
    }
}
