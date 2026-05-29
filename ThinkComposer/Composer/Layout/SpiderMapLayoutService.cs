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
using Instrumind.ThinkComposer.Model.GraphModel;
using Instrumind.ThinkComposer.Model.VisualModel;

namespace Instrumind.ThinkComposer.Composer.Layout
{
    /// <summary>
    /// Simple two-ring radial layout for concept-map style diagrams.
    /// </summary>
    public static class SpiderMapLayoutService
    {
        private const double GeometryTolerance = 0.5;

        private class ConceptGraph
        {
            public ConceptGraph()
            {
                this.Adjacency = new Dictionary<VisualSymbol, HashSet<VisualSymbol>>();
                this.RelationshipRepresentations = new List<RelationshipVisualRepresentation>();
            }

            public Dictionary<VisualSymbol, HashSet<VisualSymbol>> Adjacency;
            public List<RelationshipVisualRepresentation> RelationshipRepresentations;
        }

        public static SpiderMapLayoutResult Arrange(LayoutSelectionContext Context, SpiderMapLayoutOptions Options)
        {
            Options = Options ?? new SpiderMapLayoutOptions();
            var Result = new SpiderMapLayoutResult();

            if (Context == null || Context.ActiveView == null)
            {
                Result.AddWarning("No active view is available for Spider Map arrangement.");
                LogSummary(Result);
                return Result;
            }

            var View = Context.ActiveView;
            var LocalCommand = !View.EditEngine.IsVariating;

            try
            {
                if (LocalCommand)
                    View.EditEngine.StartCommandVariation("Arrange as Spider Map");

                var ScopeSymbols = GetScopeSymbols(Context, Options, Result);
                Result.ConceptsInspected = ScopeSymbols.Count;

                Console.WriteLine("Appearance: Arrange as Spider Map starting; view={0}; scope={1}; concepts={2}; firstRingRadius={3:0.##}; secondRingRadius={4:0.##}; minAngularSeparation={5:0.##}; minNodeSpacing={6:0.##}.",
                                  DescribeView(View),
                                  Options.ArrangeSelectedConceptsOnly ? "selected concepts" : "all visible concepts",
                                  ScopeSymbols.Count,
                                  Options.FirstRingRadius,
                                  Options.SecondRingRadius,
                                  Options.MinimumAngularSeparation,
                                  Options.MinimumNodeSpacing);

                if (ScopeSymbols.Count < 1)
                {
                    Result.AddWarning("No concept symbols are available in the requested Spider Map scope.");
                    if (LocalCommand)
                        View.EditEngine.CompleteCommandVariation();
                    LogSummary(Result);
                    return Result;
                }

                if (Options.AutoFitConceptsBeforeArrange)
                    Result.AutoFitResult = ConceptAutoFitService.FitConceptSymbols(Context.Engine, ScopeSymbols, "spider map layout");

                var Graph = BuildVisibleConceptGraph(Context, ScopeSymbols, Result);
                string RootSelectionReason;
                var Root = DetermineRoot(Context, ScopeSymbols, Graph, Options, out RootSelectionReason);
                Result.RootSymbol = Root;
                Result.RootSelectionReason = RootSelectionReason;

                if (Root == null)
                {
                    Result.AddWarning("No valid root concept could be determined for Spider Map arrangement.");
                    if (LocalCommand)
                        View.EditEngine.CompleteCommandVariation();
                    LogSummary(Result);
                    return Result;
                }

                Console.WriteLine("Appearance: Spider Map root selected: {0}; reason={1}.",
                                  DescribeSymbol(Root), Result.RootSelectionReason);

                var RootCenter = DetermineRootCenter(Context, ScopeSymbols, Root, Options);
                Console.WriteLine("Appearance: Spider Map root center: concept={0}; oldCenter=({1:0.##},{2:0.##}); layoutRootCenter=({3:0.##},{4:0.##}); preserveRoot={5}.",
                                  DescribeSymbol(Root),
                                  Root.BaseCenter.X,
                                  Root.BaseCenter.Y,
                                  RootCenter.X,
                                  RootCenter.Y,
                                  Options.PreserveRootPosition ? "true" : "false");
                var PlannedPositions = ComputeRadialPositions(ScopeSymbols, Graph, Root, RootCenter, Options);
                var PlannedBounds = ComputePlannedBounds(PlannedPositions);
                Console.WriteLine("Appearance: Spider Map layout bounds before apply: {0}.",
                                  LayoutBoundsNormalizer.FormatRect(PlannedBounds));

                ApplyLayout(PlannedPositions, Result);

                var NormalizeResult = LayoutBoundsNormalizer.NormalizeSymbolsToCanvas(View, ScopeSymbols, Options.CanvasPadding,
                                                                                      "Appearance: Spider Map");
                Result.BoundsBeforeNormalization = NormalizeResult.BoundsBefore;
                Result.BoundsAfterNormalization = NormalizeResult.BoundsAfter;
                Result.NormalizationDelta = NormalizeResult.Translation;
                Result.BoundsNormalized = NormalizeResult.WasNormalized;
                Result.FinalBoundsWithinSafeCanvas = NormalizeResult.IsWithinSafeBounds;

                if (Options.RouteLinksAfterArrange)
                    Result.RoutingResult = RouteScopeLinks(Context, Graph);

                if (Result.HasMutations)
                    View.UpdateVersion();

                if (LocalCommand)
                    View.EditEngine.CompleteCommandVariation();

                if (LocalCommand && Options.RevealArrangedContent)
                    RevealArrangedBounds(View, Result);

                LogSummary(Result);
                return Result;
            }
            catch (Exception Problem)
            {
                Console.WriteLine("Appearance: Arrange as Spider Map failed. Problem: {0}", Problem.Message);
                Console.WriteLine(Problem.ToString());

                if (LocalCommand && View.EditEngine.IsVariating)
                {
                    try
                    {
                        View.EditEngine.DiscardCommandVariation();
                        Console.WriteLine("Appearance: Arrange as Spider Map discarded its command variation after failure.");
                    }
                    catch (Exception DiscardProblem)
                    {
                        Console.WriteLine("Appearance: Could not discard failed Spider Map command variation. Problem: {0}", DiscardProblem.Message);
                        Console.WriteLine(DiscardProblem.ToString());
                    }
                }

                throw;
            }
        }

