using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Adventures.Security;

/// <summary>The request user as the current <see cref="ICurrentPrincipal"/>; an empty principal outside a request.</summary>
public sealed class HttpContextPrincipal(IHttpContextAccessor accessor) : ICurrentPrincipal
{
    public ClaimsPrincipal Principal => accessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
}
