using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Adventures.Common.Exceptions;
using Adventures.MEF;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Adventures.Blog;

/// <summary>The E-mail plug-in: the SMTP settings an owner keeps on its Customize screen (the password is an encrypted, write-only secret) and the rules each value must keep.</summary>
public static partial class MailPlugin
{
    public const string Key = "mail";

    public const string FromAddress = "FromAddress";
    public const string SmtpHost = "SmtpHost";
    public const string SmtpPort = "SmtpPort";
    public const string UserName = "UserName";
    public const string Password = "Password";
    public const string EnableSsl = "EnableSsl";
    public const string SubjectPrefix = "SubjectPrefix";
    public const string NotifyOnComment = "NotifyOnComment";

    /// <summary>The ports a mail server listens on. Nothing else can be chosen, so the test button cannot be used to probe other services.</summary>
    public static readonly IReadOnlyList<int> AllowedPorts = [25, 465, 587, 2525];

    public static readonly PluginDefinition Definition = new(
        Key,
        "E-mail",
        "Sends mail from the site: a test message to check the settings and, if you turn it on, a note to you when a visitor comment arrives. Nothing is sent until the sender address and the mail server are set.",
        "1.0.0",
        6,
        [
            new PluginSettingDefinition(FromAddress, "Sender address", "The address mail is sent from; notices to you are sent to it too.", PluginSettingKind.Text, string.Empty, 254, ValidateAddress),
            new PluginSettingDefinition(SmtpHost, "Mail server", "The name of your SMTP server, for example mail.example.org (no port and no http://).", PluginSettingKind.Text, string.Empty, 253, ValidateHost),
            new PluginSettingDefinition(SmtpPort, "Port", "25, 465, 587 or 2525 (587 is the usual one).", PluginSettingKind.Number, "587", 5, ValidatePort),
            new PluginSettingDefinition(UserName, "User name", "The sign-in name for the mail server (leave empty for a server that needs none).", PluginSettingKind.Text, string.Empty, 200),
            new PluginSettingDefinition(Password, "Password", "The mail server password. It is stored encrypted and never shown again; type a new one to replace it, or use Clear.", PluginSettingKind.Secret, string.Empty, 200),
            new PluginSettingDefinition(EnableSsl, "Use SSL", "Encrypt the connection to the mail server (recommended).", PluginSettingKind.Bool, "true"),
            new PluginSettingDefinition(SubjectPrefix, "Subject prefix", "Put in front of every subject, for example [My blog] .", PluginSettingKind.Text, string.Empty, 50, ValidatePrefix),
            new PluginSettingDefinition(NotifyOnComment, "Tell me about new comments", "Send a note to the sender address when a visitor comment arrives.", PluginSettingKind.Bool, "false"),
        ]);