        public static bool CanArrange(LayoutSelectionContext Context)
        {
            return Context != null && Context.ActiveView != null && Context.VisibleConceptSymbols.Any(IsRouteableConceptSymbol);
        }

        private static List<VisualSymbol> GetScopeSymbols(LayoutSelectionContext Context, SpiderMapLayoutOptions Options,
                                                          SpiderMapLayoutResult Result)
        {
            var Source = Options.ArrangeSelectedConceptsOnly && Context.SelectedConceptSymbols.Count > 0
                         ? Context.SelectedConceptSymbols
                         : Context.VisibleConceptSymbols;
            var Symbols = new List<VisualSymbol>();

            foreach (var Symbol in Source.Where(IsRouteableConceptSymbol).Distinct())
            {
                var Representation = Symbol.OwnerRepresentation as ConceptVisualRepresentation;
                var Concept = Representation == null ? null : Representation.RepresentedConcept;
                string Warning;
                if (CompositeViewIntegrity.IsSelfRecursiveConceptPlacement(Concept, Context.ActiveView, out Warning))
                {
                    Result.ConceptsSkipped++;
                    Result.AddWarning(Warning);
                    Console.WriteLine("Appearance: Spider Map skipped concept {0}; reason={1}.", DescribeSymbol(Symbol), Warning);
                    continue;
                }

                Symbols.Add(Symbol);
            }

            return Symbols.OrderBy(GetSymbolSortKey).ToList();
        }

