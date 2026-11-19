using System.Globalization;
using System.Xml.Linq;

namespace Adventures.Blog;

/// <summary>
/// The RSS 2.0 feed of the latest published posts. Built with an XML writer, never by joining strings, so a title with markup or an ampersand cannot
/// break the document or inject an element. It names no author (an e-mail or a person is not published by a feed) and no id, links to the current
/// post address on the site, and skips a draft even if the caller passes one.
/// </summary>
public static class RssFeed
{
    public const string ContentType = "application/rss+xml; charset=utf-8";

    public static string Build(PublicSiteSettings site, string siteUrl, IEnumerable<PublicPostSummary> posts)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(posts);
        var root = siteUrl.TrimEnd('/');
        var channel = new XElement("channel",
            new XElement("title", site.Title),
            new XElement("link", root),
            new XElement("description", site.Tagline));

        foreach (var post in posts.Where(p => !p.IsDraft))
        {
            var link = $"{root}/post/{Uri.EscapeDataString(post.Slug)}";
            var item = new XElement("item",
                new XElement("title", post.Title),
                new XElement("link", link),
                new XElement("guid", new XAttribute("isPermaLink", "true"), link));
            if (!string.IsNullOrWhiteSpace(post.Description))
            {
                item.Add(new XElement("description", post.Description));
            }

            if (DateTimeOffset.TryParse(post.DatePublished, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var published))
            {
                item.Add(new XElement("pubDate", published.UtcDateTime.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture)));
            }

            foreach (var category in post.Categories)
            {
                item.Add(new XElement("category", category));
            }

            channel.Add(item);
        }

        var document = new XElement("rss", new XAttribute("version", "2.0"), channel);
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + document.ToString(SaveOptions.DisableFormatting);
    }
}
