using System.Reflection;
using Adventures.Blog;
using Adventures.Blog.WebApi;
using Adventures.Common.Exceptions;
using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Adventures.Blog.WebApi.Tests;

public sealed class MailControllerTests
{
    private sealed class FakeMailer(bool refuse = false) : IMailer
    {
        public string? To { get; private set; }

        public Task SendTestAsync(string to, CancellationToken cancellationToken = default)
        {
            To = to;
            return refuse ? throw new BusinessRuleException("The mail server is not set.") : Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ATestIsSentToTheAddressGiven()
    {
        var mailer = new FakeMailer();

        var result = await new MailController(mailer).Test(new MailTestRequest("me@example.net"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("me@example.net", mailer.To);
    }

    [Fact]
    public async Task ARefusal_IsAnAnswerNotACrash_AndAMissingAddressIsPassedOnEmpty()
    {
        var result = await new MailController(new FakeMailer(refuse: true)).Test(new MailTestRequest(null), CancellationToken.None);

        Assert.IsNotType<NoContentResult>(result);
    }

    [Fact]
    public void TheRoute_NeedsTheSettingsRight_AndIsNeverAnonymous()
    {
        var controller = typeof(MailController);
        Assert.Contains(controller.GetCustomAttributes<RequireRightAttribute>(), a => a.Right == Rights.SettingsManage);
        Assert.All(controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), m => Assert.Empty(m.GetCustomAttributes<AllowAnonymousAttribute>()));
    }
}
