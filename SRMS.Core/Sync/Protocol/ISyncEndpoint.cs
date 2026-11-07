using System.Threading;
using System.Threading.Tasks;

namespace SRMS.Desktop.Sync.Protocol
{
    public interface ISyncEndpoint
    {
        Task<byte[]> HandleAsync(byte[] request, CancellationToken ct);
    }
}