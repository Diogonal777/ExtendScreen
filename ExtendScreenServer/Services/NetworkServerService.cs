using System.Net;
using System.Net.Sockets;
using ExtendScreenServer.Models;

namespace ExtendScreenServer.Services;

/// <summary>
/// Network transport service — sends encoded video frames, receives touch input
/// </summary>
public class NetworkServerService : IDisposable
{
    private readonly ServerConfig _config;
    private TcpListener? _inputListener;
    private TcpClient? _inputClient;
    private NetworkStream? _inputStream;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public event Action<InputEvent>? InputEventReceived;

    public bool IsConnected => _inputClient?.Connected == true;

    public NetworkServerService(ServerConfig config)
    {
        _config = config;
    }

    public async Task StartAsync()
    {
        _isRunning = true;
        _cts = new CancellationTokenSource();

        // Only listen on input port, video port is handled by StreamingService
        _inputListener = new TcpListener(IPAddress.Any, _config.InputEventPort);
        _inputListener.Start();

        // Accept input connections
        _ = Task.Run(async () =>
        {
            try
            {
                _inputClient = await _inputListener!.AcceptTcpClientAsync(_cts.Token);
                _inputStream = _inputClient.GetStream();
                System.Diagnostics.Debug.WriteLine("Input client connected.");
                _ = Task.Run(ReceiveInputLoop, _cts.Token);
            }
            catch (OperationCanceledException) { }
        }, _cts.Token);
    }

    /// <summary>
    /// Receive touch input from Android
    /// </summary>
    private async Task ReceiveInputLoop()
    {
        var buffer = new byte[1024];
        while (!_cts!.IsCancellationRequested && _inputClient?.Connected == true)
        {
            try
            {
                var bytesRead = await _inputStream!.ReadAsync(buffer, 0, buffer.Length, _cts.Token);
                if (bytesRead == 0) break;

                var inputEvent = InputEvent.Deserialize(buffer.AsSpan(0, bytesRead));
                if (inputEvent != null)
                {
                    InputEventReceived?.Invoke(inputEvent);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Input receive error: {ex.Message}");
                break;
            }
        }
    }

    /// <summary>
    /// Simulate touch input to Windows system
    /// </summary>
    public void InjectInput(InputEvent input)
    {
        InputInjector.Inject(input);
    }

    public void Stop()
    {
        _isRunning = false;
        _cts?.Cancel();
        _inputStream?.Close();
        _inputClient?.Close();
        _inputListener?.Stop();
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}