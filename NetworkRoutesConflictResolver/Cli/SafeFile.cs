namespace NetworkRoutesConflictResolver.Cli;

/// <summary>
/// File helpers that never silently destroy data: writes optionally create a timestamp-free
/// <c>.bak</c> copy first, and callers must opt in to overwriting an existing target.
/// </summary>
public static class SafeFile
{
    public static string ReadRequired(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Input file not found: {path}", path);
        }

        return File.ReadAllText(path);
    }

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="path"/>. If the file exists, a backup
    /// is required (creates <c>&lt;path&gt;.bak</c>); otherwise the write is refused to protect data.
    /// </summary>
    public static void Write(string path, string content, bool backup)
    {
        if (File.Exists(path))
        {
            if (!backup)
            {
                throw new InvalidOperationException(
                    $"Refusing to overwrite existing file '{path}' without --backup.");
            }

            File.Copy(path, path + ".bak", overwrite: true);
        }

        File.WriteAllText(path, content);
    }
}