        private static ConceptGraph BuildVisibleConceptGraph(LayoutSelectionContext Context, IList<VisualSymbol> ScopeSymbols,
                                                             SpiderMapLayoutResult Result)
        {
            var Graph = new ConceptGraph();
            var ScopeSet = new HashSet<VisualSymbol>(ScopeSymbols);
            var SymbolByIdea = ScopeSymbols.Select(Symbol => new
                                  {
                                      Symbol = Symbol,
                                      Idea = Symbol.OwnerRepresentation == null ? null : Symbol.OwnerRepresentation.RepresentedIdea
                                  })
                                  .Where(Item => Item.Idea != null)
                                  .GroupBy(Item => Item.Idea)
                                  .ToDictionary(Group => Group.Key, Group => Group.First().Symbol);

            foreach (var Symbol in ScopeSymbols)
                Graph.Adjacency[Symbol] = new HashSet<VisualSymbol>();

            foreach (var Representation in Context.VisibleRelationshipRepresentations.Where(Representation => Representation != null).Distinct())
            {
                Result.RelationshipsInspected++;

                var Relationship = Representation.RepresentedRelationship;
                if (Relationship == null || Relationship.Links == null)
                {
                    Result.AddWarning("Skipped a visible relationship representation without a relationship while building Spider Map adjacency.");
                    continue;
                }

                var EndpointSymbols = Relationship.Links
                                                  .Where(Link => Link != null && Link.AssociatedIdea != null)
                                                  .Select(Link => Link.AssociatedIdea)
                                                  .Distinct()
                                                  .Where(Idea => SymbolByIdea.ContainsKey(Idea))
                                                  .Select(Idea => SymbolByIdea[Idea])
                                                  .Distinct()
                                                  .ToList();

                if (EndpointSymbols.Count < 2)
                {
                    Console.WriteLine("Appearance: Spider Map relationship skipped for adjacency: {0}; endpointsInScope={1}.",
                                      DescribeIdea(Relationship), EndpointSymbols.Count);
                    continue;
                }

                var RelationshipAdded = false;
                for (int OriginIndex = 0; OriginIndex < EndpointSymbols.Count; OriginIndex++)
                    for (int TargetIndex = OriginIndex + 1; TargetIndex < EndpointSymbols.Count; TargetIndex++)
                    {
                        var Origin = EndpointSymbols[OriginIndex];
                        var Target = EndpointSymbols[TargetIndex];
                        if (!ScopeSet.Contains(Origin) || !ScopeSet.Contains(Target) || Origin == Target)
                            continue;

                        if (Graph.Adjacency[Origin].Add(Target))
                        {
                            Graph.Adjacency[Target].Add(Origin);
                            Result.EdgesUsed++;
                        }

                        RelationshipAdded = true;
                    }

                if (RelationshipAdded)
                    Graph.RelationshipRepresentations.Add(Representation);
            }

            Graph.RelationshipRepresentations = Graph.RelationshipRepresentations.Distinct().ToList();
            Console.WriteLine("Appearance: Spider Map graph built; relationships inspected={0}; relationship visuals in scope={1}; undirected edges={2}.",
                              Result.RelationshipsInspected, Graph.RelationshipRepresentations.Count, Result.EdgesUsed);
            return Graph;
        }

