using Adventures.Data.Sql;

namespace Adventures.Blog.Legacy;

/// <summary>Which BlogEngine blog of the database the host serves (a BlogEngine database can hold several; every table row carries a BlogID).</summary>
public interface ILegacyBlog
{
    Task<Guid> BlogIdAsync(CancellationToken cancellationToken = default);
}

/// <summary>The configured blog id, or the primary blog (<c>be_Blogs.IsPrimary</c>) when none is configured. Resolved once and kept.</summary>
public sealed class LegacyBlog(ISqlServerExecutor sql, Guid? configuredBlogId = null) : ILegacyBlog
{
    private Guid? _blogId = configuredBlogId is { } id && id != Guid.Empty ? id : null;

    public async Task<Guid> BlogIdAsync(CancellationToken cancellationToken = default)
    {
        if (_blogId is { } known)
        {
            return known;
        }

        var primary = await sql.QuerySingleOrDefaultAsync<Guid?>(
            "SELECT TOP 1 BlogId FROM be_Blogs WHERE IsPrimary = 1 AND IsActive = 1 ORDER BY BlogRowId", null, cancellationToken).ConfigureAwait(false);
        _blogId = primary ?? throw new InvalidOperationException("The legacy database has no active primary blog in be_Blogs; set Legacy:BlogId.");
        return _blogId.Value;
    }
}

/// <summary>Ids the legacy tables do not have: a tag is only a string in <c>be_PostTag</c>, so its entity id is derived from the name (RFC 4122 version 5, the same derivation the importer uses, so a tag keeps one id in both modes).</summary>
public static class LegacyIds
{
    private static readonly Guid Namespace = new("6f1c2b8e-3d54-4a0b-9a77-5e0a1c4d8b92");

    public static Guid ForTagName(string name) => Version5(Namespace, "live-tag:" + name.Trim().ToLowerInvariant());

    public static Guid Version5(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapByteOrder(namespaceBytes);
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(input, 0);
        nameBytes.CopyTo(input, namespaceBytes.Length);
        var hash = System.Security.Cryptography.SHA1.HashData(input);
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        SwapByteOrder(bytes);
        return new Guid(bytes);
    }

    private static void SwapByteOrder(byte[] guid)
    {
        (guid[0], guid[3]) = (guid[3], guid[0]);
        (guid[1], guid[2]) = (guid[2], guid[1]);
        (guid[4], guid[5]) = (guid[5], guid[4]);
        (guid[6], guid[7]) = (guid[7], guid[6]);
    }
}
