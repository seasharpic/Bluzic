using System.Collections.Concurrent;
using System.Diagnostics;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;
using Bluzic.Models;

namespace Bluzic.Services;

public class BluetoothSinkService : IDisposable
{
    private DeviceWatcher? _deviceWatcher;
    private readonly ConcurrentDictionary<string, BluetoothAudioDevice> _devices = new();
    private AudioPlaybackConnection? _activeConnection;
    private BluetoothAudioDevice? _activeDevice;
    private readonly SettingsService _settingsService;

    public event Action<IReadOnlyList<BluetoothAudioDevice>>? DevicesUpdated;
    public event Action<BluetoothAudioDevice, AudioPlaybackConnectionState>? ConnectionStateChanged;
    public event Action<string>? LogMessage;

    public BluetoothAudioDevice? ActiveDevice => _activeDevice;
    public bool IsConnected => _activeConnection?.State == AudioPlaybackConnectionState.Opened;

    public BluetoothSinkService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void StartMonitoring()
    {
        try
        {
            var selector = AudioPlaybackConnection.GetDeviceSelector();
            _deviceWatcher = DeviceInformation.CreateWatcher(selector);

            _deviceWatcher.Added += OnDeviceAdded;
            _deviceWatcher.Removed += OnDeviceRemoved;
            _deviceWatcher.Updated += OnDeviceUpdated;
            _deviceWatcher.EnumerationCompleted += OnEnumerationCompleted;

            _deviceWatcher.Start();
            LogMessage?.Invoke("Служба поиска Bluetooth A2DP устройств запущена.");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка запуска мониторинга: {ex.Message}");
        }
    }

