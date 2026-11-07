using System.Text.Json;
using System.Text.Json.Serialization;

namespace SRMS.Desktop.Sync.Protocol
{
    internal class SyncEnvelope
    {
        public string Kind    { get; set; }
        public string Payload { get; set; }

        private static readonly JsonSerializerOptions Opts = new()
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static byte[] Wrap<T>(string kind, T payload)
        {
            var env = new SyncEnvelope
            {
                Kind = kind,
                Payload = JsonSerializer.Serialize(payload, Opts)
            };
            return JsonSerializer.SerializeToUtf8Bytes(env, Opts);
        }

        public static (string Kind, T Payload) Unwrap<T>(byte[] bytes)
        {
            var env = JsonSerializer.Deserialize<SyncEnvelope>(bytes, Opts);
            var payload = JsonSerializer.Deserialize<T>(env.Payload, Opts);
            return (env.Kind, payload);
        }

        public static string PeekKind(byte[] bytes)
        {
            var env = JsonSerializer.Deserialize<SyncEnvelope>(bytes, Opts);
            return env.Kind;
        }
    }
}