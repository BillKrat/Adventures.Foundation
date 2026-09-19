using Adventures.Data;
using Xunit;

namespace Adventures.Identity.Tests;

public class UserAccountTests
{
    private static Entity MakeEntity(string standardFieldsJson, string entityType = "user") => new(
        Id: Guid.NewGuid(),
        Tenant: "acme.com",
        Org: "my-blog",
        EntityType: entityType,
        StandardFieldsJson: standardFieldsJson,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow,
        RowVersion: 1);

    [Fact]
    public void FromEntity_ParsesAllStandardFields()
    {
        var entity = MakeEntity("""
            {"username":"admin","email":"admin@acme.com","display_name":"Admin","status":"active","roles":["TenantAdmin","Author"]}
            """);

        var account = UserAccount.FromEntity(entity);

        Assert.Equal("admin", account.Username);
        Assert.Equal("admin@acme.com", account.Email);
        Assert.Equal("Admin", account.DisplayName);
        Assert.Equal(UserStatus.Active, account.Status);
        Assert.Equal(["TenantAdmin", "Author"], account.Roles);
    }

    [Theory]
    [InlineData("active", UserStatus.Active)]
    [InlineData("disabled", UserStatus.Disabled)]
    [InlineData("must_change_password", UserStatus.MustChangePassword)]
    [InlineData("DISABLED", UserStatus.Disabled)]
    public void FromEntity_ParsesStatusCaseInsensitively(string status, UserStatus expected)
    {
        var entity = MakeEntity($$"""{"username":"admin","status":"{{status}}"}""");

        var account = UserAccount.FromEntity(entity);

        Assert.Equal(expected, account.Status);
    }

    [Fact]
    public void FromEntity_DefaultsMissingOptionalFields()
    {
        var entity = MakeEntity("""{"username":"admin"}""");

        var account = UserAccount.FromEntity(entity);

        Assert.Equal(string.Empty, account.Email);
        Assert.Equal(string.Empty, account.DisplayName);
        Assert.Equal(UserStatus.Active, account.Status);
        Assert.Empty(account.Roles);
    }

    [Fact]
    public void FromEntity_ThrowsWhenUsernameMissing()
    {
        var entity = MakeEntity("""{"email":"admin@acme.com"}""");

        Assert.Throws<FormatException>(() => UserAccount.FromEntity(entity));
    }

    [Fact]
    public void FromEntity_ThrowsWhenEntityTypeIsNotUser()
    {
        var entity = MakeEntity("""{"username":"admin"}""", entityType: "post");

        Assert.Throws<FormatException>(() => UserAccount.FromEntity(entity));
    }
}
