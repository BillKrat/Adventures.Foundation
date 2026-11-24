using System.Net;
using Adventures.Common.Exceptions;
using Adventures.MEF;
using Xunit;

namespace Adventures.Blog.Tests;

/// <summary>
/// E-mail (beta row 12, parity): the SMTP settings are a plug-in's settings (the password is an encrypted, write-only secret), a test message proves them, and a
/// new comment can be announced to the owner. What an administrator types never reaches a header unchecked, the password never leaves the transport call, the
/// settings cannot aim the server at its own network, and a mail failure never fails the visitor's comment.
/// </summary>
public sealed class MailTests
{
    private sealed class FakeSettings(Dictionary<string, string>? values = null) : IPluginSettings
    {
        public Dictionary<string, string> Values { get; } = values ?? Configured();

        public IReadOnlyList<PluginSettingDefinition> DefinitionsOf(string pluginKey) => MailPlugin.Definition.Settings ?? [];

        public Task<IReadOnlyDictionary<string, string>> GetAsync(string pluginKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(Values);

        public Task SetAsync(string pluginKey, IReadOnlyDictionary<string, string?> values, IReadOnlyCollection<string>? clear = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<PluginSettingsView> GetForDisplayAsync(string pluginKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PluginSettingsView(Values, []));
    }

    private sealed class FakeTransport : IMailTransport
    {
        public List<(MailConfig Config, MailMessageData Message)> Sent { get; } = [];

        public Exception? Fail { get; set; }

        public Task SendAsync(MailConfig config, MailMessageData message, CancellationToken cancellationToken = default)
        {
            if (Fail is not null)
            {
                throw Fail;
            }

            Sent.Add((config, message));
            return Task.CompletedTask;
        }
    }

    private sealed class FakePlugins(bool on = true) : IPlugins
    {
        public Task<IReadOnlyList<PluginState>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PluginState>>([]);

        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(on);

        public Task<IReadOnlySet<string>> EnabledKeysAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(on ? new HashSet<string> { MailPlugin.Key } : new HashSet<string>());

        public Task SetEnabledAsync(string key, bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static Dictionary<string, string> Configured() => new()
    {
        [MailPlugin.FromAddress] = "owner@example.org",
        [MailPlugin.SmtpHost] = "mail.example.org",
        [MailPlugin.SmtpPort] = "587",
        [MailPlugin.UserName] = "owner",
        [MailPlugin.Password] = "s3cret-value",
        [MailPlugin.EnableSsl] = "true",
        [MailPlugin.SubjectPrefix] = "[Blog] ",
        [MailPlugin.NotifyOnComment] = "true",
    };

    private static (Mailer Mailer, FakeTransport Transport) Make(Dictionary<string, string>? values = null, bool pluginOn = true)
    {
        var transport = new FakeTransport();
        return (new Mailer(new FakeSettings(values), transport, new FakePlugins(pluginOn)), transport);
    }

    // ---- the settings

    [Theory]
    [InlineData("owner@example.org", true)]
    [InlineData("", true)]
    [InlineData("not an address", false)]
    [InlineData("a@b", false)]
    [InlineData("a@example.org\r\nBcc: x@evil.example", false)]
    [InlineData("a@example.org, b@example.org", false)]
    public void TheFromAddress_MustBeOneAddress_NeverALineBreakOrAList(string value, bool ok) =>
        Assert.Equal(ok, MailPlugin.ValidateAddress(value) is null);

    [Theory]
    [InlineData("mail.example.org", true)]
    [InlineData("smtp-1.example.org", true)]
    [InlineData("", true)]
    [InlineData("exa mple.org", false)]
    [InlineData("mail.example.org:25", false)]
    [InlineData("http://mail.example.org", false)]
    [InlineData("mail.example.org\r\n", false)]
    public void TheSmtpHost_IsAHostName_NotAnAddressWithAPortOrAScheme(string value, bool ok) =>
        Assert.Equal(ok, MailPlugin.ValidateHost(value) is null);

    [Theory]
    [InlineData("25", true)]
    [InlineData("465", true)]
    [InlineData("587", true)]
    [InlineData("2525", true)]
    [InlineData("22", false)]
    [InlineData("6379", false)]
    [InlineData("0", false)]
    [InlineData("abc", false)]
    public void ThePort_IsOneOfTheMailPorts_SoTheTestButtonCannotProbeOtherServices(string value, bool ok) =>
        Assert.Equal(ok, MailPlugin.ValidatePort(value) is null);

    [Theory]
    [InlineData("[Blog] ", true)]
    [InlineData("", true)]
    [InlineData("Hi\r\nBcc: x@evil.example", false)]
    public void TheSubjectPrefix_IsOneLine(string value, bool ok) =>
        Assert.Equal(ok, MailPlugin.ValidatePrefix(value) is null);

    [Fact]
    public void ThePasswordIsASecret_AndTheCommentNoticeIsOffUntilTheOwnerTurnsItOn()
    {
        var settings = MailPlugin.Definition.Settings!;

        Assert.Equal(PluginSettingKind.Secret, settings.Single(s => s.Name == MailPlugin.Password).Kind);
        Assert.Equal("false", settings.Single(s => s.Name == MailPlugin.NotifyOnComment).Default);
        Assert.Equal("true", settings.Single(s => s.Name == MailPlugin.EnableSsl).Default);
    }

    // ---- sending

    [Fact]
    public async Task ATestMessage_GoesToTheAddressGiven_WithThePrefix_AndTheConfigurationReachesOnlyTheTransport()
    {
        var (mailer, transport) = Make();

        await mailer.SendTestAsync("me@example.net");

        var (config, message) = Assert.Single(transport.Sent);
        Assert.Equal("me@example.net", message.To);
        Assert.StartsWith("[Blog] ", message.Subject);
        Assert.Equal(("mail.example.org", 587, "owner@example.org"), (config.Host, config.Port, config.FromAddress));
        Assert.Equal("s3cret-value", config.Password);
        Assert.DoesNotContain("s3cret-value", message.Body + message.Subject);
    }

    [Fact]
    public async Task WithNoServerOrNoFromAddress_TheTestSaysWhatIsMissing()
    {
        var values = Configured();
        values[MailPlugin.SmtpHost] = string.Empty;
        var (mailer, transport) = Make(values);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => mailer.SendTestAsync("me@example.net"));

        Assert.Contains("server", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(transport.Sent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nobody")]
    [InlineData("a@example.org\r\nBcc: x@evil.example")]
    [InlineData("a@example.org, b@example.org")]
    public async Task ATestRecipient_IsOneValidAddress(string to)
    {
        var (mailer, transport) = Make();

        await Assert.ThrowsAsync<BusinessRuleException>(() => mailer.SendTestAsync(to));

        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task WhenThePluginIsOff_NothingIsSent()
    {
        var (mailer, transport) = Make(pluginOn: false);

        await Assert.ThrowsAsync<BusinessRuleException>(() => mailer.SendTestAsync("me@example.net"));
        await mailer.NotifyAsync("Anna", "A post", "Nice");

        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task ATransportFailure_BecomesAPlainReason_WithoutThePasswordOrAStackTrace()
    {
        var (mailer, transport) = Make();
        transport.Fail = new InvalidOperationException("login failed for owner with s3cret-value at Some.Stack.Frame");

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => mailer.SendTestAsync("me@example.net"));

        Assert.DoesNotContain("s3cret-value", error.Message);
        Assert.DoesNotContain("Stack", error.Message);
    }

    // ---- the comment notice

    [Fact]
    public async Task ANewComment_IsAnnouncedToTheOwner_WhenTheNoticeIsOn()
    {
        var (mailer, transport) = Make();

        await mailer.NotifyAsync("Anna", "A post", "Nice post");

        var (_, message) = Assert.Single(transport.Sent);
        Assert.Equal("owner@example.org", message.To);
        Assert.Contains("Anna", message.Body);
        Assert.Contains("A post", message.Subject);
    }

    [Fact]
    public async Task TheCommentNotice_NeverCarriesAVisitorsLineBreakIntoTheSubject()
    {
        var (mailer, transport) = Make();

        await mailer.NotifyAsync("Anna", "A post\r\nBcc: x@evil.example", "Nice");

        var subject = Assert.Single(transport.Sent).Message.Subject;
        Assert.DoesNotContain('\r', subject);
        Assert.DoesNotContain('\n', subject);
    }

    [Fact]
    public async Task WithTheNoticeOff_NothingIsSent()
    {
        var values = Configured();
        values[MailPlugin.NotifyOnComment] = "false";
        var (mailer, transport) = Make(values);

        await mailer.NotifyAsync("Anna", "A post", "Nice");

        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task AFailingMailServer_NeverFailsTheCommentNotice()
    {
        var (mailer, transport) = Make();
        transport.Fail = new InvalidOperationException("down");

        await mailer.NotifyAsync("Anna", "A post", "Nice");   // must not throw
    }

    // ---- the network guard

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.5", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("93.184.216.34", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void OnlyPublicAddressesMayBeConnectedTo(string address, bool allowed) =>
        Assert.Equal(allowed, MailHostGuard.IsPublic(IPAddress.Parse(address)));

    [Fact]
    public async Task AHostThatResolvesToAPrivateAddress_IsRefused_UnlessTheOperatorAllowedPrivateHosts()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => MailHostGuard.EnsureAllowedAsync("localhost", new MailOptions()));
        await MailHostGuard.EnsureAllowedAsync("localhost", new MailOptions { AllowPrivateHosts = true });
    }
}
