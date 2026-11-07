using System.Threading;
using System.Threading.Tasks;

namespace SRMS.Desktop.Sync.Protocol
{
    public sealed class LoopbackEndpoint : ISyncEndpoint
    {
        private readonly SyncProtocol _inner;
        public LoopbackEndpoint(SyncProtocol inner) { _inner = inner; }

        public Task<byte[]> HandleAsync(byte[] request, CancellationToken ct)
            => _inner.HandleAsync(request, ct);
    }
}