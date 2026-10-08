using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using Bluzic.Models;

namespace Bluzic.Services;

public class SettingsService
{
    private const string AppName = "Bluzic";
    private readonly string _settingsFilePath;

    public AppSettings Settings { get; private set; }

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "Bluzic");
        Directory.CreateDirectory(dir);
        _settingsFilePath = Path.Combine(dir, "settings.json");
        Settings = LoadSettings();
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch
        {
            // fallback to default
        }

        return new AppSettings();
    }

    public void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
            UpdateStartupRegistration(Settings.StartWithWindows);
        }
        catch
        {
            // Ignore write errors
        }
    }

    public void UpdateStartupRegistration(bool startWithWindows)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            if (startWithWindows)
            {
                key.SetValue(AppName, $"\"{exePath}\" --minimized");
            }
            else
            {
                if (key.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName, false);
                }
            }
        }
        catch
        {
            // Ignore registry permission errors
        }
    }
}
