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
    /// Options for conservative single-bend connector routing.
    /// </summary>
    public class LinkObstacleRoutingOptions
    {
        public LinkObstacleRoutingOptions()
        {
            this.ObstaclePadding = 16.0;
            this.NearMissPadding = 10.0;
            this.MinimumRouteImprovement = 8.0;
            this.MinimumSegmentLength = 4.0;
            this.PreserveExistingValidRoutes = true;
            this.RouteSelectedConnectorsOnly = true;
            this.IncludeComplementsAsObstacles = false;
            this.IncludeRelationshipCentralSymbolsAsObstacles = false;
            this.CorrectRelationshipCentersBeforeRouting = true;
            this.RelationshipVisualPlacementOptions = new RelationshipVisualPlacementOptions();
        }

        public double ObstaclePadding { get; set; }

        public double NearMissPadding { get; set; }

        public double MinimumRouteImprovement { get; set; }

        public double MinimumSegmentLength { get; set; }

        public bool PreserveExistingValidRoutes { get; set; }

        public bool RouteSelectedConnectorsOnly { get; set; }

        public bool IncludeComplementsAsObstacles { get; set; }

        public bool IncludeRelationshipCentralSymbolsAsObstacles { get; set; }

        public bool CorrectRelationshipCentersBeforeRouting { get; set; }

        public RelationshipVisualPlacementOptions RelationshipVisualPlacementOptions { get; set; }
    }
}
