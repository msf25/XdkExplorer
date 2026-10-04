namespace XdkExplorer.Xbdm;

public sealed class XboxFileEntry
{
    private const uint FILE_ATTRIBUTE_READONLY = 0x01;
    private const uint FILE_ATTRIBUTE_HIDDEN = 0x02;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    public XboxFileEntry(string name, ulong size, uint attributes, DateTime created, DateTime modified)
    {
        Name = name;
        Size = size;
        Attributes = attributes;
        Created = created;
        Modified = modified;
    }

    public string Name { get; }

    public ulong Size { get; }

    public uint Attributes { get; }

    public DateTime Created { get; }

    public DateTime Modified { get; }

    public bool IsDirectory => (Attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;

    public bool IsReadOnly => (Attributes & FILE_ATTRIBUTE_READONLY) != 0;

    public bool IsHidden => (Attributes & FILE_ATTRIBUTE_HIDDEN) != 0;

    internal static XboxFileEntry FromNative(XboxDbgNative.DM_FILE_ATTRIBUTES native)
    {
        ulong size = ((ulong)native.SizeHigh << 32) | native.SizeLow;

        return new XboxFileEntry(native.Name, size, native.Attributes,
            FromFileTime(native.CreationTime), FromFileTime(native.ChangeTime));
    }

    private static DateTime FromFileTime(long fileTime)
    {
        if (fileTime <= 0)
        {
            return DateTime.MinValue;
        }

        return DateTime.FromFileTime(fileTime);
    }
}
