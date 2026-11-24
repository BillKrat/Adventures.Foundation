namespace Adventures.Entities;

/// <summary>Sends one plain-text mail from the site (the E-mail plug-in settings decide the server). Throws a business-rule exception, never a raw transport error, when mail is not set up or fails.</summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}
