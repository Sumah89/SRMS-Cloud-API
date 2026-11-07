using System;

namespace SRMS.Desktop.Models
{
    public class AttendanceData
    {
        public int Id { get; set; }
        public int StudentId { get; set; }
        public int Term { get; set; }
        public string AcademicYear { get; set; }
        public int DaysPresent { get; set; }
        public int DaysAbsent { get; set; }
        public int DaysLate { get; set; }  // NEW: Separate Late column
        public int SchoolDays { get; set; } = 180;
        public decimal? Percentage { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }

        // Term breakdowns
        public int Term1DaysPresent { get; set; }
        public int Term1DaysAbsent { get; set; }
        public int Term1DaysLate { get; set; }
        public int Term1SchoolDays { get; set; } = 180;
        public decimal Term1Percentage { get; set; }

        public int Term2DaysPresent { get; set; }
        public int Term2DaysAbsent { get; set; }
        public int Term2DaysLate { get; set; }
        public int Term2SchoolDays { get; set; } = 180;
        public decimal Term2Percentage { get; set; }

        public int Term3DaysPresent { get; set; }
        public int Term3DaysAbsent { get; set; }
        public int Term3DaysLate { get; set; }
        public int Term3SchoolDays { get; set; } = 180;
        public decimal Term3Percentage { get; set; }

        public decimal AnnualPercentage { get; set; }
        public string AttendanceStatus { get; set; }

        public AttendanceData()
        {
        }

        public AttendanceData(int studentId, int term, string academicYear)
        {
            StudentId = studentId;
            Term = term;
            AcademicYear = academicYear;
            SchoolDays = 180;
        }

        public void SetTermData(int term, int daysPresent, int daysAbsent, int daysLate = 0, int schoolDays = 180)
        {
            if (term == 1)
            {
                Term1DaysPresent = daysPresent;
                Term1DaysAbsent = daysAbsent;
                Term1DaysLate = daysLate;
                Term1SchoolDays = schoolDays;
            }
            else if (term == 2)
            {
                Term2DaysPresent = daysPresent;
                Term2DaysAbsent = daysAbsent;
                Term2DaysLate = daysLate;
                Term2SchoolDays = schoolDays;
            }
            else if (term == 3)
            {
                Term3DaysPresent = daysPresent;
                Term3DaysAbsent = daysAbsent;
                Term3DaysLate = daysLate;
                Term3SchoolDays = schoolDays;
            }
        }

        public void CalculatePercentages()
        {
            if (Term1SchoolDays > 0)
                Term1Percentage = (decimal)Term1DaysPresent / Term1SchoolDays * 100;
            if (Term2SchoolDays > 0)
                Term2Percentage = (decimal)Term2DaysPresent / Term2SchoolDays * 100;
            if (Term3SchoolDays > 0)
                Term3Percentage = (decimal)Term3DaysPresent / Term3SchoolDays * 100;

            AnnualPercentage = (Term1Percentage + Term2Percentage + Term3Percentage) / 3;
            AttendanceStatus = GetAttendanceStatus(AnnualPercentage);

            if (Term == 1)
            {
                Percentage = Term1Percentage;
                DaysPresent = Term1DaysPresent;
                DaysAbsent = Term1DaysAbsent;
                DaysLate = Term1DaysLate;
                SchoolDays = Term1SchoolDays;
            }
            else if (Term == 2)
            {
                Percentage = Term2Percentage;
                DaysPresent = Term2DaysPresent;
                DaysAbsent = Term2DaysAbsent;
                DaysLate = Term2DaysLate;
                SchoolDays = Term2SchoolDays;
            }
            else if (Term == 3)
            {
                Percentage = Term3Percentage;
                DaysPresent = Term3DaysPresent;
                DaysAbsent = Term3DaysAbsent;
                DaysLate = Term3DaysLate;
                SchoolDays = Term3SchoolDays;
            }
        }

        private string GetAttendanceStatus(decimal percentage)
        {
            if (percentage >= 90) return "Excellent";
            if (percentage >= 75) return "Good";
            if (percentage >= 60) return "Fair";
            if (percentage >= 50) return "Poor";
            return "Very Poor";
        }
    }
}