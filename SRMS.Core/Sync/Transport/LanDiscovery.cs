using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SRMS.Desktop.Sync.Transport
{
    public sealed class LanBeacon
    {
        public string DeviceId     { get; set; }
        public string FriendlyName { get; set; }
        public int    TcpPort      { get; set; }
        public string Platform     { get; set; }
        public long   Timestamp    { get; set; }
        public string SenderIp     { get; set; }
    }

    public sealed class LanDiscovery : IAsyncDisposable
    {
        public const int BeaconPort = 8766;
        private const int BroadcastIntervalMs = 3000;

        private readonly LanBeacon _self;
        private UdpClient _listener;
        private CancellationTokenSource _cts;

        public event Action<LanBeacon, IPEndPoint> BeaconReceived;

        public LanDiscovery(LanBeacon self) { _self = self; }

        public void Start()
        {
            if (_listener != null) return;
            _cts = new CancellationTokenSource();

            try
            {
                _listener = new UdpClient();
                _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Client.Bind(new IPEndPoint(IPAddress.Any, BeaconPort));
                _listener.EnableBroadcast = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SRMS.LAN] Listener bind failed: {ex.Message}");
                return;
            }

            _ = Task.Run(() => ListenLoopAsync(_cts.Token));
            _ = Task.Run(() => BroadcastLoopAsync(_cts.Token));
            Debug.WriteLine($"[SRMS.LAN] Discovery started on UDP {BeaconPort}");
        }

        private async Task ListenLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await _listener.ReceiveAsync(ct).ConfigureAwait(false);
                    string json = Encoding.UTF8.GetString(result.Buffer);
                    var beacon = JsonSerializer.Deserialize<LanBeacon>(json);
                    if (beacon == null) continue;
                    if (beacon.DeviceId == _self.DeviceId) continue;

                    beacon.SenderIp = result.RemoteEndPoint.Address.ToString();
                    BeaconReceived?.Invoke(beacon, result.RemoteEndPoint);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { Debug.WriteLine($"[SRMS.LAN] Listen error: {ex.Message}"); }
            }
        }

        private async Task BroadcastLoopAsync(CancellationToken ct)
        {
            using var sender = new UdpClient();
            sender.EnableBroadcast = true;
            var target = new IPEndPoint(IPAddress.Broadcast, BeaconPort);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    _self.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    string json = JsonSerializer.Serialize(_self);
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    await sender.SendAsync(bytes, bytes.Length, target).ConfigureAwait(false);
                }
                catch (Exception ex) { Debug.WriteLine($"[SRMS.LAN] Broadcast error: {ex.Message}"); }

                try { await Task.Delay(BroadcastIntervalMs, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        public async ValueTask DisposeAsync()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Dispose(); } catch { }
            await Task.CompletedTask;
        }
    }
}