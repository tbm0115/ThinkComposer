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
using System.Globalization;
using System.Linq;
using System.Windows;

using Instrumind.Common;
using Instrumind.Common.Visualization;
using Instrumind.ThinkComposer.MetaModel.VisualMetaModel;
using Instrumind.ThinkComposer.Model.GraphModel;
using Instrumind.ThinkComposer.Model.VisualModel;

namespace Instrumind.ThinkComposer.Composer.Layout
{
    /// <summary>
    /// Shared connector routing service backed by the deterministic multi-point planner.
    /// </summary>
    public static class LinkObstacleRoutingService
    {
        private const double GeometryTolerance = 0.001;

        private enum RouteKind
        {
            Straight,
            HorizontalFirst,
            VerticalFirst,
            HorizontalDogleg,
            VerticalDogleg,
            Existing
        }

        private class RouteCandidate
        {
            public RouteKind Kind;
            public Point Intermediate;
            public Point SourceIntermediate;
            public Point TargetIntermediate;
            public double Length;
            public int NearMisses;
            public double ExistingMovement;
            public double BendPenalty;
            public bool IsValid;
            public bool IsExisting;
            public string InvalidReason;

            public bool IsStraight
            {
                get { return this.Kind == RouteKind.Straight && this.Intermediate == Display.NULL_POINT; }
            }

            public bool IsDogleg
            {
                get { return this.Kind == RouteKind.HorizontalDogleg || this.Kind == RouteKind.VerticalDogleg; }
            }

            public double Score
            {
                get { return this.Length + this.NearMisses * 50.0 + this.ExistingMovement * 0.15 + this.BendPenalty; }
            }
        }

        private class ObstacleInfo
        {
            public VisualSymbol Symbol;
            public Rect Bounds;
        }

        private class HiddenCentralRelationshipRoute
        {
            public RelationshipVisualRepresentation Representation;
            public VisualSymbol MainSymbol;
            public VisualSymbol SourceSymbol;
            public VisualSymbol TargetSymbol;
            public VisualConnector SourceConnector;
            public VisualConnector TargetConnector;

            public IEnumerable<VisualConnector> Connectors
            {
                get
                {
                    return this.Representation == null
                           ? Enumerable.Empty<VisualConnector>()
                           : this.Representation.VisualConnectors.Where(Connector => Connector != null);
                }
            }
        }

        public static IList<VisualConnector> GetConnectorsForScope(LayoutSelectionContext Context, LinkObstacleRoutingOptions Options)
        {
            if (Context == null)
                return new List<VisualConnector>();

            Options = Options ?? new LinkObstacleRoutingOptions();
            var Source = Options.RouteSelectedConnectorsOnly ? Context.SelectedRouteableConnectors : Context.VisibleRelationshipConnectors;
            return Source.Where(IsPotentiallyRouteable).Distinct().ToList();
        }

        public static LinkObstacleRoutingResult RouteVisibleConnectors(LayoutSelectionContext Context, LinkObstacleRoutingOptions Options)
        {
            Options = Options ?? new LinkObstacleRoutingOptions();
            var Result = new LinkObstacleRoutingResult();

            if (Context == null || Context.ActiveView == null)
            {
                Result.AddWarning("No active view is available for link routing.");
                LogSummary(Result);
                return Result;
            }

            if (Options.CorrectRelationshipCentersBeforeRouting)
            {
                var PlacementScope = Options.RouteSelectedConnectorsOnly
                                     ? Context.SelectedRelationshipRepresentations
                                     : Context.VisibleRelationshipRepresentations;
                Result.RelationshipCenterPlacementResult =
                    RelationshipVisualPlacementService.PlaceVisibleRelationshipCenters(Context,
                                                                                       PlacementScope,
                                                                                       Options.RelationshipVisualPlacementOptions);
                foreach (var Warning in Result.RelationshipCenterPlacementResult.Warnings)
                    Result.AddWarning("Relationship center placement: " + Warning);
            }

            var Connectors = GetConnectorsForScope(Context, Options);
            if (Options.RouteSelectedConnectorsOnly && Result.RelationshipCenterPlacementResult != null)
            {
                // Placement invalidates every leg incident to a moved hub.  Expand only those
                // relationships; otherwise unselected legs would remain cleared and stale.
                Connectors = Connectors.Concat(Result.RelationshipCenterPlacementResult.RecomputedRepresentations
                                  .Where(Representation => Representation != null)
                                  .SelectMany(Representation => Representation.VisualConnectors ??
                                                                Enumerable.Empty<VisualConnector>())
                                  .Where(IsPotentiallyRouteable))
                                       .Distinct()
                                       .ToList();
            }
            var HiddenCentralRelationships = GetHiddenCentralRelationshipsForScope(Connectors);
            var HiddenCentralSet = new HashSet<RelationshipVisualRepresentation>(HiddenCentralRelationships);
            var IndividualConnectors = Connectors.Where(Connector => Connector.OwnerRelationshipRepresentation == null ||
                                                                     !HiddenCentralSet.Contains(Connector.OwnerRelationshipRepresentation))
                                                .ToList();

            Console.WriteLine("Appearance: Route Links with Obstacle Avoidance starting; view={0}; scope={1}; connectors={2}; hiddenCentralRelationships={3}; options obstaclePadding={4:0.##}, nearMissPadding={5:0.##}, minImprovement={6:0.##}, preserveExisting={7}, correctRelationshipCenters={8}.",
                              DescribeView(Context == null ? null : Context.ActiveView),
                              Options.RouteSelectedConnectorsOnly ? "selected links" : "all visible links",
                              Connectors.Count,
                              HiddenCentralRelationships.Count,
                              Options.ObstaclePadding,
                              Options.NearMissPadding,
                              Options.MinimumRouteImprovement,
                              Options.PreserveExistingValidRoutes,
                              Options.CorrectRelationshipCentersBeforeRouting ? "true" : "false");

            var ObstacleInfos = BuildObstacleInfos(Context, Options);
            var ObstacleRectangles = ObstacleInfos.Select(Obstacle => Obstacle.Bounds).ToList();
            Console.WriteLine("Appearance: link routing obstacle count={0}.", ObstacleRectangles.Count);

            var AcceptedRoutes = new List<IList<Point>>();
            var PendingConnectorRenders = new HashSet<VisualConnector>();
            var PendingRepresentationRenders = new HashSet<RelationshipVisualRepresentation>();
            foreach (var Relationship in HiddenCentralRelationships.OrderBy(GetRelationshipSortKey))
            {
                if (Options.RouteSelectedConnectorsOnly)
                    Console.WriteLine("Appearance route relationship: selected connector belongs to hidden-central relationship; routing relationship as a whole: {0}.",
                                      DescribeIdea(Relationship == null ? null : Relationship.RepresentedRelationship));

                RouteHiddenCentralRelationshipModern(Relationship, ObstacleInfos, Options, Result, AcceptedRoutes);
            }

            foreach (var Connector in IndividualConnectors.OrderBy(GetConnectorSortKey))
                RouteConnectorModern(Connector, ObstacleRectangles, Options, Result, AcceptedRoutes,
                                     PendingConnectorRenders, PendingRepresentationRenders);

            foreach (var Representation in PendingRepresentationRenders.OrderBy(GetRelationshipSortKey))
                Representation.Render();
            foreach (var Connector in PendingConnectorRenders
                                      .Where(Connector => Connector.OwnerRelationshipRepresentation == null ||
                                                          !PendingRepresentationRenders.Contains(Connector.OwnerRelationshipRepresentation))
                                      .OrderBy(GetConnectorSortKey))
                Connector.RenderElement();

            LogSummary(Result);
            return Result;
        }

        public static IList<Rect> BuildObstacleRectangles(LayoutSelectionContext Context, LinkObstacleRoutingOptions Options)
        {
            return BuildObstacleInfos(Context, Options).Select(Obstacle => Obstacle.Bounds).ToList();
        }

        private static IList<ObstacleInfo> BuildObstacleInfos(LayoutSelectionContext Context, LinkObstacleRoutingOptions Options)
        {
            Options = Options ?? new LinkObstacleRoutingOptions();
            var Obstacles = new List<ObstacleInfo>();

            if (Context == null)
                return Obstacles;

            foreach (var Symbol in Context.VisibleConceptSymbols)
            {
                if (Symbol == null || Symbol.IsHidden || !Symbol.IsRelatedVisible)
                    continue;

                var Area = Symbol.TotalArea;
                if (!IsUsableRect(Area))
                    continue;

                Area.Inflate(Options.ObstaclePadding, Options.ObstaclePadding);
                Obstacles.Add(new ObstacleInfo { Symbol = Symbol, Bounds = Area });
            }

            if (Options.IncludeRelationshipCentralSymbolsAsObstacles)
                foreach (var Symbol in Context.VisibleRelationshipRepresentations
                                      .Where(Representation => Representation != null &&
                                                               !IsHiddenCentralRelationship(Representation))
                                      .Select(Representation => Representation.MainSymbol)
                                      .Where(Symbol => Symbol != null && !Symbol.IsHidden && Symbol.IsRelatedVisible))
                {
                    var Area = Symbol.TotalArea;
                    if (!IsUsableRect(Area))
                        continue;

                    Area.Inflate(Options.ObstaclePadding, Options.ObstaclePadding);
                    Obstacles.Add(new ObstacleInfo { Symbol = Symbol, Bounds = Area });
                }

            return Obstacles;
        }

        private static IList<RelationshipVisualRepresentation> GetHiddenCentralRelationshipsForScope(IEnumerable<VisualConnector> Connectors)
        {
            return (Connectors ?? Enumerable.Empty<VisualConnector>())
                .Where(Connector => Connector != null)
                .Select(Connector => Connector.OwnerRelationshipRepresentation)
                .Where(IsHiddenCentralRelationship)
                .Distinct()
                .ToList();
        }

        private static void RouteHiddenCentralRelationshipModern(RelationshipVisualRepresentation Representation,
                                                                 IEnumerable<ObstacleInfo> Obstacles,
                                                                 LinkObstacleRoutingOptions Options,
                                                                 LinkObstacleRoutingResult Result,
                                                                 IList<IList<Point>> AcceptedRoutes)
        {
            Options = Options ?? new LinkObstacleRoutingOptions();
            Result = Result ?? new LinkObstacleRoutingResult();
            Result.Inspected++;
            Result.RelationshipRoutesInspected++;

            if (Representation == null || Representation.MainSymbol == null)
            {
                Result.Skipped++;
                Result.AddWarning("A hidden relationship has no central symbol and cannot be routed.");
                return;
            }

            var Main = Representation.MainSymbol;
            var Bindings = GetHiddenEndpointBindings(Representation)
                .OrderBy(Binding => GetSymbolSortKey(Binding.EndpointSymbol))
                .ThenBy(Binding => GetConnectorSortKey(Binding.Connector))
                .ToList();
            if (Bindings.Count < 2)
            {
                Result.Skipped++;
                Result.AddWarning("Hidden relationship '" + GetRelationshipSortKey(Representation) +
                                  "' has fewer than two visible endpoint connectors.");
                return;
            }

            var FilteredObstacles = (Obstacles ?? Enumerable.Empty<ObstacleInfo>())
                .Where(Obstacle => Obstacle != null && IsUsableRect(Obstacle.Bounds) &&
                                   Obstacle.Symbol != Main &&
                                   !Bindings.Any(Binding =>
                                       Binding.EndpointSymbol == Obstacle.Symbol ||
                                       Obstacle.Bounds.Contains(Binding.EndpointSymbol.BaseCenter)))
                .Select(Obstacle => Obstacle.Bounds)
                .ToList();

            if (Bindings.Count == 2 && IsGenuinelySimpleRelationship(Representation))
            {
                if (Bindings[0].EndpointSymbol == Bindings[1].EndpointSymbol)
                    RouteHiddenSelfRelationship(Representation, Bindings[0], Bindings[1], FilteredObstacles,
                                                Options, Result, AcceptedRoutes);
                else
                    RouteHiddenBinaryRelationship(Representation, Bindings[0], Bindings[1], FilteredObstacles,
                                                  Options, Result, AcceptedRoutes);
                return;
            }

            // Hidden n-ary relationships are routed as a star. Coordinate medians keep a
            // remote outlier from pulling the junction away from the endpoint cloud.
            var XValues = Bindings.Select(Binding => Binding.EndpointSymbol.BaseCenter.X).OrderBy(Value => Value).ToList();
            var YValues = Bindings.Select(Binding => Binding.EndpointSymbol.BaseCenter.Y).OrderBy(Value => Value).ToList();
            var NewCenter = new Point(Median(XValues), Median(YValues));
            if (!PointsEqual(Main.BaseCenter, NewCenter))
                Main.MoveTo(NewCenter.X, NewCenter.Y, true);

            var RoutedAny = false;
            foreach (var Binding in Bindings)
            {
                var Source = GetBindingEndpoint(Binding, true);
                var Target = GetBindingEndpoint(Binding, false);
                var Existing = GetBindingRouteFromEndpointToHub(Binding);
                var Request = CreatePlannerRequest(GetConnectorSortKey(Binding.Connector), Source, Target, Existing,
                                                   FilteredObstacles, Options, AcceptedRoutes,
                                                   Options.PreserveExistingValidRoutes
                                                   ? Options.RouteIntent : RelationshipRouteIntent.Layout);
                ApplyMandatoryWaypoints(Request, Options, Representation, Existing);
                Request.RemainingBatchWork = Math.Max(0, Options.MaximumBatchWork - Result.TotalWork);
                var Plan = OrthogonalRoutePlanner.Plan(Request);
                ApplyBindingPlannerResult(Binding, Existing, Source, Target, Plan, Result, AcceptedRoutes);
                RoutedAny = RoutedAny || Plan.Status != RelationshipRouteStatus.Preserved;
            }

            Representation.Render();
            if (RoutedAny)
                Result.DoglegRouted++;
        }

