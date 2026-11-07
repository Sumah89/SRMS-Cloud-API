using System;

namespace SRMS.Desktop.Models
{
    public class Class
    {
        public int Id { get; set; }
        public int SchoolId { get; set; }
        public string ClassName { get; set; }
        public string ClassLevel { get; set; }
        public string Stream { get; set; }
        public string AcademicYear { get; set; }
        public string ClassTeacherName { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }

        public string ResultTypeOverride { get; set; }

        public string GetEffectiveResultType(string schoolWideResultType)
        {
            return string.IsNullOrWhiteSpace(ResultTypeOverride)
                ? (schoolWideResultType ?? "FiveTests")
                : ResultTypeOverride;
        }
    }
}