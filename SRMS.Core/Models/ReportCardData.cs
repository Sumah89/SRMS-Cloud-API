using System;
using System.Collections.Generic;
using System.Linq;
using SRMS.Desktop.Services;

namespace SRMS.Desktop.Models
{
    public class ReportCardData
    {
        // Student Information
        public int StudentId { get; set; }
        public string StudentName { get; set; }
        public string AdmissionNumber { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string Gender { get; set; }
        public int ClassId { get; set; }
        public string CurrentClass { get; set; }
        public string AcademicYear { get; set; }

        // Student photo path
        public string StudentPhotoPath { get; set; }

        // School Information
        public int SchoolId { get; set; }
        public string SchoolName { get; set; }
        public string SchoolDistrict { get; set; }
        public string SchoolTown { get; set; }
        public string SchoolPhone { get; set; }
        public string SchoolEmail { get; set; }
        public string PrincipalName { get; set; }

        // Scores
        public List<SubjectScoreData> SubjectScores { get; set; } = new List<SubjectScoreData>();
        public BehaviourScoreData BehaviourScores { get; set; }
        public PsychomotorScoreData PsychomotorScores { get; set; }
        public AttendanceData Attendance { get; set; }

        // Term Summaries
        public decimal Term1Total { get; set; }
        public decimal Term1Average { get; set; }
        public decimal Term2Total { get; set; }
        public decimal Term2Average { get; set; }
        public decimal Term3Total { get; set; }
        public decimal Term3Average { get; set; }
        public decimal AnnualAverage { get; set; }

        // Subject Pass Counts
        public int CoreSubjectsPassed { get; set; }
        public int OptionalSubjectsPassed { get; set; }
        public int TotalSubjectsPassed { get; set; }
        public int TotalSubjectsCount { get; set; }

        // ── English / Maths requirement ──────────────────────────────────
        // Evaluated during report assembly (ReportDataService) so the promotion
        // engine doesn't need to re-derive from subject names. Both are set on
        // the object that flows from ReportDataService → PromotionEngine.
        public bool EnglishMathsPassed { get; set; }     // ← NEW
        public bool EnglishMathsRequired { get; set; }   // ← NEW

        // Promotion - Using Services.PromotionResult
        public PromotionResult PromotionResult { get; set; }

        // Comments & Signatures
        public string FormMasterComment { get; set; }
        public string FormMasterSignature { get; set; }

        // Helper Properties
        public bool HasBehaviourData
        {
            get { return BehaviourScores != null; }
        }

        public bool HasPsychomotorData
        {
            get { return PsychomotorScores != null; }
        }

        public bool HasAttendanceData
        {
            get { return Attendance != null && Attendance.Id > 0; }
        }

        public bool HasSubjectData
        {
            get { return SubjectScores != null && SubjectScores.Count > 0; }
        }

        public string OverallGrade
        {
            get { return GetGrade(AnnualAverage); }
        }

        public string OverallRemark
        {
            get { return GetRemark(AnnualAverage); }
        }

        // Helper Methods
        public decimal GetTermAverage(int term)
        {
            if (term == 1)
            {
                return Term1Average;
            }
            else if (term == 2)
            {
                return Term2Average;
            }
            else if (term == 3)
            {
                return Term3Average;
            }
            else
            {
                return 0;
            }
        }

        public decimal GetTermTotal(int term)
        {
            if (term == 1)
            {
                return Term1Total;
            }
            else if (term == 2)
            {
                return Term2Total;
            }
            else if (term == 3)
            {
                return Term3Total;
            }
            else
            {
                return 0;
            }
        }

        public void CalculateSummaries()
        {
            if (!HasSubjectData)
            {
                return;
            }

            Term1Total = 0;
            Term2Total = 0;
            Term3Total = 0;
            decimal totalFinalScore = 0;

            CoreSubjectsPassed = 0;
            OptionalSubjectsPassed = 0;
            TotalSubjectsPassed = 0;

            foreach (var subject in SubjectScores)
            {
                Term1Total += subject.Term1Mean;
                Term2Total += subject.Term2Mean;
                Term3Total += subject.FinalScore;
                totalFinalScore += subject.FinalScore;

                if (subject.IsPassed)
                {
                    TotalSubjectsPassed++;
                    if (subject.IsCore)
                    {
                        CoreSubjectsPassed++;
                    }
                    else
                    {
                        OptionalSubjectsPassed++;
                    }
                }
            }

            int subjectCount = SubjectScores.Count;
            if (subjectCount > 0)
            {
                Term1Average = Term1Total / subjectCount;
                Term2Average = Term2Total / subjectCount;
                Term3Average = Term3Total / subjectCount;
                AnnualAverage = totalFinalScore / subjectCount;
            }
            else
            {
                Term1Average = 0;
                Term2Average = 0;
                Term3Average = 0;
                AnnualAverage = 0;
            }

            TotalSubjectsCount = subjectCount;
        }

        private string GetGrade(decimal score)
        {
            if (score >= 80) return "A";
            if (score >= 70) return "B";
            if (score >= 60) return "C";
            if (score >= 50) return "D";
            if (score >= 40) return "E";
            return "F";
        }

        private string GetRemark(decimal score)
        {
            if (score >= 80) return "Excellent";
            if (score >= 70) return "Very Good";
            if (score >= 60) return "Good";
            if (score >= 50) return "Satisfactory";
            if (score >= 40) return "Fair";
            return "Needs Improvement";
        }
    }
}