        private static void RouteHiddenSelfRelationship(RelationshipVisualRepresentation Representation,
                                                        HiddenEndpointBinding FirstBinding,
                                                        HiddenEndpointBinding SecondBinding,
                                                        IList<Rect> Obstacles,
                                                        LinkObstacleRoutingOptions Options,
                                                        LinkObstacleRoutingResult Result,
                                                        IList<IList<Point>> AcceptedRoutes)
        {
            var Main = Representation.MainSymbol;
            var Endpoint = FirstBinding.EndpointSymbol;
            var FirstExisting = GetBindingRouteFromEndpointToHub(FirstBinding).ToList();
            var SecondExisting = GetBindingRouteFromEndpointToHub(SecondBinding).ToList();
            var HubCenter = ChooseHiddenSelfRelationshipCenter(Main, Endpoint, Obstacles, Options);
            if (!PointsEqual(Main.BaseCenter, HubCenter))
                Main.MoveTo(HubCenter.X, HubCenter.Y, true);

            var EndpointBounds = Endpoint.TotalArea;
            var HubBounds = Main.TotalArea;
            var Combined = EndpointBounds;
            Combined.Union(HubBounds);
            var Clearance = Math.Max(24.0, Options.ObstaclePadding + Options.NearMissPadding + 4.0);
            var HorizontalLoop = Math.Abs(HubCenter.X - Endpoint.BaseCenter.X) >=
                                 Math.Abs(HubCenter.Y - Endpoint.BaseCenter.Y);

            var FirstSource = GetBindingEndpoint(FirstBinding, true);
            var FirstTarget = GetBindingEndpoint(FirstBinding, false);
            var SecondSource = GetBindingEndpoint(SecondBinding, true);
            var SecondTarget = GetBindingEndpoint(SecondBinding, false);
            var FirstWaypoints = HorizontalLoop
                                 ? new List<Point>
                                   {
                                       new Point(FirstSource.X, Combined.Top - Clearance),
                                       new Point(FirstTarget.X, Combined.Top - Clearance)
                                   }
                                 : new List<Point>
                                   {
                                       new Point(Combined.Left - Clearance, FirstSource.Y),
                                       new Point(Combined.Left - Clearance, FirstTarget.Y)
                                   };
            var SecondWaypoints = HorizontalLoop
                                  ? new List<Point>
                                    {
                                        new Point(SecondSource.X, Combined.Bottom + Clearance),
                                        new Point(SecondTarget.X, Combined.Bottom + Clearance)
                                    }
                                  : new List<Point>
                                    {
                                        new Point(Combined.Right + Clearance, SecondSource.Y),
                                        new Point(Combined.Right + Clearance, SecondTarget.Y)
                                    };

            var FirstRequest = CreatePlannerRequest(GetConnectorSortKey(FirstBinding.Connector),
                                                    FirstSource, FirstTarget, FirstExisting,
                                                    Obstacles, Options, AcceptedRoutes,
                                                    RelationshipRouteIntent.Layout);
            FirstRequest.MandatoryWaypoints = FirstWaypoints;
            FirstRequest.DirtyReason = Options.DirtyReason.ToStringAlways() +
                                       (String.IsNullOrWhiteSpace(Options.DirtyReason) ? "" : "; ") +
                                       "deterministic self-reference upper/left corridor";
            FirstRequest.RemainingBatchWork = Math.Max(0, Options.MaximumBatchWork - Result.TotalWork);
            var FirstPlan = OrthogonalRoutePlanner.Plan(FirstRequest);
            ApplyBindingPlannerResult(FirstBinding, FirstExisting, FirstSource, FirstTarget,
                                      FirstPlan, Result, AcceptedRoutes);

            var SecondRequest = CreatePlannerRequest(GetConnectorSortKey(SecondBinding.Connector),
                                                     SecondSource, SecondTarget, SecondExisting,
                                                     Obstacles, Options, AcceptedRoutes,
                                                     RelationshipRouteIntent.Layout);
            SecondRequest.MandatoryWaypoints = SecondWaypoints;
            SecondRequest.DirtyReason = Options.DirtyReason.ToStringAlways() +
                                        (String.IsNullOrWhiteSpace(Options.DirtyReason) ? "" : "; ") +
                                        "deterministic self-reference lower/right corridor";
            SecondRequest.RemainingBatchWork = Math.Max(0, Options.MaximumBatchWork - Result.TotalWork);
            var SecondPlan = OrthogonalRoutePlanner.Plan(SecondRequest);
            ApplyBindingPlannerResult(SecondBinding, SecondExisting, SecondSource, SecondTarget,
                                      SecondPlan, Result, AcceptedRoutes);

            Representation.Render();
            Result.DoglegRouted++;
        }

        private static Point ChooseHiddenSelfRelationshipCenter(VisualSymbol Main, VisualSymbol Endpoint,
                                                                IList<Rect> Obstacles,
                                                                LinkObstacleRoutingOptions Options)
        {
            var EndpointBounds = Endpoint.TotalArea;
            var MainBounds = Main.TotalArea;
            var Width = IsUsableRect(MainBounds) ? MainBounds.Width : 20.0;
            var Height = IsUsableRect(MainBounds) ? MainBounds.Height : 20.0;
            var Clearance = Math.Max(48.0, Options.ObstaclePadding * 2.0 + Options.NearMissPadding + 12.0);
            var PlacementOptions = Options.RelationshipVisualPlacementOptions ??
                                   new RelationshipVisualPlacementOptions();
            var LocalCorridor = EndpointBounds;
            LocalCorridor.Inflate(PlacementOptions.CorridorPaddingX, PlacementOptions.CorridorPaddingY);
            var CurrentExclusion = EndpointBounds;
            CurrentExclusion.Inflate(Clearance / 2.0, Clearance / 2.0);
            var CurrentBounds = new Rect(Main.BaseCenter.X - Width / 2.0,
                                         Main.BaseCenter.Y - Height / 2.0, Width, Height);
            var MaximumLocalDistance = Math.Max(250.0,
                Options.RelationshipVisualPlacementOptions == null
                ? 700.0 : Options.RelationshipVisualPlacementOptions.SuspiciousDistanceThreshold);
            if (!CurrentExclusion.Contains(Main.BaseCenter) &&
                LocalCorridor.Contains(Main.BaseCenter) &&
                Distance(Main.BaseCenter, Endpoint.BaseCenter) <= MaximumLocalDistance &&
                !(Obstacles ?? new List<Rect>()).Any(Obstacle => Obstacle.IntersectsWith(CurrentBounds)))
                return Main.BaseCenter;

            // Keep the hidden junction inside the same inflated endpoint corridor used by
            // routing validation.  The previous fixed 48-pixel gap plus half the hub width
            // could place an 84-pixel hub just beyond the default 80-pixel corridor.
            var HorizontalOffset = Math.Max(1.0,
                Math.Min(Clearance + Width / 2.0,
                         Math.Max(1.0, PlacementOptions.CorridorPaddingX - 1.0)));
            var VerticalOffset = Math.Max(1.0,
                Math.Min(Clearance + Height / 2.0,
                         Math.Max(1.0, PlacementOptions.CorridorPaddingY - 1.0)));
            var Candidates = new[]
            {
                new Point(EndpointBounds.Right + HorizontalOffset, Endpoint.BaseCenter.Y),
                new Point(Endpoint.BaseCenter.X, EndpointBounds.Bottom + VerticalOffset),
                new Point(EndpointBounds.Left - HorizontalOffset, Endpoint.BaseCenter.Y),
                new Point(Endpoint.BaseCenter.X, EndpointBounds.Top - VerticalOffset)
            };
            return Candidates.Select((Center, Index) => new
                              {
                                  Center,
                                  Index,
                                  Bounds = new Rect(Center.X - Width / 2.0, Center.Y - Height / 2.0,
                                                    Width, Height)
                              })
                             .OrderBy(Item => (Obstacles ?? new List<Rect>())
                                                  .Count(Obstacle => Obstacle.IntersectsWith(Item.Bounds)))
                             .ThenBy(Item => Item.Index)
                             .Select(Item => Item.Center)
                             .First();
        }

        private static void RouteHiddenBinaryRelationship(RelationshipVisualRepresentation Representation,
                                                          HiddenEndpointBinding SourceBinding,
                                                          HiddenEndpointBinding TargetBinding,
                                                          IList<Rect> Obstacles,
                                                          LinkObstacleRoutingOptions Options,
                                                          LinkObstacleRoutingResult Result,
                                                          IList<IList<Point>> AcceptedRoutes)
        {
            var Source = GetBindingEndpoint(SourceBinding, true);
            var Target = GetBindingEndpoint(TargetBinding, true);
            var ExistingComplete = new List<Point> { Source };
            ExistingComplete.AddRange(GetBindingRouteFromEndpointToHub(SourceBinding));
            ExistingComplete.Add(Representation.MainSymbol.BaseCenter);
            ExistingComplete.AddRange(GetBindingRouteFromEndpointToHub(TargetBinding).Reverse());
            ExistingComplete.Add(Target);
            ExistingComplete = OrthogonalRoutePlanner.Simplify(ExistingComplete).ToList();
            var Existing = ExistingComplete.Count <= 2
                           ? new List<Point>()
                           : ExistingComplete.Skip(1).Take(ExistingComplete.Count - 2).ToList();

            var Request = CreatePlannerRequest(GetRelationshipSortKey(Representation), Source, Target, Existing,
                                               Obstacles, Options, AcceptedRoutes,
                                               Options.PreserveExistingValidRoutes
                                               ? Options.RouteIntent : RelationshipRouteIntent.Layout);
            ApplyMandatoryWaypoints(Request, Options, Representation, Existing);
            Request.RemainingBatchWork = Math.Max(0, Options.MaximumBatchWork - Result.TotalWork);
            var Plan = OrthogonalRoutePlanner.Plan(Request);
            var Complete = new List<Point> { Source };
            Complete.AddRange(Plan.RoutePoints ?? new List<Point>());
            Complete.Add(Target);
            Complete = OrthogonalRoutePlanner.Simplify(Complete).ToList();

            Point Center;
            IList<Point> SourceInterior;
            IList<Point> TargetInterior;
            var HubCorridor = SourceBinding.EndpointSymbol.TotalArea;
            HubCorridor.Union(TargetBinding.EndpointSymbol.TotalArea);
            var PlacementOptions = Options.RelationshipVisualPlacementOptions ??
                                   new RelationshipVisualPlacementOptions();
            HubCorridor.Inflate(PlacementOptions.CorridorPaddingX, PlacementOptions.CorridorPaddingY);
            var PreferredCenter = GetMidpoint(SourceBinding.EndpointSymbol.BaseCenter,
                                              TargetBinding.EndpointSymbol.BaseCenter);
            SplitAtPreferredCorridorPoint(Complete, PreferredCenter, HubCorridor,
                                          out Center, out SourceInterior, out TargetInterior);
            Representation.MainSymbol.MoveTo(Center.X, Center.Y, true);
            SetBindingRouteFromEndpointToHub(SourceBinding, SourceInterior);
            SetBindingRouteFromEndpointToHub(TargetBinding, TargetInterior.Reverse().ToList());
            var AppearanceChanged = false;
            if (Plan.Status != RelationshipRouteStatus.Preserved)
            {
                var SourceAppearanceChanged = SetAutomaticPathAppearance(SourceBinding.Connector);
                var TargetAppearanceChanged = SetAutomaticPathAppearance(TargetBinding.Connector);
                Result.AppearanceChanged += (SourceAppearanceChanged ? 1 : 0) +
                                            (TargetAppearanceChanged ? 1 : 0);
                AppearanceChanged = SourceAppearanceChanged || TargetAppearanceChanged;
            }
            Representation.Render();

            Result.DoglegRouted++;
            RecordPlan(Result, Plan, Existing, Plan.RoutePoints, AppearanceChanged);
            var Accepted = new List<Point> { Source };
            Accepted.AddRange(Plan.RoutePoints ?? new List<Point>());
            Accepted.Add(Target);
            AcceptedRoutes.Add(Accepted);
        }

