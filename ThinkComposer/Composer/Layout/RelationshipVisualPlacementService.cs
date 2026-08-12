// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Copyright (C) 2011-2015 Nestor Marcel Sanchez Ahumada.
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
using Instrumind.ThinkComposer.MetaModel.GraphMetaModel;
using Instrumind.ThinkComposer.Model.GraphModel;
using Instrumind.ThinkComposer.Model.VisualModel;

namespace Instrumind.ThinkComposer.Composer.Layout
{
    /// <summary>
    /// Places visible relationship central symbols near the corridor between their visible endpoint concepts.
    /// </summary>
    public static class RelationshipVisualPlacementService
    {
        private const double GeometryTolerance = 0.001;

        private class RelationshipPlacementItem
        {
            public RelationshipVisualRepresentation Representation;
            public Relationship Relationship;
            public VisualSymbol Symbol;
            public VisualSymbol OriginSymbol;
            public VisualSymbol TargetSymbol;
            public IList<VisualSymbol> EndpointSymbols;
            public Rect OriginBounds;
            public Rect TargetBounds;
            public IList<Rect> EndpointBounds;
            public Rect SymbolBounds;
            public Point OldCenter;
            public Point Midpoint;
            public Rect Corridor;
            public bool IsSelfReference;
            public bool IsSuspicious;
            public string SuspiciousReason;
        }

        public static RelationshipVisualPlacementResult PlaceVisibleRelationshipCenters(LayoutSelectionContext Context,
                                                                                        RelationshipVisualPlacementOptions Options)
        {
            return PlaceVisibleRelationshipCenters(Context, null, Options);
        }

