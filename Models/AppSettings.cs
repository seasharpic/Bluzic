namespace Bluzic.Models;

public class AppSettings
{
    public bool AutoConnect { get; set; } = true;
    public string? LastConnectedDeviceId { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool DarkTheme { get; set; } = true;
    public double Volume { get; set; } = 100.0;
}
