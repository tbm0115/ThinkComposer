// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Dependency-free regression corpus for the pure orthogonal route planner.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Media;

using Instrumind.ThinkComposer.Definitor.DefinitorUI;
using Instrumind.ThinkComposer.MetaModel.VisualMetaModel;
using Instrumind.ThinkComposer.Model.GraphModel;
using Instrumind.ThinkComposer.Model.VisualModel;

namespace Instrumind.ThinkComposer.Composer.Layout
{
    public sealed class OrthogonalRouteRegressionResult
    {
        public OrthogonalRouteRegressionResult()
        {
            this.PassedScenarios = new List<string>();
            this.Failures = new List<string>();
        }

        public IList<string> PassedScenarios { get; private set; }
        public IList<string> Failures { get; private set; }
        public bool Passed { get { return this.Failures.Count == 0; } }
    }

    public static class OrthogonalRoutePlannerRegression
    {
        public static OrthogonalRouteRegressionResult RunAll()
        {
            var Result = new OrthogonalRouteRegressionResult();
            Run(Result, "clear-straight", TestClearStraight);
            Run(Result, "deterministic-symmetric-choice", TestDeterministicSymmetricChoice);
            Run(Result, "staggered-obstacles-multiple-bends", TestStaggeredObstacles);
            Run(Result, "stale-distant-route-rejected", TestStaleRouteRejected);
            Run(Result, "untouched-safe-route-preserved", TestSafeRoutePreserved);
            Run(Result, "obstacle-order-independent", TestObstacleOrderIndependent);
            Run(Result, "parallel-connector-stable-id-tiebreak", TestParallelConnectorStableIdTiebreak);
            Run(Result, "semantic-link-shared-across-representations", TestSharedSemanticLinkAcrossRepresentations);
            Run(Result, "bounded-work-fallback", TestBoundedWorkFallback);
            Run(Result, "primary-global-rejection-retries-secondary", TestPrimaryGlobalRejectionRetriesSecondary);
            Run(Result, "remote-obstacle-does-not-pull-outer-route", TestRemoteObstacleDoesNotPullOuterRoute);
            Run(Result, "zero-work-budget-does-not-reopen", TestZeroWorkBudget);
            Run(Result, "waypoint-segments-share-work-budget", TestWaypointWorkBudget);
            Run(Result, "simplify-preserves-collinear-reversal", TestSimplifyPreservesReversal);
            Run(Result, "mandatory-waypoints-respect-hard-cap", TestMandatoryWaypointHardCap);
            Run(Result, "mandatory-flowchart-waypoint", TestMandatoryWaypoint);
            Run(Result, "invalid-endpoint-rejected", TestInvalidEndpoint);
            Run(Result, "invalid-existing-point-repaired", TestInvalidExistingPoint);
            Run(Result, "narrow-corridor", TestNarrowCorridor);
            Run(Result, "enclosed-endpoint-fallback", TestEnclosedEndpointFallback);
            Run(Result, "flowchart-corridor-integration", TestFlowchartCorridorIntegration);
            Run(Result, "distant-relationship-center-detected", TestDistantRelationshipCenterDetected);
            Run(Result, "visible-self-reference-local-placement", TestVisibleSelfReferenceLocalPlacement);
            Run(Result, "degraded-hub-placement-stays-local", TestDegradedHubPlacementStaysLocal);
            Run(Result, "continuous-path-geometry", TestContinuousPathGeometry);
            Run(Result, "rounded-path-geometry", TestRoundedPathGeometry);
            return Result;
        }

        private static void Run(OrthogonalRouteRegressionResult Result, string Name, Action Test)
        {
            try
            {
                Test();
                Result.PassedScenarios.Add(Name);
            }
            catch (Exception Problem)
            {
                Result.Failures.Add(Name + ": " + Problem.Message);
            }
        }

        private static void TestClearStraight()
        {
            var Plan = OrthogonalRoutePlanner.Plan(Request(new Point(0, 0), new Point(100, 40)));
            Require(Plan.Status == RelationshipRouteStatus.Straight, "expected direct straight route");
            Require(Plan.RoutePoints.Count == 0 && Plan.IsSafe, "straight route should be safe and bend-free");
        }

        private static void TestDeterministicSymmetricChoice()
        {
            var Request1 = Request(new Point(0, 0), new Point(120, 0));
            Request1.PreferStraightPath = false;
            Request1.Obstacles.Add(new Rect(45, -15, 30, 30));
            var First = OrthogonalRoutePlanner.Plan(Request1);
            var Second = OrthogonalRoutePlanner.Plan(Request1);
            Require(SamePoints(First.RoutePoints, Second.RoutePoints), "identical inputs chose different routes");
            Require(First.IsSafe, "deterministic route was not safe");
        }

