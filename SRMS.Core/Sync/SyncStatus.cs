using System;

namespace SRMS.Desktop.Sync
{
    public enum SyncState
    {
        Idle,
        Discovering,
        Connecting,
        Exchanging,
        Succeeded,
        Failed,
        NoPeer
    }

    public sealed class SyncStatus
    {
        public SyncState State         { get; set; } = SyncState.Idle;
        public string    Transport     { get; set; }
        public string    PeerName      { get; set; }
        public string    Message       { get; set; }
        public DateTime  LastAttempt   { get; set; }
        public DateTime? LastSuccess   { get; set; }
        public int       LastPulled    { get; set; }
        public int       LastPushed    { get; set; }
        public int       LastConflicts { get; set; }

        public event Action Changed;
        public void RaiseChanged() => Changed?.Invoke();
    }
}