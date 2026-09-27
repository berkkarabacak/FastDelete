namespace FastDelete.Core.Enumeration;

public enum WorkItemKind : byte
{
    File,
    Directory,
    ReparseLink,
}

/// <summary>One unit of deletion work, streamed from the walker to the engine.</summary>
public readonly struct DeleteWorkItem
{
    public string Path { get; }        // \\?\ -prefixed
    public WorkItemKind Kind { get; }
    public uint Attributes { get; }
    public long Size { get; }          // file bytes (0 for directories/links)

    public DeleteWorkItem(string path, WorkItemKind kind, uint attributes, long size = 0)
    {
        Path = path;
        Kind = kind;
        Attributes = attributes;
        Size = size;
    }
}
