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
    /// Options for decluttering visible relationship central symbols after concept layout.
    /// </summary>
    public class RelationshipNodeDeclutterOptions
    {
        public RelationshipNodeDeclutterOptions()
        {
            this.RelationshipBandPaddingY = 30.0;
            this.RelationshipNodeSpacingX = 24.0;
            this.MaxVerticalJitter = 40.0;
            this.RelationshipBubblePadding = 10.0;
            this.ConceptAvoidancePadding = 14.0;
            this.CorridorPaddingX = 80.0;
            this.CorridorPaddingY = 60.0;
            this.MaxPreferredDisplacement = 140.0;
            this.HardMaxDisplacement = 260.0;
            this.ShortEdgeMaxDistance = 520.0;
            this.OutsideCorridorPenalty = 10000.0;
            this.MaxGlobalDeclutterPasses = 4;
            this.CandidateShiftSteps = 4;
            this.HardRejectOutsideCorridorForAnchoredEdges = true;
            this.AvoidConceptBounds = true;
            this.IncludeOnlyVisibleRelationshipSymbols = true;
        }

        public double RelationshipBandPaddingY { get; set; }

        public double RelationshipNodeSpacingX { get; set; }

        public double MaxVerticalJitter { get; set; }

        public double RelationshipBubblePadding { get; set; }

        public double ConceptAvoidancePadding { get; set; }

        public double CorridorPaddingX { get; set; }

        public double CorridorPaddingY { get; set; }

        public double MaxPreferredDisplacement { get; set; }

        public double HardMaxDisplacement { get; set; }

        public double ShortEdgeMaxDistance { get; set; }

        public double OutsideCorridorPenalty { get; set; }

        public int MaxGlobalDeclutterPasses { get; set; }

        public int CandidateShiftSteps { get; set; }

        public bool HardRejectOutsideCorridorForAnchoredEdges { get; set; }

        public bool AvoidConceptBounds { get; set; }

        public bool IncludeOnlyVisibleRelationshipSymbols { get; set; }
    }
}
