using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Adventures.Security;

/// <summary>
/// Encrypts a value a person typed that the system must later use but never show again (a webhook address, an API token). The key lives outside
/// the store (a host secret). The stored text is <c>enc1:{keyId}:{base64}</c>, so keys can rotate and a value always says which key made it.
/// </summary>
public interface IFieldEncryptor
{
    /// <summary>True when a key is set. With no key nothing is encrypted and <see cref="Encrypt"/> refuses: a secret is never stored as plain text.</summary>
    bool IsConfigured { get; }

    /// <summary>Encrypts <paramref name="plainText"/> bound to <paramref name="context"/> (tenant, purpose and field). Empty stays empty. Throws when no key is set.</summary>
    string Encrypt(string plainText, string context);

    /// <summary>The plain text, or null when the value is not encrypted by us, the key is unknown or gone, the context differs or the value was changed. Never throws on bad data.</summary>
    string? TryDecrypt(string stored, string context);

    /// <summary>True when the text has the marker of an encrypted value.</summary>
    bool IsEncrypted(string stored);

    /// <summary>True when the value was made with a key other than the current one (re-save it to move it to the current key).</summary>
    bool NeedsReEncryption(string stored);
}

/// <summary>Where the keys come from: <c>Encryption:CurrentKeyId</c> and <c>Encryption:Keys:{id}</c> (host secrets <c>Encryption__CurrentKeyId</c>, <c>Encryption__Keys__{id}</c>).</summary>
public sealed class FieldEncryptionOptions
{
    public string? CurrentKeyId { get; set; }

    public Dictionary<string, string> Keys { get; set; } = new();
}

/// <summary>AES-256-GCM with a random 96-bit nonce per value; the context is authenticated, so a copy moved to another field or tenant does not decrypt.</summary>
public sealed partial class AesGcmFieldEncryptor : IFieldEncryptor
{
    private const string Marker = "enc1:";
    private const int MinimumKeyLength = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] KeyInfo = Encoding.UTF8.GetBytes("adventures-field-encryption-v1");

    private readonly Dictionary<string, byte[]> _keys = new(StringComparer.Ordinal);
    private readonly string? _currentKeyId;

    [GeneratedRegex("^[A-Za-z0-9_-]{1,32}$")]
    private static partial Regex KeyIdPattern();

    public AesGcmFieldEncryptor(FieldEncryptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        foreach (var (id, material) in options.Keys)
        {
            if (!KeyIdPattern().IsMatch(id ?? string.Empty))
            {
                throw new InvalidOperationException("Encryption key ids may use letters, digits, '-' and '_' (1 to 32 characters).");
            }

            if (string.IsNullOrEmpty(material) || material.Length < MinimumKeyLength)
            {
                throw new InvalidOperationException($"Encryption key '{id}' is too short: use at least {MinimumKeyLength} characters (for example 32 random bytes as base64).");
            }

            _keys[id!] = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(material), 32, salt: null, info: KeyInfo);
        }

        if (!string.IsNullOrWhiteSpace(options.CurrentKeyId))
        {
            if (!_keys.ContainsKey(options.CurrentKeyId))
            {
                throw new InvalidOperationException($"Encryption:CurrentKeyId '{options.CurrentKeyId}' has no matching key under Encryption:Keys.");
            }

            _currentKeyId = options.CurrentKeyId;
        }
        else if (_keys.Count > 0)
        {
            throw new InvalidOperationException("Encryption:Keys is set but Encryption:CurrentKeyId is not.");
        }
    }

    public bool IsConfigured => _currentKeyId is not null;

    public bool IsEncrypted(string stored) => !string.IsNullOrEmpty(stored) && stored.StartsWith(Marker, StringComparison.Ordinal);

    public bool NeedsReEncryption(string stored) =>
        IsConfigured && TryParse(stored, out var keyId, out _) && !string.Equals(keyId, _currentKeyId, StringComparison.Ordinal);

    public string Encrypt(string plainText, string context)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }

        if (_currentKeyId is null)
        {
            throw new InvalidOperationException("Encryption is not configured: set Encryption:CurrentKeyId and Encryption:Keys:<id> in the host settings before saving a credential. Nothing was stored.");
        }

        var plain = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(_keys[_currentKeyId], TagSize))
        {
            aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(context ?? string.Empty));
        }

        var packed = new byte[NonceSize + cipher.Length + TagSize];
        nonce.CopyTo(packed, 0);
        cipher.CopyTo(packed, NonceSize);
        tag.CopyTo(packed, NonceSize + cipher.Length);
        CryptographicOperations.ZeroMemory(plain);
        return $"{Marker}{_currentKeyId}:{Convert.ToBase64String(packed)}";
    }

    public string? TryDecrypt(string stored, string context)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return string.Empty;
        }

        if (!TryParse(stored, out var keyId, out var body) || !_keys.TryGetValue(keyId, out var key))
        {
            return null;
        }

        try
        {
            var packed = Convert.FromBase64String(body);
            if (packed.Length < NonceSize + TagSize)
            {
                return null;
            }

            var cipherLength = packed.Length - NonceSize - TagSize;
            var plain = new byte[cipherLength];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                packed.AsSpan(0, NonceSize),
                packed.AsSpan(NonceSize, cipherLength),
                packed.AsSpan(NonceSize + cipherLength, TagSize),
                plain,
                Encoding.UTF8.GetBytes(context ?? string.Empty));
            var text = Encoding.UTF8.GetString(plain);
            CryptographicOperations.ZeroMemory(plain);
            return text;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static bool TryParse(string stored, out string keyId, out string body)
    {
        keyId = body = string.Empty;
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Marker, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = stored[Marker.Length..];
        var colon = rest.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        keyId = rest[..colon];
        body = rest[(colon + 1)..];
        return KeyIdPattern().IsMatch(keyId);
    }
}

public static class FieldEncryptionServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IFieldEncryptor"/> from the <c>Encryption</c> configuration section. With no settings it registers a not-configured encryptor (the app starts, secrets
    /// are refused); a bad setting fails here, at start-up, not when the first secret is saved.
    /// </summary>
    public static IServiceCollection AddFieldEncryption(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("Encryption");
        var options = new FieldEncryptionOptions { CurrentKeyId = section["CurrentKeyId"] };
        foreach (var key in section.GetSection("Keys").GetChildren())
        {
            if (!string.IsNullOrEmpty(key.Value))
            {
                options.Keys[key.Key] = key.Value;
            }
        }

        services.AddSingleton<IFieldEncryptor>(new AesGcmFieldEncryptor(options));
        return services;
    }
}
