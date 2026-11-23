using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Adventures.Common.Exceptions;

namespace Adventures.Entities;

/// <summary>
/// Append-only statements about a person's e-mail verification: <c>token</c> (<c>{created ticks};{expires ticks};{token hash};{address hash}</c>), <c>used</c> (a token hash) and
/// <c>verified</c> (<c>{ticks};{address hash}</c>). Nothing is ever rewritten, so a change of address needs no clean-up: a verified statement counts only for the address it names.
/// </summary>
public interface IEmailVerificationStore
{
    Task AddAsync(string userId, string kind, string value, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListAsync(string userId, string kind, CancellationToken cancellationToken = default);
}

/// <summary>
/// The verification cycle: mail a single-use link (valid 24 hours, token kept only as a SHA-256 hash and bound to the address it was sent to), mark that address verified when the link
/// is opened, and limit a person to three mails an hour. Password recovery by mail will only ever use a verified address.
/// </summary>
public sealed partial class EmailVerificationService(IEmailVerificationStore store, IEmailSender sender, TimeProvider? clock = null)
{
    public const string Token = "token";
    public const string Used = "used";
    public const string Verified = "verified";
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan RateWindow = TimeSpan.FromHours(1);
    public const int MailsPerWindow = 3;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    [GeneratedRegex(@"^[^\s@,;<>""]{1,64}@[^\s@,;<>""]+\.[^\s@,;<>""]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AddressPattern();

    public async Task RequestAsync(string userId, string address, string linkBase, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        address = (address ?? string.Empty).Trim();
        if (address.Length is 0 or > 254 || !AddressPattern().IsMatch(address))
        {
            throw new BusinessRuleException("Save one e-mail address on your profile first, such as owner@example.org.");
        }

        var now = _clock.GetUtcNow();
        var recent = (await store.ListAsync(userId, Token, cancellationToken).ConfigureAwait(false))
            .Select(ParseToken).Count(t => t is not null && t.Value.Created > now.Subtract(RateWindow).UtcTicks);
        if (recent >= MailsPerWindow)
        {
            throw new BusinessRuleException("Too many verification mails were requested. Try again in an hour.");
        }

        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var line = string.Create(CultureInfo.InvariantCulture, $"{now.UtcTicks};{now.Add(Lifetime).UtcTicks};{Hash(token)};{Hash(Normalize(address))}");
        await store.AddAsync(userId, Token, line, cancellationToken).ConfigureAwait(false);

        var body = $"Open this link to confirm that this address is yours (it works once and for 24 hours):\r\n\r\n{linkBase}?user={Uri.EscapeDataString(userId)}&token={token}\r\n\r\nIf you did not ask for this, ignore this mail.";
        await sender.SendAsync(address, "Confirm your e-mail address", body, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Marks the address the token was sent to as verified. False (with no reason given) for a wrong, expired, used or another person's token.</summary>
    public async Task<bool> ConfirmAsync(string userId, string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var tokenHash = Hash(token);
        var now = _clock.GetUtcNow().UtcTicks;
        var used = (await store.ListAsync(userId, Used, cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        foreach (var line in await store.ListAsync(userId, Token, cancellationToken).ConfigureAwait(false))
        {
            var parsed = ParseToken(line);
            if (parsed is null || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parsed.Value.TokenHash), Encoding.ASCII.GetBytes(tokenHash)))
            {
                continue;
            }

            if (parsed.Value.Expires < now || used.Contains(tokenHash))
            {
                return false;
            }

            await store.AddAsync(userId, Used, tokenHash, cancellationToken).ConfigureAwait(false);
            await store.AddAsync(userId, Verified, string.Create(CultureInfo.InvariantCulture, $"{now};{parsed.Value.AddressHash}"), cancellationToken).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    /// <summary>Whether this exact address (case ignored) is the one the person has proved.</summary>
    public async Task<bool> IsVerifiedAsync(string userId, string address, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        var wanted = Hash(Normalize(address));
        return (await store.ListAsync(userId, Verified, cancellationToken).ConfigureAwait(false)).Any(line => line.Split(';', 2) is [_, var hash] && hash == wanted);
    }

    /// <summary>The form an address is compared in: trimmed, lower case.</summary>
    public static string Normalize(string address) => address.Trim().ToLowerInvariant();

    /// <summary>SHA-256 as upper-case hex (tokens and addresses are only ever stored in this form).</summary>
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd((char)61).Replace((char)43, (char)45).Replace((char)47, (char)95);

    public static (long Created, long Expires, string TokenHash, string AddressHash)? ParseToken(string line)
    {
        var parts = line.Split(';', 4);
        return parts.Length == 4 && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var created) && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var expires)
            ? (created, expires, parts[2], parts[3])
            : null;
    }
}
