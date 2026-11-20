using System.Security.Claims;
using Adventures.Identity;
using Adventures.Security;
using Microsoft.AspNetCore.Builder;

namespace Adventures.Identity.WebApi;

/// <summary>
/// Holds a person whose password must be changed to changing it. A token marked with <see cref="WellKnownClaims.PasswordChange"/> may call only the
/// sign-in and password endpoints, the person's own profile, the public site, the runtime banner and health; everything else under <c>/api</c> is refused with
/// 403 and <see cref="Code"/> so the client can send the person to the password form. The mark alone is not trusted for long: the store is asked
/// (<see cref="IPasswordChangeGate"/>), so the same token works the moment the password is changed. Unmarked tokens and anonymous callers are never
/// asked about, so ordinary requests cost nothing extra. Put it after authentication and before authorization.
/// Threat addressed: a seeded or administrator-set password working like any other.
/// </summary>
public sealed class PasswordChangeRequiredMiddleware(RequestDelegate next)
{
    /// <summary>The machine-readable reason in the 403 body.</summary>
    public const string Code = "password_change_required";

    private static readonly string[] AllowedPrefixes = ["/api/auth/", "/api/public/"];
    private static readonly string[] AllowedExact = ["/api/profile/me", "/api/runtime", "/api/health"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsMarked(context.User) || !IsGuarded(context.Request.Path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // Looked up only for a marked token, so a host mode with no user store (Legacy) is untouched; a marked token with no gate to ask is refused, never waved through.
        var gate = context.RequestServices.GetService<IPasswordChangeGate>();

        var subject = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? context.User.FindFirst("sub")?.Value;
        var pending = gate is null || !Guid.TryParse(subject, out var userId) || await gate.IsPendingAsync(userId, context.RequestAborted).ConfigureAwait(false);
        if (!pending)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(
            new { title = "You must change your password first.", status = StatusCodes.Status403Forbidden, code = Code },
            context.RequestAborted).ConfigureAwait(false);
    }

    private static bool IsMarked(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true && user.HasClaim(WellKnownClaims.PasswordChange, "true");

    private static bool IsGuarded(PathString path)
    {
        var value = path.Value ?? string.Empty;
        if (!value.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !AllowedPrefixes.Any(p => value.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            && !AllowedExact.Any(p => string.Equals(value.TrimEnd('/'), p, StringComparison.OrdinalIgnoreCase));
    }
}

public static class PasswordChangeRequiredExtensions
{
    /// <summary>Enforces the first-sign-in password change; call after <c>UseAuthentication</c> (and the tenant checks) and before <c>UseAuthorization</c>.</summary>
    public static IApplicationBuilder UsePasswordChangeRequired(this IApplicationBuilder app) => app.UseMiddleware<PasswordChangeRequiredMiddleware>();
}