        private static VisualSymbol DetermineRoot(LayoutSelectionContext Context, IList<VisualSymbol> ScopeSymbols, ConceptGraph Graph,
                                                  SpiderMapLayoutOptions Options, out string Reason)
        {
            Reason = "highest visible relationship degree";

            if (Options.ArrangeSelectedConceptsOnly && Context.SelectedConceptSymbols.Count == 1 &&
                ScopeSymbols.Contains(Context.SelectedConceptSymbols[0]))
            {
                Reason = "exactly one selected concept";
                return Context.SelectedConceptSymbols[0];
            }

            var TieCenter = DetermineTieCenter(Context, ScopeSymbols, Options);
            var Ranked = ScopeSymbols.OrderByDescending(Symbol => GetDegree(Graph, Symbol))
                                     .ThenBy(Symbol => Distance(Symbol.BaseCenter, TieCenter))
                                     .ThenBy(GetSymbolSortKey)
                                     .ToList();

            var Root = Ranked.FirstOrDefault();
            if (Root != null)
            {
                var Degree = GetDegree(Graph, Root);
                Reason = (Options.ArrangeSelectedConceptsOnly && Context.SelectedConceptSymbols.Count > 1)
                         ? "highest selected visible relationship degree (" + Degree.ToString(CultureInfo.InvariantCulture) + "), then closest to selection center"
                         : "highest visible relationship degree (" + Degree.ToString(CultureInfo.InvariantCulture) + "), then closest to view center";
            }

            return Root;
        }

        private static Point DetermineRootCenter(LayoutSelectionContext Context, IList<VisualSymbol> ScopeSymbols, VisualSymbol Root,
                                                 SpiderMapLayoutOptions Options)
        {
            if (Root == null)
                return new Point(0, 0);

            if (Options.PreserveRootPosition && Options.ArrangeSelectedConceptsOnly)
                return Root.BaseCenter;

            var Center = Context.CurrentViewportCenter;
            if (IsUsablePoint(Center))
                return EnforceSafeRootCenter(Center, ScopeSymbols, Options, "viewport center");

            Center = DetermineClusterCenter(ScopeSymbols);
            if (IsUsablePoint(Center))
                return EnforceSafeRootCenter(Center, ScopeSymbols, Options, "visible cluster center");

            return EnforceSafeRootCenter(Options.SafeDefaultRootCenter, ScopeSymbols, Options, "safe default");
        }

        private static Point DetermineTieCenter(LayoutSelectionContext Context, IList<VisualSymbol> ScopeSymbols, SpiderMapLayoutOptions Options)
        {
            if (Options.ArrangeSelectedConceptsOnly && Context.SelectedConceptSymbols.Count > 1)
                return DetermineClusterCenter(Context.SelectedConceptSymbols.Where(ScopeSymbols.Contains).ToList());

            if (IsUsablePoint(Context.CurrentViewportCenter))
                return Context.CurrentViewportCenter;

            return DetermineClusterCenter(ScopeSymbols);
        }

        private static Dictionary<VisualSymbol, Point> ComputeRadialPositions(IList<VisualSymbol> ScopeSymbols, ConceptGraph Graph,
                                                                              VisualSymbol Root, Point RootCenter,
                                                                              SpiderMapLayoutOptions Options)
        {
            var Positions = new Dictionary<VisualSymbol, Point>();
            var Angles = new Dictionary<VisualSymbol, double>();
            Positions[Root] = RootCenter;

            var DirectNeighbors = GetNeighbors(Graph, Root)
                                  .Where(Symbol => ScopeSymbols.Contains(Symbol))
                                  .OrderBy(GetSymbolSortKey)
                                  .ToList();
            var Remaining = ScopeSymbols.Where(Symbol => Symbol != Root && !DirectNeighbors.Contains(Symbol))
                                        .OrderBy(GetSymbolSortKey)
                                        .ToList();

            AssignRingPositions(DirectNeighbors, RootCenter, Options.FirstRingRadius, 0.0, 360.0, Options, Positions, Angles);

            var Groups = new Dictionary<VisualSymbol, List<VisualSymbol>>();
            foreach (var Symbol in Remaining)
            {
                var Anchor = DirectNeighbors.FirstOrDefault(Neighbor => GetNeighbors(Graph, Symbol).Contains(Neighbor));
                if (Anchor == null)
                    Anchor = Root;

                List<VisualSymbol> Group;
                if (!Groups.TryGetValue(Anchor, out Group))
                {
                    Group = new List<VisualSymbol>();
                    Groups[Anchor] = Group;
                }

                Group.Add(Symbol);
            }

            foreach (var Pair in Groups.OrderBy(Group => Group.Key == Root ? String.Empty : GetSymbolSortKey(Group.Key)))
            {
                var Anchor = Pair.Key;
                var Group = Pair.Value.OrderBy(GetSymbolSortKey).ToList();
                if (Anchor == Root)
                {
                    AssignRingPositions(Group, RootCenter, Options.SecondRingRadius, 0.0, 360.0, Options, Positions, Angles);
                    continue;
                }

                double AnchorAngle;
                if (!Angles.TryGetValue(Anchor, out AnchorAngle))
                    AnchorAngle = AngleFrom(RootCenter, Anchor.BaseCenter);

                var Spread = Math.Max(Options.MinimumAngularSeparation, Options.MinimumAngularSeparation * Math.Max(1, Group.Count - 1));
                AssignRingPositions(Group, RootCenter, Options.SecondRingRadius, AnchorAngle - Spread / 2.0, Spread, Options, Positions, Angles);
            }

            return Positions;
        }

