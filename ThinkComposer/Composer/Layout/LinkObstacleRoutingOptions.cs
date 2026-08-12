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

using Instrumind.ThinkComposer.Model.VisualModel;

namespace Instrumind.ThinkComposer.Composer.Layout
{
    /// <summary>
    /// Options for deterministic bounded multi-point connector routing.
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
            this.RouteIntent = RelationshipRouteIntent.PreserveIfValid;
            this.Profile = RelationshipRoutingProfile.Manual;
            this.BendCost = 40.0;
            this.CrossingCost = 250.0;
            this.MaximumPreservedDetourRatio = 4.0;
            this.TargetMaximumRoutePoints = 8;
            this.HardMaximumRoutePoints = 16;
            this.MaximumObstacles = 64;
            this.MaximumCoordinatesPerAxis = 64;
            this.MaximumGridNodes = 4096;
            this.MaximumDirectionalStates = 12288;
            this.MaximumBatchWork = 500000;
            this.MandatoryWaypointRelationships = new HashSet<RelationshipVisualRepresentation>();
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

        public RelationshipRouteIntent RouteIntent { get; set; }

        public string DirtyReason { get; set; }

        public RelationshipRoutingProfile Profile { get; set; }

        public double BendCost { get; set; }

        public double CrossingCost { get; set; }

        public double MaximumPreservedDetourRatio { get; set; }

        public int TargetMaximumRoutePoints { get; set; }

        public int HardMaximumRoutePoints { get; set; }

        public int MaximumObstacles { get; set; }

        public int MaximumCoordinatesPerAxis { get; set; }

        public int MaximumGridNodes { get; set; }

        public int MaximumDirectionalStates { get; set; }

        public int MaximumBatchWork { get; set; }

        /// <summary>
        /// Relationships whose current route points are required corridors, such as Flowchart
        /// feedback lanes. The planner may add safe detours but must visit these points in order.
        /// </summary>
        public ISet<RelationshipVisualRepresentation> MandatoryWaypointRelationships { get; private set; }
    }

    public enum RelationshipRoutingProfile
    {
        Manual,
        JsonImport,
        Spider,
        Hierarchy,
        Flowchart,
        SystemMap,
        Validation
    }
}
