using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SRMS.Desktop.Abstractions;
using SRMS.Desktop.Data;
using SRMS.Desktop.Helpers;
using SRMS.Desktop.Models;
using Syncfusion.XlsIO;

namespace SRMS.Desktop.Services
{
    /// <summary>
    /// Cross-platform base class for all report generators.
    ///
    /// Differences from the desktop version:
    ///   - No System.Drawing. Student photos are inserted via the injected
    ///     IImageProcessor, which the platform provides.
    ///   - No hardcoded AppDomain.BaseDirectory photo lookup. Uses
    ///     Global.Paths.StudentPhotosFolder instead.
    ///   - Template loading uses the executing assembly of SRMS.Core, which
    ///     is where the embedded .xlsx files live.
    /// </summary>
    public abstract class BaseReportGenerator : IReportGenerator
    {
        protected readonly DatabaseHelper _dbHelper;
        protected readonly ReportDataService _dataService;
        protected readonly SpecialCaseService _specialCaseService;
        protected readonly ILogger _logger;
        protected readonly IImageProcessor _images;

        private bool _disposed = false;

        protected abstract string TemplateResourcePath { get; }
        protected abstract string TemplateName { get; }

        /// <summary>
        /// The platform injects IImageProcessor via the derived generator's
        /// constructor, which passes it here.
        /// </summary>
        protected BaseReportGenerator(IImageProcessor imageProcessor)
        {
            _images = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));
            _dbHelper = new DatabaseHelper();
            _dataService = new ReportDataService();
            _specialCaseService = new SpecialCaseService();
            _logger = Logger.Instance;
        }

        protected byte[] LoadTemplateFromResources()
        {
            try
            {
                // Templates are embedded in SRMS.Core, not in the executing app.
                var assembly = typeof(BaseReportGenerator).Assembly;
                using (Stream resourceStream = assembly.GetManifestResourceStream(TemplateResourcePath))
                {
                    if (resourceStream == null)
                        throw new FileNotFoundException(
                            $"Template not found: {TemplateResourcePath}. " +
                            $"Check the <EmbeddedResource> entries in SRMS.Core.csproj.");

                    using (var ms = new MemoryStream())
                    {
                        resourceStream.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading template: {ex.Message}");
                throw;
            }
        }

        public virtual bool ValidateTemplate(int schoolId)
        {
            try
            {
                var templateData = LoadTemplateFromResources();
                return templateData != null && templateData.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public string GetTemplatePath() => "Embedded Resource";

        protected void SetWorksheetPageOrientation(IWorksheet worksheet)
        {
            try
            {
                worksheet.PageSetup.Orientation = ExcelPageOrientation.Landscape;
                worksheet.PageSetup.PaperSize = ExcelPaperSize.PaperA4;
                worksheet.PageSetup.FitToPagesWide = 1;
                worksheet.PageSetup.FitToPagesTall = 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting page orientation");
            }
        }

        protected string ConvertToRomanClass(string className) => className ?? "";

        protected string GetNextClassRoman(string currentClass)
        {
            if (string.IsNullOrEmpty(currentClass)) return currentClass;
            return ClassHelper.GetNextClass(currentClass);
        }

        protected string GetClassTeacherComment(decimal annualAverage)
        {
            if (annualAverage >= 80) return "Excellent performance! Keep up the good work!";
            if (annualAverage >= 70) return "Very good performance! Continue striving for excellence!";
            if (annualAverage >= 60) return "Good performance! More effort will yield better results.";
            if (annualAverage >= 50) return "Satisfactory performance. Work harder next term.";
            return "Needs improvement. Please put in more effort.";
        }

        protected string GetPrincipalComment(bool isPromoted, decimal annualAverage)
        {
            if (isPromoted)
            {
                if (annualAverage >= 80) return "PROMOTED WITH DISTINCTION! EXCELLENT!";
                if (annualAverage >= 70) return "PROMOTED WITH MERIT! VERY GOOD!";
                if (annualAverage >= 60) return "PROMOTED! GOOD EFFORT!";
                return "PROMOTED! CONGRATULATIONS!";
            }
            return "NOT PROMOTED. MORE EFFORT NEEDED.";
        }

        protected string GetClassTeacherName(int classId)
        {
            try
            {
                var classObj = _dbHelper.GetClassById(classId);
                return classObj?.ClassTeacherName ?? "Class Teacher";
            }
            catch { return "Class Teacher"; }
        }

        protected string GetOrdinalSuffix(int number)
        {
            if (number <= 0) return "";
            if (number % 100 >= 11 && number % 100 <= 13) return "th";
            switch (number % 10)
            {
                case 1: return "st";
                case 2: return "nd";
                case 3: return "rd";
                default: return "th";
            }
        }

        protected async Task<int> CalculateStudentRank(int studentId, List<Student> students, int term, string academicYear)
        {
            if (students == null || students.Count == 0) return 0;
            var studentAverages = new Dictionary<int, decimal>();

            foreach (var student in students)
            {
                try
                {
                    var rd = await _dataService.GetReportCardDataAsync(student.Id, 3, academicYear);
                    studentAverages[student.Id] = rd?.AnnualAverage ?? 0m;
                }
                catch
                {
                    studentAverages[student.Id] = 0m;
                }
            }

            if (!studentAverages.ContainsKey(studentId)) return 0;

            var studentAvg = studentAverages[studentId];
            int rank = studentAverages.Values.Count(v => v > studentAvg) + 1;
            return rank;
        }

        protected string GetColumnLetter(int col)
        {
            string result = "";
            while (col > 0)
            {
                col--;
                result = (char)('A' + col % 26) + result;
                col /= 26;
            }
            return result;
        }

        /// <summary>
        /// Platform-neutral handle to a temporarily-loaded image. We keep
        /// the underlying IPlatformImage and its source stream so we can
        /// dispose both when the report finishes.
        /// </summary>
        protected class TempImage
        {
            public IPlatformImage Image { get; set; }
            public Stream Stream { get; set; }
        }

        /// <summary>
        /// Loads the student's photo (from the model or from the platform
        /// photos folder) and inserts it into the worksheet via IImageProcessor.
        ///
        /// Returns null when no photo can be found — the caller should
        /// tolerate that and continue.
        /// </summary>
        protected TempImage InsertStudentPhoto(
            IWorksheet worksheet,
            ReportCardData data,
            int startRow,
            int startCol,
            int endRow,
            int endCol)
        {
            // ── 1. Resolve a photo path ──
            string resolvedPath = TryResolvePhotoPath(data);
            if (string.IsNullOrEmpty(resolvedPath))
                return null;

            // ── 2. Load bytes ──
            byte[] imgBytes;
            try
            {
                imgBytes = File.ReadAllBytes(resolvedPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error reading photo at {resolvedPath}");
                return null;
            }

            // ── 3. Compute target pixel size from the worksheet geometry ──
            int totalWidthPx = 0;
            int totalHeightPx = 0;
            try
            {
                for (int c = startCol; c <= endCol; c++)
                {
                    double colWidth = worksheet.Columns[c].ColumnWidth;
                    int px = (int)Math.Round(colWidth * 7.0 + 5.0);
                    totalWidthPx += px;
                }
                for (int r = startRow; r <= endRow; r++)
                {
                    double rowHeight = worksheet.Rows[r].RowHeight;
                    if (rowHeight <= 0) rowHeight = 15.0;
                    int px = (int)Math.Round(rowHeight * 96.0 / 72.0);
                    totalHeightPx += px;
                }
            }
            catch
            {
                totalWidthPx = (endCol - startCol + 1) * 60;
                totalHeightPx = (endRow - startRow + 1) * 30;
            }

            // Clamp to a minimum (3.2 cm × 3.6 cm at 96 DPI).
            int desiredWidthPx = (int)Math.Round(3.20 / 2.54 * 96.0);
            int desiredHeightPx = (int)Math.Round(3.60 / 2.54 * 96.0);
            if (totalWidthPx < desiredWidthPx) totalWidthPx = desiredWidthPx;
            if (totalHeightPx < desiredHeightPx) totalHeightPx = desiredHeightPx;

            // ── 4. Load + resize + insert via IImageProcessor ──
            try
            {
                var sourceStream = new MemoryStream(imgBytes);
                IPlatformImage sourceImage = _images.LoadFromStream(sourceStream);
                IPlatformImage resizedImage = _images.Resize(sourceImage, totalWidthPx, totalHeightPx);

                // The Syncfusion MAUI API accepts a stream for AddPicture.
                Stream imageStream = _images.ToStream(resizedImage, ImageFormat.Png);

                worksheet.Pictures.AddPicture(startRow, startCol, imageStream, totalWidthPx, totalHeightPx);

                // Keep a reference so the caller can dispose everything.
                return new TempImage
                {
                    Image = resizedImage,
                    Stream = sourceStream
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error inserting photo for {data.StudentName}");
                return null;
            }
        }

        /// <summary>
        /// Tries to resolve a photo path in this order:
        ///   1. data.StudentPhotoPath (set by the data service)
        ///   2. Global.Paths.StudentPhotosFolder/{classFolder}/student_{id}.jpg
        ///   3. Global.Paths.StudentPhotosFolder/student_{id}.jpg
        /// Returns null if nothing is found.
        /// </summary>
        private string TryResolvePhotoPath(ReportCardData data)
        {
            if (!string.IsNullOrEmpty(data.StudentPhotoPath) && File.Exists(data.StudentPhotoPath))
                return data.StudentPhotoPath;

            try
            {
                string photosRoot = null;
                try { photosRoot = Global.Paths?.StudentPhotosFolder; } catch { }
                if (string.IsNullOrEmpty(photosRoot) || !Directory.Exists(photosRoot))
                    return null;

                string studentIdFile = $"student_{data.StudentId}.jpg";

                // Try class-specific folder first.
                try
                {
                    var student = _dbHelper.GetStudentById(data.StudentId);
                    if (student != null)
                    {
                        var classObj = _dbHelper.GetClassById(student.ClassId);
                        string className = classObj?.ClassName?
                            .Replace("/", "_")
                            .Replace("\\", "_")
                            .Trim();
                        if (!string.IsNullOrEmpty(className))
                        {
                            string classFolder = Path.Combine(photosRoot, className);
                            string p = Path.Combine(classFolder, studentIdFile);
                            if (File.Exists(p))
                            {
                                data.StudentPhotoPath = p;
                                return p;
                            }
                        }
                    }
                }
                catch { }

                // Fall back to root of photos folder.
                string rootPath = Path.Combine(photosRoot, studentIdFile);
                if (File.Exists(rootPath))
                {
                    data.StudentPhotoPath = rootPath;
                    return rootPath;
                }
            }
            catch { }

            return null;
        }

        // Abstract methods to be implemented by derived classes.
        protected abstract void PopulateStudentInfo(IWorksheet worksheet, ReportCardData data, int rank, int classSize);
        protected abstract void PopulateSubjectScores(IWorksheet worksheet, ReportCardData data, List<Subject> activeSubjects, int startRow);
        protected abstract void PopulateBehaviourScores(IWorksheet worksheet, ReportCardData data);
        protected abstract void PopulatePsychomotorScores(IWorksheet worksheet, ReportCardData data);
        protected abstract void PopulateAttendance(IWorksheet worksheet, ReportCardData data, int studentId, string academicYear);
        protected abstract void PopulatePromotionInfo(IWorksheet worksheet, ReportCardData data, string classTeacherName, string displayClass);
        protected abstract void PopulateSubjectAnalysis(IWorksheet worksheet, ReportCardData data);

        public abstract Task<byte[]> GenerateReportCardAsync(int studentId, int term, string academicYear, int classId = 0, List<Student> allStudentsInClass = null);
        public abstract Task<byte[]> GenerateSelectedReportCardsAsync(List<int> studentIds, int term, string academicYear, IProgress<int> progress = null, IProgress<string> statusProgress = null);
        public abstract Task<bool> SaveReportCardToFileAsync(int studentId, int term, string academicYear, string filePath);
        public abstract bool SaveReportCardToFile(int studentId, int term, string academicYear, string filePath);

        public virtual void Dispose()
        {
            if (!_disposed)
            {
                _dbHelper?.Dispose();
                _disposed = true;
            }
        }
    }
}