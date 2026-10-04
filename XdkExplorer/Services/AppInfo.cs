using System.Runtime.InteropServices;

namespace XdkExplorer.Services;

public static class AppInfo
{
    /// <summary>Version from the project file, e.g. 0.1.0.</summary>
    public static string Version => typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    /// <summary>e.g. ".NET 10.0.12, x86", for support questions. Plain ASCII separators, it also goes to the console.</summary>
    public static string Runtime => $"{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
}
