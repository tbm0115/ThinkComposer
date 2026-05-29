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
using Instrumind.ThinkComposer.MetaModel.GraphMetaModel;
using Instrumind.ThinkComposer.Model.GraphModel;
using Instrumind.ThinkComposer.Model.VisualModel;

namespace Instrumind.ThinkComposer.Composer.Layout
{
    /// <summary>
    /// Simple top-down hierarchy layout for visible concept maps.
    /// </summary>
    public static class HierarchyMapLayoutService
    {
        private const double GeometryTolerance = 0.5;

        private class DirectedConceptGraph
        {
            public DirectedConceptGraph()
            {
                this.Children = new Dictionary<VisualSymbol, HashSet<VisualSymbol>>();
                this.Parents = new Dictionary<VisualSymbol, HashSet<VisualSymbol>>();
                this.Adjacency = new Dictionary<VisualSymbol, HashSet<VisualSymbol>>();
                this.RelationshipRepresentations = new List<RelationshipVisualRepresentation>();
            }

            public Dictionary<VisualSymbol, HashSet<VisualSymbol>> Children;
            public Dictionary<VisualSymbol, HashSet<VisualSymbol>> Parents;
            public Dictionary<VisualSymbol, HashSet<VisualSymbol>> Adjacency;
            public List<RelationshipVisualRepresentation> RelationshipRepresentations;
        }

        private class HierarchyComponent
        {
            public HierarchyComponent()
            {
                this.Nodes = new List<VisualSymbol>();
                this.Roots = new List<VisualSymbol>();
                this.Levels = new Dictionary<VisualSymbol, int>();
                this.Order = new Dictionary<VisualSymbol, int>();
                this.RootSelectionReason = "";
            }

            public List<VisualSymbol> Nodes;
            public List<VisualSymbol> Roots;
            public Dictionary<VisualSymbol, int> Levels;
            public Dictionary<VisualSymbol, int> Order;
            public string RootSelectionReason;
            public bool CycleOrRootlessFallback;
        }

        public static bool CanArrange(LayoutSelectionContext Context)
        {
            return Context != null && Context.ActiveView != null && Context.VisibleConceptSymbols.Any(IsRouteableConceptSymbol);
        }

