using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SRMS.Desktop.Sync.Transport
{
    public static class LanTransport
    {
        public const int DefaultTcpPort = 8767;

        public static async Task<(TcpSyncServer Server, LanDiscovery Discovery)> StartServerAsync(
            Protocol.SyncProtocol protocol, string deviceId, string friendlyName,
            string platform, int tcpPort = DefaultTcpPort)
        {
            var server = new TcpSyncServer(protocol, tcpPort);
            server.Start();

            var beacon = new LanBeacon
            {
                DeviceId     = deviceId,
                FriendlyName = friendlyName,
                TcpPort      = tcpPort,
                Platform     = platform
            };
            var discovery = new LanDiscovery(beacon);
            discovery.Start();

            await Task.CompletedTask;
            return (server, discovery);
        }

        public static async Task<LanBeacon> FindPeerAsync(
            string ownDeviceId, int timeoutMs = 4000, CancellationToken ct = default)
        {
            var self = new LanBeacon
            {
                DeviceId     = ownDeviceId,
                FriendlyName = "",
                TcpPort      = 0,
                Platform     = "android"
            };
            var discovery = new LanDiscovery(self);

            var tcs = new TaskCompletionSource<LanBeacon>();
            discovery.BeaconReceived += (beacon, endpoint) =>
            {
                if (tcs.Task.IsCompleted) return;
                if (beacon.TcpPort <= 0) return;
                tcs.TrySetResult(beacon);
            };

            discovery.Start();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            using (cts.Token.Register(() => tcs.TrySetResult(null)))
            {
                var result = await tcs.Task.ConfigureAwait(false);
                await discovery.DisposeAsync().ConfigureAwait(false);
                return result;
            }
        }

        public static async Task<TcpSyncEndpoint> ConnectAsync(
            IPAddress peerIp, int peerPort, int timeoutMs = 4000, CancellationToken ct = default)
        {
            var client = new TcpClient();
            client.NoDelay = true;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            try
            {
                await client.ConnectAsync(peerIp, peerPort, cts.Token).ConfigureAwait(false);
                Debug.WriteLine($"[SRMS.LAN] Connected to {peerIp}:{peerPort}");
                return new TcpSyncEndpoint(client);
            }
            catch
            {
                client.Dispose();
                throw new InvalidOperationException($"LAN connect to {peerIp}:{peerPort} failed.");
            }
        }
    }
}