        private static IList<HiddenEndpointBinding> GetHiddenEndpointBindings(RelationshipVisualRepresentation Representation)
        {
            var Result = new List<HiddenEndpointBinding>();
            if (Representation == null || Representation.MainSymbol == null || Representation.VisualConnectors == null)
                return Result;

            var Main = Representation.MainSymbol;
            foreach (var Connector in Representation.VisualConnectors.Where(Connector => Connector != null).Distinct())
            {
                if (Connector.OriginSymbol == Main && IsVisibleConceptSymbol(Connector.TargetSymbol))
                    Result.Add(new HiddenEndpointBinding
                    {
                        Connector = Connector,
                        EndpointSymbol = Connector.TargetSymbol,
                        EndpointIsOrigin = false
                    });
                else if (Connector.TargetSymbol == Main && IsVisibleConceptSymbol(Connector.OriginSymbol))
                    Result.Add(new HiddenEndpointBinding
                    {
                        Connector = Connector,
                        EndpointSymbol = Connector.OriginSymbol,
                        EndpointIsOrigin = true
                    });
            }
            return Result;
        }

        private static Point GetBindingEndpoint(HiddenEndpointBinding Binding, bool EndpointSide)
        {
            var UseOrigin = EndpointSide ? Binding.EndpointIsOrigin : !Binding.EndpointIsOrigin;
            return GetEndpoint(Binding.Connector, UseOrigin);
        }

        private static IList<Point> GetBindingRouteFromEndpointToHub(HiddenEndpointBinding Binding)
        {
            var Points = GetRoutePoints(Binding == null ? null : Binding.Connector);
            return Binding != null && Binding.EndpointIsOrigin ? Points : Points.Reverse().ToList();
        }

        private static void SetBindingRouteFromEndpointToHub(HiddenEndpointBinding Binding, IEnumerable<Point> Points)
        {
            if (Binding == null || Binding.Connector == null)
                return;
            var Route = (Points ?? Enumerable.Empty<Point>()).ToList();
            Binding.Connector.SetRoutePoints(Binding.EndpointIsOrigin ? Route : Route.AsEnumerable().Reverse());
        }

        private static void SplitAtPathMidpoint(IList<Point> Complete, out Point Center,
                                                out IList<Point> SourceInterior, out IList<Point> TargetInterior)
        {
            SourceInterior = new List<Point>();
            TargetInterior = new List<Point>();
            if (Complete == null || Complete.Count < 2)
            {
                Center = new Point(0, 0);
                return;
            }

            var Total = 0.0;
            for (var Index = 0; Index < Complete.Count - 1; Index++)
                Total += Distance(Complete[Index], Complete[Index + 1]);
            var Half = Total / 2.0;
            var Travelled = 0.0;
            var CenterIndex = 1;
            var Expanded = Complete.ToList();
            Center = GetMidpoint(Complete[0], Complete[Complete.Count - 1]);
            for (var Index = 0; Index < Complete.Count - 1; Index++)
            {
                var Segment = Distance(Complete[Index], Complete[Index + 1]);
                if (Travelled + Segment + GeometryTolerance < Half)
                {
                    Travelled += Segment;
                    continue;
                }

                var Fraction = Segment <= GeometryTolerance ? 0.0 : (Half - Travelled) / Segment;
                Center = new Point(Complete[Index].X + (Complete[Index + 1].X - Complete[Index].X) * Fraction,
                                   Complete[Index].Y + (Complete[Index + 1].Y - Complete[Index].Y) * Fraction);
                if (PointsEqual(Center, Complete[Index]))
                    CenterIndex = Index;
                else if (PointsEqual(Center, Complete[Index + 1]))
                    CenterIndex = Index + 1;
                else
                {
                    CenterIndex = Index + 1;
                    Expanded.Insert(CenterIndex, Center);
                }
                break;
            }

            SourceInterior = Expanded.Skip(1).Take(Math.Max(0, CenterIndex - 1)).ToList();
            TargetInterior = Expanded.Skip(CenterIndex + 1).Take(Math.Max(0, Expanded.Count - CenterIndex - 2)).ToList();
        }

        private static void SplitAtPreferredCorridorPoint(IList<Point> Complete, Point Preferred, Rect Corridor,
                                                          out Point Center,
                                                          out IList<Point> SourceInterior,
                                                          out IList<Point> TargetInterior)
        {
            if (Complete == null || Complete.Count < 2 || !IsUsableRect(Corridor))
            {
                SplitAtPathMidpoint(Complete, out Center, out SourceInterior, out TargetInterior);
                return;
            }

            // Rect.Contains excludes the right/bottom boundary.  Splitting exactly on one
            // of those edges therefore produces a hub which validation reports as outside
            // the endpoint corridor even though clipping accepted it.  Search a small,
            // scale-independent inset so the chosen junction is unambiguously local while
            // retaining essentially the full corridor on compact diagrams.
            var EffectiveCorridor = Corridor;
            var InsetX = Math.Min(1.0, Math.Max(0.0, Corridor.Width / 4.0));
            var InsetY = Math.Min(1.0, Math.Max(0.0, Corridor.Height / 4.0));
            EffectiveCorridor.Inflate(-InsetX, -InsetY);
            if (!IsUsableRect(EffectiveCorridor))
                EffectiveCorridor = Corridor;

            var BestIndex = -1;
            var BestT = 0.0;
            var BestScore = Double.PositiveInfinity;
            for (var Index = 0; Index < Complete.Count - 1; Index++)
            {
                double MinimumT;
                double MaximumT;
                if (!TryClipSegmentToRect(Complete[Index], Complete[Index + 1], EffectiveCorridor,
                                          out MinimumT, out MaximumT))
                    continue;

                var Start = Complete[Index];
                var End = Complete[Index + 1];
                var DX = End.X - Start.X;
                var DY = End.Y - Start.Y;
                var LengthSquared = DX * DX + DY * DY;
                if (LengthSquared <= GeometryTolerance)
                    continue;
                var T = ((Preferred.X - Start.X) * DX + (Preferred.Y - Start.Y) * DY) / LengthSquared;
                T = Math.Max(MinimumT, Math.Min(MaximumT, T));
                // Do not collapse the hidden junction onto an endpoint when the first/last
                // segment has a usable portion inside the corridor.  Prefer the corridor-side
                // exit/entry point so both connector legs retain a nonzero anchor segment.
                if (Index == 0 && T <= GeometryTolerance && MaximumT > GeometryTolerance)
                    T = MaximumT;
                if (Index == Complete.Count - 2 && T >= 1.0 - GeometryTolerance &&
                    MinimumT < 1.0 - GeometryTolerance)
                    T = MinimumT;
                var Candidate = new Point(Start.X + DX * T, Start.Y + DY * T);
                var EndpointPenalty = (Index == 0 && T <= GeometryTolerance) ||
                                      (Index == Complete.Count - 2 && T >= 1.0 - GeometryTolerance)
                                      ? 1000000.0 : 0.0;
                var Score = Distance(Candidate, Preferred) + EndpointPenalty;
                if (Score + GeometryTolerance < BestScore)
                {
                    BestScore = Score;
                    BestIndex = Index;
                    BestT = T;
                }
            }

            if (BestIndex < 0)
            {
                SplitAtPathMidpoint(Complete, out Center, out SourceInterior, out TargetInterior);
                return;
            }

            var SegmentStart = Complete[BestIndex];
            var SegmentEnd = Complete[BestIndex + 1];
            Center = new Point(SegmentStart.X + (SegmentEnd.X - SegmentStart.X) * BestT,
                               SegmentStart.Y + (SegmentEnd.Y - SegmentStart.Y) * BestT);
            var Expanded = Complete.ToList();
            int CenterIndex;
            if (PointsEqual(Center, SegmentStart))
                CenterIndex = BestIndex;
            else if (PointsEqual(Center, SegmentEnd))
                CenterIndex = BestIndex + 1;
            else
            {
                CenterIndex = BestIndex + 1;
                Expanded.Insert(CenterIndex, Center);
            }
            SourceInterior = Expanded.Skip(1).Take(Math.Max(0, CenterIndex - 1)).ToList();
            TargetInterior = Expanded.Skip(CenterIndex + 1)
                                     .Take(Math.Max(0, Expanded.Count - CenterIndex - 2)).ToList();
        }

        private static bool TryClipSegmentToRect(Point Start, Point End, Rect Rectangle,
                                                 out double MinimumT, out double MaximumT)
        {
            MinimumT = 0.0;
            MaximumT = 1.0;
            var DX = End.X - Start.X;
            var DY = End.Y - Start.Y;
            return ClipParameter(-DX, Start.X - Rectangle.Left, ref MinimumT, ref MaximumT) &&
                   ClipParameter(DX, Rectangle.Right - Start.X, ref MinimumT, ref MaximumT) &&
                   ClipParameter(-DY, Start.Y - Rectangle.Top, ref MinimumT, ref MaximumT) &&
                   ClipParameter(DY, Rectangle.Bottom - Start.Y, ref MinimumT, ref MaximumT) &&
                   MaximumT + GeometryTolerance >= MinimumT;
        }

        private static bool ClipParameter(double Direction, double Offset,
                                          ref double MinimumT, ref double MaximumT)
        {
            if (Math.Abs(Direction) <= GeometryTolerance)
                return Offset >= -GeometryTolerance;
            var T = Offset / Direction;
            if (Direction < 0.0)
            {
                if (T > MaximumT)
                    return false;
                MinimumT = Math.Max(MinimumT, T);
            }
            else
            {
                if (T < MinimumT)
                    return false;
                MaximumT = Math.Min(MaximumT, T);
            }
            return true;
        }

        private static double Median(IList<double> Values)
        {
            if (Values == null || Values.Count == 0)
                return 0.0;
            var Middle = Values.Count / 2;
            return Values.Count % 2 == 1 ? Values[Middle] : (Values[Middle - 1] + Values[Middle]) / 2.0;
        }

