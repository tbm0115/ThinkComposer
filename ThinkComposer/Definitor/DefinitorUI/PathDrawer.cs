// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Copyright (C) 2011-2015 Néstor Marcel Sánchez Ahumada.
// https://github.com/nmarcel/ThinkComposer
//
// This file is part of ThinkComposer, which is free software licensed under the GNU General Public License.
// It is provided without any warranty. You should find a copy of the license in the root directory of this software product.
// -------------------------------------------------------------------------------------------
//
// Project: Instrumind ThinkComposer v1.0
// File   : PathDrawer.cs
// Object : Instrumind.ThinkComposer.Definitor.DefinitorUI.PathDrawer (Class)
//
// Date       Author             Changes
// ---------- ------------------ -------------------------------------------------------------
// 2009.09.07 Néstor Sánchez A.  Creation
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

using Instrumind.Common;
using Instrumind.Common.Visualization;

using Instrumind.ThinkComposer.MetaModel.VisualMetaModel;

/// Provides user-interface common services, plus the components for the Domain related Definitions.
namespace Instrumind.ThinkComposer.Definitor.DefinitorUI
{
    /// <summary>
    /// Provides services for drawing connector paths.
    /// </summary>
    public static class PathDrawer
    {
        /// <summary>
        /// Default radius, in view coordinates, used for rounded polyline corners.
        /// The usable radius is also capped by the lengths of the adjacent segments.
        /// </summary>
        public const double DEFAULT_CORNER_RADIUS = 6.0;

        /// <summary>
        /// Creates and returns a Path using the supplied Path style, Corner style, Pen, Brush, Target and Source positions, Intermediate positions (if any) and Magnitude factor.
        /// </summary>
        public static Drawing CreatePath(EPathStyle PathStyle, EPathCorner CornerStyle, Pen PathPen, Brush PathBrush,
                                         Point TargetPosition, Point SourcePosition, IEnumerable<Point> IntermediatePositions = null, double Magnitude = 1.0)
        {
            var Points = new List<Point> { SourcePosition };
            if (IntermediatePositions != null)
                Points.AddRange(IntermediatePositions.Where(IsUsablePoint));

            Points.Add(TargetPosition);
            Points = RemoveConsecutiveDuplicates(Points);

            if (Points.Count < 2)
                return new GeometryDrawing(PathBrush, PathPen, new LineGeometry(SourcePosition, TargetPosition));

            var Figure = new PathFigure { StartPoint = Points[0], IsClosed = false, IsFilled = false };

            // The old Singleline styles drew each legacy leg as a sharp straight segment.
            // Preserve that appearance after migrating the former IntermediatePosition.
            var UseRoundedCorners = (CornerStyle == EPathCorner.Rounded
                                     && (PathStyle == EPathStyle.MultilineRightAngled
                                         || PathStyle == EPathStyle.MultilineFreeAngled)
                                     && Points.Count > 2);

            if (UseRoundedCorners)
                AppendRoundedSegments(Figure, Points, DEFAULT_CORNER_RADIUS);
            else
                for (int Index = 1; Index < Points.Count; Index++)
                    Figure.Segments.Add(new LineSegment(Points[Index], true));

            var Geometry = new PathGeometry();
            Geometry.Figures.Add(Figure);

            return new GeometryDrawing(PathBrush, PathPen, Geometry);
        }

        private static void AppendRoundedSegments(PathFigure Figure, IList<Point> Points, double RequestedRadius)
        {
            for (int Index = 1; Index < Points.Count - 1; Index++)
            {
                var Previous = Points[Index - 1];
                var Corner = Points[Index];
                var Next = Points[Index + 1];

                var Incoming = Corner - Previous;
                var Outgoing = Next - Corner;
                var IncomingLength = Incoming.Length;
                var OutgoingLength = Outgoing.Length;

                if (IncomingLength <= double.Epsilon || OutgoingLength <= double.Epsilon)
                    continue;

                var Radius = Math.Min(RequestedRadius, Math.Min(IncomingLength / 3.0, OutgoingLength / 3.0));
                if (Radius <= double.Epsilon)
                {
                    Figure.Segments.Add(new LineSegment(Corner, true));
                    continue;
                }

                Incoming.Normalize();
                Outgoing.Normalize();

                var CornerEntry = Corner - Incoming * Radius;
                var CornerExit = Corner + Outgoing * Radius;

                Figure.Segments.Add(new LineSegment(CornerEntry, true));
                Figure.Segments.Add(new QuadraticBezierSegment(Corner, CornerExit, true));
            }

            Figure.Segments.Add(new LineSegment(Points[Points.Count - 1], true));
        }

        private static List<Point> RemoveConsecutiveDuplicates(IEnumerable<Point> Points)
        {
            var Result = new List<Point>();
            foreach (var Point in Points)
                if (Result.Count == 0 || Result[Result.Count - 1] != Point)
                    Result.Add(Point);

            return Result;
        }

        private static bool IsUsablePoint(Point Point)
        {
            return !(double.IsNaN(Point.X) || double.IsInfinity(Point.X)
                     || double.IsNaN(Point.Y) || double.IsInfinity(Point.Y));
        }

    }
}
