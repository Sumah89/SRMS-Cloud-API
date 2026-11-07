using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SRMS.Desktop.Sync.Transport
{
    internal static class FrameCodec
    {
        public const int MaxFrameSize = 4 * 1024 * 1024;

        public static async Task WriteFrameAsync(NetworkStream stream, byte[] payload, CancellationToken ct)
        {
            if (payload == null) payload = Array.Empty<byte>();
            byte[] header = new byte[4];
            header[0] = (byte)((payload.Length >> 24) & 0xFF);
            header[1] = (byte)((payload.Length >> 16) & 0xFF);
            header[2] = (byte)((payload.Length >> 8)  & 0xFF);
            header[3] = (byte)( payload.Length        & 0xFF);

            await stream.WriteAsync(header, 0, 4, ct).ConfigureAwait(false);
            if (payload.Length > 0)
                await stream.WriteAsync(payload, 0, payload.Length, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        public static async Task<byte[]> ReadFrameAsync(NetworkStream stream, CancellationToken ct)
        {
            byte[] header = await ReadExactAsync(stream, 4, ct).ConfigureAwait(false);
            if (header == null) return null;

            int len = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            if (len < 0 || len > MaxFrameSize)
                throw new IOException($"Frame size {len} out of range.");

            if (len == 0) return Array.Empty<byte>();
            return await ReadExactAsync(stream, len, ct).ConfigureAwait(false);
        }

        private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, CancellationToken ct)
        {
            byte[] buf = new byte[count];
            int got = 0;
            while (got < count)
            {
                int n = await stream.ReadAsync(buf, got, count - got, ct).ConfigureAwait(false);
                if (n == 0) return got == 0 ? null : throw new IOException("Stream ended mid-frame.");
                got += n;
            }
            return buf;
        }
    }
}