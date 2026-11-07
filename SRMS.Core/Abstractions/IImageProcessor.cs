using System;
using System.IO;

namespace SRMS.Desktop.Abstractions
{
    /// <summary>
    /// Platform-agnostic image manipulation for report generation.
    ///
    /// This interface exists because System.Drawing.Common is Windows-only
    /// in .NET 8 (and later). We need identical report output on Windows
    /// and Android, so every image operation goes through this abstraction.
    ///
    /// Desktop implementation: SystemDrawingImageProcessor (wraps System.Drawing.Bitmap)
    /// Mobile implementation: SkiaImageProcessor (wraps SkiaSharp.SKBitmap)
    ///
    /// The report generators (FiveTests / SixTests / SSSI / Termly /
    /// TransferCertificate) depend ONLY on this interface, never on
    /// System.Drawing or SkiaSharp directly.
    /// </summary>
    public interface IImageProcessor
    {
        /// <summary>
        /// Loads an image from a stream. The caller owns the stream and is
        /// responsible for disposing it. The returned IPlatformImage must
        /// also be disposed by the caller.
        /// </summary>
        IPlatformImage LoadFromStream(Stream stream);

        /// <summary>
        /// Loads an image from a file path.
        /// </summary>
        IPlatformImage LoadFromFile(string filePath);

        /// <summary>
        /// Resizes an image to the exact pixel dimensions.
        /// If either dimension is 0, aspect ratio is preserved.
        /// </summary>
        IPlatformImage Resize(IPlatformImage source, int widthPx, int heightPx);

        /// <summary>
        /// Serializes the image to a stream in the requested format.
        /// The caller owns the returned stream.
        /// </summary>
        Stream ToStream(IPlatformImage image, ImageFormat format);

        /// <summary>
        /// Rasterizes a single page of a PDF (provided as bytes) into a
        /// PNG or JPEG stream. Used by PngGeneratorService.
        /// </summary>
        Stream RasterizePdfPage(byte[] pdfBytes, int pageIndex, int dpi, ImageFormat format);

        /// <summary>Returns the number of pages in a PDF document.</summary>
        int GetPdfPageCount(byte[] pdfBytes);
    }

    /// <summary>
    /// Opaque handle to a loaded image. Callers must Dispose when done.
    /// The concrete type is platform-specific and never exposed.
    /// </summary>
    public interface IPlatformImage : IDisposable
    {
        int WidthPx { get; }
        int HeightPx { get; }
    }

    public enum ImageFormat
    {
        Png,
        Jpeg
    }
}