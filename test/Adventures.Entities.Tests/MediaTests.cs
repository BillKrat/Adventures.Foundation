using Xunit;

namespace Adventures.Entities.Tests;

/// <summary>Only real raster images are accepted, under a rebuilt safe name and a size cap; the same bytes are stored once.</summary>
public sealed class MediaTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F'];
    private static readonly byte[] Gif = "GIF89a\x01\x00\x01\x00"u8.ToArray();
    private static readonly byte[] Webp = "RIFF\x24\x00\x00\x00WEBPVP8 "u8.ToArray();

    [Theory]
    [InlineData("photo.PNG", "image/png", "photo.png")]
    [InlineData("My Photo (1).jpeg", "image/jpeg", "my-photo-1.jpg")]
    [InlineData("anim.gif", "image/gif", "anim.gif")]
    [InlineData("pic", "image/webp", "pic.webp")]
    public void ARealImage_IsAccepted_UnderASafeNameWithTheRealExtension(string fileName, string claimed, string expectedName)
    {
        var data = claimed switch { "image/png" => Png, "image/jpeg" => Jpeg, "image/gif" => Gif, _ => Webp };

        var check = MediaRules.Check(fileName, claimed, data);

        Assert.True(check.IsAccepted, check.Error);
        Assert.Equal(claimed, check.ContentType);
        Assert.Equal(expectedName, check.FileName);
    }

    [Fact]
    public void ANameThatIsAPathOrScript_IsRebuiltFromLettersAndDigitsOnly()
    {
        var check = MediaRules.Check("..\\..\\<script>alert(1)</script>.html.png", "image/png", Png);

        Assert.True(check.IsAccepted);
        Assert.Matches("^[a-z0-9-]+\\.png$", check.FileName);
        Assert.DoesNotContain("..", check.FileName);
        Assert.Equal("image.png", MediaRules.Check(null, null, Png).FileName);
        Assert.True(MediaRules.Check("x.png", null, Png).IsAccepted);   // no claimed type: the bytes decide
    }

    [Fact]
    public void ScriptsHtmlSvgAndRenamedFilesAreRefused_WhateverTheyClaim()
    {
        var html = "<html><script>alert(1)</script></html>"u8.ToArray();
        var svg = "<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"u8.ToArray();

        Assert.False(MediaRules.Check("a.png", "image/png", html).IsAccepted);
        Assert.False(MediaRules.Check("a.svg", "image/svg+xml", svg).IsAccepted);
        Assert.False(MediaRules.Check("a.png", "image/png", Jpeg).IsAccepted);   // a JPEG claiming to be a PNG
        Assert.Contains("not the type", MediaRules.Check("a.png", "image/png", Jpeg).Error);
        Assert.False(MediaRules.Check("a.png", "image/png", []).IsAccepted);
        Assert.False(MediaRules.Check("a.png", "image/png", new byte[MediaRules.MaxBytes + 1]).IsAccepted);
    }

    [Fact]
    public async Task TheSameBytesAreStoredOnce_AndAnImageCanBeReadBack()
    {
        IMediaStore store = new InMemoryMediaStore();

        var first = await store.SaveAsync("a.png", "image/png", Png, "Admin");
        var again = await store.SaveAsync("other-name.png", "image/png", Png, "Claude");
        var other = await store.SaveAsync("b.jpg", "image/jpeg", Jpeg, "Admin");

        Assert.Equal(first.Id, again.Id);
        Assert.NotEqual(first.Id, other.Id);
        Assert.Equal(2, (await store.ListAsync()).Count);
        var file = await store.GetAsync(first.Id);
        Assert.Equal(Png, file!.Data);
        Assert.Equal("image/png", file.Info.ContentType);
        Assert.Null(await store.GetAsync(Guid.NewGuid()));
    }
}
