using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SRMS.Desktop.Data;

namespace SRMS.Desktop.Sync.Protocol
{
    public sealed class SyncSessionResult
    {
        public SyncOutcome Outcome        { get; set; }
        public int         PulledCount    { get; set; }
        public int         PushedCount    { get; set; }
        public int         ConflictsCount { get; set; }
        public string      PeerDeviceId   { get; set; }
        public string      PeerName       { get; set; }
    }

    public sealed class SyncProtocol : ISyncEndpoint
    {
        public const int ProtocolVersion = 1;

        private readonly DatabaseHelper _db;
        private readonly PeerStore      _peers;
        private readonly ChangeLogStore _log;

        public SyncProtocol(DatabaseHelper db)
        {
            _db      = db;
            _peers   = new PeerStore(db);
            _log     = new ChangeLogStore(db);
        }

        public async Task<SyncSessionResult> RunAsync(ISyncEndpoint remote, CancellationToken ct)
        {
            var result = new SyncSessionResult();
            var localDev = DeviceInfoStore.Current;

            var helloReq = new HelloRequest
            {
                DeviceId        = localDev.DeviceId,
                FriendlyName    = localDev.FriendlyName,
                SchoolId        = localDev.SchoolId,
                ProtocolVersion = ProtocolVersion,
                LastSeqRecv     = 0
            };

            var helloRespBytes = await remote
                .HandleAsync(SyncEnvelope.Wrap("hello", helloReq), ct)
                .ConfigureAwait(false);
            var (kind, helloResp) = SyncEnvelope.Unwrap<HelloResponse>(helloRespBytes);
            if (kind != "hello_resp") { result.Outcome = SyncOutcome.Aborted; return result; }

            result.PeerDeviceId = helloResp.DeviceId;
            result.PeerName     = helloResp.FriendlyName;

            if (helloResp.ProtocolVersion != ProtocolVersion)
            { result.Outcome = SyncOutcome.VersionMismatch; return result; }

            if (localDev.SchoolId.HasValue && helloResp.SchoolId.HasValue
                && localDev.SchoolId.Value != helloResp.SchoolId.Value)
            { result.Outcome = SyncOutcome.SchoolMismatch; return result; }

            var peer = _peers.GetOrCreate(helloResp.DeviceId);
            peer.DeviceName = helloResp.FriendlyName;
            peer.SchoolId   = helloResp.SchoolId;
            _peers.UpdateAfterHandshake(peer);

            var pullReq = new PullRequest { AfterSeq = peer.LastSeqRecv, BatchSize = 500 };
            var pullRespBytes = await remote
                .HandleAsync(SyncEnvelope.Wrap("pull", pullReq), ct)
                .ConfigureAwait(false);
            var (pk, pullResp) = SyncEnvelope.Unwrap<PullResponse>(pullRespBytes);
            if (pk != "pull_resp") { result.Outcome = SyncOutcome.Aborted; return result; }

            foreach (var change in pullResp.Changes)
            {
                var outcome = _log.Apply(change);
                result.PulledCount++;
                if (outcome == ApplyOutcome.ConflictLocalWon
                    || outcome == ApplyOutcome.ConflictTieLocalWon
                    || outcome == ApplyOutcome.ConflictTieIncomingWon)
                    result.ConflictsCount++;
            }
            _peers.SetLastSeqRecv(helloResp.DeviceId, pullResp.NewLastSeq);

            var toSend = _log.GetPendingForPeer(helloResp.DeviceId, 500);
            var pushReq = new PushRequest
            {
                Changes       = toSend,
                SenderLastSeq = _log.GetMaxSeq()
            };
            var pushRespBytes = await remote
                .HandleAsync(SyncEnvelope.Wrap("push", pushReq), ct)
                .ConfigureAwait(false);
            var (pushK, pushResp) = SyncEnvelope.Unwrap<PushResponse>(pushRespBytes);
            if (pushK != "push_resp") { result.Outcome = SyncOutcome.Aborted; return result; }

            result.PushedCount = pushResp.AcceptedCount;

            long sentMax = 0;
            foreach (var c in toSend) if (c.Seq > sentMax) sentMax = c.Seq;
            if (sentMax > 0) _peers.SetLastSeqSent(helloResp.DeviceId, sentMax);

            await remote.HandleAsync(SyncEnvelope.Wrap("bye", new ByeMessage { Reason = "ok" }), ct)
                .ConfigureAwait(false);

            DeviceInfoStore.TouchLastSync(_db);
            result.Outcome = SyncOutcome.Success;
            return result;
        }

        public Task<byte[]> HandleAsync(byte[] request, CancellationToken ct)
        {
            string kind = SyncEnvelope.PeekKind(request);

            switch (kind)
            {
                case "hello":
                {
                    var (_, req) = SyncEnvelope.Unwrap<HelloRequest>(request);
                    var local = DeviceInfoStore.Current;

                    var resp = new HelloResponse
                    {
                        DeviceId        = local.DeviceId,
                        FriendlyName    = local.FriendlyName,
                        SchoolId        = local.SchoolId,
                        ProtocolVersion = ProtocolVersion,
                        LastSeqRecv     = 0
                    };

                    var peer = _peers.GetOrCreate(req.DeviceId);
                    peer.DeviceName = req.FriendlyName;
                    peer.SchoolId   = req.SchoolId;
                    _peers.UpdateAfterHandshake(peer);

                    Debug.WriteLine($"[SRMS.Sync] HELLO from {req.FriendlyName} ({req.DeviceId})");
                    return Task.FromResult(SyncEnvelope.Wrap("hello_resp", resp));
                }

                case "pull":
                {
                    var (_, req) = SyncEnvelope.Unwrap<PullRequest>(request);
                    var changes = _log.GetChangesAfter(req.AfterSeq, req.BatchSize);
                    long newLast = req.AfterSeq;
                    foreach (var c in changes) if (c.Seq > newLast) newLast = c.Seq;
                    Debug.WriteLine($"[SRMS.Sync] PULL after seq={req.AfterSeq} → {changes.Count} rows");
                    return Task.FromResult(SyncEnvelope.Wrap("pull_resp",
                        new PullResponse { Changes = changes, NewLastSeq = newLast }));
                }

                case "push":
                {
                    var (_, req) = SyncEnvelope.Unwrap<PushRequest>(request);
                    int accepted = 0;
                    foreach (var change in req.Changes)
                    {
                        try { _log.Apply(change); accepted++; }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[SRMS.Sync] Apply failed for {change.TableName}/{change.SyncUuid}: {ex.Message}");
                        }
                    }
                    Debug.WriteLine($"[SRMS.Sync] PUSH accepted {accepted}/{req.Changes.Count}");
                    return Task.FromResult(SyncEnvelope.Wrap("push_resp",
                        new PushResponse { AcceptedCount = accepted, ReceiverLastSeq = _log.GetMaxSeq() }));
                }

                case "bye":
                    Debug.WriteLine("[SRMS.Sync] BYE");
                    return Task.FromResult(Array.Empty<byte>());

                default:
                    Debug.WriteLine($"[SRMS.Sync] Unknown message kind: {kind}");
                    return Task.FromResult(Array.Empty<byte>());
            }
        }
    }
}