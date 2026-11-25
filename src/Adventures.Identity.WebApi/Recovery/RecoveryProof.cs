using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Adventures.Identity.WebApi;

/// <summary>A one-time recovery challenge: put a file named <see cref="FileName"/> into the folder <see cref="Folder"/> next to the app (App_Data), before <see cref="ExpiresAtUtc"/>.</summary>
public sealed record RecoveryChallenge(string Code, string FileName, string Folder, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Proof that the person controls the installation, with no e-mail: the app hands out a one-time code and the person puts a file named with it on the host (FTP, the hosting
/// panel's file manager). Only someone who can write files there can do that, which is the trust boundary of a self-hosted install. Codes are kept in memory only: a restart
/// forgets them, so a stale file proves nothing.
/// </summary>
public interface IRecoveryProof
{
    RecoveryChallenge Start();

    /// <summary>True when the code was issued here, has not expired or been used, and its file exists.</summary>
    bool IsProven(string? code);

    /// <summary>Uses the code up: removes its file (best effort) and forgets it and every other pending code.</summary>
    void Consume(string code);
}

public sealed partial class FileRecoveryProof(string folder, TimeProvider? clock = null) : IRecoveryProof
{
    public const string FolderName = "App_Data";
    public const string FilePrefix = "recovery-";
    public const int MaxPending = 20;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly List<(string Code, DateTimeOffset Expires)> _pending = [];

    public RecoveryChallenge Start()
    {
        Directory.CreateDirectory(folder);   // so the person has a place to put the file
        var code = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var expires = _clock.GetUtcNow().Add(Lifetime);
        lock (_gate)
        {
            Prune();
            while (_pending.Count >= MaxPending)
            {
                _pending.RemoveAt(0);   // an anonymous endpoint must not be able to grow this without limit
            }

            _pending.Add((code, expires));
        }

        return new RecoveryChallenge(code, FilePrefix + code, FolderName, expires);
    }

    public bool IsProven(string? code)
    {
        if (string.IsNullOrEmpty(code) || !HexCode().IsMatch(code))
        {
            return false;   // the code builds a file path: nothing but 32 lower-case hex characters is ever used
        }

        lock (_gate)
        {
            Prune();
            if (!_pending.Any(p => p.Code == code))
            {
                return false;
            }
        }

        var path = Path.Combine(folder, FilePrefix + code);
        return File.Exists(path) || File.Exists(path + ".txt");
    }

    public void Consume(string code)
    {
        if (!string.IsNullOrEmpty(code) && HexCode().IsMatch(code))
        {
            var path = Path.Combine(folder, FilePrefix + code);
            foreach (var file in new[] { path, path + ".txt" })
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // the code is forgotten below either way, so a file that stays proves nothing
                }
            }
        }

        lock (_gate)
        {
            _pending.Clear();
        }
    }

    private void Prune()
    {
        var now = _clock.GetUtcNow();
        _pending.RemoveAll(p => p.Expires <= now);
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex HexCode();
}
