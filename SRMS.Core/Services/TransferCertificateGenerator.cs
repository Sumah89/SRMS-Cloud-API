using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QRCoder;
using SRMS.Desktop.Abstractions;
using SRMS.Desktop.Data;
using SRMS.Desktop.Models;
using Syncfusion.Pdf;
using Syncfusion.XlsIO;
using Syncfusion.XlsIORenderer;
using ImageFormat = SRMS.Desktop.Abstractions.ImageFormat;

namespace SRMS.Desktop.Services
{
    public class TransferCertificateGenerator
    {
        private const string RESOURCE_PATH
            = "SRMS.Desktop.Resources.TransferCertificateTemplate.xlsx";

        private const string DateFormat = "dd/MM/yy";

        private readonly School _schoolInfo;
        private readonly DatabaseHelper _dbHelper;
        private readonly ILogger _logger;
        private readonly IImageProcessor _images;

        private class TempImage
        {
            public IPlatformImage Image { get; set; }
            public Stream Stream { get; set; }
        }

        public TransferCertificateGenerator(School schoolInfo, IImageProcessor images)
        {
            _schoolInfo = schoolInfo;
            _dbHelper = new DatabaseHelper();
            _logger = Logger.Instance;
            _images = images ?? throw new ArgumentNullException(nameof(images));
        }

        public bool GenerateCertificate(
            string filePath,
            string admissionNumber,
            string studentName,
            string sex,
            DateTime dateOfBirth,
            string classStudied,
            DateTime dateOfAdmission,
            DateTime dateOfLastAttendance,
            string reasonForLeaving,
            string conduct,
            string academicPerformance,
            string feeOwed,
            string parentName,
            string remarks,
            DateTime dateOfIssue,
            string classTeacherName = null)
        {
            try
            {
                byte[] pdfBytes = BuildCertificatePdf(
                    admissionNumber, studentName, sex, dateOfBirth, classStudied,
                    dateOfAdmission, dateOfLastAttendance, reasonForLeaving,
                    conduct, academicPerformance, feeOwed, parentName,
                    remarks, dateOfIssue, classTeacherName);

                if (pdfBytes == null || pdfBytes.Length == 0)
                {
                    _logger.LogError("TransferCertificateGenerator: PDF build returned no bytes.");
                    return false;
                }

                File.WriteAllBytes(filePath, pdfBytes);
                return true;
            }
            catch (Exception ex)
            {
                try { _logger.LogError(ex, "TransferCertificateGenerator.GenerateCertificate failed"); } catch { }
                System.Diagnostics.Debug.WriteLine("Transfer Certificate generation error: " + ex);
                return false;
            }
        }

        private byte[] BuildCertificatePdf(
            string admissionNumber, string studentName, string sex,
            DateTime dateOfBirth, string classStudied,
            DateTime dateOfAdmission, DateTime dateOfLastAttendance,
            string reasonForLeaving, string conduct, string academicPerformance,
            string feeOwed, string parentName, string remarks,
            DateTime dateOfIssue, string classTeacherName)
        {
            byte[] templateBytes = LoadTemplateFromResources();
            if (templateBytes == null || templateBytes.Length == 0)
                throw new FileNotFoundException("Transfer certificate template not found: " + RESOURCE_PATH);

            using (var engine = new ExcelEngine())
            {
                var app = engine.Excel;
                app.DefaultVersion = ExcelVersion.Excel2016;

                using (var ms = new MemoryStream(templateBytes))
                {
                    var wb = app.Workbooks.Open(ms);
                    var ws = wb.Worksheets[0];

                    try
                    {
                        ws.PageSetup.Orientation = ExcelPageOrientation.Portrait;
                        ws.PageSetup.PaperSize = ExcelPaperSize.PaperA4;
                        ws.PageSetup.FitToPagesWide = 1;
                        ws.PageSetup.FitToPagesTall = 0;
                        ws.PageSetup.LeftMargin = 0.2;
                        ws.PageSetup.RightMargin = 0.2;
                        ws.PageSetup.TopMargin = 0.2;
                        ws.PageSetup.BottomMargin = 0.2;
                    }
                    catch { }

                    ws.Range["C4"].Text = (_schoolInfo?.SchoolName ?? "SCHOOL NAME").ToUpperInvariant();
                    ws.Range["G7"].Text = BuildLocation();
                    ws.Range["I9"].Text = "MOTTO: \"May Our Light Shine\"";

                    ws.Range["AD11"].Text = "TC/" + dateOfIssue.ToString("yyyy") + "/" + (admissionNumber ?? "").Trim();
                    ws.Range["AE12"].Text = dateOfIssue.ToString(DateFormat);

                    ws.Range["Q16"].Text = studentName ?? "";
                    ws.Range["Q18"].Text = admissionNumber ?? "";
                    ws.Range["Q20"].Text = dateOfBirth.ToString(DateFormat);
                    ws.Range["Q22"].Text = sex ?? "";
                    ws.Range["Q24"].Text = classStudied ?? "";
                    ws.Range["Q26"].Text = dateOfAdmission.ToString(DateFormat);
                    ws.Range["Q28"].Text = dateOfLastAttendance.ToString(DateFormat);
                    ws.Range["Q30"].Text = reasonForLeaving ?? "";
                    ws.Range["Q32"].Text = conduct ?? "";
                    ws.Range["Q34"].Text = academicPerformance ?? "";
                    ws.Range["Q36"].Text = string.IsNullOrWhiteSpace(feeOwed) ? "None" : feeOwed;

                    ws.Range["K40"].Text = parentName ?? "";

                    ws.Range["F44"].Text = string.IsNullOrWhiteSpace(classTeacherName) ? "Class Teacher" : classTeacherName;
                    ws.Range["V44"].Text = !string.IsNullOrWhiteSpace(_schoolInfo?.PrincipalName)
                        ? _schoolInfo.PrincipalName : "Principal";

                    try { ws.Range["N48"].Text = "Academic Year: " + GetCurrentAcademicYear(); } catch { }

                    var tempImages = new List<TempImage>();

                    try { InsertSchoolLogo(ws, tempImages, 3, 2); } catch { }
                    try
                    {
                        InsertQrCode(ws, tempImages,
                            _schoolInfo?.SchoolName, studentName, admissionNumber,
                            dateOfIssue, dateOfBirth);
                    }
                    catch { }

                    try { ws.Calculate(); } catch { }
                    ws.EnableSheetCalculations();

                    var renderer = new XlsIORenderer();

                    using (var converted = renderer.ConvertToPDF(wb))
                    using (var outStream = new MemoryStream())
                    {
                        converted.Save(outStream);

                        foreach (var ti in tempImages)
                        {
                            try { ti?.Image?.Dispose(); } catch { }
                            try { ti?.Stream?.Dispose(); } catch { }
                        }
                        return outStream.ToArray();
                    }
                }
            }
        }

