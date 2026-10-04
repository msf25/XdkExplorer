using System.IO;
using static XdkExplorer.Xbdm.XboxDbgNative;

namespace XdkExplorer.Xbdm;

/// <summary>
/// Upload via the raw sendfile command with the payload streamed in chunks.
/// sendfile exists in every XBDM version, unlike writefile (4531+).
/// </summary>
public sealed class FileUpload : IChunkedTransfer
{
    public const int DefaultChunkSize = 256 * 1024;

    private readonly int _chunkSize;
    private FileStream? _file;
    private byte[] _buffer = [];
    private IntPtr _connection;
    private bool _remoteFileCreated;
    private long _totalBytes;
    private long _transferredBytes;

    public FileUpload(string localPath, string remotePath, int chunkSize = DefaultChunkSize)
    {
        LocalPath = localPath;
        RemotePath = remotePath;
        _chunkSize = chunkSize;
    }

    public string LocalPath { get; }

    public string RemotePath { get; }

    public long TotalBytes => _totalBytes;

    public long TransferredBytes => _transferredBytes;

    public void Begin()
    {
        _file = new FileStream(LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read, _chunkSize);
        _totalBytes = _file.Length;

        if (_totalBytes > uint.MaxValue)
        {
            throw new IOException("Files larger than 4 GB are not supported by FATX.");
        }

        ThrowIfFailed(DmOpenConnection(out _connection), "DmOpenConnection");

        var hr = XbdmProtocol.SendCommand(_connection, $"sendfile name=\"{RemotePath}\" length=0x{_totalBytes:x}", out var response);

        if (hr != XBDM_READYFORBIN)
        {
            throw new XbdmException(hr, $"sendfile ({response})");
        }

        _remoteFileCreated = true;
        _buffer = new byte[_chunkSize];
    }

    public bool Step()
    {
        if (_transferredBytes < _totalBytes)
        {
            int read = _file!.Read(_buffer, 0, (int)Math.Min(_chunkSize, _totalBytes - _transferredBytes));

            if (read <= 0)
            {
                throw new IOException("Local file ended early.");
            }

            XbdmProtocol.SendAll(_connection, _buffer, read);

            _transferredBytes += read;
        }

        if (_transferredBytes < _totalBytes)
        {
            return false;
        }

        var hr = XbdmProtocol.ReceiveStatus(_connection, out var response);

        ThrowIfFailed(hr, $"sendfile completion ({response})");

        Close();

        return true;
    }

    public void Abort()
    {
        Close();
    }

    /// <summary>
    /// XBDM keeps an aborted upload locked for a moment (1-2 s on xemu) and then usually removes the partial file
    /// by itself. Until then deleting fails with "cannot access".
    /// </summary>
    public bool TryRemovePartialTarget()
    {
        if (!_remoteFileCreated)
        {
            return true;
        }

        int hr = DmDeleteFile(RemotePath, false);

        return Succeeded(hr) || hr == XBDM_NOSUCHFILE;
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
