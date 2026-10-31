using System.Security.Claims;

namespace Adventures.Security;

/// <summary>
/// Who is acting right now, as a <see cref="ClaimsPrincipal"/> (a BCL type, so it needs no web framework). A web host supplies the signed-in
/// request user (<c>Adventures.Security.AspNetCore</c>), a desktop or console host supplies the person who signed in
/// (<see cref="AmbientPrincipal"/>). Code that needs the acting user asks this, never an HTTP context.
/// </summary>
public interface ICurrentPrincipal
{
    /// <summary>The acting principal; an unauthenticated (empty) principal when nobody is signed in, never null.</summary>
    ClaimsPrincipal Principal { get; }
}

/// <summary>A settable <see cref="ICurrentPrincipal"/> for hosts that have one signed-in person at a time (desktop apps, tools, tests).</summary>
public sealed class AmbientPrincipal : ICurrentPrincipal
{
    private ClaimsPrincipal _principal = new(new ClaimsIdentity());

    public ClaimsPrincipal Principal => _principal;

    /// <summary>Records who signed in. Passing null signs out.</summary>
    public void SignIn(ClaimsPrincipal? principal) => _principal = principal ?? new ClaimsPrincipal(new ClaimsIdentity());
}
