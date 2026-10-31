using System.Security.Claims;
using Xunit;

namespace Adventures.Security.Tests;

public class AmbientPrincipalTests
{
    [Fact]
    public void Nobody_is_signed_in_until_someone_signs_in()
    {
        ICurrentPrincipal current = new AmbientPrincipal();

        Assert.False(current.Principal.Identity?.IsAuthenticated);
    }

    [Fact]
    public void SignIn_makes_the_person_the_acting_principal_and_null_signs_out()
    {
        var ambient = new AmbientPrincipal();
        ambient.SignIn(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Admin")], "test")));

        Assert.Equal("Admin", ambient.Principal.Identity?.Name);
        Assert.True(ambient.Principal.Identity?.IsAuthenticated);

        ambient.SignIn(null);
        Assert.False(ambient.Principal.Identity?.IsAuthenticated);
    }
}
