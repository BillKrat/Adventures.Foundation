using Adventures.Common.Exceptions;
using Adventures.Entities;
using Adventures.MEF;
using Xunit;

namespace Adventures.Blog.Tests;

/// <summary>The Contact plug-in: a visitor writes to the owner by e-mail. Off until the owner switches it on; nothing is sent when it is off or when no address can be found to send to.</summary>
public sealed class ContactFormTests
{
    private sealed class FakePlugins(bool on) : IPlugins
    {
        public Task<IReadOnlyList<PluginState>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PluginState>>([]);

        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(key == ContactPlugin.Key && on);

        public Task<IReadOnlySet<string>> EnabledKeysAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task SetEnabledAsync(string key, bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSettings(string recipient, string intro, string mailFrom) : IPluginSettings
    {
        public IReadOnlyList<PluginSettingDefinition> DefinitionsOf(string pluginKey) => [];

        public Task<IReadOnlyDictionary<string, string>> GetAsync(string pluginKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(pluginKey switch
            {
                ContactPlugin.Key => new Dictionary<string, string> { [ContactPlugin.Recipient] = recipient, [ContactPlugin.Intro] = intro },
                MailPlugin.Key => new Dictionary<string, string> { [MailPlugin.FromAddress] = mailFrom },
                _ => new Dictionary<string, string>(),
            });

        public Task SetAsync(string pluginKey, IReadOnlyDictionary<string, string?> values, IReadOnlyCollection<string>? clear = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<PluginSettingsView> GetForDisplayAsync(string pluginKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeSite : ISiteSettings
    {
        public Task<PublicSiteSettings> GetPublicAsync(CancellationToken cancellationToken = default) => Task.FromResult(new PublicSiteSettings("My Blog", "", 10));

        public Task<string> GetLegacyTimeZoneAsync(CancellationToken cancellationToken = default) => Task.FromResult("UTC");
    }

    private sealed class FakeMail : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
        {
            Sent.Add((to, subject, body));
            return Task.CompletedTask;
        }
    }

    private static (ContactForm Form, FakeMail Mail) Make(bool on = true, string recipient = "owner@example.org", string intro = "Write to me.", string mailFrom = "")
    {
        var mail = new FakeMail();
        return (new ContactForm(new FakePlugins(on), new FakeSettings(recipient, intro, mailFrom), new FakeSite(), mail), mail);
    }

    private static ContactMessage Message(string name = "Ann", string? email = "ann@example.org", string text = "Hello there.") => new(name, email, text);

    [Fact]
    public async Task TheInfo_SaysWhetherTheFormIsOn_AndCarriesTheIntroOnlyWhenItIs()
    {
        Assert.Equal(new ContactInfo(true, "Write to me."), await Make().Form.GetInfoAsync());
        Assert.Equal(new ContactInfo(false, string.Empty), await Make(on: false).Form.GetInfoAsync());
    }

    [Fact]
    public async Task AMessage_GoesToTheRecipient_WithTheSiteNameAndTheVisitorDetails()
    {
        var (form, mail) = Make();

        await form.SendAsync(Message(), CancellationToken.None);

        var sent = Assert.Single(mail.Sent);
        Assert.Equal("owner@example.org", sent.To);
        Assert.Contains("My Blog", sent.Subject);
        Assert.Contains("Ann", sent.Subject);
        Assert.Contains("ann@example.org", sent.Body);
        Assert.Contains("Hello there.", sent.Body);
    }

    [Fact]
    public async Task WithoutARecipient_TheMailSenderAddressIsUsed_AndWithNeither_NothingIsSentAndTheOwnerIsToldItIsNotSetUp()
    {
        var (form, mail) = Make(recipient: "", mailFrom: "site@example.org");
        await form.SendAsync(Message(), CancellationToken.None);
        Assert.Equal("site@example.org", Assert.Single(mail.Sent).To);

        var (none, noMail) = Make(recipient: "", mailFrom: "");
        await Assert.ThrowsAsync<BusinessRuleException>(() => none.SendAsync(Message(), CancellationToken.None));
        Assert.Empty(noMail.Sent);
    }

    [Fact]
    public async Task WhenThePluginIsOff_NothingIsSent()
    {
        var (form, mail) = Make(on: false);

        await Assert.ThrowsAsync<BusinessRuleException>(() => form.SendAsync(Message(), CancellationToken.None));

        Assert.Empty(mail.Sent);
    }

    [Theory]
    [InlineData("", "ann@example.org", "Hi")]
    [InlineData("   ", "ann@example.org", "Hi")]
    [InlineData("Ann", "not-an-address", "Hi")]
    [InlineData("Ann", "", "Hi")]
    [InlineData("Ann", null, "Hi")]
    [InlineData("Ann", "ann@example.org", "")]
    [InlineData("Ann", "ann@example.org", "   ")]
    public async Task AnIncompleteOrMalformedMessage_IsRefused_AndNothingIsSent(string name, string? email, string text)
    {
        var (form, mail) = Make();

        await Assert.ThrowsAsync<BusinessRuleException>(() => form.SendAsync(new ContactMessage(name, email, text), CancellationToken.None));

        Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task TooLongAMessage_OrName_IsRefused()
    {
        var (form, mail) = Make();

        await Assert.ThrowsAsync<BusinessRuleException>(() => form.SendAsync(Message(text: new string('x', ContactForm.MaxMessageLength + 1)), CancellationToken.None));
        await Assert.ThrowsAsync<BusinessRuleException>(() => form.SendAsync(Message(name: new string('n', ContactForm.MaxNameLength + 1)), CancellationToken.None));

        Assert.Empty(mail.Sent);
    }

    [Fact]
    public async Task ALineBreakInTheName_CannotAddAMailHeader()
    {
        var (form, mail) = Make();

        await form.SendAsync(Message(name: "Ann\r\nBcc: victim@example.org"), CancellationToken.None);

        var sent = Assert.Single(mail.Sent);
        Assert.DoesNotContain('\r', sent.Subject);
        Assert.DoesNotContain('\n', sent.Subject);
    }

    [Fact]
    public void TheContactPlugin_StartsOff_AndDeclaresARecipientAndAnIntro()
    {
        Assert.False(ContactPlugin.Definition.DefaultEnabled);
        Assert.Equal([ContactPlugin.Recipient, ContactPlugin.Intro], ContactPlugin.Definition.Settings!.Select(s => s.Name));
        Assert.NotNull(ContactPlugin.Definition.Settings![0].Validate!("not an address"));
        Assert.Null(ContactPlugin.Definition.Settings![0].Validate!(""));
        Assert.Null(ContactPlugin.Definition.Settings![0].Validate!("me@example.org"));
    }
}
