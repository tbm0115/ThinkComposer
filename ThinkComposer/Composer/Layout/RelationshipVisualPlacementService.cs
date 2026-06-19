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
            public Rect OriginBounds;
            public Rect TargetBounds;
            public Rect SymbolBounds;
            public Point OldCenter;
            public Point Midpoint;
            public Rect Corridor;
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
            foreach (var Item in Items.OrderBy(Item => DescribeIdea(Item.Relationship), StringComparer.OrdinalIgnoreCase))
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
                    Result.RelationshipCentersSkipped++;
                    Result.AddWarning("No safe endpoint-corridor candidate was found for relationship '" + DescribeIdea(Item.Relationship) + "'.");
                    Console.WriteLine("Relationship center placement warning: relationship={0}; no safe candidate near midpoint={1}; corridor={2}.",
                                      DescribeIdea(Item.Relationship), FormatPoint(Item.Midpoint), FormatRect(Item.Corridor));
                    continue;
                }

                var NewBounds = BoundsAt(Item.SymbolBounds, Best.Center);
                PlacedRelationshipBounds[Item] = NewBounds;

                if (Distance(Item.OldCenter, Best.Center) <= GeometryTolerance)
                {
                    Result.RelationshipCentersPreserved++;
                    Item.Symbol.RenderElement();
                }
                else
                {
                    Item.Symbol.MoveTo(Best.Center.X, Best.Center.Y, true);
                    ClearStaleIntermediatePositions(Item.Representation);
                    Item.Representation.Render();
                    Result.RelationshipCentersRecomputed++;
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

            if (OriginSymbols.Count != 1 || TargetSymbols.Count != 1)
            {
                SkipReason = "relationship does not have exactly one visible origin and one visible target concept in the active view";
                return false;
            }

            var OriginBounds = OriginSymbols[0].TotalArea;
            var TargetBounds = TargetSymbols[0].TotalArea;
            var SymbolBounds = Symbol.TotalArea;
            if (!IsUsableRect(OriginBounds) || !IsUsableRect(TargetBounds) || !IsUsableRect(SymbolBounds))
            {
                SkipReason = "relationship or endpoint symbol bounds are not usable";
                return false;
            }

            var Midpoint = MidpointOf(OriginSymbols[0].BaseCenter, TargetSymbols[0].BaseCenter);
            var Corridor = Union(OriginBounds, TargetBounds);
            Corridor.Inflate(Options.CorridorPaddingX, Options.CorridorPaddingY);

            var SuspiciousReason = GetSuspiciousReason(Symbol.BaseCenter, Midpoint, OriginSymbols[0].BaseCenter,
                                                       TargetSymbols[0].BaseCenter, Corridor, Options);

            Item = new RelationshipPlacementItem
            {
                Representation = Representation,
                Relationship = Representation.RepresentedRelationship,
                Symbol = Symbol,
                OriginSymbol = OriginSymbols[0],
                TargetSymbol = TargetSymbols[0],
                OriginBounds = OriginBounds,
                TargetBounds = TargetBounds,
                SymbolBounds = SymbolBounds,
                OldCenter = Symbol.BaseCenter,
                Midpoint = Midpoint,
                Corridor = Corridor,
                IsSuspicious = !String.IsNullOrWhiteSpace(SuspiciousReason),
                SuspiciousReason = SuspiciousReason
            };

            return true;
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
            var ConceptObstacleBounds = ConceptSymbols.Where(Symbol => Symbol != null &&
                                                                      Symbol != Item.OriginSymbol &&
                                                                      Symbol != Item.TargetSymbol)
                                                      .Select(Symbol =>
                                                      {
                                                          var Bounds = Symbol.TotalArea;
                                                          Bounds.Inflate(Options.RelationshipCenterObstaclePadding,
                                                                         Options.RelationshipCenterObstaclePadding);
                                                          return Bounds;
                                                      })
                                                      .ToList();

            var EndpointBounds = new List<Rect> { Item.OriginBounds, Item.TargetBounds }
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
                Candidate.ConnectorLength = Distance(Item.OriginSymbol.BaseCenter, Candidate.Center) +
                                            Distance(Candidate.Center, Item.TargetSymbol.BaseCenter);

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

        private static IEnumerable<RelationshipVisualPlacementCandidate> BuildCandidates(RelationshipPlacementItem Item,
                                                                                         RelationshipVisualPlacementOptions Options)
        {
            var Direction = new Vector(Item.TargetSymbol.BaseCenter.X - Item.OriginSymbol.BaseCenter.X,
                                       Item.TargetSymbol.BaseCenter.Y - Item.OriginSymbol.BaseCenter.Y);
            if (Direction.Length < GeometryTolerance)
                Direction = new Vector(1.0, 0.0);

            Direction.Normalize();
            var Perpendicular = new Vector(-Direction.Y, Direction.X);

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

        private static void ClearStaleIntermediatePositions(RelationshipVisualRepresentation Representation)
        {
            if (Representation == null)
                return;

            foreach (var Connector in Representation.VisualConnectors.Where(Connector => Connector != null))
                if (Connector.IntermediatePosition != Display.NULL_POINT)
                    Connector.UpdateIntermediatePoint(Display.NULL_POINT);
        }

        private static string GetSuspiciousReason(Point Center, Point Midpoint, Point Origin, Point Target, Rect Corridor,
                                                  RelationshipVisualPlacementOptions Options)
        {
            var MidpointDistance = Distance(Center, Midpoint);
            var EndpointDistance = Math.Max(Distance(Origin, Target), GeometryTolerance);

            if (MidpointDistance > Options.SuspiciousDistanceThreshold)
                return "center is farther than suspicious distance threshold from endpoint midpoint";

            if (MidpointDistance > EndpointDistance * Options.SuspiciousDistanceMultiplier)
                return "center is too far from endpoint midpoint relative to endpoint distance";

            if (!Corridor.Contains(Center))
                return "center is outside endpoint corridor";

            return null;
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
                   !Double.IsNaN(Rect.Y) &&
                   Rect.Width > GeometryTolerance &&
                   Rect.Height > GeometryTolerance;
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
        public double Score { get; set; }
    }

    public class RelationshipVisualPlacementIssue
    {
        public string RelationshipTechName { get; set; }
        public string Severity { get; set; }
        public string Message { get; set; }
    }
}
