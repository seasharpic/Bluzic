using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace Bluzic.Services;

public class AudioVolumeService
{
    private float _currentVolume = 100f;
    private bool _isMuted = false;
    private string? _activeDeviceName;

    public AudioVolumeService()
    {
    }

    public void SetActiveBluetoothDeviceName(string? name)
    {
        _activeDeviceName = name;
        ApplyVolumeAndMute();
    }

    public float GetAppVolume() => _currentVolume;

    public void SetAppVolume(float volumePercentage)
    {
        _currentVolume = Math.Clamp(volumePercentage, 0f, 100f);
        ApplyVolumeAndMute();
    }

    public bool GetMute() => _isMuted;

    public void SetMute(bool isMuted)
    {
        _isMuted = isMuted;
        ApplyVolumeAndMute();
    }

    public void ApplyVolumeAndMute()
    {
        Task.Run(() =>
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var currentPid = Process.GetCurrentProcess().Id;
                var scalar = Math.Clamp(_currentVolume / 100f, 0f, 1f);

                // Enumerate ALL active audio endpoints (Render & Capture)
                var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active);

                foreach (var endpoint in endpoints)
                {
                    try
                    {
                        var friendlyName = endpoint.FriendlyName ?? string.Empty;
                        var id = endpoint.ID ?? string.Empty;

                        bool isA2dpEndpoint = friendlyName.Contains("A2DP", StringComparison.OrdinalIgnoreCase) ||
                                              friendlyName.Contains("SNK", StringComparison.OrdinalIgnoreCase) ||
                                              id.Contains("A2DP", StringComparison.OrdinalIgnoreCase) ||
                                              id.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase) ||
                                              (!string.IsNullOrEmpty(_activeDeviceName) && friendlyName.Contains(_activeDeviceName, StringComparison.OrdinalIgnoreCase));

                        // 1. If it's the A2DP SNK endpoint (like "Микрофон (POCO X7 Pro A2DP SNK)"), adjust its volume & mute
                        if (isA2dpEndpoint && endpoint.AudioEndpointVolume != null)
                        {
                            endpoint.AudioEndpointVolume.MasterVolumeLevelScalar = scalar;
                            endpoint.AudioEndpointVolume.Mute = _isMuted;
                        }

                        // 2. Adjust sessions in this endpoint
                        var sessionManager = endpoint.AudioSessionManager;
                        if (sessionManager != null)
                        {
                            var sessions = sessionManager.Sessions;
                            if (sessions != null)
                            {
                                for (int i = 0; i < sessions.Count; i++)
                                {
                                    try
                                    {
                                        var session = sessions[i];
                                        if (session == null) continue;

                                        int pid = (int)session.GetProcessID;
                                        string disp = session.DisplayName ?? string.Empty;
                                        string sessId = session.GetSessionIdentifier ?? string.Empty;

                                        bool sessionMatches = (pid == currentPid) ||
                                                              disp.Contains("A2DP", StringComparison.OrdinalIgnoreCase) ||
                                                              disp.Contains("SNK", StringComparison.OrdinalIgnoreCase) ||
                                                              disp.Contains("Bluzic", StringComparison.OrdinalIgnoreCase) ||
                                                              sessId.Contains("A2DP", StringComparison.OrdinalIgnoreCase) ||
                                                              sessId.Contains("SNK", StringComparison.OrdinalIgnoreCase) ||
                                                              (!string.IsNullOrEmpty(_activeDeviceName) && (disp.Contains(_activeDeviceName, StringComparison.OrdinalIgnoreCase) || sessId.Contains(_activeDeviceName, StringComparison.OrdinalIgnoreCase)));

                                        if (sessionMatches && session.SimpleAudioVolume != null)
                                        {
                                            session.SimpleAudioVolume.Volume = scalar;
                                            session.SimpleAudioVolume.Mute = _isMuted;
                                        }
                                    }
                                    catch
                                    {
                                        // Ignore individual session errors
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore individual endpoint errors
                    }
                    finally
                    {
                        endpoint.Dispose();
                    }
                }
            }
            catch
            {
                // Ignore background volume errors
            }
        });
    }
}
