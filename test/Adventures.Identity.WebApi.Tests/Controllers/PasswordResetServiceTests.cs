using Adventures.Entities;
using Adventures.Common.Exceptions;
using Xunit;

namespace Adventures.Identity.WebApi.Tests;

/// <summary>
/// Forgot my password (2026-11-23): a link goes only to a VERIFIED address, asking gives the same answer whether or not the person exists, the token is hashed, single-use, bound to the
/// address, expires, and is limited to three mails an hour. Threats: account enumeration, taking over an account with an unproved address, token theft or replay, mail bombing, a
/// reset that outlives a change of address.
/// </summary>
public sealed class PasswordResetServiceTests
{
    private const string UserId = "11111111-1111-1111-1111-111111111111";
    private const string OtherId = "22222222-2222-2222-2222-222222222222";
    private const string Address = "bill@example.org";
    private const string LinkBase = "https://www.example.org/reset-password";

    private sealed class Memory : IEmailVerificationStore
    {
        public List<(string User, string Kind, string Value)> Rows { get; } = [];

        public Task AddAsync(string userId, string kind, string value, CancellationToken cancellationToken = default)
        {
            Rows.Add((userId, kind, value));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAsync(string userId, string kind, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(Rows.Where(r => r.User == userId && r.Kind == kind).Select(r => r.Value).ToList());
    }

    private sealed class Sender : IEmailSender
    {
        public bool Fail { get; set; }

        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                throw new BusinessRuleException("mail is not set up");
            }

            Sent.Add((to, subject, body));
            return Task.CompletedTask;
        }
    }

    private sealed class Directory : IResetDirectory
    {
        public Dictionary<string, ResetPerson> People { get; } = new()
        {
            [UserId] = new ResetPerson(UserId, "Bill", Address),
            [OtherId] = new ResetPerson(OtherId, "Other", "other@example.org"),
        };

        public Task<ResetPerson?> FindAsync(string identifier, CancellationToken cancellationToken = default) =>
            Task.FromResult(People.Values.FirstOrDefault(p => string.Equals(p.UserName, identifier, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Email, identifier, StringComparison.OrdinalIgnoreCase)));

        public Task<ResetPerson?> GetAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(People.GetValueOrDefault(userId));
    }

    private sealed class Resetter : IPasswordResetter
    {
        public List<(string Tenant, string User, string Password)> Calls { get; } = [];

        public ResetOutcome Outcome { get; set; } = ResetOutcome.Reset;

