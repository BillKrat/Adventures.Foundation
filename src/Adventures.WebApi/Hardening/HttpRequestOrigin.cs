using System.Net;
using Adventures.Security;
using Microsoft.AspNetCore.Http;

namespace Adventures.WebApi;

/// <summary>
/// Whether the request comes from this machine: the connection's remote address is a loopback address and no proxy forwarded it (a forwarding
/// header means the real caller is somewhere else, so it is never local). Used only to gate the Legacy-mode developer sign-in.
/// </summary>
public sealed class HttpRequestOrigin(IHttpContextAccessor accessor) : IRequestOrigin
{
    public bool IsLocal => IsLocalRequest(accessor.HttpContext);

    public static bool IsLocalRequest(HttpContext? context)
    {
        if (context?.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address))
        {
            return false;
        }

        var headers = context.Request.Headers;
        return !headers.ContainsKey("X-Forwarded-For") && !headers.ContainsKey("Forwarded") && !headers.ContainsKey("X-Real-IP");
    }
}
