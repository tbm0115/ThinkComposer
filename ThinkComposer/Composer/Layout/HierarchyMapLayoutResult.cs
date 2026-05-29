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

namespace Instrumind.ThinkComposer.Composer.Layout
{
    /// <summary>
    /// Summary of a Hierarchy Map arrangement run.
    /// </summary>
    public class HierarchyMapLayoutResult
    {
        public HierarchyMapLayoutResult()
        {
            this.Warnings = new List<string>();
            this.BoundsBeforeNormalization = Rect.Empty;
            this.BoundsAfterNormalization = Rect.Empty;
            this.RevealAction = "none";
        }

        public int ConceptsInspected { get; set; }

        public int ConceptsArranged { get; set; }

        public int ConceptsMoved { get; set; }

        public int ConceptsSkipped { get; set; }

        public int RelationshipsInspected { get; set; }

        public int DirectedEdges { get; set; }

        public int UndirectedEdges { get; set; }

        public int UnclearRelationships { get; set; }

        public int ComponentCount { get; set; }

        public int RootCount { get; set; }

        public int LevelCount { get; set; }

        public int CyclesDetected { get; set; }

        public ConceptAutoFitResult AutoFitResult { get; set; }

        public LinkObstacleRoutingResult RoutingResult { get; set; }

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
                       (this.AutoFitResult != null && this.AutoFitResult.SymbolsFitted > 0) ||
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
