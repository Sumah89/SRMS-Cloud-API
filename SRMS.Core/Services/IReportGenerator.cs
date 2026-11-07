using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SRMS.Desktop.Models;

namespace SRMS.Desktop.Services
{
    public interface IReportGenerator : IDisposable
    {
        Task<byte[]> GenerateReportCardAsync(int studentId, int term, string academicYear, int classId = 0, List<Student> allStudentsInClass = null);
        Task<byte[]> GenerateSelectedReportCardsAsync(List<int> studentIds, int term, string academicYear, IProgress<int> progress = null, IProgress<string> statusProgress = null);
        bool SaveReportCardToFile(int studentId, int term, string academicYear, string filePath);
        bool ValidateTemplate(int schoolId);
        string GetTemplatePath();
    }
}