// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using System.Text.Json;
using Adventures.Data;
using Xunit;

namespace Adventures.Identity.Tests;

/// <summary>Providing a first password: the account is created without roles when it is missing, the password is generated, strong enough and must be changed, and only the hash is stored.</summary>
public sealed class UserProvisioningTests
{
    private readonly FakeEntityRepository _entities = new();
    private readonly FakeUserCredentialStore _credentials = new();
    private readonly FakePasswordHasher _hasher = new();

    private UserProvisioning Service() => new(_entities, _credentials, _hasher);

    [Fact]
    public async Task AMissingAccountIsCreatedWithNoRoles_AndGetsAGeneratedPasswordThatMustBeChanged()
    {
        var result = await Service().SetInitialPasswordAsync("global-webnet.com", "test-editor", "test-editor@example.test", "TEST Editor");

        Assert.True(result.AccountCreated);
        Assert.Equal("test-editor", result.Username);
        Assert.True(result.Password.Length >= PasswordPolicy.MinimumLength);
        var created = Assert.Single(_entities.CreateCalls);
        Assert.Equal("user", created.EntityType);
        using var json = JsonDocument.Parse(created.StandardFieldsJson);
        Assert.Equal("test-editor", json.RootElement.GetProperty("username").GetString());
        Assert.Equal("TEST Editor", json.RootElement.GetProperty("display_name").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("roles").GetArrayLength());
        var call = Assert.Single(_credentials.CreateOrResetCalls);
        Assert.Equal(created.Id, call.UserId);
        Assert.True(call.MustChange);
        Assert.Equal("hashed:" + result.Password, call.Hash);
    }

    [Fact]
    public async Task AnExistingAccountKeepsItsRolesAndOnlyGetsANewPassword()
    {
        var existing = new Entity(Guid.NewGuid(), "global-webnet.com", "default", "user", """{"username":"Admin","roles":["TenantAdmin"]}""", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1);
        _entities.EntityToReturn = existing;

        var result = await Service().SetInitialPasswordAsync("global-webnet.com", "Admin", null, null);

        Assert.False(result.AccountCreated);
        Assert.Empty(_entities.CreateCalls);
        Assert.Equal(existing.Id, Assert.Single(_credentials.CreateOrResetCalls).UserId);
    }

    [Fact]
    public async Task EveryCallGivesADifferentPassword_WithoutLookAlikeCharacters()
    {
        var service = Service();

        var first = await service.SetInitialPasswordAsync("t", "a", null, null);
        var second = await service.SetInitialPasswordAsync("t", "a", null, null);

        Assert.NotEqual(first.Password, second.Password);
        Assert.DoesNotContain(first.Password, c => "0OIl1".Contains(c));
    }
}