        private static void AssignRingPositions(IList<VisualSymbol> Symbols, Point RootCenter, double Radius,
                                                double StartAngle, double Spread, SpiderMapLayoutOptions Options,
                                                IDictionary<VisualSymbol, Point> Positions,
                                                IDictionary<VisualSymbol, double> Angles)
        {
            if (Symbols == null || Symbols.Count < 1)
                return;

            var Step = Symbols.Count == 1 ? 0.0 : Spread / Symbols.Count;
            var MinimumStep = Options.MinimumAngularSeparation;
            if (Spread >= 360.0 && Symbols.Count > 0)
                Step = 360.0 / Symbols.Count;

            if (Step < MinimumStep && Spread < 360.0)
                Step = MinimumStep;

            var FirstAngle = Symbols.Count == 1
                             ? StartAngle + Spread / 2.0
                             : StartAngle + Step / 2.0;

            for (int Index = 0; Index < Symbols.Count; Index++)
            {
                var Angle = NormalizeAngle(FirstAngle + Index * Step);
                var Candidate = PointFrom(RootCenter, Radius, Angle);
                Candidate = ResolveSpacing(Candidate, RootCenter, Angle, Radius, Symbols[Index], Positions, Options);
                Positions[Symbols[Index]] = Candidate;
                Angles[Symbols[Index]] = Angle;
            }
        }

        private static Point ResolveSpacing(Point Candidate, Point RootCenter, double Angle, double Radius, VisualSymbol Symbol,
                                            IDictionary<VisualSymbol, Point> ExistingPositions, SpiderMapLayoutOptions Options)
        {
            var EffectiveRadius = Radius;
            for (int Attempt = 0; Attempt < 20; Attempt++)
            {
                if (!ExistingPositions.Any(Pair => CentersTooClose(Symbol, Candidate, Pair.Key, Pair.Value, Options.MinimumNodeSpacing)))
                    return Candidate;

                EffectiveRadius += Options.MinimumNodeSpacing * 1.5;
                Candidate = PointFrom(RootCenter, EffectiveRadius, Angle);
            }

            return Candidate;
        }

        private static bool CentersTooClose(VisualSymbol First, Point FirstCenter, VisualSymbol Second, Point SecondCenter, double MinimumSpacing)
        {
            var FirstRadius = Math.Max(First == null ? 0.0 : First.BaseWidth, First == null ? 0.0 : First.BaseHeight) / 2.0;
            var SecondRadius = Math.Max(Second == null ? 0.0 : Second.BaseWidth, Second == null ? 0.0 : Second.BaseHeight) / 2.0;
            return Distance(FirstCenter, SecondCenter) < FirstRadius + SecondRadius + MinimumSpacing;
        }

