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
using System.Windows;

using Instrumind.ThinkComposer.Model.VisualModel;

namespace Instrumind.ThinkComposer.Composer.Layout
{
    /// <summary>
    /// Summary of a System Map arrangement run.
    /// </summary>
    public class SystemMapLayoutResult
    {
        public SystemMapLayoutResult()
        {
            this.Warnings = new List<string>();
            this.BoundsBeforeNormalization = Rect.Empty;
            this.BoundsAfterNormalization = Rect.Empty;
            this.BoundaryRectangle = Rect.Empty;
            this.GroupRegionStatus = "skipped";
            this.GroupRegionWarning = "";
            this.RevealAction = "none";
        }

        public int ConceptsInspected { get; set; }

        public int ConceptsArranged { get; set; }

        public int ConceptsMoved { get; set; }

        public int ConceptsSkipped { get; set; }

        public int RelationshipsInspected { get; set; }

        public int RelationshipVisualsInScope { get; set; }

        public int SystemRootCount { get; set; }

        public int InternalCount { get; set; }

        public int ExternalCount { get; set; }

        public int AmbiguousCount { get; set; }

        public int LeftExternalCount { get; set; }

        public int RightExternalCount { get; set; }

        public int TopBottomExternalCount { get; set; }

        public VisualSymbol SystemRootSymbol { get; set; }

        public string RootSelectionReason { get; set; }

        public Rect BoundaryRectangle { get; set; }

        public string GroupRegionStatus { get; set; }

        public bool GroupRegionCreated { get; set; }

        public bool GroupRegionUpdated { get; set; }

        public bool GroupRegionSkipped { get; set; }

        public string GroupRegionWarning { get; set; }

        public int GroupRegionContainmentExpansions { get; set; }

        public int CrossBoundaryRelationships { get; set; }

        public int CrossBoundaryRelationshipBubblesMoved { get; set; }

        public int CrossBoundaryBubbleCandidatesRejected { get; set; }

        public int CrossBoundaryBubblePlacementWarnings { get; set; }

        public int SystemMapValidationWarnings { get; set; }

        public ConceptAutoFitResult AutoFitResult { get; set; }

        public LinkObstacleRoutingResult RoutingResult { get; set; }

        public RelationshipNodeDeclutterResult RelationshipNodeDeclutterResult { get; set; }

        public Rect BoundsBeforeNormalization { get; set; }

        public Rect BoundsAfterNormalization { get; set; }

        public Vector NormalizationDelta { get; set; }

        public bool BoundsNormalized { get; set; }

        public bool FinalBoundsWithinSafeCanvas { get; set; }

        public string RevealAction { get; set; }

        public IList<string> Warnings { get; private set; }

        public bool HasMutations
        {
            get
            {
                return this.ConceptsMoved > 0 ||
                       this.GroupRegionCreated ||
                       this.GroupRegionUpdated ||
                       this.GroupRegionContainmentExpansions > 0 ||
                       this.CrossBoundaryRelationshipBubblesMoved > 0 ||
                       (this.AutoFitResult != null && this.AutoFitResult.SymbolsFitted > 0) ||
                       (this.RelationshipNodeDeclutterResult != null && this.RelationshipNodeDeclutterResult.HasMutations) ||
                       (this.RoutingResult != null && this.RoutingResult.HasMutations);
            }
        }

        public int LinksRouted
        {
            get
            {
                return this.RoutingResult == null
                       ? 0
                       : this.RoutingResult.Routed + this.RoutingResult.Straightened + this.RoutingResult.DoglegRouted;
            }
        }

        public int SkippedTotal
        {
            get { return this.ConceptsSkipped + (this.RoutingResult == null ? 0 : this.RoutingResult.Skipped); }
        }

        public void AddWarning(string Warning)
        {
            if (!String.IsNullOrWhiteSpace(Warning))
                this.Warnings.Add(Warning);
        }
    }
}
