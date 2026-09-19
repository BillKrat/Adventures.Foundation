using System.IdentityModel.Tokens.Jwt;
using Adventures.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Adventures.Security.Tests;

public class JwtServiceCollectionExtensionsTests
{
    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience",
                ["Jwt:SigningKey"] = "0123456789abcdef0123456789abcdef",
                ["Jwt:AccessTokenLifetime"] = "00:15:00",
            })
            .Build();

    [Fact]
    public void AddSharedJwtAuthentication_RegistersJwtTokenServiceAndBindsOptions()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration();

        services.AddSharedJwtAuthentication(configuration);
        var provider = services.BuildServiceProvider();

        var tokenService = provider.GetService<IJwtTokenService>();
        var options = provider.GetRequiredService<IOptions<JwtTokenOptions>>().Value;

        Assert.NotNull(tokenService);
        Assert.Equal("TestIssuer", options.Issuer);
        Assert.Equal("TestAudience", options.Audience);
        Assert.Equal(TimeSpan.FromMinutes(15), options.AccessTokenLifetime);
    }

    [Fact]
    public void AddSharedJwtAuthentication_RegistersJwtBearerAuthenticationScheme()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration();

        services.AddSharedJwtAuthentication(configuration);
        var provider = services.BuildServiceProvider();

        var schemeProvider = provider.GetRequiredService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>();
        var scheme = schemeProvider.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme).GetAwaiter().GetResult();

        Assert.NotNull(scheme);
    }

    [Fact]
    public void IssuedToken_ValidatesSuccessfully_WithConfiguredTokenValidationParameters()
    {
        var services = new ServiceCollection();
        var configuration = BuildConfiguration();
        services.AddSharedJwtAuthentication(configuration);
        var provider = services.BuildServiceProvider();

        var tokenService = provider.GetRequiredService<IJwtTokenService>();
        var issued = tokenService.IssueToken("erin");

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "TestIssuer",
            ValidateAudience = true,
            ValidAudience = "TestAudience",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef")),
            ValidateLifetime = true,
        };

        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(issued.AccessToken, validationParameters, out var validatedToken);

        Assert.NotNull(principal);
        Assert.NotNull(validatedToken);
        var subjectClaim = principal.FindFirst(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        Assert.Equal("erin", subjectClaim?.Value);
    }
}
