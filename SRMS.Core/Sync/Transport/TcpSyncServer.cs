using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SRMS.Desktop.Sync.Protocol;

namespace SRMS.Desktop.Sync.Transport
{
    public sealed class TcpSyncServer : IAsyncDisposable
    {
        private readonly SyncProtocol _protocol;
        private readonly int _port;
        private TcpListener _listener;
        private CancellationTokenSource _cts;

        public int Port => _port;

        public TcpSyncServer(SyncProtocol protocol, int port = 8765)
        {
            _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            _port     = port;
        }

        public void Start()
        {
            if (_listener != null) return;
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
            Debug.WriteLine($"[SRMS.Sync] TCP server listening on 0.0.0.0:{_port}");
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = null;
                try { client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { Debug.WriteLine($"[SRMS.Sync] Accept failed: {ex.Message}"); continue; }

                _ = Task.Run(() => HandleClientAsync(client, ct));
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            try
            {
                using (client)
                {
                    client.NoDelay = true;
                    var stream = client.GetStream();

                    while (!ct.IsCancellationRequested)
                    {
                        byte[] request;
                        try { request = await FrameCodec.ReadFrameAsync(stream, ct).ConfigureAwait(false); }
                        catch { break; }
                        if (request == null) break;

                        byte[] response = await _protocol.HandleAsync(request, ct).ConfigureAwait(false);
                        if (response != null && response.Length > 0)
                            await FrameCodec.WriteFrameAsync(stream, response, ct).ConfigureAwait(false);

                        if (response == null || response.Length == 0) break;
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[SRMS.Sync] Client handler: {ex.Message}"); }
        }

        public async ValueTask DisposeAsync()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            await Task.CompletedTask;
        }
    }
}