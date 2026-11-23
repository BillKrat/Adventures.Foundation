using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Adventures.Entities;

namespace Adventures.Identity.WebApi;

/// <summary>A person as the reset flow needs them: the id the verification statements are kept under, the sign-in name and the saved address.</summary>
public sealed record ResetPerson(string UserId, string UserName, string Email);

/// <summary>Finds a person of the request tenant by sign-in name or by saved address.</summary>
public interface IResetDirectory
{
    Task<ResetPerson?> FindAsync(string identifier, CancellationToken cancellationToken = default);

    Task<ResetPerson?> GetAsync(string userId, CancellationToken cancellationToken = default);
}

public enum PasswordResetResult
{
    Done,
    InvalidLink,
    WeakPassword,
}

/// <summary>
/// Forgot my password. Asking is silent: the same (nothing) comes back whether or not the person exists, has a verified address, is rate limited or the mail fails, so the form
/// cannot be used to learn who has an account. A link goes only to an address the person has proved (<see cref="EmailVerificationService"/>). The token is 32 random bytes kept
/// as a SHA-256 hash, valid one hour, usable once, bound to the address it was mailed to, and at most three are mailed an hour per person. A reset also needs the address to be
/// still verified and unchanged, so changing the address after the mail went out kills the link.
/// </summary>
public sealed class PasswordResetService(
    IResetDirectory directory,
    EmailVerificationService verification,
    IEmailVerificationStore store,
    IEmailSender sender,
    IPasswordResetter resetter,
    TimeProvider? clock = null)
{
    public const string Token = "reset-token";
    public const string Used = "reset-used";
    public const int MailsPerWindow = 3;
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan RateWindow = TimeSpan.FromHours(1);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task RequestAsync(string identifier, string linkBase, CancellationToken cancellationToken = default)
    {
        try
        {
            identifier = (identifier ?? string.Empty).Trim();
            if (identifier.Length is 0 or > 254)
            {
                return;
            }

            var person = await directory.FindAsync(identifier, cancellationToken).ConfigureAwait(false);
            if (person is null || string.IsNullOrWhiteSpace(person.Email) || !await verification.IsVerifiedAsync(person.UserId, person.Email, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var now = _clock.GetUtcNow();
            var recent = (await store.ListAsync(person.UserId, Token, cancellationToken).ConfigureAwait(false))
                .Select(EmailVerificationService.ParseToken).Count(t => t is not null && t.Value.Created > now.Subtract(RateWindow).UtcTicks);
            if (recent >= MailsPerWindow)
            {
                return;
            }

            var token = EmailVerificationService.NewToken();
            var line = string.Create(CultureInfo.InvariantCulture, $"{now.UtcTicks};{now.Add(Lifetime).UtcTicks};{EmailVerificationService.Hash(token)};{EmailVerificationService.Hash(EmailVerificationService.Normalize(person.Email))}");
            await store.AddAsync(person.UserId, Token, line, cancellationToken).ConfigureAwait(false);

            var body = $"Someone asked to reset the password of your account. Open this link to choose a new one (it works once and for one hour):\r\n\r\n{linkBase}?user={Uri.EscapeDataString(person.UserId)}&token={token}\r\n\r\nIf it was not you, ignore this mail: your password has not changed.";
            await sender.SendAsync(person.Email, "Reset your password", body, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // never tell the asker what went wrong (mail not set up, a store fault): the answer must not depend on the person
        }
    }

    public async Task<PasswordResetResult> ResetAsync(string tenant, string userId, string token, string newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
        {
            return PasswordResetResult.InvalidLink;
        }

        var person = await directory.GetAsync(userId, cancellationToken).ConfigureAwait(false);
        if (person is null || string.IsNullOrWhiteSpace(person.Email))
        {
            return PasswordResetResult.InvalidLink;
        }

        var tokenHash = EmailVerificationService.Hash(token);
        var addressHash = EmailVerificationService.Hash(EmailVerificationService.Normalize(person.Email));
        var now = _clock.GetUtcNow().UtcTicks;
        var used = (await store.ListAsync(userId, Used, cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);

        var valid = false;
        foreach (var line in await store.ListAsync(userId, Token, cancellationToken).ConfigureAwait(false))
        {
            if (EmailVerificationService.ParseToken(line) is { } parsed
                && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parsed.TokenHash), Encoding.ASCII.GetBytes(tokenHash))
                && parsed.Expires >= now && parsed.AddressHash == addressHash && !used.Contains(tokenHash))
            {
                valid = true;
                break;
            }
        }

        if (!valid || !await verification.IsVerifiedAsync(userId, person.Email, cancellationToken).ConfigureAwait(false))
        {
            return PasswordResetResult.InvalidLink;
        }

        if (!PasswordPolicy.IsAcceptable(newPassword ?? string.Empty, string.Empty))
        {
            return PasswordResetResult.WeakPassword;
        }

        await store.AddAsync(userId, Used, tokenHash, cancellationToken).ConfigureAwait(false);
        return await resetter.ResetAsync(tenant, person.UserName, newPassword!, cancellationToken).ConfigureAwait(false) == ResetOutcome.Reset
            ? PasswordResetResult.Done
            : PasswordResetResult.InvalidLink;
    }
}

/// <summary>The resetter of a host that has no account store of its own to change (Legacy mode, the developer sign-in): it never resets anything.</summary>
public sealed class NoPasswordResetter : IPasswordResetter
{
    public Task<ResetOutcome> ResetAsync(string tenant, string username, string newPassword, CancellationToken cancellationToken = default) =>
        Task.FromResult(ResetOutcome.NotFound);
}
