// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Copyright (C) 2011-2015 Néstor Marcel Sánchez Ahumada.
// https://github.com/nmarcel/ThinkComposer
//
// This file is part of ThinkComposer, which is free software licensed under the GNU General Public License.
// It is provided without any warranty. You should find a copy of the license in the root directory of this software product.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

using Instrumind.Common;
using Instrumind.ThinkComposer.Model.VisualModel;

namespace Instrumind.ThinkComposer.Composer.Layout
{
    /// <summary>
    /// Shared helper for keeping arranged visual batches inside reachable canvas coordinates.
    /// </summary>
    public static class LayoutBoundsNormalizer
    {
        public const double DefaultCanvasPadding = 80.0;

        public static LayoutBoundsNormalizationResult NormalizeSymbolsToCanvas(View View, IEnumerable<VisualSymbol> Symbols,
                                                                               double CanvasPadding, string LogPrefix)
        {
            var Result = new LayoutBoundsNormalizationResult();
            Result.CanvasPadding = CanvasPadding;

            var SymbolList = (Symbols ?? Enumerable.Empty<VisualSymbol>())
                             .Where(Symbol => Symbol != null)
                             .Distinct()
                             .ToList();

            Result.BoundsBefore = ComputeSymbolBounds(SymbolList);
            if (Result.BoundsBefore.IsEmpty)
            {
                Console.WriteLine("{0} normalize bounds skipped: no usable arranged symbol bounds.", LogPrefix.ToStringAlways());
                return Result;
            }

            var DeltaX = Result.BoundsBefore.Left < CanvasPadding ? CanvasPadding - Result.BoundsBefore.Left : 0.0;
            var DeltaY = Result.BoundsBefore.Top < CanvasPadding ? CanvasPadding - Result.BoundsBefore.Top : 0.0;
            Result.Translation = new Vector(DeltaX, DeltaY);

            if (Math.Abs(DeltaX) > 0.001 || Math.Abs(DeltaY) > 0.001)
            {
                foreach (var Symbol in SymbolList)
                    Symbol.MoveTo(Symbol.BaseCenter.X + DeltaX, Symbol.BaseCenter.Y + DeltaY, true);

                Result.SymbolsTranslated = SymbolList.Count;
                Result.WasNormalized = true;
            }
            else
                foreach (var Symbol in SymbolList)
                    Symbol.RenderElement();

            Result.BoundsAfter = ComputeSymbolBounds(SymbolList);
            Result.IsWithinSafeBounds = !Result.BoundsAfter.IsEmpty &&
                                        Result.BoundsAfter.Left >= CanvasPadding - 0.001 &&
                                        Result.BoundsAfter.Top >= CanvasPadding - 0.001;

            Console.WriteLine("{0} normalize bounds: before={1}; dx={2:0.##}; dy={3:0.##}; after={4}; withinSafeBounds={5}; symbolsTranslated={6}.",
                              LogPrefix.ToStringAlways(),
                              FormatRect(Result.BoundsBefore),
                              DeltaX,
                              DeltaY,
                              FormatRect(Result.BoundsAfter),
                              Result.IsWithinSafeBounds ? "true" : "false",
                              Result.SymbolsTranslated.ToString(CultureInfo.InvariantCulture));

            return Result;
        }

        public static Rect ComputeSymbolBounds(IEnumerable<VisualSymbol> Symbols)
        {
            var Bounds = (Symbols ?? Enumerable.Empty<VisualSymbol>())
                         .Where(Symbol => Symbol != null)
                         .Select(Symbol => Symbol.TotalArea)
                         .Where(IsUsableRect)
                         .ToList();

            if (Bounds.Count < 1)
                return Rect.Empty;

            var Result = Bounds[0];
            foreach (var Rect in Bounds.Skip(1))
                Result.Union(Rect);

            return Result;
        }

        public static string FormatRect(Rect Rect)
        {
            if (Rect.IsEmpty)
                return "<empty>";

            return String.Format(CultureInfo.InvariantCulture,
                                 "x={0:0.##} y={1:0.##} width={2:0.##} height={3:0.##}",
                                 Rect.X, Rect.Y, Rect.Width, Rect.Height);
        }

        private static bool IsUsableRect(Rect Rect)
        {
            return !Rect.IsEmpty &&
                   !Double.IsNaN(Rect.X) && !Double.IsNaN(Rect.Y) &&
                   !Double.IsNaN(Rect.Width) && !Double.IsNaN(Rect.Height) &&
                   !Double.IsInfinity(Rect.X) && !Double.IsInfinity(Rect.Y) &&
                   !Double.IsInfinity(Rect.Width) && !Double.IsInfinity(Rect.Height) &&
                   Rect.Width >= 0.0 && Rect.Height >= 0.0;
        }
    }

    public class LayoutBoundsNormalizationResult
    {
        public Rect BoundsBefore { get; set; }

        public Rect BoundsAfter { get; set; }

        public Vector Translation { get; set; }

        public double CanvasPadding { get; set; }

        public bool WasNormalized { get; set; }

        public bool IsWithinSafeBounds { get; set; }

        public int SymbolsTranslated { get; set; }
    }
}
