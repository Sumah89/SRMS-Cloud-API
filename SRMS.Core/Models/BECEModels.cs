using System;
using System.Collections.Generic;

namespace SRMS.Desktop.Models
{
    #region BECE School Models

    public class BECESchool
    {
        public int Id { get; set; }
        public string SchoolName { get; set; }
        public string SchoolLocation { get; set; }
        public string SchoolMotto { get; set; }
        public string WAECCode { get; set; }
        public string CentreNumber { get; set; }
        public string AcademicYear { get; set; }
        public string SchoolCrestPath { get; set; }
        public string PrincipalName { get; set; }
        public string CARTeacherName { get; set; }
        public string SchoolContact { get; set; }
        public DateTime RegistrationDate { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? DeletedDate { get; set; }

        public string DisplayName => $"{SchoolName} ({AcademicYear})";
        public string FullAddress => $"{SchoolLocation} | {SchoolContact}";
    }

    #endregion

    #region BECE Student Models

    public class BECEStudent
    {
        public int Id { get; set; }
        public int BECESchoolId { get; set; }
        public string StudentName { get; set; }
        public string Gender { get; set; }
        public string StudentSerialNumber { get; set; }
        public string IndexNumber { get; set; }
        public string StudentPhotoPath { get; set; }
        public DateTime RegistrationDate { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }

        public string DisplayName => $"{StudentName} ({IndexNumber})";
        public string GenderDisplay => Gender == "Female" ? "F" : "M";
    }

    #endregion

    #region BECE Subject Score Models

    public class BECESubjectScore
    {
        public int Id { get; set; }
        public int BECEStudentId { get; set; }
        public string SubjectCategory { get; set; }
        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }
        public int Score { get; set; }
        public string GradeWord { get; set; }
        public string Interpretation { get; set; }
        public DateTime EntryDate { get; set; }
    }

    #endregion

    #region Promoted JSS III Student

    public class PromotedStudent
    {
        public int StudentId { get; set; }
        public int SchoolId { get; set; }
        public int ClassId { get; set; }

