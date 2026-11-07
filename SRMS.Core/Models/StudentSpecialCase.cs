using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SRMS.Desktop.Models
{
    public class StudentSpecialCase
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public string AcademicYear { get; set; }
        public string SelectedTests { get; set; }
        public string Term1Formula { get; set; }
        public string Term2Formula { get; set; }
        public string Term3Formula { get; set; }
        public string Percent60Formula { get; set; }
        public string AnnualMeanFormula { get; set; }
        public string FinalScoreFormula { get; set; }
        public bool IsActive { get; set; }
        public bool IsSSSI { get; set; }
        public string ResultType { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }

        /// <summary>
        /// Returns the list of test identifiers (T1..T6, Exam) that this student actually took.
        /// </summary>
        public List<string> GetSelectedTestsList()
        {
            if (string.IsNullOrEmpty(SelectedTests)) return new List<string>();
            try
            {
                return JsonConvert.DeserializeObject<List<string>>(SelectedTests) ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        public bool HasTest(string testName)
        {
            return GetSelectedTestsList().Contains(testName, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates a deep copy of this special-case object.
        /// </summary>
        public StudentSpecialCase Clone()
        {
            return new StudentSpecialCase
            {
                Id = Id,
                StudentId = StudentId,
                AcademicYear = AcademicYear,
                SelectedTests = SelectedTests,
                Term1Formula = Term1Formula,
                Term2Formula = Term2Formula,
                Term3Formula = Term3Formula,
                Percent60Formula = Percent60Formula,
                AnnualMeanFormula = AnnualMeanFormula,
                FinalScoreFormula = FinalScoreFormula,
                IsActive = IsActive,
                IsSSSI = IsSSSI,
                ResultType = ResultType,
                CreatedDate = CreatedDate,
                ModifiedDate = ModifiedDate
            };
        }
    }
}