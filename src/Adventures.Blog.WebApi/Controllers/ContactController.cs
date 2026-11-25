using Adventures.Blog;
using Adventures.WebApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Adventures.Blog.WebApi;

/// <summary>What a visitor posts to the contact form. <paramref name="Website"/> is a hidden field no person fills in: a robot that does is dropped without a word.</summary>
public sealed record ContactRequest(string? Name, string? Email, string? Message, string? Website);

/// <summary>
/// The Contact plug-in for visitors, anonymous: whether the form is on (for the CONTACT link) and the text above it, and the form itself, which e-mails the
/// owner (rate limited per client address like comments, size capped, a honeypot). It never returns the recipient address.
/// </summary>
[ApiController]
[Route("api/public/contact")]
[AllowAnonymous]
public sealed class PublicContactController(IContactForm form) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ContactInfo>> Info(CancellationToken cancellationToken) => Ok(await form.GetInfoAsync(cancellationToken));

    [HttpPost]
    [EnableRateLimiting(RequestHardening.CommentPolicy)]
    [RequestSizeLimit(16 * 1024)]
    public Task<IActionResult> Send([FromBody] ContactRequest request, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            if (!string.IsNullOrWhiteSpace(request.Website))
            {
                // the honeypot: answer exactly as for a real message so a robot learns nothing, and send nothing
                return Accepted();
            }

            await form.SendAsync(new ContactMessage(request.Name ?? string.Empty, request.Email, request.Message ?? string.Empty), cancellationToken);
            return Accepted();
        });
}