        private static void RouteHiddenCentralRelationship(RelationshipVisualRepresentation Representation, IEnumerable<ObstacleInfo> Obstacles,
                                                           LinkObstacleRoutingOptions Options, LinkObstacleRoutingResult Result)
        {
            Options = Options ?? new LinkObstacleRoutingOptions();
            Result = Result ?? new LinkObstacleRoutingResult();
            Result.Inspected++;
            Result.RelationshipRoutesInspected++;

            string SkipReason;
            HiddenCentralRelationshipRoute Route;
            if (!TryGetHiddenCentralRelationshipRoute(Representation, out Route, out SkipReason))
            {
                Result.Skipped++;
                Result.AddWarning(SkipReason);
                Console.WriteLine("Appearance route relationship: {0}; route=skipped-hidden-center; reason={1}.",
                                  DescribeIdea(Representation == null ? null : Representation.RepresentedRelationship), SkipReason);
                return;
            }

            var Source = Route.SourceSymbol.BaseCenter;
            var Target = Route.TargetSymbol.BaseCenter;
            if (Distance(Source, Target) < Options.MinimumSegmentLength)
            {
                Result.Skipped++;
                SkipReason = "source and target are too close to route safely";
                Result.AddWarning(SkipReason + " for " + DescribeIdea(Representation.RepresentedRelationship) + ".");
                Console.WriteLine("Appearance route relationship: {0}; route=skipped-hidden-center; reason={1}.",
                                  DescribeHiddenCentralRoute(Route), SkipReason);
                return;
            }

            var FilteredObstacleInfos = (Obstacles ?? Enumerable.Empty<ObstacleInfo>())
                .Where(Obstacle => Obstacle != null &&
                                   IsUsableRect(Obstacle.Bounds) &&
                                   Obstacle.Symbol != Route.SourceSymbol &&
                                   Obstacle.Symbol != Route.TargetSymbol &&
                                   Obstacle.Symbol != Route.MainSymbol)
                .ToList();
            var OldCenter = Route.MainSymbol.BaseCenter;
            var Existing = BuildHiddenExistingCandidate(Route, Source, Target, OldCenter, FilteredObstacleInfos, Options);
            var Candidates = BuildHiddenCandidates(Source, Target, OldCenter, FilteredObstacleInfos, Options);
            var ValidCandidates = Candidates.Where(Candidate => Candidate.IsValid && !Candidate.IsDogleg).OrderBy(Candidate => Candidate.Score).ToList();
            var ValidDoglegCandidates = Candidates.Where(Candidate => Candidate.IsValid && Candidate.IsDogleg).OrderBy(Candidate => Candidate.Score).ToList();
            LogHiddenCenterCandidateDiagnostics(Route, Source, Target, OldCenter, FilteredObstacleInfos, Existing, Candidates);

            if (Existing != null && Existing.IsValid && IsExistingRoutePreservable(Existing, Source, Target))
            {
                var BestAlternative = ValidCandidates.Concat(ValidDoglegCandidates).FirstOrDefault(Candidate => !RouteEquivalent(Candidate, Existing));
                if (Options.PreserveExistingValidRoutes &&
                    (BestAlternative == null ||
                     Existing.Score <= BestAlternative.Score + Options.MinimumRouteImprovement))
                {
                    Result.Unchanged++;
                    Console.WriteLine("Appearance route relationship: {0}; oldHiddenCenter={1}; route=unchanged-hidden-center; score={2:0.###}; connectorSegments={3}.",
                                      DescribeHiddenCentralRoute(Route), FormatPoint(OldCenter), Existing.Score,
                                      Route.Connectors.Count());
                    return;
                }
            }

            var Best = ValidCandidates.FirstOrDefault();
            if (Best == null)
                Best = ValidDoglegCandidates.FirstOrDefault();

            if (Best == null)
            {
                Result.Skipped++;
                SkipReason = "no valid straight, single-bend, or dogleg route avoids concept obstacles";
                Result.AddWarning(SkipReason + " for " + DescribeIdea(Representation.RepresentedRelationship) + ".");
                Console.WriteLine("Appearance route relationship: {0}; oldHiddenCenter={1}; route=skipped-hidden-center; reason={2}.",
                                  DescribeHiddenCentralRoute(Route), FormatPoint(OldCenter), SkipReason);
                return;
            }

            var NewCenter = GetHiddenCenterForCandidate(Best, Source, Target);
            if (CandidateMatchesCurrent(Route, Best, OldCenter, Source, Target))
            {
                Result.Unchanged++;
                Console.WriteLine("Appearance route relationship: {0}; oldHiddenCenter={1}; route=unchanged-hidden-center; score={2:0.###}; connectorSegments={3}.",
                                  DescribeHiddenCentralRoute(Route), FormatPoint(OldCenter), Best.Score,
                                  Route.Connectors.Count());
                return;
            }

            if (Best.Intermediate == Display.NULL_POINT)
                Route.MainSymbol.IsAutoPositionable = true;
            else
                Route.MainSymbol.IsAutoPositionable = false;

            Route.MainSymbol.MoveTo(NewCenter.X, NewCenter.Y, Best.Intermediate != Display.NULL_POINT);

            var RenderedSegments = 0;
            if (Best.IsDogleg)
            {
                SetLegacyCandidateRoutePoint(Route.SourceConnector, Best.SourceIntermediate);
                SetLegacyCandidateRoutePoint(Route.TargetConnector, Best.TargetIntermediate);
                RenderedSegments = 2;

                foreach (var Connector in Route.Connectors.Where(Connector => Connector != Route.SourceConnector &&
                                                                              Connector != Route.TargetConnector))
                {
                    Connector.RenderElement();
                    RenderedSegments++;
                }
            }
            else
            {
                foreach (var Connector in Route.Connectors)
                {
                    Connector.ClearRoutePoints();
                    RenderedSegments++;
                }
            }

            Representation.Render();

            if (Best.IsDogleg)
                Result.DoglegRouted++;
            else
            if (Best.Intermediate == Display.NULL_POINT)
                Result.Straightened++;
            else
                Result.Routed++;

            Console.WriteLine("Appearance route relationship: {0}; oldHiddenCenter={1}; chosen={2}-hidden-center; newHiddenCenter={3}; length={4:0.###}; nearMisses={5}; score={6:0.###}; connectorSegmentsRendered={7}.",
                              DescribeHiddenCentralRoute(Route), FormatPoint(OldCenter),
                              FormatHiddenRouteKind(Best.Kind),
                              FormatPoint(NewCenter), Best.Length, Best.NearMisses, Best.Score,
                              RenderedSegments);
        }

        public static void RouteConnector(VisualConnector Connector, IEnumerable<Rect> Obstacles, LinkObstacleRoutingOptions Options, LinkObstacleRoutingResult Result)
        {
            var PendingRenders = new HashSet<VisualConnector>();
            var PendingRepresentations = new HashSet<RelationshipVisualRepresentation>();
            RouteConnectorModern(Connector, Obstacles, Options, Result, new List<IList<Point>>(),
                                 PendingRenders, PendingRepresentations);
            foreach (var Representation in PendingRepresentations)
                Representation.Render();
            foreach (var Pending in PendingRenders.Where(Item => Item.OwnerRelationshipRepresentation == null ||
                                                                  !PendingRepresentations.Contains(Item.OwnerRelationshipRepresentation)))
                Pending.RenderElement();
        }

        private sealed class HiddenEndpointBinding
        {
            public VisualConnector Connector;
            public VisualSymbol EndpointSymbol;
            public bool EndpointIsOrigin;
        }

        private static void RouteConnectorModern(VisualConnector Connector, IEnumerable<Rect> Obstacles,
                                                 LinkObstacleRoutingOptions Options, LinkObstacleRoutingResult Result,
                                                 IList<IList<Point>> AcceptedRoutes,
                                                 ISet<VisualConnector> PendingRenders,
                                                 ISet<RelationshipVisualRepresentation> PendingRepresentationRenders)
        {
            Options = Options ?? new LinkObstacleRoutingOptions();
            Result = Result ?? new LinkObstacleRoutingResult();
            Result.Inspected++;
            Result.ConnectorRoutesInspected++;

            var Description = DescribeConnector(Connector);
            string SkipReason;
            if (!ValidateConnector(Connector, out SkipReason))
            {
                Result.Skipped++;
                Result.AddWarning(SkipReason);
                Console.WriteLine("Appearance route link: {0} -> skipped: {1}", Description, SkipReason);
                return;
            }

            var Source = GetEndpoint(Connector, true);
            var Target = GetEndpoint(Connector, false);
            if (Distance(Source, Target) < Options.MinimumSegmentLength)
            {
                Result.Skipped++;
                SkipReason = "source and target are too close to route safely";
                Result.AddWarning(SkipReason);
                Console.WriteLine("Appearance route link: {0} -> skipped: {1}", Description, SkipReason);
                return;
            }

            var FilteredObstacles = (Obstacles ?? Enumerable.Empty<Rect>())
                .Where(Rectangle => IsUsableRect(Rectangle) &&
                                    !RectangleContainsSymbol(Rectangle, Connector.OriginSymbol) &&
                                    !RectangleContainsSymbol(Rectangle, Connector.TargetSymbol))
                .ToList();

            var ExistingPoints = GetRoutePoints(Connector);
            var Intent = Options.PreserveExistingValidRoutes
                         ? Options.RouteIntent
                         : (Options.RouteIntent == RelationshipRouteIntent.PreserveIfValid
                            ? RelationshipRouteIntent.Layout
                            : Options.RouteIntent);
            var Request = CreatePlannerRequest(GetConnectorSortKey(Connector), Source, Target, ExistingPoints,
                                               FilteredObstacles, Options, AcceptedRoutes, Intent);
            ApplyMandatoryWaypoints(Request, Options, Connector.OwnerRelationshipRepresentation, ExistingPoints);
            Request.RemainingBatchWork = Math.Max(0, Options.MaximumBatchWork - Result.TotalWork);
            var Plan = OrthogonalRoutePlanner.Plan(Request);
            ApplyPlannerResult(Connector, ExistingPoints, Source, Target, Plan, Result, AcceptedRoutes,
                               PendingRenders, PendingRepresentationRenders);
        }

        private static OrthogonalRouteRequest CreatePlannerRequest(string RouteKey, Point Source, Point Target,
                                                                   IList<Point> ExistingPoints, IList<Rect> Obstacles,
                                                                   LinkObstacleRoutingOptions Options,
                                                                   IList<IList<Point>> AcceptedRoutes,
                                                                   RelationshipRouteIntent Intent)
        {
            return new OrthogonalRouteRequest
            {
                RouteKey = RouteKey,
                Source = Source,
                Target = Target,
                ExistingRoutePoints = ExistingPoints ?? new List<Point>(),
                Obstacles = Obstacles ?? new List<Rect>(),
                AcceptedRoutes = AcceptedRoutes ?? new List<IList<Point>>(),
                Intent = Intent,
                DirtyReason = Options.DirtyReason,
                BendCost = Options.BendCost,
                NearMissCost = 50.0,
                CrossingCost = Options.CrossingCost,
                NearMissPadding = Options.NearMissPadding,
                MaximumPreservedDetourRatio = Options.MaximumPreservedDetourRatio,
                TargetMaximumRoutePoints = Options.TargetMaximumRoutePoints,
                HardMaximumRoutePoints = Options.HardMaximumRoutePoints,
                MaximumObstacles = Options.MaximumObstacles,
                MaximumCoordinatesPerAxis = Options.MaximumCoordinatesPerAxis,
                MaximumGridNodes = Options.MaximumGridNodes,
                MaximumDirectionalStates = Options.MaximumDirectionalStates,
                RemainingBatchWork = Math.Max(0, Options.MaximumBatchWork)
            };
        }

        private static void ApplyMandatoryWaypoints(OrthogonalRouteRequest Request,
                                                    LinkObstacleRoutingOptions Options,
                                                    RelationshipVisualRepresentation Representation,
                                                    IEnumerable<Point> ExistingPoints)
        {
            if (Request == null || Options == null || Representation == null ||
                Options.MandatoryWaypointRelationships == null ||
                !Options.MandatoryWaypointRelationships.Contains(Representation))
                return;

            Request.MandatoryWaypoints = (ExistingPoints ?? Enumerable.Empty<Point>())
                                         .Where(IsUsablePoint)
                                         .ToList();
            Request.DirtyReason = Request.DirtyReason.ToStringAlways() +
                                  (String.IsNullOrWhiteSpace(Request.DirtyReason) ? "" : "; ") +
                                  "mandatory relationship corridor";
        }

        private static void ApplyPlannerResult(VisualConnector Connector, IList<Point> Existing,
                                               Point Source, Point Target, OrthogonalRouteResult Plan,
                                               LinkObstacleRoutingResult Result,
                                               IList<IList<Point>> AcceptedRoutes,
                                               ISet<VisualConnector> PendingRenders,
                                               ISet<RelationshipVisualRepresentation> PendingRepresentationRenders)
        {
            var NewPoints = (Plan.RoutePoints ?? new List<Point>()).ToList();
            var AppearanceChanged = false;
            if (RoutesEqual(Existing, NewPoints))
            {
                Result.Unchanged++;
                if (Plan.Status != RelationshipRouteStatus.Preserved)
                {
                    AppearanceChanged = SetAutomaticPathAppearance(Connector);
                    if (AppearanceChanged)
                        Result.AppearanceChanged++;
                    QueueAppearanceRender(Connector, PendingRenders, PendingRepresentationRenders,
                                          AppearanceChanged);
                }
            }
            else
            {
                Connector.SetRoutePoints(NewPoints);
                AppearanceChanged = SetAutomaticPathAppearance(Connector);
                if (AppearanceChanged)
                    Result.AppearanceChanged++;
                if (PendingRenders != null)
                    PendingRenders.Add(Connector);
                QueueAppearanceRender(Connector, PendingRenders, PendingRepresentationRenders,
                                      AppearanceChanged);
                if (NewPoints.Count == 0)
                    Result.Straightened++;
                else
                    Result.Routed++;
            }

            RecordPlan(Result, Plan, Existing, NewPoints, AppearanceChanged);
            var Accepted = new List<Point> { Source };
            Accepted.AddRange(NewPoints);
            Accepted.Add(Target);
            AcceptedRoutes.Add(Accepted);

            Console.WriteLine("Appearance route link: {0}; intent={1}; status={2}; oldPoints={3}; newPoints={4}; bends={5}; length={6:0.###}; detour={7:0.###}; crossings={8}; nearMisses={9}; work={10}; safe={11}.",
                              Plan.RouteKey, Plan.Intent, Plan.Status,
                              Existing == null ? 0 : Existing.Count, NewPoints.Count,
                              Plan.BendCount, Plan.Length, Plan.DetourRatio, Plan.CrossingCount,
                              Plan.NearMissCount, Plan.WorkCount, Plan.IsSafe ? "true" : "false");
        }

        private static void QueueAppearanceRender(VisualConnector Connector,
                                                  ISet<VisualConnector> PendingConnectorRenders,
                                                  ISet<RelationshipVisualRepresentation> PendingRepresentationRenders,
                                                  bool AppearanceChanged)
        {
            if (!AppearanceChanged || Connector == null)
                return;

            // PathStyle and PathCorner are stored in the owning representation's custom format
            // map.  Rendering only the triggering leg would leave sibling legs visually stale.
            var Representation = Connector.OwnerRelationshipRepresentation;
            if (Representation != null && PendingRepresentationRenders != null)
                PendingRepresentationRenders.Add(Representation);
            else if (PendingConnectorRenders != null)
                PendingConnectorRenders.Add(Connector);
        }

