// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Copyright (C) 2011-2015 Nestor Marcel Sanchez Ahumada.
// https://github.com/nmarcel/ThinkComposer
//
// This file is part of ThinkComposer, which is free software licensed under the GNU General Public License.
// -------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Windows;

namespace Instrumind.ThinkComposer.Composer.Layout
{
    /// <summary>
    /// Describes why a connector is being considered by the shared router.
    /// </summary>
    public enum RelationshipRouteIntent
    {
        PreserveIfValid,
        RepairSuspicious,
        EndpointMoved,
        RelationshipCenterMoved,
        Generated,
        Layout
    }

    public enum RelationshipRouteStatus
    {
        Preserved,
        Straight,
        Routed,
        OuterFallback,
        DirectFallback,
        DegradedDirect,
        Failed
    }

    /// <summary>
    /// Pure routing input. Points are absolute view coordinates and obstacles are expected to
    /// already include the caller's desired symbol clearance.
    /// </summary>
    public sealed class OrthogonalRouteRequest
    {
        public OrthogonalRouteRequest()
        {
            this.Obstacles = new List<Rect>();
            this.ExistingRoutePoints = new List<Point>();
            this.MandatoryWaypoints = new List<Point>();
            this.AcceptedRoutes = new List<IList<Point>>();
            this.Intent = RelationshipRouteIntent.PreserveIfValid;
            this.PreferStraightPath = true;
            this.BendCost = 40.0;
            this.NearMissCost = 50.0;
            this.CrossingCost = 250.0;
            this.NearMissPadding = 10.0;
            this.GridClearance = 1.0;
            this.PrimarySearchMargin = 160.0;
            this.SecondarySearchMargin = 480.0;
            this.MaximumPreservedDetourRatio = 4.0;
            this.TargetMaximumRoutePoints = 8;
            this.HardMaximumRoutePoints = 16;
            this.MaximumObstacles = 64;
            this.MaximumCoordinatesPerAxis = 64;
            this.MaximumGridNodes = 4096;
            this.MaximumDirectionalStates = 12288;
            this.RemainingBatchWork = 500000;
        }

        public string RouteKey { get; set; }
        public Point Source { get; set; }
        public Point Target { get; set; }
        public IList<Rect> Obstacles { get; set; }
        public IList<Point> ExistingRoutePoints { get; set; }
        public IList<Point> MandatoryWaypoints { get; set; }
        public IList<IList<Point>> AcceptedRoutes { get; set; }
        public RelationshipRouteIntent Intent { get; set; }
        public string DirtyReason { get; set; }
        public bool PreferStraightPath { get; set; }
        public double BendCost { get; set; }
        public double NearMissCost { get; set; }
        public double CrossingCost { get; set; }
        public double NearMissPadding { get; set; }
        public double GridClearance { get; set; }
        public double PrimarySearchMargin { get; set; }
        public double SecondarySearchMargin { get; set; }
        public double MaximumPreservedDetourRatio { get; set; }
        public int TargetMaximumRoutePoints { get; set; }
        public int HardMaximumRoutePoints { get; set; }
        public int MaximumObstacles { get; set; }
        public int MaximumCoordinatesPerAxis { get; set; }
        public int MaximumGridNodes { get; set; }
        public int MaximumDirectionalStates { get; set; }
        public int RemainingBatchWork { get; set; }
    }

    /// <summary>
    /// Structured, deterministic diagnostics returned for every route attempt.
    /// RoutePoints contains interior points only.
    /// </summary>
    public sealed class OrthogonalRouteResult
    {
        public OrthogonalRouteResult()
        {
            this.RoutePoints = new List<Point>();
            this.Warnings = new List<string>();
        }

        public string RouteKey { get; set; }
        public Point Source { get; set; }
        public Point Target { get; set; }
        public RelationshipRouteIntent Intent { get; set; }
        public string DirtyReason { get; set; }
        public RelationshipRouteStatus Status { get; set; }
        public IList<Point> RoutePoints { get; set; }
        public int ObstacleCount { get; set; }
        public int GridNodeCount { get; set; }
        public int DirectionalStateCount { get; set; }
        public int WorkCount { get; set; }
        public int BendCount { get; set; }
        public int CrossingCount { get; set; }
        public int NearMissCount { get; set; }
        public double Length { get; set; }
        public double DetourRatio { get; set; }
        public bool HitObstacleCap { get; set; }
        public bool HitCoordinateCap { get; set; }
        public bool HitGridNodeCap { get; set; }
        public bool HitStateCap { get; set; }
        public bool HitBatchWorkCap { get; set; }
        public bool IsSuspicious { get; set; }
        public bool IsSafe { get; set; }
        public IList<string> Warnings { get; private set; }

        public void AddWarning(string Warning)
        {
            if (!System.String.IsNullOrWhiteSpace(Warning))
                this.Warnings.Add(Warning);
        }
    }

    public sealed class RelationshipRouteDiagnostic
    {
        public RelationshipRouteDiagnostic()
        {
            this.OldPoints = new List<Point>();
            this.NewPoints = new List<Point>();
        }

        public string RouteKey { get; set; }
        public Point Source { get; set; }
        public Point Target { get; set; }
        public RelationshipRouteIntent Intent { get; set; }
        public string DirtyReason { get; set; }
        public RelationshipRouteStatus Status { get; set; }
        public int OldPointCount { get; set; }
        public int NewPointCount { get; set; }
        public IList<Point> OldPoints { get; set; }
        public IList<Point> NewPoints { get; set; }
        public int BendCount { get; set; }
        public int ObstacleCount { get; set; }
        public int GridNodeCount { get; set; }
        public int DirectionalStateCount { get; set; }
        public int WorkCount { get; set; }
        public int CrossingCount { get; set; }
        public int NearMissCount { get; set; }
        public double DetourRatio { get; set; }
        public bool HitObstacleCap { get; set; }
        public bool HitCoordinateCap { get; set; }
        public bool HitGridNodeCap { get; set; }
        public bool HitStateCap { get; set; }
        public bool HitBatchWorkCap { get; set; }
        public bool UsedFallback { get; set; }
        public bool AppearanceChanged { get; set; }
        public bool IsDistantRelationshipCenter { get; set; }
        public bool IsSuspicious { get; set; }
        public bool IsSafe { get; set; }
        public string Message { get; set; }
    }
}
