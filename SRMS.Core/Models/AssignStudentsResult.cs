namespace SRMS.Desktop.Models
{
    public class AssignStudentsResult
    {
        public int MovedCount { get; set; }
        public int BatchId { get; set; }

        public bool Succeeded => MovedCount > 0;
    }
}