        public static RelationshipVisualPlacementResult PlaceVisibleRelationshipCenters(LayoutSelectionContext Context,
                                                                                        IEnumerable<RelationshipVisualRepresentation> Scope,
                                                                                        RelationshipVisualPlacementOptions Options)
        {
            Options = Options ?? new RelationshipVisualPlacementOptions();
            Options.PlacementMode = NormalizeMode(Options.PlacementMode);
            var Result = new RelationshipVisualPlacementResult();

            Console.WriteLine("BEGIN THINKCOMPOSER LAYOUT ROUTING REPORT");
            Console.WriteLine("Algorithm: EndpointCorridorRelationshipCenters");
            Console.WriteLine("Relationship center placement starting; mode={0}; recomputeSuspicious={1}; maxDisplacement={2:0.##}; obstaclePadding={3:0.##}; overlapPadding={4:0.##}.",
                              Options.PlacementMode,
                              Options.RecomputeSuspiciousRelationshipVisuals ? "true" : "false",
                              Options.MaxRelationshipCenterDisplacement,
                              Options.RelationshipCenterObstaclePadding,
                              Options.RelationshipCenterOverlapPadding);

            if (Context == null || Context.ActiveView == null)
            {
                Result.AddWarning("No active view is available for relationship center placement.");
                Console.WriteLine("Relationship center placement skipped: no active view.");
                Console.WriteLine("END THINKCOMPOSER LAYOUT ROUTING REPORT");
                return Result;
            }

            var ConceptSymbols = Context.VisibleConceptSymbols
                                        .Where(Symbol => IsUsableVisibleSymbol(Symbol))
                                        .Distinct()
                                        .ToList();
            var ConceptByIdea = ConceptSymbols
                .Where(Symbol => Symbol.OwnerRepresentation != null && Symbol.OwnerRepresentation.RepresentedIdea != null)
                .GroupBy(Symbol => Symbol.OwnerRepresentation.RepresentedIdea)
                .ToDictionary(Group => Group.Key, Group => Group.First());

            var RelationshipScope = (Scope ?? Context.VisibleRelationshipRepresentations)
                .Where(Representation => Representation != null)
                .Distinct()
                .ToList();

            var Items = new List<RelationshipPlacementItem>();
            foreach (var Representation in RelationshipScope)
            {
                Result.RelationshipCentersInspected++;
                string SkipReason;
                RelationshipPlacementItem Item;
                if (!TryBuildItem(Representation, ConceptByIdea, Options, out Item, out SkipReason))
                {
                    Result.RelationshipCentersSkipped++;
                    Result.AddIssue(new RelationshipVisualPlacementIssue
                    {
                        RelationshipTechName = DescribeIdea(Representation == null ? null : Representation.RepresentedRelationship),
                        Severity = "Info",
                        Message = SkipReason
                    });
                    Console.WriteLine("Relationship center placement skipped: relationship={0}; reason={1}.",
                                      DescribeIdea(Representation == null ? null : Representation.RepresentedRelationship),
                                      SkipReason);
                    continue;
                }

                Items.Add(Item);
                if (Item.IsSuspicious)
                    Result.SuspiciousRelationshipCenters++;
            }

            if (Items.Count > 0 && Result.SuspiciousRelationshipCenters > Items.Count * 0.25)
            {
                var Message = "More than 25% of inspected relationship centers are far from their endpoint corridors; imported relationship visuals may have been placed in a global band.";
                Result.AddWarning(Message);
                Console.WriteLine("Relationship center placement batch warning: {0}", Message);
            }

            var PlacedRelationshipBounds = Items.ToDictionary(Item => Item, Item => Item.SymbolBounds);
            foreach (var Item in Items
                .OrderBy(Item => Item.Relationship == null ? "~" : Item.Relationship.GlobalId.ToString("D"),
                         StringComparer.Ordinal)
                .ThenBy(Item => Item.Representation == null ? "~" : Item.Representation.GlobalId.ToString("D"),
                        StringComparer.Ordinal)
                .ThenBy(Item => DescribeIdea(Item.Relationship), StringComparer.OrdinalIgnoreCase))
            {
                var Recompute = ShouldRecompute(Item, Options);
                if (!Recompute)
                {
                    Result.RelationshipCentersPreserved++;
                    Console.WriteLine("Relationship center placement preserved: relationship={0}; oldCenter={1}; midpoint={2}; corridor={3}; reason={4}.",
                                      DescribeIdea(Item.Relationship), FormatPoint(Item.OldCenter), FormatPoint(Item.Midpoint),
                                      FormatRect(Item.Corridor),
                                      Item.IsSuspicious ? Item.SuspiciousReason : "already near endpoint corridor");
                    continue;
                }

                RelationshipVisualPlacementCandidate Best;
                var OtherRelationshipBounds = PlacedRelationshipBounds
                    .Where(Pair => Pair.Key != Item)
                    .Select(Pair => Pair.Value)
                    .ToList();
                if (!TryChooseCandidate(Item, ConceptSymbols, OtherRelationshipBounds, Options, out Best))
                {
                    // Recompute is reached only for a suspicious hub or an explicitly forced
                    // placement mode.  Preserving a known-distant center here recreates the
                    // original giant-sweep failure, so choose the least-colliding deterministic
                    // point inside the endpoint corridor and invalidate its stale routes.
                    if (!TryChooseDegradedCandidate(Item, ConceptSymbols, OtherRelationshipBounds,
                                                    Options, out Best))
                    {
                        Result.RelationshipCentersSkipped++;
                        Result.AddWarning("No endpoint-corridor candidate was found for relationship '" +
                                          DescribeIdea(Item.Relationship) + "'.");
                        Console.WriteLine("Relationship center placement warning: relationship={0}; no corridor-local candidate near midpoint={1}; corridor={2}.",
                                          DescribeIdea(Item.Relationship), FormatPoint(Item.Midpoint),
                                          FormatRect(Item.Corridor));
                        continue;
                    }

                    var DegradedWarning = "No collision-free endpoint-corridor candidate was found for relationship '" +
                                          DescribeIdea(Item.Relationship) + "'; using deterministic degraded candidate '" +
                                          Best.Label + "' with collision score " +
                                          Best.CollisionScore.ToString("0.###", CultureInfo.InvariantCulture) + ".";
                    Result.AddWarning(DegradedWarning);
                    Console.WriteLine("Relationship center placement degraded: relationship={0}; candidate={1}; center={2}; collisions={3}; collisionScore={4:0.###}.",
                                      DescribeIdea(Item.Relationship), Best.Label, FormatPoint(Best.Center),
                                      Best.CollisionCount, Best.CollisionScore);
                }

                var NewBounds = BoundsAt(Item.SymbolBounds, Best.Center);
                PlacedRelationshipBounds[Item] = NewBounds;

                if (Distance(Item.OldCenter, Best.Center) <= GeometryTolerance)
                {
                    var HadStaleRoutePoints = Best.IsDegraded &&
                        (Item.Representation.VisualConnectors ?? Enumerable.Empty<VisualConnector>())
                        .Any(Connector => Connector != null && Connector.RoutePoints != null &&
                                          Connector.RoutePoints.Count > 0);
                    if (Best.IsDegraded)
                        ClearStaleRoutePoints(Item.Representation);
                    if (HadStaleRoutePoints)
                    {
                        Item.Representation.Render();
                        Result.RelationshipCentersRecomputed++;
                        Result.RecomputedRepresentations.Add(Item.Representation);
                        Result.HasMutations = true;
                    }
                    else
                    {
                        Result.RelationshipCentersPreserved++;
                        Item.Symbol.RenderElement();
                    }
                }
                else
                {
                    Item.Symbol.MoveTo(Best.Center.X, Best.Center.Y, true);
                    ClearStaleRoutePoints(Item.Representation);
                    Item.Representation.Render();
                    Result.RelationshipCentersRecomputed++;
                    Result.RecomputedRepresentations.Add(Item.Representation);
                    Result.HasMutations = true;
                }

                Console.WriteLine("Relationship center placement move: relationship={0}; mode={1}; suspicious={2}; reason={3}; oldCenter={4}; midpoint={5}; newCenter={6}; displacement={7:0.##}; corridor={8}; insideCorridor={9}; candidate={10}; score={11:0.###}.",
                                  DescribeIdea(Item.Relationship),
                                  Options.PlacementMode,
                                  Item.IsSuspicious ? "true" : "false",
                                  Item.SuspiciousReason.ToStringAlways("recomputed by mode"),
                                  FormatPoint(Item.OldCenter),
                                  FormatPoint(Item.Midpoint),
                                  FormatPoint(Best.Center),
                                  Distance(Item.Midpoint, Best.Center),
                                  FormatRect(Item.Corridor),
                                  Item.Corridor.Contains(Best.Center) ? "true" : "false",
                                  Best.Label,
                                  Best.Score);
            }

            Result.FinalRelationshipOverlapCount = CountRelationshipOverlaps(PlacedRelationshipBounds.Values.ToList(),
                                                                             Options.RelationshipCenterOverlapPadding);
            Console.WriteLine("Relationship center placement summary: inspected={0}; recomputed={1}; preserved={2}; skipped={3}; suspicious={4}; finalRelationshipOverlaps={5}; warnings={6}.",
                              Result.RelationshipCentersInspected,
                              Result.RelationshipCentersRecomputed,
                              Result.RelationshipCentersPreserved,
                              Result.RelationshipCentersSkipped,
                              Result.SuspiciousRelationshipCenters,
                              Result.FinalRelationshipOverlapCount,
                              Result.Warnings.Count);
            Console.WriteLine("END THINKCOMPOSER LAYOUT ROUTING REPORT");

            return Result;
        }

