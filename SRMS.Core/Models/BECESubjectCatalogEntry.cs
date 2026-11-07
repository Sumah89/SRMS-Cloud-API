namespace SRMS.Desktop.Models
{
    /// <summary>
    /// One entry in a school's BECE subject catalogue.
    ///
    /// Participation flags:
    ///   IsCore      : compulsory for every candidate
    ///   IsOptional  : candidate may offer
    ///   IsSelective : candidate may offer (elective tier)
    ///
    /// IsGeneral  : the subject is offered by every candidate
    ///              (all Core subjects are General; some Optional and
    ///              Selective subjects may also be General)
    /// </summary>
    public class BECESubjectCatalogEntry
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }

        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }

        public bool IsCore { get; set; }
        public bool IsOptional { get; set; }
        public bool IsSelective { get; set; }
        public bool IsGeneral { get; set; }

        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; }

        public BECESubjectCatalogEntry Clone()
        {
            return new BECESubjectCatalogEntry
            {
                Id = Id,
                SchoolId = SchoolId,
                SubjectName = SubjectName,
                SubjectCode = SubjectCode,
                IsCore = IsCore,
                IsOptional = IsOptional,
                IsSelective = IsSelective,
                IsGeneral = IsGeneral,
                IsActive = IsActive,
                SortOrder = SortOrder
            };
        }
    }
}