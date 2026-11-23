using Adventures.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Adventures.Identity.WebApi.Tests;

/// <summary>
/// An administrator sends the address confirmation mail for another person (2026-11-23), so someone who cannot sign in can still confirm their address and then use forgot-my-password.
/// Threats: mailing anywhere but the address saved on that person, a link at a foreign host, reaching a person who is not in this tenant.
/// </summary>
public sealed class UserEmailVerificationControllerTests
{
    private const string PersonId = "11111111-1111-1111-1111-111111111111";

    private sealed class Memory : IEmailVerificationStore
    {
        private readonly List<(string User, string Kind, string Value)> _rows = [];

        public Task AddAsync(string userId, string kind, string value, CancellationToken cancellationToken = default)
        {
            _rows.Add((userId, kind, value));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAsync(string userId, string kind, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(_rows.Where(r => r.User == userId && r.Kind == kind).Select(r => r.Value).ToList());
    }

    private sealed class Sender : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
        {
            Sent.Add((to, subject, body));
            return Task.CompletedTask;
        }
    }

    private sealed class Hosts(params string[] names) : ITenantHostSource
    {
        public Task<IReadOnlyList<string>> AllNamesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(names);
    }

    private sealed class Env : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static EntityFormModel Form(string email) =>
        new(
            new EntitySchemaModel("https://global-webnet.com/schema/User", [new EntityFieldSchema("UserName", "String", IsRequired: true), new EntityFieldSchema("Email", "String", IsRequired: false)]),
            new EntityDataModel(PersonId, new Dictionary<string, string?> { ["UserName"] = "Bill", ["Email"] = email }));

    private readonly Sender _sender = new();
    private readonly Memory _store = new();

    private UserEmailVerificationController Build(EntityFormModel? form) =>
        new(new FakeUserPresenter { FormById = form }, new EmailVerificationService(_store, _sender), new Hosts("example.org", "www.example.org"), new Env())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    [Fact]
    public async Task StatusShowsTheSavedAddressAsNotConfirmed_UntilItIs()
    {
        var controller = Build(Form("bill@example.org"));

        var before = Assert.IsType<OkObjectResult>((await controller.Status(PersonId, CancellationToken.None)).Result);
        Assert.Equal(new VerificationStatus("bill@example.org", false), before.Value);

        await controller.Request(PersonId, new VerificationRequest("https://www.example.org"), CancellationToken.None);
        var body = _sender.Sent[0].Body;
        await new EmailVerificationService(_store, _sender).ConfirmAsync(PersonId, body[(body.IndexOf("token=", StringComparison.Ordinal) + 6)..].Split(['\r', '\n', '&', ' '])[0]);

        var after = Assert.IsType<OkObjectResult>((await controller.Status(PersonId, CancellationToken.None)).Result);
        Assert.Equal(new VerificationStatus("bill@example.org", true), after.Value);
    }

    [Fact]
    public async Task RequestMailsOnlyTheAddressSavedOnThatPerson_WithALinkToARegisteredHost()
    {
        var result = await Build(Form("bill@example.org")).Request(PersonId, new VerificationRequest("https://www.example.org"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var mail = Assert.Single(_sender.Sent);
        Assert.Equal("bill@example.org", mail.To);
        Assert.Contains($"https://www.example.org/verify-email?user={PersonId}&token=", mail.Body);
    }

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("")]
    public async Task AForeignLinkHostIsRefused_AndNothingIsSent(string clientBase)
    {
        var result = await Build(Form("bill@example.org")).Request(PersonId, new VerificationRequest(clientBase), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task APersonWithNoSavedAddressGetsNothing_AndAMissingPersonIsNotFound()
    {
        Assert.IsType<ConflictObjectResult>(await Build(Form(string.Empty)).Request(PersonId, new VerificationRequest("https://www.example.org"), CancellationToken.None));
        Assert.IsType<NotFoundResult>(await Build(null).Request(PersonId, new VerificationRequest("https://www.example.org"), CancellationToken.None));
        Assert.IsType<NotFoundResult>((await Build(null).Status(PersonId, CancellationToken.None)).Result);
        Assert.Empty(_sender.Sent);
    }
}
