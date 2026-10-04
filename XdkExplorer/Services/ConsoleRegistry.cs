using Microsoft.Win32;

namespace XdkExplorer.Services;

/// <summary>
/// Console list shared with the XDK shell extension (Xbox Neighborhood) and the default console of the XDK tools.
/// This is the only place the list is stored.
///
/// xbshlext\Consoles holds one REG_DWORD per console, named after what the user entered (name or IP).
/// The meaning of the value is unknown, the original writes something that looks like a tick count.
/// The unnamed default value is the number of consoles.
/// XboxName is the default console used by Visual Studio .NET 2003 and the XDK tools.
/// </summary>
public static class ConsoleRegistry
{
    private const string SdkKeyPath = @"Software\Microsoft\XboxSDK";
    private const string ConsolesKeyPath = @"Software\Microsoft\XboxSDK\xbshlext\Consoles";
    private const string DefaultConsoleValue = "XboxName";

    public static List<string> ReadConsoles()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ConsolesKeyPath);

        if (key == null)
        {
            return [];
        }

        return key.GetValueNames()
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();
    }

    public static void Add(string target)
    {
        using var key = Registry.CurrentUser.CreateSubKey(ConsolesKeyPath);

        if (FindValueName(key, target) == null)
        {
            key.SetValue(target, Environment.TickCount, RegistryValueKind.DWord);
        }

        WriteCount(key);
    }

    public static void Remove(string target)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ConsolesKeyPath, writable: true);

        if (key != null && FindValueName(key, target) is { } valueName)
        {
            key.DeleteValue(valueName);
            WriteCount(key);
        }
    }

    /// <summary>Default console of the XDK tools, or null if none is set. Readable before xboxdbg.dll is loaded.</summary>
    public static string? ReadDefault()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SdkKeyPath);

        return key?.GetValue(DefaultConsoleValue) is string name && name.Length > 0 ? name : null;
    }

    public static void SetDefault(string target)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SdkKeyPath);

        key.SetValue(DefaultConsoleValue, target, RegistryValueKind.String);
    }

    public static void ClearDefault()
    {
        using var key = Registry.CurrentUser.CreateSubKey(SdkKeyPath);

        key.SetValue(DefaultConsoleValue, "", RegistryValueKind.String);
    }

    public static bool IsDefault(string target)
    {
        return string.Equals(ReadDefault(), target, StringComparison.OrdinalIgnoreCase);
    }

    // Written from the actual entries rather than counted up and down, so a wrong value heals itself
    private static void WriteCount(RegistryKey key)
    {
        int count = key.GetValueNames().Count(name => !string.IsNullOrEmpty(name));

        key.SetValue(string.Empty, count, RegistryValueKind.DWord);
    }

    // Registry value names are case-insensitive, but the stored spelling is needed to delete it
    private static string? FindValueName(RegistryKey key, string target)
    {
        return key.GetValueNames().FirstOrDefault(name => string.Equals(name, target, StringComparison.OrdinalIgnoreCase));
    }
}
