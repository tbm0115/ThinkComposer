// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Copyright (C) 2011-2015 Néstor Marcel Sánchez Ahumada.
// https://github.com/nmarcel/ThinkComposer
//
// This file is part of ThinkComposer, which is free software licensed under the GNU General Public License.
// It is provided without any warranty. You should find a copy of the license in the root directory of this software product.
// -------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Instrumind.ThinkComposer.Composer.Layout
{
    /// <summary>
    /// Summary of an obstacle-routing run.
    /// </summary>
    public class LinkObstacleRoutingResult
    {
        public LinkObstacleRoutingResult()
        {
            this.Warnings = new List<string>();
        }

        public int Inspected { get; set; }

        public int ConnectorRoutesInspected { get; set; }

        public int RelationshipRoutesInspected { get; set; }

        public int Routed { get; set; }

        public int Straightened { get; set; }

        public int DoglegRouted { get; set; }

        public int Unchanged { get; set; }

        public int Skipped { get; set; }

        public RelationshipVisualPlacementResult RelationshipCenterPlacementResult { get; set; }

        public IList<string> Warnings { get; private set; }

        public bool HasMutations
        {
            get
            {
                return this.Routed > 0 ||
                       this.Straightened > 0 ||
                       this.DoglegRouted > 0 ||
                       (this.RelationshipCenterPlacementResult != null && this.RelationshipCenterPlacementResult.HasMutations);
            }
        }

        public void AddWarning(string Warning)
        {
            if (!System.String.IsNullOrWhiteSpace(Warning))
                this.Warnings.Add(Warning);
        }
    }
}
