using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Adventures.Identity.WebApi;

public static class IdentityWebApiExtensions
{
    /// <summary>
    /// Makes MVC find the identity controllers in this library (a controller in a referenced library is not discovered by default).
    /// Call it on the builder returned by <c>AddControllers()</c>, together with the identity services behind them.
    /// </summary>
    public static IMvcBuilder AddIdentityControllers(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddApplicationPart(typeof(IdentityWebApiExtensions).Assembly);
    }

    /// <summary>Registers forgot-my-password: the directory over the tenant's people and the reset service. Needs <c>AddUserIdentity</c>, the verification store and an <c>IEmailSender</c> as well.</summary>
    public static IServiceCollection AddPasswordReset(this IServiceCollection services)
    {
        services.AddScoped<IResetDirectory, UserBllResetDirectory>();
        services.AddScoped<PasswordResetService>();
        // hosts without the account store (Legacy, developer sign-in) can not reset a password by mail: every reset then ends as an invalid link
        services.TryAddScoped<IPasswordResetter, NoPasswordResetter>();
        return services;
    }
}
