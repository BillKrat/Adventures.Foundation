using Adventures.Common.Exceptions;
using Adventures.Data.NQuad;
using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>
/// The e-mail address verification cycle (2026-11-23): the server mails a single-use link, valid for a day, whose token is stored only as a hash and tied to the address it was
/// sent to; opening it marks that address verified; a changed address is not verified; requests are rate limited. Threats: token theft from the store, replay, a token used for a
/// different address or person, brute force by volume, mail bombing a person.
/// </summary>
public sealed class EmailVerificationTests
{
    private const string UserA = "11111111-1111-1111-1111-111111111111";
    private const string UserB = "22222222-2222-2222-2222-222222222222";
    private const string Address = "owner@example.org";

    private sealed class RecordingSender : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];
        public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default) { Sent.Add((to, subject, body)); return Task.CompletedTask; }
    }

    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly InMemoryNQuadStore _quads = new();
    private readonly RecordingSender _sender = new();
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero));

    private EmailVerificationService Service() => new(new NQuadEmailVerificationStore(_quads), _sender, _clock);

    private static string TokenIn(string body)
    {
        var marker = "token=";
        var start = body.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = body.IndexOfAny([' ', '\r', '\n', '&'], start);
        return body[start..(end < 0 ? body.Length : end)];
    }

    [Fact]
    public async Task RequestSendsOneMailToTheAddressWithALink_AndStoresOnlyAHashOfTheToken()
    {
        await Service().RequestAsync(UserA, Address, "https://example.org/verify-email");

        var mail = Assert.Single(_sender.Sent);
        Assert.Equal(Address, mail.To);
        var token = TokenIn(mail.Body);
        Assert.True(token.Length >= 32);
        Assert.Contains("https://example.org/verify-email", mail.Body);
        foreach (var quad in await _quads.QueryAsync())
        {
            Assert.DoesNotContain(token, quad.Object ?? string.Empty);
        }
    }

    [Fact]
    public async Task OpeningTheLinkVerifiesThatAddress()
    {
        var service = Service();
        Assert.False(await service.IsVerifiedAsync(UserA, Address));
        await service.RequestAsync(UserA, Address, "https://example.org/verify-email");

        Assert.True(await service.ConfirmAsync(UserA, TokenIn(_sender.Sent[0].Body)));

        Assert.True(await service.IsVerifiedAsync(UserA, Address));
        Assert.True(await service.IsVerifiedAsync(UserA, "OWNER@example.org"), "addresses compare without regard to case");
    }

    [Fact]
    public async Task ATokenWorksOnce()
    {
        var service = Service();
        await service.RequestAsync(UserA, Address, "https://example.org/verify-email");
        var token = TokenIn(_sender.Sent[0].Body);

        Assert.True(await service.ConfirmAsync(UserA, token));
        Assert.False(await service.ConfirmAsync(UserA, token));
    }

    [Fact]
    public async Task ATokenExpiresAfterADay()
    {
        var service = Service();
        await service.RequestAsync(UserA, Address, "https://example.org/verify-email");
        _clock.Now = _clock.Now.AddHours(24).AddMinutes(1);

        Assert.False(await service.ConfirmAsync(UserA, TokenIn(_sender.Sent[0].Body)));
        Assert.False(await service.IsVerifiedAsync(UserA, Address));
    }

    [Fact]
    public async Task AWrongTokenOrAnotherPersonsTokenVerifiesNothing()
    {
        var service = Service();
        await service.RequestAsync(UserA, Address, "https://example.org/verify-email");
        var token = TokenIn(_sender.Sent[0].Body);

        Assert.False(await service.ConfirmAsync(UserA, "not-the-token"));
        Assert.False(await service.ConfirmAsync(UserB, token));
        Assert.False(await service.ConfirmAsync(UserA, string.Empty));
        Assert.False(await service.IsVerifiedAsync(UserA, Address));
        Assert.False(await service.IsVerifiedAsync(UserB, Address));
    }

    [Fact]
    public async Task ChangingTheAddressMeansTheNewOneIsNotVerified()
    {
        var service = Service();
        await service.RequestAsync(UserA, Address, "https://example.org/verify-email");
        await service.ConfirmAsync(UserA, TokenIn(_sender.Sent[0].Body));

        Assert.False(await service.IsVerifiedAsync(UserA, "someone.else@example.org"));
    }

    [Fact]
    public async Task AtMostThreeMailsAnHourForOnePerson_AndOtherPeopleAreUnaffected()
    {
        var service = Service();
        for (var i = 0; i < 3; i++)
        {
            await service.RequestAsync(UserA, Address, "https://example.org/verify-email");
        }

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.RequestAsync(UserA, Address, "https://example.org/verify-email"));
        Assert.Equal(3, _sender.Sent.Count);

        await service.RequestAsync(UserB, "b@example.org", "https://example.org/verify-email");
        Assert.Equal(4, _sender.Sent.Count);

        _clock.Now = _clock.Now.AddMinutes(61);
        await service.RequestAsync(UserA, Address, "https://example.org/verify-email");
        Assert.Equal(5, _sender.Sent.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not an address")]
    [InlineData("a@b.org, c@d.org")]
    public async Task OnlyOnePlainAddressIsAccepted(string address)
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service().RequestAsync(UserA, address, "https://example.org/verify-email"));
        Assert.Empty(_sender.Sent);
    }
}
