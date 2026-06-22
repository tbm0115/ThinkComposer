using System;
using System.Globalization;

namespace Instrumind.Common.Portable
{
    public struct TcColor : IEquatable<TcColor>
    {
        public TcColor(byte a, byte r, byte g, byte b)
        {
            A = a;
            R = r;
            G = g;
            B = b;
        }

        public byte A { get; }
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }

        public static TcColor Transparent
        {
            get { return new TcColor(0, 0, 0, 0); }
        }

        public static TcColor FromArgb(byte a, byte r, byte g, byte b)
        {
            return new TcColor(a, r, g, b);
        }

        public static TcColor FromRgb(byte r, byte g, byte b)
        {
            return new TcColor(255, r, g, b);
        }

        public static bool TryParseHex(string value, out TcColor color)
        {
            color = Transparent;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            var hex = value.Trim();
            if (hex.StartsWith("#", StringComparison.Ordinal))
                hex = hex.Substring(1);

            if (hex.Length != 6 && hex.Length != 8)
                return false;

            try
            {
                var offset = hex.Length == 8 ? 2 : 0;
                var alpha = hex.Length == 8
                    ? byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                    : (byte)255;

                color = new TcColor(
                    alpha,
                    byte.Parse(hex.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(hex.Substring(offset + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(hex.Substring(offset + 4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        public string ToHexArgb()
        {
            return "#"
                   + A.ToString("X2", CultureInfo.InvariantCulture)
                   + R.ToString("X2", CultureInfo.InvariantCulture)
                   + G.ToString("X2", CultureInfo.InvariantCulture)
                   + B.ToString("X2", CultureInfo.InvariantCulture);
        }

        public bool Equals(TcColor other)
        {
            return A == other.A && R == other.R && G == other.G && B == other.B;
        }

        public override bool Equals(object obj)
        {
            return obj is TcColor && Equals((TcColor)obj);
        }

        public override int GetHashCode()
        {
            return PortableHash.Combine(A, R, G, B);
        }

        public override string ToString()
        {
            return ToHexArgb();
        }

        public static bool operator ==(TcColor left, TcColor right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(TcColor left, TcColor right)
        {
            return !left.Equals(right);
        }
    }
}
