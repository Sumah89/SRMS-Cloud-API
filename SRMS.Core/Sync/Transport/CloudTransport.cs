using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using SRMS.Desktop.Sync.Protocol;

namespace SRMS.Desktop.Sync.Transport
{
    public sealed class CloudTransport : IAsyncDisposable
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly Func<Task<string>> _idTokenProvider;
        private HubConnection _hub;

        public event Action<SyncChangeRow> RemoteChangeArrived;

        public CloudTransport(string baseUrl, Func<Task<string>> idTokenProvider)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _idTokenProvider = idTokenProvider;
            _http = new HttpClient { BaseAddress = new Uri(_baseUrl + "/") };
        }

        private async Task AuthorizeAsync(HttpRequestMessage req)
        {
            string token = await _idTokenProvider().ConfigureAwait(false);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        public async Task<PullResponse> PullAsync(long afterSeq, int batchSize, CancellationToken ct)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "sync/pull")
            {
                Content = JsonContent.Create(new { afterSeq, batchSize })
            };
            await AuthorizeAsync(req).ConfigureAwait(false);

            var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadFromJsonAsync<CloudPullResponse>(cancellationToken: ct);
            var changes = new List<SyncChangeRow>();
            foreach (var c in body!.Changes)
            {
                changes.Add(new SyncChangeRow
                {
                    Seq       = c.Seq,
                    SyncUuid  = c.SyncUuid,
                    TableName = c.TableName,
                    Op        = c.Op,
                    Payload   = c.Payload,
                    UpdatedAt = c.UpdatedAt,
                    UpdatedBy = c.UpdatedBy
                });
            }
            return new PullResponse { Changes = changes, NewLastSeq = body.NewLastSeq };
        }

        public async Task<PushResponse> PushAsync(List<SyncChangeRow> changes, CancellationToken ct)
        {
            var wire = new List<object>();
            foreach (var c in changes)
            {
                wire.Add(new
                {
                    seq = c.Seq, syncUuid = c.SyncUuid, tableName = c.TableName,
                    op = c.Op, payload = c.Payload, updatedAt = c.UpdatedAt, updatedBy = c.UpdatedBy
                });
            }
            var req = new HttpRequestMessage(HttpMethod.Post, "sync/push")
            {
                Content = JsonContent.Create(new { changes = wire })
            };
            await AuthorizeAsync(req).ConfigureAwait(false);

            var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadFromJsonAsync<CloudPushResponse>(cancellationToken: ct);
            return new PushResponse
            {
                AcceptedCount   = body!.AcceptedCount,
                ReceiverLastSeq = body.ServerLastSeq
            };
        }

        public async Task StartRealtimeAsync(CancellationToken ct)
        {
            if (_hub != null) return;

            _hub = new HubConnectionBuilder()
                .WithUrl($"{_baseUrl}/sync/hub", options =>
                {
                    options.AccessTokenProvider = async () => await _idTokenProvider();
                })
                .WithAutomaticReconnect()
                .Build();

            _hub.On<CloudChangeRow>("change", c =>
            {
                RemoteChangeArrived?.Invoke(new SyncChangeRow
                {
                    Seq       = c.Seq,
                    SyncUuid  = c.SyncUuid,
                    TableName = c.TableName,
                    Op        = c.Op,
                    Payload   = c.Payload,
                    UpdatedAt = c.UpdatedAt,
                    UpdatedBy = c.UpdatedBy
                });
            });

            await _hub.StartAsync(ct).ConfigureAwait(false);
            Debug.WriteLine("[SRMS.Cloud] SignalR connected.");
        }

        public async ValueTask DisposeAsync()
        {
            if (_hub != null)
            {
                try { await _hub.DisposeAsync().ConfigureAwait(false); } catch { }
                _hub = null;
            }
            _http?.Dispose();
        }

        private sealed class CloudPullResponse
        {
            public List<CloudChangeRow> Changes { get; set; } = new();
            public long NewLastSeq { get; set; }
        }

        private sealed class CloudPushResponse
        {
            public int AcceptedCount { get; set; }
            public long ServerLastSeq { get; set; }
        }

        private sealed class CloudChangeRow
        {
            public long Seq { get; set; }
            public string SyncUuid { get; set; } = "";
            public string TableName { get; set; } = "";
            public string Op { get; set; } = "";
            public string Payload { get; set; } = "";
            public long UpdatedAt { get; set; }
            public string UpdatedBy { get; set; } = "";
        }
    }
}