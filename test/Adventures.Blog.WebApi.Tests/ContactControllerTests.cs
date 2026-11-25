using System.Reflection;
using Adventures.Blog;
using Adventures.Blog.WebApi;
using Adventures.Common.Exceptions;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace Adventures.Blog.WebApi.Tests;

/// <summary>The public contact form: anonymous, rate limited, size capped, with a honeypot; it answers what the site needs and never the recipient.</summary>
public sealed class ContactControllerTests
{
    private sealed class FakeForm : IContactForm
    {
        public ContactInfo Info { get; set; } = new(true, "Write to me.");

        public ContactMessage? Sent { get; private set; }

        public bool Refuse { get; set; }

        public Task<ContactInfo> GetInfoAsync(CancellationToken cancellationToken = default) => Task.FromResult(Info);

        public Task SendAsync(ContactMessage message, CancellationToken cancellationToken = default)
        {
            if (Refuse)
            {
                throw new BusinessRuleException("The message must be between 1 and 4000 characters.");
            }

            Sent = message;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task TheInfo_IsWhatTheFormAnswers()
    {
        var info = Assert.IsType<ContactInfo>(Assert.IsType<OkObjectResult>((await new PublicContactController(new FakeForm()).Info(CancellationToken.None)).Result).Value);

        Assert.Equal(new ContactInfo(true, "Write to me."), info);
    }

    [Fact]
    public async Task AMessage_IsSent_AndAccepted()
    {
        var form = new FakeForm();

        var result = await new PublicContactController(form).Send(new ContactRequest("Ann", "ann@example.org", "Hello", null), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal(new ContactMessage("Ann", "ann@example.org", "Hello"), form.Sent);
    }

    [Fact]
    public async Task TheHoneypot_IsAnsweredLikeARealMessage_ButNothingIsSent()
    {
        var form = new FakeForm();

        var result = await new PublicContactController(form).Send(new ContactRequest("Bot", "bot@example.org", "Buy now", "http://spam.example"), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.Null(form.Sent);
    }

    [Fact]
    public async Task ARefusedMessage_IsAConflict_WithTheReason()
    {
        var result = await new PublicContactController(new FakeForm { Refuse = true }).Send(new ContactRequest("Ann", "ann@example.org", "", null), CancellationToken.None);

        Assert.Contains("between 1 and 4000", Assert.IsType<ConflictObjectResult>(result).Value as string);
    }

    [Fact]
    public void TheEndpoints_AreAnonymousUnderApiPublic_AndTheSendIsRateLimitedAndSizeCapped()
    {
        var type = typeof(PublicContactController);
        Assert.NotEmpty(type.GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.Contains(type.GetCustomAttributes<RouteAttribute>(), a => a.Template == "api/public/contact");
        var send = type.GetMethod(nameof(PublicContactController.Send))!;
        Assert.Contains(send.GetCustomAttributes<EnableRateLimitingAttribute>(), a => a.PolicyName == RequestHardening.CommentPolicy);
        Assert.NotEmpty(send.GetCustomAttributes<RequestSizeLimitAttribute>());
    }
}
