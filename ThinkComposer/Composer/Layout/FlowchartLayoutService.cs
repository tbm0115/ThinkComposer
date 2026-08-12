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
    /// Simple left-to-right process-flow layout for visible concept maps.
    /// </summary>
    public static class FlowchartLayoutService
    {
        private const double GeometryTolerance = 0.5;

        private enum FlowchartEdgeType
        {
            PrimaryForward,
            BranchForward,
            SameLevel,
            FeedbackReverse,
            LongCrossLink,
            Ambiguous
        }

        private class DirectedConceptGraph
        {
            public DirectedConceptGraph()
            {
                this.Children = new Dictionary<VisualSymbol, HashSet<VisualSymbol>>();
                this.Parents = new Dictionary<VisualSymbol, HashSet<VisualSymbol>>();
                this.Adjacency = new Dictionary<VisualSymbol, HashSet<VisualSymbol>>();
                this.RelationshipRepresentations = new List<RelationshipVisualRepresentation>();
                this.RelationshipInfos = new List<FlowRelationshipInfo>();
            }

            public Dictionary<VisualSymbol, HashSet<VisualSymbol>> Children;
            public Dictionary<VisualSymbol, HashSet<VisualSymbol>> Parents;
            public Dictionary<VisualSymbol, HashSet<VisualSymbol>> Adjacency;
            public List<RelationshipVisualRepresentation> RelationshipRepresentations;
            public List<FlowRelationshipInfo> RelationshipInfos;
        }

        private class FlowRelationshipInfo
        {
            public FlowRelationshipInfo()
            {
                this.Origins = new List<VisualSymbol>();
                this.Targets = new List<VisualSymbol>();
                this.Endpoints = new List<VisualSymbol>();
                this.EdgeType = FlowchartEdgeType.Ambiguous;
                this.SourceStep = -1;
                this.TargetStep = -1;
            }

            public RelationshipVisualRepresentation Representation;
            public Relationship Relationship;
            public List<VisualSymbol> Origins;
            public List<VisualSymbol> Targets;
            public List<VisualSymbol> Endpoints;
            public FlowchartEdgeType EdgeType;
            public int SourceStep;
            public int TargetStep;

            public bool HasDirectedEndpoints
            {
                get { return this.Origins.Count > 0 && this.Targets.Count > 0; }
            }

            public bool UsesFeedbackLane
            {
                get
                {
                    return this.EdgeType == FlowchartEdgeType.FeedbackReverse ||
                           this.EdgeType == FlowchartEdgeType.LongCrossLink;
                }
            }

            public string SortKey
            {
                get
                {
                    return this.Relationship == null
                           ? String.Empty
                           : this.Relationship.Name.ToStringAlways() + "|" +
                             this.Relationship.TechName.ToStringAlways() + "|" +
                             this.Relationship.GlobalId.ToString("D");
                }
            }
        }

        private class FlowComponent
        {
            public FlowComponent()
            {
                this.Nodes = new List<VisualSymbol>();
                this.Starts = new List<VisualSymbol>();
                this.Steps = new Dictionary<VisualSymbol, int>();
                this.Order = new Dictionary<VisualSymbol, int>();
                this.StartSelectionReason = "";
            }

            public List<VisualSymbol> Nodes;
            public List<VisualSymbol> Starts;
            public Dictionary<VisualSymbol, int> Steps;
            public Dictionary<VisualSymbol, int> Order;
            public string StartSelectionReason;
            public bool CycleOrRootlessFallback;
        }

        public static bool CanArrange(LayoutSelectionContext Context)
        {
            return Context != null && Context.ActiveView != null && Context.VisibleConceptSymbols.Any(IsRouteableConceptSymbol);
        }

        public static FlowchartLayoutResult Arrange(LayoutSelectionContext Context, FlowchartLayoutOptions Options)
        {
            Options = Options ?? new FlowchartLayoutOptions();
            var Result = new FlowchartLayoutResult();

            if (Context == null || Context.ActiveView == null)
            {
                Result.AddWarning("No active view is available for Flowchart arrangement.");
                LogSummary(Result);
                return Result;
            }

            var View = Context.ActiveView;
            var LocalCommand = !View.EditEngine.IsVariating;

            try
            {
                if (LocalCommand)
                    View.EditEngine.StartCommandVariation("Arrange as Flowchart");

                var ScopeSymbols = GetScopeSymbols(Context, Options, Result);
                Result.ConceptsInspected = ScopeSymbols.Count;

                Console.WriteLine("Appearance: Arrange as Flowchart starting; view={0}; scope={1}; concepts={2}; stepSpacingX={3:0.##}; laneSpacingY={4:0.##}; componentSpacingY={5:0.##}.",
                                  DescribeView(View),
                                  Options.ArrangeSelectedConceptsOnly ? "selected concepts" : "all visible concepts",
                                  ScopeSymbols.Count,
                                  Options.StepSpacingX,
                                  Options.LaneSpacingY,
                                  Options.ComponentSpacingY);

                if (ScopeSymbols.Count < 1)
                {
                    Result.AddWarning("No concept symbols are available in the requested Flowchart scope.");
                    if (LocalCommand)
                        View.EditEngine.CompleteCommandVariation();
                    LogSummary(Result);
                    return Result;
                }

                if (Options.AutoFitConceptsBeforeArrange)
                    Result.AutoFitResult = ConceptAutoFitService.FitConceptSymbols(Context.Engine, ScopeSymbols, "flowchart layout");

                var Graph = BuildDirectedGraph(Context, ScopeSymbols, Result);
                var Components = BuildComponents(Context, ScopeSymbols, Graph, Options, Result);
                var StepBySymbol = GetStepMap(Components);
                ClassifyFlowRelationships(Graph, StepBySymbol, Result);
                var PlannedPositions = ComputePositions(Components, Graph, Options, Result);

                Console.WriteLine("Appearance: Flowchart layout bounds before apply: {0}.",
                                  LayoutBoundsNormalizer.FormatRect(ComputePlannedBounds(PlannedPositions)));

                ApplyLayout(PlannedPositions, Result);

                if (Options.NormalizeBounds)
                {
                    var NormalizeResult = LayoutBoundsNormalizer.NormalizeSymbolsToCanvas(View, ScopeSymbols, Options.CanvasPadding,
                                                                                          "Appearance: Flowchart");
                    Result.BoundsBeforeNormalization = NormalizeResult.BoundsBefore;
                    Result.BoundsAfterNormalization = NormalizeResult.BoundsAfter;
                    Result.NormalizationDelta = NormalizeResult.Translation;
                    Result.BoundsNormalized = NormalizeResult.WasNormalized;
                    Result.FinalBoundsWithinSafeCanvas = NormalizeResult.IsWithinSafeBounds;
                }
                else
                    Result.BoundsAfterNormalization = LayoutBoundsNormalizer.ComputeSymbolBounds(ScopeSymbols);

                var FeedbackLaneRepresentations = PlaceFeedbackLaneRelationships(View, Graph.RelationshipInfos, ScopeSymbols, Options, Result);
                var InBandRelationshipRepresentations = Graph.RelationshipInfos
                                                            .Where(Info => Info != null &&
                                                                           Info.Representation != null &&
                                                                           !Info.UsesFeedbackLane)
                                                            .Select(Info => Info.Representation)
                                                            .Distinct()
                                                            .ToList();

                if (Options.DeclutterRelationshipNodesAfterArrange)
                {
                    Result.RelationshipNodeDeclutterResult = RelationshipNodeDeclutterService.Declutter(View,
                                                                                                        InBandRelationshipRepresentations,
                                                                                                        ScopeSymbols,
                                                                                                        StepBySymbol,
                                                                                                        Options.RelationshipNodeDeclutterOptions);
                    foreach (var Warning in Result.RelationshipNodeDeclutterResult.Warnings)
                        Result.AddWarning(Warning);
                }

                if (Options.RouteLinksAfterArrange)
                    Result.RoutingResult = RouteScopeLinks(Context, InBandRelationshipRepresentations,
                                                          FeedbackLaneRepresentations, ScopeSymbols);

                ValidateFlowchartRoutes(Graph.RelationshipInfos, ScopeSymbols, FeedbackLaneRepresentations, Options, Result);

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
                Console.WriteLine("Appearance: Arrange as Flowchart failed. Problem: {0}", Problem.Message);
                Console.WriteLine(Problem.ToString());

                if (LocalCommand && View.EditEngine.IsVariating)
                {
                    try
                    {
                        View.EditEngine.DiscardCommandVariation();
                        Console.WriteLine("Appearance: Arrange as Flowchart discarded its command variation after failure.");
                    }
                    catch (Exception DiscardProblem)
                    {
                        Console.WriteLine("Appearance: Could not discard failed Flowchart command variation. Problem: {0}", DiscardProblem.Message);
                        Console.WriteLine(DiscardProblem.ToString());
                    }
                }

                throw;
            }
        }

        private static List<VisualSymbol> GetScopeSymbols(LayoutSelectionContext Context, FlowchartLayoutOptions Options,
                                                          FlowchartLayoutResult Result)
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
                    Console.WriteLine("Appearance: Flowchart skipped concept {0}; reason={1}.", DescribeSymbol(Symbol), Warning);
                    continue;
                }

                Symbols.Add(Symbol);
            }

            return Symbols.OrderBy(GetSymbolSortKey).ToList();
        }

        private static DirectedConceptGraph BuildDirectedGraph(LayoutSelectionContext Context, IList<VisualSymbol> ScopeSymbols,
                                                               FlowchartLayoutResult Result)
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
                    Result.AddWarning("Skipped a visible relationship representation without a relationship while building Flowchart graph.");
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
                var RelationshipInfo = new FlowRelationshipInfo();
                RelationshipInfo.Representation = Representation;
                RelationshipInfo.Relationship = Relationship;
                RelationshipInfo.Origins = Origins;
                RelationshipInfo.Targets = Targets;
                RelationshipInfo.Endpoints = Endpoints;

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
                                      "' has no clear origin/target roles in the flowchart scope; using it only for lane membership.");
                    Console.WriteLine("Appearance: Flowchart unclear relationship direction: {0}; endpointsInScope={1}.",
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
                    Console.WriteLine("Appearance: Flowchart relationship skipped for graph: {0}; endpointsInScope={1}.",
                                      DescribeIdea(Relationship), Endpoints.Count);

                if (RelationshipAdded)
                {
                    Graph.RelationshipRepresentations.Add(Representation);
                    Graph.RelationshipInfos.Add(RelationshipInfo);
                }
            }

            Graph.RelationshipRepresentations = Graph.RelationshipRepresentations.Distinct().ToList();
            Graph.RelationshipInfos = Graph.RelationshipInfos
                                      .GroupBy(Info => Info.Representation)
                                      .Select(Group => Group.First())
                                      .ToList();
            Console.WriteLine("Appearance: Flowchart graph built; relationships inspected={0}; relationship visuals in scope={1}; directedEdges={2}; undirectedOnlyEdges={3}; unclearRelationships={4}.",
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

        private static List<FlowComponent> BuildComponents(LayoutSelectionContext Context, IList<VisualSymbol> ScopeSymbols,
                                                           DirectedConceptGraph Graph, FlowchartLayoutOptions Options,
                                                           FlowchartLayoutResult Result)
        {
            var Components = new List<FlowComponent>();
            var Unvisited = new HashSet<VisualSymbol>(ScopeSymbols);

            while (Unvisited.Count > 0)
            {
                var Seed = Unvisited.OrderBy(GetSymbolSortKey).First();
                var Component = new FlowComponent();
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
                DetermineStartsForComponent(Context, Component, Graph, Options, Result);
                AssignSteps(Component, Graph, Result);
                Components.Add(Component);
            }

            Result.ComponentCount = Components.Count;
            Result.StartCount = Components.Sum(Component => Component.Starts.Count);
            Result.StepCount = Components.SelectMany(Component => Component.Steps.Values).DefaultIfEmpty(0).Max() + 1;

            Console.WriteLine("Appearance: Flowchart components built; components={0}; starts={1}; maxSteps={2}.",
                              Result.ComponentCount, Result.StartCount, Result.StepCount);
            return Components.OrderBy(Component => Component.Starts.Count > 0 ? GetSymbolSortKey(Component.Starts[0]) : GetSymbolSortKey(Component.Nodes[0]))
                             .ToList();
        }

        private static void DetermineStartsForComponent(LayoutSelectionContext Context, FlowComponent Component,
                                                        DirectedConceptGraph Graph, FlowchartLayoutOptions Options,
                                                        FlowchartLayoutResult Result)
        {
            var SelectedStart = Options.ArrangeSelectedConceptsOnly && Context.SelectedConceptSymbols.Count == 1
                                ? Context.SelectedConceptSymbols[0]
                                : null;
            if (SelectedStart != null && Component.Nodes.Contains(SelectedStart) && Graph.Children[SelectedStart].Count > 0)
            {
                Component.Starts.Add(SelectedStart);
                Component.StartSelectionReason = "single selected concept with outgoing flow";
                Console.WriteLine("Appearance: Flowchart selected start {0}; reason={1}.",
                                  DescribeSymbol(SelectedStart), Component.StartSelectionReason);
                return;
            }

            var Starts = Component.Nodes
                                  .Where(Symbol => !Graph.Parents[Symbol].Any(Component.Nodes.Contains))
                                  .OrderByDescending(Symbol => Graph.Children[Symbol].Count(Component.Nodes.Contains))
                                  .ThenByDescending(Symbol => GetTotalDegree(Graph, Symbol, Component.Nodes))
                                  .ThenBy(GetSymbolSortKey)
                                  .ToList();
            if (Starts.Count > 0)
            {
                Component.Starts = Starts;
                Component.StartSelectionReason = "no incoming directed flow";
                foreach (var Start in Component.Starts)
                    Console.WriteLine("Appearance: Flowchart start {0}; reason={1}.",
                                      DescribeSymbol(Start), Component.StartSelectionReason);
                return;
            }

            var TieCenter = DetermineTieCenter(Context, Component.Nodes, Options);
            var Fallback = Component.Nodes.OrderByDescending(Symbol => Graph.Children[Symbol].Count(Component.Nodes.Contains))
                                          .ThenByDescending(Symbol => GetTotalDegree(Graph, Symbol, Component.Nodes))
                                          .ThenBy(Symbol => Distance(Symbol.BaseCenter, TieCenter))
                                          .ThenBy(GetSymbolSortKey)
                                          .FirstOrDefault();
            if (Fallback != null)
            {
                Component.Starts.Add(Fallback);
                Component.CycleOrRootlessFallback = true;
                Component.StartSelectionReason = "cycle/rootless fallback";
                Result.CyclesDetected++;
                Result.AddWarning("Flowchart found a rootless or cyclic component; using '" +
                                  GetIdeaName(Fallback) + "' as the deterministic start.");
                Console.WriteLine("Appearance: Flowchart fallback start {0}; reason={1}.",
                                  DescribeSymbol(Fallback), Component.StartSelectionReason);
            }
        }

        private static void ClassifyFlowRelationships(DirectedConceptGraph Graph, IDictionary<VisualSymbol, int> Steps,
                                                       FlowchartLayoutResult Result)
        {
            if (Graph == null || Graph.RelationshipInfos == null)
                return;

            foreach (var Info in Graph.RelationshipInfos.OrderBy(Info => Info.SortKey))
            {
                if (Info == null || !Info.HasDirectedEndpoints)
                {
                    if (Info != null)
                        Info.EdgeType = FlowchartEdgeType.Ambiguous;

                    Result.AmbiguousEdges++;
                    continue;
                }

                var OriginSteps = Info.Origins.Where(Symbol => Symbol != null && Steps.ContainsKey(Symbol))
                                               .Select(Symbol => Steps[Symbol])
                                               .ToList();
                var TargetSteps = Info.Targets.Where(Symbol => Symbol != null && Steps.ContainsKey(Symbol))
                                               .Select(Symbol => Steps[Symbol])
                                               .ToList();
                if (OriginSteps.Count < 1 || TargetSteps.Count < 1)
                {
                    Info.EdgeType = FlowchartEdgeType.Ambiguous;
                    Result.AmbiguousEdges++;
                    Console.WriteLine("Appearance: Flowchart edge classification: {0} type=ambiguous reason=missing source/target step.",
                                      DescribeIdea(Info.Relationship));
                    continue;
                }

                Info.SourceStep = OriginSteps.Min();
                Info.TargetStep = TargetSteps.Min();
                var StepDelta = Info.TargetStep - Info.SourceStep;

                if (StepDelta < 0)
                    Info.EdgeType = FlowchartEdgeType.FeedbackReverse;
                else
                if (StepDelta == 0)
                    Info.EdgeType = FlowchartEdgeType.SameLevel;
                else
                if (StepDelta > 1)
                    Info.EdgeType = FlowchartEdgeType.LongCrossLink;
                else
                if (HasBranchingEndpoint(Graph, Info))
                    Info.EdgeType = FlowchartEdgeType.BranchForward;
                else
                    Info.EdgeType = FlowchartEdgeType.PrimaryForward;

                IncrementEdgeType(Result, Info.EdgeType);
                Console.WriteLine("Appearance: Flowchart edge classification: {0} source={1} step={2} target={3} step={4} type={5}.",
                                  DescribeIdea(Info.Relationship),
                                  DescribeSymbol(Info.Origins.OrderBy(GetSymbolSortKey).FirstOrDefault()),
                                  Info.SourceStep,
                                  DescribeSymbol(Info.Targets.OrderBy(GetSymbolSortKey).FirstOrDefault()),
                                  Info.TargetStep,
                                  FormatEdgeType(Info.EdgeType));
            }

            Console.WriteLine("Appearance: Flowchart edge classification summary; primary-forward={0}; branch-forward={1}; same-level={2}; feedback/reverse={3}; long-cross-link={4}; ambiguous={5}.",
                              Result.PrimaryForwardEdges,
                              Result.BranchForwardEdges,
                              Result.SameLevelEdges,
                              Result.FeedbackReverseEdges,
                              Result.LongCrossLinkEdges,
                              Result.AmbiguousEdges);
        }

        private static bool HasBranchingEndpoint(DirectedConceptGraph Graph, FlowRelationshipInfo Info)
        {
            if (Graph == null || Info == null)
                return false;

            return Info.Origins.Any(Origin => Origin != null && Graph.Children.ContainsKey(Origin) && Graph.Children[Origin].Count > 1) ||
                   Info.Targets.Any(Target => Target != null && Graph.Parents.ContainsKey(Target) && Graph.Parents[Target].Count > 1);
        }

        private static void IncrementEdgeType(FlowchartLayoutResult Result, FlowchartEdgeType EdgeType)
        {
            switch (EdgeType)
            {
                case FlowchartEdgeType.PrimaryForward:
                    Result.PrimaryForwardEdges++;
                    break;
                case FlowchartEdgeType.BranchForward:
                    Result.BranchForwardEdges++;
                    break;
                case FlowchartEdgeType.SameLevel:
                    Result.SameLevelEdges++;
                    break;
                case FlowchartEdgeType.FeedbackReverse:
                    Result.FeedbackReverseEdges++;
                    break;
                case FlowchartEdgeType.LongCrossLink:
                    Result.LongCrossLinkEdges++;
                    break;
                default:
                    Result.AmbiguousEdges++;
                    break;
            }
        }

        private static void AssignSteps(FlowComponent Component, DirectedConceptGraph Graph, FlowchartLayoutResult Result)
        {
            var Queue = new Queue<VisualSymbol>();
            var VisitCounts = new Dictionary<VisualSymbol, int>();
            var MaxSafeStep = Math.Max(1, Component.Nodes.Count);

            foreach (var Start in Component.Starts.OrderBy(GetSymbolSortKey))
            {
                Component.Steps[Start] = 0;
                Queue.Enqueue(Start);
                VisitCounts[Start] = 1;
            }

            while (Queue.Count > 0)
            {
                var Parent = Queue.Dequeue();
                var ParentStep = Component.Steps[Parent];
                foreach (var Child in Graph.Children[Parent].Where(Component.Nodes.Contains).OrderBy(GetSymbolSortKey))
                {
                    var CandidateStep = ParentStep + 1;
                    if (CandidateStep > MaxSafeStep)
                    {
                        Result.CyclesDetected++;
                        Console.WriteLine("Appearance: Flowchart cycle/feedback edge capped: parent={0}; child={1}; parentStep={2}; candidateStep={3}.",
                                          DescribeSymbol(Parent), DescribeSymbol(Child), ParentStep, CandidateStep);
                        continue;
                    }

                    int ExistingStep;
                    if (Component.Steps.TryGetValue(Child, out ExistingStep) &&
                        CandidateStep > ExistingStep &&
                        HasDirectedPath(Child, Parent, Graph, Component.Nodes, new HashSet<VisualSymbol>()))
                    {
                        Result.CyclesDetected++;
                        Console.WriteLine("Appearance: Flowchart feedback/cycle edge held at existing step: parent={0}; child={1}; parentStep={2}; childStep={3}; candidateStep={4}.",
                                          DescribeSymbol(Parent), DescribeSymbol(Child), ParentStep, ExistingStep, CandidateStep);
                        continue;
                    }

                    if (!Component.Steps.TryGetValue(Child, out ExistingStep) || CandidateStep > ExistingStep)
                    {
                        Component.Steps[Child] = CandidateStep;
                        int VisitCount;
                        VisitCounts.TryGetValue(Child, out VisitCount);
                        VisitCount++;
                        VisitCounts[Child] = VisitCount;

                        if (VisitCount <= MaxSafeStep + 1)
                            Queue.Enqueue(Child);
                        else
                        {
                            Result.CyclesDetected++;
                            Console.WriteLine("Appearance: Flowchart cycle guard stopped revisiting child={0}; visits={1}.",
                                              DescribeSymbol(Child), VisitCount);
                        }
                    }
                    else
                    if (ExistingStep <= ParentStep)
                    {
                        Result.CyclesDetected++;
                        Console.WriteLine("Appearance: Flowchart cycle/cross edge detected: parent={0}; child={1}; parentStep={2}; childStep={3}.",
                                          DescribeSymbol(Parent), DescribeSymbol(Child), ParentStep, ExistingStep);
                    }
                }
            }

            var Unassigned = Component.Nodes.Where(Symbol => !Component.Steps.ContainsKey(Symbol)).OrderBy(GetSymbolSortKey).ToList();
            if (Unassigned.Count > 0)
            {
                Result.AddWarning("Flowchart found " + Unassigned.Count.ToString(CultureInfo.InvariantCulture) +
                                  " concepts not reachable from component starts; placing them at step 1.");
                foreach (var Symbol in Unassigned)
                    Component.Steps[Symbol] = 1;
            }
        }

        private static bool HasDirectedPath(VisualSymbol From, VisualSymbol To, DirectedConceptGraph Graph,
                                            IList<VisualSymbol> Scope, ISet<VisualSymbol> Visited)
        {
            if (From == null || To == null || Graph == null || !Graph.Children.ContainsKey(From))
                return false;

            if (From == To)
                return true;

            if (!Visited.Add(From))
                return false;

            foreach (var Child in Graph.Children[From].Where(Symbol => Scope.Contains(Symbol)))
                if (Child == To || HasDirectedPath(Child, To, Graph, Scope, Visited))
                    return true;

            return false;
        }

        private static Dictionary<VisualSymbol, Point> ComputePositions(IList<FlowComponent> Components,
                                                                        DirectedConceptGraph Graph,
                                                                        FlowchartLayoutOptions Options,
                                                                        FlowchartLayoutResult Result)
        {
            var Positions = new Dictionary<VisualSymbol, Point>();
            var CurrentY = 0.0;

            foreach (var Component in Components)
            {
                var StepGroups = Component.Nodes.GroupBy(Symbol => Component.Steps.ContainsKey(Symbol) ? Component.Steps[Symbol] : 0)
                                      .OrderBy(Group => Group.Key)
                                      .ToList();
                var OrderedSteps = new Dictionary<int, List<VisualSymbol>>();
                var PreviousOrder = new Dictionary<VisualSymbol, int>();
                foreach (var StepGroup in StepGroups)
                {
                    var Ordered = OrderStepNodes(StepGroup.ToList(), Component, Graph, PreviousOrder);
                    OrderedSteps[StepGroup.Key] = Ordered;
                    PreviousOrder = Ordered.Select((Symbol, Index) => new { Symbol, Index })
                                           .ToDictionary(Item => Item.Symbol, Item => Item.Index);
                }

                var StepWidths = OrderedSteps.ToDictionary(Pair => Pair.Key,
                                                           Pair => Pair.Value.Select(Symbol => Symbol.BaseWidth).DefaultIfEmpty(0.0).Max());
                var StepHeights = OrderedSteps.ToDictionary(Pair => Pair.Key,
                                                            Pair => MeasureStepHeight(Pair.Value, Options.LaneSpacingY));
                var ComponentHeight = StepHeights.Values.DefaultIfEmpty(0.0).Max();
                var StepCenters = ComputeStepCenters(StepWidths, Options.StepSpacingX);

                foreach (var Pair in OrderedSteps.OrderBy(Pair => Pair.Key))
                {
                    var Step = Pair.Key;
                    var Nodes = Pair.Value;
                    var TotalHeight = MeasureStepHeight(Nodes, Options.LaneSpacingY);
                    var Y = CurrentY + (ComponentHeight - TotalHeight) / 2.0;

                    foreach (var Symbol in Nodes)
                    {
                        Y += Symbol.BaseHeight / 2.0;
                        Positions[Symbol] = new Point(StepCenters[Step], Y);
                        Console.WriteLine("Appearance: Flowchart planned concept {0}; step={1}; center=({2:0.##},{3:0.##}).",
                                          DescribeSymbol(Symbol), Step, Positions[Symbol].X, Positions[Symbol].Y);
                        Y += Symbol.BaseHeight / 2.0 + Options.LaneSpacingY;
                    }
                }

                CurrentY += ComponentHeight + Options.ComponentSpacingY;
            }

            return Positions;
        }

        private static Dictionary<int, double> ComputeStepCenters(IDictionary<int, double> StepWidths, double StepSpacingX)
        {
            var Centers = new Dictionary<int, double>();
            var CurrentX = 0.0;
            var PreviousWidth = 0.0;

            foreach (var Step in StepWidths.Keys.OrderBy(Step => Step))
            {
                var Width = StepWidths[Step];
                if (Centers.Count < 1)
                    CurrentX = Width / 2.0;
                else
                    CurrentX += PreviousWidth / 2.0 + StepSpacingX + Width / 2.0;

                Centers[Step] = CurrentX;
                PreviousWidth = Width;
            }

            return Centers;
        }

        private static double MeasureStepHeight(IList<VisualSymbol> Symbols, double LaneSpacingY)
        {
            if (Symbols == null || Symbols.Count < 1)
                return 0.0;

            return Symbols.Sum(Symbol => Symbol.BaseHeight) + Math.Max(0, Symbols.Count - 1) * LaneSpacingY;
        }

        private static Dictionary<VisualSymbol, int> GetStepMap(IEnumerable<FlowComponent> Components)
        {
            return (Components ?? Enumerable.Empty<FlowComponent>())
                   .SelectMany(Component => Component.Steps)
                   .GroupBy(Pair => Pair.Key)
                   .ToDictionary(Group => Group.Key, Group => Group.Min(Pair => Pair.Value));
        }

        private static List<VisualSymbol> OrderStepNodes(IList<VisualSymbol> Nodes, FlowComponent Component,
                                                         DirectedConceptGraph Graph,
                                                         IDictionary<VisualSymbol, int> PreviousOrder)
        {
            return Nodes.OrderBy(Symbol => GetPrimaryParentOrder(Symbol, Component, Graph, PreviousOrder))
                        .ThenBy(GetSymbolSortKey)
                        .ToList();
        }

        private static int GetPrimaryParentOrder(VisualSymbol Symbol, FlowComponent Component, DirectedConceptGraph Graph,
                                                 IDictionary<VisualSymbol, int> PreviousOrder)
        {
            var MatchingParents = Graph.Parents[Symbol]
                                       .Where(Parent => Component.Nodes.Contains(Parent) && PreviousOrder.ContainsKey(Parent))
                                       .ToList();
            return MatchingParents.Count < 1 ? Int32.MaxValue : MatchingParents.Min(Parent => PreviousOrder[Parent]);
        }

        private static void ApplyLayout(IDictionary<VisualSymbol, Point> Positions, FlowchartLayoutResult Result)
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
                Console.WriteLine("Appearance: Flowchart concept {0}; oldCenter=({1:0.##},{2:0.##}); newCenter=({3:0.##},{4:0.##}).",
                                  DescribeSymbol(Symbol), OldCenter.X, OldCenter.Y, NewCenter.X, NewCenter.Y);
            }
        }

        private static IList<RelationshipVisualRepresentation> PlaceFeedbackLaneRelationships(View View,
                                                                                                IEnumerable<FlowRelationshipInfo> RelationshipInfos,
                                                                                                IList<VisualSymbol> ScopeSymbols,
                                                                                                FlowchartLayoutOptions Options,
                                                                                                FlowchartLayoutResult Result)
        {
            var FeedbackInfos = (RelationshipInfos ?? Enumerable.Empty<FlowRelationshipInfo>())
                                .Where(Info => Info != null && Info.UsesFeedbackLane && Info.Representation != null)
                                .OrderBy(Info => Info.SortKey)
                                .ToList();
            if (FeedbackInfos.Count < 1)
                return new List<RelationshipVisualRepresentation>();

            var ConceptBounds = LayoutBoundsNormalizer.ComputeSymbolBounds(ScopeSymbols);
            var Lane = ChooseFeedbackLane(FeedbackInfos, ConceptBounds, Options);
            var LaneDirection = Lane.Item1;
            var LaneY = Lane.Item2;
            var LaneLabel = LaneDirection < 0 ? "top" : "bottom";

            Console.WriteLine("Appearance: Flowchart feedback lane selected; placement={0}; lane={1}; y={2:0.##}; relationships={3}; conceptBounds={4}.",
                              Options.FeedbackLanePlacement.ToStringAlways("Auto"),
                              LaneLabel,
                              LaneY,
                              FeedbackInfos.Count,
                              LayoutBoundsNormalizer.FormatRect(ConceptBounds));

            var RoutedRepresentations = new List<RelationshipVisualRepresentation>();
            for (var Index = 0; Index < FeedbackInfos.Count; Index++)
            {
                var Info = FeedbackInfos[Index];
                var MainSymbol = Info.Representation.MainSymbol;
                if (MainSymbol == null)
                {
                    Result.FeedbackLaneRelationshipsSkipped++;
                    Result.AddWarning("Flowchart skipped feedback lane placement for '" +
                                      Info.Relationship.TechName.ToStringAlways() + "': relationship has no central symbol.");
                    continue;
                }

                var Source = AveragePoint(Info.Origins.Select(Symbol => Symbol.BaseCenter));
                var Target = AveragePoint(Info.Targets.Select(Symbol => Symbol.BaseCenter));
                var CenterX = (Source.X + Target.X) / 2.0;
                var CenterY = LaneY + LaneDirection * Index * Options.FeedbackLaneSpacingY;
                var HalfHeight = Math.Max(MainSymbol.BaseHeight / 2.0, 10.0);
                if (CenterY - HalfHeight < Options.CanvasPadding)
                    CenterY = Options.CanvasPadding + HalfHeight;

                var OldCenter = MainSymbol.BaseCenter;
                var NewCenter = new Point(CenterX, CenterY);
                Result.FeedbackLaneRelationships++;

                Console.WriteLine("Appearance: Flowchart feedback lane placement: {0}; type={1}; sourceStep={2}; targetStep={3}; lane={4}; oldBubble=({5:0.##},{6:0.##}); newBubble=({7:0.##},{8:0.##}).",
                                  DescribeIdea(Info.Relationship),
                                  FormatEdgeType(Info.EdgeType),
                                  Info.SourceStep,
                                  Info.TargetStep,
                                  LaneLabel,
                                  OldCenter.X,
                                  OldCenter.Y,
                                  NewCenter.X,
                                  NewCenter.Y);

                if (Distance(OldCenter, NewCenter) > GeometryTolerance)
                {
                    MainSymbol.MoveTo(NewCenter.X, NewCenter.Y, true);
                    Result.FeedbackLaneRelationshipsMoved++;
                }
                else
                    MainSymbol.RenderElement();

                if (RouteFeedbackRelationshipThroughLane(Info, Source, Target, NewCenter, Result))
                    RoutedRepresentations.Add(Info.Representation);
            }

            return RoutedRepresentations.Distinct().ToList();
        }

        private static Tuple<int, double> ChooseFeedbackLane(IList<FlowRelationshipInfo> FeedbackInfos, Rect ConceptBounds,
                                                             FlowchartLayoutOptions Options)
        {
            var Placement = Options.FeedbackLanePlacement.ToStringAlways("Auto").Trim();
            var MaxHalfHeight = FeedbackInfos.Select(Info => Info.Representation == null || Info.Representation.MainSymbol == null
                                                            ? 16.0
                                                            : Math.Max(Info.Representation.MainSymbol.BaseHeight / 2.0, 16.0))
                                             .DefaultIfEmpty(16.0)
                                             .Max();
            var LanePadding = FeedbackInfos.Any(Info => Info.EdgeType == FlowchartEdgeType.LongCrossLink)
                              ? Math.Max(Options.FeedbackLanePaddingY, Options.CrossLinkLanePaddingY)
                              : Options.FeedbackLanePaddingY;
            var TopY = ConceptBounds.Top - LanePadding;
            var BottomY = ConceptBounds.Bottom + LanePadding;
            var TopHasRoom = TopY - MaxHalfHeight >= Options.CanvasPadding;

            if (Placement.Equals("Top", StringComparison.OrdinalIgnoreCase))
                return Tuple.Create(-1, Math.Max(Options.CanvasPadding + MaxHalfHeight, TopY));

            if (Placement.Equals("Bottom", StringComparison.OrdinalIgnoreCase))
                return Tuple.Create(1, BottomY);

            if (Options.PreferTopFeedbackLane && TopHasRoom)
                return Tuple.Create(-1, TopY);

            return Tuple.Create(1, BottomY);
        }

        private static bool RouteFeedbackRelationshipThroughLane(FlowRelationshipInfo Info, Point Source, Point Target,
                                                                  Point LaneCenter, FlowchartLayoutResult Result)
        {
            if (Info == null || Info.Representation == null || Info.Representation.MainSymbol == null)
                return false;

            var SourceSymbol = Info.Origins.OrderBy(GetSymbolSortKey).FirstOrDefault();
            var TargetSymbol = Info.Targets.OrderBy(GetSymbolSortKey).FirstOrDefault();
            if (SourceSymbol == null || TargetSymbol == null)
            {
                Result.FeedbackLaneRelationshipsSkipped++;
                Result.AddWarning("Flowchart could not route feedback relationship '" +
                                  Info.Relationship.TechName.ToStringAlways() + "': missing source or target symbol.");
                return false;
            }

            VisualConnector SourceConnector;
            VisualConnector TargetConnector;
            if (!TryFindRelationshipConnector(Info.Representation, SourceSymbol, Info.Representation.MainSymbol, out SourceConnector) ||
                !TryFindRelationshipConnector(Info.Representation, TargetSymbol, Info.Representation.MainSymbol, out TargetConnector))
            {
                Result.FeedbackLaneRelationshipsSkipped++;
                Result.AddWarning("Flowchart could not route feedback relationship '" +
                                  Info.Relationship.TechName.ToStringAlways() + "': expected source/target connector segments were not found.");
                Console.WriteLine("Appearance: Flowchart feedback route skipped; relationship={0}; reason=missing connector ordering.",
                                  DescribeIdea(Info.Relationship));
                return false;
            }

            var SourceIntermediate = new Point(SourceSymbol.BaseCenter.X, LaneCenter.Y);
            var TargetIntermediate = new Point(TargetSymbol.BaseCenter.X, LaneCenter.Y);
            SourceConnector.SetRoutePoints(new[] { SourceIntermediate });
            TargetConnector.SetRoutePoints(new[] { TargetIntermediate });

            foreach (var Connector in Info.Representation.VisualConnectors
                                              .Where(Connector => Connector != null &&
                                                                  Connector != SourceConnector &&
                                                                  Connector != TargetConnector))
                Connector.RenderElement();

            Info.Representation.Render();
            Result.FeedbackLaneRelationshipsRouted++;

            Console.WriteLine("Appearance: Flowchart feedback route: {0} source={1} step={2} target={3} step={4} laneY={5:0.##}; bubble=({6:0.##},{7:0.##}); route=horizontal-feedback-lane; sourceIntermediate=({8:0.##},{9:0.##}); targetIntermediate=({10:0.##},{11:0.##}).",
                              DescribeIdea(Info.Relationship),
                              DescribeSymbol(SourceSymbol),
                              Info.SourceStep,
                              DescribeSymbol(TargetSymbol),
                              Info.TargetStep,
                              LaneCenter.Y,
                              LaneCenter.X,
                              LaneCenter.Y,
                              SourceIntermediate.X,
                              SourceIntermediate.Y,
                              TargetIntermediate.X,
                              TargetIntermediate.Y);
            return true;
        }

        private static bool TryFindRelationshipConnector(RelationshipVisualRepresentation Representation, VisualSymbol Endpoint,
                                                          VisualSymbol MainSymbol, out VisualConnector Connector)
        {
            Connector = null;
            if (Representation == null || Endpoint == null || MainSymbol == null || Representation.VisualConnectors == null)
                return false;

            Connector = Representation.VisualConnectors
                                      .Where(Item => Item != null)
                                      .FirstOrDefault(Item => (Item.OriginSymbol == Endpoint && Item.TargetSymbol == MainSymbol) ||
                                                              (Item.OriginSymbol == MainSymbol && Item.TargetSymbol == Endpoint));
            if (Connector == null && Endpoint.OwnerRepresentation != null)
            {
                // The same Idea may have several visual representations in one View.  Graph
                // analysis is Idea-based, while the relationship connector is representation-
                // based, so fall back to the actual connected symbol for the same Idea.
                var EndpointIdea = Endpoint.OwnerRepresentation.RepresentedIdea;
                Connector = Representation.VisualConnectors
                    .Where(Item => Item != null)
                    .Where(Item => Item.OriginSymbol == MainSymbol || Item.TargetSymbol == MainSymbol)
                    .Where(Item =>
                    {
                        var Other = Item.OriginSymbol == MainSymbol ? Item.TargetSymbol : Item.OriginSymbol;
                        return Other != null && Other.OwnerRepresentation != null &&
                               Other.OwnerRepresentation.RepresentedIdea == EndpointIdea;
                    })
                    .OrderBy(Item => Item.GlobalId)
                    .FirstOrDefault();
            }
            return Connector != null;
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

        private static LinkObstacleRoutingResult RouteScopeLinks(LayoutSelectionContext Context,
                                                                 IEnumerable<RelationshipVisualRepresentation> RelationshipRepresentations,
                                                                 IEnumerable<RelationshipVisualRepresentation> MandatoryCorridorRelationships,
                                                                 IEnumerable<VisualSymbol> MovedSymbols)
        {
            var Moved = new HashSet<VisualSymbol>((MovedSymbols ?? Enumerable.Empty<VisualSymbol>()).Where(Symbol => Symbol != null));
            var Incident = Context.VisibleRelationshipRepresentations
                                  .Where(Representation => Representation != null &&
                                         Representation.VisualConnectors.Any(Connector => Connector != null &&
                                             (Moved.Contains(Connector.OriginSymbol) || Moved.Contains(Connector.TargetSymbol))));
            var Connectors = (RelationshipRepresentations ?? Enumerable.Empty<RelationshipVisualRepresentation>()).Concat(Incident)
                                  .Distinct()
                                  .Where(Representation => Representation != null)
                                  .SelectMany(Representation => Representation.VisualConnectors)
                                  .Where(Connector => Connector != null)
                                  .Cast<VisualObject>()
                                  .Distinct()
                                  .ToList();

            if (Connectors.Count < 1)
            {
                Console.WriteLine("Appearance: Flowchart post-route skipped; no relationship connectors are in scope.");
                return null;
            }

            var RouteContext = LayoutSelectionContext.FromViewSelection(Context.Engine, Context.ActiveView, Connectors);
            var RouteOptions = new LinkObstacleRoutingOptions();
            RouteOptions.RouteSelectedConnectorsOnly = true;
            RouteOptions.PreserveExistingValidRoutes = true;
            RouteOptions.RouteIntent = RelationshipRouteIntent.Layout;
            RouteOptions.DirtyReason = "Flowchart moved endpoint symbols; feedback-lane routes are mandatory existing corridors";
            RouteOptions.Profile = RelationshipRoutingProfile.Flowchart;
            RouteOptions.IncludeRelationshipCentralSymbolsAsObstacles = true;
            // The Flowchart pass has already positioned relationship hubs and has authored
            // feedback-lane waypoints.  Re-running generic endpoint-corridor placement here can
            // move the hub and clear those mandatory points before the shared router sees them.
            RouteOptions.CorrectRelationshipCentersBeforeRouting = false;
            RelationshipRoutingCoordinator.ConfigureMandatoryCorridors(RouteOptions, MandatoryCorridorRelationships);
            return RelationshipRoutingCoordinator.Route(RouteContext, RouteOptions);
        }

        private static void ValidateFlowchartRoutes(IEnumerable<FlowRelationshipInfo> RelationshipInfos,
                                                    IList<VisualSymbol> ScopeSymbols,
                                                    IEnumerable<RelationshipVisualRepresentation> FeedbackLaneRepresentations,
                                                    FlowchartLayoutOptions Options,
                                                    FlowchartLayoutResult Result)
        {
            var Infos = (RelationshipInfos ?? Enumerable.Empty<FlowRelationshipInfo>())
                        .Where(Info => Info != null && Info.Representation != null)
                        .ToList();
            var FeedbackSet = new HashSet<RelationshipVisualRepresentation>((FeedbackLaneRepresentations ?? Enumerable.Empty<RelationshipVisualRepresentation>())
                                                                            .Where(Representation => Representation != null));
            var ConceptBounds = (ScopeSymbols ?? Enumerable.Empty<VisualSymbol>())
                                .Where(Symbol => Symbol != null)
                                .Select(Symbol => new { Symbol = Symbol, Bounds = Inflate(Symbol.TotalArea, 8.0) })
                                .Where(Item => !Item.Bounds.IsEmpty)
                                .ToList();
            var BubbleBounds = Infos.Where(Info => Info.Representation.MainSymbol != null &&
                                                   !Info.Representation.MainSymbol.IsHidden &&
                                                   Info.Representation.MainSymbol.IsRelatedVisible)
                                    .Select(Info => new
                                    {
                                        Info = Info,
                                        Bounds = Inflate(Info.Representation.MainSymbol.TotalArea, 8.0)
                                    })
                                    .Where(Item => !Item.Bounds.IsEmpty)
                                    .ToList();

            foreach (var Bubble in BubbleBounds)
                foreach (var Concept in ConceptBounds)
                    if (!Bubble.Info.Endpoints.Contains(Concept.Symbol) &&
                        Bubble.Bounds.IntersectsWith(Concept.Bounds))
                        AddValidationWarning(Result, "Flowchart validation warning: relationship bubble '" +
                                             Bubble.Info.Relationship.TechName.ToStringAlways() +
                                             "' overlaps concept '" + GetIdeaName(Concept.Symbol) + "'.");

            for (var FirstIndex = 0; FirstIndex < BubbleBounds.Count; FirstIndex++)
                for (var SecondIndex = FirstIndex + 1; SecondIndex < BubbleBounds.Count; SecondIndex++)
                    if (BubbleBounds[FirstIndex].Bounds.IntersectsWith(BubbleBounds[SecondIndex].Bounds))
                        AddValidationWarning(Result, "Flowchart validation warning: relationship bubble '" +
                                             BubbleBounds[FirstIndex].Info.Relationship.TechName.ToStringAlways() +
                                             "' overlaps relationship bubble '" +
                                             BubbleBounds[SecondIndex].Info.Relationship.TechName.ToStringAlways() + "'.");

            foreach (var Info in Infos.Where(Info => FeedbackSet.Contains(Info.Representation)))
            {
                var SourceSymbol = Info.Origins.OrderBy(GetSymbolSortKey).FirstOrDefault();
                var TargetSymbol = Info.Targets.OrderBy(GetSymbolSortKey).FirstOrDefault();
                var MainSymbol = Info.Representation.MainSymbol;
                if (SourceSymbol == null || TargetSymbol == null || MainSymbol == null)
                    continue;

                var RoutePoints = GetRelationshipRoutePoints(Info, SourceSymbol, TargetSymbol, MainSymbol);
                for (var Index = 0; Index < RoutePoints.Count - 1; Index++)
                {
                    var Start = RoutePoints[Index];
                    var End = RoutePoints[Index + 1];
                    foreach (var Concept in ConceptBounds)
                        if (Concept.Symbol != SourceSymbol &&
                            Concept.Symbol != TargetSymbol &&
                            SegmentIntersectsRect(Start, End, Concept.Bounds))
                            AddValidationWarning(Result, "Flowchart validation warning: feedback edge '" +
                                                 Info.Relationship.TechName.ToStringAlways() +
                                                 "' segment intersects concept '" + GetIdeaName(Concept.Symbol) + "'.");

                    foreach (var Bubble in BubbleBounds)
                        if (Bubble.Info.Representation != Info.Representation &&
                            SegmentIntersectsRect(Start, End, Bubble.Bounds))
                            AddValidationWarning(Result, "Flowchart validation warning: feedback edge '" +
                                                 Info.Relationship.TechName.ToStringAlways() +
                                                 "' segment intersects relationship bubble '" +
                                                 Bubble.Info.Relationship.TechName.ToStringAlways() + "'.");
                }
            }

            Console.WriteLine("Appearance: Flowchart validation completed; feedbackLaneRelationships={0}; warnings={1}.",
                              FeedbackSet.Count,
                              Result.FlowchartValidationWarnings);
        }

        private static List<Point> GetRelationshipRoutePoints(FlowRelationshipInfo Info, VisualSymbol SourceSymbol,
                                                              VisualSymbol TargetSymbol, VisualSymbol MainSymbol)
        {
            var Points = new List<Point>();
            Points.Add(SourceSymbol.BaseCenter);

            VisualConnector SourceConnector;
            if (TryFindRelationshipConnector(Info.Representation, SourceSymbol, MainSymbol, out SourceConnector))
            {
                var SourceRoute = SourceConnector.RoutePoints == null
                                  ? new List<Point>() : SourceConnector.RoutePoints.ToList();
                if (SourceConnector.OriginSymbol != SourceSymbol)
                    SourceRoute.Reverse();
                Points.AddRange(SourceRoute.Where(IsUsablePoint));
            }

            Points.Add(MainSymbol.BaseCenter);

            VisualConnector TargetConnector;
            if (TryFindRelationshipConnector(Info.Representation, TargetSymbol, MainSymbol, out TargetConnector))
            {
                var TargetRoute = TargetConnector.RoutePoints == null
                                  ? new List<Point>() : TargetConnector.RoutePoints.ToList();
                if (TargetConnector.OriginSymbol != MainSymbol)
                    TargetRoute.Reverse();
                Points.AddRange(TargetRoute.Where(IsUsablePoint));
            }

            Points.Add(TargetSymbol.BaseCenter);
            return Points;
        }

        private static void AddValidationWarning(FlowchartLayoutResult Result, string Warning)
        {
            Result.FlowchartValidationWarnings++;
            Result.AddWarning(Warning);
            Console.WriteLine(Warning);
        }

        private static Rect Inflate(Rect Bounds, double Padding)
        {
            if (Bounds.IsEmpty)
                return Bounds;

            Bounds.Inflate(Padding, Padding);
            return Bounds;
        }

        private static bool SegmentIntersectsRect(Point Start, Point End, Rect Bounds)
        {
            if (Bounds.IsEmpty)
                return false;

            if (Bounds.Contains(Start) || Bounds.Contains(End))
                return true;

            var TopLeft = new Point(Bounds.Left, Bounds.Top);
            var TopRight = new Point(Bounds.Right, Bounds.Top);
            var BottomRight = new Point(Bounds.Right, Bounds.Bottom);
            var BottomLeft = new Point(Bounds.Left, Bounds.Bottom);

            return SegmentsIntersect(Start, End, TopLeft, TopRight) ||
                   SegmentsIntersect(Start, End, TopRight, BottomRight) ||
                   SegmentsIntersect(Start, End, BottomRight, BottomLeft) ||
                   SegmentsIntersect(Start, End, BottomLeft, TopLeft);
        }

        private static bool SegmentsIntersect(Point FirstStart, Point FirstEnd, Point SecondStart, Point SecondEnd)
        {
            var D1 = Direction(SecondStart, SecondEnd, FirstStart);
            var D2 = Direction(SecondStart, SecondEnd, FirstEnd);
            var D3 = Direction(FirstStart, FirstEnd, SecondStart);
            var D4 = Direction(FirstStart, FirstEnd, SecondEnd);

            if (((D1 > GeometryTolerance && D2 < -GeometryTolerance) || (D1 < -GeometryTolerance && D2 > GeometryTolerance)) &&
                ((D3 > GeometryTolerance && D4 < -GeometryTolerance) || (D3 < -GeometryTolerance && D4 > GeometryTolerance)))
                return true;

            return Math.Abs(D1) <= GeometryTolerance && PointOnSegment(SecondStart, SecondEnd, FirstStart) ||
                   Math.Abs(D2) <= GeometryTolerance && PointOnSegment(SecondStart, SecondEnd, FirstEnd) ||
                   Math.Abs(D3) <= GeometryTolerance && PointOnSegment(FirstStart, FirstEnd, SecondStart) ||
                   Math.Abs(D4) <= GeometryTolerance && PointOnSegment(FirstStart, FirstEnd, SecondEnd);
        }

        private static double Direction(Point First, Point Second, Point Test)
        {
            return (Test.X - First.X) * (Second.Y - First.Y) -
                   (Test.Y - First.Y) * (Second.X - First.X);
        }

        private static bool PointOnSegment(Point First, Point Second, Point Test)
        {
            return Test.X >= Math.Min(First.X, Second.X) - GeometryTolerance &&
                   Test.X <= Math.Max(First.X, Second.X) + GeometryTolerance &&
                   Test.Y >= Math.Min(First.Y, Second.Y) - GeometryTolerance &&
                   Test.Y <= Math.Max(First.Y, Second.Y) + GeometryTolerance;
        }

        private static void RevealArrangedBounds(View View, FlowchartLayoutResult Result)
        {
            if (View == null || View.Presenter == null || Result == null || Result.BoundsAfterNormalization.IsEmpty)
            {
                if (Result != null)
                    Result.RevealAction = "none";

                Console.WriteLine("Appearance: Flowchart reveal arranged bounds: view={0}; bounds={1}; action=none.",
                                  DescribeView(View),
                                  Result == null ? "<none>" : LayoutBoundsNormalizer.FormatRect(Result.BoundsAfterNormalization));
                return;
            }

            var Bounds = Result.BoundsAfterNormalization;
            Bounds.Inflate(Result.BoundsAfterNormalization.Width * 0.08 + 40.0,
                           Result.BoundsAfterNormalization.Height * 0.08 + 40.0);
            View.Presenter.BringIntoView(Bounds);
            Result.RevealAction = "BringIntoView";
            Console.WriteLine("Appearance: Flowchart reveal arranged bounds: view={0}; bounds={1}; action=BringIntoView.",
                              DescribeView(View), LayoutBoundsNormalizer.FormatRect(Bounds));
        }

        private static Point DetermineTieCenter(LayoutSelectionContext Context, IList<VisualSymbol> Symbols,
                                                FlowchartLayoutOptions Options)
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

        private static Point AveragePoint(IEnumerable<Point> Points)
        {
            var UsablePoints = (Points ?? Enumerable.Empty<Point>()).Where(IsUsablePoint).ToList();
            if (UsablePoints.Count < 1)
                return new Point(0, 0);

            return new Point(UsablePoints.Average(Point => Point.X),
                             UsablePoints.Average(Point => Point.Y));
        }

        private static string FormatEdgeType(FlowchartEdgeType EdgeType)
        {
            switch (EdgeType)
            {
                case FlowchartEdgeType.PrimaryForward:
                    return "primary-forward";
                case FlowchartEdgeType.BranchForward:
                    return "branch-forward";
                case FlowchartEdgeType.SameLevel:
                    return "same-level";
                case FlowchartEdgeType.FeedbackReverse:
                    return "feedback/reverse";
                case FlowchartEdgeType.LongCrossLink:
                    return "long-cross-link";
                default:
                    return "ambiguous";
            }
        }

        private static string GetSymbolSortKey(VisualSymbol Symbol)
        {
            if (Symbol == null || Symbol.OwnerRepresentation == null || Symbol.OwnerRepresentation.RepresentedIdea == null)
                return String.Empty;

            var Idea = Symbol.OwnerRepresentation.RepresentedIdea;
            return Idea.Name.ToStringAlways() + "|" + Idea.TechName.ToStringAlways() + "|" + Idea.GlobalId.ToString("D");
        }

        private static string GetIdeaName(VisualSymbol Symbol)
        {
            if (Symbol == null || Symbol.OwnerRepresentation == null || Symbol.OwnerRepresentation.RepresentedIdea == null)
                return "<no concept>";

            return Symbol.OwnerRepresentation.RepresentedIdea.Name.ToStringAlways();
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

        private static void LogSummary(FlowchartLayoutResult Result)
        {
            Console.WriteLine("Appearance: Arrange as Flowchart completed; concepts inspected={0}; arranged={1}; moved={2}; skipped={3}; relationships inspected={4}; directedEdges={5}; undirectedEdges={6}; components={7}; starts={8}; steps={9}; cycles={10}; links routed={11}; route skipped={12}; warnings={13}.",
                              Result.ConceptsInspected,
                              Result.ConceptsArranged,
                              Result.ConceptsMoved,
                              Result.ConceptsSkipped,
                              Result.RelationshipsInspected,
                              Result.DirectedEdges,
                              Result.UndirectedEdges,
                              Result.ComponentCount,
                              Result.StartCount,
                              Result.StepCount,
                              Result.CyclesDetected,
                              Result.LinksRouted,
                              Result.RoutingResult == null ? 0 : Result.RoutingResult.Skipped,
                              Result.Warnings.Count);

            Console.WriteLine("Appearance: Flowchart edge summary; primary-forward={0}; branch-forward={1}; same-level={2}; feedback/reverse={3}; long-cross-link={4}; ambiguous={5}.",
                              Result.PrimaryForwardEdges,
                              Result.BranchForwardEdges,
                              Result.SameLevelEdges,
                              Result.FeedbackReverseEdges,
                              Result.LongCrossLinkEdges,
                              Result.AmbiguousEdges);

            Console.WriteLine("Appearance: Flowchart feedback lane summary; relationships={0}; moved={1}; routed={2}; skipped={3}; validationWarnings={4}.",
                              Result.FeedbackLaneRelationships,
                              Result.FeedbackLaneRelationshipsMoved,
                              Result.FeedbackLaneRelationshipsRouted,
                              Result.FeedbackLaneRelationshipsSkipped,
                              Result.FlowchartValidationWarnings);

            if (Result.AutoFitResult != null)
                Console.WriteLine("Appearance: Flowchart auto-fit summary; inspected={0}; fitted={1}; skipped={2}.",
                                  Result.AutoFitResult.SymbolsInspected,
                                  Result.AutoFitResult.SymbolsFitted,
                                  Result.AutoFitResult.SymbolsSkipped);

            if (Result.RoutingResult != null)
                Console.WriteLine("Appearance: Flowchart route summary; connector routes inspected={0}; relationship routes inspected={1}; routed={2}; dogleg routed={3}; straightened={4}; unchanged={5}; skipped={6}.",
                                  Result.RoutingResult.ConnectorRoutesInspected,
                                  Result.RoutingResult.RelationshipRoutesInspected,
                                  Result.RoutingResult.Routed,
                                  Result.RoutingResult.DoglegRouted,
                                  Result.RoutingResult.Straightened,
                                  Result.RoutingResult.Unchanged,
                                  Result.RoutingResult.Skipped);

            if (Result.RelationshipNodeDeclutterResult != null)
                Console.WriteLine("Appearance: Flowchart relationship node declutter summary; inspected={0}; moved={1}; skipped={2}; initialOverlaps={3}; overlapGroups={4}; globalPasses={5}; globalMoves={6}; corridorCorrections={7}; corridorViolations={8}; finalBubbleOverlaps={9}; finalConceptOverlaps={10}; warnings={11}.",
                                  Result.RelationshipNodeDeclutterResult.RelationshipSymbolsInspected,
                                  Result.RelationshipNodeDeclutterResult.RelationshipSymbolsMoved,
                                  Result.RelationshipNodeDeclutterResult.RelationshipSymbolsSkipped,
                                  Result.RelationshipNodeDeclutterResult.InitialOverlapCount,
                                  Result.RelationshipNodeDeclutterResult.OverlapGroupsDetected,
                                  Result.RelationshipNodeDeclutterResult.GlobalDeclutterPasses,
                                  Result.RelationshipNodeDeclutterResult.GlobalDeclutterMoves,
                                  Result.RelationshipNodeDeclutterResult.CorridorCorrections,
                                  Result.RelationshipNodeDeclutterResult.CorridorViolations,
                                  Result.RelationshipNodeDeclutterResult.FinalOverlapCount,
                                  Result.RelationshipNodeDeclutterResult.FinalConceptOverlapCount,
                                  Result.RelationshipNodeDeclutterResult.Warnings.Count);

            Console.WriteLine("Appearance: Flowchart bounds summary; beforeNormalize={0}; dx={1:0.##}; dy={2:0.##}; final={3}; withinSafeCanvas={4}; reveal={5}.",
                              LayoutBoundsNormalizer.FormatRect(Result.BoundsBeforeNormalization),
                              Result.NormalizationDelta.X,
                              Result.NormalizationDelta.Y,
                              LayoutBoundsNormalizer.FormatRect(Result.BoundsAfterNormalization),
                              Result.FinalBoundsWithinSafeCanvas ? "true" : "false",
                              Result.RevealAction.ToStringAlways("none"));

            foreach (var Warning in Result.Warnings)
                Console.WriteLine("Appearance Flowchart warning: {0}", Warning);
        }
    }
}
