using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using ExtendScreenServer.Models;
using ExtendScreenServer.Services;

namespace ExtendScreenServer.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ServerConfig _config;
    private readonly DiscoveryService _discoveryService;
    private readonly StreamingService _streamingService;
    private readonly NetworkServerService _networkService;
    private readonly VirtualDisplayService _virtualDisplayService;

    public event PropertyChangedEventHandler? PropertyChanged;

    private DeviceInfo? _connectedDevice;
    public DeviceInfo? ConnectedDevice
    {
        get => _connectedDevice;
        set { _connectedDevice = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsConnected)); }
    }

    public bool IsConnected => ConnectedDevice != null;

    private string _statusText = "Not connected";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private int _selectedResolutionIndex = 0;
    public int SelectedResolutionIndex
    {
        get => _selectedResolutionIndex;
        set { _selectedResolutionIndex = value; OnPropertyChanged(); ApplyResolution(); }
    }

    private int _selectedFpsIndex = 0;
    public int SelectedFpsIndex
    {
        get => _selectedFpsIndex;
        set { _selectedFpsIndex = value; OnPropertyChanged(); ApplyFps(); }
    }

    private string _connectionType = "Wi-Fi";
    public string ConnectionType
    {
        get => _connectionType;
        set { _connectionType = value; OnPropertyChanged(); }
    }

    private bool _isUsbForwarding;
    public bool IsUsbForwarding
    {
        get => _isUsbForwarding;
        set { _isUsbForwarding = value; OnPropertyChanged(); }
    }

    private bool _isExtending;
    public bool IsExtending
    {
        get => _isExtending;
        set { _isExtending = value; OnPropertyChanged(); }
    }

    private string _displayModeText = "Mirror mode";
    public string DisplayModeText
    {
        get => _displayModeText;
        set { _displayModeText = value; OnPropertyChanged(); }
    }

    // Mode-related properties
    private bool _isWifiMode = true;
    public bool IsWifiMode
    {
        get => _isWifiMode;
        set { _isWifiMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(ModeLabelText)); OnPropertyChanged(nameof(ModeDescription)); }
    }

    public string ModeLabelText => IsWifiMode ? "Wi-Fi Mode" : "USB Mode";
    public string ModeDescription => IsWifiMode
        ? "Phone and PC are on the same LAN; the phone will automatically discover and connect to the PC"
        : "Connect via ADB port forwarding, supports USB cable or wireless debugging";

    private bool _isServiceStarted;
    public bool IsServiceStarted
    {
        get => _isServiceStarted;
        set { _isServiceStarted = value; OnPropertyChanged(); }
    }

    public string[] ResolutionOptions { get; } = { "Auto", "1280x720", "1920x1080", "2560x1440" };
    public string[] FpsOptions { get; } = { "30 fps", "60 fps" };

    private string _localIpAddress = "Getting...";
    public string LocalIpAddress
    {
        get => _localIpAddress;
        set { _localIpAddress = value; OnPropertyChanged(); }
    }

    public MainViewModel()
    {
        _config = new ServerConfig();
        _discoveryService = new DiscoveryService(_config);
        _streamingService = new StreamingService(_config);
        _networkService = new NetworkServerService(_config);
        _virtualDisplayService = new VirtualDisplayService();

        _discoveryService.DeviceConnected += OnDeviceFound;
        _networkService.InputEventReceived += OnInputReceived;

        LocalIpAddress = GetLocalIP();

        if (!_virtualDisplayService.IsDriverInstalled)
        {
            DisplayModeText = "Mirror mode (virtual display driver not installed)";
        }
    }

    private static string GetLocalIP()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var iface in interfaces)
            {
                if (iface.OperationalStatus != OperationalStatus.Up) continue;
                if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (iface.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                var name = iface.Name.ToLower();
                if (name.Contains("virtual") || name.Contains("vmware") ||
                    name.Contains("virtualbox") || name.Contains("hyper-v") ||
                    name.Contains("pseudo") || name.Contains("bluetooth") ||
                    name.Contains("wsl") || name.Contains("docker"))
                    continue;

                foreach (var addr in iface.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var ip = addr.Address.ToString();
                        if (ip.StartsWith("192.168.") || ip.StartsWith("10.") || ip.StartsWith("172."))
                            return ip;
                    }
                }
            }

            foreach (var iface in interfaces)
            {
                if (iface.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var addr in iface.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr.Address))
                        return addr.Address.ToString();
                }
            }
        }
        catch { }
        return "127.0.0.1";
    }

    /// <summary>
    /// Select Wi-Fi mode and start services
    /// </summary>
    public async Task StartWifiModeAsync()
    {
        IsWifiMode = true;
        ConnectionType = "Wi-Fi";
        await StartServicesAsync();
    }

    /// <summary>
    /// Select USB mode and start services
    /// </summary>
    public async Task StartUsbModeAsync()
    {
        IsWifiMode = false;
        ConnectionType = "USB";
        await StartServicesAsync();

        // USB mode automatically sets up port forwarding (run on background thread to avoid UI freeze)
        await SetupUsbForwardAsync();
    }

    public async Task StartServicesAsync()
    {
        if (_isServiceStarted) return;

        try
        {
            StatusText = "Starting services...";

            int captureWidth, captureHeight, captureX, captureY;

            if (_virtualDisplayService.IsDriverInstalled)
            {
                StatusText = "Creating virtual display...";
                bool created = await Task.Run(() => _virtualDisplayService.CreateVirtualDisplay());

                if (created && _virtualDisplayService.IsVirtualDisplayCreated)
                {
                    var allScreens = System.Windows.Forms.Screen.AllScreens;
                    System.Diagnostics.Debug.WriteLine($"=== All screens ({allScreens.Length}) ===");
                    foreach (var s in allScreens)
                    {
                        System.Diagnostics.Debug.WriteLine($"  {s.DeviceName}: {s.Bounds} Primary={s.Primary}");
                    }

                    var bounds = _virtualDisplayService.VirtualScreenBounds;
                    captureWidth = bounds.Width;
                    captureHeight = bounds.Height;
                    captureX = bounds.X;
                    captureY = bounds.Y;

                    _config.ScreenWidth = captureWidth;
                    _config.ScreenHeight = captureHeight;

                    InputInjector.SetScreenBounds(captureWidth, captureHeight);
                    InputInjector.SetScreenOffset(captureX, captureY);

                    IsExtending = true;
                    DisplayModeText = $"Extend mode {captureWidth}x{captureHeight} @({captureX},{captureY})";
                    StatusText = $"Virtual display created {captureWidth}x{captureHeight} @({captureX},{captureY})";
                }
                else
                {
                    var screen = System.Windows.Forms.Screen.PrimaryScreen;
                    captureWidth = screen?.Bounds.Width ?? 1920;
                    captureHeight = screen?.Bounds.Height ?? 1080;
                    captureX = 0;
                    captureY = 0;

                    _config.ScreenWidth = captureWidth;
                    _config.ScreenHeight = captureHeight;
                    InputInjector.SetScreenBounds(captureWidth, captureHeight);

                    DisplayModeText = $"Mirror mode (creation failed: {_virtualDisplayService.LastError})";
                }
            }
            else
            {
                var screen = System.Windows.Forms.Screen.PrimaryScreen;
                captureWidth = screen?.Bounds.Width ?? 1920;
                captureHeight = screen?.Bounds.Height ?? 1080;
                captureX = 0;
                captureY = 0;

                _config.ScreenWidth = captureWidth;
                _config.ScreenHeight = captureHeight;
                InputInjector.SetScreenBounds(captureWidth, captureHeight);

                DisplayModeText = $"Mirror mode ({_virtualDisplayService.LastError})";
            }

            _streamingService.Start(captureWidth, captureHeight, captureX, captureY, _virtualDisplayService.VirtualDeviceName);
            _streamingService.ClientConnectionChanged += connected =>
            {
                if (connected)
                {
                    var mode = IsExtending ? "Extend" : "Mirror";
                    StatusText = $"Connected | {mode} mode | {_config.ScreenWidth}x{_config.ScreenHeight} @{_config.TargetFps}fps";
                }
                else
                {
                    StatusText = "Video connection disconnected, waiting for reconnection...";
                    ConnectedDevice = null;
                }
            };

            // Only start UDP discovery service in Wi-Fi mode
            if (IsWifiMode)
            {
                _discoveryService.Start();
            }

            await _networkService.StartAsync();

            _isServiceStarted = true;
            IsServiceStarted = true;

            var modeText = IsExtending ? "Extend" : "Mirror";
            var connMode = IsWifiMode ? "Wi-Fi" : "USB";
            StatusText = IsWifiMode
                ? $"Waiting for device connection... [{modeText} mode] IP: {LocalIpAddress}"
                : $"USB forwarding started, please click USB connect on the phone [{modeText} mode]";
        }
        catch (Exception ex)
        {
            StatusText = $"Startup failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Stop all services, return to mode selection
    /// </summary>
    public void StopServices()
    {
        Disconnect();
        StopUsbForward();

        _streamingService.Stop();
        _networkService.Stop();
        _discoveryService.Stop();

        _virtualDisplayService.RemoveVirtualDisplay();

        _isServiceStarted = false;
        IsServiceStarted = false;
        ConnectedDevice = null;
        StatusText = "Not connected";
    }

    private async Task SetupUsbForwardAsync()
    {
        StatusText = "Setting up ADB forwarding...";

        try
        {
            var adbPath = await Task.Run(() => FindAdbPath());
            if (adbPath == null)
            {
                StatusText = "adb not found, please install Android SDK Platform Tools";
                return;
            }

            // Start adb server first
            await Task.Run(() => RunAdbCommand(adbPath, "start-server"));

            // Check device connection
            var devicesOutput = await Task.Run(() => RunAdbCommandWithOutput(adbPath, "devices"));

            if (string.IsNullOrEmpty(devicesOutput))
            {
                StatusText = "ADB command execution failed, please check if ADB is working properly";
                return;
            }

            // Check for offline devices, attempt reconnect
            if (devicesOutput.Contains("\toffline"))
            {
                StatusText = "Device offline detected, attempting to reconnect...";
                await Task.Run(() =>
                {
                    RunAdbCommand(adbPath, "reconnect");
                    System.Threading.Thread.Sleep(2000);
                });

                devicesOutput = await Task.Run(() => RunAdbCommandWithOutput(adbPath, "devices"));

                if (!devicesOutput.Contains("\tdevice"))
                {
                    StatusText = "Device offline, please re-authorize USB debugging on the phone, or re-pair wireless debugging";
                    return;
                }
            }
            else if (!devicesOutput.Contains("\tdevice"))
            {
                StatusText = "No connected device detected, please connect a device via USB cable or wireless debugging";
                return;
            }

            var videoPort = _config.VideoStreamPort;
            var inputPort = _config.InputEventPort;
            var fwdVideoPort = _config.UsbForwardVideoPort;
            var fwdInputPort = _config.UsbForwardInputPort;

            await Task.Run(() =>
            {
                // Use adb reverse: device local port → PC port (device actively connects to PC)
                RunAdbCommand(adbPath, $"reverse tcp:{fwdVideoPort} tcp:{videoPort}");
                RunAdbCommand(adbPath, $"reverse tcp:{fwdInputPort} tcp:{inputPort}");
            });

            _streamingService.AllowNewConnections();
            IsUsbForwarding = true;

            var modeText = IsExtending ? "Extend" : "Mirror";
            StatusText = $"ADB forwarding started, please click USB connect on the phone [{modeText} mode]";
        }
        catch (Exception ex)
        {
            StatusText = $"ADB forwarding failed: {ex.Message}";
        }
    }

    private void OnDeviceFound(DeviceInfo device)
    {
        if (ConnectedDevice == null)
        {
            _streamingService.AllowNewConnections();
            device.IsConnected = true;
            ConnectedDevice = device;
            ConnectionType = device.ConnectionType == Models.ConnectionType.USB ? "USB" : "Wi-Fi";
            StatusText = $"Connected: {device.DeviceName} | {_config.ScreenWidth}x{_config.ScreenHeight} @{_config.TargetFps}fps";
        }
    }

    private void OnInputReceived(InputEvent input)
    {
        _networkService.InjectInput(input);
    }

    private void ApplyResolution()
    {
        var resolutions = new[] { (0, 0), (1280, 720), (1920, 1080), (2560, 1440) };
        var (w, h) = resolutions[SelectedResolutionIndex];
        if (w > 0) _config.ScreenWidth = w;
        if (h > 0) _config.ScreenHeight = h;
    }

    private void ApplyFps()
    {
        _config.TargetFps = SelectedFpsIndex == 0 ? 30 : 60;
    }

    public void Disconnect()
    {
        _streamingService.DisconnectClient();
        ConnectedDevice = null;
        var modeText = IsExtending ? "Extend" : "Mirror";
        var connMode = IsWifiMode ? "Wi-Fi" : "USB";
        StatusText = IsWifiMode
            ? $"Disconnected, waiting for reconnection... [{modeText} mode] IP: {LocalIpAddress}"
            : $"Disconnected, waiting for reconnection... [USB mode]";
    }

    public void StopUsbForward()
    {
        try
        {
            var adbPath = FindAdbPath();
            if (adbPath != null)
            {
                RunAdbCommand(adbPath, $"reverse --remove tcp:{_config.UsbForwardVideoPort}");
                RunAdbCommand(adbPath, $"reverse --remove tcp:{_config.UsbForwardInputPort}");
            }
        }
        catch { }
        IsUsbForwarding = false;
    }

    private static string? FindAdbPath()
    {
        var candidates = new[]
        {
            @"D:\professionalSoftware\Android\SDK\platform-tools\adb.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Android", "Sdk", "platform-tools", "adb.exe"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "adb",
                Arguments = "version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null) return "adb";
        }
        catch { }

        return null;
    }

    private static void RunAdbCommand(string adbPath, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = adbPath,
            Arguments = args,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi);
        proc?.WaitForExit(5000); // 5 second timeout
        if (proc != null && !proc.HasExited)
            proc.Kill();
    }

    /// <summary>
    /// Execute ADB command and return output (read output first, then wait for exit to avoid deadlock)
    /// </summary>
    private static string RunAdbCommandWithOutput(string adbPath, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = adbPath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return "";

            // Read output first, then wait for exit (avoid deadlock)
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(8000);
            if (!proc.HasExited)
            {
                proc.Kill();
                return output; // Return the partial output already read
            }
            return output;
        }
        catch
        {
            return "";
        }
    }

    public void Shutdown()
    {
        StopServices();
        _virtualDisplayService.Dispose();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}