        private static void ApplyBindingPlannerResult(HiddenEndpointBinding Binding, IList<Point> Existing,
                                                      Point Source, Point Target, OrthogonalRouteResult Plan,
                                                      LinkObstacleRoutingResult Result,
                                                      IList<IList<Point>> AcceptedRoutes)
        {
            var NewPoints = (Plan.RoutePoints ?? new List<Point>()).ToList();
            var AppearanceChanged = false;
            if (RoutesEqual(Existing, NewPoints))
            {
                Result.Unchanged++;
                if (Plan.Status != RelationshipRouteStatus.Preserved)
                {
                    AppearanceChanged = SetAutomaticPathAppearance(Binding.Connector);
                    if (AppearanceChanged)
                        Result.AppearanceChanged++;
                }
            }
            else
            {
                SetBindingRouteFromEndpointToHub(Binding, NewPoints);
                AppearanceChanged = SetAutomaticPathAppearance(Binding.Connector);
                if (AppearanceChanged)
                    Result.AppearanceChanged++;
                if (NewPoints.Count == 0)
                    Result.Straightened++;
                else
                    Result.Routed++;
            }
            RecordPlan(Result, Plan, Existing, NewPoints, AppearanceChanged);
            var Accepted = new List<Point> { Source };
            Accepted.AddRange(NewPoints);
            Accepted.Add(Target);
            AcceptedRoutes.Add(Accepted);
        }

        private static void RecordPlan(LinkObstacleRoutingResult Result, OrthogonalRouteResult Plan,
                                       IEnumerable<Point> OldPoints, IEnumerable<Point> NewPoints,
                                       bool AppearanceChanged = false)
        {
            var OldPointList = (OldPoints ?? Enumerable.Empty<Point>()).ToList();
            var NewPointList = (NewPoints ?? Enumerable.Empty<Point>()).ToList();
            Result.TotalWork += Plan.WorkCount;
            if (Plan.IsSuspicious)
                Result.SuspiciousRoutes++;
            if (Plan.Status == RelationshipRouteStatus.OuterFallback ||
                Plan.Status == RelationshipRouteStatus.DirectFallback)
                Result.SafeFallbacks++;
            if (Plan.Status == RelationshipRouteStatus.DegradedDirect)
                Result.DegradedFallbacks++;

            foreach (var Warning in Plan.Warnings)
                Result.AddWarning((Plan.RouteKey ?? "route") + ": " + Warning);

            Result.Diagnostics.Add(new RelationshipRouteDiagnostic
            {
                RouteKey = Plan.RouteKey,
                Source = Plan.Source,
                Target = Plan.Target,
                Intent = Plan.Intent,
                DirtyReason = Plan.DirtyReason,
                Status = Plan.Status,
                OldPointCount = OldPointList.Count,
                NewPointCount = NewPointList.Count,
                OldPoints = OldPointList.ToList(),
                NewPoints = NewPointList.ToList(),
                BendCount = Plan.BendCount,
                ObstacleCount = Plan.ObstacleCount,
                GridNodeCount = Plan.GridNodeCount,
                DirectionalStateCount = Plan.DirectionalStateCount,
                WorkCount = Plan.WorkCount,
                CrossingCount = Plan.CrossingCount,
                NearMissCount = Plan.NearMissCount,
                DetourRatio = Plan.DetourRatio,
                HitObstacleCap = Plan.HitObstacleCap,
                HitCoordinateCap = Plan.HitCoordinateCap,
                HitGridNodeCap = Plan.HitGridNodeCap,
                HitStateCap = Plan.HitStateCap,
                HitBatchWorkCap = Plan.HitBatchWorkCap,
                AppearanceChanged = AppearanceChanged,
                UsedFallback = Plan.Status == RelationshipRouteStatus.OuterFallback ||
                               Plan.Status == RelationshipRouteStatus.DirectFallback ||
                               Plan.Status == RelationshipRouteStatus.DegradedDirect,
                IsSuspicious = Plan.IsSuspicious,
                IsSafe = Plan.IsSafe,
                Message = Plan.Warnings.Count == 0 ? null : String.Join(" ", Plan.Warnings.ToArray())
            });
        }

        private static IList<Point> GetRoutePoints(VisualConnector Connector)
        {
            return Connector == null || Connector.RoutePoints == null
                   ? new List<Point>()
                   : Connector.RoutePoints.ToList();
        }

        private static bool RoutesEqual(IList<Point> First, IList<Point> Second)
        {
            First = First ?? new List<Point>();
            Second = Second ?? new List<Point>();
            if (First.Count != Second.Count)
                return false;
            for (var Index = 0; Index < First.Count; Index++)
                if (!PointsEqual(First[Index], Second[Index]))
                    return false;
            return true;
        }

        private static bool SetAutomaticPathAppearance(VisualConnector Connector)
        {
            if (Connector == null)
                return false;
            var Changed = VisualConnectorsFormat.GetPathStyle(Connector) != EPathStyle.MultilineRightAngled ||
                          VisualConnectorsFormat.GetPathCorner(Connector) != EPathCorner.Rounded;
            if (!Changed)
                return false;
            VisualConnectorsFormat.SetPathStyle(Connector, EPathStyle.MultilineRightAngled);
            VisualConnectorsFormat.SetPathCorner(Connector, EPathCorner.Rounded);
            return true;
        }

        private static string GetRelationshipSortKey(RelationshipVisualRepresentation Representation)
        {
            var Relationship = Representation == null ? null : Representation.RepresentedRelationship;
            return Relationship == null
                   ? "~:" + (Representation == null ? "~" : Representation.GlobalId.ToString("D"))
                   : Relationship.GlobalId.ToString("D") + ":" + Relationship.TechName.ToStringAlways() + ":" +
                     Representation.GlobalId.ToString("D");
        }

        private static string GetConnectorSortKey(VisualConnector Connector)
        {
            if (Connector == null)
                return "~";
            return GetRelationshipSortKey(Connector.OwnerRelationshipRepresentation) + ":" +
                   GetSymbolSortKey(Connector.OriginSymbol) + "->" + GetSymbolSortKey(Connector.TargetSymbol) + ":" +
                   (Connector.RepresentedLink == null
                    ? "~" : Connector.RepresentedLink.GlobalId.ToString("D")) + ":" +
                   Connector.GlobalId.ToString("D");
        }

        private static string GetSymbolSortKey(VisualSymbol Symbol)
        {
            var Idea = Symbol == null || Symbol.OwnerRepresentation == null
                       ? null : Symbol.OwnerRepresentation.RepresentedIdea;
            return Idea == null
                   ? "~"
                   : Idea.GlobalId.ToString("D") + ":" + Idea.TechName.ToStringAlways();
        }

        public static bool SegmentIntersectsObstacle(Point Start, Point End, Rect Obstacle)
        {
            if (!IsUsablePoint(Start) || !IsUsablePoint(End) || !IsUsableRect(Obstacle))
                return false;

            if (Obstacle.Contains(Start) || Obstacle.Contains(End))
                return true;

            var SegmentBounds = new Rect(Start, End);
            SegmentBounds.Inflate(GeometryTolerance, GeometryTolerance);
            if (!SegmentBounds.IntersectsWith(Obstacle))
                return false;

            return SegmentIntersectsSegment(Start, End, Obstacle.TopLeft, Obstacle.TopRight) ||
                   SegmentIntersectsSegment(Start, End, Obstacle.TopRight, Obstacle.BottomRight) ||
                   SegmentIntersectsSegment(Start, End, Obstacle.BottomRight, Obstacle.BottomLeft) ||
                   SegmentIntersectsSegment(Start, End, Obstacle.BottomLeft, Obstacle.TopLeft);
        }

        private static List<RouteCandidate> BuildCandidates(Point Source, Point Target, Point ExistingIntermediate,
                                                            IList<Rect> Obstacles, LinkObstacleRoutingOptions Options)
        {
            var Candidates = new List<RouteCandidate>();
            Candidates.Add(CreateCandidate(RouteKind.Straight, Display.NULL_POINT, Source, Target, Obstacles, Options, ExistingIntermediate));

            var HorizontalFirst = new Point(Target.X, Source.Y);
            if (Distance(Source, HorizontalFirst) >= Options.MinimumSegmentLength &&
                Distance(HorizontalFirst, Target) >= Options.MinimumSegmentLength)
                Candidates.Add(CreateCandidate(RouteKind.HorizontalFirst, HorizontalFirst, Source, Target, Obstacles, Options, ExistingIntermediate));

            var VerticalFirst = new Point(Source.X, Target.Y);
            if (Distance(Source, VerticalFirst) >= Options.MinimumSegmentLength &&
                Distance(VerticalFirst, Target) >= Options.MinimumSegmentLength)
                Candidates.Add(CreateCandidate(RouteKind.VerticalFirst, VerticalFirst, Source, Target, Obstacles, Options, ExistingIntermediate));

            return Candidates;
        }

        private static RouteCandidate BuildExistingCandidate(Point Source, Point Target, Point ExistingIntermediate,
                                                             IList<Rect> Obstacles, LinkObstacleRoutingOptions Options)
        {
            if (ExistingIntermediate == Display.NULL_POINT)
                return CreateCandidate(RouteKind.Straight, Display.NULL_POINT, Source, Target, Obstacles, Options, ExistingIntermediate, true);

            return CreateCandidate(RouteKind.Existing, ExistingIntermediate, Source, Target, Obstacles, Options, ExistingIntermediate, true);
        }

        private static List<RouteCandidate> BuildHiddenCandidates(Point Source, Point Target, Point ExistingIntermediate,
                                                                  IList<ObstacleInfo> Obstacles, LinkObstacleRoutingOptions Options)
        {
            var Candidates = new List<RouteCandidate>();
            Candidates.Add(CreateHiddenCandidate(RouteKind.Straight, Display.NULL_POINT, Source, Target, Obstacles, Options, ExistingIntermediate));

            var HorizontalFirst = new Point(Target.X, Source.Y);
            if (Distance(Source, HorizontalFirst) >= Options.MinimumSegmentLength &&
                Distance(HorizontalFirst, Target) >= Options.MinimumSegmentLength)
                Candidates.Add(CreateHiddenCandidate(RouteKind.HorizontalFirst, HorizontalFirst, Source, Target, Obstacles, Options, ExistingIntermediate));

            var VerticalFirst = new Point(Source.X, Target.Y);
            if (Distance(Source, VerticalFirst) >= Options.MinimumSegmentLength &&
                Distance(VerticalFirst, Target) >= Options.MinimumSegmentLength)
                Candidates.Add(CreateHiddenCandidate(RouteKind.VerticalFirst, VerticalFirst, Source, Target, Obstacles, Options, ExistingIntermediate));

            foreach (var BusY in BuildHorizontalDoglegBusCandidates(Source, Target, Obstacles, Options))
                Candidates.Add(CreateHiddenDoglegCandidate(RouteKind.HorizontalDogleg, BusY, Source, Target, Obstacles, Options, ExistingIntermediate));

            foreach (var BusX in BuildVerticalDoglegBusCandidates(Source, Target, Obstacles, Options))
                Candidates.Add(CreateHiddenDoglegCandidate(RouteKind.VerticalDogleg, BusX, Source, Target, Obstacles, Options, ExistingIntermediate));

            return Candidates;
        }

        private static RouteCandidate BuildHiddenExistingCandidate(HiddenCentralRelationshipRoute Route, Point Source, Point Target, Point ExistingIntermediate,
                                                                   IList<ObstacleInfo> Obstacles, LinkObstacleRoutingOptions Options)
        {
            var SourcePoint = GetSingletonRoutePoint(Route == null ? null : Route.SourceConnector);
            var TargetPoint = GetSingletonRoutePoint(Route == null ? null : Route.TargetConnector);
            if (Route != null && Route.SourceConnector != null && Route.TargetConnector != null &&
                SourcePoint != Display.NULL_POINT && TargetPoint != Display.NULL_POINT)
            {
                var ExistingDoglegKind = (NearlyEqual(SourcePoint.Y, ExistingIntermediate.Y) &&
                                          NearlyEqual(TargetPoint.Y, ExistingIntermediate.Y))
                                         ? RouteKind.HorizontalDogleg
                                         : RouteKind.VerticalDogleg;

                return CreateHiddenDoglegCandidate(ExistingDoglegKind, ExistingIntermediate,
                                                   SourcePoint, TargetPoint,
                                                   Source, Target, Obstacles, Options, ExistingIntermediate, true);
            }

            if (ExistingIntermediate == Display.NULL_POINT)
                return CreateHiddenCandidate(RouteKind.Straight, Display.NULL_POINT, Source, Target, Obstacles, Options, ExistingIntermediate, true);

            return CreateHiddenCandidate(RouteKind.Existing, ExistingIntermediate, Source, Target, Obstacles, Options, ExistingIntermediate, true);
        }