        private static void TestStaggeredObstacles()
        {
            var Input = Request(new Point(0, 0), new Point(220, 0));
            Input.PreferStraightPath = false;
            Input.Obstacles.Add(new Rect(40, -30, 35, 70));
            Input.Obstacles.Add(new Rect(105, -70, 35, 75));
            Input.Obstacles.Add(new Rect(165, -5, 35, 75));
            var Plan = OrthogonalRoutePlanner.Plan(Input);
            Require(Plan.IsSafe, "multi-obstacle route was not safe");
            Require(Plan.RoutePoints.Count >= 2, "multi-obstacle route did not use multiple bends");
        }

        private static void TestStaleRouteRejected()
        {
            var Input = Request(new Point(0, 0), new Point(100, 0));
            Input.Intent = RelationshipRouteIntent.RepairSuspicious;
            Input.ExistingRoutePoints.Add(new Point(0, 10000));
            Input.ExistingRoutePoints.Add(new Point(100, 10000));
            var Plan = OrthogonalRoutePlanner.Plan(Input);
            Require(Plan.Status != RelationshipRouteStatus.Preserved, "distant route was preserved");
            Require(Plan.RoutePoints.All(Point => Math.Abs(Point.Y) < 1000), "replacement still contains the distant bend");
        }

        private static void TestSafeRoutePreserved()
        {
            var Input = Request(new Point(0, 0), new Point(100, 100));
            Input.Intent = RelationshipRouteIntent.PreserveIfValid;
            Input.ExistingRoutePoints.Add(new Point(100, 0));
            var Plan = OrthogonalRoutePlanner.Plan(Input);
            Require(Plan.Status == RelationshipRouteStatus.Preserved, "safe manual route was not preserved");
            Require(SamePoints(Plan.RoutePoints, Input.ExistingRoutePoints), "preserved route geometry changed");
        }

        private static void TestObstacleOrderIndependent()
        {
            var Obstacles = new List<Rect>
            {
                new Rect(40, -20, 25, 50),
                new Rect(90, -50, 25, 60),
                new Rect(140, -10, 25, 60)
            };
            var FirstRequest = Request(new Point(0, 0), new Point(200, 0));
            FirstRequest.PreferStraightPath = false;
            FirstRequest.Obstacles = Obstacles.ToList();
            var SecondRequest = Request(new Point(0, 0), new Point(200, 0));
            SecondRequest.PreferStraightPath = false;
            SecondRequest.Obstacles = Obstacles.AsEnumerable().Reverse().ToList();
            Require(SamePoints(OrthogonalRoutePlanner.Plan(FirstRequest).RoutePoints,
                               OrthogonalRoutePlanner.Plan(SecondRequest).RoutePoints),
                    "obstacle enumeration order changed the route");
        }

        private static void TestParallelConnectorStableIdTiebreak()
        {
            var First = new VisualConnector { GlobalId = new Guid("00000000-0000-0000-0000-000000000001") };
            var Second = new VisualConnector { GlobalId = new Guid("00000000-0000-0000-0000-000000000002") };
            var SortKeyMethod = typeof(LinkObstacleRoutingService).GetMethod("GetConnectorSortKey",
                                                                            BindingFlags.Static | BindingFlags.NonPublic);
            Require(SortKeyMethod != null, "connector stable sort-key helper was not found");
            var FirstKey = (string)SortKeyMethod.Invoke(null, new object[] { First });
            var SecondKey = (string)SortKeyMethod.Invoke(null, new object[] { Second });
            Require(!String.Equals(FirstKey, SecondKey, StringComparison.Ordinal),
                    "parallel connectors with identical endpoints still have tied sort keys");
            Require(String.CompareOrdinal(FirstKey, SecondKey) < 0,
                    "connector stable-id tiebreak did not produce deterministic order");
        }