        private static void ApplyLayout(IDictionary<VisualSymbol, Point> Positions, SpiderMapLayoutResult Result)
        {
            foreach (var Pair in Positions.OrderBy(Item => GetSymbolSortKey(Item.Key)))
            {
                var Symbol = Pair.Key;
                var NewCenter = Pair.Value;
                var OldCenter = Symbol.BaseCenter;

                if (Distance(OldCenter, NewCenter) > GeometryTolerance)
                {
                    Symbol.MoveTo(NewCenter.X, NewCenter.Y, true);
                    Result.ConceptsMoved++;
                }
                else
                    Symbol.RenderElement();

                Result.ConceptsArranged++;
                Console.WriteLine("Appearance: Spider Map concept {0}; oldCenter=({1:0.##},{2:0.##}); newCenter=({3:0.##},{4:0.##}).",
                                  DescribeSymbol(Symbol),
                                  OldCenter.X, OldCenter.Y,
                                  NewCenter.X, NewCenter.Y);
            }
        }

        private static Rect ComputePlannedBounds(IDictionary<VisualSymbol, Point> Positions)
        {
            if (Positions == null || Positions.Count < 1)
                return Rect.Empty;

            Rect? Bounds = null;
            foreach (var Pair in Positions)
            {
                var Symbol = Pair.Key;
                if (Symbol == null)
                    continue;

                var Center = Pair.Value;
                var Rect = new Rect(Center.X - Symbol.BaseWidth / 2.0,
                                    Center.Y - Symbol.BaseHeight / 2.0,
                                    Symbol.BaseWidth,
                                    Symbol.BaseHeight);

                if (Bounds == null)
                    Bounds = Rect;
                else
                {
                    var Current = Bounds.Value;
                    Current.Union(Rect);
                    Bounds = Current;
                }
            }

            return Bounds ?? Rect.Empty;
        }

        private static void RevealArrangedBounds(View View, SpiderMapLayoutResult Result)
        {
            if (View == null || View.Presenter == null || Result == null || Result.BoundsAfterNormalization.IsEmpty)
            {
                if (Result != null)
                    Result.RevealAction = "none";

                Console.WriteLine("Appearance: Spider Map reveal arranged bounds: view={0}; bounds={1}; action=none.",
                                  DescribeView(View),
                                  Result == null ? "<none>" : LayoutBoundsNormalizer.FormatRect(Result.BoundsAfterNormalization));
                return;
            }

            var Bounds = Result.BoundsAfterNormalization;
            Bounds.Inflate(Result.BoundsAfterNormalization.Width * 0.08 + 40.0,
                           Result.BoundsAfterNormalization.Height * 0.08 + 40.0);
            View.Presenter.BringIntoView(Bounds);
            Result.RevealAction = "BringIntoView";
            Console.WriteLine("Appearance: Spider Map reveal arranged bounds: view={0}; bounds={1}; action=BringIntoView.",
                              DescribeView(View),
                              LayoutBoundsNormalizer.FormatRect(Bounds));
        }

        private static LinkObstacleRoutingResult RouteScopeLinks(LayoutSelectionContext Context, ConceptGraph Graph)
        {
            var Connectors = Graph.RelationshipRepresentations
                                  .Where(Representation => Representation != null)
                                  .SelectMany(Representation => Representation.VisualConnectors)
                                  .Where(Connector => Connector != null)
                                  .Cast<VisualObject>()
                                  .Distinct()
                                  .ToList();

            if (Connectors.Count < 1)
            {
                Console.WriteLine("Appearance: Spider Map post-route skipped; no relationship connectors are in scope.");
                return null;
            }

            var RouteContext = LayoutSelectionContext.FromViewSelection(Context.Engine, Context.ActiveView, Connectors);
            var RouteOptions = new LinkObstacleRoutingOptions();
            RouteOptions.RouteSelectedConnectorsOnly = true;
            return LinkObstacleRoutingService.RouteVisibleConnectors(RouteContext, RouteOptions);
        }