        private static bool TryBuildItem(RelationshipVisualRepresentation Representation, IDictionary<Idea, VisualSymbol> ConceptByIdea,
                                         RelationshipVisualPlacementOptions Options, out RelationshipPlacementItem Item, out string SkipReason)
        {
            Item = null;
            SkipReason = null;

            if (Representation == null || Representation.RepresentedRelationship == null)
            {
                SkipReason = "relationship representation is missing its represented relationship";
                return false;
            }

            var Symbol = Representation.MainSymbol;
            if (!IsUsableVisibleSymbol(Symbol))
            {
                SkipReason = "relationship central symbol is hidden or not visible";
                return false;
            }

            var OriginIdeas = Representation.RepresentedRelationship.Links == null
                              ? new List<Idea>()
                              : Representation.RepresentedRelationship.Links
                                    .Where(Link => Link != null &&
                                                   Link.RoleDefinitor != null &&
                                                   Link.RoleDefinitor.RoleType == ERoleType.Origin &&
                                                   Link.AssociatedIdea != null)
                                    .Select(Link => Link.AssociatedIdea)
                                    .Distinct()
                                    .ToList();
            var TargetIdeas = Representation.RepresentedRelationship.Links == null
                              ? new List<Idea>()
                              : Representation.RepresentedRelationship.Links
                                    .Where(Link => Link != null &&
                                                   Link.RoleDefinitor != null &&
                                                   Link.RoleDefinitor.RoleType == ERoleType.Target &&
                                                   Link.AssociatedIdea != null)
                                    .Select(Link => Link.AssociatedIdea)
                                    .Distinct()
                                    .ToList();

            var OriginSymbols = OriginIdeas.Where(ConceptByIdea.ContainsKey).Select(Idea => ConceptByIdea[Idea]).Distinct().ToList();
            var TargetSymbols = TargetIdeas.Where(ConceptByIdea.ContainsKey).Select(Idea => ConceptByIdea[Idea]).Distinct().ToList();

            // Prefer the actual endpoint representations connected to this hub. This is
            // essential when the same Idea is represented more than once in a View and for
            // n-ary relationships where an Idea-only lookup loses endpoint identity.
            var VisualConnectors = Representation.VisualConnectors == null
                                   ? new List<VisualConnector>()
                                   : Representation.VisualConnectors.Where(Connector => Connector != null).ToList();
            var ConnectorOrigins = VisualConnectors
                .Where(Connector => Connector.TargetSymbol == Symbol && IsUsableVisibleSymbol(Connector.OriginSymbol))
                .Select(Connector => Connector.OriginSymbol).Distinct().ToList();
            var ConnectorTargets = VisualConnectors
                .Where(Connector => Connector.OriginSymbol == Symbol && IsUsableVisibleSymbol(Connector.TargetSymbol))
                .Select(Connector => Connector.TargetSymbol).Distinct().ToList();
            if (ConnectorOrigins.Count > 0)
                OriginSymbols = ConnectorOrigins;
            if (ConnectorTargets.Count > 0)
                TargetSymbols = ConnectorTargets;

            var EndpointSymbols = OriginSymbols.Concat(TargetSymbols).Where(Endpoint => Endpoint != null).Distinct().ToList();
            var ConnectorEndpointLegCount = VisualConnectors.Count(Connector =>
                Connector.OriginSymbol == Symbol && IsUsableVisibleSymbol(Connector.TargetSymbol) ||
                Connector.TargetSymbol == Symbol && IsUsableVisibleSymbol(Connector.OriginSymbol));
            if (!IsPlaceableEndpointTopology(EndpointSymbols.Count, ConnectorEndpointLegCount))
            {
                SkipReason = "relationship has fewer than two visible endpoint connector legs in the active view";
                return false;
            }

            var IsSelfReference = EndpointSymbols.Count == 1;
            var OriginSymbol = OriginSymbols.FirstOrDefault() ?? EndpointSymbols[0];
            var TargetSymbol = IsSelfReference
                               ? EndpointSymbols[0]
                               : TargetSymbols.FirstOrDefault(Endpoint => Endpoint != OriginSymbol) ??
                                 EndpointSymbols.First(Endpoint => Endpoint != OriginSymbol);
            var EndpointBounds = EndpointSymbols.Select(Endpoint => Endpoint.TotalArea).ToList();
            var OriginBounds = OriginSymbol.TotalArea;
            var TargetBounds = TargetSymbol.TotalArea;
            var SymbolBounds = Symbol.TotalArea;
            if (EndpointBounds.Any(Bounds => !IsUsableRect(Bounds)) || !IsUsableRect(SymbolBounds))
            {
                SkipReason = "relationship or endpoint symbol bounds are not usable";
                return false;
            }

            var Midpoint = EndpointSymbols.Count == 2
                           ? MidpointOf(EndpointSymbols[0].BaseCenter, EndpointSymbols[1].BaseCenter)
                           : CoordinateMedian(EndpointSymbols.Select(Endpoint => Endpoint.BaseCenter));
            var Corridor = EndpointBounds[0];
            foreach (var Bounds in EndpointBounds.Skip(1))
                Corridor.Union(Bounds);
            Corridor.Inflate(Options.CorridorPaddingX, Options.CorridorPaddingY);

            var SuspiciousReason = IsSelfReference
                                   ? GetSuspiciousReasonForSelfReference(Symbol.BaseCenter, Midpoint,
                                                                        EndpointBounds[0], Corridor, Options)
                                   : GetSuspiciousReasonForEndpoints(Symbol.BaseCenter, Midpoint,
                                                                     EndpointSymbols.Select(Endpoint => Endpoint.BaseCenter),
                                                                     Corridor, Options);

            Item = new RelationshipPlacementItem
            {
                Representation = Representation,
                Relationship = Representation.RepresentedRelationship,
                Symbol = Symbol,
                OriginSymbol = OriginSymbol,
                TargetSymbol = TargetSymbol,
                EndpointSymbols = EndpointSymbols,
                OriginBounds = OriginBounds,
                TargetBounds = TargetBounds,
                EndpointBounds = EndpointBounds,
                SymbolBounds = SymbolBounds,
                OldCenter = Symbol.BaseCenter,
                Midpoint = Midpoint,
                Corridor = Corridor,
                IsSelfReference = IsSelfReference,
                IsSuspicious = !String.IsNullOrWhiteSpace(SuspiciousReason),
                SuspiciousReason = SuspiciousReason
            };

            return true;
        }

        internal static bool IsPlaceableEndpointTopology(int DistinctEndpointCount, int ConnectorLegCount)
        {
            return DistinctEndpointCount >= 2 ||
                   DistinctEndpointCount == 1 && ConnectorLegCount >= 2;
        }

