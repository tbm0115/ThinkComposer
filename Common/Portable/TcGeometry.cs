using System;

namespace Instrumind.Common.Portable
{
    public enum TcDashStyle
    {
        Solid,
        Dash,
        Dot,
        DashDot,
        DashDotDot,
        Custom
    }

    public struct TcPoint : IEquatable<TcPoint>
    {
        public TcPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }

        public bool Equals(TcPoint other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y);
        }

        public override bool Equals(object obj)
        {
            return obj is TcPoint && Equals((TcPoint)obj);
        }

        public override int GetHashCode()
        {
            return PortableHash.Combine(X, Y);
        }
    }

    public struct TcSize : IEquatable<TcSize>
    {
        public TcSize(double width, double height)
        {
            Width = width;
            Height = height;
        }

        public double Width { get; }
        public double Height { get; }

        public bool Equals(TcSize other)
        {
            return Width.Equals(other.Width) && Height.Equals(other.Height);
        }

        public override bool Equals(object obj)
        {
            return obj is TcSize && Equals((TcSize)obj);
        }

        public override int GetHashCode()
        {
            return PortableHash.Combine(Width, Height);
        }
    }

    public struct TcRect : IEquatable<TcRect>
    {
        public TcRect(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }

        public TcPoint Location
        {
            get { return new TcPoint(X, Y); }
        }

        public TcSize Size
        {
            get { return new TcSize(Width, Height); }
        }

        public bool Equals(TcRect other)
        {
            return X.Equals(other.X)
                   && Y.Equals(other.Y)
                   && Width.Equals(other.Width)
                   && Height.Equals(other.Height);
        }

        public override bool Equals(object obj)
        {
            return obj is TcRect && Equals((TcRect)obj);
        }

        public override int GetHashCode()
        {
            return PortableHash.Combine(X, Y, Width, Height);
        }
    }
}
