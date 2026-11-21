using Adventures.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Adventures.Security.Tests;

/// <summary>
/// Encrypted field values: a credential stored by the system is encrypted with AES-256-GCM under a key that lives outside the store (a host secret),
/// the stored text names the key that made it (so keys can rotate), and it is bound to a context (tenant, purpose and field) so a copy moved to another
/// field or tenant does not decrypt. Nothing is ever stored as plain text when no key is set, and a value that cannot be read is simply "not set".
/// </summary>
public sealed class FieldEncryptionTests
{
    private const string KeyOne = "k1-0123456789abcdef0123456789abcdef-aaaa";
    private const string KeyTwo = "k2-fedcba9876543210fedcba9876543210-bbbb";
    private const string Context = "plugin-setting:tenant-a:slack:WebhookUrl";

    private static AesGcmFieldEncryptor Encryptor(string current, params (string Id, string Key)[] keys) =>
        new(new FieldEncryptionOptions { CurrentKeyId = current, Keys = keys.ToDictionary(k => k.Id, k => k.Key) });

    [Fact]
    public void ASecret_RoundTrips_AndTheStoredTextIsNotThePlainText()
    {
        var encryptor = Encryptor("k1", ("k1", KeyOne));

        var stored = encryptor.Encrypt("https://hooks.example.test/services/T000/B000/hunter2", Context);

        Assert.StartsWith("enc1:k1:", stored);
        Assert.DoesNotContain("hunter2", stored);
        Assert.DoesNotContain("hooks.example", stored);
        Assert.Equal("https://hooks.example.test/services/T000/B000/hunter2", encryptor.TryDecrypt(stored, Context));
        Assert.True(encryptor.IsEncrypted(stored));
    }

    [Fact]
    public void TheSameSecret_EncryptsDifferentlyEachTime_BecauseEveryEncryptionHasItsOwnNonce()
    {
        var encryptor = Encryptor("k1", ("k1", KeyOne));

        Assert.NotEqual(encryptor.Encrypt("same", Context), encryptor.Encrypt("same", Context));
    }

    [Fact]
    public void ACopyMovedToAnotherFieldOrTenant_DoesNotDecrypt()
    {
        var encryptor = Encryptor("k1", ("k1", KeyOne));
        var stored = encryptor.Encrypt("secret", Context);

        Assert.Null(encryptor.TryDecrypt(stored, "plugin-setting:tenant-b:slack:WebhookUrl"));
        Assert.Null(encryptor.TryDecrypt(stored, "plugin-setting:tenant-a:slack:Token"));
        Assert.Null(encryptor.TryDecrypt(stored, string.Empty));
    }

    [Fact]
    public void ATamperedOrTruncatedValue_DoesNotDecrypt_AndNeverThrows()
    {
        var encryptor = Encryptor("k1", ("k1", KeyOne));
        var stored = encryptor.Encrypt("secret", Context);
        var body = stored["enc1:k1:".Length..];
        var flipped = "enc1:k1:" + (body[0] == 'A' ? 'B' : 'A') + body[1..];

        Assert.Null(encryptor.TryDecrypt(flipped, Context));
        Assert.Null(encryptor.TryDecrypt(stored[..^6], Context));
        Assert.Null(encryptor.TryDecrypt("enc1:k1:", Context));
        Assert.Null(encryptor.TryDecrypt("enc1:k1:not-base64!!", Context));
        Assert.Null(encryptor.TryDecrypt("enc1:k1", Context));
    }

    [Fact]
    public void AValueWithoutTheMarker_IsNotMistakenForACiphertext_SoStoredPlainTextIsNeverReturned()
    {
        var encryptor = Encryptor("k1", ("k1", KeyOne));

        Assert.False(encryptor.IsEncrypted("plain text a person typed"));
        Assert.Null(encryptor.TryDecrypt("plain text a person typed", Context));
    }

    [Fact]
    public void TheWrongKey_DoesNotDecrypt()
    {
        var stored = Encryptor("k1", ("k1", KeyOne)).Encrypt("secret", Context);

        Assert.Null(Encryptor("k1", ("k1", KeyTwo)).TryDecrypt(stored, Context));
        Assert.Null(Encryptor("k2", ("k2", KeyTwo)).TryDecrypt(stored, Context));
    }

