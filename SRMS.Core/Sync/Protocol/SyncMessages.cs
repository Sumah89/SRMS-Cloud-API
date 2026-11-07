using System.Collections.Generic;

namespace SRMS.Desktop.Sync.Protocol
{
    public class SyncChangeRow
    {
        public long   Seq       { get; set; }
        public string SyncUuid  { get; set; }
        public string TableName { get; set; }
        public string Op        { get; set; }
        public string Payload   { get; set; }
        public long   UpdatedAt { get; set; }
        public string UpdatedBy { get; set; }
    }

    public class HelloRequest
    {
        public string DeviceId         { get; set; }
        public string FriendlyName     { get; set; }
        public int?   SchoolId         { get; set; }
        public int    ProtocolVersion  { get; set; }
        public long   LastSeqRecv      { get; set; }
    }

    public class HelloResponse
    {
        public string DeviceId         { get; set; }
        public string FriendlyName     { get; set; }
        public int?   SchoolId         { get; set; }
        public int    ProtocolVersion  { get; set; }
        public long   LastSeqRecv      { get; set; }
    }

    public class PullRequest
    {
        public long AfterSeq  { get; set; }
        public int  BatchSize { get; set; } = 500;
    }

    public class PullResponse
    {
        public List<SyncChangeRow> Changes { get; set; } = new();
        public long NewLastSeq { get; set; }
    }

    public class PushRequest
    {
        public List<SyncChangeRow> Changes { get; set; } = new();
        public long SenderLastSeq { get; set; }
    }

    public class PushResponse
    {
        public int  AcceptedCount   { get; set; }
        public long ReceiverLastSeq { get; set; }
    }

    public class ByeMessage { public string Reason { get; set; } }

    public enum SyncOutcome
    {
        Success,
        VersionMismatch,
        SchoolMismatch,
        Aborted
    }
}