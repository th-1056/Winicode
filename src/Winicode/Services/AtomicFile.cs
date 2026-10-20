namespace Winicode;

/// <summary>Writes on the destination volume. Failure leaves the old destination intact.</summary>
internal static class AtomicFile
{
    internal static byte[] Read(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximumBytes) throw new InvalidDataException("文件超过允许的大小。");
        using var contents = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = stream.Read(buffer, 0, Math.Min(buffer.Length, maximumBytes + 1 - (int)contents.Length))) > 0)
        {
            contents.Write(buffer, 0, read);
            if (contents.Length > maximumBytes) throw new InvalidDataException("文件超过允许的大小。");
        }
        return contents.ToArray();
    }

    internal static void Write(string path, byte[] contents, bool keepBackup = true)
    {
        path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 8192, FileOptions.WriteThrough))
            {
                stream.Write(contents);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, keepBackup ? path + ".bak" : null);
            else File.Move(temporary, path); // Never overwrite a destination created concurrently.
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static void PreserveDamaged(string path)
    {
        // Keep the unreadable original as well as a known good .bak during recovery saves.
        File.Copy(path, path + $".damaged.{DateTime.UtcNow:yyyyMMddHHmmss}.{Guid.NewGuid():N}", overwrite: false);
    }

    internal static bool IsFileFailure(Exception exception) => exception is IOException
        or UnauthorizedAccessException or System.Security.SecurityException;
}