        private static IEnumerable<VisualSymbol> GetNeighbors(ConceptGraph Graph, VisualSymbol Symbol)
        {
            HashSet<VisualSymbol> Neighbors;
            return Graph.Adjacency.TryGetValue(Symbol, out Neighbors)
                   ? Neighbors
                   : Enumerable.Empty<VisualSymbol>();
        }

        private static int GetDegree(ConceptGraph Graph, VisualSymbol Symbol)
        {
            return GetNeighbors(Graph, Symbol).Count();
        }

        private static bool IsRouteableConceptSymbol(VisualSymbol Symbol)
        {
            return Symbol != null &&
                   !Symbol.IsHidden &&
                   Symbol.IsRelatedVisible &&
                   Symbol.OwnerRepresentation is ConceptVisualRepresentation &&
                   Symbol.OwnerRepresentation.RepresentedIdea is Concept &&
                   IsUsablePoint(Symbol.BaseCenter);
        }

        private static Point DetermineClusterCenter(IList<VisualSymbol> Symbols)
        {
            if (Symbols == null || Symbols.Count < 1)
                return new Point(0, 0);

            var Bounds = Symbols.Select(Symbol => Symbol.TotalArea)
                                .Where(Rectangle => !Rectangle.IsEmpty)
                                .ToList();
            if (Bounds.Count < 1)
                return new Point(Symbols.Average(Symbol => Symbol.BaseCenter.X),
                                 Symbols.Average(Symbol => Symbol.BaseCenter.Y));

            var Union = Bounds[0];
            foreach (var Rect in Bounds.Skip(1))
                Union.Union(Rect);

            return new Point(Union.Left + Union.Width / 2.0, Union.Top + Union.Height / 2.0);
        }

        private static Point EnforceSafeRootCenter(Point Center, IList<VisualSymbol> Symbols, SpiderMapLayoutOptions Options, string Source)
        {
            var MaxHalfSize = Symbols == null || Symbols.Count < 1
                              ? 0.0
                              : Symbols.Max(Symbol => Math.Max(Symbol.BaseWidth, Symbol.BaseHeight) / 2.0);
            var Minimum = Options.FirstRingRadius + MaxHalfSize + Options.CanvasPadding;
            var Adjusted = new Point(Math.Max(Center.X, Minimum), Math.Max(Center.Y, Minimum));

            if (Distance(Center, Adjusted) > GeometryTolerance)
                Console.WriteLine("Appearance: Spider Map root center adjusted from {0}; requested=({1:0.##},{2:0.##}); adjusted=({3:0.##},{4:0.##}); minimumRootMargin={5:0.##}.",
                                  Source.ToStringAlways(),
                                  Center.X,
                                  Center.Y,
                                  Adjusted.X,
                                  Adjusted.Y,
                                  Minimum);

            return Adjusted;
        }

        private static bool IsUsablePoint(Point Point)
        {
            return !Double.IsNaN(Point.X) && !Double.IsNaN(Point.Y) &&
                   !Double.IsInfinity(Point.X) && !Double.IsInfinity(Point.Y) &&
                   Math.Abs(Point.X) < 10000000.0 && Math.Abs(Point.Y) < 10000000.0;
        }

        private static Point PointFrom(Point Origin, double Radius, double AngleDegrees)
        {
            var Radians = AngleDegrees * Math.PI / 180.0;
            return new Point(Origin.X + Radius * Math.Cos(Radians),
                             Origin.Y + Radius * Math.Sin(Radians));
        }

        private static double AngleFrom(Point Origin, Point Target)
        {
            return NormalizeAngle(Math.Atan2(Target.Y - Origin.Y, Target.X - Origin.X) * 180.0 / Math.PI);
        }

        private static double NormalizeAngle(double Angle)
        {
            while (Angle < 0.0)
                Angle += 360.0;

            while (Angle >= 360.0)
                Angle -= 360.0;

            return Angle;
        }

