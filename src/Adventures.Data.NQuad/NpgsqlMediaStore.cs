// Assessed 2026-10-24 17:37 CT (Claude): automated scan for secrets, injection and unsafe patterns - no finding in this file. See docs/artifacts/Claude-2026-10-24-security-practices-patterns-review.md (ai-research-blog).
using Adventures.Entities;
using Dapper;
using Npgsql;

namespace Adventures.Data.NQuad;

/// <summary>
/// Keeps uploaded images in a PostgreSQL table (<c>app_media</c>) next to the N-Quad store: they survive a redeploy, travel with the
/// database backup, and an installation needs no writable folder. The bytes are stored once per distinct image (unique SHA-256).
/// </summary>
public sealed class NpgsqlMediaStore(string connectionString, string tableName = "app_media") : IMediaStore
{
    private readonly string _table = ValidTable(tableName);
    private bool _created;

    private static string ValidTable(string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z_][a-z0-9_]{0,62}$") ? name : throw new ArgumentException("Table name must be a lower-case identifier.", nameof(name));

    public async Task<MediaInfo> SaveAsync(string fileName, string contentType, byte[] data, string uploadedBy, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var hash = MediaRules.Sha256Of(data);
        var existing = await FindByHashAsync(connection, hash, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var info = new MediaInfo(Guid.NewGuid(), fileName, contentType, data.Length, hash, uploadedBy, DateTimeOffset.UtcNow);
        await connection.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO {_table} (id, file_name, content_type, length, sha256, uploaded_by, uploaded_at, data) VALUES (@Id, @FileName, @ContentType, @Length, @Sha256, @UploadedBy, @UploadedAt, @Data) ON CONFLICT (sha256) DO NOTHING",
            new { info.Id, info.FileName, info.ContentType, info.Length, info.Sha256, info.UploadedBy, info.UploadedAt, Data = data },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        // a concurrent upload of the same bytes wins the unique key: return whichever row holds them
        return await FindByHashAsync(connection, hash, cancellationToken).ConfigureAwait(false) ?? info;
    }

    public async Task<MediaFile?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT id, file_name AS FileName, content_type AS ContentType, length, sha256, uploaded_by AS UploadedBy, uploaded_at AS UploadedAt, data FROM {_table} WHERE id = @id",
            new { id }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return row is null ? null : new MediaFile(row.ToInfo(), row.Data);
    }

    public async Task<IReadOnlyList<MediaInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"SELECT id, file_name AS FileName, content_type AS ContentType, length, sha256, uploaded_by AS UploadedBy, uploaded_at AS UploadedAt FROM {_table} ORDER BY uploaded_at DESC",
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.Select(r => r.ToInfo()).ToList();
    }

    private async Task<MediaInfo?> FindByHashAsync(NpgsqlConnection connection, string hash, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT id, file_name AS FileName, content_type AS ContentType, length, sha256, uploaded_by AS UploadedBy, uploaded_at AS UploadedAt FROM {_table} WHERE sha256 = @hash",
            new { hash }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return row?.ToInfo();
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (!_created)
        {
            await connection.ExecuteAsync(new CommandDefinition($"""
                CREATE TABLE IF NOT EXISTS {_table} (
                    id UUID PRIMARY KEY,
                    file_name TEXT NOT NULL,
                    content_type TEXT NOT NULL,
                    length INT NOT NULL,
                    sha256 TEXT NOT NULL UNIQUE,
                    uploaded_by TEXT NOT NULL,
                    uploaded_at TIMESTAMPTZ NOT NULL,
                    data BYTEA NOT NULL
                );
                """, cancellationToken: cancellationToken)).ConfigureAwait(false);
            _created = true;
        }

        return connection;
    }

    private sealed class Row
    {
        public Guid Id { get; set; }

        public string FileName { get; set; } = string.Empty;

        public string ContentType { get; set; } = string.Empty;

        public int Length { get; set; }

        public string Sha256 { get; set; } = string.Empty;

        public string UploadedBy { get; set; } = string.Empty;

        public DateTimeOffset UploadedAt { get; set; }

        public byte[] Data { get; set; } = [];

        public MediaInfo ToInfo() => new(Id, FileName, ContentType, Length, Sha256, UploadedBy, UploadedAt);
    }
}