        /// <summary>
        /// Evaluates a persisted relationship hub against the actual symbols connected to it,
        /// without moving or otherwise mutating the visual model.  The routing validator uses
        /// this to catch the original failure mode where two individually straight connector
        /// legs lead to a hub thousands of pixels away from both endpoint concepts.
        /// </summary>
        internal static bool TryEvaluateRelationshipCenter(RelationshipVisualRepresentation Representation,
                                                           RelationshipVisualPlacementOptions Options,
                                                           out Point ReferenceCenter,
                                                           out Rect EndpointCorridor,
                                                           out int EndpointCount,
                                                           out double NormalizedDistance,
                                                           out string SuspiciousReason)
        {
            ReferenceCenter = new Point(Double.NaN, Double.NaN);
            EndpointCorridor = Rect.Empty;
            EndpointCount = 0;
            NormalizedDistance = 0.0;
            SuspiciousReason = null;
            Options = Options ?? new RelationshipVisualPlacementOptions();

            // Validation also needs to inspect the hidden junction used by simple logical
            // relationships; a distant hidden hub creates the same enormous connector sweep.
            if (Representation == null || Representation.MainSymbol == null ||
                !IsUsableRect(Representation.MainSymbol.TotalArea))
                return false;

            var Main = Representation.MainSymbol;
            var EndpointLegs = (Representation.VisualConnectors ?? Enumerable.Empty<VisualConnector>())
                .Where(Connector => Connector != null)
                .Select(Connector => Connector.OriginSymbol == Main
                                     ? Connector.TargetSymbol
                                     : (Connector.TargetSymbol == Main ? Connector.OriginSymbol : null))
                .Where(IsUsableVisibleSymbol)
                .ToList();
            var Endpoints = EndpointLegs
                .Distinct()
                .ToList();
            if (EndpointLegs.Count < 2 || Endpoints.Count < 1)
                return false;

            var EndpointCenters = Endpoints.Select(Endpoint => Endpoint.BaseCenter).ToList();
            var EndpointBounds = Endpoints.Select(Endpoint => Endpoint.TotalArea).ToList();
            EndpointCount = Endpoints.Count;
            ReferenceCenter = Endpoints.Count == 1
                              ? EndpointCenters[0]
                              : Endpoints.Count == 2
                              ? MidpointOf(EndpointCenters[0], EndpointCenters[1])
                              : CoordinateMedian(EndpointCenters);
            EndpointCorridor = EndpointBounds[0];
            foreach (var Bounds in EndpointBounds.Skip(1))
                EndpointCorridor.Union(Bounds);
            EndpointCorridor.Inflate(Options.CorridorPaddingX, Options.CorridorPaddingY);

            var Span = Endpoints.Count == 1
                       ? Math.Max(GeometryTolerance,
                                  Math.Max(EndpointBounds[0].Width, EndpointBounds[0].Height))
                       : GeometryTolerance;
            for (var First = 0; First < EndpointCenters.Count; First++)
                for (var Second = First + 1; Second < EndpointCenters.Count; Second++)
                    Span = Math.Max(Span, Distance(EndpointCenters[First], EndpointCenters[Second]));
            NormalizedDistance = Distance(Main.BaseCenter, ReferenceCenter) / Span;
            SuspiciousReason = Endpoints.Count == 1
                               ? GetSuspiciousReasonForSelfReference(Main.BaseCenter, ReferenceCenter,
                                                                    EndpointBounds[0], EndpointCorridor, Options)
                               : GetSuspiciousReasonForEndpoints(Main.BaseCenter, ReferenceCenter,
                                                                 EndpointCenters, EndpointCorridor, Options);
            return true;
        }

        internal static string GetSuspiciousRelationshipCenterReason(Point Center,
                                                                      IEnumerable<Point> EndpointCenters,
                                                                      IEnumerable<Rect> EndpointBounds,
                                                                      RelationshipVisualPlacementOptions Options)
        {
            Options = Options ?? new RelationshipVisualPlacementOptions();
            var Centers = (EndpointCenters ?? Enumerable.Empty<Point>()).ToList();
            var Bounds = (EndpointBounds ?? Enumerable.Empty<Rect>()).ToList();
            if (!IsUsablePoint(Center) || Centers.Count < 1 || Bounds.Count != Centers.Count ||
                Centers.Any(Point => !IsUsablePoint(Point)) || Bounds.Any(Rectangle => !IsUsableRect(Rectangle)))
                return "relationship center or endpoint geometry is invalid";

            var Reference = Centers.Count == 1
                            ? Centers[0]
                            : Centers.Count == 2
                            ? MidpointOf(Centers[0], Centers[1])
                            : CoordinateMedian(Centers);
            var Corridor = Bounds[0];
            foreach (var Rectangle in Bounds.Skip(1))
                Corridor.Union(Rectangle);
            Corridor.Inflate(Options.CorridorPaddingX, Options.CorridorPaddingY);
            return Centers.Count == 1
                   ? GetSuspiciousReasonForSelfReference(Center, Reference, Bounds[0], Corridor, Options)
                   : GetSuspiciousReasonForEndpoints(Center, Reference, Centers, Corridor, Options);
        }

        private static bool ShouldRecompute(RelationshipPlacementItem Item, RelationshipVisualPlacementOptions Options)
        {
            var Mode = GetPlacementModeForItem(Item, Options);

            if (StringEquals(Mode, RelationshipVisualPlacementOptions.ModeExplicit))
                return false;

            if (StringEquals(Mode, RelationshipVisualPlacementOptions.ModeDefer))
                return false;

            if (StringEquals(Mode, RelationshipVisualPlacementOptions.ModeMidpoint) ||
                StringEquals(Mode, RelationshipVisualPlacementOptions.ModeEndpointCorridor))
                return true;

            if (StringEquals(Mode, RelationshipVisualPlacementOptions.ModeHideGeneric))
                return true;

            return Options.RecomputeSuspiciousRelationshipVisuals && Item.IsSuspicious;
        }

        private static string GetPlacementModeForItem(RelationshipPlacementItem Item, RelationshipVisualPlacementOptions Options)
        {
            if (Item != null && Item.Relationship != null &&
                Options != null && Options.RelationshipPlacementModesByTechName != null)
            {
                string Mode;
                if (!String.IsNullOrEmpty(Item.Relationship.TechName) &&
                    Options.RelationshipPlacementModesByTechName.TryGetValue(Item.Relationship.TechName, out Mode))
                    return NormalizeMode(Mode);

                var Id = Item.Relationship.GlobalId.ToString("D");
                if (Options.RelationshipPlacementModesByTechName.TryGetValue(Id, out Mode))
                    return NormalizeMode(Mode);
            }

            return Options == null ? RelationshipVisualPlacementOptions.ModeAuto : NormalizeMode(Options.PlacementMode);
        }