    private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation deviceInfo)
    {
        var device = new BluetoothAudioDevice
        {
            Id = deviceInfo.Id,
            Name = string.IsNullOrWhiteSpace(deviceInfo.Name) ? "Неизвестное устройство" : deviceInfo.Name,
            DeviceInfo = deviceInfo,
            StatusText = "Готов к подключению"
        };

        _devices[deviceInfo.Id] = device;
        LogMessage?.Invoke($"Обнаружено устройство: {device.Name}");
        NotifyDevicesChanged();

        // Check for Auto-Connect
        if (_settingsService.Settings.AutoConnect)
        {
            if (_activeDevice == null && 
                (_settingsService.Settings.LastConnectedDeviceId == null || _settingsService.Settings.LastConnectedDeviceId == deviceInfo.Id))
            {
                LogMessage?.Invoke($"Автоподключение к {device.Name}...");
                _ = ConnectAsync(device);
            }
        }
    }

    private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate deviceInfoUpdate)
    {
        if (_devices.TryRemove(deviceInfoUpdate.Id, out var device))
        {
            if (_activeDevice?.Id == device.Id)
            {
                DisconnectActive();
            }
            LogMessage?.Invoke($"Устройство отключено: {device.Name}");
            NotifyDevicesChanged();
        }
    }

    private void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate deviceInfoUpdate)
    {
        if (_devices.TryGetValue(deviceInfoUpdate.Id, out var device))
        {
            device.DeviceInfo?.Update(deviceInfoUpdate);
            NotifyDevicesChanged();
        }
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args)
    {
        LogMessage?.Invoke($"Сканирование завершено. Найдено устройств: {_devices.Count}");
        NotifyDevicesChanged();
    }

    public async Task<bool> ConnectAsync(BluetoothAudioDevice device)
    {
        if (_activeDevice != null && _activeDevice.Id != device.Id)
        {
            DisconnectActive();
        }

        try
        {
            device.StatusText = "Подключение...";
            LogMessage?.Invoke($"Подключение к {device.Name}...");

            var connection = AudioPlaybackConnection.TryCreateFromId(device.Id);
            if (connection == null)
            {
                device.StatusText = "Не удалось инициализировать AudioPlaybackConnection";
                device.IsConnected = false;
                LogMessage?.Invoke($"Ошибка: AudioPlaybackConnection вернул null для {device.Name}");
                return false;
            }

            connection.Start();

            connection.StateChanged += (s, e) =>
            {
                var state = s.State;
                device.IsConnected = state == AudioPlaybackConnectionState.Opened;
                device.IsStreaming = state == AudioPlaybackConnectionState.Opened;
                device.StatusText = state switch
                {
                    AudioPlaybackConnectionState.Opened => "Активно (Звук передаётся)",
                    AudioPlaybackConnectionState.Closed => "Соединение закрыто",
                    _ => "Неизвестное состояние"
                };

                LogMessage?.Invoke($"Статус {device.Name}: {device.StatusText}");
                ConnectionStateChanged?.Invoke(device, state);
            };

            var openResult = await connection.OpenAsync();
            if (openResult.Status == AudioPlaybackConnectionOpenResultStatus.Success)
            {
                _activeConnection = connection;
                _activeDevice = device;
                device.Connection = connection;
                device.IsConnected = true;
                device.IsStreaming = true;
                device.StatusText = "Активно (Звук передаётся)";

                _settingsService.Settings.LastConnectedDeviceId = device.Id;
                _settingsService.SaveSettings();

                LogMessage?.Invoke($"Успешно подключено к {device.Name}!");
                ConnectionStateChanged?.Invoke(device, AudioPlaybackConnectionState.Opened);
                NotifyDevicesChanged();
                return true;
            }
            else
            {
                device.StatusText = $"Ошибка подключения ({openResult.Status})";
                device.IsConnected = false;
                connection.Dispose();
                LogMessage?.Invoke($"Не удалось открыть поток для {device.Name}: {openResult.Status}");
                NotifyDevicesChanged();
                return false;
            }
        }
        catch (Exception ex)
        {
            device.StatusText = $"Ошибка: {ex.Message}";
            device.IsConnected = false;
            LogMessage?.Invoke($"Исключение при подключении к {device.Name}: {ex.Message}");
            NotifyDevicesChanged();
            return false;
        }
    }

    public void DisconnectActive()
    {
        if (_activeDevice != null)
        {
            LogMessage?.Invoke($"Отключение от {_activeDevice.Name}...");
            _activeDevice.IsConnected = false;
            _activeDevice.IsStreaming = false;
            _activeDevice.StatusText = "Отключено";
            _activeDevice = null;
        }

        if (_activeConnection != null)
        {
            try
            {
                _activeConnection.Dispose();
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Ошибка при закрытии соединения: {ex.Message}");
            }
            _activeConnection = null;
        }

        NotifyDevicesChanged();
    }

    public async Task RefreshDevicesAsync()
    {
        try
        {
            var selector = AudioPlaybackConnection.GetDeviceSelector();
            var devices = await DeviceInformation.FindAllAsync(selector);
            _devices.Clear();

            foreach (var info in devices)
            {
                var dev = new BluetoothAudioDevice
                {
                    Id = info.Id,
                    Name = string.IsNullOrWhiteSpace(info.Name) ? "Bluetooth Устройство" : info.Name,
                    DeviceInfo = info,
                    StatusText = (_activeDevice?.Id == info.Id && IsConnected) ? "Активно (Звук передаётся)" : "Готов к подключению",
                    IsConnected = (_activeDevice?.Id == info.Id && IsConnected)
                };
                _devices[info.Id] = dev;
            }

            NotifyDevicesChanged();
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка обновления списка: {ex.Message}");
        }
    }

    private void NotifyDevicesChanged()
    {
        var list = _devices.Values.ToList();
        DevicesUpdated?.Invoke(list);
    }

    public void Dispose()
    {
        DisconnectActive();
        if (_deviceWatcher != null)
        {
            _deviceWatcher.Stop();
            _deviceWatcher = null;
        }
    }
}
