using Adventures.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Adventures.Identity.WebApi.Tests;

/// <summary>
/// Forgot my password over HTTP (2026-11-23). Threats: learning who has an account from the answer or its timing, a mail link pointing at a foreign host, one failure text per bad link
/// so a link cannot be probed, and a weak password being accepted.
/// </summary>
public sealed class PasswordResetControllerTests
{
    private const string UserId = "11111111-1111-1111-1111-111111111111";

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

    private sealed class People : IResetDirectory
    {
        private static readonly ResetPerson Bill = new(UserId, "Bill", "bill@example.org");

        public Task<ResetPerson?> FindAsync(string identifier, CancellationToken cancellationToken = default) =>
            Task.FromResult<ResetPerson?>(identifier is "Bill" or "bill@example.org" ? Bill : null);

        public Task<ResetPerson?> GetAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ResetPerson?>(userId == UserId ? Bill : null);
    }

    private sealed class Resetter : IPasswordResetter
    {
        public List<(string Tenant, string User, string Password)> Calls { get; } = [];

        public Task<ResetOutcome> ResetAsync(string tenant, string username, string newPassword, CancellationToken cancellationToken = default)
        {
            Calls.Add((tenant, username, newPassword));
            return Task.FromResult(ResetOutcome.Reset);
        }
    }

    private sealed class RequestTenant(string host) : Adventures.WebApi.IRequestTenant
    {
        public CurrentTenant Current { get; } = new("t1", "Test", host, host, TenantMatch.Domain);
    }

    private sealed class Controller(PasswordResetService reset, params string[] hosts)
        : PasswordResetController(reset, new Hosts(hosts), new Env(), new ConfigurationBuilder().Build(), new RequestTenant("example.org"))
    {
        public List<Func<Task>> Deferred { get; } = [];

        protected override void AfterResponse(Func<Task> work) => Deferred.Add(work);
    }

    private readonly Memory _store = new();
    private readonly Sender _sender = new();
    private readonly Resetter _resetter = new();

    private Controller Build()
    {
        var verification = new EmailVerificationService(_store, _sender);
        var service = new PasswordResetService(new People(), verification, _store, _sender, _resetter);
        return new Controller(service, "example.org", "www.example.org") { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
    }

    private async Task VerifyAddressAsync()
    {
        var verification = new EmailVerificationService(_store, _sender);
        await verification.RequestAsync(UserId, "bill@example.org", "https://www.example.org/verify-email");
        var body = _sender.Sent[^1].Body;
        await verification.ConfirmAsync(UserId, body[(body.IndexOf("token=", StringComparison.Ordinal) + 6)..].Split(['\r', '\n', '&', ' '])[0]);
        _sender.Sent.Clear();
    }

    [Theory]
    [InlineData("Bill")]
    [InlineData("nobody")]
    public async Task AskingAlwaysAnswersNoContent_AndTheWorkRunsAfterTheResponse(string identifier)
    {
        await VerifyAddressAsync();
        var controller = Build();

        var result = await controller.Request(new ForgotPasswordRequest(identifier, "https://www.example.org"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(_sender.Sent);
        foreach (var work in controller.Deferred)
        {
            await work();
        }

        Assert.Equal(identifier == "Bill" ? 1 : 0, _sender.Sent.Count);
    }

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("http://www.example.org")]
    [InlineData("")]
    public async Task ALinkBaseWeDoNotServeIsRefused_AndNothingIsScheduled(string clientBase)
    {
        var controller = Build();

        Assert.IsType<BadRequestObjectResult>(await controller.Request(new ForgotPasswordRequest("Bill", clientBase), CancellationToken.None));
        Assert.Empty(controller.Deferred);
    }

    [Fact]
    public async Task TheLinkChoosesANewPasswordOnce_WithOnePlainFailureForAnyBadLink()
    {
        await VerifyAddressAsync();
        var controller = Build();
        await controller.Request(new ForgotPasswordRequest("Bill", "https://www.example.org"), CancellationToken.None);
        await controller.Deferred[0]();
        var body = _sender.Sent[0].Body;
        var token = body[(body.IndexOf("token=", StringComparison.Ordinal) + 6)..].Split(['\r', '\n', '&', ' '])[0];

        var weak = Assert.IsType<UnprocessableEntityObjectResult>(await controller.Reset(new ResetPasswordRequest(UserId, token, "short"), CancellationToken.None));
        Assert.Contains("At least", weak.Value!.ToString());
        Assert.IsType<NoContentResult>(await controller.Reset(new ResetPasswordRequest(UserId, token, "a long new password 1"), CancellationToken.None));
        Assert.Equal("Bill", Assert.Single(_resetter.Calls).User);

        var replay = Assert.IsType<BadRequestObjectResult>(await controller.Reset(new ResetPasswordRequest(UserId, token, "a long new password 1"), CancellationToken.None));
        var wrong = Assert.IsType<BadRequestObjectResult>(await controller.Reset(new ResetPasswordRequest(UserId, "nope", "a long new password 1"), CancellationToken.None));
        var empty = Assert.IsType<BadRequestObjectResult>(await controller.Reset(new ResetPasswordRequest(null, null, null), CancellationToken.None));
        Assert.Equal(replay.Value, wrong.Value);
        Assert.Equal(replay.Value, empty.Value);
    }
}