        private static void TestSharedSemanticLinkAcrossRepresentations()
        {
            var FirstRepresentation = (RelationshipVisualRepresentation)
                FormatterServices.GetUninitializedObject(typeof(RelationshipVisualRepresentation));
            var SecondRepresentation = (RelationshipVisualRepresentation)
                FormatterServices.GetUninitializedObject(typeof(RelationshipVisualRepresentation));
            var Link = (RoleBasedLink)FormatterServices.GetUninitializedObject(typeof(RoleBasedLink));
            var First = new VisualConnector
            {
                OwnerRelationshipRepresentation = FirstRepresentation,
                RepresentedLink = Link
            };
            var Second = new VisualConnector
            {
                OwnerRelationshipRepresentation = SecondRepresentation,
                RepresentedLink = Link
            };
            Require(RelationshipRoutingCoordinator.GetAmbiguousLinkConnectorsForValidation(new[] { First, Second }).Count == 0,
                    "a semantic Link shared by two valid visual representations was marked ambiguous");

            var Duplicate = new VisualConnector
            {
                OwnerRelationshipRepresentation = FirstRepresentation,
                RepresentedLink = Link
            };
            Require(RelationshipRoutingCoordinator.GetAmbiguousLinkConnectorsForValidation(
                        new[] { First, Second, Duplicate }).SetEquals(new[] { First, Duplicate }),
                    "a Link repeated inside one representation was not isolated as ambiguous");
        }

        private static void TestBoundedWorkFallback()
        {
            var Input = Request(new Point(0, 0), new Point(200, 0));
            Input.PreferStraightPath = false;
            Input.RemainingBatchWork = 1;
            Input.Obstacles.Add(new Rect(80, -20, 40, 40));
            var Plan = OrthogonalRoutePlanner.Plan(Input);
            Require(Plan.WorkCount <= Input.RemainingBatchWork,
                    "bounded search exceeded its work allowance");
            Require(Plan.Status == RelationshipRouteStatus.OuterFallback ||
                    Plan.Status == RelationshipRouteStatus.DirectFallback ||
                    Plan.Status == RelationshipRouteStatus.DegradedDirect,
                    "work cap did not select a documented fallback");
        }

        private static void TestPrimaryGlobalRejectionRetriesSecondary()
        {
            var Input = Request(new Point(0, 0), new Point(500, 0));
            Input.PreferStraightPath = false;
            // The tall obstacle intersects the primary envelope and contributes y=-201.
            // The horizontal obstacle sits just outside that envelope, invalidating the
            // primary candidate globally; the secondary search must see it and choose +201.
            Input.Obstacles.Add(new Rect(240, -200, 20, 400));
            Input.Obstacles.Add(new Rect(-20, -230, 540, 40));
            var Plan = OrthogonalRoutePlanner.Plan(Input);
            Require(Plan.Status == RelationshipRouteStatus.Routed && Plan.IsSafe,
                    "globally rejected primary candidate did not trigger a safe secondary-envelope search");
            Require(Plan.RoutePoints.Any(Point => Point.Y > 200),
                    "secondary-envelope route did not avoid the out-of-primary obstacle");
        }

        private static void TestRemoteObstacleDoesNotPullOuterRoute()
        {
            var Input = Request(new Point(0, 0), new Point(120, 0));
            Input.PreferStraightPath = false;
            Input.RemainingBatchWork = 0;
            Input.Obstacles.Add(new Rect(50, -15, 20, 30));
            Input.Obstacles.Add(new Rect(10000, 10000, 100, 100));
            var Plan = OrthogonalRoutePlanner.Plan(Input);
            Require(Plan.Status == RelationshipRouteStatus.OuterFallback && Plan.IsSafe,
                    "a local safe outer route was not selected after bounded search exhaustion");
            Require(Plan.RoutePoints.All(Point => Math.Abs(Point.X) < 1000 && Math.Abs(Point.Y) < 1000),
                    "a remote unrelated obstacle pulled the outer route into a distant sweep");
            Require(Plan.DetourRatio <= Input.MaximumPreservedDetourRatio,
                    "the accepted outer fallback exceeded the configured detour threshold");
        }

        private static void TestZeroWorkBudget()
        {
            var Input = Request(new Point(0, 0), new Point(200, 0));
            Input.PreferStraightPath = false;
            Input.RemainingBatchWork = 0;
            Input.Obstacles.Add(new Rect(80, -20, 40, 40));
            var Plan = OrthogonalRoutePlanner.Plan(Input);
            Require(Plan.WorkCount == 0, "a zero work budget was reopened");
            Require(Plan.HitBatchWorkCap, "zero work budget was not diagnosed");
        }

