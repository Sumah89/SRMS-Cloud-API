using System.Collections.Generic;
using System.Threading.Tasks;
using SRMS.Desktop.Models;

namespace SRMS.Desktop.Services
{
    public interface IReportDataService
    {
        Task<ReportCardData> GetReportCardDataAsync(int studentId, int term, string academicYear);
        Task<List<SubjectScoreData>> GetSubjectScoresAsync(int studentId, int term, string academicYear);
        Task<BehaviourScoreData> GetBehaviourScoresAsync(int studentId, int term, string academicYear);
        Task<PsychomotorScoreData> GetPsychomotorScoresAsync(int studentId, int term, string academicYear);
        Task<AttendanceData> GetAttendanceAsync(int studentId, int term, string academicYear);
        Task<SchoolReportConfig> GetSchoolConfigAsync(int schoolId);
        Task<SchoolPromotionCriteria> GetPromotionCriteriaAsync(int schoolId, int? classId = null);
        Task<List<ReportSubjectRowMapping>> GetSubjectRowMappingsAsync(int schoolId, int? classId = null);
        Task<bool> SavePromotionCriteriaAsync(SchoolPromotionCriteria criteria);
        Task<bool> SaveSchoolConfigAsync(SchoolReportConfig config);
    }
}