        public string AdmissionNumber { get; set; }
        public string FullName { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string Gender { get; set; }
        public string ClassName { get; set; }
        public string Stream { get; set; }
        public string PhotoPath { get; set; }
        public string ParentName { get; set; }
        public string ParentPhone { get; set; }
        public string Address { get; set; }

        public string WaecCode { get; set; }
        public string SerialNumber { get; set; }
        public string IndexNumber { get; set; }

        public string ExamMonth { get; set; }
        public string ExamYear { get; set; }
        public string CARTeacherName { get; set; }
        public string PrincipalName { get; set; }

        public List<BECESubjectScoreInput> CoreSubjects { get; set; } = new List<BECESubjectScoreInput>();
        public List<BECESubjectScoreInput> OptionalSubjects { get; set; } = new List<BECESubjectScoreInput>();
        public List<BECESubjectScoreInput> SelectiveSubjects { get; set; } = new List<BECESubjectScoreInput>();

        public int ExamYearValue
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ExamYear) &&
                    int.TryParse(ExamYear, out int y) && y > 1900 && y < 2200)
                    return y;
                return DateTime.Now.Year;
            }
        }

        public string DisplayName => $"{FullName} ({AdmissionNumber})";
    }

    #endregion

    #region BECE Candidate Record (persisted per year)

    public class BECECandidateRecord
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public int SchoolId { get; set; }
        public string AcademicYear { get; set; } = "";

        public string WaecCode { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string IndexNumber { get; set; } = "";

        public string ExamMonth { get; set; } = "";
        public int ExamYear { get; set; }

        public string CARTeacherName { get; set; } = "";
        public string PrincipalName { get; set; } = "";
        public string ParentName { get; set; } = "";
        public string PhotoPath { get; set; } = "";

        public string CoreSubjects { get; set; } = "";
        public string OptionalSubjects { get; set; } = "";
        public string SelectiveSubjects { get; set; } = "";

        public bool IsRemoved { get; set; }
    }

    #endregion

    #region BECE Certificate Input Model — FULL

    public class BECECertificateInput
    {
        public int BECESchoolId { get; set; }
        public string SchoolName { get; set; }
        public string SchoolLocation { get; set; }
        public string SchoolContact { get; set; }
        public string SchoolMotto { get; set; }
        public string WAECCode { get; set; }
        public string CentreNumber { get; set; }
        public string AcademicYear { get; set; }
        public string SchoolCrestPath { get; set; }
        public string PrincipalName { get; set; }
        public string CARTeacherName { get; set; }

        public int BECEStudentId { get; set; }
        public string StudentName { get; set; }
        public string Gender { get; set; }
        public string DateOfBirth { get; set; }
        public string StudentSerialNumber { get; set; }
        public string CandidateNumber { get; set; }
        public string StudentPhotoPath { get; set; }
        public string ParentName { get; set; }

        public string ExamMonth { get; set; }
        public string ExamYear { get; set; }
        public string CertificateNumber { get; set; }

        public List<BECESubjectScoreInput> CoreSubjects { get; set; } = new List<BECESubjectScoreInput>();
        public List<BECESubjectScoreInput> OptionalSubjects { get; set; } = new List<BECESubjectScoreInput>();
        public List<BECESubjectScoreInput> SelectiveSubjects { get; set; } = new List<BECESubjectScoreInput>();

        public string FullIndexNumber => $"{WAECCode}{StudentSerialNumber}";
        public string DisplayIndexNumber => $"{WAECCode} {StudentSerialNumber}";
        public string GenderPronoun => Gender?.ToLower() == "male" ? "his" : "her";
    }

    #endregion

    #region BECE Subject Score Input

    public class BECESubjectScoreInput
    {
        public string SubjectName { get; set; }
        public string SubjectCode { get; set; }
        public int Score { get; set; }

        public string GradeWord => GetGradeWord(Score);
        public string Interpretation => GetInterpretation(Score);

        private static string GetGradeWord(int score) => score switch
        {
            1 => "ONE",
            2 => "TWO",
            3 => "THREE",
            4 => "FOUR",
            5 => "FIVE",
            6 => "SIX",
            7 => "SEVEN",
            8 => "EIGHT",
            9 => "NINE",
            10 => "TEN",
            _ => "---"
        };

        private static string GetInterpretation(int score) => score switch
        {
            1 => "EXCELLENT",
            2 => "VERY GOOD",
            3 => "GOOD",
            4 => "CREDIT",
            5 => "CREDIT",
            6 => "PASS",
            7 => "PASS",
            8 => "WEAK PASS",
            9 => "FAIL",
            10 => "FAIL",
            _ => "---"
        };
    }

    #endregion

    #region BECE Result Summary

    public class BECEResultSummary
    {
        public int Aggregate { get; set; }
        public int CoreSubjectsPassed { get; set; }
        public int OptionalSubjectsPassed { get; set; }
        public int SelectiveSubjectsPassed { get; set; }
        public int TotalSubjectsPassed { get; set; }
        public int Credits { get; set; }
        public int Passes { get; set; }
        public int TotalSubjects { get; set; }
        public string OverallPerformance { get; set; }

        public string AggregateGrade => Aggregate <= 6 ? "A" : Aggregate <= 12 ? "B" : Aggregate <= 18 ? "C" : Aggregate <= 24 ? "D" : Aggregate <= 30 ? "E" : "F";
        public string AggregateDescription => Aggregate <= 6 ? "Distinction" : Aggregate <= 12 ? "Merit" : Aggregate <= 18 ? "Credit" : Aggregate <= 24 ? "Pass" : Aggregate <= 30 ? "Pass" : "Fail";
        public string SummaryText => $"Aggregate: {Aggregate} | Credits: {Credits} | Passes: {Passes} | Performance: {OverallPerformance}";
    }

    #endregion

    #region BECE Result (Database)

    public class BECEResult
    {
        public int Id { get; set; }
        public int BECEStudentId { get; set; }
        public int Aggregate { get; set; }
        public int CoreSubjectsPassed { get; set; }
        public int OptionalSubjectsPassed { get; set; }
        public int TotalSubjectsPassed { get; set; }
        public int Credits { get; set; }
        public int Passes { get; set; }
        public string OverallPerformance { get; set; }
        public string CertificatePath { get; set; }
        public DateTime GeneratedDate { get; set; }
    }

    #endregion

    #region BECE Generation Result

    public class BECEGenerationResult
    {
        public int StudentId { get; set; }
        public string StudentName { get; set; }
        public string AdmissionNumber { get; set; }
        public string IndexNumber { get; set; }
        public string PdfPath { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int Aggregate { get; set; }
        public int TotalPassed { get; set; }
    }

    #endregion

    #region BECE Certificate View Model

    public class BECECertificateViewModel
    {
        public int Id { get; set; }
        public int BECEStudentId { get; set; }
        public string StudentName { get; set; }
        public string IndexNumber { get; set; }
        public int Aggregate { get; set; }
        public string OverallPerformance { get; set; }
        public string CertificatePath { get; set; }
        public DateTime GeneratedDate { get; set; }
        public bool HasCertificate => !string.IsNullOrEmpty(CertificatePath);
        public string CertificateFileName => HasCertificate ? System.IO.Path.GetFileName(CertificatePath) : "Not Generated";
    }

    #endregion

    #region BECE School Statistics

    public class BECESchoolStats
    {
        public int TotalStudents { get; set; }
        public int ScoresComplete { get; set; }
        public int CertificatesGenerated { get; set; }
        public decimal AverageAggregate { get; set; }
        public int BestAggregate { get; set; }
        public int WorstAggregate { get; set; }
        public int StudentsWithDistinction { get; set; }
        public int StudentsWithMerit { get; set; }
        public int StudentsWithCredit { get; set; }
        public int StudentsWithPass { get; set; }
        public int StudentsWithFail { get; set; }

        public double PassRate
        {
            get
            {
                if (TotalStudents == 0) return 0;
                return ((double)(StudentsWithDistinction + StudentsWithMerit + StudentsWithCredit + StudentsWithPass) / TotalStudents) * 100;
            }
        }
    }

    #endregion
}