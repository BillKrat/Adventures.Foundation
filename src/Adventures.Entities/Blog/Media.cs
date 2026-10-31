// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using System.Security.Cryptography;

namespace Adventures.Entities;

/// <summary>The description of one stored image (everything but its bytes).</summary>
public sealed record MediaInfo(Guid Id, string FileName, string ContentType, int Length, string Sha256, string UploadedBy, DateTimeOffset UploadedAt);

/// <summary>An image to serve: its description and bytes.</summary>
public sealed record MediaFile(MediaInfo Info, byte[] Data);

/// <summary>What the upload rules decided about a file.</summary>
public sealed record MediaCheck(bool IsAccepted, string? Error, string ContentType, string FileName);

/// <summary>
/// Which uploads are accepted as images, and nothing else. An upload is trusted for none of what the client claims: the type must be
/// one of a short list of raster formats, the first bytes must actually be that format (a renamed script or an HTML page is refused),
/// the size is capped, and the stored file name is rebuilt from letters and digits with the extension of the real type. SVG is not
/// accepted: it can carry script.
/// </summary>
public static class MediaRules
{
    /// <summary>The largest image accepted, in bytes (5 MB).</summary>
    public const int MaxBytes = 5 * 1024 * 1024;

    public static MediaCheck Check(string? fileName, string? claimedContentType, ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return Refuse("The file is empty.");
        }

        if (data.Length > MaxBytes)
        {
            return Refuse($"The image is larger than {MaxBytes / (1024 * 1024)} MB.");
        }

        var (type, extension) = Sniff(data);
        if (type is null)
        {
            return Refuse("Only PNG, JPEG, GIF and WebP images are accepted.");
        }

        if (!string.IsNullOrWhiteSpace(claimedContentType) && !string.Equals(claimedContentType.Split(';')[0].Trim(), type, StringComparison.OrdinalIgnoreCase))
        {
            return Refuse("The file is not the type it says it is.");
        }

        return new MediaCheck(true, null, type, SafeName(fileName, extension));
    }

    /// <summary>"My Photo (1).PNG" becomes "my-photo-1.png": lower case letters and digits, single hyphens, the extension of the real type.</summary>
    public static string SafeName(string? fileName, string extension)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
        var name = PageBll.Slugify(stem);
        if (name.Length > 60)
        {
            name = name[..60].Trim('-');
        }

        return (name.Length == 0 ? "image" : name) + extension;
    }

    public static string Sha256Of(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static (string? Type, string Extension) Sniff(ReadOnlySpan<byte> d)
    {
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47 && d[4] == 0x0D && d[5] == 0x0A && d[6] == 0x1A && d[7] == 0x0A)
        {
            return ("image/png", ".png");
        }

        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF)
        {
            return ("image/jpeg", ".jpg");
        }

        if (d.Length >= 6 && d[0] == (byte)'G' && d[1] == (byte)'I' && d[2] == (byte)'F' && d[3] == (byte)'8' && (d[4] == (byte)'7' || d[4] == (byte)'9') && d[5] == (byte)'a')
        {
            return ("image/gif", ".gif");
        }

        if (d.Length >= 12 && d[0] == (byte)'R' && d[1] == (byte)'I' && d[2] == (byte)'F' && d[3] == (byte)'F' && d[8] == (byte)'W' && d[9] == (byte)'E' && d[10] == (byte)'B' && d[11] == (byte)'P')
        {
            return ("image/webp", ".webp");
        }

        return (null, string.Empty);
    }

    private static MediaCheck Refuse(string error) => new(false, error, string.Empty, string.Empty);
}

/// <summary>Where uploaded images are kept. Saving the same bytes twice returns the first copy (the address of an image never changes).</summary>
public interface IMediaStore
{
    /// <summary>Stores an already-checked image (see <see cref="MediaRules"/>); the same bytes return the existing item.</summary>
    Task<MediaInfo> SaveAsync(string fileName, string contentType, byte[] data, string uploadedBy, CancellationToken cancellationToken = default);

    Task<MediaFile?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every stored image, newest first (no bytes).</summary>
    Task<IReadOnlyList<MediaInfo>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>Keeps images in memory: for tests and the in-memory development store.</summary>
public sealed class InMemoryMediaStore : IMediaStore
{
    private readonly List<MediaFile> _files = [];

    public Task<MediaInfo> SaveAsync(string fileName, string contentType, byte[] data, string uploadedBy, CancellationToken cancellationToken = default)
    {
        lock (_files)
        {
            var hash = MediaRules.Sha256Of(data);
            var existing = _files.FirstOrDefault(f => f.Info.Sha256 == hash);
            if (existing is not null)
            {
                return Task.FromResult(existing.Info);
            }

            var info = new MediaInfo(Guid.NewGuid(), fileName, contentType, data.Length, hash, uploadedBy, DateTimeOffset.UtcNow);
            _files.Add(new MediaFile(info, data));
            return Task.FromResult(info);
        }
    }

    public Task<MediaFile?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_files)
        {
            return Task.FromResult(_files.FirstOrDefault(f => f.Info.Id == id));
        }
    }

    public Task<IReadOnlyList<MediaInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        lock (_files)
        {
            return Task.FromResult<IReadOnlyList<MediaInfo>>(_files.Select(f => f.Info).OrderByDescending(i => i.UploadedAt).ToList());
        }
    }
}
