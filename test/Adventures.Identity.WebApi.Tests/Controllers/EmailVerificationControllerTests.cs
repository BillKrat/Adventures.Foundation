using System.Security.Claims;
using Adventures.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Adventures.Identity.WebApi.Tests;

/// <summary>
/// Address verification endpoints (2026-11-23). Threats: mailing an address the person did not save (mail relay), a link that points at a site we do not host (phishing through our mail),
/// confirming for someone else, telling an attacker why a token failed.
/// </summary>
public sealed class EmailVerificationControllerTests
{
    private const string UserId = "11111111-1111-1111-1111-111111111111";
    private const string Address = "bill@example.org";

    private sealed class Sender : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
        {
            Sent.Add((to, subject, body));
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryStore : IEmailVerificationStore
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

    private sealed class Hosts(params string[] names) : ITenantHostSource
    {
        public Task<IReadOnlyList<string>> AllNamesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(names);
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static EntityFormModel Form(string email = Address) =>
        new(
            new EntitySchemaModel("https://global-webnet.com/schema/User", [new EntityFieldSchema("UserName", "String", IsRequired: true), new EntityFieldSchema("Email", "String", IsRequired: false)]),
            new EntityDataModel(UserId, new Dictionary<string, string?> { ["UserName"] = "Bill", ["Email"] = email }));

    private readonly Sender _sender = new();

    private EmailVerificationController Controller(EntityFormModel? form, string? userName = "Bill", string environment = "Production")
    {
        var service = new EmailVerificationService(new MemoryStore(), _sender);
        var identity = userName is null ? new ClaimsIdentity() : new ClaimsIdentity([new Claim(ClaimTypes.Name, userName)], "Test");
        return new EmailVerificationController(new FakeUserPresenter { FormByUserName = form }, service, new Hosts("global-webnet.com", "www.global-webnet.com"), new Env(environment))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } },
        };
    }

    private static string TokenIn(string body) => body[(body.IndexOf("token=", StringComparison.Ordinal) + 6)..].Split(['\r', '\n', '&', ' '])[0];

    [Fact]
    public async Task RequestMailsTheSavedAddress_AndTheLinkPointsAtAHostWeServe()
    {
        var result = await Controller(Form()).Request(new VerificationRequest("https://www.global-webnet.com"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var mail = Assert.Single(_sender.Sent);
        Assert.Equal(Address, mail.To);
        Assert.Contains("https://www.global-webnet.com/verify-email?user=", mail.Body);
    }

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("https://global-webnet.com.evil.example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://localhost:4200")]
    [InlineData("")]
    public async Task ALinkBaseOutsideTheRegisteredHostsIsRefused_AndNothingIsSent(string clientBase)
    {
        var result = await Controller(Form()).Request(new VerificationRequest(clientBase), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task LocalhostIsAllowedOnlyInDevelopment()
    {
        var result = await Controller(Form(), environment: "Development").Request(new VerificationRequest("http://localhost:4200"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Contains("http://localhost:4200/verify-email?", Assert.Single(_sender.Sent).Body);
    }

    [Fact]
    public async Task WithNoSavedAddressNothingIsSent()
    {
        var result = await Controller(Form(email: string.Empty)).Request(new VerificationRequest("https://www.global-webnet.com"), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task ASignedOutCallerCannotAskForOrReadAStatus()
    {
        var controller = Controller(Form(), userName: null);

        Assert.IsType<UnauthorizedResult>(await controller.Request(new VerificationRequest("https://www.global-webnet.com"), CancellationToken.None));
        Assert.IsType<UnauthorizedResult>((await controller.Status(CancellationToken.None)).Result);
    }

    [Fact]
    public async Task StatusSaysVerifiedOnlyAfterTheLinkWasOpened_AndOnlyForTheCurrentAddress()
    {
        var controller = Controller(Form());
        var before = Assert.IsType<OkObjectResult>((await controller.Status(CancellationToken.None)).Result);
        Assert.False(((VerificationStatus)before.Value!).Verified);

        await controller.Request(new VerificationRequest("https://www.global-webnet.com"), CancellationToken.None);
        var confirmed = await controller.Confirm(new ConfirmVerification(UserId, TokenIn(_sender.Sent[0].Body)), CancellationToken.None);
        Assert.IsType<NoContentResult>(confirmed);

        var after = Assert.IsType<OkObjectResult>((await controller.Status(CancellationToken.None)).Result);
        var status = (VerificationStatus)after.Value!;
        Assert.True(status.Verified);
        Assert.Equal(Address, status.Address);
    }

    [Fact]
    public async Task ConfirmGivesTheSameAnswerForEveryFailure()
    {
        var controller = Controller(Form());
        await controller.Request(new VerificationRequest("https://www.global-webnet.com"), CancellationToken.None);
        var token = TokenIn(_sender.Sent[0].Body);

        var wrongToken = Assert.IsType<BadRequestObjectResult>(await controller.Confirm(new ConfirmVerification(UserId, "nope"), CancellationToken.None));
        var wrongPerson = Assert.IsType<BadRequestObjectResult>(await controller.Confirm(new ConfirmVerification("someone-else", token), CancellationToken.None));
        var empty = Assert.IsType<BadRequestObjectResult>(await controller.Confirm(new ConfirmVerification(string.Empty, string.Empty), CancellationToken.None));

        Assert.Equal(wrongToken.Value, wrongPerson.Value);
        Assert.Equal(wrongToken.Value, empty.Value);
    }
}
