using System;

namespace SRMS.Desktop.Abstractions
{
    /// <summary>
    /// Platform-agnostic color value. Replaces System.Drawing.Color in code
    /// that needs to specify colors for Excel cells (ScoreColorCoding) or
    /// for PDF drawing.
    ///
    /// Convert to/from platform-specific types:
    ///   Desktop: System.Drawing.Color.FromArgb(r, g, b)
    ///   Mobile:  Microsoft.Maui.Graphics.Color.FromRgb(r, g, b)
    /// </summary>
    public readonly struct RgbColor : IEquatable<RgbColor>
    {
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }

        public RgbColor(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public static RgbColor FromArgb(int r, int g, int b)
        {
            return new RgbColor(
                (byte)Math.Clamp(r, 0, 255),
                (byte)Math.Clamp(g, 0, 255),
                (byte)Math.Clamp(b, 0, 255));
        }

        public static RgbColor Black => new RgbColor(0, 0, 0);
        public static RgbColor White => new RgbColor(255, 255, 255);
        public static RgbColor Red => new RgbColor(255, 0, 0);
        public static RgbColor Green => new RgbColor(0, 128, 0);
        public static RgbColor Blue => new RgbColor(0, 0, 255);

        public string ToHex()
        {
            return $"#{R:X2}{G:X2}{B:X2}";
        }

        public bool Equals(RgbColor other)
            => R == other.R && G == other.G && B == other.B;

        public override bool Equals(object obj)
            => obj is RgbColor other && Equals(other);

        public override int GetHashCode()
            => (R << 16) | (G << 8) | B;

        public override string ToString() => ToHex();
    }
}