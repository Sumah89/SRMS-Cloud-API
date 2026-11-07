using System;

namespace SRMS.Desktop.Models
{
    public class AcademicPerformance
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public int SubjectId { get; set; }
        public int Term { get; set; }
        public string AcademicYear { get; set; }

        // 5-Test properties
        public decimal? T1 { get; set; }
        public decimal? T2 { get; set; }
        public decimal? T3 { get; set; }
        public decimal? T4 { get; set; }
        public decimal? Exam { get; set; }

        // 6-Test additional properties
        public decimal? T5 { get; set; }
        public decimal? T6 { get; set; }

        // Calculated fields
        public decimal? Percent40 { get; set; }
        public decimal? Total { get; set; }
        public string Grade { get; set; }
        public string Remarks { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }

        // Helper properties
        public decimal? Term1Mean => (T1 + T2) / 2;
        public decimal? Term2Mean => (T3 + T4) / 2;
        public decimal? Term3Mean => (T5 + T6) / 2;
        public decimal? AnnualMean => (T1 + T2 + T3 + T4 + T5 + T6) / 6;
        public decimal? FinalScore => Total ?? (Percent40.HasValue ? Percent40 + Exam : Exam);

        public bool IsFiveTests => T5 == null && T6 == null;
        public bool IsSixTests => T5.HasValue || T6.HasValue;
    }
}