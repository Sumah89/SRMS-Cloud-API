using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SRMS.Desktop.Sync.Protocol;

namespace SRMS.Desktop.Sync.Transport
{
    public sealed class TcpSyncEndpoint : ISyncEndpoint, IAsyncDisposable
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;

        public TcpSyncEndpoint(TcpClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _client.NoDelay = true;
            _stream = _client.GetStream();
        }

        public async Task<byte[]> HandleAsync(byte[] request, CancellationToken ct)
        {
            await FrameCodec.WriteFrameAsync(_stream, request, ct).ConfigureAwait(false);
            return await FrameCodec.ReadFrameAsync(_stream, ct).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync()
        {
            try { _stream?.Dispose(); } catch { }
            try { _client?.Dispose(); } catch { }
            return ValueTask.CompletedTask;
        }
    }
}