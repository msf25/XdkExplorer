namespace XdkExplorer.Xbdm;

public sealed class XbdmException : Exception
{
    public XbdmException(int hr, string operation)
        : base($"{operation} failed: {XboxDbgNative.TranslateError(hr)} (0x{hr:X8})")
    {
        HResult = hr;
    }

    public bool IsConnectionError => HResult == XboxDbgNative.XBDM_CANNOTCONNECT || HResult == XboxDbgNative.XBDM_CONNECTIONLOST;
}
