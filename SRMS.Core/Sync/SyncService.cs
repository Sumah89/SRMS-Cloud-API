using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SRMS.Desktop.Data;
using SRMS.Desktop.Sync.Protocol;
using SRMS.Desktop.Sync.Transport;

namespace SRMS.Desktop.Sync
{
    public sealed class SyncService : IAsyncDisposable
    {
        private readonly DatabaseHelper _db;
        private readonly SyncProtocol   _protocol;
        private TcpSyncServer           _lanServer;
        private LanDiscovery            _lanDiscovery;
        private CloudTransport          _cloud;

        public SyncStatus Status { get; } = new SyncStatus();

        public SyncService(DatabaseHelper db)
        {
            _db       = db;
            _protocol = new SyncProtocol(db);
        }

        public void ConfigureCloud(CloudTransport cloud)
        {
            _cloud = cloud;
            if (_cloud != null)
            {
                _cloud.RemoteChangeArrived += change =>
                {
                    try
                    {
                        var store = new ChangeLogStore(_db);
                        store.Apply(change);
                        Debug.WriteLine($"[SRMS.Sync] Realtime change applied: {change.TableName}/{change.SyncUuid}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SRMS.Sync] Realtime apply failed: {ex.Message}");
                    }
                };
            }
        }

        public async Task StartLanServerAsync()
        {
            if (_lanServer != null) return;
            var dev = DeviceInfoStore.Current;
            var (server, discovery) = await LanTransport.StartServerAsync(
                _protocol, dev.DeviceId, dev.FriendlyName, dev.Platform)
                .ConfigureAwait(false);
            _lanServer = server;
            _lanDiscovery = discovery;
            Debug.WriteLine($"[SRMS.Sync] LAN server up on port {LanTransport.DefaultTcpPort}");
        }

        public async Task<SyncSessionResult> SyncNowAsync(CancellationToken ct = default)
        {
            Status.LastAttempt = DateTime.UtcNow;

            // ─── Cloud first (instant path) ────────────────────────
            if (_cloud != null)
            {
                try
                {
                    Status.Transport = "Cloud";
                    Status.State     = SyncState.Exchanging;
                    Status.Message   = "Syncing via cloud…";
                    Status.RaiseChanged();

                    var peerStore = new PeerStore(_db);
                    var peer = peerStore.GetOrCreate("cloud");
                    var store = new ChangeLogStore(_db);

                    // The desktop/mobile database is the complete offline working copy.
                    // Publish local changes first; the cloud is a replica and is not the
                    // source of truth for normal school operations.
                    var toSend = store.GetPendingForPeer("cloud", 500);
                    var push = await _cloud.PushAsync(toSend, ct).ConfigureAwait(false);

                    long sentMax = 0;
                    foreach (var c in toSend) if (c.Seq > sentMax) sentMax = c.Seq;
                    if (sentMax > 0) peerStore.SetLastSeqSent("cloud", sentMax);

                    var pull = await _cloud.PullAsync(peer.LastSeqRecv, 500, ct).ConfigureAwait(false);
                    int pulled = 0;
                    foreach (var c in pull.Changes) { store.Apply(c); pulled++; }
                    peerStore.SetLastSeqRecv("cloud", pull.NewLastSeq);

                    var result = new SyncSessionResult
                    {
                        Outcome     = SyncOutcome.Success,
                        PulledCount = pulled,
                        PushedCount = push.AcceptedCount,
                        PeerName    = "cloud"
                    };
                    ApplyResult(result, "Cloud");
                    return result;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SRMS.Sync] Cloud attempt failed: {ex.Message}");
                }
            }

            // ─── LAN fallback ──────────────────────────────────────
            try
            {
                Status.Transport = "LAN";
                Status.State     = SyncState.Discovering;
                Status.Message   = "Looking for LAN peers…";
                Status.RaiseChanged();

                var dev = DeviceInfoStore.Current;
                var peerBeacon = await LanTransport.FindPeerAsync(dev.DeviceId, 4000, ct).ConfigureAwait(false);

                if (peerBeacon != null && !string.IsNullOrEmpty(peerBeacon.SenderIp))
                {
                    Status.State    = SyncState.Connecting;
                    Status.PeerName = peerBeacon.FriendlyName;
                    Status.RaiseChanged();

                    var ip = IPAddress.Parse(peerBeacon.SenderIp);
                    await using var endpoint = await LanTransport
                        .ConnectAsync(ip, peerBeacon.TcpPort, 4000, ct).ConfigureAwait(false);

                    Status.State = SyncState.Exchanging;
                    Status.RaiseChanged();

                    var result = await _protocol.RunAsync(endpoint, ct).ConfigureAwait(false);
                    ApplyResult(result, "LAN");
                    return result;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SRMS.Sync] LAN attempt failed: {ex.Message}");
            }

            // ─── USB fallback ──────────────────────────────────────
            try
            {
                Status.Transport = "USB";
                Status.State     = SyncState.Connecting;
                Status.Message   = "Trying USB tunnel…";
                Status.RaiseChanged();

                await using var endpoint = await UsbTransport
                    .ConnectFromPhoneAsync(ct: ct).ConfigureAwait(false);

                Status.State = SyncState.Exchanging;
                Status.RaiseChanged();

                var result = await _protocol.RunAsync(endpoint, ct).ConfigureAwait(false);
                ApplyResult(result, "USB");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SRMS.Sync] USB attempt failed: {ex.Message}");
            }

            Status.State   = SyncState.NoPeer;
            Status.Message = "No peer reachable over Cloud, LAN or USB.";
            Status.RaiseChanged();
            return new SyncSessionResult { Outcome = SyncOutcome.Aborted };
        }

        private void ApplyResult(SyncSessionResult result, string transport)
        {
            Status.LastPulled         = result.PulledCount;
            Status.LastPushed         = result.PushedCount;
            Status.LastConflicts      = result.ConflictsCount;
            Status.PeerName           = result.PeerName;
            Status.Transport          = transport;

            if (result.Outcome == SyncOutcome.Success)
            {
                Status.State       = SyncState.Succeeded;
                Status.LastSuccess = DateTime.UtcNow;
                Status.Message     = $"Synced via {transport}: pulled {result.PulledCount}, pushed {result.PushedCount}.";
            }
            else
            {
                Status.State   = SyncState.Failed;
                Status.Message = $"Sync failed: {result.Outcome}.";
            }
            Status.RaiseChanged();
        }

        public async ValueTask DisposeAsync()
        {
            if (_lanDiscovery != null) await _lanDiscovery.DisposeAsync().ConfigureAwait(false);
            if (_lanServer    != null) await _lanServer.DisposeAsync().ConfigureAwait(false);
            if (_cloud        != null) await _cloud.DisposeAsync().ConfigureAwait(false);
        }
    }
}