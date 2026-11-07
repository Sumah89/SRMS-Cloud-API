namespace SRMS.Cloud
{
    public sealed record ChangeRow(
        long   Seq,
        string SyncUuid,
        string TableName,
        string Op,
        string Payload,
        long   UpdatedAt,
        string UpdatedBy);
}