        public static HierarchyMapLayoutResult Arrange(LayoutSelectionContext Context, HierarchyMapLayoutOptions Options)
        {
            Options = Options ?? new HierarchyMapLayoutOptions();
            var Result = new HierarchyMapLayoutResult();

            if (Context == null || Context.ActiveView == null)
            {
                Result.AddWarning("No active view is available for Hierarchy Map arrangement.");
                LogSummary(Result);
                return Result;
            }

            var View = Context.ActiveView;
            var LocalCommand = !View.EditEngine.IsVariating;

            try
            {
                if (LocalCommand)
                    View.EditEngine.StartCommandVariation("Arrange as Hierarchy Map");

                var ScopeSymbols = GetScopeSymbols(Context, Options, Result);
                Result.ConceptsInspected = ScopeSymbols.Count;

                Console.WriteLine("Appearance: Arrange as Hierarchy Map starting; view={0}; scope={1}; concepts={2}; levelSpacingY={3:0.##}; nodeSpacingX={4:0.##}; componentSpacingX={5:0.##}.",
                                  DescribeView(View),
                                  Options.ArrangeSelectedConceptsOnly ? "selected concepts" : "all visible concepts",
                                  ScopeSymbols.Count,
                                  Options.LevelSpacingY,
                                  Options.NodeSpacingX,
                                  Options.ComponentSpacingX);

                if (ScopeSymbols.Count < 1)
                {
                    Result.AddWarning("No concept symbols are available in the requested Hierarchy Map scope.");
                    if (LocalCommand)
                        View.EditEngine.CompleteCommandVariation();
                    LogSummary(Result);
                    return Result;
                }

                if (Options.AutoFitConceptsBeforeArrange)
                    Result.AutoFitResult = ConceptAutoFitService.FitConceptSymbols(Context.Engine, ScopeSymbols, "hierarchy map layout");

                var Graph = BuildDirectedGraph(Context, ScopeSymbols, Result);
                var Components = BuildComponents(Context, ScopeSymbols, Graph, Options, Result);
                var PlannedPositions = ComputePositions(Components, Graph, Options, Result);

                Console.WriteLine("Appearance: Hierarchy Map layout bounds before apply: {0}.",
                                  LayoutBoundsNormalizer.FormatRect(ComputePlannedBounds(PlannedPositions)));

                ApplyLayout(PlannedPositions, Result);

                if (Options.NormalizeBounds)
                {
                    var NormalizeResult = LayoutBoundsNormalizer.NormalizeSymbolsToCanvas(View, ScopeSymbols, Options.CanvasPadding,
                                                                                          "Appearance: Hierarchy Map");
                    Result.BoundsBeforeNormalization = NormalizeResult.BoundsBefore;
                    Result.BoundsAfterNormalization = NormalizeResult.BoundsAfter;
                    Result.NormalizationDelta = NormalizeResult.Translation;
                    Result.BoundsNormalized = NormalizeResult.WasNormalized;
                    Result.FinalBoundsWithinSafeCanvas = NormalizeResult.IsWithinSafeBounds;
                }
                else
                    Result.BoundsAfterNormalization = LayoutBoundsNormalizer.ComputeSymbolBounds(ScopeSymbols);

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
                Console.WriteLine("Appearance: Arrange as Hierarchy Map failed. Problem: {0}", Problem.Message);
                Console.WriteLine(Problem.ToString());

                if (LocalCommand && View.EditEngine.IsVariating)
                {
                    try
                    {
                        View.EditEngine.DiscardCommandVariation();
                        Console.WriteLine("Appearance: Arrange as Hierarchy Map discarded its command variation after failure.");
                    }
                    catch (Exception DiscardProblem)
                    {
                        Console.WriteLine("Appearance: Could not discard failed Hierarchy Map command variation. Problem: {0}", DiscardProblem.Message);
                        Console.WriteLine(DiscardProblem.ToString());
                    }
                }

                throw;
            }
        }

        private static List<VisualSymbol> GetScopeSymbols(LayoutSelectionContext Context, HierarchyMapLayoutOptions Options,
                                                          HierarchyMapLayoutResult Result)
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
                    Console.WriteLine("Appearance: Hierarchy Map skipped concept {0}; reason={1}.", DescribeSymbol(Symbol), Warning);
                    continue;
                }