        private static void TestWaypointWorkBudget()
        {
            var Input = Request(new Point(0, 0), new Point(240, 0));
            Input.PreferStraightPath = false;
            Input.RemainingBatchWork = 2;
            Input.MandatoryWaypoints.Add(new Point(80, -60));
            Input.MandatoryWaypoints.Add(new Point(160, 60));
            var Plan = OrthogonalRoutePlanner.Plan(Input);
            Require(Plan.WorkCount <= Input.RemainingBatchWork,
                    "mandatory waypoint segments each reused the full work budget");
            Require(Plan.HitBatchWorkCap, "shared waypoint work exhaustion was not diagnosed");
        }

        private static void TestSimplifyPreservesReversal()
        {
            var Reversal = OrthogonalRoutePlanner.Simplify(new[]
            {
                new Point(0, 0), new Point(0, 20), new Point(0, 10)
            });
            Require(Reversal.Count == 3, "simplification removed a mandatory collinear reversal");

            var Forward = OrthogonalRoutePlanner.Simplify(new[]
            {
                new Point(0, 0), new Point(0, 10), new Point(0, 20)
            });
            Require(Forward.Count == 2, "simplification did not remove a forward collinear point");
        }

        private static void TestMandatoryWaypointHardCap()
        {
            var Input = Request(new Point(0, 0), new Point(400, 0));
            Input.HardMaximumRoutePoints = 3;
            Input.MandatoryWaypoints.Add(new Point(50, 20));
            Input.MandatoryWaypoints.Add(new Point(100, -20));
            Input.MandatoryWaypoints.Add(new Point(150, 20));
            Input.MandatoryWaypoints.Add(new Point(200, -20));
            var Plan = OrthogonalRoutePlanner.Plan(Input);
            Require(Plan.Status == RelationshipRouteStatus.DegradedDirect && Plan.IsSuspicious,
                    "oversized mandatory route did not produce an explicit degraded diagnostic");
            Require(Plan.RoutePoints.Count <= Input.HardMaximumRoutePoints,
                    "oversized mandatory route escaped the hard automatic cap");
            Require(Plan.Warnings.Any(Warning => Warning.IndexOf("mandatory", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                                 Warning.IndexOf("limit", StringComparison.OrdinalIgnoreCase) >= 0),
                    "oversized mandatory route did not explain the cap violation");
        }

        private static void TestMandatoryWaypoint()
        {
            var Input = Request(new Point(0, 0), new Point(120, 0));
            Input.MandatoryWaypoints.Add(new Point(60, -80));
            var Plan = OrthogonalRoutePlanner.Plan(Input);
            Require(Plan.RoutePoints.Any(Point => NearlyEqual(Point.X, 60) && NearlyEqual(Point.Y, -80)),
                    "mandatory feedback-lane waypoint was lost");
        }

        private static void TestInvalidEndpoint()
        {
            var Input = Request(new Point(Double.NaN, 0), new Point(100, 0));
            var Plan = OrthogonalRoutePlanner.Plan(Input);
            Require(Plan.Status == RelationshipRouteStatus.Failed && !Plan.IsSafe,
                    "nonfinite endpoint was accepted");
        }

        private static void TestInvalidExistingPoint()
        {
            var Input = Request(new Point(0, 0), new Point(100, 0));
            Input.Intent = RelationshipRouteIntent.RepairSuspicious;
            Input.ExistingRoutePoints.Add(new Point(40, Double.PositiveInfinity));
            var Plan = OrthogonalRoutePlanner.Plan(Input);
            Require(Plan.IsSuspicious, "invalid existing point was not diagnosed");
            Require(Plan.Status != RelationshipRouteStatus.Preserved,
                    "invalid existing route was preserved");
            Require(Plan.RoutePoints.All(Point => !Double.IsNaN(Point.X) && !Double.IsInfinity(Point.X) &&
                                                  !Double.IsNaN(Point.Y) && !Double.IsInfinity(Point.Y)),
                    "invalid replacement point escaped validation");
        }

        private static void TestNarrowCorridor()
        {
            var Input = Request(new Point(0, 0), new Point(200, 0));
            Input.PreferStraightPath = false;
            Input.Obstacles.Add(new Rect(20, -80, 160, 74));
            Input.Obstacles.Add(new Rect(20, 6, 160, 74));
            var Plan = OrthogonalRoutePlanner.Plan(Input);
            Require(Plan.IsSafe, "planner failed a collision-free narrow corridor");
            Require(Plan.Status != RelationshipRouteStatus.DegradedDirect,
                    "narrow corridor incorrectly used degraded fallback");
        }

        private static void TestEnclosedEndpointFallback()
        {
            var Input = Request(new Point(0, 0), new Point(100, 0));
            Input.PreferStraightPath = false;
            Input.Obstacles.Add(new Rect(-20, -20, 15, 40));
            Input.Obstacles.Add(new Rect(5, -20, 15, 40));
            Input.Obstacles.Add(new Rect(-5, -20, 10, 15));
            Input.Obstacles.Add(new Rect(-5, 5, 10, 15));
            var Plan = OrthogonalRoutePlanner.Plan(Input);
            Require(Plan.Status == RelationshipRouteStatus.DegradedDirect && !Plan.IsSafe,
                    "enclosed endpoint did not produce an explicit degraded fallback");
            Require(Plan.Warnings.Any(Warning => Warning.IndexOf("degraded", StringComparison.OrdinalIgnoreCase) >= 0),
                    "enclosed endpoint fallback was not diagnosed");
        }

        private static void TestFlowchartCorridorIntegration()
        {
            var Options = new LinkObstacleRoutingOptions { Profile = RelationshipRoutingProfile.Flowchart };
            var Representation = (RelationshipVisualRepresentation)
                FormatterServices.GetUninitializedObject(typeof(RelationshipVisualRepresentation));
            RelationshipRoutingCoordinator.ConfigureMandatoryCorridors(Options, new[] { Representation });
            Require(Options.MandatoryWaypointRelationships.Contains(Representation),
                    "Flowchart coordinator did not map feedback relationships to mandatory corridors");
        }

        private static void TestDistantRelationshipCenterDetected()
        {
            var EndpointCenters = new[] { new Point(0, 0), new Point(100, 0) };
            var EndpointBounds = new[] { new Rect(-10, -10, 20, 20), new Rect(90, -10, 20, 20) };
            var DistantReason = RelationshipVisualPlacementService.GetSuspiciousRelationshipCenterReason(
                new Point(5000, 5000), EndpointCenters, EndpointBounds, new RelationshipVisualPlacementOptions());
            Require(!String.IsNullOrWhiteSpace(DistantReason),
                    "a relationship hub thousands of pixels from both endpoints was considered healthy");

            var LocalReason = RelationshipVisualPlacementService.GetSuspiciousRelationshipCenterReason(
                new Point(50, 0), EndpointCenters, EndpointBounds, new RelationshipVisualPlacementOptions());
            Require(String.IsNullOrWhiteSpace(LocalReason),
                    "a relationship hub at the endpoint midpoint was considered suspicious");

            var SelfBounds = new[] { new Rect(-30, -20, 60, 40) };
            var DistantSelfReason = RelationshipVisualPlacementService.GetSuspiciousRelationshipCenterReason(
                new Point(5000, 0), new[] { new Point(0, 0) }, SelfBounds,
                new RelationshipVisualPlacementOptions());
            Require(!String.IsNullOrWhiteSpace(DistantSelfReason),
                    "a distant two-leg self-reference hub escaped relationship-center validation");
            var LocalSelfReason = RelationshipVisualPlacementService.GetSuspiciousRelationshipCenterReason(
                new Point(70, 0), new[] { new Point(0, 0) }, SelfBounds,
                new RelationshipVisualPlacementOptions());
            Require(String.IsNullOrWhiteSpace(LocalSelfReason),
                    "a local self-reference loop hub was considered suspicious");
        }

        private static void TestVisibleSelfReferenceLocalPlacement()
        {
            Require(RelationshipVisualPlacementService.IsPlaceableEndpointTopology(1, 2),
                    "two connector legs to one endpoint were rejected as an unplaceable visible self-reference");
            Require(!RelationshipVisualPlacementService.IsPlaceableEndpointTopology(1, 1),
                    "a single dangling connector leg was accepted as a self-reference");

            var Options = new RelationshipVisualPlacementOptions();
            var EndpointBounds = new Rect(-100, -30, 200, 60);
            var HubBounds = new Rect(-42, -13.5, 84, 27);
            var Corridor = EndpointBounds;
            Corridor.Inflate(Options.CorridorPaddingX, Options.CorridorPaddingY);
            var EndpointExclusion = EndpointBounds;
            EndpointExclusion.Inflate(Options.RelationshipCenterObstaclePadding,
                                      Options.RelationshipCenterObstaclePadding);
            var Candidates = RelationshipVisualPlacementService.GetSelfReferenceCandidateCenters(
                EndpointBounds, HubBounds, new Point(0, 0), Options);
            Require(Candidates.Any(Center =>
            {
                var Bounds = new Rect(Center.X - HubBounds.Width / 2.0,
                                      Center.Y - HubBounds.Height / 2.0,
                                      HubBounds.Width, HubBounds.Height);
                return Corridor.Contains(Center) && !EndpointExclusion.IntersectsWith(Bounds);
            }), "visible self-reference placement produced no local non-overlapping hub candidate");
        }

        private static void TestDegradedHubPlacementStaysLocal()
        {
            var Selected = RelationshipVisualPlacementService.SelectLowestCollisionDegradedCandidate(new[]
            {
                new RelationshipVisualPlacementCandidate
                {
                    Label = "outside-but-clear", Center = new Point(5000, 0),
                    Bounds = new Rect(4990, -10, 20, 20), InsideCorridor = false,
                    CollisionScore = 0, CollisionCount = 0, Score = 0
                },
                new RelationshipVisualPlacementCandidate
                {
                    Label = "local-more-collisions", Center = new Point(40, 0),
                    Bounds = new Rect(30, -10, 20, 20), InsideCorridor = true,
                    CollisionScore = 200, CollisionCount = 2, Score = 200
                },
                new RelationshipVisualPlacementCandidate
                {
                    Label = "local-least-collisions", Center = new Point(60, 0),
                    Bounds = new Rect(50, -10, 20, 20), InsideCorridor = true,
                    CollisionScore = 100, CollisionCount = 1, Score = 100
                }
            });
            Require(Selected != null && Selected.Label == "local-least-collisions",
                    "degraded placement preserved a distant center or ignored the lowest local collision score");
        }

        private static void TestContinuousPathGeometry()
        {
            AssertSinglePathGeometry(new List<Point>(), 1);
            AssertSinglePathGeometry(new List<Point> { new Point(50, 0) }, 2);
            AssertSinglePathGeometry(new List<Point>
            {
                new Point(25, 0), new Point(25, 40), new Point(75, 40), new Point(75, 0)
            }, 5);
        }

        private static void TestRoundedPathGeometry()
        {
            var Drawing = PathDrawer.CreatePath(EPathStyle.MultilineRightAngled, EPathCorner.Rounded,
                                                new Pen(Brushes.Black, 1.0), Brushes.Transparent,
                                                new Point(100, 50), new Point(0, 0),
                                                new[] { new Point(50, 0), new Point(50, 50) });
            var GeometryDrawing = Drawing as GeometryDrawing;
            var Geometry = GeometryDrawing == null ? null : GeometryDrawing.Geometry as PathGeometry;
            Require(Geometry != null && Geometry.Figures.Count == 1, "rounded route did not produce one PathGeometry");
            Require(Geometry.Figures[0].Segments.OfType<QuadraticBezierSegment>().Any(),
                    "rounded route did not emit rounded-corner segments");
        }

        private static void AssertSinglePathGeometry(IList<Point> Intermediate, int ExpectedSegments)
        {
            var Drawing = PathDrawer.CreatePath(EPathStyle.MultilineFreeAngled, EPathCorner.Sharp,
                                                new Pen(Brushes.Black, 1.0), Brushes.Transparent,
                                                new Point(100, 0), new Point(0, 0), Intermediate);
            var GeometryDrawing = Drawing as GeometryDrawing;
            var Geometry = GeometryDrawing == null ? null : GeometryDrawing.Geometry as PathGeometry;
            Require(Geometry != null && Geometry.Figures.Count == 1, "route did not produce one continuous PathGeometry");
            Require(Geometry.Figures[0].Segments.Count == ExpectedSegments,
                    "route segment count did not match the supplied 0/1/many points");
        }

        private static OrthogonalRouteRequest Request(Point Source, Point Target)
        {
            return new OrthogonalRouteRequest
            {
                RouteKey = "regression",
                Source = Source,
                Target = Target,
                Intent = RelationshipRouteIntent.Generated
            };
        }

        private static bool SamePoints(IList<Point> First, IList<Point> Second)
        {
            if (First == null || Second == null || First.Count != Second.Count)
                return false;
            for (var Index = 0; Index < First.Count; Index++)
                if (!NearlyEqual(First[Index].X, Second[Index].X) || !NearlyEqual(First[Index].Y, Second[Index].Y))
                    return false;
            return true;
        }

        private static bool NearlyEqual(double First, double Second)
        {
            return Math.Abs(First - Second) <= 0.001;
        }

        private static void Require(bool Condition, string Message)
        {
            if (!Condition)
                throw new InvalidOperationException(Message);
        }
    }
}
