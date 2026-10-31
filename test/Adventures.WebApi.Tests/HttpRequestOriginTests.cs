using System.Net;
using Adventures.WebApi;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Adventures.WebApi.Tests;

public class HttpRequestOriginTests
{
    private static HttpContext Context(string? remote, params (string Name, string Value)[] headers)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remote is null ? null : IPAddress.Parse(remote);
        foreach (var (name, value) in headers)
        {
            context.Request.Headers[name] = value;
        }

        return context;
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void A_loopback_connection_with_no_forwarding_is_local(string address) =>
        Assert.True(HttpRequestOrigin.IsLocalRequest(Context(address)));

    [Theory]
    [InlineData("203.0.113.9")]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.20")]
    public void Any_other_address_is_not_local(string address) =>
        Assert.False(HttpRequestOrigin.IsLocalRequest(Context(address)));

    [Theory]
    [InlineData("X-Forwarded-For")]
    [InlineData("Forwarded")]
    [InlineData("X-Real-IP")]
    public void A_forwarded_request_is_never_local_even_from_a_loopback_proxy(string header) =>
        Assert.False(HttpRequestOrigin.IsLocalRequest(Context("127.0.0.1", (header, "203.0.113.9"))));

    [Fact]
    public void No_connection_address_or_no_request_is_not_local()
    {
        Assert.False(HttpRequestOrigin.IsLocalRequest(Context(null)));
        Assert.False(HttpRequestOrigin.IsLocalRequest(null));
    }
}
