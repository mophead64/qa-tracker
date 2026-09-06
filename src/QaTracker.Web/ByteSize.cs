namespace QaTracker.Web;

/// <summary>
/// Human-readable byte sizes (binary units — 1 KB = 1024 B). Shared by the attachment
/// panel/upload modal and the system-settings storage-usage stat.
/// </summary>
public static class ByteSize
{
    public static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes < 0 ? 0 : bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }
}
