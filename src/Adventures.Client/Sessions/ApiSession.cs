using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Adventures.Security;

namespace Adventures.Client;

/// <summary>
/// Who is using the desktop app. Signs in against the API's token endpoint, keeps the access token in memory only (never written, never logged),
/// puts it on the shared <see cref="HttpClient"/> as a bearer header and records the acting <see cref="ClaimsPrincipal"/> in the ambient
/// <see cref="ICurrentPrincipal"/> so client code asks the same question a server does. The server remains the authority: every call is checked there.
/// </summary>
public sealed class ApiSession(HttpClient http, AmbientPrincipal principal, string tokenPath = ApiSession.DefaultTokenPath)
{
    public const string DefaultTokenPath = "api/auth/token";

    private static readonly JwtSecurityTokenHandler Reader = new();

    public bool IsSignedIn => principal.Principal.Identity?.IsAuthenticated == true;

    public ClaimsPrincipal Principal => principal.Principal;

    /// <summary>True when the server wants the person to change the password before doing anything else.</summary>
    public bool MustChangePassword { get; private set; }

    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    /// <summary>Signs in. False when the server refuses the credentials (it does not say which part was wrong); an <see cref="ApiException"/> for any other failure.</summary>
    public async Task<bool> SignInAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        SignOut();
        using var response = await http.PostAsJsonAsync(tokenPath, new { userName, password }, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await ApiErrors.FromAsync(response, cancellationToken).ConfigureAwait(false);
        }

        var token = await response.Content.ReadFromJsonAsync<TokenReply>(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token?.AccessToken))
        {
            throw new ApiException(response.StatusCode, "The server did not return a sign-in token.");
        }

        var claims = Reader.ReadJwtToken(token.AccessToken).Claims;
        principal.SignIn(new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer")));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        MustChangePassword = token.MustChangePassword;
        ExpiresAtUtc = token.ExpiresAtUtc;
        return true;
    }

    public void SignOut()
    {
        http.DefaultRequestHeaders.Authorization = null;
        principal.SignIn(null);
        MustChangePassword = false;
        ExpiresAtUtc = null;
    }

    private sealed record TokenReply(string? AccessToken, DateTimeOffset? ExpiresAtUtc, bool MustChangePassword);
}