    [Fact]
    public void WhenKeysRotate_OldValuesStillDecrypt_NewOnesUseTheNewKey_AndOldOnesAreFlaggedForReEncryption()
    {
        var old = Encryptor("k1", ("k1", KeyOne)).Encrypt("secret", Context);
        var rotated = Encryptor("k2", ("k1", KeyOne), ("k2", KeyTwo));

        Assert.Equal("secret", rotated.TryDecrypt(old, Context));
        Assert.True(rotated.NeedsReEncryption(old));
        var fresh = rotated.Encrypt("secret", Context);
        Assert.StartsWith("enc1:k2:", fresh);
        Assert.False(rotated.NeedsReEncryption(fresh));
    }

    [Fact]
    public void WithNoKey_NothingIsEverEncryptedOrStored_AndItSaysWhy()
    {
        var none = new AesGcmFieldEncryptor(new FieldEncryptionOptions());

        Assert.False(none.IsConfigured);
        var refusal = Assert.Throws<InvalidOperationException>(() => none.Encrypt("secret", Context));
        Assert.Contains("Encryption", refusal.Message);
        Assert.DoesNotContain("secret", refusal.Message);
    }

    [Fact]
    public void ACurrentKeyThatIsNotInTheKeyList_IsAConfigurationError()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Encryptor("k9", ("k1", KeyOne)));

        Assert.Contains("k9", error.Message);
    }

    [Fact]
    public void AKeyThatIsTooShortToBeSecure_IsRefused_WithoutEchoingIt()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Encryptor("k1", ("k1", "short-key")));

        Assert.DoesNotContain("short-key", error.Message);
        Assert.Contains("32", error.Message);
    }

    [Fact]
    public void ABadKeyId_IsRefused_BecauseItIsPartOfTheStoredFormat()
    {
        Assert.Throws<InvalidOperationException>(() => Encryptor("k:1", ("k:1", KeyOne)));
        Assert.Throws<InvalidOperationException>(() => Encryptor("", ("", KeyOne)));
    }

    [Fact]
    public void AnEmptySecret_StaysEmpty_SoClearingAValueNeedsNoKey()
    {
        var none = new AesGcmFieldEncryptor(new FieldEncryptionOptions());

        Assert.Equal(string.Empty, none.Encrypt(string.Empty, Context));
        Assert.Equal(string.Empty, none.TryDecrypt(string.Empty, Context));
        Assert.False(none.IsEncrypted(string.Empty));
    }

    [Fact]
    public void ALongUnicodeSecret_RoundTrips()
    {
        var encryptor = Encryptor("k1", ("k1", KeyOne));
        var secret = string.Concat(Enumerable.Repeat("pässwörd-密码-🔑-", 200));

        Assert.Equal(secret, encryptor.TryDecrypt(encryptor.Encrypt(secret, Context), Context));
    }

    [Fact]
    public void TheKeysComeFromConfiguration_ThatIsHostSecretsLikeEncryption__Keys__k1()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:CurrentKeyId"] = "k2",
            ["Encryption:Keys:k1"] = KeyOne,
            ["Encryption:Keys:k2"] = KeyTwo,
        }).Build();
        var services = new ServiceCollection().AddFieldEncryption(configuration);

        var encryptor = services.BuildServiceProvider().GetRequiredService<IFieldEncryptor>();

        Assert.True(encryptor.IsConfigured);
        Assert.StartsWith("enc1:k2:", encryptor.Encrypt("x", Context));
    }

    [Fact]
    public void WithNoEncryptionSettings_TheServiceIsRegisteredAsNotConfigured_SoTheAppStartsAndSecretsAreRefused()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection().AddFieldEncryption(configuration);

        var encryptor = services.BuildServiceProvider().GetRequiredService<IFieldEncryptor>();

        Assert.False(encryptor.IsConfigured);
    }

    [Fact]
    public void ABadConfiguration_FailsAtStartUpWithAClearMessage_NotWhenTheFirstSecretIsSaved()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:CurrentKeyId"] = "k1",
            ["Encryption:Keys:k1"] = "too-short",
        }).Build();

        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddFieldEncryption(configuration).BuildServiceProvider().GetRequiredService<IFieldEncryptor>());

        Assert.Contains("32", error.Message);
    }
}
