using System.IO;
using static XdkExplorer.Xbdm.XboxDbgNative;

namespace XdkExplorer.Xbdm;

/// <summary>Helpers for talking raw XBDM commands over a connection from DmOpenConnection.</summary>
internal static unsafe class XbdmProtocol
{
    private const int ResponseBufferSize = 512;

    public static int SendCommand(IntPtr connection, string command, out string response)
    {
        var buffer = stackalloc byte[ResponseBufferSize];
        uint size = ResponseBufferSize;

        int hr = DmSendCommand(connection, command, buffer, ref size);
        response = ReadAnsi(buffer, size);

        return hr;
    }

    public static int ReceiveStatus(IntPtr connection, out string response)
    {
        var buffer = stackalloc byte[ResponseBufferSize];
        uint size = ResponseBufferSize;

        int hr = DmReceiveStatusResponse(connection, buffer, ref size);
        response = ReadAnsi(buffer, size);

        return hr;
    }

    /// <summary>DmReceiveBinary may return fewer bytes than requested, so this loops until count bytes arrived.</summary>
    public static void ReceiveExact(IntPtr connection, byte[] buffer, int count)
    {
        int got = 0;

        fixed (byte* p = buffer)
        {
            while (got < count)
            {
                ThrowIfFailed(DmReceiveBinary(connection, p + got, (uint)(count - got), out var n), "DmReceiveBinary");

                if (n == 0)
                {
                    throw new IOException("Connection closed during transfer.");
                }

                got += (int)n;
            }
        }
    }

    public static void SendAll(IntPtr connection, byte[] buffer, int count)
    {
        fixed (byte* p = buffer)
        {
            ThrowIfFailed(DmSendBinary(connection, p, (uint)count), "DmSendBinary");
        }
    }

    private static string ReadAnsi(byte* buffer, uint size)
    {
        // The DLL does not guarantee a terminator when the response fills the buffer
        buffer[Math.Min(size, ResponseBufferSize - 1)] = 0;

        return new string((sbyte*)buffer).TrimEnd('\r', '\n');
    }
}
