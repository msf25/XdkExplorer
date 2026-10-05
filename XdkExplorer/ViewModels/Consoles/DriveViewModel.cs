using CommunityToolkit.Mvvm.ComponentModel;
using XdkExplorer.Services;
using XdkExplorer.Xbdm;

namespace XdkExplorer.ViewModels;

public sealed partial class DriveViewModel : ObservableObject
{
    private XboxDrive _drive;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public DriveViewModel(ConsoleViewModel console, XboxDrive drive)
    {
        Console = console;
        _drive = drive;
    }

    public ConsoleViewModel Console { get; }

    public char Letter => _drive.Letter;

    public string Name => _drive.Name;

    public ulong Free => _drive.Free;

    public ulong Total => _drive.Total;

    public string Label => $"{Letter}:";

    public string RootPath => $@"{Letter}:\";

    public string FreeText => Total == 0 ? "" : $"{Format.Size(Free)} free";

    public string SpaceText => Total == 0 ? "" : $"{Format.Size(Free)} free of {Format.Size(Total)}";

    public string ToolTipText => Total == 0 ? $"{Name} ({Label})" : $"{Name} ({Label})\n{Format.Size(Free)} free of {Format.Size(Total)}";

    public double UsedFraction => Total == 0 ? 0 : 1.0 - (double)Free / Total;

    public bool IsAlmostFull => UsedFraction > 0.9;

    /// <summary>Takes over new name and space of the same letter, e.g. after a title mounted another utility partition.</summary>
    public void Update(XboxDrive drive)
    {
        _drive = drive;

        // Everything except IsSelected derives from the drive
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Sort key that puts E: first, since that is where dev builds live.</summary>
    public int SortOrder => Letter == 'E' ? 0 : Letter;
}
