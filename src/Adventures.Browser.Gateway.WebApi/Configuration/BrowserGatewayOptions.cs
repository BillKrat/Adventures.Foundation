namespace Adventures.Browser.Gateway.WebApi;

/// <summary>The <c>BrowserGateway</c> settings of an installation. They name real sites, so they live on the server, never in a repository.</summary>
public sealed class BrowserGatewayOptions
{
    public const string Section = "BrowserGateway";

    /// <summary>The only sites this gateway ever calls.</summary>
    public List<BrowserSiteOptions> Sites { get; set; } = [];

    /// <summary>Origins of web pages that may call the gateway from a browser (the web client's address). Empty: same origin only.</summary>
    public List<string> AllowedOrigins { get; set; } = [];

    public int SignInsPerMinute { get; set; } = 10;

    public int ReadsPerMinute { get; set; } = 120;
}

public sealed class BrowserSiteOptions
{
    /// <summary>The name a person types to choose the site, for example its domain.</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>The address of that site's API.</summary>
    public string Api { get; set; } = string.Empty;
}