        private static RouteCandidate CreateCandidate(RouteKind Kind, Point Intermediate, Point Source, Point Target,
                                                      IList<Rect> Obstacles, LinkObstacleRoutingOptions Options,
                                                      Point ExistingIntermediate, bool IsExisting = false)
        {
            var Candidate = new RouteCandidate();
            Candidate.Kind = Kind;
            Candidate.Intermediate = Intermediate;
            Candidate.IsExisting = IsExisting;

            if (Intermediate == Display.NULL_POINT)
            {
                string InvalidReason;
                Candidate.Length = Distance(Source, Target);
                Candidate.IsValid = IsSegmentValid(Source, Target, Obstacles, Options, out InvalidReason);
                Candidate.InvalidReason = InvalidReason;
                Candidate.NearMisses = CountNearMisses(Source, Target, Obstacles, Options);
            }
            else
            {
                string FirstInvalidReason;
                string SecondInvalidReason;
                Candidate.Length = Distance(Source, Intermediate) + Distance(Intermediate, Target);
                var FirstSegmentIsValid = IsSegmentValid(Source, Intermediate, Obstacles, Options, out FirstInvalidReason);
                var SecondSegmentIsValid = IsSegmentValid(Intermediate, Target, Obstacles, Options, out SecondInvalidReason);
                Candidate.IsValid = FirstSegmentIsValid && SecondSegmentIsValid;
                Candidate.InvalidReason = FirstInvalidReason.NullDefault(SecondInvalidReason);
                Candidate.NearMisses = CountNearMisses(Source, Intermediate, Obstacles, Options) +
                                       CountNearMisses(Intermediate, Target, Obstacles, Options);
            }

            Candidate.ExistingMovement = ExistingIntermediate == Display.NULL_POINT || Intermediate == Display.NULL_POINT
                                         ? (ExistingIntermediate == Intermediate ? 0.0 : Options.MinimumRouteImprovement)
                                         : Distance(ExistingIntermediate, Intermediate);
            return Candidate;
        }

        private static RouteCandidate CreateHiddenCandidate(RouteKind Kind, Point Intermediate, Point Source, Point Target,
                                                            IList<ObstacleInfo> Obstacles, LinkObstacleRoutingOptions Options,
                                                            Point ExistingIntermediate, bool IsExisting = false)
        {
            var Candidate = new RouteCandidate();
            Candidate.Kind = Kind;
            Candidate.Intermediate = Intermediate;
            Candidate.IsExisting = IsExisting;

            string InvalidReason;
            Candidate.IsValid = IsHiddenRouteValid(Kind, Source, Target, Intermediate, Obstacles, Options, out InvalidReason);
            Candidate.InvalidReason = InvalidReason;

            var RectObstacles = (Obstacles ?? Enumerable.Empty<ObstacleInfo>()).Select(Obstacle => Obstacle.Bounds).ToList();
            if (Intermediate == Display.NULL_POINT)
            {
                Candidate.Length = Distance(Source, Target);
                Candidate.NearMisses = CountNearMisses(Source, Target, RectObstacles, Options);
            }
            else
            {
                Candidate.Length = Distance(Source, Intermediate) + Distance(Intermediate, Target);
                Candidate.NearMisses = CountNearMisses(Source, Intermediate, RectObstacles, Options) +
                                       CountNearMisses(Intermediate, Target, RectObstacles, Options);
            }

            Candidate.ExistingMovement = ExistingIntermediate == Display.NULL_POINT || Intermediate == Display.NULL_POINT
                                         ? (ExistingIntermediate == Intermediate ? 0.0 : Options.MinimumRouteImprovement)
                                         : Distance(ExistingIntermediate, Intermediate);
            return Candidate;
        }

        private static RouteCandidate CreateHiddenDoglegCandidate(RouteKind Kind, double BusCoordinate, Point Source, Point Target,
                                                                  IList<ObstacleInfo> Obstacles, LinkObstacleRoutingOptions Options,
                                                                  Point ExistingIntermediate)
        {
            var MidX = (Source.X + Target.X) / 2.0;
            var MidY = (Source.Y + Target.Y) / 2.0;

            if (Kind == RouteKind.HorizontalDogleg)
                return CreateHiddenDoglegCandidate(Kind, new Point(MidX, BusCoordinate),
                                                   new Point(Source.X, BusCoordinate),
                                                   new Point(Target.X, BusCoordinate),
                                                   Source, Target, Obstacles, Options, ExistingIntermediate, false);

            return CreateHiddenDoglegCandidate(Kind, new Point(BusCoordinate, MidY),
                                               new Point(BusCoordinate, Source.Y),
                                               new Point(BusCoordinate, Target.Y),
                                               Source, Target, Obstacles, Options, ExistingIntermediate, false);
        }

        private static RouteCandidate CreateHiddenDoglegCandidate(RouteKind Kind, Point HiddenCenter, Point SourceIntermediate,
                                                                  Point TargetIntermediate, Point Source, Point Target,
                                                                  IList<ObstacleInfo> Obstacles, LinkObstacleRoutingOptions Options,
                                                                  Point ExistingIntermediate, bool IsExisting)
        {
            var Candidate = new RouteCandidate();
            Candidate.Kind = Kind;
            Candidate.Intermediate = HiddenCenter;
            Candidate.SourceIntermediate = SourceIntermediate;
            Candidate.TargetIntermediate = TargetIntermediate;
            Candidate.IsExisting = IsExisting;
            Candidate.BendPenalty = IsExisting ? 40.0 : 120.0;

            string InvalidReason;
            Candidate.IsValid = IsHiddenDoglegRouteValid(Source, Target, SourceIntermediate, HiddenCenter,
                                                         TargetIntermediate, Obstacles, Options, out InvalidReason);
            Candidate.InvalidReason = InvalidReason;

            Candidate.Length = Distance(Source, SourceIntermediate) +
                               Distance(SourceIntermediate, HiddenCenter) +
                               Distance(HiddenCenter, TargetIntermediate) +
                               Distance(TargetIntermediate, Target);

            var RectObstacles = (Obstacles ?? Enumerable.Empty<ObstacleInfo>()).Select(Obstacle => Obstacle.Bounds).ToList();
            Candidate.NearMisses = CountNearMisses(Source, SourceIntermediate, RectObstacles, Options) +
                                   CountNearMisses(SourceIntermediate, HiddenCenter, RectObstacles, Options) +
                                   CountNearMisses(HiddenCenter, TargetIntermediate, RectObstacles, Options) +
                                   CountNearMisses(TargetIntermediate, Target, RectObstacles, Options);

            Candidate.ExistingMovement = ExistingIntermediate == Display.NULL_POINT
                                         ? Options.MinimumRouteImprovement
                                         : Distance(ExistingIntermediate, HiddenCenter);

            return Candidate;
        }

        private static bool IsSegmentValid(Point Start, Point End, IList<Rect> Obstacles, LinkObstacleRoutingOptions Options)
        {
            string Reason;
            return IsSegmentValid(Start, End, Obstacles, Options, out Reason);
        }

        private static bool IsSegmentValid(Point Start, Point End, IList<Rect> Obstacles, LinkObstacleRoutingOptions Options, out string Reason)
        {
            Reason = null;
            if (Distance(Start, End) < Options.MinimumSegmentLength)
            {
                Reason = "segment is too short";
                return false;
            }

            foreach (var Obstacle in Obstacles ?? Enumerable.Empty<Rect>())
                if (SegmentIntersectsObstacle(Start, End, Obstacle))
                {
                    Reason = "segment intersects an obstacle";
                    return false;
                }

            return true;
        }

        private static bool IsHiddenRouteValid(RouteKind Kind, Point Source, Point Target, Point Intermediate,
                                               IList<ObstacleInfo> Obstacles, LinkObstacleRoutingOptions Options,
                                               out string Reason)
        {
            Reason = null;

            if (Kind == RouteKind.Straight && Intermediate == Display.NULL_POINT)
            {
                var Midpoint = GetMidpoint(Source, Target);
                ObstacleInfo ContainingObstacle;
                if (TryGetContainingObstacle(Midpoint, Obstacles, out ContainingObstacle))
                {
                    Reason = "midpoint inside obstacle " + DescribeObstacle(ContainingObstacle);
                    return false;
                }

                if (Distance(Source, Target) < Options.MinimumSegmentLength)
                {
                    Reason = "source-target segment is too short";
                    return false;
                }

                ObstacleInfo IntersectedObstacle;
                if (TryGetIntersectingObstacle(Source, Target, Obstacles, out IntersectedObstacle))
                {
                    Reason = "source-target segment intersects obstacle " + DescribeObstacle(IntersectedObstacle);
                    return false;
                }

                return true;
            }

            if (!IsUsablePoint(Intermediate))
            {
                Reason = "hidden center/elbow point has invalid geometry";
                return false;
            }

            ObstacleInfo HitObstacle;
            if (TryGetContainingObstacle(Intermediate, Obstacles, out HitObstacle))
            {
                Reason = "elbow inside obstacle " + DescribeObstacle(HitObstacle);
                return false;
            }

            if (Distance(Source, Intermediate) < Options.MinimumSegmentLength ||
                Distance(Intermediate, Target) < Options.MinimumSegmentLength)
            {
                Reason = "one route segment is too short";
                return false;
            }

            if (TryGetIntersectingObstacle(Source, Intermediate, Obstacles, out HitObstacle))
            {
                Reason = "source-to-elbow segment intersects obstacle " + DescribeObstacle(HitObstacle);
                return false;
            }

            if (TryGetIntersectingObstacle(Intermediate, Target, Obstacles, out HitObstacle))
            {
                Reason = "elbow-to-target segment intersects obstacle " + DescribeObstacle(HitObstacle);
                return false;
            }

            return true;
        }

