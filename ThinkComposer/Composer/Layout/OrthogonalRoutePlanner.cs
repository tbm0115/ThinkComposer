// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Deterministic bounded orthogonal relationship route planner.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Instrumind.ThinkComposer.Composer.Layout
{
    /// <summary>
    /// Pure obstacle router. It has no model or rendering dependencies and is suitable for
    /// import, layout, UI, headless validation and dependency-free regression tests.
    /// </summary>
    public static class OrthogonalRoutePlanner
    {
        private const double Tolerance = 0.001;
        private const int MaximumAutomaticRoutePoints = 16;

        private enum TravelDirection
        {
            None = 0,
            Horizontal = 1,
            Vertical = 2
        }

        private sealed class SearchNode
        {
            public int Id;
            public int XIndex;
            public int YIndex;
            public Point Point;
        }

        private sealed class OpenItem
        {
            public int State;
            public double F;
            public double G;
            public long Sequence;
        }

        private sealed class OpenHeap
        {
            private readonly List<OpenItem> Items = new List<OpenItem>();

            public int Count { get { return this.Items.Count; } }

            public void Push(OpenItem Item)
            {
                this.Items.Add(Item);
                var Index = this.Items.Count - 1;
                while (Index > 0)
                {
                    var Parent = (Index - 1) / 2;
                    if (Compare(this.Items[Parent], this.Items[Index]) <= 0)
                        break;
                    Swap(this.Items, Parent, Index);
                    Index = Parent;
                }
            }

            public OpenItem Pop()
            {
                var Result = this.Items[0];
                var Last = this.Items[this.Items.Count - 1];
                this.Items.RemoveAt(this.Items.Count - 1);
                if (this.Items.Count == 0)
                    return Result;

                this.Items[0] = Last;
                var Index = 0;
                while (true)
                {
                    var Left = Index * 2 + 1;
                    var Right = Left + 1;
                    var Smallest = Index;
                    if (Left < this.Items.Count && Compare(this.Items[Left], this.Items[Smallest]) < 0)
                        Smallest = Left;
                    if (Right < this.Items.Count && Compare(this.Items[Right], this.Items[Smallest]) < 0)
                        Smallest = Right;
                    if (Smallest == Index)
                        break;
                    Swap(this.Items, Index, Smallest);
                    Index = Smallest;
                }
                return Result;
            }

            private static int Compare(OpenItem First, OpenItem Second)
            {
                var Result = First.F.CompareTo(Second.F);
                if (Result != 0)
                    return Result;
                Result = First.G.CompareTo(Second.G);
                if (Result != 0)
                    return Result;
                Result = First.State.CompareTo(Second.State);
                if (Result != 0)
                    return Result;
                return First.Sequence.CompareTo(Second.Sequence);
            }

            private static void Swap(List<OpenItem> Source, int First, int Second)
            {
                var Item = Source[First];
                Source[First] = Source[Second];
                Source[Second] = Item;
            }
        }

        private sealed class SearchResult
        {
            public IList<Point> Points;
            public int GridNodes;
            public int States;
            public int Work;
            public bool CoordinateCap;
            public bool GridCap;
            public bool StateCap;
            public bool WorkCap;
            public bool ObstacleCap;
        }

        public static OrthogonalRouteResult Plan(OrthogonalRouteRequest Request)
        {
            var Result = NewResult(Request);
            if (Request == null)
            {
                Result.Status = RelationshipRouteStatus.Failed;
                Result.AddWarning("No routing request was supplied.");
                return Result;
            }

            if (!IsUsablePoint(Request.Source) || !IsUsablePoint(Request.Target))
            {
                Result.Status = RelationshipRouteStatus.Failed;
                Result.AddWarning("The route has a nonfinite source or target.");
                return Result;
            }

            var Obstacles = NormalizeObstacles(Request, Result);
            Result.ObstacleCount = Obstacles.Count;
            var RawExisting = (Request.ExistingRoutePoints ?? new List<Point>()).ToList();
            var ExistingHasInvalidPoint = RawExisting.Any(Point => !IsUsablePoint(Point));
            var ExistingExceedsModelCap = RawExisting.Count > 32;
            var Existing = NormalizeInteriorPoints(RawExisting, Request.Source, Request.Target);
            bool ExistingSuspicious;
            string ExistingReason;
            var ExistingSafe = ValidateCompletePath(BuildCompletePath(Request.Source, Existing, Request.Target), Obstacles,
                                                    Request.MaximumPreservedDetourRatio,
                                                    out ExistingSuspicious, out ExistingReason);
            if (ExistingHasInvalidPoint || ExistingExceedsModelCap)
            {
                ExistingSafe = false;
                ExistingSuspicious = true;
                ExistingReason = ExistingHasInvalidPoint
                                 ? "route contains a nonfinite interior point"
                                 : "route exceeds the 32-point visual model limit";
            }

            if ((Request.Intent == RelationshipRouteIntent.PreserveIfValid ||
                 Request.Intent == RelationshipRouteIntent.RepairSuspicious) &&
                ExistingSafe && !ExistingSuspicious)
            {
                PopulateMetrics(Result, BuildCompletePath(Request.Source, Existing, Request.Target), Obstacles, Request);
                Result.RoutePoints = Existing;
                Result.Status = RelationshipRouteStatus.Preserved;
                Result.IsSafe = true;
                return Result;
            }

            if ((!ExistingSafe || ExistingSuspicious) && RawExisting.Count > 0)
            {
                Result.IsSuspicious = true;
                Result.AddWarning("Existing route was not preserved: " + ExistingReason);
            }

            var Mandatory = NormalizeInteriorPoints(Request.MandatoryWaypoints, Request.Source, Request.Target);
            var AutomaticPointCap = Math.Min(MaximumAutomaticRoutePoints,
                                             Math.Max(0, Request.HardMaximumRoutePoints));
            if (Mandatory.Count > AutomaticPointCap)
            {
                var Direct = new List<Point> { Request.Source, Request.Target };
                Result.RoutePoints = new List<Point>();
                Result.Status = RelationshipRouteStatus.DegradedDirect;
                Result.IsSafe = false;
                Result.IsSuspicious = true;
                Result.AddWarning("The route declares " + Mandatory.Count +
                                  " mandatory waypoints, exceeding the hard automatic limit of " +
                                  AutomaticPointCap + "; mandatory geometry was not truncated and a degraded direct route was emitted.");
                PopulateMetrics(Result, Direct, Obstacles, Request);
                return Result;
            }
            if (Mandatory.Count == 0 && Request.PreferStraightPath &&
                IsSegmentSafe(Request.Source, Request.Target, Obstacles))
            {
                var Direct = new List<Point> { Request.Source, Request.Target };
                PopulateMetrics(Result, Direct, Obstacles, Request);
                Result.RoutePoints = new List<Point>();
                Result.Status = RelationshipRouteStatus.Straight;
                Result.IsSafe = true;
                return Result;
            }

            var Required = new List<Point> { Request.Source };
            Required.AddRange(Mandatory);
            Required.Add(Request.Target);

            var PrimaryEnvelope = BoundsOf(Required);
            PrimaryEnvelope.Inflate(Math.Max(1.0, Request.PrimarySearchMargin), Math.Max(1.0, Request.PrimarySearchMargin));
            var SecondaryEnvelope = BoundsOf(Required);
            SecondaryEnvelope.Inflate(Math.Max(Request.PrimarySearchMargin, Request.SecondarySearchMargin),
                                      Math.Max(Request.PrimarySearchMargin, Request.SecondarySearchMargin));

            var PrimaryObstacles = Obstacles.Where(Obstacle => Obstacle.IntersectsWith(PrimaryEnvelope)).ToList();
            var SecondaryObstacles = Obstacles.Where(Obstacle => Obstacle.IntersectsWith(SecondaryEnvelope)).ToList();
            var PrimarySearch = SearchThroughWaypoints(Required, PrimaryObstacles, Request, Result);
            var Complete = PrepareSearchCandidate(PrimarySearch, AutomaticPointCap, Result,
                                                  "Primary-envelope");
            if (TryAcceptSearchCandidate(Complete, Required, Obstacles, Request, Result,
                                         "Primary-envelope"))
                return Result;

            // A path which is safe against primary-envelope obstacles can still intersect an
            // obstacle which only enters the fixed secondary envelope.  Validation is always
            // global, so a rejected primary candidate must trigger the second bounded search;
            // previously it skipped straight to fallback merely because A* had returned points.
            var SecondarySearch = SearchThroughWaypoints(Required, SecondaryObstacles, Request, Result);
            Complete = PrepareSearchCandidate(SecondarySearch, AutomaticPointCap, Result,
                                              "Secondary-envelope");
            if (TryAcceptSearchCandidate(Complete, Required, Obstacles, Request, Result,
                                         "Secondary-envelope"))
                return Result;

            var Suspicious = false;
            string Reason = null;

            // Boundary coordinates come only from obstacles inside the fixed secondary
            // envelope.  All obstacles still participate in collision validation, so a remote
            // symbol cannot pull an otherwise local connector into a giant sweep.
            var Outer = BuildOuterFallback(Required, SecondaryObstacles, Obstacles, Request);
            if (Outer != null && ValidateAutomaticPath(Outer, Required, Obstacles,
                                                       Request.MaximumPreservedDetourRatio,
                                                       out Suspicious, out Reason) && !Suspicious)
            {
                Result.RoutePoints = Interior(Outer);
                Result.Status = RelationshipRouteStatus.OuterFallback;
                Result.IsSafe = true;
                Result.AddWarning("The bounded grid search used the safe outer-perimeter fallback.");
                PopulateMetrics(Result, Outer, Obstacles, Request);
                return Result;
            }
            if (Outer != null && Suspicious)
                Result.AddWarning("Outer-perimeter route was rejected as suspicious: " + Reason + ".");

            var DirectPath = new List<Point> { Request.Source, Request.Target };
            if (Mandatory.Count == 0 && IsSegmentSafe(Request.Source, Request.Target, Obstacles))
            {
                Result.RoutePoints = new List<Point>();
                Result.Status = RelationshipRouteStatus.DirectFallback;
                Result.IsSafe = true;
                Result.AddWarning("The bounded route search fell back to a collision-free direct path.");
                PopulateMetrics(Result, DirectPath, Obstacles, Request);
                return Result;
            }

            var Degraded = Mandatory.Count == 0 ? DirectPath : Required;
            if (Degraded.Count - 2 > AutomaticPointCap)
            {
                Degraded = DirectPath;
                Result.AddWarning("The degraded mandatory route exceeded the hard automatic route-point limit; mandatory geometry was not truncated.");
            }
            Result.RoutePoints = Interior(Degraded);
            Result.Status = RelationshipRouteStatus.DegradedDirect;
            Result.IsSafe = false;
            Result.IsSuspicious = true;
            Result.AddWarning("No collision-free bounded route was found; stale geometry was discarded and a degraded direct path was used.");
            PopulateMetrics(Result, Degraded, Obstacles, Request);
            return Result;
        }

        private static IList<Point> PrepareSearchCandidate(SearchResult Search, int AutomaticPointCap,
                                                            OrthogonalRouteResult Result, string Label)
        {
            if (Search == null || Search.Points == null)
                return null;

            var Complete = Simplify(Search.Points);
            if (Complete.Count - 2 <= AutomaticPointCap)
                return Complete;

            Result.AddWarning(Label + " route exceeded the automatic route-point cap.");
            return null;
        }

        private static bool TryAcceptSearchCandidate(IList<Point> Complete, IList<Point> Required,
                                                     IList<Rect> Obstacles, OrthogonalRouteRequest Request,
                                                     OrthogonalRouteResult Result, string Label)
        {
            if (Complete == null)
                return false;

            bool Suspicious;
            string Reason;
            var Safe = ValidateAutomaticPath(Complete, Required, Obstacles,
                                             Request.MaximumPreservedDetourRatio,
                                             out Suspicious, out Reason);
            if (!Safe || Suspicious)
            {
                Result.AddWarning(Label + " route was rejected" +
                                  (Suspicious ? " as suspicious" : "") +
                                  (String.IsNullOrWhiteSpace(Reason) ? "." : ": " + Reason + "."));
                return false;
            }

            Result.RoutePoints = Interior(Complete);
            Result.Status = RelationshipRouteStatus.Routed;
            Result.IsSafe = true;
            Result.IsSuspicious = Result.RoutePoints.Count > Math.Max(0, Request.TargetMaximumRoutePoints);
            if (Result.IsSuspicious)
                Result.AddWarning("The safe route uses more than the target number of bends.");
            PopulateMetrics(Result, Complete, Obstacles, Request);
            return true;
        }

        public static bool IsRouteSuspicious(Point Source, IEnumerable<Point> RoutePoints, Point Target,
                                             IEnumerable<Rect> Obstacles, double MaximumDetourRatio,
                                             out string Reason)
        {
            bool Suspicious;
            var Safe = ValidateCompletePath(BuildCompletePath(Source, RoutePoints, Target),
                                            (Obstacles ?? Enumerable.Empty<Rect>()).Where(IsUsableRect).ToList(),
                                            MaximumDetourRatio, out Suspicious, out Reason);
            return !Safe || Suspicious;
        }

        public static IList<Point> Simplify(IEnumerable<Point> Source)
        {
            var Points = (Source ?? Enumerable.Empty<Point>()).Where(IsUsablePoint).ToList();
            var Result = new List<Point>();
            foreach (var Point in Points)
                if (Result.Count == 0 || !PointsEqual(Result[Result.Count - 1], Point))
                    Result.Add(Point);

            var Changed = true;
            while (Changed && Result.Count > 2)
            {
                Changed = false;
                for (var Index = 1; Index < Result.Count - 1; Index++)
                {
                    if (IsForwardCollinear(Result[Index - 1], Result[Index], Result[Index + 1]))
                    {
                        Result.RemoveAt(Index);
                        Changed = true;
                        break;
                    }
                }
            }
            return Result;
        }

        private static bool IsForwardCollinear(Point Previous, Point Current, Point Next)
        {
            if (NearlyEqual(Previous.X, Current.X) && NearlyEqual(Current.X, Next.X))
                return (Current.Y - Previous.Y) * (Next.Y - Current.Y) > Tolerance;
            if (NearlyEqual(Previous.Y, Current.Y) && NearlyEqual(Current.Y, Next.Y))
                return (Current.X - Previous.X) * (Next.X - Current.X) > Tolerance;
            return false;
        }

        private static OrthogonalRouteResult NewResult(OrthogonalRouteRequest Request)
        {
            return new OrthogonalRouteResult
            {
                RouteKey = Request == null ? null : Request.RouteKey,
                Source = Request == null ? new Point(Double.NaN, Double.NaN) : Request.Source,
                Target = Request == null ? new Point(Double.NaN, Double.NaN) : Request.Target,
                Intent = Request == null ? RelationshipRouteIntent.PreserveIfValid : Request.Intent,
                DirtyReason = Request == null ? null : Request.DirtyReason,
                Status = RelationshipRouteStatus.Failed
            };
        }

        private static List<Rect> NormalizeObstacles(OrthogonalRouteRequest Request, OrthogonalRouteResult Result)
        {
            var Corridor = new Rect(Request.Source, Request.Target);
            Corridor.Inflate(Math.Max(Request.SecondarySearchMargin, 1.0), Math.Max(Request.SecondarySearchMargin, 1.0));
            var Obstacles = (Request.Obstacles ?? new List<Rect>()).Where(IsUsableRect)
                .OrderBy(Rectangle => DistanceToRect(Corridor, Rectangle))
                .ThenBy(Rectangle => Rectangle.Left)
                .ThenBy(Rectangle => Rectangle.Top)
                .ThenBy(Rectangle => Rectangle.Right)
                .ThenBy(Rectangle => Rectangle.Bottom)
                .ToList();

            var Cap = Math.Max(0, Request.MaximumObstacles);
            if (Cap > 0 && Obstacles.Count > Cap)
            {
                Result.HitObstacleCap = true;
                Result.AddWarning("Coordinate-producing obstacles were deterministically limited to " + Cap +
                                  "; all " + Obstacles.Count + " obstacles remain active for collision validation.");
            }
            return Obstacles;
        }

        private static SearchResult SearchThroughWaypoints(IList<Point> Required, IList<Rect> Obstacles,
                                                            OrthogonalRouteRequest Request, OrthogonalRouteResult Aggregate)
        {
            var Complete = new List<Point>();
            for (var Index = 0; Index < Required.Count - 1; Index++)
            {
                // Work is a batch-wide budget.  A failed primary envelope and every mandatory
                // waypoint segment consume from the same allowance; no later search may reopen it.
                var RemainingWork = Math.Max(0, Request.RemainingBatchWork - Aggregate.WorkCount);
                var RemainingStates = Math.Max(0, Request.MaximumDirectionalStates -
                                                  Aggregate.DirectionalStateCount);
                if (RemainingWork == 0 || RemainingStates == 0)
                {
                    Aggregate.HitBatchWorkCap = Aggregate.HitBatchWorkCap || RemainingWork == 0;
                    Aggregate.HitStateCap = Aggregate.HitStateCap || RemainingStates == 0;
                    return new SearchResult { WorkCap = RemainingWork == 0, StateCap = RemainingStates == 0 };
                }

                var SegmentSearch = Search(Required[Index], Required[Index + 1], Required, Obstacles,
                                           Request, RemainingWork, RemainingStates);
                if (SegmentSearch == null)
                    return null;

                Aggregate.GridNodeCount = Math.Max(Aggregate.GridNodeCount, SegmentSearch.GridNodes);
                Aggregate.DirectionalStateCount += SegmentSearch.States;
                Aggregate.WorkCount += SegmentSearch.Work;
                Aggregate.HitCoordinateCap = Aggregate.HitCoordinateCap || SegmentSearch.CoordinateCap;
                Aggregate.HitGridNodeCap = Aggregate.HitGridNodeCap || SegmentSearch.GridCap;
                Aggregate.HitStateCap = Aggregate.HitStateCap || SegmentSearch.StateCap;
                Aggregate.HitBatchWorkCap = Aggregate.HitBatchWorkCap || SegmentSearch.WorkCap;
                Aggregate.HitObstacleCap = Aggregate.HitObstacleCap || SegmentSearch.ObstacleCap;
                if (SegmentSearch.Points == null)
                    return SegmentSearch;

                if (Complete.Count > 0)
                    Complete.RemoveAt(Complete.Count - 1);
                Complete.AddRange(SegmentSearch.Points);
            }
            return new SearchResult { Points = Simplify(Complete) };
        }

        private static SearchResult Search(Point Source, Point Target, IList<Point> RequiredCoordinates,
                                           IList<Rect> Obstacles, OrthogonalRouteRequest Request,
                                           int MaximumWork, int MaximumStates)
        {
            var Result = new SearchResult();
            if (MaximumWork <= 0 || MaximumStates <= 0)
            {
                Result.WorkCap = MaximumWork <= 0;
                Result.StateCap = MaximumStates <= 0;
                return Result;
            }
            var CoordinateObstacleCap = Math.Max(0, Request.MaximumObstacles);
            var SearchCorridor = new Rect(Source, Target);
            SearchCorridor.Inflate(Math.Max(1.0, Request.SecondarySearchMargin), Math.Max(1.0, Request.SecondarySearchMargin));
            var CoordinateObstacles = (Obstacles ?? new List<Rect>())
                .OrderBy(Rectangle => DistanceToRect(SearchCorridor, Rectangle))
                .ThenBy(Rectangle => Rectangle.Left)
                .ThenBy(Rectangle => Rectangle.Top)
                .ThenBy(Rectangle => Rectangle.Right)
                .ThenBy(Rectangle => Rectangle.Bottom)
                .ToList();
            if (CoordinateObstacleCap > 0 && CoordinateObstacles.Count > CoordinateObstacleCap)
            {
                CoordinateObstacles = CoordinateObstacles.Take(CoordinateObstacleCap).ToList();
                Result.ObstacleCap = true;
            }
            bool XCap;
            bool YCap;
            var XValues = BuildCoordinates(true, Source, Target, RequiredCoordinates, CoordinateObstacles, Request, out XCap);
            var YValues = BuildCoordinates(false, Source, Target, RequiredCoordinates, CoordinateObstacles, Request, out YCap);
            Result.CoordinateCap = XCap || YCap;

            if ((long)XValues.Count * (long)YValues.Count > Request.MaximumGridNodes)
            {
                Result.GridCap = true;
                return Result;
            }

            var Nodes = new List<SearchNode>();
            var NodeAt = new Dictionary<long, int>();
            for (var X = 0; X < XValues.Count; X++)
                for (var Y = 0; Y < YValues.Count; Y++)
                {
                    var Point = new Point(XValues[X], YValues[Y]);
                    if (IsStrictlyInsideAny(Point, Obstacles) && !PointsEqual(Point, Source) && !PointsEqual(Point, Target))
                        continue;
                    var Node = new SearchNode { Id = Nodes.Count, XIndex = X, YIndex = Y, Point = Point };
                    Nodes.Add(Node);
                    NodeAt[GridKey(X, Y)] = Node.Id;
                }

            Result.GridNodes = Nodes.Count;
            if (Nodes.Count == 0 || Nodes.Count > Request.MaximumGridNodes)
            {
                Result.GridCap = true;
                return Result;
            }

            int SourceNode;
            int TargetNode;
            if (!TryFindNode(Nodes, Source, out SourceNode) || !TryFindNode(Nodes, Target, out TargetNode))
                return Result;

            var StateCount = Nodes.Count * 3;
            if (StateCount > MaximumStates)
            {
                Result.StateCap = true;
                return Result;
            }

            var G = Enumerable.Repeat(Double.PositiveInfinity, StateCount).ToArray();
            var Previous = Enumerable.Repeat(-1, StateCount).ToArray();
            var Closed = new bool[StateCount];
            var Heap = new OpenHeap();
            long Sequence = 0;
            var StartState = StateId(SourceNode, TravelDirection.None);
            G[StartState] = 0.0;
            Heap.Push(new OpenItem { State = StartState, G = 0.0, F = Manhattan(Source, Target), Sequence = Sequence++ });
            var AcceptedSegments = BuildAcceptedSegments(Request.AcceptedRoutes);
            var GoalState = -1;

            while (Heap.Count > 0)
            {
                if (Result.Work >= MaximumWork)
                {
                    Result.WorkCap = true;
                    return Result;
                }

                var CurrentItem = Heap.Pop();
                if (Closed[CurrentItem.State])
                    continue;
                Closed[CurrentItem.State] = true;
                Result.States++;
                Result.Work++;
                if (Result.States > MaximumStates)
                {
                    Result.StateCap = true;
                    return Result;
                }
                var CurrentNode = CurrentItem.State / 3;
                var CurrentDirection = (TravelDirection)(CurrentItem.State % 3);
                if (CurrentNode == TargetNode)
                {
                    GoalState = CurrentItem.State;
                    break;
                }

                foreach (var Neighbor in GetVisibleNeighbors(Nodes[CurrentNode], XValues, YValues, NodeAt, Nodes, Obstacles))
                {
                    var Direction = NearlyEqual(Nodes[CurrentNode].Point.Y, Neighbor.Point.Y)
                                    ? TravelDirection.Horizontal : TravelDirection.Vertical;
                    var NextState = StateId(Neighbor.Id, Direction);
                    var SegmentLength = Manhattan(Nodes[CurrentNode].Point, Neighbor.Point);
                    var Bend = CurrentDirection != TravelDirection.None && CurrentDirection != Direction ? Request.BendCost : 0.0;
                    var Near = CountNearMisses(Nodes[CurrentNode].Point, Neighbor.Point, Obstacles, Request.NearMissPadding);
                    var Crossings = CountCrossings(Nodes[CurrentNode].Point, Neighbor.Point, AcceptedSegments);
                    var NewG = G[CurrentItem.State] + SegmentLength + Bend + Near * Request.NearMissCost + Crossings * Request.CrossingCost;
                    if (NewG + Tolerance >= G[NextState])
                        continue;

                    G[NextState] = NewG;
                    Previous[NextState] = CurrentItem.State;
                    Heap.Push(new OpenItem
                    {
                        State = NextState,
                        G = NewG,
                        F = NewG + Manhattan(Neighbor.Point, Target),
                        Sequence = Sequence++
                    });
                }
            }

            if (GoalState < 0)
                return Result;

            var Reversed = new List<Point>();
            for (var State = GoalState; State >= 0; State = Previous[State])
            {
                Reversed.Add(Nodes[State / 3].Point);
                if (State == StartState)
                    break;
            }
            Reversed.Reverse();
            Result.Points = Simplify(Reversed);
            return Result;
        }

        private static IList<double> BuildCoordinates(bool Horizontal, Point Source, Point Target,
                                                      IList<Point> Required, IList<Rect> Obstacles,
                                                      OrthogonalRouteRequest Request, out bool HitCap)
        {
            HitCap = false;
            var Mandatory = new List<double>();
            Mandatory.Add(Horizontal ? Source.X : Source.Y);
            Mandatory.Add(Horizontal ? Target.X : Target.Y);
            Mandatory.AddRange((Required ?? new List<Point>()).Select(Point => Horizontal ? Point.X : Point.Y));
            Mandatory = Mandatory.Where(IsFinite).Distinct(new NearlyEqualDoubleComparer()).OrderBy(Value => Value).ToList();

            var Optional = new List<double>();
            var Clearance = Math.Max(0.01, Request.GridClearance);
            foreach (var Obstacle in Obstacles ?? new List<Rect>())
            {
                if (Horizontal)
                {
                    Optional.Add(Obstacle.Left - Clearance);
                    Optional.Add(Obstacle.Right + Clearance);
                }
                else
                {
                    Optional.Add(Obstacle.Top - Clearance);
                    Optional.Add(Obstacle.Bottom + Clearance);
                }
            }

            var Mid = Horizontal ? (Source.X + Target.X) / 2.0 : (Source.Y + Target.Y) / 2.0;
            Optional = Optional.Where(IsFinite).Distinct(new NearlyEqualDoubleComparer())
                .Where(Value => !Mandatory.Any(Item => NearlyEqual(Item, Value)))
                .OrderBy(Value => DistanceToInterval(Value,
                                                     Math.Min(Horizontal ? Source.X : Source.Y, Horizontal ? Target.X : Target.Y),
                                                     Math.Max(Horizontal ? Source.X : Source.Y, Horizontal ? Target.X : Target.Y)))
                .ThenBy(Value => Math.Abs(Value - Mid))
                .ThenBy(Value => Value)
                .ToList();

            var Cap = Math.Max(Mandatory.Count, Request.MaximumCoordinatesPerAxis);
            if (Mandatory.Count + Optional.Count > Cap)
            {
                Optional = Optional.Take(Math.Max(0, Cap - Mandatory.Count)).ToList();
                HitCap = true;
            }
            return Mandatory.Concat(Optional).OrderBy(Value => Value).ToList();
        }

        private static IEnumerable<SearchNode> GetVisibleNeighbors(SearchNode Node, IList<double> XValues, IList<double> YValues,
                                                                   IDictionary<long, int> NodeAt, IList<SearchNode> Nodes,
                                                                   IList<Rect> Obstacles)
        {
            var Candidates = new List<SearchNode>();
            SearchNode Neighbor;
            if (TryNearestNode(Node.XIndex, Node.YIndex, -1, 0, XValues.Count, YValues.Count, NodeAt, Nodes, out Neighbor) &&
                IsSegmentSafe(Node.Point, Neighbor.Point, Obstacles))
                Candidates.Add(Neighbor);
            if (TryNearestNode(Node.XIndex, Node.YIndex, 1, 0, XValues.Count, YValues.Count, NodeAt, Nodes, out Neighbor) &&
                IsSegmentSafe(Node.Point, Neighbor.Point, Obstacles))
                Candidates.Add(Neighbor);
            if (TryNearestNode(Node.XIndex, Node.YIndex, 0, -1, XValues.Count, YValues.Count, NodeAt, Nodes, out Neighbor) &&
                IsSegmentSafe(Node.Point, Neighbor.Point, Obstacles))
                Candidates.Add(Neighbor);
            if (TryNearestNode(Node.XIndex, Node.YIndex, 0, 1, XValues.Count, YValues.Count, NodeAt, Nodes, out Neighbor) &&
                IsSegmentSafe(Node.Point, Neighbor.Point, Obstacles))
                Candidates.Add(Neighbor);
            return Candidates.OrderBy(Candidate => Candidate.Point.X).ThenBy(Candidate => Candidate.Point.Y);
        }

        private static bool TryNearestNode(int X, int Y, int DX, int DY, int Width, int Height,
                                           IDictionary<long, int> NodeAt, IList<SearchNode> Nodes, out SearchNode Node)
        {
            Node = null;
            X += DX;
            Y += DY;
            while (X >= 0 && X < Width && Y >= 0 && Y < Height)
            {
                int Id;
                if (NodeAt.TryGetValue(GridKey(X, Y), out Id))
                {
                    Node = Nodes[Id];
                    return true;
                }
                X += DX;
                Y += DY;
            }
            return false;
        }

        private static IList<Point> BuildOuterFallback(IList<Point> Required,
                                                       IList<Rect> BoundaryObstacles,
                                                       IList<Rect> ValidationObstacles,
                                                       OrthogonalRouteRequest Request)
        {
            if (Required == null || Required.Count < 2)
                return null;

            var Complete = new List<Point>();
            for (var Index = 0; Index < Required.Count - 1; Index++)
            {
                var Segment = BuildOuterFallbackSegment(Required[Index], Required[Index + 1], Required,
                                                        BoundaryObstacles, ValidationObstacles, Request);
                if (Segment == null)
                    return null;
                if (Complete.Count > 0)
                    Complete.RemoveAt(Complete.Count - 1);
                Complete.AddRange(Segment);
            }
            Complete = Simplify(Complete).ToList();
            var AutomaticPointCap = Math.Min(MaximumAutomaticRoutePoints,
                                             Math.Max(0, Request.HardMaximumRoutePoints));
            return Complete.Count - 2 <= AutomaticPointCap ? Complete : null;
        }

        private static IList<Point> BuildOuterFallbackSegment(Point Source, Point Target, IList<Point> Required,
                                                              IList<Rect> BoundaryObstacles,
                                                              IList<Rect> ValidationObstacles,
                                                              OrthogonalRouteRequest Request)
        {

            var AllBounds = BoundsOf(Required);
            foreach (var Obstacle in BoundaryObstacles ?? new List<Rect>())
                AllBounds.Union(Obstacle);
            var Clearance = Math.Max(24.0, Request.NearMissPadding + Request.GridClearance + 1.0);
            var Left = AllBounds.Left - Clearance;
            var Right = AllBounds.Right + Clearance;
            var Top = AllBounds.Top - Clearance;
            var Bottom = AllBounds.Bottom + Clearance;
            var Candidates = new List<IList<Point>>
            {
                new List<Point> { Source, new Point(Source.X, Top), new Point(Target.X, Top), Target },
                new List<Point> { Source, new Point(Source.X, Bottom), new Point(Target.X, Bottom), Target },
                new List<Point> { Source, new Point(Left, Source.Y), new Point(Left, Target.Y), Target },
                new List<Point> { Source, new Point(Right, Source.Y), new Point(Right, Target.Y), Target }
            };
            var AutomaticPointCap = Math.Min(MaximumAutomaticRoutePoints,
                                             Math.Max(0, Request.HardMaximumRoutePoints));
            return Candidates.Select(Simplify)
                .Where(Path => Path.Count - 2 <= AutomaticPointCap)
                .Where(Path => IsCompletePathSafe(Path, ValidationObstacles))
                .OrderBy(Path => PathLength(Path))
                .ThenBy(Path => String.Join(";", Path.Select(Point => Point.X.ToString("R") + "," + Point.Y.ToString("R")).ToArray()))
                .FirstOrDefault();
        }

        private static bool ValidateAutomaticPath(IList<Point> Complete, IList<Point> Required,
                                                  IList<Rect> Obstacles, double MaximumDetourRatio,
                                                  out bool Suspicious, out string Reason)
        {
            Suspicious = false;
            Reason = null;
            if (Complete == null || Complete.Count < 2 || Complete.Any(Point => !IsUsablePoint(Point)))
            {
                Reason = "route contains invalid or missing points";
                return false;
            }
            if (!IsCompletePathSafe(Complete, Obstacles))
            {
                Reason = "route intersects an obstacle";
                return false;
            }

            var RequiredPath = (Required ?? new List<Point>()).Where(IsUsablePoint).ToList();
            if (RequiredPath.Count < 2)
                RequiredPath = new List<Point> { Complete[0], Complete[Complete.Count - 1] };
            var Baseline = PathLength(RequiredPath);
            var Length = PathLength(Complete);
            if (Baseline > Tolerance && !Double.IsInfinity(MaximumDetourRatio) &&
                Length / Baseline > MaximumDetourRatio)
            {
                Suspicious = true;
                Reason = "route detour ratio " + (Length / Baseline).ToString("0.###") +
                         " exceeds " + MaximumDetourRatio.ToString("0.###");
            }

            // Mandatory Flowchart lanes define the expected corridor.  For ordinary links this
            // reduces to the endpoint corridor used for preservation validation.
            var Corridor = BoundsOf(RequiredPath);
            var Direct = Distance(Complete[0], Complete[Complete.Count - 1]);
            var CorridorPadding = Math.Max(200.0, Math.Max(Direct, Baseline) * 2.0);
            Corridor.Inflate(CorridorPadding, CorridorPadding);
            if (Complete.Skip(1).Take(Math.Max(0, Complete.Count - 2))
                        .Any(Point => !Corridor.Contains(Point)))
            {
                Suspicious = true;
                Reason = "route leaves the required endpoint/waypoint corridor excessively";
            }
            return true;
        }

        private static void PopulateMetrics(OrthogonalRouteResult Result, IList<Point> Complete,
                                            IList<Rect> Obstacles, OrthogonalRouteRequest Request)
        {
            Result.Length = PathLength(Complete);
            var Direct = Distance(Request.Source, Request.Target);
            Result.DetourRatio = Direct <= Tolerance ? (Result.Length <= Tolerance ? 1.0 : Double.PositiveInfinity) : Result.Length / Direct;
            Result.BendCount = Math.Max(0, Complete.Count - 2);
            Result.NearMissCount = 0;
            Result.CrossingCount = 0;
            var Accepted = BuildAcceptedSegments(Request.AcceptedRoutes);
            for (var Index = 0; Index < Complete.Count - 1; Index++)
            {
                Result.NearMissCount += CountNearMisses(Complete[Index], Complete[Index + 1], Obstacles, Request.NearMissPadding);
                Result.CrossingCount += CountCrossings(Complete[Index], Complete[Index + 1], Accepted);
            }
        }

        private static bool ValidateCompletePath(IList<Point> Complete, IList<Rect> Obstacles,
                                                 double MaximumDetourRatio, out bool Suspicious, out string Reason)
        {
            Suspicious = false;
            Reason = null;
            if (Complete == null || Complete.Count < 2 || Complete.Any(Point => !IsUsablePoint(Point)))
            {
                Reason = "route contains invalid or missing points";
                return false;
            }
            if (!IsCompletePathSafe(Complete, Obstacles))
            {
                Reason = "route intersects an obstacle";
                return false;
            }

            var Direct = Distance(Complete[0], Complete[Complete.Count - 1]);
            var Length = PathLength(Complete);
            if (Direct > Tolerance && !Double.IsInfinity(MaximumDetourRatio) && Length / Direct > MaximumDetourRatio)
            {
                Suspicious = true;
                Reason = "route detour ratio " + (Length / Direct).ToString("0.###") + " exceeds " + MaximumDetourRatio.ToString("0.###");
            }

            var Corridor = new Rect(Complete[0], Complete[Complete.Count - 1]);
            var CorridorPadding = Math.Max(200.0, Direct * 2.0);
            Corridor.Inflate(CorridorPadding, CorridorPadding);
            if (Complete.Skip(1).Take(Math.Max(0, Complete.Count - 2)).Any(Point => !Corridor.Contains(Point)))
            {
                Suspicious = true;
                Reason = "route leaves the endpoint corridor excessively";
            }
            return true;
        }

        private static bool IsCompletePathSafe(IList<Point> Complete, IList<Rect> Obstacles)
        {
            for (var Index = 0; Index < Complete.Count - 1; Index++)
                if (!IsSegmentSafe(Complete[Index], Complete[Index + 1], Obstacles))
                    return false;
            return true;
        }

        private static bool IsSegmentSafe(Point Start, Point End, IList<Rect> Obstacles)
        {
            if (!IsUsablePoint(Start) || !IsUsablePoint(End) || Distance(Start, End) <= Tolerance)
                return false;
            return !(Obstacles ?? new List<Rect>()).Any(Obstacle => SegmentIntersectsObstacle(Start, End, Obstacle));
        }

        public static bool SegmentIntersectsObstacle(Point Start, Point End, Rect Obstacle)
        {
            if (!IsUsableRect(Obstacle))
                return false;
            if (IsStrictlyInside(Obstacle, Start) || IsStrictlyInside(Obstacle, End))
                return true;

            var Bounds = new Rect(Start, End);
            Bounds.Inflate(Tolerance, Tolerance);
            if (!Bounds.IntersectsWith(Obstacle))
                return false;

            return SegmentsIntersect(Start, End, Obstacle.TopLeft, Obstacle.TopRight) ||
                   SegmentsIntersect(Start, End, Obstacle.TopRight, Obstacle.BottomRight) ||
                   SegmentsIntersect(Start, End, Obstacle.BottomRight, Obstacle.BottomLeft) ||
                   SegmentsIntersect(Start, End, Obstacle.BottomLeft, Obstacle.TopLeft);
        }

        private static int CountNearMisses(Point Start, Point End, IEnumerable<Rect> Obstacles, double Padding)
        {
            var Count = 0;
            foreach (var Obstacle in Obstacles ?? Enumerable.Empty<Rect>())
            {
                var Expanded = Obstacle;
                Expanded.Inflate(Math.Max(0.0, Padding), Math.Max(0.0, Padding));
                if (!SegmentIntersectsObstacle(Start, End, Obstacle) && SegmentIntersectsObstacle(Start, End, Expanded))
                    Count++;
            }
            return Count;
        }

        private static IList<Tuple<Point, Point>> BuildAcceptedSegments(IEnumerable<IList<Point>> Routes)
        {
            var Result = new List<Tuple<Point, Point>>();
            foreach (var Route in Routes ?? Enumerable.Empty<IList<Point>>())
                if (Route != null)
                    for (var Index = 0; Index < Route.Count - 1; Index++)
                        Result.Add(Tuple.Create(Route[Index], Route[Index + 1]));
            return Result;
        }

        private static int CountCrossings(Point Start, Point End, IEnumerable<Tuple<Point, Point>> Segments)
        {
            var Count = 0;
            foreach (var Segment in Segments ?? Enumerable.Empty<Tuple<Point, Point>>())
            {
                if (PointsEqual(Start, Segment.Item1) || PointsEqual(Start, Segment.Item2) ||
                    PointsEqual(End, Segment.Item1) || PointsEqual(End, Segment.Item2))
                    continue;
                if (SegmentsIntersect(Start, End, Segment.Item1, Segment.Item2))
                    Count++;
            }
            return Count;
        }

        private static bool SegmentsIntersect(Point A, Point B, Point C, Point D)
        {
            var D1 = Direction(C, D, A);
            var D2 = Direction(C, D, B);
            var D3 = Direction(A, B, C);
            var D4 = Direction(A, B, D);
            if (((D1 > Tolerance && D2 < -Tolerance) || (D1 < -Tolerance && D2 > Tolerance)) &&
                ((D3 > Tolerance && D4 < -Tolerance) || (D3 < -Tolerance && D4 > Tolerance)))
                return true;
            return Math.Abs(D1) <= Tolerance && OnSegment(C, D, A) ||
                   Math.Abs(D2) <= Tolerance && OnSegment(C, D, B) ||
                   Math.Abs(D3) <= Tolerance && OnSegment(A, B, C) ||
                   Math.Abs(D4) <= Tolerance && OnSegment(A, B, D);
        }

        private static double Direction(Point A, Point B, Point C)
        {
            return (C.X - A.X) * (B.Y - A.Y) - (C.Y - A.Y) * (B.X - A.X);
        }

        private static bool OnSegment(Point A, Point B, Point C)
        {
            return C.X >= Math.Min(A.X, B.X) - Tolerance && C.X <= Math.Max(A.X, B.X) + Tolerance &&
                   C.Y >= Math.Min(A.Y, B.Y) - Tolerance && C.Y <= Math.Max(A.Y, B.Y) + Tolerance;
        }

        private static IList<Point> NormalizeInteriorPoints(IEnumerable<Point> Source, Point Start, Point End)
        {
            var Complete = new List<Point> { Start };
            Complete.AddRange((Source ?? Enumerable.Empty<Point>()).Where(IsUsablePoint));
            Complete.Add(End);
            return Interior(Simplify(Complete));
        }

        private static IList<Point> BuildCompletePath(Point Source, IEnumerable<Point> InteriorPoints, Point Target)
        {
            var Result = new List<Point> { Source };
            Result.AddRange(InteriorPoints ?? Enumerable.Empty<Point>());
            Result.Add(Target);
            return Simplify(Result);
        }

        private static IList<Point> Interior(IList<Point> Complete)
        {
            return Complete == null || Complete.Count <= 2
                   ? new List<Point>()
                   : Complete.Skip(1).Take(Complete.Count - 2).ToList();
        }

        private static bool TryFindNode(IList<SearchNode> Nodes, Point Point, out int Id)
        {
            var Node = Nodes.FirstOrDefault(Item => PointsEqual(Item.Point, Point));
            Id = Node == null ? -1 : Node.Id;
            return Node != null;
        }

        private static int StateId(int NodeId, TravelDirection Direction)
        {
            return NodeId * 3 + (int)Direction;
        }

        private static long GridKey(int X, int Y)
        {
            return ((long)X << 32) | (uint)Y;
        }

        private static Rect BoundsOf(IEnumerable<Point> Points)
        {
            var List = (Points ?? Enumerable.Empty<Point>()).Where(IsUsablePoint).ToList();
            if (List.Count == 0)
                return Rect.Empty;
            var Result = new Rect(List[0], List[0]);
            foreach (var Point in List.Skip(1))
                Result.Union(Point);
            if (Result.Width <= Tolerance)
                Result.Inflate(1.0, 0.0);
            if (Result.Height <= Tolerance)
                Result.Inflate(0.0, 1.0);
            return Result;
        }

        private static double PathLength(IList<Point> Path)
        {
            var Result = 0.0;
            if (Path == null)
                return Result;
            for (var Index = 0; Index < Path.Count - 1; Index++)
                Result += Distance(Path[Index], Path[Index + 1]);
            return Result;
        }

        private static double Manhattan(Point First, Point Second)
        {
            return Math.Abs(First.X - Second.X) + Math.Abs(First.Y - Second.Y);
        }

        private static double Distance(Point First, Point Second)
        {
            var DX = First.X - Second.X;
            var DY = First.Y - Second.Y;
            return Math.Sqrt(DX * DX + DY * DY);
        }

        private static bool IsStrictlyInsideAny(Point Point, IEnumerable<Rect> Obstacles)
        {
            return (Obstacles ?? Enumerable.Empty<Rect>()).Any(Obstacle => IsStrictlyInside(Obstacle, Point));
        }

        private static bool IsStrictlyInside(Rect Rectangle, Point Point)
        {
            return Point.X > Rectangle.Left + Tolerance && Point.X < Rectangle.Right - Tolerance &&
                   Point.Y > Rectangle.Top + Tolerance && Point.Y < Rectangle.Bottom - Tolerance;
        }

        private static double DistanceToRect(Rect First, Rect Second)
        {
            var DX = Math.Max(0.0, Math.Max(First.Left - Second.Right, Second.Left - First.Right));
            var DY = Math.Max(0.0, Math.Max(First.Top - Second.Bottom, Second.Top - First.Bottom));
            return Math.Sqrt(DX * DX + DY * DY);
        }

        private static double DistanceToInterval(double Value, double Minimum, double Maximum)
        {
            return Value < Minimum ? Minimum - Value : (Value > Maximum ? Value - Maximum : 0.0);
        }

        private static bool IsUsablePoint(Point Point)
        {
            return IsFinite(Point.X) && IsFinite(Point.Y);
        }

        private static bool IsUsableRect(Rect Rectangle)
        {
            return !Rectangle.IsEmpty && Rectangle.Width >= 0 && Rectangle.Height >= 0 &&
                   IsFinite(Rectangle.Left) && IsFinite(Rectangle.Top) &&
                   IsFinite(Rectangle.Right) && IsFinite(Rectangle.Bottom);
        }

        private static bool IsFinite(double Value)
        {
            return !Double.IsNaN(Value) && !Double.IsInfinity(Value);
        }

        private static bool PointsEqual(Point First, Point Second)
        {
            return NearlyEqual(First.X, Second.X) && NearlyEqual(First.Y, Second.Y);
        }

        private static bool NearlyEqual(double First, double Second)
        {
            return Math.Abs(First - Second) <= Tolerance;
        }

        private sealed class NearlyEqualDoubleComparer : IEqualityComparer<double>
        {
            public bool Equals(double First, double Second) { return NearlyEqual(First, Second); }
            public int GetHashCode(double Value) { return Math.Round(Value / Tolerance).GetHashCode(); }
        }
    }
}