        private byte[] LoadTemplateFromResources()
        {
            var assembly = typeof(TransferCertificateGenerator).Assembly;
            using (Stream rs = assembly.GetManifestResourceStream(RESOURCE_PATH))
            {
                if (rs == null)
                {
                    _logger.LogError(
                        "TransferCertificateGenerator: template resource '" + RESOURCE_PATH + "' not found.");
                    throw new FileNotFoundException(
                        "Transfer certificate template not found: " + RESOURCE_PATH);
                }
                using (var ms = new MemoryStream())
                {
                    rs.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        private void InsertSchoolLogo(IWorksheet ws, List<TempImage> tempImages, int row, int col)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_schoolInfo?.LogoPath) || !File.Exists(_schoolInfo.LogoPath))
                    return;

                byte[] imgBytes = File.ReadAllBytes(_schoolInfo.LogoPath);
                int targetPx = (int)Math.Round(2.5 / 2.54 * 96.0);

                using (var ms = new MemoryStream(imgBytes))
                {
                    var src = _images.LoadFromStream(ms);
                    var resized = _images.Resize(src, targetPx, targetPx);
                    using (var outStream = _images.ToStream(resized, ImageFormat.Png))
                    {
                        ws.Pictures.AddPicture(row, col, outStream, targetPx, targetPx);
                    }
                    tempImages.Add(new TempImage { Image = resized, Stream = ms });
                }
            }
            catch { }
        }

        private void InsertQrCode(
            IWorksheet ws, List<TempImage> tempImages,
            string schoolName, string studentName, string admissionNumber,
            DateTime issuedOn, DateTime dateOfBirth)
        {
            string qrData =
                "School: " + (schoolName ?? "") + "\n" +
                "Student: " + (studentName ?? "") + "\n" +
                "Admission No: " + (admissionNumber ?? "") + "\n" +
                "Date of Birth: " + dateOfBirth.ToString(DateFormat) + "\n" +
                "Issued On: " + issuedOn.ToString(DateFormat) + "\n" +
                "Document: Transfer Certificate\n" +
                "SRMS — Developed by Alimamy Amara Sumah, Kambia, Sierra Leone";

            using (var qrGen = new QRCodeGenerator())
            {
                var qrCodeData = qrGen.CreateQrCode(qrData, QRCodeGenerator.ECCLevel.Q);
                using (var qrCode = new BitmapByteQRCode(qrCodeData))
                {
                    byte[] qrBytes = qrCode.GetGraphic(20);
                    using (var qrMs = new MemoryStream(qrBytes))
                    {
                        var src = _images.LoadFromStream(qrMs);
                        var resized = _images.Resize(src, 100, 100);
                        using (var outStream = _images.ToStream(resized, ImageFormat.Png))
                        {
                            ws.Pictures.AddPicture(8, 29, outStream, 100, 100);
                        }
                        tempImages.Add(new TempImage { Image = resized, Stream = qrMs });
                    }
                }
            }
        }

        private string BuildLocation()
        {
            string district = _schoolInfo?.District ?? "";
            string town = _schoolInfo?.Town ?? "";
            if (!string.IsNullOrWhiteSpace(district) && !string.IsNullOrWhiteSpace(town))
                return district.ToUpperInvariant() + ", " + town.ToUpperInvariant() + " - SIERRA LEONE";
            if (!string.IsNullOrWhiteSpace(district))
                return district.ToUpperInvariant() + " - SIERRA LEONE";
            if (!string.IsNullOrWhiteSpace(town))
                return town.ToUpperInvariant() + " - SIERRA LEONE";
            return "SIERRA LEONE";
        }

        private string GetCurrentAcademicYear()
        {
            int year = DateTime.Now.Year;
            int month = DateTime.Now.Month;
            if (month >= 9) return year + "/" + (year + 1);
            return (year - 1) + "/" + year;
        }
    }
}