namespace Adventures.Client;

/// <summary>Where a client may send a password.</summary>
public static class ApiAddress
{
    /// <summary>The address itself when it is https, or plain http to this machine (development); otherwise an <see cref="ArgumentException"/>.</summary>
    public static Uri Require(Uri address, string? parameterName = null)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!address.IsAbsoluteUri || !(address.Scheme == Uri.UriSchemeHttps || (address.Scheme == Uri.UriSchemeHttp && address.IsLoopback)))
        {
            throw new ArgumentException("The API address must use https (plain http is allowed only for localhost).", parameterName);
        }

        return address;
    }
}
