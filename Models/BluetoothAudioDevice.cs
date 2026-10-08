using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace Bluzic.Models;

public class BluetoothAudioDevice : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private bool _isConnected;
    private bool _isStreaming;
    private string _statusText = "Готов к подключению";

    public string Id { get; set; } = string.Empty;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetField(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(ActionText));
            }
        }
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            if (SetField(ref _isStreaming, value))
            {
                OnPropertyChanged(nameof(StatusColor));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public string DeviceTypeIcon => Name.ToLowerInvariant() switch
    {
        var n when n.Contains("iphone") || n.Contains("phone") || n.Contains("galaxy") || n.Contains("pixel") || n.Contains("xiaomi") || n.Contains("redmi") || n.Contains("huawei") || n.Contains("honor") => "📱",
        var n when n.Contains("ipad") || n.Contains("tab") || n.Contains("pad") => "📟",
        var n when n.Contains("tv") || n.Contains("smart") => "📺",
        _ => "📻"
    };

    public string StatusColor => IsConnected ? (IsStreaming ? "#10B981" : "#3B82F6") : "#64748B";

    public string ActionText => IsConnected ? "Отключить" : "Подключить";

    public DeviceInformation? DeviceInfo { get; set; }
    public AudioPlaybackConnection? Connection { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