        private static bool IsHiddenDoglegRouteValid(Point Source, Point Target, Point SourceIntermediate,
                                                     Point HiddenCenter, Point TargetIntermediate,
                                                     IList<ObstacleInfo> Obstacles, LinkObstacleRoutingOptions Options,
                                                     out string Reason)
        {
            Reason = null;

            if (!IsUsablePoint(SourceIntermediate) || !IsUsablePoint(HiddenCenter) || !IsUsablePoint(TargetIntermediate))
            {
                Reason = "dogleg point has invalid geometry";
                return false;
            }

            ObstacleInfo HitObstacle;
            if (TryGetContainingObstacle(SourceIntermediate, Obstacles, out HitObstacle))
            {
                Reason = "source-side dogleg bend inside obstacle " + DescribeObstacle(HitObstacle);
                return false;
            }

            if (TryGetContainingObstacle(HiddenCenter, Obstacles, out HitObstacle))
            {
                Reason = "hidden junction inside obstacle " + DescribeObstacle(HitObstacle);
                return false;
            }

            if (TryGetContainingObstacle(TargetIntermediate, Obstacles, out HitObstacle))
            {
                Reason = "target-side dogleg bend inside obstacle " + DescribeObstacle(HitObstacle);
                return false;
            }

            var Segments = new[]
            {
                new Tuple<Point, Point, string>(Source, SourceIntermediate, "source-to-source-bend"),
                new Tuple<Point, Point, string>(SourceIntermediate, HiddenCenter, "source-bend-to-hidden-junction"),
                new Tuple<Point, Point, string>(HiddenCenter, TargetIntermediate, "hidden-junction-to-target-bend"),
                new Tuple<Point, Point, string>(TargetIntermediate, Target, "target-bend-to-target")
            };

            foreach (var Segment in Segments)
            {
                if (Distance(Segment.Item1, Segment.Item2) < Options.MinimumSegmentLength)
                {
                    Reason = Segment.Item3 + " segment is too short";
                    return false;
                }

                if (TryGetIntersectingObstacle(Segment.Item1, Segment.Item2, Obstacles, out HitObstacle))
                {
                    Reason = Segment.Item3 + " segment intersects obstacle " + DescribeObstacle(HitObstacle);
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<double> BuildHorizontalDoglegBusCandidates(Point Source, Point Target,
                                                                              IList<ObstacleInfo> Obstacles,
                                                                              LinkObstacleRoutingOptions Options)
        {
            var Blockers = GetHorizontalDoglegBlockers(Source, Target, Obstacles).ToList();
            if (Blockers.Count < 1)
                yield break;

            var Clearance = Math.Max(Options.ObstaclePadding, Options.NearMissPadding);
            var Top = Blockers.Min(Obstacle => Obstacle.Bounds.Top);
            var Bottom = Blockers.Max(Obstacle => Obstacle.Bounds.Bottom);

            yield return Top - Clearance;
            yield return Bottom + Clearance;

            foreach (var Obstacle in Blockers)
            {
                yield return Obstacle.Bounds.Top - Clearance;
                yield return Obstacle.Bounds.Bottom + Clearance;
            }
        }

        private static IEnumerable<double> BuildVerticalDoglegBusCandidates(Point Source, Point Target,
                                                                            IList<ObstacleInfo> Obstacles,
                                                                            LinkObstacleRoutingOptions Options)
        {
            var Blockers = GetVerticalDoglegBlockers(Source, Target, Obstacles).ToList();
            if (Blockers.Count < 1)
                yield break;

            var Clearance = Math.Max(Options.ObstaclePadding, Options.NearMissPadding);
            var Left = Blockers.Min(Obstacle => Obstacle.Bounds.Left);
            var Right = Blockers.Max(Obstacle => Obstacle.Bounds.Right);

            yield return Left - Clearance;
            yield return Right + Clearance;

            foreach (var Obstacle in Blockers)
            {
                yield return Obstacle.Bounds.Left - Clearance;
                yield return Obstacle.Bounds.Right + Clearance;
            }
        }

        private static IEnumerable<ObstacleInfo> GetHorizontalDoglegBlockers(Point Source, Point Target, IEnumerable<ObstacleInfo> Obstacles)
        {
            var MinX = Math.Min(Source.X, Target.X);
            var MaxX = Math.Max(Source.X, Target.X);
            var MinY = Math.Min(Source.Y, Target.Y);
            var MaxY = Math.Max(Source.Y, Target.Y);
            var Corridor = new Rect(MinX, MinY, Math.Max(GeometryTolerance, MaxX - MinX), Math.Max(GeometryTolerance, MaxY - MinY));
            Corridor.Inflate(GeometryTolerance, GeometryTolerance);

            foreach (var Obstacle in Obstacles ?? Enumerable.Empty<ObstacleInfo>())
                if (Obstacle != null &&
                    (SegmentIntersectsObstacle(Source, Target, Obstacle.Bounds) ||
                     Obstacle.Bounds.IntersectsWith(Corridor)))
                    yield return Obstacle;
        }

        private static IEnumerable<ObstacleInfo> GetVerticalDoglegBlockers(Point Source, Point Target, IEnumerable<ObstacleInfo> Obstacles)
        {
            var MinX = Math.Min(Source.X, Target.X);
            var MaxX = Math.Max(Source.X, Target.X);
            var MinY = Math.Min(Source.Y, Target.Y);
            var MaxY = Math.Max(Source.Y, Target.Y);
            var Corridor = new Rect(MinX, MinY, Math.Max(GeometryTolerance, MaxX - MinX), Math.Max(GeometryTolerance, MaxY - MinY));
            Corridor.Inflate(GeometryTolerance, GeometryTolerance);

            foreach (var Obstacle in Obstacles ?? Enumerable.Empty<ObstacleInfo>())
                if (Obstacle != null &&
                    (SegmentIntersectsObstacle(Source, Target, Obstacle.Bounds) ||
                     Obstacle.Bounds.IntersectsWith(Corridor)))
                    yield return Obstacle;
        }

        private static int CountNearMisses(Point Start, Point End, IList<Rect> Obstacles, LinkObstacleRoutingOptions Options)
        {
            var Count = 0;
            foreach (var Obstacle in Obstacles)
            {
                var NearObstacle = Obstacle;
                NearObstacle.Inflate(Options.NearMissPadding, Options.NearMissPadding);
                if (SegmentIntersectsObstacle(Start, End, NearObstacle))
                    Count++;
            }

            return Count;
        }

        private static bool TryGetContainingObstacle(Point Point, IEnumerable<ObstacleInfo> Obstacles, out ObstacleInfo ContainingObstacle)
        {
            ContainingObstacle = null;
            if (!IsUsablePoint(Point))
                return false;

            foreach (var Obstacle in Obstacles ?? Enumerable.Empty<ObstacleInfo>())
                if (Obstacle != null && IsUsableRect(Obstacle.Bounds) && Obstacle.Bounds.Contains(Point))
                {
                    ContainingObstacle = Obstacle;
                    return true;
                }

            return false;
        }

        private static bool TryGetIntersectingObstacle(Point Start, Point End, IEnumerable<ObstacleInfo> Obstacles, out ObstacleInfo IntersectingObstacle)
        {
            IntersectingObstacle = null;

            foreach (var Obstacle in Obstacles ?? Enumerable.Empty<ObstacleInfo>())
                if (Obstacle != null && SegmentIntersectsObstacle(Start, End, Obstacle.Bounds))
                {
                    IntersectingObstacle = Obstacle;
                    return true;
                }

            return false;
        }

        private static void LogHiddenCenterCandidateDiagnostics(HiddenCentralRelationshipRoute Route, Point Source, Point Target, Point Current,
                                                               IList<ObstacleInfo> Obstacles, RouteCandidate Existing,
                                                               IList<RouteCandidate> Candidates)
        {
            var RelationshipTechName = Route == null || Route.Representation == null || Route.Representation.RepresentedRelationship == null
                                       ? "<none>"
                                       : Route.Representation.RepresentedRelationship.TechName.ToStringAlways();
            ObstacleInfo CurrentObstacle;
            var CurrentInsideObstacle = TryGetContainingObstacle(Current, Obstacles, out CurrentObstacle);

            Console.WriteLine("Appearance hidden-center route {0}: source={1}, target={2}, current={3}, obstacles={4}, currentInsideObstacle={5}.",
                              RelationshipTechName,
                              FormatPoint(Source),
                              FormatPoint(Target),
                              FormatPoint(Current),
                              Obstacles == null ? 0 : Obstacles.Count,
                              CurrentInsideObstacle ? DescribeObstacle(CurrentObstacle) : "false");

            LogHiddenCandidateDiagnostic(RelationshipTechName, "current", Existing, Source, Target);

            foreach (var Candidate in Candidates ?? Enumerable.Empty<RouteCandidate>())
                LogHiddenCandidateDiagnostic(RelationshipTechName, Candidate.Kind.ToString(), Candidate, Source, Target);
        }

        private static void LogHiddenCandidateDiagnostic(string RelationshipTechName, string Label, RouteCandidate Candidate, Point Source, Point Target)
        {
            if (Candidate == null)
                return;

            var EffectiveCenter = Candidate.Intermediate == Display.NULL_POINT
                                  ? GetMidpoint(Source, Target)
                                  : Candidate.Intermediate;

            if (Candidate.IsDogleg)
            {
                Console.WriteLine("Appearance hidden-center route {0}: {1} {2}; P1={3}; J={4}; P2={5}; score={6:0.###}; length={7:0.###}; reason={8}.",
                                  RelationshipTechName,
                                  FormatHiddenRouteKind(Candidate.Kind),
                                  Candidate.IsValid ? "valid" : "invalid",
                                  FormatPoint(Candidate.SourceIntermediate),
                                  FormatPoint(EffectiveCenter),
                                  FormatPoint(Candidate.TargetIntermediate),
                                  Candidate.Score,
                                  Candidate.Length,
                                  Candidate.InvalidReason.ToStringAlways("<none>"));
                return;
            }

            Console.WriteLine("Appearance hidden-center route {0}: {1} {2}; elbow={3}; score={4:0.###}; length={5:0.###}; reason={6}.",
                              RelationshipTechName,
                              Label,
                              Candidate.IsValid ? "valid" : "invalid",
                              FormatPoint(EffectiveCenter),
                              Candidate.Score,
                              Candidate.Length,
                              Candidate.InvalidReason.ToStringAlways("<none>"));
        }

        private static bool IsHiddenCentralRelationship(RelationshipVisualRepresentation Representation)
        {
            if (Representation == null || Representation.MainSymbol == null || Representation.RepresentedRelationship == null)
                return false;

            var Definition = Representation.RepresentedRelationship.RelationshipDefinitor == null
                             ? null
                             : Representation.RepresentedRelationship.RelationshipDefinitor.Value;
            var DefinitionHidesSimpleCentralSymbol = Definition != null &&
                                                     Definition.HideCentralSymbolWhenSimple &&
                                                     Definition.IsSimple;

            return Representation.MainSymbol.IsHidden ||
                   !Representation.MainSymbol.IsRelatedVisible ||
                   DefinitionHidesSimpleCentralSymbol;
        }

        private static bool IsGenuinelySimpleRelationship(RelationshipVisualRepresentation Representation)
        {
            var Relationship = Representation == null ? null : Representation.RepresentedRelationship;
            var Definition = Relationship == null || Relationship.RelationshipDefinitor == null
                             ? null : Relationship.RelationshipDefinitor.Value;
            if (Definition == null || !Definition.IsSimple || Relationship.Links == null ||
                Relationship.Links.Count(Link => Link != null) != 2 ||
                Representation == null || Representation.MainSymbol == null)
                return false;

            var Connectors = (Representation.VisualConnectors ?? Enumerable.Empty<VisualConnector>())
                .Where(Connector => Connector != null)
                .Distinct()
                .ToList();
            if (Connectors.Count != 2)
                return false;

            // Exactly two semantic links/visual legs is the simple-link invariant.  Do not
            // require two distinct endpoint Ideas: a true auto-reference has two legs attached
            // to the same endpoint and is handled by the deterministic self-loop branch.
            return true;
        }

        private static bool TryGetHiddenCentralRelationshipRoute(RelationshipVisualRepresentation Representation,
                                                                 out HiddenCentralRelationshipRoute Route, out string Reason)
        {
            Route = null;
            Reason = null;

            if (Representation == null)
            {
                Reason = "relationship representation is null";
                return false;
            }

            if (Representation.GetDisplayingView() == null)
            {
                Reason = "relationship representation has no displaying view";
                return false;
            }

            if (!IsHiddenCentralRelationship(Representation))
            {
                Reason = "relationship does not hide its central symbol";
                return false;
            }

            var MainSymbol = Representation.MainSymbol;
            if (MainSymbol == null)
            {
                Reason = "relationship has no main symbol";
                return false;
            }

            var Connectors = Representation.VisualConnectors.Where(Connector => Connector != null).Distinct().ToList();
            if (Connectors.Count < 2)
            {
                Reason = "relationship has fewer than two connector segments";
                return false;
            }

            var SourceConnector = Connectors.FirstOrDefault(Connector => Connector.TargetSymbol == MainSymbol &&
                                                                         IsVisibleConceptSymbol(Connector.OriginSymbol));
            var TargetConnector = Connectors.FirstOrDefault(Connector => Connector.OriginSymbol == MainSymbol &&
                                                                         IsVisibleConceptSymbol(Connector.TargetSymbol));

            VisualSymbol SourceSymbol = SourceConnector == null ? null : SourceConnector.OriginSymbol;
            VisualSymbol TargetSymbol = TargetConnector == null ? null : TargetConnector.TargetSymbol;

            if (SourceSymbol == null || TargetSymbol == null)
            {
                var EndpointSymbols = Connectors
                    .Select(Connector => Connector.OriginSymbol == MainSymbol ? Connector.TargetSymbol :
                                         (Connector.TargetSymbol == MainSymbol ? Connector.OriginSymbol : null))
                    .Where(IsVisibleConceptSymbol)
                    .Distinct()
                    .ToList();

                if (EndpointSymbols.Count >= 2)
                {
                    SourceSymbol = EndpointSymbols[0];
                    TargetSymbol = EndpointSymbols[1];
                    SourceConnector = Connectors.FirstOrDefault(Connector => Connector.OriginSymbol == SourceSymbol || Connector.TargetSymbol == SourceSymbol);
                    TargetConnector = Connectors.FirstOrDefault(Connector => Connector.OriginSymbol == TargetSymbol || Connector.TargetSymbol == TargetSymbol);
                }
            }

            if (SourceSymbol == null || TargetSymbol == null)
            {
                Reason = "relationship does not have two visible concept endpoints through its hidden main symbol";
                return false;
            }

            if (!IsUsablePoint(SourceSymbol.BaseCenter) || !IsUsablePoint(TargetSymbol.BaseCenter) || !IsUsablePoint(MainSymbol.BaseCenter))
            {
                Reason = "relationship has invalid endpoint or hidden-center geometry";
                return false;
            }

            Route = new HiddenCentralRelationshipRoute
            {
                Representation = Representation,
                MainSymbol = MainSymbol,
                SourceSymbol = SourceSymbol,
                TargetSymbol = TargetSymbol,
                SourceConnector = SourceConnector,
                TargetConnector = TargetConnector
            };
            return true;
        }

        private static bool IsVisibleConceptSymbol(VisualSymbol Symbol)
        {
            return Symbol != null &&
                   Symbol.OwnerRepresentation is ConceptVisualRepresentation &&
                   !Symbol.IsHidden &&
                   Symbol.IsRelatedVisible;
        }

        private static bool IsPotentiallyRouteable(VisualConnector Connector)
        {
            return Connector != null;
        }

        private static bool ValidateConnector(VisualConnector Connector, out string Reason)
        {
            Reason = null;

            if (Connector == null)
            {
                Reason = "connector is null";
                return false;
            }

            if (Connector.GetDisplayingView() == null)
            {
                Reason = "connector has no displaying view";
                return false;
            }

            if (Connector.OriginSymbol == null || Connector.TargetSymbol == null)
            {
                Reason = "connector has missing origin or target symbol";
                return false;
            }

            if (Connector.OriginSymbol.IsHidden || Connector.TargetSymbol.IsHidden ||
                !Connector.OriginSymbol.IsRelatedVisible || !Connector.TargetSymbol.IsRelatedVisible)
            {
                Reason = "connector has hidden origin or target symbol";
                return false;
            }

            if (!IsUsablePoint(GetEndpoint(Connector, true)) || !IsUsablePoint(GetEndpoint(Connector, false)))
            {
                Reason = "connector has invalid endpoint geometry";
                return false;
            }

            return true;
        }

        private static Point GetEndpoint(VisualConnector Connector, bool Origin)
        {
            var Symbol = Origin ? Connector.OriginSymbol : Connector.TargetSymbol;
            var Position = Origin ? Connector.OriginPosition : Connector.TargetPosition;

            if (IsUsablePoint(Position) && Position != Display.NULL_POINT)
                return Position;

            return Symbol == null ? Display.NULL_POINT : Symbol.BaseCenter;
        }

        private static bool RectangleContainsSymbol(Rect Rectangle, VisualSymbol Symbol)
        {
            if (Symbol == null)
                return false;

            return Rectangle.Contains(Symbol.BaseCenter);
        }

        private static bool IsExistingOrthogonal(RouteCandidate Candidate, Point Source, Point Target)
        {
            if (Candidate == null || Candidate.Intermediate == Display.NULL_POINT)
                return true;

            return (NearlyEqual(Source.X, Candidate.Intermediate.X) || NearlyEqual(Source.Y, Candidate.Intermediate.Y)) &&
                   (NearlyEqual(Target.X, Candidate.Intermediate.X) || NearlyEqual(Target.Y, Candidate.Intermediate.Y));
        }

        private static bool IsExistingRoutePreservable(RouteCandidate Candidate, Point Source, Point Target)
        {
            if (Candidate == null)
                return false;

            if (Candidate.IsDogleg)
                return true;

            return IsExistingOrthogonal(Candidate, Source, Target);
        }

        private static bool RouteEquivalent(RouteCandidate First, RouteCandidate Second)
        {
            if (First == null || Second == null)
                return false;

            if (First.IsDogleg || Second.IsDogleg)
                return First.IsDogleg == Second.IsDogleg &&
                       PointsEqual(First.SourceIntermediate, Second.SourceIntermediate) &&
                       PointsEqual(First.Intermediate, Second.Intermediate) &&
                       PointsEqual(First.TargetIntermediate, Second.TargetIntermediate);

            if (First.Intermediate == Display.NULL_POINT && Second.Intermediate == Display.NULL_POINT)
                return true;

            return PointsEqual(First.Intermediate, Second.Intermediate);
        }

        private static bool CandidateMatchesCurrent(HiddenCentralRelationshipRoute Route, RouteCandidate Candidate,
                                                    Point OldCenter, Point Source, Point Target)
        {
            if (Route == null || Candidate == null)
                return false;

            if (!PointsEqual(OldCenter, GetHiddenCenterForCandidate(Candidate, Source, Target)))
                return false;

            if (Candidate.IsDogleg)
                return Route.SourceConnector != null &&
                       Route.TargetConnector != null &&
                       PointsEqual(GetSingletonRoutePoint(Route.SourceConnector), Candidate.SourceIntermediate) &&
                       PointsEqual(GetSingletonRoutePoint(Route.TargetConnector), Candidate.TargetIntermediate);

            return Route.Connectors.All(Connector => Connector.RoutePoints == null || Connector.RoutePoints.Count == 0);
        }

        private static Point GetSingletonRoutePoint(VisualConnector Connector)
        {
            return Connector != null && Connector.RoutePoints != null && Connector.RoutePoints.Count == 1
                   ? Connector.RoutePoints[0]
                   : Display.NULL_POINT;
        }

        private static void SetLegacyCandidateRoutePoint(VisualConnector Connector, Point Point)
        {
            if (Connector == null)
                return;
            if (Point == Display.NULL_POINT)
                Connector.ClearRoutePoints();
            else
                Connector.SetRoutePoints(new[] { Point });
        }

        private static bool SegmentIntersectsSegment(Point A, Point B, Point C, Point D)
        {
            var D1 = Direction(C, D, A);
            var D2 = Direction(C, D, B);
            var D3 = Direction(A, B, C);
            var D4 = Direction(A, B, D);

            if (((D1 > GeometryTolerance && D2 < -GeometryTolerance) || (D1 < -GeometryTolerance && D2 > GeometryTolerance)) &&
                ((D3 > GeometryTolerance && D4 < -GeometryTolerance) || (D3 < -GeometryTolerance && D4 > GeometryTolerance)))
                return true;

            return Math.Abs(D1) <= GeometryTolerance && OnSegment(C, D, A) ||
                   Math.Abs(D2) <= GeometryTolerance && OnSegment(C, D, B) ||
                   Math.Abs(D3) <= GeometryTolerance && OnSegment(A, B, C) ||
                   Math.Abs(D4) <= GeometryTolerance && OnSegment(A, B, D);
        }

        private static double Direction(Point A, Point B, Point C)
        {
            return (C.X - A.X) * (B.Y - A.Y) - (B.X - A.X) * (C.Y - A.Y);
        }

        private static bool OnSegment(Point A, Point B, Point C)
        {
            return C.X >= Math.Min(A.X, B.X) - GeometryTolerance &&
                   C.X <= Math.Max(A.X, B.X) + GeometryTolerance &&
                   C.Y >= Math.Min(A.Y, B.Y) - GeometryTolerance &&
                   C.Y <= Math.Max(A.Y, B.Y) + GeometryTolerance;
        }

        private static double Distance(Point First, Point Second)
        {
            var DeltaX = First.X - Second.X;
            var DeltaY = First.Y - Second.Y;
            return Math.Sqrt(DeltaX * DeltaX + DeltaY * DeltaY);
        }

        private static Point GetMidpoint(Point First, Point Second)
        {
            return new Point((First.X + Second.X) / 2.0, (First.Y + Second.Y) / 2.0);
        }

        private static Point GetHiddenCenterForCandidate(RouteCandidate Candidate, Point Source, Point Target)
        {
            if (Candidate == null)
                return Display.NULL_POINT;

            return Candidate.Intermediate == Display.NULL_POINT
                   ? GetMidpoint(Source, Target)
                   : Candidate.Intermediate;
        }

        private static bool PointsEqual(Point First, Point Second)
        {
            if (First == Display.NULL_POINT && Second == Display.NULL_POINT)
                return true;

            return NearlyEqual(First.X, Second.X) && NearlyEqual(First.Y, Second.Y);
        }

        private static bool NearlyEqual(double First, double Second)
        {
            return Math.Abs(First - Second) <= GeometryTolerance;
        }

        private static bool IsUsablePoint(Point Point)
        {
            return !Point.X.IsNan() && !Point.Y.IsNan() &&
                   !Double.IsInfinity(Point.X) && !Double.IsInfinity(Point.Y);
        }

        private static bool IsUsableRect(Rect Rect)
        {
            return !Rect.IsEmpty &&
                   !Rect.Left.IsNan() &&
                   !Rect.Top.IsNan() &&
                   !Rect.Width.IsNan() &&
                   !Rect.Height.IsNan() &&
                   Rect.Width > 0.0 &&
                   Rect.Height > 0.0;
        }

        private static string DescribeConnector(VisualConnector Connector)
        {
            if (Connector == null)
                return "<null connector>";

            var Relationship = Connector.OwnerRelationshipRepresentation == null
                               ? null
                               : Connector.OwnerRelationshipRepresentation.RepresentedRelationship;
            return "relationship=" + DescribeIdea(Relationship) +
                   "; origin=" + DescribeSymbol(Connector.OriginSymbol) +
                   "; target=" + DescribeSymbol(Connector.TargetSymbol);
        }

        private static string DescribeHiddenCentralRoute(HiddenCentralRelationshipRoute Route)
        {
            if (Route == null)
                return "<null hidden-central relationship route>";

            return "relationship=" + DescribeIdea(Route.Representation == null ? null : Route.Representation.RepresentedRelationship) +
                   "; source=" + DescribeSymbol(Route.SourceSymbol) +
                   "; target=" + DescribeSymbol(Route.TargetSymbol);
        }

        private static string DescribeObstacle(ObstacleInfo Obstacle)
        {
            if (Obstacle == null)
                return "<unknown>";

            var Idea = Obstacle.Symbol == null || Obstacle.Symbol.OwnerRepresentation == null
                       ? null
                       : Obstacle.Symbol.OwnerRepresentation.RepresentedIdea;

            if (Idea != null)
                return "'" + Idea.TechName.ToStringAlways() + "'";

            return FormatRect(Obstacle.Bounds);
        }

        private static string FormatHiddenRouteKind(RouteKind Kind)
        {
            if (Kind == RouteKind.Straight)
                return "straight";

            if (Kind == RouteKind.HorizontalFirst)
                return "horizontal-first";

            if (Kind == RouteKind.VerticalFirst)
                return "vertical-first";

            if (Kind == RouteKind.HorizontalDogleg)
                return "horizontal-dogleg";

            if (Kind == RouteKind.VerticalDogleg)
                return "vertical-dogleg";

            return Kind.ToString();
        }

        private static string DescribeSymbol(VisualSymbol Symbol)
        {
            if (Symbol == null || Symbol.OwnerRepresentation == null)
                return "<none>";

            return DescribeIdea(Symbol.OwnerRepresentation.RepresentedIdea);
        }

        private static string DescribeIdea(Idea Idea)
        {
            if (Idea == null)
                return "<none>";

            return "'" + Idea.Name.ToStringAlways() + "' techName=" + Idea.TechName.ToStringAlways() +
                   " id=" + Idea.GlobalId.ToString("D");
        }

        private static string DescribeView(View View)
        {
            if (View == null)
                return "<none>";

            return "'" + View.Name.ToStringAlways() + "' techName=" + View.TechName.ToStringAlways() +
                   " id=" + View.GlobalId.ToString("D");
        }

        private static string FormatPoint(Point Point)
        {
            if (Point == Display.NULL_POINT || !IsUsablePoint(Point))
                return "<straight>";

            return "(" + Point.X.ToString("0.###", CultureInfo.InvariantCulture) +
                   "," + Point.Y.ToString("0.###", CultureInfo.InvariantCulture) + ")";
        }

        private static string FormatRect(Rect Rect)
        {
            if (!IsUsableRect(Rect))
                return "<invalid rect>";

            return "(" + Rect.X.ToString("0.###", CultureInfo.InvariantCulture) +
                   "," + Rect.Y.ToString("0.###", CultureInfo.InvariantCulture) +
                   "," + Rect.Width.ToString("0.###", CultureInfo.InvariantCulture) +
                   "," + Rect.Height.ToString("0.###", CultureInfo.InvariantCulture) + ")";
        }

        private static void LogSummary(LinkObstacleRoutingResult Result)
        {
            Console.WriteLine("Appearance: Route Links with Obstacle Avoidance completed; connector routes inspected={0}; relationship routes inspected={1}; total inspected={2}; routed={3}; dogleg routed={4}; straightened={5}; appearance changed={6}; unchanged={7}; skipped={8}; warnings={9}.",
                              Result.ConnectorRoutesInspected, Result.RelationshipRoutesInspected, Result.Inspected,
                              Result.Routed, Result.DoglegRouted, Result.Straightened, Result.AppearanceChanged,
                              Result.Unchanged, Result.Skipped, Result.Warnings.Count);

            if (Result.RelationshipCenterPlacementResult != null)
                Console.WriteLine("Appearance: relationship center placement before routing; inspected={0}; recomputed={1}; preserved={2}; suspicious={3}; skipped={4}; finalOverlaps={5}.",
                                  Result.RelationshipCenterPlacementResult.RelationshipCentersInspected,
                                  Result.RelationshipCenterPlacementResult.RelationshipCentersRecomputed,
                                  Result.RelationshipCenterPlacementResult.RelationshipCentersPreserved,
                                  Result.RelationshipCenterPlacementResult.SuspiciousRelationshipCenters,
                                  Result.RelationshipCenterPlacementResult.RelationshipCentersSkipped,
                                  Result.RelationshipCenterPlacementResult.FinalRelationshipOverlapCount);

            foreach (var Warning in Result.Warnings)
                Console.WriteLine("Appearance routing warning: {0}", Warning);
        }
    }
}
