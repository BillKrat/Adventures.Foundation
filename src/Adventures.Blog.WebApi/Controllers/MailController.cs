using Adventures.Blog;
using Adventures.Entities;
using Adventures.WebApi;
using Microsoft.AspNetCore.Mvc;

namespace Adventures.Blog.WebApi;

public sealed record MailTestRequest(string? To);

/// <summary>
/// Proves the saved mail settings with one test message to an address the administrator types. It needs the site-settings right, sends to that one address
/// only, and never returns the password or the server's own words.
/// </summary>
[ApiController]
[Route("api/mail")]
[RequireRight(Rights.SettingsManage)]
public sealed class MailController(IMailer mailer) : ApiControllerBase
{
    [HttpPost("test")]
    public Task<IActionResult> Test([FromBody] MailTestRequest request, CancellationToken cancellationToken) =>
        GuardedAsync(async () =>
        {
            await mailer.SendTestAsync(request.To ?? string.Empty, cancellationToken);
            return NoContent();
        });
}
