using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Windows.Media.Audio;
using Bluzic.Models;
using Bluzic.Services;

namespace Bluzic.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly BluetoothSinkService _bluetoothService;
    private readonly AudioVolumeService _volumeService;
    private readonly SettingsService _settingsService;
    private readonly Dispatcher _dispatcher;

    private BluetoothAudioDevice? _selectedDevice;
    private BluetoothAudioDevice? _activeDevice;
    private bool _isActiveDeviceConnected;
    private string _statusMessage = "Готов к работе";
    private double _volume = 100.0;
    private bool _isMuted;
    private bool _isScanning;

    public ObservableCollection<BluetoothAudioDevice> Devices { get; } = new();

    public BluetoothAudioDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetField(ref _selectedDevice, value))
            {
                OnPropertyChanged(nameof(HasSelectedDevice));
            }
        }
    }

    public BluetoothAudioDevice? ActiveDevice
    {
        get => _activeDevice;
        set
        {
            if (SetField(ref _activeDevice, value))
            {
                OnPropertyChanged(nameof(HasActiveDevice));
            }
        }
    }

    public bool IsActiveDeviceConnected
    {
        get => _isActiveDeviceConnected;
        set
        {
            if (SetField(ref _isActiveDeviceConnected, value))
            {
                OnPropertyChanged(nameof(ActiveStatusText));
                OnPropertyChanged(nameof(ActiveStatusColor));
            }
        }
    }

    public string ActiveStatusText => IsActiveDeviceConnected 
        ? $"Подключено: {ActiveDevice?.Name ?? "Устройство"}" 
        : "Нет активного подключения";

    public string ActiveStatusColor => IsActiveDeviceConnected ? "#10B981" : "#94A3B8";

    public bool HasDevices => Devices.Count > 0;
    public bool HasSelectedDevice => SelectedDevice != null;
    public bool HasActiveDevice => ActiveDevice != null;

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        set => SetField(ref _isScanning, value);
    }

    public double Volume
    {
        get => _volume;
        set
        {
            if (SetField(ref _volume, value))
            {
                _volumeService.SetAppVolume((float)value);
                _settingsService.Settings.Volume = value;
                _settingsService.SaveSettings();
                OnPropertyChanged(nameof(VolumeIcon));
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetField(ref _isMuted, value))
            {
                _volumeService.SetMute(value);
                OnPropertyChanged(nameof(VolumeIcon));
            }
        }
    }

    public string VolumeIcon => IsMuted || Volume <= 0 ? "🔇" : (Volume < 50 ? "🔉" : "🔊");

    public bool AutoConnect
    {
        get => _settingsService.Settings.AutoConnect;
        set
        {
            _settingsService.Settings.AutoConnect = value;
            _settingsService.SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool MinimizeToTray
    {
        get => _settingsService.Settings.MinimizeToTray;
        set
        {
            _settingsService.Settings.MinimizeToTray = value;
            _settingsService.SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool StartWithWindows
    {
        get => _settingsService.Settings.StartWithWindows;
        set
        {
            _settingsService.Settings.StartWithWindows = value;
            _settingsService.SaveSettings();
            OnPropertyChanged();
        }
    }

    #region Commands

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ToggleConnectionCommand { get; }
    public ICommand RefreshDevicesCommand { get; }
    public ICommand OpenBluetoothSettingsCommand { get; }
    public ICommand ToggleMuteCommand { get; }

    #endregion

    public MainViewModel(
        BluetoothSinkService bluetoothService,
        AudioVolumeService volumeService,
        SettingsService settingsService)
    {
        _bluetoothService = bluetoothService;
        _volumeService = volumeService;
        _settingsService = settingsService;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        _volume = _settingsService.Settings.Volume > 0 ? _settingsService.Settings.Volume : _volumeService.GetAppVolume();
        _volumeService.SetAppVolume((float)_volume);
        _isMuted = _volumeService.GetMute();

        ConnectCommand = new AsyncRelayCommand(async param =>
        {
            if (param is BluetoothAudioDevice dev)
            {
                await ConnectDeviceAsync(dev);
            }
            else if (SelectedDevice != null)
            {
                await ConnectDeviceAsync(SelectedDevice);
            }
        });

        DisconnectCommand = new RelayCommand(() =>
        {
            _bluetoothService.DisconnectActive();
            ActiveDevice = null;
            IsActiveDeviceConnected = false;
            StatusMessage = "Отключено";
        });

        ToggleConnectionCommand = new AsyncRelayCommand(async param =>
        {
            var dev = param as BluetoothAudioDevice ?? SelectedDevice;
            if (dev == null) return;

            if (dev.IsConnected)
            {
                _bluetoothService.DisconnectActive();
                ActiveDevice = null;
                IsActiveDeviceConnected = false;
                StatusMessage = $"Отключено от {dev.Name}";
            }
            else
            {
                await ConnectDeviceAsync(dev);
            }
        });

        RefreshDevicesCommand = new AsyncRelayCommand(async () =>
        {
            IsScanning = true;
            StatusMessage = "Поиск устройств Bluetooth...";
            await _bluetoothService.RefreshDevicesAsync();
            IsScanning = false;
            StatusMessage = $"Найдено устройств: {Devices.Count}";
        });

        OpenBluetoothSettingsCommand = new RelayCommand(() =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:bluetooth",
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore
            }
        });

        ToggleMuteCommand = new RelayCommand(() =>
        {
            IsMuted = !IsMuted;
        });

        _bluetoothService.DevicesUpdated += OnDevicesUpdated;
        _bluetoothService.ConnectionStateChanged += OnConnectionStateChanged;
        _bluetoothService.LogMessage += msg =>
        {
            _dispatcher.Invoke(() => StatusMessage = msg);
        };

        _bluetoothService.StartMonitoring();
    }

    private async Task ConnectDeviceAsync(BluetoothAudioDevice device)
    {
        StatusMessage = $"Подключение к {device.Name}...";
        var success = await _bluetoothService.ConnectAsync(device);
        if (success)
        {
            ActiveDevice = device;
            IsActiveDeviceConnected = true;
            StatusMessage = $"Подключено: {device.Name} (Звук передаётся на ПК)";
        }
        else
        {
            StatusMessage = $"Не удалось подключиться к {device.Name}";
        }
    }

    private void OnDevicesUpdated(IReadOnlyList<BluetoothAudioDevice> devices)
    {
        _dispatcher.Invoke(() =>
        {
            Devices.Clear();
            foreach (var d in devices)
            {
                Devices.Add(d);
            }

            if (SelectedDevice == null && Devices.Count > 0)
            {
                SelectedDevice = Devices[0];
            }

            ActiveDevice = _bluetoothService.ActiveDevice;
            IsActiveDeviceConnected = _bluetoothService.IsConnected;

            OnPropertyChanged(nameof(HasDevices));
        });
    }

    private void OnConnectionStateChanged(BluetoothAudioDevice device, AudioPlaybackConnectionState state)
    {
        _dispatcher.Invoke(() =>
        {
            ActiveDevice = state == AudioPlaybackConnectionState.Opened ? device : null;
            IsActiveDeviceConnected = state == AudioPlaybackConnectionState.Opened;
            if (state == AudioPlaybackConnectionState.Opened)
            {
                _volumeService.SetActiveBluetoothDeviceName(device?.Name);
            }
            else
            {
                _volumeService.SetActiveBluetoothDeviceName(null);
            }
        });
    }

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
