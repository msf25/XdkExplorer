using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XdkExplorer.Services;

public sealed class SavedConsole
{
    /// <summary>Name or IP passed to XBDM.</summary>
    public string Target { get; set; } = "";

    /// <summary>Name the console reported for itself, if known.</summary>
    public string? Name { get; set; }

    /// <summary>Last known IP, used for the reachability check.</summary>
    public string? Address { get; set; }
}

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Details learned from consoles (reported name, last IP), keyed by Target. Only a cache: the list of consoles itself
    /// lives in the registry and is shared with the Xbox Neighborhood, see ConsoleRegistry.
    /// </summary>
    public List<SavedConsole> ConsoleCache { get; set; } = new();

    /// <summary>Settings files of older versions call the cache "Consoles". Only read, never written.</summary>
    [JsonPropertyName("Consoles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SavedConsole>? LegacyConsoles
    {
        get => null;
        set
        {
            if (value != null && ConsoleCache.Count == 0)
            {
                ConsoleCache = value;
            }
        }
    }

    /// <summary>Bytes per DmSendBinary/DmReceiveBinary call. Smaller blocks make cancelling react faster on slow links.</summary>
    public int ChunkSize { get; set; } = 256 * 1024;

    public string? XdkDllPath { get; set; }

    /// <summary>False for design-time data, which must never overwrite the real settings file.</summary>
    [JsonIgnore]
    public bool IsPersistent { get; init; } = true;

    public static string FilePath
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XdkExplorer", "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // A broken settings file must not prevent startup
        }

        return new AppSettings();
    }

    public void Save()
    {
        if (!IsPersistent)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>Cached details for a console, created on first use.</summary>
    public SavedConsole GetConsoleInfo(string target)
    {
        var info = ConsoleCache.FirstOrDefault(c => string.Equals(c.Target, target, StringComparison.OrdinalIgnoreCase));

        if (info == null)
        {
            info = new SavedConsole { Target = target };
            ConsoleCache.Add(info);
        }

        return info;
    }

    /// <summary>Drops cached details of consoles that are no longer in the given list. Returns true if anything was removed.</summary>
    public bool KeepConsoleInfo(IReadOnlyCollection<string> targets)
    {
        var known = new HashSet<string>(targets, StringComparer.OrdinalIgnoreCase);

        return ConsoleCache.RemoveAll(c => !known.Contains(c.Target)) > 0;
    }
}