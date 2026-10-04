using System.IO;
using static XdkExplorer.Xbdm.XboxDbgNative;

namespace XdkExplorer.Xbdm;

/// <summary>Download via the raw getfile command with the payload read in chunks.</summary>
public sealed class FileDownload : IChunkedTransfer
{
    public const int DefaultChunkSize = 256 * 1024;

    private readonly int _chunkSize;
    private FileStream? _file;
    private byte[] _buffer = [];
    private IntPtr _connection;
    private bool _localFileCreated;
    private long _totalBytes;
    private long _transferredBytes;

    public FileDownload(string remotePath, string localPath, int chunkSize = DefaultChunkSize)
    {
        RemotePath = remotePath;
        LocalPath = localPath;
        _chunkSize = chunkSize;
    }

    public string RemotePath { get; }

    public string LocalPath { get; }

    public long TotalBytes => _totalBytes;

    public long TransferredBytes => _transferredBytes;

    public void Begin()
    {
        ThrowIfFailed(DmOpenConnection(out _connection), "DmOpenConnection");

        var hr = XbdmProtocol.SendCommand(_connection, $"getfile name=\"{RemotePath}\"", out var response);

        if (hr != XBDM_BINRESPONSE)
        {
            throw new XbdmException(hr, $"getfile ({response})");
        }

        // The binary response starts with the payload length as a little-endian DWORD
        var lengthBytes = new byte[4];

        XbdmProtocol.ReceiveExact(_connection, lengthBytes, 4);

        _totalBytes = BitConverter.ToUInt32(lengthBytes, 0);
        _file = new FileStream(LocalPath, FileMode.Create, FileAccess.Write, FileShare.None, _chunkSize);
        _localFileCreated = true;
        _buffer = new byte[_chunkSize];
    }

    public bool Step()
    {
        if (_transferredBytes < _totalBytes)
        {
            int want = (int)Math.Min(_chunkSize, _totalBytes - _transferredBytes);

            XbdmProtocol.ReceiveExact(_connection, _buffer, want);

            _file!.Write(_buffer, 0, want);
            _transferredBytes += want;
        }

        if (_transferredBytes < _totalBytes)
        {
            return false;
        }

        Close();

        return true;
    }

    public void Abort()
    {
        Close();
    }

    public bool TryRemovePartialTarget()
    {
        if (_localFileCreated)
        {
            File.Delete(LocalPath);
        }

        return true;
    }

    private void Close()
    {
        if (_connection != IntPtr.Zero)
        {
            DmCloseConnection(_connection);
            _connection = IntPtr.Zero;
        }

        _file?.Dispose();
        _file = null;
    }
}