        public Task<ResetOutcome> ResetAsync(string tenant, string username, string newPassword, CancellationToken cancellationToken = default)
        {
            Calls.Add((tenant, username, newPassword));
            return Task.FromResult(Outcome);
        }
    }

    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly Memory _store = new();
    private readonly Sender _sender = new();
    private readonly Directory _directory = new();
    private readonly Resetter _resetter = new();
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero));

    private EmailVerificationService Verification() => new(_store, _sender, _clock);

    private PasswordResetService Service() => new(_directory, Verification(), _store, _sender, _resetter, _clock);

    /// <summary>Makes the address of a person verified through the real cycle.</summary>
    private async Task VerifyAsync(string userId, string address)
    {
        var verification = Verification();
        await verification.RequestAsync(userId, address, "https://www.example.org/verify-email");
        var body = _sender.Sent[^1].Body;
        Assert.True(await verification.ConfirmAsync(userId, TokenIn(body)));
        _sender.Sent.Clear();
    }

    private static string TokenIn(string body) => body[(body.IndexOf("token=", StringComparison.Ordinal) + 6)..].Split(['\r', '\n', '&', ' '])[0];

    [Fact]
    public async Task ASendsALinkToAVerifiedAddress_ByUserNameOrByAddress()
    {
        await VerifyAsync(UserId, Address);

        await Service().RequestAsync("Bill", LinkBase);
        await Service().RequestAsync("BILL@example.org", LinkBase);

        Assert.Equal(2, _sender.Sent.Count);
        Assert.All(_sender.Sent, mail => Assert.Equal(Address, mail.To));
        Assert.Contains($"{LinkBase}?user={UserId}&token=", _sender.Sent[0].Body);
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("")]
    [InlineData("a@b.org, c@d.org")]
    public async Task AnUnknownPersonGetsNoMailAndNoError(string identifier)
    {
        await Service().RequestAsync(identifier, LinkBase);

        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task AnAddressNobodyProvedGetsNoMail()
    {
        await Service().RequestAsync("Bill", LinkBase);

        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task AMailFailureIsNotShownToTheAsker()
    {
        await VerifyAsync(UserId, Address);
        _sender.Fail = true;

        await Service().RequestAsync("Bill", LinkBase);

        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task OnlyAHashOfTheTokenIsStored()
    {
        await VerifyAsync(UserId, Address);
        await Service().RequestAsync("Bill", LinkBase);
        var token = TokenIn(_sender.Sent[0].Body);

        Assert.DoesNotContain(_store.Rows, row => row.Value.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AtMostThreeMailsAnHourForOnePerson()
    {
        await VerifyAsync(UserId, Address);
        for (var i = 0; i < 5; i++)
        {
            await Service().RequestAsync("Bill", LinkBase);
        }

        Assert.Equal(3, _sender.Sent.Count);

        _clock.Now = _clock.Now.AddMinutes(61);
        await Service().RequestAsync("Bill", LinkBase);
        Assert.Equal(4, _sender.Sent.Count);
    }

    [Fact]
    public async Task TheLinkSetsTheNewPasswordOnce()
    {
        await VerifyAsync(UserId, Address);
        await Service().RequestAsync("Bill", LinkBase);
        var token = TokenIn(_sender.Sent[0].Body);

        Assert.Equal(PasswordResetResult.Done, await Service().ResetAsync("example.org", UserId, token, "a long new password 1"));
        Assert.Equal(("example.org", "Bill", "a long new password 1"), Assert.Single(_resetter.Calls));

        Assert.Equal(PasswordResetResult.InvalidLink, await Service().ResetAsync("example.org", UserId, token, "another long password 2"));
        Assert.Single(_resetter.Calls);
    }

    [Fact]
    public async Task AWeakPasswordIsRefusedAndTheLinkStaysUsable()
    {
        await VerifyAsync(UserId, Address);
        await Service().RequestAsync("Bill", LinkBase);
        var token = TokenIn(_sender.Sent[0].Body);

        Assert.Equal(PasswordResetResult.WeakPassword, await Service().ResetAsync("example.org", UserId, token, "short"));
        Assert.Empty(_resetter.Calls);
        Assert.Equal(PasswordResetResult.Done, await Service().ResetAsync("example.org", UserId, token, "a long new password 1"));
    }

    [Fact]
    public async Task AnExpiredWrongOrForeignTokenResetsNothing()
    {
        await VerifyAsync(UserId, Address);
        await Service().RequestAsync("Bill", LinkBase);
        var token = TokenIn(_sender.Sent[0].Body);

        Assert.Equal(PasswordResetResult.InvalidLink, await Service().ResetAsync("example.org", UserId, "nope", "a long new password 1"));
        Assert.Equal(PasswordResetResult.InvalidLink, await Service().ResetAsync("example.org", OtherId, token, "a long new password 1"));
        Assert.Equal(PasswordResetResult.InvalidLink, await Service().ResetAsync("example.org", UserId, string.Empty, "a long new password 1"));

        _clock.Now = _clock.Now.AddMinutes(61);
        Assert.Equal(PasswordResetResult.InvalidLink, await Service().ResetAsync("example.org", UserId, token, "a long new password 1"));
        Assert.Empty(_resetter.Calls);
    }

    [Fact]
    public async Task ALinkDiesWhenTheAddressChangesAfterTheMailWentOut()
    {
        await VerifyAsync(UserId, Address);
        await Service().RequestAsync("Bill", LinkBase);
        var token = TokenIn(_sender.Sent[0].Body);
        _directory.People[UserId] = new ResetPerson(UserId, "Bill", "attacker@example.net");

        Assert.Equal(PasswordResetResult.InvalidLink, await Service().ResetAsync("example.org", UserId, token, "a long new password 1"));
        Assert.Empty(_resetter.Calls);
    }

    [Fact]
    public async Task AVerificationTokenIsNotAResetToken()
    {
        await VerifyAsync(UserId, Address);
        var verification = Verification();
        await verification.RequestAsync(UserId, Address, "https://www.example.org/verify-email");
        var verifyToken = TokenIn(_sender.Sent[^1].Body);

        Assert.Equal(PasswordResetResult.InvalidLink, await Service().ResetAsync("example.org", UserId, verifyToken, "a long new password 1"));
    }

    [Fact]
    public async Task ARefusedResetByTheAccountStoreIsAnInvalidLink()
    {
        await VerifyAsync(UserId, Address);
        await Service().RequestAsync("Bill", LinkBase);
        _resetter.Outcome = ResetOutcome.NotFound;

        Assert.Equal(PasswordResetResult.InvalidLink, await Service().ResetAsync("example.org", UserId, TokenIn(_sender.Sent[0].Body), "a long new password 1"));
    }
}
