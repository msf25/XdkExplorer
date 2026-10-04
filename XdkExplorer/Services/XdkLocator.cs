using System.Diagnostics;
using System.IO;
using XdkExplorer.Xbdm;

namespace XdkExplorer.Services;

public sealed class XdkInstallation
{
    public XdkInstallation(string dllPath, string? version)
    {
        DllPath = dllPath;
        Version = version;
    }

    public string DllPath { get; }

    /// <summary>File version of xboxdbg.dll, e.g. 1.00.5849.1.</summary>
    public string? Version { get; }
}

/// <summary>Finds xboxdbg.dll, either from the XDK installer's registry entry or below a folder the user picked.</summary>
public static class XdkLocator
{
    public static XdkInstallation? FindInstalled()
    {
        var dll = XboxDbgNative.FindInstalledDll();

        return dll == null ? null : FromDll(dll);
    }

    /// <summary>Searches the folder and all subfolders. Prefers the standard location xbox\bin if there are several.</summary>
    public static XdkInstallation? FindInFolder(string folder)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive
        };

        string? best = null;

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, XboxDbgNative.DllName, options))
            {
                if (file.Contains(@"\xbox\bin\", StringComparison.OrdinalIgnoreCase))
                {
                    best = file;

                    break;
                }

                best ??= file;
            }
        }
        catch (IOException)
        {
            return null;
        }

        return best == null ? null : FromDll(best);
    }

    public static XdkInstallation? FromDll(string dllPath)
    {
        if (!File.Exists(dllPath))
        {
            return null;
        }

        var version = FileVersionInfo.GetVersionInfo(dllPath).FileVersion;

        return new XdkInstallation(dllPath, string.IsNullOrWhiteSpace(version) ? null : version);
    }
}