        private static bool TryChooseCandidate(RelationshipPlacementItem Item, IList<VisualSymbol> ConceptSymbols, IList<Rect> OtherRelationshipBounds,
                                               RelationshipVisualPlacementOptions Options,
                                               out RelationshipVisualPlacementCandidate Best)
        {
            var EndpointSet = new HashSet<VisualSymbol>(Item.EndpointSymbols ?? new List<VisualSymbol>());
            var ConceptObstacleBounds = ConceptSymbols.Where(Symbol => Symbol != null && !EndpointSet.Contains(Symbol))
                                                      .Select(Symbol =>
                                                      {
                                                          var Bounds = Symbol.TotalArea;
                                                          Bounds.Inflate(Options.RelationshipCenterObstaclePadding,
                                                                         Options.RelationshipCenterObstaclePadding);
                                                          return Bounds;
                                                      })
                                                      .ToList();

            var EndpointBounds = (Item.EndpointBounds ?? new List<Rect> { Item.OriginBounds, Item.TargetBounds })
                .Select(Bounds =>
                {
                    Bounds.Inflate(Options.RelationshipCenterObstaclePadding, Options.RelationshipCenterObstaclePadding);
                    return Bounds;
                })
                .ToList();

            var Candidates = BuildCandidates(Item, Options).ToList();
            var ValidCandidates = new List<RelationshipVisualPlacementCandidate>();
            foreach (var Candidate in Candidates)
            {
                Candidate.Bounds = BoundsAt(Item.SymbolBounds, Candidate.Center);
                Candidate.InsideCorridor = Item.Corridor.Contains(Candidate.Center);
                Candidate.DistanceFromMidpoint = Distance(Candidate.Center, Item.Midpoint);
                Candidate.ConnectorLength = (Item.EndpointSymbols ?? new List<VisualSymbol>
                                            { Item.OriginSymbol, Item.TargetSymbol })
                                            .Where(Symbol => Symbol != null)
                                            .Sum(Symbol => Distance(Symbol.BaseCenter, Candidate.Center));

                string RejectReason;
                if (!ValidateCandidate(Candidate, EndpointBounds, ConceptObstacleBounds, OtherRelationshipBounds, Options, out RejectReason))
                {
                    Console.WriteLine("Relationship center candidate rejected: relationship={0}; candidate={1}; center={2}; reason={3}.",
                                      DescribeIdea(Item.Relationship), Candidate.Label, FormatPoint(Candidate.Center), RejectReason);
                    continue;
                }

                Candidate.Score = ScoreCandidate(Candidate, Item, OtherRelationshipBounds, Options);
                ValidCandidates.Add(Candidate);
            }

            Best = ValidCandidates.OrderBy(Candidate => Candidate.Score)
                                  .ThenBy(Candidate => Candidate.Label, StringComparer.OrdinalIgnoreCase)
                                  .FirstOrDefault();
            return Best != null;
        }

        private static bool TryChooseDegradedCandidate(RelationshipPlacementItem Item,
                                                       IList<VisualSymbol> ConceptSymbols,
                                                       IList<Rect> OtherRelationshipBounds,
                                                       RelationshipVisualPlacementOptions Options,
                                                       out RelationshipVisualPlacementCandidate Best)
        {
            var EndpointSet = new HashSet<VisualSymbol>(Item.EndpointSymbols ?? new List<VisualSymbol>());
            var ConceptObstacleBounds = ConceptSymbols.Where(Symbol => Symbol != null && !EndpointSet.Contains(Symbol))
                .Select(Symbol =>
                {
                    var Bounds = Symbol.TotalArea;
                    Bounds.Inflate(Options.RelationshipCenterObstaclePadding,
                                   Options.RelationshipCenterObstaclePadding);
                    return Bounds;
                }).ToList();
            var EndpointBounds = (Item.EndpointBounds ?? new List<Rect> { Item.OriginBounds, Item.TargetBounds })
                .Select(Bounds =>
                {
                    Bounds.Inflate(Options.RelationshipCenterObstaclePadding,
                                   Options.RelationshipCenterObstaclePadding);
                    return Bounds;
                }).ToList();

            var Candidates = BuildCandidates(Item, Options).ToList();
            foreach (var Candidate in Candidates)
            {
                Candidate.Bounds = BoundsAt(Item.SymbolBounds, Candidate.Center);
                Candidate.InsideCorridor = Item.Corridor.Contains(Candidate.Center);
                Candidate.DistanceFromMidpoint = Distance(Candidate.Center, Item.Midpoint);
                Candidate.ConnectorLength = (Item.EndpointSymbols ?? new List<VisualSymbol>
                                            { Item.OriginSymbol, Item.TargetSymbol })
                                            .Where(Symbol => Symbol != null)
                                            .Sum(Symbol => Distance(Symbol.BaseCenter, Candidate.Center));
                var EndpointCollisions = EndpointBounds.Count(Bounds => Bounds.IntersectsWith(Candidate.Bounds));
                var ConceptCollisions = ConceptObstacleBounds.Count(Bounds => Bounds.IntersectsWith(Candidate.Bounds));
                var RelationshipCollisions = (OtherRelationshipBounds ?? new List<Rect>()).Count(Bounds =>
                {
                    var Expanded = Bounds;
                    Expanded.Inflate(Options.RelationshipCenterOverlapPadding,
                                     Options.RelationshipCenterOverlapPadding);
                    return Expanded.IntersectsWith(Candidate.Bounds);
                });
                Candidate.CollisionCount = EndpointCollisions + ConceptCollisions + RelationshipCollisions;
                Candidate.CollisionScore = EndpointCollisions * 1000000.0 +
                                           ConceptCollisions * 10000.0 +
                                           RelationshipCollisions * 100.0;
                Candidate.Score = Candidate.CollisionScore +
                                  ScoreCandidate(Candidate, Item, OtherRelationshipBounds, Options);
                Candidate.IsDegraded = true;
            }

            Best = SelectLowestCollisionDegradedCandidate(Candidates);
            return Best != null;
        }

