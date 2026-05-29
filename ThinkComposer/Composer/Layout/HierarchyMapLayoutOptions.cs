// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Copyright (C) 2011-2015 Néstor Marcel Sánchez Ahumada.
// https://github.com/nmarcel/ThinkComposer
//
// This file is part of ThinkComposer, which is free software licensed under the GNU General Public License.
// It is provided without any warranty. You should find a copy of the license in the root directory of this software product.
// -------------------------------------------------------------------------------------------

namespace Instrumind.ThinkComposer.Composer.Layout
{
    /// <summary>
    /// Options for the first-pass top-down hierarchy arrangement.
    /// </summary>
    public class HierarchyMapLayoutOptions
    {
        public HierarchyMapLayoutOptions()
        {
            this.ArrangeSelectedConceptsOnly = true;
            this.AutoFitConceptsBeforeArrange = true;
            this.RouteLinksAfterArrange = true;
            this.NormalizeBounds = true;
            this.RevealArrangedContent = true;
            this.LevelSpacingY = 180.0;
            this.NodeSpacingX = 80.0;
            this.ComponentSpacingX = 220.0;
            this.CanvasPadding = LayoutBoundsNormalizer.DefaultCanvasPadding;
        }

        public bool ArrangeSelectedConceptsOnly { get; set; }

        public bool AutoFitConceptsBeforeArrange { get; set; }

        public bool RouteLinksAfterArrange { get; set; }

        public bool NormalizeBounds { get; set; }

        public bool RevealArrangedContent { get; set; }

        public double LevelSpacingY { get; set; }

        public double NodeSpacingX { get; set; }

        public double ComponentSpacingX { get; set; }

        public double CanvasPadding { get; set; }
    }
}
