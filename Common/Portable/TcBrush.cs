using System;
using System.Collections.Generic;

namespace Instrumind.Common.Portable
{
    public enum TcBrushKind
    {
        None,
        Solid,
        LinearGradient
    }

    public struct TcGradientStop : IEquatable<TcGradientStop>
    {
        public TcGradientStop(TcColor color, double offset)
        {
            Color = color;
            Offset = offset;
        }

        public TcColor Color { get; }
        public double Offset { get; }

        public bool Equals(TcGradientStop other)
        {
            return Color.Equals(other.Color) && Offset.Equals(other.Offset);
        }

        public override bool Equals(object obj)
        {
            return obj is TcGradientStop && Equals((TcGradientStop)obj);
        }

        public override int GetHashCode()
        {
            return PortableHash.Combine(Color, Offset);
        }
    }

    public sealed class TcBrush : IEquatable<TcBrush>
    {
        private static readonly TcGradientStop[] NoStops = new TcGradientStop[0];
        private readonly TcGradientStop[] gradientStops;

        private TcBrush(TcBrushKind kind, TcColor color, TcGradientStop[] stops, double angle)
        {
            Kind = kind;
            Color = color;
            gradientStops = stops ?? NoStops;
            Angle = angle;
        }

        public static TcBrush None
        {
            get { return new TcBrush(TcBrushKind.None, TcColor.Transparent, NoStops, 0); }
        }

        public TcBrushKind Kind { get; }
        public TcColor Color { get; }
        public double Angle { get; }

        public IReadOnlyList<TcGradientStop> GradientStops
        {
            get { return gradientStops; }
        }

        public static TcBrush Solid(TcColor color)
        {
            return new TcBrush(TcBrushKind.Solid, color, NoStops, 0);
        }

        public static TcBrush LinearGradient(TcColor startColor, TcColor endColor, double angle)
        {
            return new TcBrush(
                TcBrushKind.LinearGradient,
                startColor,
                new[] { new TcGradientStop(startColor, 0), new TcGradientStop(endColor, 1) },
                angle);
        }

        public bool Equals(TcBrush other)
        {
            if (other == null || Kind != other.Kind || !Color.Equals(other.Color) || !Angle.Equals(other.Angle))
                return false;

            if (gradientStops.Length != other.gradientStops.Length)
                return false;

            for (var index = 0; index < gradientStops.Length; index++)
                if (!gradientStops[index].Equals(other.gradientStops[index]))
                    return false;

            return true;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as TcBrush);
        }

        public override int GetHashCode()
        {
            var hash = PortableHash.Combine(Kind, Color, Angle);

            unchecked
            {
                for (var index = 0; index < gradientStops.Length; index++)
                    hash = (hash * 31) + gradientStops[index].GetHashCode();
            }

            return hash;
        }
    }
}