                Symbols.Add(Symbol);
            }

            return Symbols.OrderBy(GetSymbolSortKey).ToList();
        }

        private static DirectedConceptGraph BuildDirectedGraph(LayoutSelectionContext Context, IList<VisualSymbol> ScopeSymbols,
                                                               HierarchyMapLayoutResult Result)
        {
            var Graph = new DirectedConceptGraph();
            var SymbolByIdea = ScopeSymbols.Select(Symbol => new
                                  {
                                      Symbol = Symbol,
                                      Idea = Symbol.OwnerRepresentation == null ? null : Symbol.OwnerRepresentation.RepresentedIdea
                                  })
                                  .Where(Item => Item.Idea != null)
                                  .GroupBy(Item => Item.Idea)
                                  .ToDictionary(Group => Group.Key, Group => Group.First().Symbol);

            foreach (var Symbol in ScopeSymbols)
            {
                Graph.Children[Symbol] = new HashSet<VisualSymbol>();
                Graph.Parents[Symbol] = new HashSet<VisualSymbol>();
                Graph.Adjacency[Symbol] = new HashSet<VisualSymbol>();
            }

            foreach (var Representation in Context.VisibleRelationshipRepresentations.Where(Representation => Representation != null).Distinct())
            {
                Result.RelationshipsInspected++;

                var Relationship = Representation.RepresentedRelationship;
                if (Relationship == null || Relationship.Links == null)
                {
                    Result.AddWarning("Skipped a visible relationship representation without a relationship while building Hierarchy Map graph.");
                    continue;
                }

                var Origins = Relationship.Links
                                          .Where(Link => Link != null && Link.AssociatedIdea != null &&
                                                         Link.RoleDefinitor != null &&
                                                         Link.RoleDefinitor.RoleType == ERoleType.Origin &&
                                                         SymbolByIdea.ContainsKey(Link.AssociatedIdea))
                                          .Select(Link => SymbolByIdea[Link.AssociatedIdea])
                                          .Distinct()
                                          .ToList();
                var Targets = Relationship.Links
                                          .Where(Link => Link != null && Link.AssociatedIdea != null &&
                                                         Link.RoleDefinitor != null &&
                                                         Link.RoleDefinitor.RoleType == ERoleType.Target &&
                                                         SymbolByIdea.ContainsKey(Link.AssociatedIdea))
                                          .Select(Link => SymbolByIdea[Link.AssociatedIdea])
                                          .Distinct()
                                          .ToList();
                var Endpoints = Relationship.Links
                                            .Where(Link => Link != null && Link.AssociatedIdea != null &&
                                                           SymbolByIdea.ContainsKey(Link.AssociatedIdea))
                                            .Select(Link => SymbolByIdea[Link.AssociatedIdea])
                                            .Distinct()
                                            .ToList();

                var RelationshipAdded = false;
                if (Origins.Count > 0 && Targets.Count > 0)
                {
                    foreach (var Origin in Origins)
                        foreach (var Target in Targets)
                            if (Origin != Target)
                            {
                                if (Graph.Children[Origin].Add(Target))
                                {
                                    Graph.Parents[Target].Add(Origin);
                                    Result.DirectedEdges++;
                                }

                                AddUndirected(Graph, Origin, Target);
                                RelationshipAdded = true;
                            }
                }
                else
                if (Endpoints.Count > 1)
                {
                    Result.UnclearRelationships++;
                    Result.AddWarning("Relationship '" + Relationship.TechName.ToStringAlways() +
                                      "' has no clear origin/target roles in the hierarchy scope; using it only for component membership.");
                    Console.WriteLine("Appearance: Hierarchy Map unclear relationship direction: {0}; endpointsInScope={1}.",
                                      DescribeIdea(Relationship), Endpoints.Count);

                    for (int OriginIndex = 0; OriginIndex < Endpoints.Count; OriginIndex++)
                        for (int TargetIndex = OriginIndex + 1; TargetIndex < Endpoints.Count; TargetIndex++)
                            if (Endpoints[OriginIndex] != Endpoints[TargetIndex])
                            {
                                AddUndirected(Graph, Endpoints[OriginIndex], Endpoints[TargetIndex]);
                                Result.UndirectedEdges++;
                                RelationshipAdded = true;
                            }
                }
                else
                    Console.WriteLine("Appearance: Hierarchy Map relationship skipped for graph: {0}; endpointsInScope={1}.",
                                      DescribeIdea(Relationship), Endpoints.Count);

                if (RelationshipAdded)
                    Graph.RelationshipRepresentations.Add(Representation);
            }

            Graph.RelationshipRepresentations = Graph.RelationshipRepresentations.Distinct().ToList();
            Console.WriteLine("Appearance: Hierarchy Map graph built; relationships inspected={0}; relationship visuals in scope={1}; directedEdges={2}; undirectedOnlyEdges={3}; unclearRelationships={4}.",
                              Result.RelationshipsInspected,
                              Graph.RelationshipRepresentations.Count,
                              Result.DirectedEdges,
                              Result.UndirectedEdges,
                              Result.UnclearRelationships);
            return Graph;
        }

        private static void AddUndirected(DirectedConceptGraph Graph, VisualSymbol First, VisualSymbol Second)
        {
            Graph.Adjacency[First].Add(Second);
            Graph.Adjacency[Second].Add(First);
        }

        private static List<HierarchyComponent> BuildComponents(LayoutSelectionContext Context, IList<VisualSymbol> ScopeSymbols,
                                                                DirectedConceptGraph Graph, HierarchyMapLayoutOptions Options,
                                                                HierarchyMapLayoutResult Result)
        {
            var Components = new List<HierarchyComponent>();
            var Unvisited = new HashSet<VisualSymbol>(ScopeSymbols);

            while (Unvisited.Count > 0)
            {
                var Seed = Unvisited.OrderBy(GetSymbolSortKey).First();
                var Component = new HierarchyComponent();
                var Queue = new Queue<VisualSymbol>();
                Queue.Enqueue(Seed);
                Unvisited.Remove(Seed);

                while (Queue.Count > 0)
                {
                    var Current = Queue.Dequeue();
                    Component.Nodes.Add(Current);

                    foreach (var Next in Graph.Adjacency[Current].OrderBy(GetSymbolSortKey))
                        if (Unvisited.Remove(Next))
                            Queue.Enqueue(Next);
                }

                Component.Nodes = Component.Nodes.OrderBy(GetSymbolSortKey).ToList();
                DetermineRootsForComponent(Context, Component, Graph, Options, Result);
                AssignLevels(Component, Graph, Result);
                Components.Add(Component);
            }

            Result.ComponentCount = Components.Count;
            Result.RootCount = Components.Sum(Component => Component.Roots.Count);
            Result.LevelCount = Components.SelectMany(Component => Component.Levels.Values).DefaultIfEmpty(0).Max() + 1;

            Console.WriteLine("Appearance: Hierarchy Map components built; components={0}; roots={1}; maxLevels={2}.",
                              Result.ComponentCount, Result.RootCount, Result.LevelCount);
            return Components.OrderBy(Component => Component.Roots.Count > 0 ? GetSymbolSortKey(Component.Roots[0]) : GetSymbolSortKey(Component.Nodes[0]))
                             .ToList();
        }

        private static void DetermineRootsForComponent(LayoutSelectionContext Context, HierarchyComponent Component,
                                                       DirectedConceptGraph Graph, HierarchyMapLayoutOptions Options,
                                                       HierarchyMapLayoutResult Result)
        {
            var SelectedRoot = Options.ArrangeSelectedConceptsOnly && Context.SelectedConceptSymbols.Count == 1
                               ? Context.SelectedConceptSymbols[0]
                               : null;
            if (SelectedRoot != null && Component.Nodes.Contains(SelectedRoot) && Graph.Children[SelectedRoot].Count > 0)
            {
                Component.Roots.Add(SelectedRoot);
                Component.RootSelectionReason = "single selected concept with outgoing relationships";
                Console.WriteLine("Appearance: Hierarchy Map component root: {0}; reason={1}.",
                                  DescribeSymbol(SelectedRoot), Component.RootSelectionReason);
                return;
            }

            var Roots = Component.Nodes.Where(Symbol => Graph.Parents[Symbol].Count(Parent => Component.Nodes.Contains(Parent)) < 1)
                                       .OrderByDescending(Symbol => Graph.Children[Symbol].Count(Child => Component.Nodes.Contains(Child)))
                                       .ThenByDescending(Symbol => GetTotalDegree(Graph, Symbol, Component.Nodes))
                                       .ThenBy(Symbol => Distance(Symbol.BaseCenter, DetermineTieCenter(Context, Component.Nodes, Options)))
                                       .ThenBy(GetSymbolSortKey)
                                       .ToList();

            if (Roots.Count > 0)
            {
                Component.Roots.AddRange(Roots);
                Component.RootSelectionReason = "nodes with no incoming in-scope edges";
            }
            else
            {
                var FallbackRoot = Component.Nodes.OrderByDescending(Symbol => Graph.Children[Symbol].Count(Child => Component.Nodes.Contains(Child)))
                                                  .ThenByDescending(Symbol => GetTotalDegree(Graph, Symbol, Component.Nodes))
                                                  .ThenBy(Symbol => Distance(Symbol.BaseCenter, DetermineTieCenter(Context, Component.Nodes, Options)))
                                                  .ThenBy(GetSymbolSortKey)
                                                  .First();
                Component.Roots.Add(FallbackRoot);
                Component.CycleOrRootlessFallback = true;
                Component.RootSelectionReason = "cycle/rootless fallback: highest out-degree, then total degree";
                Result.CyclesDetected++;
                Result.AddWarning("Hierarchy Map detected a rootless or cyclic component; using '" +
                                  DescribeSymbol(FallbackRoot) + "' as fallback root.");
            }

            Console.WriteLine("Appearance: Hierarchy Map component roots: {0}; reason={1}.",
                              String.Join("; ", Component.Roots.Select(DescribeSymbol).ToArray()),
                              Component.RootSelectionReason);
        }

        private static void AssignLevels(HierarchyComponent Component, DirectedConceptGraph Graph, HierarchyMapLayoutResult Result)
        {
            var Queue = new Queue<VisualSymbol>();
            foreach (var Root in Component.Roots.OrderBy(GetSymbolSortKey))
            {
                Component.Levels[Root] = 0;
                Queue.Enqueue(Root);
            }

            while (Queue.Count > 0)
            {
                var Parent = Queue.Dequeue();
                var ParentLevel = Component.Levels[Parent];
                foreach (var Child in Graph.Children[Parent].Where(Component.Nodes.Contains).OrderBy(GetSymbolSortKey))
                {
                    var CandidateLevel = ParentLevel + 1;
                    int ExistingLevel;
                    if (!Component.Levels.TryGetValue(Child, out ExistingLevel))
                    {
                        Component.Levels[Child] = CandidateLevel;
                        Queue.Enqueue(Child);
                        continue;
                    }

                    if (CandidateLevel < ExistingLevel)
                    {
                        Component.Levels[Child] = CandidateLevel;
                        Queue.Enqueue(Child);
                    }
                    else
                    if (ExistingLevel <= ParentLevel)
                    {
                        Result.CyclesDetected++;
                        Console.WriteLine("Appearance: Hierarchy Map cycle/cross edge detected: parent={0}; child={1}; parentLevel={2}; childLevel={3}.",
                                          DescribeSymbol(Parent), DescribeSymbol(Child), ParentLevel, ExistingLevel);
                    }
                }
            }

            var Unassigned = Component.Nodes.Where(Symbol => !Component.Levels.ContainsKey(Symbol)).OrderBy(GetSymbolSortKey).ToList();
            if (Unassigned.Count > 0)
            {
                Result.AddWarning("Hierarchy Map found " + Unassigned.Count.ToString(CultureInfo.InvariantCulture) +
                                  " concepts not reachable from component roots; placing them at level 1.");
                foreach (var Symbol in Unassigned)
                    Component.Levels[Symbol] = 1;
            }
        }

        private static Dictionary<VisualSymbol, Point> ComputePositions(IList<HierarchyComponent> Components,
                                                                        DirectedConceptGraph Graph,
                                                                        HierarchyMapLayoutOptions Options,
                                                                        HierarchyMapLayoutResult Result)
        {
            var Positions = new Dictionary<VisualSymbol, Point>();
            var CurrentX = 0.0;

            foreach (var Component in Components)
            {
                var LevelGroups = Component.Nodes.GroupBy(Symbol => Component.Levels.ContainsKey(Symbol) ? Component.Levels[Symbol] : 0)
                                       .OrderBy(Group => Group.Key)
                                       .ToList();
                var OrderedLevels = new Dictionary<int, List<VisualSymbol>>();
                var PreviousOrder = new Dictionary<VisualSymbol, int>();
                foreach (var LevelGroup in LevelGroups)
                {
                    var Ordered = OrderLevelNodes(LevelGroup.ToList(), Component, Graph, PreviousOrder);
                    OrderedLevels[LevelGroup.Key] = Ordered;
                    PreviousOrder = Ordered.Select((Symbol, Index) => new { Symbol, Index })
                                           .ToDictionary(Item => Item.Symbol, Item => Item.Index);
                }

                var LevelWidths = OrderedLevels.ToDictionary(Pair => Pair.Key,
                                                             Pair => MeasureLevelWidth(Pair.Value, Options.NodeSpacingX));
                var ComponentWidth = LevelWidths.Values.DefaultIfEmpty(0.0).Max();
                var CurrentY = 0.0;

                foreach (var Pair in OrderedLevels.OrderBy(Pair => Pair.Key))
                {
                    var Level = Pair.Key;
                    var Nodes = Pair.Value;
                    var LevelHeight = Nodes.Select(Symbol => Symbol.BaseHeight).DefaultIfEmpty(0.0).Max();
                    var LevelWidth = LevelWidths[Level];
                    var X = CurrentX + (ComponentWidth - LevelWidth) / 2.0;
                    var CenterY = CurrentY + LevelHeight / 2.0;

                    foreach (var Symbol in Nodes)
                    {
                        X += Symbol.BaseWidth / 2.0;
                        Positions[Symbol] = new Point(X, CenterY);
                        X += Symbol.BaseWidth / 2.0 + Options.NodeSpacingX;
                        Console.WriteLine("Appearance: Hierarchy Map planned concept {0}; level={1}; center=({2:0.##},{3:0.##}).",
                                          DescribeSymbol(Symbol), Level, Positions[Symbol].X, Positions[Symbol].Y);
                    }

                    CurrentY += LevelHeight + Options.LevelSpacingY;
                }

                CurrentX += ComponentWidth + Options.ComponentSpacingX;
            }

            return Positions;
        }

        private static List<VisualSymbol> OrderLevelNodes(IList<VisualSymbol> Nodes, HierarchyComponent Component,
                                                          DirectedConceptGraph Graph,
                                                          IDictionary<VisualSymbol, int> PreviousOrder)
        {
            return Nodes.OrderBy(Symbol => GetPrimaryParentOrder(Symbol, Component, Graph, PreviousOrder))
                        .ThenBy(GetSymbolSortKey)
                        .ToList();
        }

        private static int GetPrimaryParentOrder(VisualSymbol Symbol, HierarchyComponent Component, DirectedConceptGraph Graph,
                                                 IDictionary<VisualSymbol, int> PreviousOrder)
        {
            var MatchingParents = Graph.Parents[Symbol]
                                       .Where(Parent => Component.Nodes.Contains(Parent) && PreviousOrder.ContainsKey(Parent))
                                       .ToList();
            return MatchingParents.Count < 1 ? Int32.MaxValue : MatchingParents.Min(Parent => PreviousOrder[Parent]);
        }

        private static double MeasureLevelWidth(IList<VisualSymbol> Symbols, double NodeSpacingX)
        {
            if (Symbols == null || Symbols.Count < 1)
                return 0.0;

            return Symbols.Sum(Symbol => Symbol.BaseWidth) + Math.Max(0, Symbols.Count - 1) * NodeSpacingX;
        }

        private static void ApplyLayout(IDictionary<VisualSymbol, Point> Positions, HierarchyMapLayoutResult Result)
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
                Console.WriteLine("Appearance: Hierarchy Map concept {0}; oldCenter=({1:0.##},{2:0.##}); newCenter=({3:0.##},{4:0.##}).",
                                  DescribeSymbol(Symbol), OldCenter.X, OldCenter.Y, NewCenter.X, NewCenter.Y);
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

        private static LinkObstacleRoutingResult RouteScopeLinks(LayoutSelectionContext Context, DirectedConceptGraph Graph)
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
                Console.WriteLine("Appearance: Hierarchy Map post-route skipped; no relationship connectors are in scope.");
                return null;
            }

            var RouteContext = LayoutSelectionContext.FromViewSelection(Context.Engine, Context.ActiveView, Connectors);
            var RouteOptions = new LinkObstacleRoutingOptions();
            RouteOptions.RouteSelectedConnectorsOnly = true;
            return LinkObstacleRoutingService.RouteVisibleConnectors(RouteContext, RouteOptions);
        }

        private static void RevealArrangedBounds(View View, HierarchyMapLayoutResult Result)
        {
            if (View == null || View.Presenter == null || Result == null || Result.BoundsAfterNormalization.IsEmpty)
            {
                if (Result != null)
                    Result.RevealAction = "none";

                Console.WriteLine("Appearance: Hierarchy Map reveal arranged bounds: view={0}; bounds={1}; action=none.",
                                  DescribeView(View),
                                  Result == null ? "<none>" : LayoutBoundsNormalizer.FormatRect(Result.BoundsAfterNormalization));
                return;
            }

            var Bounds = Result.BoundsAfterNormalization;
            Bounds.Inflate(Result.BoundsAfterNormalization.Width * 0.08 + 40.0,
                           Result.BoundsAfterNormalization.Height * 0.08 + 40.0);
            View.Presenter.BringIntoView(Bounds);
            Result.RevealAction = "BringIntoView";
            Console.WriteLine("Appearance: Hierarchy Map reveal arranged bounds: view={0}; bounds={1}; action=BringIntoView.",
                              DescribeView(View), LayoutBoundsNormalizer.FormatRect(Bounds));
        }

        private static Point DetermineTieCenter(LayoutSelectionContext Context, IList<VisualSymbol> Symbols,
                                                HierarchyMapLayoutOptions Options)
        {
            if (Options.ArrangeSelectedConceptsOnly && Context.SelectedConceptSymbols.Count > 1)
                return DetermineClusterCenter(Context.SelectedConceptSymbols.Where(Symbols.Contains).ToList());

            if (IsUsablePoint(Context.CurrentViewportCenter))
                return Context.CurrentViewportCenter;

            return DetermineClusterCenter(Symbols);
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

        private static int GetTotalDegree(DirectedConceptGraph Graph, VisualSymbol Symbol, IList<VisualSymbol> ComponentNodes)
        {
            return Graph.Adjacency[Symbol].Count(ComponentNodes.Contains);
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

        private static bool IsUsablePoint(Point Point)
        {
            return !Double.IsNaN(Point.X) && !Double.IsNaN(Point.Y) &&
                   !Double.IsInfinity(Point.X) && !Double.IsInfinity(Point.Y) &&
                   Math.Abs(Point.X) < 10000000.0 && Math.Abs(Point.Y) < 10000000.0;
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

        private static void LogSummary(HierarchyMapLayoutResult Result)
        {
            Console.WriteLine("Appearance: Arrange as Hierarchy Map completed; concepts inspected={0}; arranged={1}; moved={2}; skipped={3}; relationships inspected={4}; directedEdges={5}; undirectedEdges={6}; components={7}; roots={8}; levels={9}; cycles={10}; links routed={11}; route skipped={12}; warnings={13}.",
                              Result.ConceptsInspected,
                              Result.ConceptsArranged,
                              Result.ConceptsMoved,
                              Result.ConceptsSkipped,
                              Result.RelationshipsInspected,
                              Result.DirectedEdges,
                              Result.UndirectedEdges,
                              Result.ComponentCount,
                              Result.RootCount,
                              Result.LevelCount,
                              Result.CyclesDetected,
                              Result.LinksRouted,
                              Result.RoutingResult == null ? 0 : Result.RoutingResult.Skipped,
                              Result.Warnings.Count);

            if (Result.AutoFitResult != null)
                Console.WriteLine("Appearance: Hierarchy Map auto-fit summary; inspected={0}; fitted={1}; skipped={2}.",
                                  Result.AutoFitResult.SymbolsInspected,
                                  Result.AutoFitResult.SymbolsFitted,
                                  Result.AutoFitResult.SymbolsSkipped);

            if (Result.RoutingResult != null)
                Console.WriteLine("Appearance: Hierarchy Map route summary; connector routes inspected={0}; relationship routes inspected={1}; routed={2}; dogleg routed={3}; straightened={4}; unchanged={5}; skipped={6}.",
                                  Result.RoutingResult.ConnectorRoutesInspected,
                                  Result.RoutingResult.RelationshipRoutesInspected,
                                  Result.RoutingResult.Routed,
                                  Result.RoutingResult.DoglegRouted,
                                  Result.RoutingResult.Straightened,
                                  Result.RoutingResult.Unchanged,
                                  Result.RoutingResult.Skipped);

            Console.WriteLine("Appearance: Hierarchy Map bounds summary; beforeNormalize={0}; dx={1:0.##}; dy={2:0.##}; final={3}; withinSafeCanvas={4}; reveal={5}.",
                              LayoutBoundsNormalizer.FormatRect(Result.BoundsBeforeNormalization),
                              Result.NormalizationDelta.X,
                              Result.NormalizationDelta.Y,
                              LayoutBoundsNormalizer.FormatRect(Result.BoundsAfterNormalization),
                              Result.FinalBoundsWithinSafeCanvas ? "true" : "false",
                              Result.RevealAction.ToStringAlways("none"));

            foreach (var Warning in Result.Warnings)
                Console.WriteLine("Appearance Hierarchy Map warning: {0}", Warning);
        }
    }
}
