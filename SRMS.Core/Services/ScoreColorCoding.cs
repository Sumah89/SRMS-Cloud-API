using System;
using Syncfusion.XlsIO;
using SRMS.Desktop.Abstractions;

namespace SRMS.Desktop.Services
{
    public enum ScoreType
    {
        TestScore,
        MeanScore,
        Percent60,
        Percent40
    }

    /// <summary>
    /// Cross-platform score color coding for Excel cells.
    ///
    /// Uses RgbColor internally so the decision logic (shouldHighlight /
    /// what color to return) is platform-neutral.
    ///
    /// When writing to a Syncfusion IRange, we convert RgbColor to
    /// Syncfusion.Drawing.Color — the type Syncfusion itself exposes
    /// on both Windows and MAUI.
    /// </summary>
    public static class ScoreColorCoding
    {
        private const decimal TEST_THRESHOLD = 50m;
        private const decimal MEAN_THRESHOLD = 50m;
        private const decimal PERCENT60_THRESHOLD = 30m;
        private const decimal PERCENT40_THRESHOLD = 20m;

        public static bool ShouldHighlight(decimal score, ScoreType type)
        {
            switch (type)
            {
                case ScoreType.TestScore: return score < TEST_THRESHOLD && score > 0;
                case ScoreType.MeanScore: return score < MEAN_THRESHOLD && score > 0;
                case ScoreType.Percent60: return score < PERCENT60_THRESHOLD && score > 0;
                case ScoreType.Percent40: return score < PERCENT40_THRESHOLD && score > 0;
                default: return false;
            }
        }

        public static RgbColor GetColor(decimal score, ScoreType type)
        {
            return ShouldHighlight(score, type) ? RgbColor.Red : RgbColor.Black;
        }

        public static string FormatTestScore(decimal score)
            => score <= 0 ? "" : Convert.ToInt32(score).ToString();

        public static string FormatMeanScore(decimal score)
            => score <= 0 ? "" : Math.Round(score, 1).ToString("F1");

        public static string FormatPercentScore(decimal score)
            => score <= 0 ? "" : Convert.ToInt32(score).ToString();

        public static decimal GetDisplayValue(decimal score, ScoreType type)
        {
            if (score <= 0) return 0;
            return type == ScoreType.MeanScore ? Math.Round(score, 1) : Math.Round(score, 0);
        }

        /// <summary>
        /// Applies score + color to a Syncfusion IRange cell.
        ///
        /// The RgbColor is converted to Syncfusion.Drawing.Color here.
        /// On .NET 8 desktop and MAUI, Syncfusion exposes this type via
        /// its own Syncfusion.Drawing namespace — no System.Drawing
        /// dependency leaks into SRMS.Core.
        /// </summary>
        public static void ApplyToExcelCell(IRange cell, decimal score, ScoreType type)
        {
            if (score <= 0) { cell.Clear(); return; }

            cell.Number = Convert.ToDouble(GetDisplayValue(score, type));

            var rgb = GetColor(score, type);
            cell.CellStyle.Font.RGBColor = ToSyncfusionColor(rgb);
        }

        public static void ApplyToExcelCell(IRange cell, decimal? score, ScoreType type)
        {
            if (!score.HasValue || score.Value <= 0) { cell.Clear(); return; }
            ApplyToExcelCell(cell, score.Value, type);
        }

        /// <summary>
        /// Convert our platform-neutral RgbColor to Syncfusion.Drawing.Color.
        /// This is the only place System.Drawing.Color is referenced, and
        /// it's safe because Syncfusion owns that type on every platform.
        /// </summary>
        private static Syncfusion.Drawing.Color ToSyncfusionColor(RgbColor c)
        {
            return Syncfusion.Drawing.Color.FromArgb(255, c.R, c.G, c.B);
        }
    }
}