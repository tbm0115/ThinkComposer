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
    /// Options for the first-pass left-to-right flowchart arrangement.
    /// </summary>
    public class FlowchartLayoutOptions
    {
        public FlowchartLayoutOptions()
        {
            this.ArrangeSelectedConceptsOnly = true;
            this.AutoFitConceptsBeforeArrange = true;
            this.RouteLinksAfterArrange = true;
            this.DeclutterRelationshipNodesAfterArrange = true;
            this.NormalizeBounds = true;
            this.RevealArrangedContent = true;
            this.StepSpacingX = 240.0;
            this.LaneSpacingY = 140.0;
            this.ComponentSpacingY = 220.0;
            this.CanvasPadding = LayoutBoundsNormalizer.DefaultCanvasPadding;
            this.FeedbackLanePlacement = "Auto";
            this.FeedbackLanePaddingY = 100.0;
            this.FeedbackLaneSpacingY = 60.0;
            this.CrossLinkLanePaddingY = 80.0;
            this.PreferTopFeedbackLane = true;
            this.RelationshipNodeDeclutterOptions = new RelationshipNodeDeclutterOptions();
        }

        public bool ArrangeSelectedConceptsOnly { get; set; }

        public bool AutoFitConceptsBeforeArrange { get; set; }

        public bool RouteLinksAfterArrange { get; set; }

        public bool DeclutterRelationshipNodesAfterArrange { get; set; }

        public bool NormalizeBounds { get; set; }

        public bool RevealArrangedContent { get; set; }

        public double StepSpacingX { get; set; }

        public double LaneSpacingY { get; set; }

        public double ComponentSpacingY { get; set; }

        public double CanvasPadding { get; set; }

        public string FeedbackLanePlacement { get; set; }

        public double FeedbackLanePaddingY { get; set; }

        public double FeedbackLaneSpacingY { get; set; }

        public double CrossLinkLanePaddingY { get; set; }

        public bool PreferTopFeedbackLane { get; set; }

        public RelationshipNodeDeclutterOptions RelationshipNodeDeclutterOptions { get; set; }
    }
}
