using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Adventures.Security;

/// <summary>Registers the shared JWT issuing/validation building blocks for a host application.</summary>
public static class JwtServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="JwtTokenOptions"/> from configuration, registers <see cref="IJwtTokenService"/>,
    /// and configures JWT bearer authentication so incoming tokens issued by <see cref="IJwtTokenService"/>
    /// are validated using the same shared settings.
    /// </summary>
    public static IServiceCollection AddSharedJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = JwtTokenOptions.SectionName)
    {
        services.AddOptions<JwtTokenOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations();

        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();

        var jwtOptions = configuration.GetSection(sectionName).Get<JwtTokenOptions>() ?? new JwtTokenOptions();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearerOptions =>
            {
                bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization();

        return services;
    }

    /// <summary>The naming convention for a scope-based authorization policy, e.g. "Scope:mcp.postgres.query".</summary>
    public static string ScopePolicyName(string scope) => $"Scope:{scope}";

    /// <summary>
    /// Registers the <see cref="ScopeAuthorizationHandler"/> and adds an authorization policy (named via
    /// <see cref="ScopePolicyName"/>) for each of the given scopes, so endpoints can be protected with
    /// <c>[Authorize(Policy = JwtServiceCollectionExtensions.ScopePolicyName("mcp.postgres.query"))]</c>.
    /// </summary>
    public static IServiceCollection AddScopeAuthorization(this IServiceCollection services, params string[] scopes)
    {
        services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();

        foreach (var scope in scopes)
        {
            var policyName = ScopePolicyName(scope);
            services.AddAuthorization(options =>
                options.AddPolicy(policyName, policy => policy.Requirements.Add(new ScopeRequirement(scope))));
        }

        return services;
    }
}
