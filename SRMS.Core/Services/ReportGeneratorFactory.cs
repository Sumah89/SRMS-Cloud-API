using System;
using SRMS.Desktop.Abstractions;
using SRMS.Desktop.Data;
using SRMS.Desktop.Helpers;

namespace SRMS.Desktop.Services
{
    /// <summary>
    /// Factory for creating report generators based on the school's result
    /// type system. Requires an IImageProcessor so it can pass it to each
    /// generator's constructor.
    /// </summary>
    public static class ReportGeneratorFactory
    {
        private static IReportGenerator _fiveTestsGenerator;
        private static IReportGenerator _sixTestsGenerator;
        private static IReportGenerator _sixTests15Generator;
        private static IReportGenerator _sss1Generator;
        private static readonly object _lockObject = new object();
        private static IImageProcessor _images;

        /// <summary>
        /// The platform provides the image processor once at startup.
        /// Must be set before calling any GetGenerator method.
        /// </summary>
        public static void Initialize(IImageProcessor imageProcessor)
        {
            _images = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));
        }

        public static IReportGenerator GetGenerator(string resultType)
        {
            switch (resultType)
            {
                case "SixTests":
                    return GetSixTestsGenerator();
                case "SixTests15":
                    return GetSixTests15Generator();
                case "FiveTests":
                default:
                    return GetFiveTestsGenerator();
            }
        }

        public static IReportGenerator GetGeneratorBySchoolId(int schoolId)
        {
            try
            {
                using (var db = new DatabaseHelper())
                {
                    var school = db.GetSchoolById(schoolId);
                    string resultType = Normalize(school?.ResultType);
                    return GetGenerator(resultType);
                }
            }
            catch
            {
                return GetFiveTestsGenerator();
            }
        }

        public static IReportGenerator GetGeneratorByClass(int schoolId, int classId)
        {
            try
            {
                using (var db = new DatabaseHelper())
                {
                    var classObj = db.GetClassById(classId);
                    string className = classObj?.ClassName ?? "";

                    if (ClassHelper.IsSSSIClass(className))
                        return GetSSS1Generator();

                    var school = db.GetSchoolById(schoolId);
                    string resultType = Normalize(classObj?.ResultTypeOverride);
                    if (string.Equals(resultType, "FiveTests", StringComparison.OrdinalIgnoreCase))
                        resultType = Normalize(school?.ResultType);
                    return GetGenerator(resultType);
                }
            }
            catch
            {
                return GetFiveTestsGenerator();
            }
        }

        public static IReportGenerator GetGeneratorByClassName(string className)
        {
            if (ClassHelper.IsSSSIClass(className))
                return GetSSS1Generator();
            return GetFiveTestsGenerator();
        }

        public static bool IsSSS1Class(string className)
            => ClassHelper.IsSSSIClass(className);

        private static IReportGenerator GetFiveTestsGenerator()
        {
            if (_fiveTestsGenerator == null)
            {
                lock (_lockObject)
                {
                    if (_fiveTestsGenerator == null)
                    {
                        EnsureImages();
                        _fiveTestsGenerator = new FiveTestsReportGenerator(_images);
                    }
                }
            }
            return _fiveTestsGenerator;
        }

        private static IReportGenerator GetSixTestsGenerator()
        {
            if (_sixTestsGenerator == null)
            {
                lock (_lockObject)
                {
                    if (_sixTestsGenerator == null)
                    {
                        EnsureImages();
                        _sixTestsGenerator = new SixTestsReportGenerator(_images);
                    }
                }
            }
            return _sixTestsGenerator;
        }

        private static IReportGenerator GetSixTests15Generator()
        {
            if (_sixTests15Generator == null)
            {
                lock (_lockObject)
                {
                    if (_sixTests15Generator == null)
                    {
                        EnsureImages();
                        _sixTests15Generator = new SixTests15SubjectsMaxReportGenerator(_images);
                    }
                }
            }
            return _sixTests15Generator;
        }

        private static IReportGenerator GetSSS1Generator()
        {
            if (_sss1Generator == null)
            {
                lock (_lockObject)
                {
                    if (_sss1Generator == null)
                    {
                        EnsureImages();
                        _sss1Generator = new SSSIReportGenerator(_images);
                    }
                }
            }
            return _sss1Generator;
        }

        public static void DisposeGenerators()
        {
            try { _fiveTestsGenerator?.Dispose(); } catch { } _fiveTestsGenerator = null;
            try { _sixTestsGenerator?.Dispose(); } catch { } _sixTestsGenerator = null;
            try { _sixTests15Generator?.Dispose(); } catch { } _sixTests15Generator = null;
            try { _sss1Generator?.Dispose(); } catch { } _sss1Generator = null;
        }

        private static string Normalize(string resultType)
        {
            if (string.Equals(resultType?.Trim(), "SixTests15", StringComparison.OrdinalIgnoreCase)) return "SixTests15";
            if (string.Equals(resultType?.Trim(), "SixTests", StringComparison.OrdinalIgnoreCase)) return "SixTests";
            return "FiveTests";
        }

        public static string GetSchoolResultType(int schoolId)
        {
            try
            {
                using (var db = new DatabaseHelper())
                {
                    var school = db.GetSchoolById(schoolId);
                    return school?.ResultType ?? "FiveTests";
                }
            }
            catch { return "FiveTests"; }
        }

        public static string EnsureConsistentResultType(int schoolId)
            => GetSchoolResultType(schoolId);

        private static void EnsureImages()
        {
            if (_images == null)
                throw new InvalidOperationException(
                    "ReportGeneratorFactory.Initialize(IImageProcessor) must be called at app startup before creating generators.");
        }
    }
}