        internal static RelationshipVisualPlacementCandidate SelectLowestCollisionDegradedCandidate(
            IEnumerable<RelationshipVisualPlacementCandidate> Candidates)
        {
            return (Candidates ?? Enumerable.Empty<RelationshipVisualPlacementCandidate>())
                .Where(Candidate => Candidate != null && Candidate.InsideCorridor &&
                                    IsUsablePoint(Candidate.Center) && IsUsableRect(Candidate.Bounds))
                .OrderBy(Candidate => Candidate.CollisionScore)
                .ThenBy(Candidate => Candidate.CollisionCount)
                .ThenBy(Candidate => Candidate.Score)
                .ThenBy(Candidate => Candidate.Label, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static IEnumerable<RelationshipVisualPlacementCandidate> BuildCandidates(RelationshipPlacementItem Item,
                                                                                         RelationshipVisualPlacementOptions Options)
        {
            var Direction = new Vector(Item.TargetSymbol.BaseCenter.X - Item.OriginSymbol.BaseCenter.X,
                                       Item.TargetSymbol.BaseCenter.Y - Item.OriginSymbol.BaseCenter.Y);
            if (Direction.Length < GeometryTolerance)
                Direction = new Vector(1.0, 0.0);

            Direction.Normalize();
            var Perpendicular = new Vector(-Direction.Y, Direction.X);

            if (Item.IsSelfReference)
            {
                var SelfCenters = GetSelfReferenceCandidateCenters(Item.EndpointBounds[0], Item.SymbolBounds,
                                                                    Item.Midpoint, Options);
                var Labels = new[] { "self-right", "self-bottom", "self-left", "self-top" };
                for (var Index = 0; Index < SelfCenters.Count; Index++)
                    yield return Candidate(SelfCenters[Index], Labels[Index]);
            }

            yield return Candidate(Item.Midpoint, "midpoint");
            yield return Candidate(ClampToRect(Item.Midpoint, Item.Corridor), "corridor-clamped-midpoint");

            var Steps = new[] { 40.0, 80.0, 120.0 };
            foreach (var Step in Steps)
            {
                yield return Candidate(Item.Midpoint + Perpendicular * Step, "perpendicular+" + Step.ToString("0", CultureInfo.InvariantCulture));
                yield return Candidate(Item.Midpoint - Perpendicular * Step, "perpendicular-" + Step.ToString("0", CultureInfo.InvariantCulture));
                yield return Candidate(Item.Midpoint + Direction * Step, "along+" + Step.ToString("0", CultureInfo.InvariantCulture));
                yield return Candidate(Item.Midpoint - Direction * Step, "along-" + Step.ToString("0", CultureInfo.InvariantCulture));
                yield return Candidate(Item.Midpoint + new Vector(Step, 0), "horizontal+" + Step.ToString("0", CultureInfo.InvariantCulture));
                yield return Candidate(Item.Midpoint - new Vector(Step, 0), "horizontal-" + Step.ToString("0", CultureInfo.InvariantCulture));
                yield return Candidate(Item.Midpoint + new Vector(0, Step), "vertical+" + Step.ToString("0", CultureInfo.InvariantCulture));
                yield return Candidate(Item.Midpoint - new Vector(0, Step), "vertical-" + Step.ToString("0", CultureInfo.InvariantCulture));
            }
        }

        internal static IList<Point> GetSelfReferenceCandidateCenters(Rect EndpointBounds, Rect SymbolBounds,
                                                                       Point EndpointCenter,
                                                                       RelationshipVisualPlacementOptions Options)
        {
            Options = Options ?? new RelationshipVisualPlacementOptions();
            var Clearance = Math.Max(1.0, Options.RelationshipCenterObstaclePadding + 1.0);
            return new List<Point>
            {
                new Point(EndpointBounds.Right + Clearance + SymbolBounds.Width / 2.0, EndpointCenter.Y),
                new Point(EndpointCenter.X, EndpointBounds.Bottom + Clearance + SymbolBounds.Height / 2.0),
                new Point(EndpointBounds.Left - Clearance - SymbolBounds.Width / 2.0, EndpointCenter.Y),
                new Point(EndpointCenter.X, EndpointBounds.Top - Clearance - SymbolBounds.Height / 2.0)
            };
        }

        private static RelationshipVisualPlacementCandidate Candidate(Point Center, string Label)
        {
            return new RelationshipVisualPlacementCandidate { Center = Center, Label = Label };
        }

        private static bool ValidateCandidate(RelationshipVisualPlacementCandidate Candidate, IList<Rect> EndpointBounds,
                                              IList<Rect> ConceptObstacleBounds, IList<Rect> OtherRelationshipBounds,
                                              RelationshipVisualPlacementOptions Options, out string RejectReason)
        {
            RejectReason = null;

            if (Options.RequireInsideEndpointCorridor && !Candidate.InsideCorridor)
            {
                RejectReason = "outside endpoint corridor";
                return false;
            }

            if (Options.MaxRelationshipCenterDisplacement > 0 &&
                Candidate.DistanceFromMidpoint > Options.MaxRelationshipCenterDisplacement)
            {
                RejectReason = "outside maximum midpoint displacement";
                return false;
            }

            if (EndpointBounds.Any(Bounds => Bounds.IntersectsWith(Candidate.Bounds)))
            {
                RejectReason = "overlaps endpoint concept";
                return false;
            }

            if (ConceptObstacleBounds.Any(Bounds => Bounds.IntersectsWith(Candidate.Bounds)))
            {
                RejectReason = "overlaps concept obstacle";
                return false;
            }

            return true;
        }

        private static double ScoreCandidate(RelationshipVisualPlacementCandidate Candidate, RelationshipPlacementItem Item,
                                             IList<Rect> OtherRelationshipBounds, RelationshipVisualPlacementOptions Options)
        {
            var Score = Candidate.DistanceFromMidpoint * 6.0 + Candidate.ConnectorLength * 0.05;
            if (!Candidate.InsideCorridor)
                Score += 10000.0;

            var Inflated = Candidate.Bounds;
            Inflated.Inflate(Options.RelationshipCenterOverlapPadding, Options.RelationshipCenterOverlapPadding);
            var Overlaps = OtherRelationshipBounds.Count(Bounds =>
            {
                var Other = Bounds;
                Other.Inflate(Options.RelationshipCenterOverlapPadding, Options.RelationshipCenterOverlapPadding);
                return Other.IntersectsWith(Inflated);
            });
            Score += Overlaps * 2500.0;
            Score += Distance(Candidate.Center, Item.OldCenter) * 0.05;
            return Score;
        }

        private static void ClearStaleRoutePoints(RelationshipVisualRepresentation Representation)
        {
            if (Representation == null)
                return;

            foreach (var Connector in Representation.VisualConnectors.Where(Connector => Connector != null))
                if (Connector.RoutePoints != null && Connector.RoutePoints.Count > 0)
                    Connector.ClearRoutePoints();
        }

        private static string GetSuspiciousReason(Point Center, Point Midpoint, Point Origin, Point Target, Rect Corridor,
                                                  RelationshipVisualPlacementOptions Options)
        {
            var MidpointDistance = Distance(Center, Midpoint);
            var EndpointDistance = Math.Max(Distance(Origin, Target), GeometryTolerance);

            if (MidpointDistance > EndpointDistance * Options.SuspiciousDistanceMultiplier)
                return MidpointDistance > Options.SuspiciousDistanceThreshold
                       ? "center is farther than suspicious distance threshold and endpoint-relative limit"
                       : "center is too far from endpoint midpoint relative to endpoint distance";

            if (!Corridor.Contains(Center))
                return "center is outside endpoint corridor";

            return null;
        }

        private static string GetSuspiciousReasonForEndpoints(Point Center, Point Midpoint,
                                                              IEnumerable<Point> Endpoints, Rect Corridor,
                                                              RelationshipVisualPlacementOptions Options)
        {
            var Points = (Endpoints ?? Enumerable.Empty<Point>()).ToList();
            if (Points.Count == 2)
                return GetSuspiciousReason(Center, Midpoint, Points[0], Points[1], Corridor, Options);

            var MidpointDistance = Distance(Center, Midpoint);
            var EndpointSpan = GeometryTolerance;
            for (var First = 0; First < Points.Count; First++)
                for (var Second = First + 1; Second < Points.Count; Second++)
                    EndpointSpan = Math.Max(EndpointSpan, Distance(Points[First], Points[Second]));

            if (MidpointDistance > EndpointSpan * Options.SuspiciousDistanceMultiplier)
                return MidpointDistance > Options.SuspiciousDistanceThreshold
                       ? "center is farther than suspicious distance threshold and endpoint-relative limit"
                       : "center is too far from endpoint median relative to endpoint span";
            if (!Corridor.Contains(Center))
                return "center is outside the multi-endpoint corridor";
            return null;
        }

        private static string GetSuspiciousReasonForSelfReference(Point Center, Point EndpointCenter,
                                                                  Rect EndpointBounds, Rect Corridor,
                                                                  RelationshipVisualPlacementOptions Options)
        {
            var DistanceFromEndpoint = Distance(Center, EndpointCenter);
            var EndpointSpan = Math.Max(GeometryTolerance,
                                        Math.Max(EndpointBounds.Width, EndpointBounds.Height));
            // A self-reference junction must sit outside its endpoint symbol so the two legs
            // form a visible loop, but it must remain in the local inflated corridor.
            if (DistanceFromEndpoint > Options.SuspiciousDistanceThreshold &&
                DistanceFromEndpoint > EndpointSpan * Math.Max(4.0, Options.SuspiciousDistanceMultiplier))
                return "self-reference center is excessively distant from its endpoint";
            if (!Corridor.Contains(Center))
                return "self-reference center is outside its local endpoint corridor";
            return null;
        }

        private static Point CoordinateMedian(IEnumerable<Point> Source)
        {
            var Points = (Source ?? Enumerable.Empty<Point>()).ToList();
            if (Points.Count == 0)
                return new Point(0, 0);
            var X = Points.Select(Point => Point.X).OrderBy(Value => Value).ToList();
            var Y = Points.Select(Point => Point.Y).OrderBy(Value => Value).ToList();
            var Middle = Points.Count / 2;
            return Points.Count % 2 == 1
                   ? new Point(X[Middle], Y[Middle])
                   : new Point((X[Middle - 1] + X[Middle]) / 2.0,
                               (Y[Middle - 1] + Y[Middle]) / 2.0);
        }

        private static int CountRelationshipOverlaps(IList<Rect> Bounds, double Padding)
        {
            var Count = 0;
            for (int First = 0; First < Bounds.Count; First++)
                for (int Second = First + 1; Second < Bounds.Count; Second++)
                {
                    var A = Bounds[First];
                    var B = Bounds[Second];
                    A.Inflate(Padding, Padding);
                    B.Inflate(Padding, Padding);
                    if (A.IntersectsWith(B))
                        Count++;
                }

            return Count;
        }

        private static bool IsUsableVisibleSymbol(VisualSymbol Symbol)
        {
            return Symbol != null &&
                   !Symbol.IsHidden &&
                   Symbol.IsRelatedVisible &&
                   IsUsableRect(Symbol.TotalArea);
        }

        private static bool IsUsableRect(Rect Rect)
        {
            return !Double.IsNaN(Rect.X) &&
                   !Double.IsInfinity(Rect.X) &&
                   !Double.IsNaN(Rect.Y) &&
                   !Double.IsInfinity(Rect.Y) &&
                   !Double.IsNaN(Rect.Width) &&
                   !Double.IsInfinity(Rect.Width) &&
                   !Double.IsNaN(Rect.Height) &&
                   !Double.IsInfinity(Rect.Height) &&
                   Rect.Width > GeometryTolerance &&
                   Rect.Height > GeometryTolerance;
        }

        private static bool IsUsablePoint(Point Point)
        {
            return !Double.IsNaN(Point.X) && !Double.IsInfinity(Point.X) &&
                   !Double.IsNaN(Point.Y) && !Double.IsInfinity(Point.Y);
        }

        private static Rect Union(Rect First, Rect Second)
        {
            var Result = First;
            Result.Union(Second);
            return Result;
        }

        private static Rect BoundsAt(Rect OriginalBounds, Point Center)
        {
            return new Rect(Center.X - OriginalBounds.Width / 2.0,
                            Center.Y - OriginalBounds.Height / 2.0,
                            OriginalBounds.Width,
                            OriginalBounds.Height);
        }

        private static Point MidpointOf(Point First, Point Second)
        {
            return new Point((First.X + Second.X) / 2.0, (First.Y + Second.Y) / 2.0);
        }

        private static Point ClampToRect(Point Point, Rect Rect)
        {
            return new Point(Point.X.EnforceRange(Rect.Left, Rect.Right),
                             Point.Y.EnforceRange(Rect.Top, Rect.Bottom));
        }

        private static double Distance(Point First, Point Second)
        {
            var DeltaX = First.X - Second.X;
            var DeltaY = First.Y - Second.Y;
            return Math.Sqrt(DeltaX * DeltaX + DeltaY * DeltaY);
        }

        private static string NormalizeMode(string Mode)
        {
            if (String.IsNullOrWhiteSpace(Mode))
                return RelationshipVisualPlacementOptions.ModeAuto;

            var Normalized = Mode.Trim();
            if (StringEquals(Normalized, RelationshipVisualPlacementOptions.ModeExplicit))
                return RelationshipVisualPlacementOptions.ModeExplicit;
            if (StringEquals(Normalized, RelationshipVisualPlacementOptions.ModeMidpoint))
                return RelationshipVisualPlacementOptions.ModeMidpoint;
            if (StringEquals(Normalized, RelationshipVisualPlacementOptions.ModeEndpointCorridor) ||
                StringEquals(Normalized, "endpoint-corridor"))
                return RelationshipVisualPlacementOptions.ModeEndpointCorridor;
            if (StringEquals(Normalized, RelationshipVisualPlacementOptions.ModeHideGeneric) ||
                StringEquals(Normalized, "hide-generic"))
                return RelationshipVisualPlacementOptions.ModeHideGeneric;
            if (StringEquals(Normalized, RelationshipVisualPlacementOptions.ModeDefer))
                return RelationshipVisualPlacementOptions.ModeDefer;

            return RelationshipVisualPlacementOptions.ModeAuto;
        }

        private static bool StringEquals(string First, string Second)
        {
            return String.Equals(First, Second, StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatPoint(Point Point)
        {
            return "(" + Point.X.ToString("0.##", CultureInfo.InvariantCulture) + "," +
                   Point.Y.ToString("0.##", CultureInfo.InvariantCulture) + ")";
        }

        private static string FormatRect(Rect Rect)
        {
            return "x=" + Rect.X.ToString("0.##", CultureInfo.InvariantCulture) +
                   " y=" + Rect.Y.ToString("0.##", CultureInfo.InvariantCulture) +
                   " width=" + Rect.Width.ToString("0.##", CultureInfo.InvariantCulture) +
                   " height=" + Rect.Height.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string DescribeIdea(Idea Idea)
        {
            if (Idea == null)
                return "<none>";

            return Idea.Name.ToStringAlways(Idea.TechName.ToStringAlways(Idea.GlobalId.ToString()));
        }
    }

    public class RelationshipVisualPlacementOptions
    {
        public const string ModeExplicit = "explicit";
        public const string ModeMidpoint = "midpoint";
        public const string ModeEndpointCorridor = "endpointCorridor";
        public const string ModeAuto = "auto";
        public const string ModeHideGeneric = "hideGeneric";
        public const string ModeDefer = "defer";

        public RelationshipVisualPlacementOptions()
        {
            this.PlacementMode = ModeAuto;
            this.RecomputeSuspiciousRelationshipVisuals = true;
            this.HideGenericRelationshipCenters = false;
            this.MaxRelationshipCenterDisplacement = 250.0;
            this.RelationshipCenterObstaclePadding = 16.0;
            this.RelationshipCenterOverlapPadding = 8.0;
            this.CorridorPaddingX = 80.0;
            this.CorridorPaddingY = 60.0;
            this.SuspiciousDistanceThreshold = 700.0;
            this.SuspiciousDistanceMultiplier = 2.0;
            this.RequireInsideEndpointCorridor = true;
            this.RelationshipPlacementModesByTechName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string PlacementMode { get; set; }
        public bool RecomputeSuspiciousRelationshipVisuals { get; set; }
        public bool HideGenericRelationshipCenters { get; set; }
        public double MaxRelationshipCenterDisplacement { get; set; }
        public double RelationshipCenterObstaclePadding { get; set; }
        public double RelationshipCenterOverlapPadding { get; set; }
        public double CorridorPaddingX { get; set; }
        public double CorridorPaddingY { get; set; }
        public double SuspiciousDistanceThreshold { get; set; }
        public double SuspiciousDistanceMultiplier { get; set; }
        public bool RequireInsideEndpointCorridor { get; set; }
        public IDictionary<string, string> RelationshipPlacementModesByTechName { get; set; }
    }

    public class RelationshipVisualPlacementResult
    {
        public RelationshipVisualPlacementResult()
        {
            this.Warnings = new List<string>();
            this.Issues = new List<RelationshipVisualPlacementIssue>();
            this.RecomputedRepresentations = new List<RelationshipVisualRepresentation>();
        }

        public int RelationshipCentersInspected { get; set; }
        public int RelationshipCentersRecomputed { get; set; }
        public int RelationshipCentersPreserved { get; set; }
        public int RelationshipCentersSkipped { get; set; }
        public int RelationshipCentersHiddenOrDeferred { get; set; }
        public int SuspiciousRelationshipCenters { get; set; }
        public int FinalRelationshipOverlapCount { get; set; }
        public bool HasMutations { get; set; }
        public IList<string> Warnings { get; private set; }
        public IList<RelationshipVisualPlacementIssue> Issues { get; private set; }
        public IList<RelationshipVisualRepresentation> RecomputedRepresentations { get; private set; }

        public void AddWarning(string Warning)
        {
            if (!String.IsNullOrWhiteSpace(Warning))
                this.Warnings.Add(Warning);
        }

        public void AddIssue(RelationshipVisualPlacementIssue Issue)
        {
            if (Issue == null)
                return;

            this.Issues.Add(Issue);
            if (String.Equals(Issue.Severity, "Warning", StringComparison.OrdinalIgnoreCase))
                this.AddWarning(Issue.Message);
        }
    }

    public class RelationshipVisualPlacementCandidate
    {
        public string Label { get; set; }
        public Point Center { get; set; }
        public Rect Bounds { get; set; }
        public bool InsideCorridor { get; set; }
        public double DistanceFromMidpoint { get; set; }
        public double ConnectorLength { get; set; }
        public int CollisionCount { get; set; }
        public double CollisionScore { get; set; }
        public bool IsDegraded { get; set; }
        public double Score { get; set; }
    }

    public class RelationshipVisualPlacementIssue
    {
        public string RelationshipTechName { get; set; }
        public string Severity { get; set; }
        public string Message { get; set; }
    }
}
