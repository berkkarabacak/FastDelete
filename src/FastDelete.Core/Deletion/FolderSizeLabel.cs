namespace FastDelete.Core.Deletion;

/// <summary>Plain size words for a folder row. A capped walk must not pretend to be an exact size.</summary>
public static class FolderSizeLabel
{
    public const long ItemCap = 200_000;

    public static string FromStats(long bytes, long itemsSeen)
    {
        if (itemsSeen >= ItemCap)
            return "Very large";
        if (bytes <= 0)
            return "Empty";
        return ByteText(bytes);
    }

    public static string ByteText(long bytes) => bytes switch
    {
        >= 1L << 40 => $"{bytes / (double)(1L << 40):F2} TB",
        >= 1L << 30 => $"{bytes / (double)(1L << 30):F2} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):F2} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):F1} KB",
        _ => $"{bytes} B",
    };
}
