using System.Collections.Generic;

namespace SRMS.Desktop.Models
{
    /// <summary>
    /// Populated before committing a promotion, used to render the
    /// confirmation dialog.
    /// </summary>
    public class PromotionPreview
    {
        public int SourceClassId { get; set; }
        public int DestinationClassId { get; set; }
        public string SourceClassName { get; set; }
        public string DestinationClassName { get; set; }
        public string SourceAcademicYear { get; set; }
        public string DestinationAcademicYear { get; set; }

        public List<PreviewStudent> ToPromote { get; set; } = new List<PreviewStudent>();
        public List<PreviewStudent> AlreadyInDestination { get; set; } = new List<PreviewStudent>();
        public List<PreviewStudent> Invalid { get; set; } = new List<PreviewStudent>();

        public int TotalToProcess => ToPromote.Count;
    }

    public class PreviewStudent
    {
        public int StudentId { get; set; }
        public string AdmissionNumber { get; set; }
        public string FullName { get; set; }
        public string Reason { get; set; }
    }
}