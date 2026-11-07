using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SRMS.Desktop.Sync.Protocol;

namespace SRMS.Desktop.Sync.Transport
{
    public static class UsbTransport
    {
        public const int DefaultPort = 8765;

        public static async Task<TcpSyncServer> PrepareDesktopListenerAsync(
            SyncProtocol protocol, string adbPath = "adb", int port = DefaultPort)
        {
            string arg = $"reverse tcp:{port} tcp:{port}";
            Debug.WriteLine($"[SRMS.Sync] Running: {adbPath} {arg}");

            try
            {
                var psi = new ProcessStartInfo(adbPath, arg)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                await proc.WaitForExitAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SRMS.Sync] adb reverse failed: {ex.Message}");
            }

            var server = new TcpSyncServer(protocol, port);
            server.Start();
            return server;
        }

        public static async Task<TcpSyncEndpoint> ConnectFromPhoneAsync(
            string host = "127.0.0.1", int port = DefaultPort, int timeoutMs = 3000,
            CancellationToken ct = default)
        {
            var client = new TcpClient();
            client.NoDelay = true;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            try
            {
                await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
                Debug.WriteLine($"[SRMS.Sync] USB tunnel connected to {host}:{port}");
                return new TcpSyncEndpoint(client);
            }
            catch
            {
                client.Dispose();
                throw new InvalidOperationException(
                    "USB tunnel not reachable. Is `adb reverse` running on the desktop?");
            }
        }
    }
}