/*
 * File IO utilities: common traps and practical recovery patterns
 * --------------------------------------------------------------
 * What: helpers for staged text replacement, exclusive-open retries, exact byte
 * reads, lexical path containment, and lazy line searches.
 * Shows: file modes/sharing, cancellation, cleanup, partial reads, encoding, path
 * traversal, and the difference between atomic visibility and durability.
 * How: replacement stages a sibling file and renames after closing it; retries
 * classify known transient errors and stop after a bounded number of attempts;
 * exact reads delegate to the BCL's looping implementation; paths are normalized
 * before containment checks; line searches use an iterator instead of ReadAllText.
 * Use:
 *   await FileUtilities.ReplaceTextAsync("notes.txt", "first\nneedle");
 *   foreach (var hit in FileUtilities.FindLines("notes.txt", "needle"))
 *       Console.WriteLine($"{hit.Number}: {hit.Text}");
 *   await using var handle = await FileUtilities.OpenExclusiveAsync("notes.txt");
 * The caller owns returned streams. Parent directories must exist. Unix sharing
 * and rename behavior varies by filesystem; retries there need a caller-supplied
 * classifier. Path containment is lexical, not a defense against symlinks.
 * FileIOTour demonstrates each helper alongside the failure it addresses.
 * Sample files: FileIO/Examples (see its README for expected results).
 * Run `dotnet run` to check the shipped samples automatically. For manual
 * reads, resolve paths from the repo root or AppContext.BaseDirectory; save
 * outputs in a temporary directory to keep these fixtures reusable.
 */

using System.Text;

namespace InterviewToolkit.FileIO;

/// <summary>Small helpers illustrating resource ownership and file consistency tradeoffs.</summary>
public static class FileUtilities
{
    /// <summary>UTF-8 without a BOM; invalid bytes or unpaired surrogates cause an exception.</summary>
    public static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    /// <summary>
    /// Stage beside the destination, close the stream, then rename over it.
    /// Avoids exposing a partially written destination on filesystems with atomic
    /// same-directory rename. Not a transaction or a power-loss durability guarantee;
    /// concurrent writers still need coordination (last successful rename wins).
    /// </summary>
    /// <example>await FileUtilities.ReplaceTextAsync("settings.txt", "enabled=true");</example>
    public static async Task ReplaceTextAsync(string path, string text, CancellationToken cancellationToken = default)
    {
        string destination = Path.GetFullPath(path);
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                byte[] bytes = StrictUtf8.GetBytes(text);
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Disposal above must precede rename, especially on Windows. A final
            // cancellation check avoids publishing a canceled staged write.
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            // Cleanup may itself fail (permissions, device loss); callers must
            // not assume every exception here means the destination is unchanged.
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>
    /// Retry only Windows sharing/lock violations, with bounded attempts and
    /// cancellation. Permission errors, missing files and disk failures propagate.
    /// On Unix errno mappings differ, so the caller must supply a classifier for
    /// its known transient errors rather than retrying every IOException blindly.
    /// </summary>
    /// <returns>A read/write handle owned by the caller; dispose it to release exclusive sharing.</returns>
    /// <remarks>attempts includes the first open. onRetry runs before each delay.</remarks>
    public static async Task<FileStream> OpenExclusiveAsync(string path, int attempts = 5,
        TimeSpan? delay = null, CancellationToken cancellationToken = default,
        Func<IOException, bool>? isTransient = null, Action<int>? onRetry = null)
    {
        if (attempts < 1) throw new ArgumentOutOfRangeException(nameof(attempts));
        var pause = delay ?? TimeSpan.FromMilliseconds(100);
        if (pause < TimeSpan.Zero || pause.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(delay));
        // Low HResult bits contain Windows error codes: 32 is a sharing
        // violation, 33 is a lock violation. Do not infer Unix errors from them.
        isTransient ??= exception => OperatingSystem.IsWindows() && (exception.HResult & 0xffff) is 32 or 33;
        for (int attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception) when (attempt < attempts && isTransient(exception))
            {
                onRetry?.Invoke(attempt);
                await Task.Delay(pause, cancellationToken);
            }
        }
    }

    /// <summary>Read may return fewer bytes than requested, even before EOF.</summary>
    /// <remarks>Leaves the caller-owned stream open and advances its position by count bytes on success.</remarks>
    public static async Task<byte[]> ReadExactlyAsync(Stream stream, int count, CancellationToken cancellationToken = default)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var bytes = new byte[count];
        // Built-in ReadExactlyAsync loops and throws EndOfStreamException if short.
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        return bytes;
    }

    /// <summary>
    /// Lexical containment only. Path.Combine(root, absolutePath) discards root!
    /// This does not defeat symlinks, mount points, or directory replacement races.
    /// </summary>
    /// <returns>An absolute path; this helper does not create a file or directory.</returns>
    public static string ResolveUnderDirectory(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new ArgumentException("Expected a nonempty relative path.", nameof(relativePath));
        string fullRoot = Path.GetFullPath(root);
        string candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        string relative = Path.GetRelativePath(fullRoot, candidate);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
            throw new ArgumentException("Path escapes the directory.", nameof(relativePath));
        return candidate;
    }

    /// <summary>Lazy enumeration bounds memory, but errors can occur during iteration.</summary>
    /// <remarks>Uses ordinal matching and one-based line numbers; dispose manual enumerators.</remarks>
    public static IEnumerable<(int Number, string Text)> FindLines(string path, string text)
    {
        int number = 0;
        foreach (string line in File.ReadLines(path, StrictUtf8))
        {
            number++;
            if (line.Contains(text, StringComparison.Ordinal)) yield return (number, line);
        }
    }
}