        private static double Distance(Point First, Point Second)
        {
            var DeltaX = First.X - Second.X;
            var DeltaY = First.Y - Second.Y;
            return Math.Sqrt(DeltaX * DeltaX + DeltaY * DeltaY);
        }

        private static string GetSymbolSortKey(VisualSymbol Symbol)
        {
            if (Symbol == null || Symbol.OwnerRepresentation == null || Symbol.OwnerRepresentation.RepresentedIdea == null)
                return String.Empty;

            var Idea = Symbol.OwnerRepresentation.RepresentedIdea;
            return Idea.Name.ToStringAlways() + "|" + Idea.TechName.ToStringAlways() + "|" + Idea.GlobalId.ToString("D");
        }

        private static string DescribeView(View View)
        {
            return View == null
                   ? "<no view>"
                   : View.Name.ToStringAlways() + " (" + View.TechName.ToStringAlways() + ", id=" + View.GlobalId + ")";
        }

        private static string DescribeSymbol(VisualSymbol Symbol)
        {
            if (Symbol == null || Symbol.OwnerRepresentation == null)
                return "<no symbol>";

            return DescribeIdea(Symbol.OwnerRepresentation.RepresentedIdea);
        }

        private static string DescribeIdea(Idea Idea)
        {
            return Idea == null
                   ? "<no idea>"
                   : Idea.Name.ToStringAlways() + " (" + Idea.TechName.ToStringAlways() + ", id=" + Idea.GlobalId + ")";
        }

        private static void LogSummary(SpiderMapLayoutResult Result)
        {
            Console.WriteLine("Appearance: Arrange as Spider Map completed; concepts inspected={0}; arranged={1}; moved={2}; skipped={3}; relationships inspected={4}; edges={5}; links routed={6}; route skipped={7}; warnings={8}.",
                              Result.ConceptsInspected,
                              Result.ConceptsArranged,
                              Result.ConceptsMoved,
                              Result.ConceptsSkipped,
                              Result.RelationshipsInspected,
                              Result.EdgesUsed,
                              Result.LinksRouted,
                              Result.RoutingResult == null ? 0 : Result.RoutingResult.Skipped,
                              Result.Warnings.Count);

            if (Result.AutoFitResult != null)
                Console.WriteLine("Appearance: Spider Map auto-fit summary; inspected={0}; fitted={1}; skipped={2}.",
                                  Result.AutoFitResult.SymbolsInspected,
                                  Result.AutoFitResult.SymbolsFitted,
                                  Result.AutoFitResult.SymbolsSkipped);

            if (Result.RoutingResult != null)
                Console.WriteLine("Appearance: Spider Map route summary; connector routes inspected={0}; relationship routes inspected={1}; routed={2}; dogleg routed={3}; straightened={4}; unchanged={5}; skipped={6}.",
                                  Result.RoutingResult.ConnectorRoutesInspected,
                                  Result.RoutingResult.RelationshipRoutesInspected,
                                  Result.RoutingResult.Routed,
                                  Result.RoutingResult.DoglegRouted,
                                  Result.RoutingResult.Straightened,
                                  Result.RoutingResult.Unchanged,
                                  Result.RoutingResult.Skipped);

            Console.WriteLine("Appearance: Spider Map bounds summary; beforeNormalize={0}; dx={1:0.##}; dy={2:0.##}; final={3}; withinSafeCanvas={4}; reveal={5}.",
                              LayoutBoundsNormalizer.FormatRect(Result.BoundsBeforeNormalization),
                              Result.NormalizationDelta.X,
                              Result.NormalizationDelta.Y,
                              LayoutBoundsNormalizer.FormatRect(Result.BoundsAfterNormalization),
                              Result.FinalBoundsWithinSafeCanvas ? "true" : "false",
                              Result.RevealAction.ToStringAlways("none"));

            foreach (var Warning in Result.Warnings)
                Console.WriteLine("Appearance Spider Map warning: {0}", Warning);
        }
    }
}
