using System.IO;
using System.Windows.Media.Imaging;

namespace XdkExplorer.Services;

/// <summary>Names and saves console screenshots. DmScreenShot always writes a .bmp, other formats are converted here.</summary>
public static class ScreenshotFile
{
    public const string DialogFilter = "Bitmap (*.bmp)|*.bmp|PNG image (*.png)|*.png";

    public static string DefaultFolder => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    /// <summary>First free name like the original Neighborhood uses, e.g. Helmo-image1.bmp.</summary>
    public static string NextFreeName(string folder, string consoleName)
    {
        var baseName = string.Concat(consoleName.Split(Path.GetInvalidFileNameChars()));

        for (int number = 1; ; number++)
        {
            var name = $"{baseName}-image{number}";

            if (!File.Exists(Path.Combine(folder, name + ".bmp")) && !File.Exists(Path.Combine(folder, name + ".png")))
            {
                return name + ".bmp";
            }
        }
    }

    /// <summary>Copies the captured .bmp to the target, converting it if the target is a .png.</summary>
    public static void SaveAs(string bmpPath, string targetPath)
    {
        if (!string.Equals(Path.GetExtension(targetPath), ".png", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(bmpPath, targetPath, true);

            return;
        }

        using var input = File.OpenRead(bmpPath);
        var decoder = new BmpBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var encoder = new PngBitmapEncoder();

        encoder.Frames.Add(decoder.Frames[0]);

        using var output = File.Create(targetPath);
        encoder.Save(output);
    }
}
