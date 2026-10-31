using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Security.AspNetCore.Tests;

public class HttpContextPrincipalTests
{
    [Fact]
    public void The_request_user_is_the_current_principal_and_an_empty_one_outside_a_request()
    {
        var services = new ServiceCollection();
        services.AddCurrentPrincipal();
        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        var current = provider.GetRequiredService<ICurrentPrincipal>();

        Assert.False(current.Principal.Identity?.IsAuthenticated);

        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Claude")], "test")),
        };
        Assert.Equal("Claude", current.Principal.Identity?.Name);
    }
}
