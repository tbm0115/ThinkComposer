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
    /// Options for the first-pass radial concept-map arrangement.
    /// </summary>
    public class SpiderMapLayoutOptions
    {
        public SpiderMapLayoutOptions()
        {
            this.ArrangeSelectedConceptsOnly = true;
            this.AutoFitConceptsBeforeArrange = true;
            this.RouteLinksAfterArrange = true;
            this.PreserveRootPosition = true;
            this.FirstRingRadius = 260.0;
            this.SecondRingRadius = 460.0;
            this.MinimumAngularSeparation = 20.0;
            this.MinimumNodeSpacing = 40.0;
            this.CanvasPadding = LayoutBoundsNormalizer.DefaultCanvasPadding;
            this.SafeDefaultRootCenter = new System.Windows.Point(600.0, 420.0);
            this.RevealArrangedContent = true;
        }

        public bool ArrangeSelectedConceptsOnly { get; set; }

        public bool AutoFitConceptsBeforeArrange { get; set; }

        public bool RouteLinksAfterArrange { get; set; }

        public bool PreserveRootPosition { get; set; }

        public double FirstRingRadius { get; set; }

        public double SecondRingRadius { get; set; }

        public double MinimumAngularSeparation { get; set; }

        public double MinimumNodeSpacing { get; set; }

        public double CanvasPadding { get; set; }

        public System.Windows.Point SafeDefaultRootCenter { get; set; }

        public bool RevealArrangedContent { get; set; }
    }
}
