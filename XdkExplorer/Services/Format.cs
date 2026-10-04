namespace XdkExplorer.Services;

public static class Format
{
    private static readonly string[] Units = ["bytes", "KB", "MB", "GB", "TB"];

    public static string Size(double bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes:0} {Units[0]}";
        }

        int unit = 0;

        while (bytes >= 1024 && unit < Units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        return bytes >= 100 ? $"{bytes:0} {Units[unit]}" : $"{bytes:0.##} {Units[unit]}";
    }

    public static string Rate(double bytesPerSecond) => $"{Size(bytesPerSecond)}/s";

    public static string Duration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours} h {duration.Minutes} min";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{duration.Minutes} min {duration.Seconds:00} s";
        }

        return $"{Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds))} s";
    }
}
