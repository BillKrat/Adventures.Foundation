using Adventures.Common.Exceptions;
using Adventures.Entities;
using Adventures.MEF;

namespace Adventures.Blog;

/// <summary>
/// The Contact plug-in: when the owner switches it on, the site menu gets a CONTACT link (after Home) to a form that sends the visitor's message to the
/// owner by e-mail. It starts off, because it shows something to every visitor and needs the E-mail plug-in to be set up.
/// </summary>
public static class ContactPlugin
{
    public const string Key = "contact";

    public const string Recipient = "Recipient";

    public const string Intro = "Intro";

    public static readonly PluginDefinition Definition = new(
        Key,
        "Contact",
        "A CONTACT link in the site menu (after Home) and a form that e-mails the visitor's message to you. It needs the E-mail plug-in to be set up. Off until you switch it on.",
        "1.0.0",
        7,
        [
            new PluginSettingDefinition(Recipient, "Send messages to", "The address that receives the messages. Leave empty to use the sender address of the E-mail plug-in.", PluginSettingKind.Text, string.Empty, 254, MailPlugin.ValidateAddress),
            new PluginSettingDefinition(Intro, "Text above the form", "A line or two shown above the form (plain text).", PluginSettingKind.Lines, "Send me a message and I will answer by e-mail.", 500),
        ],
        DefaultEnabled: false);
}

/// <summary>What the site needs to know about the form: whether to show the CONTACT link, and the text above the form. The recipient is never part of it.</summary>
public sealed record ContactInfo(bool Enabled, string Intro);

/// <summary>What a visitor sends.</summary>
public sealed record ContactMessage(string Name, string? Email, string Message);

public interface IContactForm
{
    Task<ContactInfo> GetInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>E-mails the message to the owner. A business-rule error when the plug-in is off, no address can be found to send to, or the name, address or text is missing, too long or malformed.</summary>
    Task SendAsync(ContactMessage message, CancellationToken cancellationToken = default);
}

public sealed class ContactForm(IPlugins plugins, IPluginSettings settings, ISiteSettings site, IEmailSender mail) : IContactForm
{
    public const int MaxNameLength = 80;
    public const int MaxMessageLength = 4000;

    public async Task<ContactInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        if (!await plugins.IsEnabledAsync(ContactPlugin.Key, cancellationToken).ConfigureAwait(false))
        {
            return new ContactInfo(false, string.Empty);
        }

        var values = await settings.GetAsync(ContactPlugin.Key, cancellationToken).ConfigureAwait(false);
        return new ContactInfo(true, values.GetValueOrDefault(ContactPlugin.Intro)?.Trim() ?? string.Empty);
    }

    public async Task SendAsync(ContactMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!await plugins.IsEnabledAsync(ContactPlugin.Key, cancellationToken).ConfigureAwait(false))
        {
            throw new BusinessRuleException("The contact form is not available on this site.");
        }

        var name = OneLine(message.Name);
        var email = OneLine(message.Email ?? string.Empty);
        var text = Clean(message.Message);
        if (name.Length is 0 or > MaxNameLength)
        {
            throw new BusinessRuleException($"The name must be between 1 and {MaxNameLength} characters.");
        }

        if (email.Length == 0 || MailPlugin.ValidateAddress(email) is not null)
        {
            throw new BusinessRuleException("Give an e-mail address I can answer to, such as you@example.org.");
        }

        if (text.Length is 0 or > MaxMessageLength)
        {
            throw new BusinessRuleException($"The message must be between 1 and {MaxMessageLength} characters.");
        }

        var contact = await settings.GetAsync(ContactPlugin.Key, cancellationToken).ConfigureAwait(false);
        var to = contact.GetValueOrDefault(ContactPlugin.Recipient)?.Trim();
        if (string.IsNullOrEmpty(to))
        {
            to = (await settings.GetAsync(MailPlugin.Key, cancellationToken).ConfigureAwait(false)).GetValueOrDefault(MailPlugin.FromAddress)?.Trim();
        }

        if (string.IsNullOrEmpty(to))
        {
            throw new BusinessRuleException("The contact form is not set up yet: the owner has not given an address to send to.");
        }

        var title = (await site.GetPublicAsync(cancellationToken).ConfigureAwait(false)).Title;
        await mail.SendAsync(
            to,
            $"{title}: message from {name}",
            $"From: {name} <{email}>{Environment.NewLine}{Environment.NewLine}{text}{Environment.NewLine}",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The value without control characters, so a line break cannot start a new mail header.</summary>
    private static string OneLine(string value) => new string(value.Where(ch => !char.IsControl(ch)).ToArray()).Trim();

    private static string Clean(string value) => new string(value.Where(ch => !char.IsControl(ch) || ch is '\n' or '\r' or '\t').ToArray()).Trim();
}