    [GeneratedRegex(@"^[^\s@,;<>""]{1,64}@[^\s@,;<>""]+\.[^\s@,;<>""]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AddressPattern();

    [GeneratedRegex("^[A-Za-z0-9]([A-Za-z0-9.-]{0,251}[A-Za-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex HostPattern();

    /// <summary>Null when the value is empty or exactly one plain address (no line break, no list, no display name).</summary>
    public static string? ValidateAddress(string value) =>
        value.Length == 0 || (value.Length <= 254 && AddressPattern().IsMatch(value)) ? null : "Give one e-mail address, such as owner@example.org.";

    public static string? ValidateHost(string value) =>
        value.Length == 0 || HostPattern().IsMatch(value) ? null : "Give the server name only, such as mail.example.org (no port, no http://, no spaces).";

    public static string? ValidatePort(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && AllowedPorts.Contains(port)
            ? null
            : $"The port must be one of {string.Join(", ", AllowedPorts)}.";

    public static string? ValidatePrefix(string value) =>
        value.Any(char.IsControl) ? "The prefix must be one line." : null;
}

/// <summary>The mail settings as they are used to send.</summary>
public sealed record MailConfig(string FromAddress, string Host, int Port, string UserName, string Password, bool EnableSsl, string SubjectPrefix, bool NotifyOnComment)
{
    public bool HasServer => Host.Length > 0;

    public bool HasSender => FromAddress.Length > 0;

    public static MailConfig From(IReadOnlyDictionary<string, string> values)
    {
        string Text(string name) => values.TryGetValue(name, out var v) ? v.Trim() : string.Empty;
        bool Flag(string name, bool fallback) => values.TryGetValue(name, out var v) ? !string.Equals(v.Trim(), "false", StringComparison.OrdinalIgnoreCase) : fallback;
        var port = int.TryParse(Text(MailPlugin.SmtpPort), NumberStyles.None, CultureInfo.InvariantCulture, out var p) && MailPlugin.AllowedPorts.Contains(p) ? p : 587;
        return new MailConfig(
            Text(MailPlugin.FromAddress), Text(MailPlugin.SmtpHost), port, Text(MailPlugin.UserName),
            values.TryGetValue(MailPlugin.Password, out var password) ? password : string.Empty,
            Flag(MailPlugin.EnableSsl, true), values.TryGetValue(MailPlugin.SubjectPrefix, out var prefix) ? prefix : string.Empty,
            values.TryGetValue(MailPlugin.NotifyOnComment, out var notify) && string.Equals(notify.Trim(), "true", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>One plain-text message: a recipient, a one-line subject and a body.</summary>
public sealed record MailMessageData(string To, string Subject, string Body);

/// <summary>Where a message actually leaves for the mail server (a seam, so the rules are tested without a server).</summary>
public interface IMailTransport
{
    Task SendAsync(MailConfig config, MailMessageData message, CancellationToken cancellationToken = default);
}

/// <summary>Operator choices for mail (configuration, not the Customize screen).</summary>
public sealed class MailOptions
{
    /// <summary>Allows a mail server on a private network (a company relay). Off by default: the site must not be aimed at its own network from a form.</summary>
    public bool AllowPrivateHosts { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>Refuses a mail server that resolves to a loopback, private, link-local or otherwise non-public address.</summary>
public static class MailHostGuard
{
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] is 0 or 10 or 127 or >= 224
                || (b[0] == 100 && b[1] is >= 64 and <= 127)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] is >= 16 and <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                || (b[0] == 198 && b[1] is 18 or 19));
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(IPAddress.IsLoopback(address) || address.Equals(IPAddress.IPv6None) || address.Equals(IPAddress.IPv6Any)
                || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (b[0] & 0xFE) == 0xFC);
        }

        return false;
    }

    public static async Task EnsureAllowedAsync(string host, MailOptions options, CancellationToken cancellationToken = default)
    {
        if (options.AllowPrivateHosts)
        {
            return;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            throw new BusinessRuleException("The mail server name could not be found. Check the spelling.");
        }

        if (addresses.Length == 0 || addresses.Any(a => !IsPublic(a)))
        {
            throw new BusinessRuleException("The mail server name points to a private or local address, which is not allowed.");
        }
    }
}

/// <summary>Sends through SMTP (System.Net.Mail). Plain text only; the server name is checked by <see cref="MailHostGuard"/> first.</summary>
public sealed class SmtpMailTransport(MailOptions options) : IMailTransport
{
    public async Task SendAsync(MailConfig config, MailMessageData message, CancellationToken cancellationToken = default)
    {
        await MailHostGuard.EnsureAllowedAsync(config.Host, options, cancellationToken).ConfigureAwait(false);
        using var client = new SmtpClient(config.Host, config.Port)
        {
            EnableSsl = config.EnableSsl,
            Timeout = (int)options.Timeout.TotalMilliseconds,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };
        if (config.UserName.Length > 0)
        {
            client.Credentials = new NetworkCredential(config.UserName, config.Password);
        }

        using var mail = new MailMessage(new MailAddress(config.FromAddress), new MailAddress(message.To))
        {
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = false,
            SubjectEncoding = System.Text.Encoding.UTF8,
            BodyEncoding = System.Text.Encoding.UTF8,
        };
        await client.SendMailAsync(mail, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Tells the owner a visitor comment arrived. Best effort: it never throws, so mail trouble can never fail a visitor's comment.</summary>
public interface ICommentNotifier
{
    Task NotifyAsync(string author, string postTitle, string body, CancellationToken cancellationToken = default);
}

public interface IMailer
{
    /// <summary>Sends a test message with the saved settings. A business-rule error saying what is missing or why the server refused (never the password).</summary>
    Task SendTestAsync(string to, CancellationToken cancellationToken = default);
}

public sealed class Mailer(IPluginSettings settings, IMailTransport transport, IPlugins? plugins = null) : IMailer, ICommentNotifier
{
    public async Task SendTestAsync(string to, CancellationToken cancellationToken = default)
    {
        await EnsureOnAsync(cancellationToken).ConfigureAwait(false);
        var config = await ConfigAsync(cancellationToken).ConfigureAwait(false);
        if (!config.HasServer)
        {
            throw new BusinessRuleException("The mail server is not set. Fill in the server and the sender address first.");
        }

        if (!config.HasSender)
        {
            throw new BusinessRuleException("The sender address is not set.");
        }

        if (string.IsNullOrWhiteSpace(to) || MailPlugin.ValidateAddress(to.Trim()) is not null)
        {
            throw new BusinessRuleException("Give one e-mail address to send the test message to.");
        }

        await SendAsync(config, new MailMessageData(to.Trim(), OneLine(config.SubjectPrefix + "Test message"), "This is a test message from your site. If you can read it, the mail settings work."), cancellationToken).ConfigureAwait(false);
    }

    public async Task NotifyAsync(string author, string postTitle, string body, CancellationToken cancellationToken = default)
    {
        try
        {
            if (plugins is not null && !await plugins.IsEnabledAsync(MailPlugin.Key, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var config = await ConfigAsync(cancellationToken).ConfigureAwait(false);
            if (!config.NotifyOnComment || !config.HasServer || !config.HasSender)
            {
                return;
            }

            var text = body.Length > 500 ? body[..500] + "..." : body;
            await SendAsync(
                config,
                new MailMessageData(
                    config.FromAddress,
                    OneLine($"{config.SubjectPrefix}New comment on {postTitle}"),
                    $"{OneLine(author)} wrote on \"{OneLine(postTitle)}\":\n\n{Plain(text)}\n\nApprove or delete it on Admin > Comments."),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // a notice that cannot be sent is not the visitor's problem
        }
    }

    private async Task EnsureOnAsync(CancellationToken cancellationToken)
    {
        if (plugins is not null && !await plugins.IsEnabledAsync(MailPlugin.Key, cancellationToken).ConfigureAwait(false))
        {
            throw new BusinessRuleException("The E-mail plug-in is switched off. Switch it on under Plugins first.");
        }
    }

    private async Task<MailConfig> ConfigAsync(CancellationToken cancellationToken) =>
        MailConfig.From(await settings.GetAsync(MailPlugin.Key, cancellationToken).ConfigureAwait(false));

    private async Task SendAsync(MailConfig config, MailMessageData message, CancellationToken cancellationToken)
    {
        try
        {
            await transport.SendAsync(config, message, cancellationToken).ConfigureAwait(false);
        }
        catch (BusinessRuleException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // never echo the exception text: it can carry the user name, the server banner or the password
            var reason = ex is SmtpException smtp ? smtp.StatusCode.ToString() : "no answer from the server";
            throw new BusinessRuleException($"The message could not be sent ({reason}). Check the server, port, sign-in and SSL setting.");
        }
    }

    /// <summary>Removes every line break and control character, so text from a visitor or a setting can never add a header.</summary>
    private static string OneLine(string text)
    {
        var cleaned = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length > 150 ? cleaned[..150] : cleaned;
    }

    /// <summary>Keeps new lines but drops other control characters.</summary>
    private static string Plain(string text) => new(text.Where(c => c == '\n' || !char.IsControl(c)).ToArray());
}

public static class MailServiceCollectionExtensions
{
    /// <summary>Registers the E-mail plug-in, the SMTP transport, the mailer and the comment notice. Bind <see cref="MailOptions"/> from configuration (Mail:AllowPrivateHosts) when needed.</summary>
    public static IServiceCollection AddMail(this IServiceCollection services)
    {
        services.AddPlugin(MailPlugin.Definition);
        services.TryAddSingleton<MailOptions>();
        services.TryAddScoped<IMailTransport, SmtpMailTransport>();
        services.TryAddScoped<Mailer>();
        services.TryAddScoped<IMailer>(p => p.GetRequiredService<Mailer>());
        services.TryAddScoped<ICommentNotifier>(p => p.GetRequiredService<Mailer>());
        return services;
    }
}
