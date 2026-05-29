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
    /// Options for the first-pass system-boundary layout.
    /// </summary>
    public class SystemMapLayoutOptions
    {
        public SystemMapLayoutOptions()
        {
            this.ArrangeSelectedConceptsOnly = true;
            this.AutoFitConceptsBeforeArrange = true;
            this.RouteLinksAfterArrange = true;
            this.DeclutterRelationshipNodesAfterArrange = true;
            this.NormalizeBounds = true;
            this.RevealArrangedContent = true;
            this.CreateGroupRegion = true;
            this.ReuseExistingGroupRegion = true;
            this.InternalGridColumns = 0;
            this.InternalSpacingX = 180.0;
            this.InternalSpacingY = 120.0;
            this.BoundaryPadding = 120.0;
            this.GroupRegionPadding = 120.0;
            this.GroupRegionPaddingLeft = 120.0;
            this.GroupRegionPaddingRight = 120.0;
            this.GroupRegionPaddingTop = 120.0;
            this.GroupRegionPaddingBottom = 140.0;
            this.GroupRegionSendToBack = true;
            this.GroupRegionLabelMode = "SystemRootName";
            this.CrossBoundaryBubbleOffsetX = 36.0;
            this.CrossBoundaryBubbleSpacingY = 48.0;
            this.CrossBoundaryBubbleLanePaddingX = 28.0;
            this.CrossBoundaryBubbleCandidateStepY = 36.0;
            this.CrossBoundaryBubbleObstaclePadding = 12.0;
            this.CrossBoundaryBubbleMaxOffsetSlots = 6;
            this.ExternalSpacingY = 120.0;
            this.ExternalOffsetX = 260.0;
            this.CanvasPadding = LayoutBoundsNormalizer.DefaultCanvasPadding;
            this.RelationshipNodeDeclutterOptions = new RelationshipNodeDeclutterOptions();
        }

        public bool ArrangeSelectedConceptsOnly { get; set; }

        public bool AutoFitConceptsBeforeArrange { get; set; }

        public bool RouteLinksAfterArrange { get; set; }

        public bool DeclutterRelationshipNodesAfterArrange { get; set; }

        public bool NormalizeBounds { get; set; }

        public bool RevealArrangedContent { get; set; }

        public bool CreateGroupRegion { get; set; }

        public bool ReuseExistingGroupRegion { get; set; }

        public int InternalGridColumns { get; set; }

        public double InternalSpacingX { get; set; }

        public double InternalSpacingY { get; set; }

        public double BoundaryPadding { get; set; }

        public double GroupRegionPadding { get; set; }

        public double GroupRegionPaddingLeft { get; set; }

        public double GroupRegionPaddingRight { get; set; }

        public double GroupRegionPaddingTop { get; set; }

        public double GroupRegionPaddingBottom { get; set; }

        public bool GroupRegionSendToBack { get; set; }

        public string GroupRegionLabelMode { get; set; }

        public double CrossBoundaryBubbleOffsetX { get; set; }

        public double CrossBoundaryBubbleSpacingY { get; set; }

        public double CrossBoundaryBubbleLanePaddingX { get; set; }

        public double CrossBoundaryBubbleCandidateStepY { get; set; }

        public double CrossBoundaryBubbleObstaclePadding { get; set; }

        public int CrossBoundaryBubbleMaxOffsetSlots { get; set; }

        public double ExternalSpacingY { get; set; }

        public double ExternalOffsetX { get; set; }

        public double CanvasPadding { get; set; }

        public RelationshipNodeDeclutterOptions RelationshipNodeDeclutterOptions { get; set; }
    }
}
