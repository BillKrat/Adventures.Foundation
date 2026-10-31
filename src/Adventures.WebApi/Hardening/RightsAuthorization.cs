using System.Security.Claims;
using Adventures.Entities;
using Adventures.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.WebApi;

/// <summary>
/// A further condition on a right, beyond holding it (for example a plan that includes the feature). None is registered today; the seam is
/// here so entitlements can be added later without touching a single route. Every registered condition must allow.
/// </summary>
public interface IRightCondition
{
    Task<bool> AllowsAsync(ClaimsPrincipal user, string right, CancellationToken cancellationToken = default);
}

/// <summary>Answers "may this caller do this?" for routes, menu entries and views. One instance per request, so the rights are worked out once.</summary>
public interface IRightsAuthorizer
{
    /// <summary>Every right the caller holds (empty when not signed in).</summary>
    Task<IReadOnlySet<string>> RightsOfAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>True when the caller holds the right and every registered <see cref="IRightCondition"/> allows it.</summary>
    Task<bool> IsAllowedAsync(ClaimsPrincipal user, string right, CancellationToken cancellationToken = default);
}

/// <summary>Resolves the caller rights through <see cref="IRightsResolver"/> (register it scoped: the answer is kept for the request).</summary>
public sealed class RightsAuthorizer(IRightsResolver resolver, IEnumerable<IRightCondition> conditions) : IRightsAuthorizer
{
    private IReadOnlySet<string>? _rights;
    private string? _forUser;

    public async Task<IReadOnlySet<string>> RightsOfAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Identity?.IsAuthenticated != true)
        {
            return new HashSet<string>();
        }

        var name = user.Identity.Name ?? string.Empty;
        if (_rights is null || !string.Equals(_forUser, name, StringComparison.Ordinal))
        {
            var roles = user.FindAll(claim => claim.Type is WellKnownClaims.Roles or ClaimTypes.Role)
                .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            _rights = await resolver.ResolveAsync(name, roles, cancellationToken).ConfigureAwait(false);
            _forUser = name;
        }

        return _rights;
    }

    public async Task<bool> IsAllowedAsync(ClaimsPrincipal user, string right, CancellationToken cancellationToken = default)
    {
        if (!(await RightsOfAsync(user, cancellationToken).ConfigureAwait(false)).Contains(right))
        {
            return false;
        }

        foreach (var condition in conditions)
        {
            if (!await condition.AllowsAsync(user, right, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Shared answer for the route filters: 401 when not signed in, 403 when the right is missing (or none was named), nothing when allowed.</summary>
internal static class RightsGate
{
    public static async Task CheckAsync(AuthorizationFilterContext context, string? right)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var authorizer = context.HttpContext.RequestServices.GetRequiredService<IRightsAuthorizer>();
        if (string.IsNullOrEmpty(right) || !await authorizer.IsAllowedAsync(context.HttpContext.User, right, context.HttpContext.RequestAborted).ConfigureAwait(false))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
        }
    }
}

/// <summary>The right a route (or one action) needs. On an action it replaces the controller <see cref="AdminRightsAttribute"/> for that action.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireRightAttribute(string right) : Attribute, IAsyncAuthorizationFilter
{
    public string Right { get; } = right;

    public Task OnAuthorizationAsync(AuthorizationFilterContext context) => RightsGate.CheckAsync(context, Right);
}

/// <summary>
/// The rights an entity controller standard actions need, one per verb: GET/HEAD read, POST create, PUT/PATCH update, DELETE delete.
/// A verb with no right named is refused, so a route cannot be left open by forgetting a name. An action that carries its own
/// <see cref="RequireRightAttribute"/> (publish, approve) is decided by that instead.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class AdminRightsAttribute : Attribute, IAsyncAuthorizationFilter
{
    public string? Read { get; init; }

    public string? Create { get; init; }

    public string? Update { get; init; }

    public string? Delete { get; init; }

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is ControllerActionDescriptor { MethodInfo: { } method }
            && method.GetCustomAttributes(typeof(RequireRightAttribute), inherit: true).Length > 0)
        {
            return Task.CompletedTask;
        }

        var right = context.HttpContext.Request.Method.ToUpperInvariant() switch
        {
            "GET" or "HEAD" => Read,
            "POST" => Create,
            "PUT" or "PATCH" => Update,
            "DELETE" => Delete,
            _ => null,
        };
        return RightsGate.CheckAsync(context, right);
    }
}

public static class RightsAuthorizationExtensions
{
    /// <summary>Registers the per-request rights authorizer (the host also registers <see cref="IRightsResolver"/>).</summary>
    public static IServiceCollection AddRightsAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IRightsAuthorizer, RightsAuthorizer>();
        return services;
    }
}
