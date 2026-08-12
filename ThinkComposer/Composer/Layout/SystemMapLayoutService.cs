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
using Instrumind.ThinkComposer.MetaModel;
using Instrumind.ThinkComposer.MetaModel.GraphMetaModel;
using Instrumind.ThinkComposer.Model.GraphModel;
using Instrumind.ThinkComposer.Model.VisualModel;

namespace Instrumind.ThinkComposer.Composer.Layout
{
    /// <summary>
    /// First-pass system-boundary layout for system/context maps.
    /// </summary>
    public static class SystemMapLayoutService
    {
        private const double GeometryTolerance = 0.5;

        private enum SystemConceptRole
        {
            SystemRoot,
            Internal,
            External,
            Ambiguous
        }

        private enum ExternalSide
        {
            Left,
            Right,
            Top,
            Bottom
        }

        private enum SystemRelationshipKind
        {
            InternalInternal,
            ExternalToInternal,
            InternalToExternal,
            ExternalExternal,
            RootInternal,
            Ambiguous
        }

        private class SystemConceptPlacement
        {
            public VisualSymbol Symbol;
            public SystemConceptRole Role;
            public ExternalSide ExternalSide;
            public string Reason;

            public bool IsInsideBoundary
            {
                get { return this.Role == SystemConceptRole.SystemRoot || this.Role == SystemConceptRole.Internal || this.Role == SystemConceptRole.Ambiguous; }
            }
        }

        private class SystemGraph
        {
            public SystemGraph()
            {
                this.Adjacency = new Dictionary<VisualSymbol, HashSet<VisualSymbol>>();
                this.Children = new Dictionary<VisualSymbol, HashSet<VisualSymbol>>();
                this.Parents = new Dictionary<VisualSymbol, HashSet<VisualSymbol>>();
                this.RelationshipRepresentations = new List<RelationshipVisualRepresentation>();
            }

            public Dictionary<VisualSymbol, HashSet<VisualSymbol>> Adjacency;
            public Dictionary<VisualSymbol, HashSet<VisualSymbol>> Children;
            public Dictionary<VisualSymbol, HashSet<VisualSymbol>> Parents;
            public List<RelationshipVisualRepresentation> RelationshipRepresentations;
        }

        private class SystemRelationshipInfo
        {
            public SystemRelationshipInfo()
            {
                this.Origins = new List<SystemConceptPlacement>();
                this.Targets = new List<SystemConceptPlacement>();
                this.Endpoints = new List<SystemConceptPlacement>();
                this.Kind = SystemRelationshipKind.Ambiguous;
                this.Side = ExternalSide.Left;
                this.Reason = "";
            }

            public RelationshipVisualRepresentation Representation;
            public Relationship Relationship;
            public List<SystemConceptPlacement> Origins;
            public List<SystemConceptPlacement> Targets;
            public List<SystemConceptPlacement> Endpoints;
            public SystemRelationshipKind Kind;
            public ExternalSide Side;
            public string Reason;

            public bool IsCrossBoundary
            {
                get
                {
                    return this.Kind == SystemRelationshipKind.ExternalToInternal ||
                           this.Kind == SystemRelationshipKind.InternalToExternal;
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

        private class SystemMapObstacle
        {
            public Rect Bounds;
            public string Description;
            public bool IsConcept;
            public bool IsRelationshipBubble;
        }

        private class CrossBoundaryLane
        {
            public ExternalSide Side;
            public Rect Bounds;
            public Rect ExternalBounds;
            public double BoundaryX;
        }

        private class CrossBoundaryBubbleCandidate
        {
            public CrossBoundaryBubbleCandidate()
            {
                this.RejectionReasons = new List<string>();
            }

            public Point Center;
            public string Label;
            public double Score;
            public bool IsValid;
            public int ConceptOverlaps;
            public int RelationshipOverlaps;
            public List<string> RejectionReasons;
        }

        public static bool CanArrange(LayoutSelectionContext Context)
        {
            return Context != null && Context.ActiveView != null && Context.VisibleConceptSymbols.Any(IsRouteableConceptSymbol);
        }

        public static SystemMapLayoutResult Arrange(LayoutSelectionContext Context, SystemMapLayoutOptions Options)
        {
            Options = Options ?? new SystemMapLayoutOptions();
            var Result = new SystemMapLayoutResult();

            if (Context == null || Context.ActiveView == null)
            {
                Result.AddWarning("No active view is available for System Map arrangement.");
                LogSummary(Result);
                return Result;
            }

            var View = Context.ActiveView;
            var LocalCommand = !View.EditEngine.IsVariating;

            try
            {
                if (LocalCommand)
                    View.EditEngine.StartCommandVariation("Arrange as System Map");

                var ScopeSymbols = GetScopeSymbols(Context, Options, Result);
                Result.ConceptsInspected = ScopeSymbols.Count;

                Console.WriteLine("Appearance: Arrange as System Map starting; view={0}; scope={1}; concepts={2}; internalSpacing=({3:0.##},{4:0.##}); externalOffsetX={5:0.##}; boundaryPadding={6:0.##}; groupRegionPadding=(left={7:0.##}, top={8:0.##}, right={9:0.##}, bottom={10:0.##}); createGroupRegion={11}; reuseGroupRegion={12}.",
                                  DescribeView(View),
                                  Options.ArrangeSelectedConceptsOnly ? "selected concepts" : "all visible concepts",
                                  ScopeSymbols.Count,
                                  Options.InternalSpacingX,
                                  Options.InternalSpacingY,
                                  Options.ExternalOffsetX,
                                  Options.BoundaryPadding,
                                  Options.GroupRegionPaddingLeft,
                                  Options.GroupRegionPaddingTop,
                                  Options.GroupRegionPaddingRight,
                                  Options.GroupRegionPaddingBottom,
                                  Options.CreateGroupRegion ? "true" : "false",
                                  Options.ReuseExistingGroupRegion ? "true" : "false");

                if (ScopeSymbols.Count < 1)
                {
                    Result.AddWarning("No concept symbols are available in the requested System Map scope.");
                    if (LocalCommand)
                        View.EditEngine.CompleteCommandVariation();
                    LogSummary(Result);
                    return Result;
                }

                if (Options.AutoFitConceptsBeforeArrange)
                    Result.AutoFitResult = ConceptAutoFitService.FitConceptSymbols(Context.Engine, ScopeSymbols, "system map layout");

                var Graph = BuildVisibleGraph(Context, ScopeSymbols, Result);
                string RootSelectionReason;
                var Root = DetermineSystemRoot(Context, ScopeSymbols, Graph, Options, out RootSelectionReason);
                Result.SystemRootSymbol = Root;
                Result.RootSelectionReason = RootSelectionReason;
                Result.SystemRootCount = Root == null ? 0 : 1;

                if (Root == null)
                {
                    Result.AddWarning("No valid system/root concept could be determined for System Map arrangement.");
                    if (LocalCommand)
                        View.EditEngine.CompleteCommandVariation();
                    LogSummary(Result);
                    return Result;
                }

                Console.WriteLine("Appearance: System Map root selected: {0}; reason={1}.",
                                  DescribeSymbol(Root), Result.RootSelectionReason);

                var Placements = ClassifyConcepts(ScopeSymbols, Root, Graph, Context, Options, Result);
                var PlannedPositions = ComputePositions(Context, Placements, Root, Options, Result);

                Console.WriteLine("Appearance: System Map implicit boundary before apply: {0}.",
                                  LayoutBoundsNormalizer.FormatRect(Result.BoundaryRectangle));
                Console.WriteLine("Appearance: System Map layout bounds before apply: {0}.",
                                  LayoutBoundsNormalizer.FormatRect(ComputePlannedBounds(PlannedPositions)));

                ApplyLayout(PlannedPositions, Result);

                if (Options.NormalizeBounds)
                {
                    var NormalizeResult = LayoutBoundsNormalizer.NormalizeSymbolsToCanvas(View, ScopeSymbols, Options.CanvasPadding,
                                                                                          "Appearance: System Map");
                    Result.BoundsBeforeNormalization = NormalizeResult.BoundsBefore;
                    Result.BoundsAfterNormalization = NormalizeResult.BoundsAfter;
                    Result.NormalizationDelta = NormalizeResult.Translation;
                    Result.BoundsNormalized = NormalizeResult.WasNormalized;
                    Result.FinalBoundsWithinSafeCanvas = NormalizeResult.IsWithinSafeBounds;
                }
                else
                    Result.BoundsAfterNormalization = LayoutBoundsNormalizer.ComputeSymbolBounds(ScopeSymbols);

                Result.BoundaryRectangle = ComputeGroupRegionBoundaryFromFinalSymbols(Placements, Options);
                Console.WriteLine("Appearance: System Map group region final symbol boundary: {0}.",
                                  LayoutBoundsNormalizer.FormatRect(Result.BoundaryRectangle));
                var GroupRegion = ApplyGroupRegionBoundary(View, Root, Placements, Options, Result);

                var RelationshipInfos = ClassifySystemRelationships(Graph, Placements, Result);

                if (Options.DeclutterRelationshipNodesAfterArrange)
                {
                    var Levels = BuildDeclutterLevels(Placements);
                    Result.RelationshipNodeDeclutterResult = RelationshipNodeDeclutterService.Declutter(View,
                                                                                                        Graph.RelationshipRepresentations,
                                                                                                        ScopeSymbols,
                                                                                                        Levels,
                                                                                                        Options.RelationshipNodeDeclutterOptions);
                    foreach (var Warning in Result.RelationshipNodeDeclutterResult.Warnings)
                        Result.AddWarning(Warning);
                }

                PlaceCrossBoundaryRelationshipBubbles(View, RelationshipInfos, Placements, Result.BoundaryRectangle, Options, Result);
                ExpandGroupRegionForInternalRelationshipBubbles(GroupRegion, RelationshipInfos, Options, Result);

                if (Options.RouteLinksAfterArrange)
                    Result.RoutingResult = RouteScopeLinks(Context, Graph, ScopeSymbols);

                ValidateSystemMapLayout(RelationshipInfos, Placements, GroupRegion, Options, Result);

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
                Console.WriteLine("Appearance: Arrange as System Map failed. Problem: {0}", Problem.Message);
                Console.WriteLine(Problem.ToString());

                if (LocalCommand && View.EditEngine.IsVariating)
                {
                    try
                    {
                        View.EditEngine.DiscardCommandVariation();
                        Console.WriteLine("Appearance: Arrange as System Map discarded its command variation after failure.");
                    }
                    catch (Exception DiscardProblem)
                    {
                        Console.WriteLine("Appearance: Could not discard failed System Map command variation. Problem: {0}", DiscardProblem.Message);
                        Console.WriteLine(DiscardProblem.ToString());
                    }
                }

                throw;
            }
        }

        private static List<VisualSymbol> GetScopeSymbols(LayoutSelectionContext Context, SystemMapLayoutOptions Options,
                                                          SystemMapLayoutResult Result)
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
                    Console.WriteLine("Appearance: System Map skipped concept {0}; reason={1}.", DescribeSymbol(Symbol), Warning);
                    continue;
                }

                Symbols.Add(Symbol);
            }

            return Symbols.OrderBy(GetSymbolSortKey).ToList();
        }

        private static SystemGraph BuildVisibleGraph(LayoutSelectionContext Context, IList<VisualSymbol> ScopeSymbols,
                                                     SystemMapLayoutResult Result)
        {
            var Graph = new SystemGraph();
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
                Graph.Adjacency[Symbol] = new HashSet<VisualSymbol>();
                Graph.Children[Symbol] = new HashSet<VisualSymbol>();
                Graph.Parents[Symbol] = new HashSet<VisualSymbol>();
            }

            foreach (var Representation in Context.VisibleRelationshipRepresentations.Where(Representation => Representation != null).Distinct())
            {
                Result.RelationshipsInspected++;
                var Relationship = Representation.RepresentedRelationship;
                if (Relationship == null || Relationship.Links == null)
                    continue;

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
                foreach (var Origin in Origins)
                    foreach (var Target in Targets)
                        if (Origin != Target)
                        {
                            Graph.Children[Origin].Add(Target);
                            Graph.Parents[Target].Add(Origin);
                            AddUndirected(Graph, Origin, Target);
                            RelationshipAdded = true;
                        }

                if (!RelationshipAdded && Endpoints.Count > 1)
                    for (int FirstIndex = 0; FirstIndex < Endpoints.Count; FirstIndex++)
                        for (int SecondIndex = FirstIndex + 1; SecondIndex < Endpoints.Count; SecondIndex++)
                            if (Endpoints[FirstIndex] != Endpoints[SecondIndex])
                            {
                                AddUndirected(Graph, Endpoints[FirstIndex], Endpoints[SecondIndex]);
                                RelationshipAdded = true;
                            }

                if (RelationshipAdded)
                    Graph.RelationshipRepresentations.Add(Representation);
            }

            Graph.RelationshipRepresentations = Graph.RelationshipRepresentations.Distinct().ToList();
            Result.RelationshipVisualsInScope = Graph.RelationshipRepresentations.Count;
            Console.WriteLine("Appearance: System Map graph built; relationships inspected={0}; relationship visuals in scope={1}.",
                              Result.RelationshipsInspected,
                              Result.RelationshipVisualsInScope);
            return Graph;
        }

        private static void AddUndirected(SystemGraph Graph, VisualSymbol First, VisualSymbol Second)
        {
            Graph.Adjacency[First].Add(Second);
            Graph.Adjacency[Second].Add(First);
        }

        private static VisualSymbol DetermineSystemRoot(LayoutSelectionContext Context, IList<VisualSymbol> ScopeSymbols,
                                                        SystemGraph Graph, SystemMapLayoutOptions Options,
                                                        out string Reason)
        {
            if (Options.ArrangeSelectedConceptsOnly && Context.SelectedConceptSymbols.Count == 1 &&
                ScopeSymbols.Contains(Context.SelectedConceptSymbols[0]))
            {
                Reason = "single selected concept";
                return Context.SelectedConceptSymbols[0];
            }

            var Candidates = Options.ArrangeSelectedConceptsOnly && Context.SelectedConceptSymbols.Count > 1
                             ? Context.SelectedConceptSymbols.Where(ScopeSymbols.Contains).ToList()
                             : ScopeSymbols.ToList();
            if (Candidates.Count < 1)
            {
                Reason = "no candidates";
                return null;
            }

            var TieCenter = DetermineTieCenter(Context, Candidates, Options);
            var OrderedCandidates = Options.ArrangeSelectedConceptsOnly && Context.SelectedConceptSymbols.Count > 1
                                    ? Candidates.OrderByDescending(Symbol => GetDegree(Graph, Symbol))
                                                .ThenByDescending(GetSystemKeywordScore)
                                                .ThenBy(Symbol => Distance(Symbol.BaseCenter, TieCenter))
                                                .ThenBy(GetSymbolSortKey)
                                    : Candidates.OrderByDescending(GetSystemKeywordScore)
                                                .ThenByDescending(Symbol => GetDegree(Graph, Symbol))
                                                .ThenBy(Symbol => Distance(Symbol.BaseCenter, TieCenter))
                                                .ThenBy(GetSymbolSortKey);
            var Best = OrderedCandidates.FirstOrDefault();
            Reason = Options.ArrangeSelectedConceptsOnly && Context.SelectedConceptSymbols.Count > 1
                     ? "selected concept with highest degree, system keyword score, center distance, deterministic name/techName/id"
                     : "system keyword score, highest degree, center distance, deterministic name/techName/id";

            if (Best != null && GetSystemKeywordScore(Best) < 1 && GetDegree(Graph, Best) < 1)
                Reason = "no obvious system root; deterministic first available concept";
            else
            if (Best != null && GetSystemKeywordScore(Best) < 1)
                Reason = "no system keyword; highest relationship degree";

            return Best;
        }

        private static IList<SystemConceptPlacement> ClassifyConcepts(IList<VisualSymbol> ScopeSymbols, VisualSymbol Root,
                                                                      SystemGraph Graph, LayoutSelectionContext Context,
                                                                      SystemMapLayoutOptions Options,
                                                                      SystemMapLayoutResult Result)
        {
            var Placements = new List<SystemConceptPlacement>();

            foreach (var Symbol in ScopeSymbols.OrderBy(GetSymbolSortKey))
            {
                var Placement = new SystemConceptPlacement();
                Placement.Symbol = Symbol;
                Placement.ExternalSide = ExternalSide.Left;

                if (Symbol == Root)
                {
                    Placement.Role = SystemConceptRole.SystemRoot;
                    Placement.Reason = "selected/detected system root";
                    Result.SystemRootCount = 1;
                }
                else
                if (HasExternalKeyword(Symbol) && !IsStronglyInternal(Symbol, Root, Graph))
                {
                    Placement.Role = SystemConceptRole.External;
                    Placement.ExternalSide = DetermineExternalSide(Symbol, Graph);
                    Placement.Reason = "external/environment keyword and not strongly internal";
                    Result.ExternalCount++;
                    if (Placement.ExternalSide == ExternalSide.Left)
                        Result.LeftExternalCount++;
                    else
                    if (Placement.ExternalSide == ExternalSide.Right)
                        Result.RightExternalCount++;
                    else
                        Result.TopBottomExternalCount++;
                }
                else
                if (Graph.Adjacency.ContainsKey(Root) && Graph.Adjacency[Root].Contains(Symbol))
                {
                    Placement.Role = SystemConceptRole.Internal;
                    Placement.Reason = "directly connected to system/root";
                    Result.InternalCount++;
                }
                else
                if (IsLeafConnectedToNonExternal(Symbol, Root, Graph))
                {
                    Placement.Role = SystemConceptRole.Internal;
                    Placement.Reason = "leaf connected to internal component";
                    Result.InternalCount++;
                }
                else
                {
                    Placement.Role = SystemConceptRole.Ambiguous;
                    Placement.Reason = "ambiguous; defaulted inside implicit system boundary";
                    Result.AmbiguousCount++;
                }

                Placements.Add(Placement);
                Console.WriteLine("Appearance: System Map classification: {0}; role={1}; side={2}; degree={3}; reason={4}.",
                                  DescribeSymbol(Symbol),
                                  FormatRole(Placement.Role),
                                  Placement.Role == SystemConceptRole.External ? Placement.ExternalSide.ToString() : "inside",
                                  GetDegree(Graph, Symbol),
                                  Placement.Reason);
            }

            Console.WriteLine("Appearance: System Map classification summary; root={0}; internal={1}; external={2}; ambiguous={3}; leftExternal={4}; rightExternal={5}; topBottomExternal={6}.",
                              Result.SystemRootCount,
                              Result.InternalCount,
                              Result.ExternalCount,
                              Result.AmbiguousCount,
                              Result.LeftExternalCount,
                              Result.RightExternalCount,
                              Result.TopBottomExternalCount);
            return Placements;
        }

        private static Dictionary<VisualSymbol, Point> ComputePositions(LayoutSelectionContext Context,
                                                                        IList<SystemConceptPlacement> Placements,
                                                                        VisualSymbol Root,
                                                                        SystemMapLayoutOptions Options,
                                                                        SystemMapLayoutResult Result)
        {
            var Positions = new Dictionary<VisualSymbol, Point>();
            var Center = DetermineLayoutCenter(Context, Placements.Select(Placement => Placement.Symbol).ToList(), Options);
            Positions[Root] = Center;

            var InternalSymbols = Placements.Where(Placement => Placement.Symbol != Root && Placement.IsInsideBoundary)
                                            .Select(Placement => Placement.Symbol)
                                            .OrderBy(GetSymbolSortKey)
                                            .ToList();
            var Columns = Options.InternalGridColumns > 0
                          ? Options.InternalGridColumns
                          : Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Math.Max(InternalSymbols.Count, 1))));
            var Rows = Math.Max(1, (int)Math.Ceiling(InternalSymbols.Count / (double)Columns));
            var GridWidth = Math.Max(0, Columns - 1) * Options.InternalSpacingX;
            var GridHeight = Math.Max(0, Rows - 1) * Options.InternalSpacingY;
            var GridStartX = Center.X - GridWidth / 2.0;
            var GridStartY = Center.Y + Options.InternalSpacingY;

            for (var Index = 0; Index < InternalSymbols.Count; Index++)
            {
                var Row = Index / Columns;
                var Column = Index % Columns;
                Positions[InternalSymbols[Index]] = new Point(GridStartX + Column * Options.InternalSpacingX,
                                                              GridStartY + Row * Options.InternalSpacingY);
            }

            var InsideBounds = ComputePlannedBounds(Positions.Where(Pair => Placements.First(Placement => Placement.Symbol == Pair.Key).IsInsideBoundary)
                                                             .ToDictionary(Pair => Pair.Key, Pair => Pair.Value));
            Result.BoundaryRectangle = InsideBounds;
            if (!Result.BoundaryRectangle.IsEmpty)
                Result.BoundaryRectangle.Inflate(Options.GroupRegionPadding, Options.GroupRegionPadding);

            PlaceExternalSymbols(Placements, Positions, Result.BoundaryRectangle, Options);

            foreach (var Pair in Positions.OrderBy(Pair => GetSymbolSortKey(Pair.Key)))
                Console.WriteLine("Appearance: System Map planned concept {0}; center=({1:0.##},{2:0.##}).",
                                  DescribeSymbol(Pair.Key), Pair.Value.X, Pair.Value.Y);

            return Positions;
        }

        private static void PlaceExternalSymbols(IList<SystemConceptPlacement> Placements,
                                                 IDictionary<VisualSymbol, Point> Positions,
                                                 Rect Boundary,
                                                 SystemMapLayoutOptions Options)
        {
            if (Boundary.IsEmpty)
                Boundary = ComputePlannedBounds(Positions);

            var LeftSymbols = Placements.Where(Placement => Placement.Role == SystemConceptRole.External &&
                                                           Placement.ExternalSide == ExternalSide.Left)
                                        .Select(Placement => Placement.Symbol)
                                        .OrderBy(GetSymbolSortKey)
                                        .ToList();
            var RightSymbols = Placements.Where(Placement => Placement.Role == SystemConceptRole.External &&
                                                            Placement.ExternalSide != ExternalSide.Left)
                                         .Select(Placement => Placement.Symbol)
                                         .OrderBy(GetSymbolSortKey)
                                         .ToList();

            PlaceVerticalStack(LeftSymbols, Boundary.Left - Options.ExternalOffsetX, Boundary.Top + Boundary.Height / 2.0,
                               Options.ExternalSpacingY, Positions);
            PlaceVerticalStack(RightSymbols, Boundary.Right + Options.ExternalOffsetX, Boundary.Top + Boundary.Height / 2.0,
                               Options.ExternalSpacingY, Positions);
        }

        private static void PlaceVerticalStack(IList<VisualSymbol> Symbols, double X, double CenterY, double SpacingY,
                                               IDictionary<VisualSymbol, Point> Positions)
        {
            if (Symbols.Count < 1)
                return;

            var TotalHeight = (Symbols.Count - 1) * SpacingY;
            var StartY = CenterY - TotalHeight / 2.0;
            for (var Index = 0; Index < Symbols.Count; Index++)
                Positions[Symbols[Index]] = new Point(X, StartY + Index * SpacingY);
        }

        private static void ApplyLayout(IDictionary<VisualSymbol, Point> Positions, SystemMapLayoutResult Result)
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
                Console.WriteLine("Appearance: System Map concept {0}; oldCenter=({1:0.##},{2:0.##}); newCenter=({3:0.##},{4:0.##}).",
                                  DescribeSymbol(Symbol), OldCenter.X, OldCenter.Y, NewCenter.X, NewCenter.Y);
            }
        }

        private static Dictionary<VisualSymbol, int> BuildDeclutterLevels(IEnumerable<SystemConceptPlacement> Placements)
        {
            return (Placements ?? Enumerable.Empty<SystemConceptPlacement>())
                   .Where(Placement => Placement != null && Placement.Symbol != null)
                   .ToDictionary(Placement => Placement.Symbol,
                                 Placement => Placement.Role == SystemConceptRole.External
                                              ? (Placement.ExternalSide == ExternalSide.Left ? 0 : 2)
                                              : 1);
        }

        private static Rect ComputeGroupRegionBoundaryFromFinalSymbols(IList<SystemConceptPlacement> Placements,
                                                                       SystemMapLayoutOptions Options)
        {
            var InternalBounds = (Placements ?? Enumerable.Empty<SystemConceptPlacement>())
                                 .Where(Placement => Placement != null && Placement.Symbol != null && Placement.IsInsideBoundary)
                                 .Select(Placement => Placement.Symbol.TotalArea)
                                 .Where(Rectangle => !Rectangle.IsEmpty)
                                 .ToList();
            if (InternalBounds.Count < 1)
                return Rect.Empty;

            var Boundary = InternalBounds[0];
            foreach (var Bounds in InternalBounds.Skip(1))
                Boundary.Union(Bounds);

            Console.WriteLine("Appearance: System Map group region internal final bounds before padding: {0}; padding=(left={1:0.##}, top={2:0.##}, right={3:0.##}, bottom={4:0.##}).",
                              LayoutBoundsNormalizer.FormatRect(Boundary),
                              Options.GroupRegionPaddingLeft,
                              Options.GroupRegionPaddingTop,
                              Options.GroupRegionPaddingRight,
                              Options.GroupRegionPaddingBottom);

            Boundary = Inflate(Boundary,
                               Options.GroupRegionPaddingLeft,
                               Options.GroupRegionPaddingTop,
                               Options.GroupRegionPaddingRight,
                               Options.GroupRegionPaddingBottom);
            return Boundary;
        }

        private static VisualComplement ApplyGroupRegionBoundary(View View, VisualSymbol Root, IList<SystemConceptPlacement> Placements,
                                                                 SystemMapLayoutOptions Options, SystemMapLayoutResult Result)
        {
            if (!Options.CreateGroupRegion)
            {
                Result.GroupRegionSkipped = true;
                Result.GroupRegionStatus = "skipped";
                Result.GroupRegionWarning = "CreateGroupRegion=false";
                Console.WriteLine("Appearance: System Map group region skipped; reason={0}; implicitBoundary={1}.",
                                  Result.GroupRegionWarning,
                                  LayoutBoundsNormalizer.FormatRect(Result.BoundaryRectangle));
                return null;
            }

            if (View == null || Root == null || Result.BoundaryRectangle.IsEmpty)
            {
                Result.GroupRegionSkipped = true;
                Result.GroupRegionStatus = "skipped";
                Result.GroupRegionWarning = "missing view, root symbol, or boundary rectangle";
                Result.AddWarning("System Map could not create a Group Region: " + Result.GroupRegionWarning + ".");
                Console.WriteLine("Appearance: System Map group region skipped; reason={0}.", Result.GroupRegionWarning);
                return null;
            }

            var Existing = Options.ReuseExistingGroupRegion
                           ? FindReusableGroupRegion(View, Root, Placements, Result.BoundaryRectangle)
                           : null;
            var WasCreated = false;

            if (Existing == null)
            {
                var Owner = Ownership.Create<View, VisualSymbol>(Root);
                Existing = new VisualComplement(Domain.ComplementDefGroupRegion, Owner, GetRectCenter(Result.BoundaryRectangle),
                                                Result.BoundaryRectangle.Width);
                Root.AddComplement(Existing);
                View.PutComplement(Existing);
                WasCreated = true;
                Console.WriteLine("Appearance: System Map group region created; root={0}; boundary={1}.",
                                  DescribeSymbol(Root),
                                  LayoutBoundsNormalizer.FormatRect(Result.BoundaryRectangle));
            }
            else
                Console.WriteLine("Appearance: System Map group region reused; root={0}; existingBounds={1}; boundary={2}.",
                                  DescribeSymbol(Root),
                                  LayoutBoundsNormalizer.FormatRect(Existing.TotalArea),
                                  LayoutBoundsNormalizer.FormatRect(Result.BoundaryRectangle));

            var OldBounds = Existing.TotalArea;
            Existing.ResizeTo(Result.BoundaryRectangle.Width, Result.BoundaryRectangle.Height);
            Existing.MoveTo(Result.BoundaryRectangle.Left + Result.BoundaryRectangle.Width / 2.0,
                            Result.BoundaryRectangle.Top + Result.BoundaryRectangle.Height / 2.0,
                            true);
            View.PutComplement(Existing);

            var ZOrderBefore = Existing.ZOrder;
            if (Options.GroupRegionSendToBack)
                View.SendBackwards(Existing, true);
            var ZOrderAfter = Existing.ZOrder;

            if (WasCreated)
            {
                Result.GroupRegionCreated = true;
                Result.GroupRegionStatus = "created";
            }
            else
            {
                Result.GroupRegionUpdated = true;
                Result.GroupRegionStatus = "updated";
            }

            Console.WriteLine("Appearance: System Map group region {0}; oldBounds={1}; newBounds={2}; zOrderBefore={3}; zOrderAfter={4}; sendToBack={5}; labelMode={6}.",
                              Result.GroupRegionStatus,
                              LayoutBoundsNormalizer.FormatRect(OldBounds),
                              LayoutBoundsNormalizer.FormatRect(Existing.TotalArea),
                              ZOrderBefore,
                              ZOrderAfter,
                              Options.GroupRegionSendToBack ? "true" : "false",
                              Options.GroupRegionLabelMode.ToStringAlways("none"));
            ValidateAndExpandGroupRegion(Existing, Placements, Options, Result);
            return Existing;
        }

        private static void ValidateAndExpandGroupRegion(VisualComplement GroupRegion,
                                                         IList<SystemConceptPlacement> Placements,
                                                         SystemMapLayoutOptions Options,
                                                         SystemMapLayoutResult Result)
        {
            if (GroupRegion == null)
                return;

            var Bounds = GroupRegion.TotalArea;
            var Required = Bounds;
            foreach (var Placement in Placements.Where(Placement => Placement != null && Placement.Symbol != null && Placement.IsInsideBoundary))
            {
                var SymbolBounds = Placement.Symbol.TotalArea;
                var DistanceLeft = SymbolBounds.Left - Bounds.Left;
                var DistanceTop = SymbolBounds.Top - Bounds.Top;
                var DistanceRight = Bounds.Right - SymbolBounds.Right;
                var DistanceBottom = Bounds.Bottom - SymbolBounds.Bottom;
                var Inside = Bounds.Contains(SymbolBounds.TopLeft) && Bounds.Contains(SymbolBounds.BottomRight);

                Console.WriteLine("Appearance: System Map containment check: {0}; inside={1}; distanceLeft={2:0.##}; distanceTop={3:0.##}; distanceRight={4:0.##}; distanceBottom={5:0.##}.",
                                  DescribeSymbol(Placement.Symbol),
                                  Inside ? "true" : "false",
                                  DistanceLeft,
                                  DistanceTop,
                                  DistanceRight,
                                  DistanceBottom);

                var SymbolRequired = Inflate(SymbolBounds,
                                             Options.GroupRegionPaddingLeft,
                                             Options.GroupRegionPaddingTop,
                                             Options.GroupRegionPaddingRight,
                                             Options.GroupRegionPaddingBottom);
                Required.Union(SymbolRequired);
            }

            if (Required.Left < Bounds.Left - GeometryTolerance ||
                Required.Top < Bounds.Top - GeometryTolerance ||
                Required.Right > Bounds.Right + GeometryTolerance ||
                Required.Bottom > Bounds.Bottom + GeometryTolerance)
            {
                var ExpandLeft = Math.Max(0, Bounds.Left - Required.Left);
                var ExpandTop = Math.Max(0, Bounds.Top - Required.Top);
                var ExpandRight = Math.Max(0, Required.Right - Bounds.Right);
                var ExpandBottom = Math.Max(0, Required.Bottom - Bounds.Bottom);

                GroupRegion.ResizeTo(Required.Width, Required.Height);
                GroupRegion.MoveTo(Required.Left + Required.Width / 2.0,
                                   Required.Top + Required.Height / 2.0,
                                   true);
                Result.BoundaryRectangle = Required;
                Result.GroupRegionContainmentExpansions++;

                Console.WriteLine("Appearance: System Map expanded Group Region for containment; left={0:0.##}; top={1:0.##}; right={2:0.##}; bottom={3:0.##}; newBounds={4}.",
                                  ExpandLeft,
                                  ExpandTop,
                                  ExpandRight,
                                  ExpandBottom,
                                  LayoutBoundsNormalizer.FormatRect(Required));
            }
        }

        private static VisualComplement FindReusableGroupRegion(View View, VisualSymbol Root,
                                                                 IList<SystemConceptPlacement> Placements,
                                                                 Rect Boundary)
        {
            var SelectedRegion = View.SelectedObjects
                                     .OfType<VisualComplement>()
                                     .FirstOrDefault(Complement => Complement.IsComplementGroupRegion &&
                                                                   View.ViewChildren.Any(Child => Child != null && Child.Key == Complement));
            if (SelectedRegion != null)
            {
                Console.WriteLine("Appearance: System Map selected group region found for reuse; bounds={0}.",
                                  LayoutBoundsNormalizer.FormatRect(SelectedRegion.TotalArea));
                return SelectedRegion;
            }

            var RootRegion = Root.AttachedComplements
                                 .FirstOrDefault(Complement => Complement.IsComplementGroupRegion &&
                                                               View.ViewChildren.Any(Child => Child != null && Child.Key == Complement));
            if (RootRegion != null)
            {
                Console.WriteLine("Appearance: System Map root-attached group region found for reuse; bounds={0}.",
                                  LayoutBoundsNormalizer.FormatRect(RootRegion.TotalArea));
                return RootRegion;
            }

            var InsideSymbols = Placements.Where(Placement => Placement.IsInsideBoundary)
                                          .Select(Placement => Placement.Symbol)
                                          .Where(Symbol => Symbol != null)
                                          .ToList();
            var RegionScores = View.ViewChildren
                                   .Where(Child => Child != null && Child.Key is VisualComplement)
                                   .Select(Child => (VisualComplement)Child.Key)
                                   .Where(Complement => Complement.IsComplementGroupRegion)
                                   .Select(Complement => new
                                   {
                                       Complement = Complement,
                                       Score = InsideSymbols.Count(Symbol => Complement.TotalArea.Contains(Symbol.BaseCenter)),
                                       Distance = Distance(GetRectCenter(Complement.TotalArea), GetRectCenter(Boundary))
                                   })
                                   .Where(Item => Item.Score > 0)
                                   .OrderByDescending(Item => Item.Score)
                                   .ThenBy(Item => Item.Distance)
                                   .FirstOrDefault();
            if (RegionScores != null && RegionScores.Score >= Math.Max(1, InsideSymbols.Count / 2))
            {
                Console.WriteLine("Appearance: System Map containing group region found for reuse; containedInternalSymbols={0}; bounds={1}.",
                                  RegionScores.Score,
                                  LayoutBoundsNormalizer.FormatRect(RegionScores.Complement.TotalArea));
                return RegionScores.Complement;
            }

            Console.WriteLine("Appearance: System Map no reusable group region found.");
            return null;
        }

        private static IList<SystemRelationshipInfo> ClassifySystemRelationships(SystemGraph Graph,
                                                                                 IList<SystemConceptPlacement> Placements,
                                                                                 SystemMapLayoutResult Result)
        {
            var PlacementByIdea = (Placements ?? Enumerable.Empty<SystemConceptPlacement>())
                                  .Where(Placement => Placement != null &&
                                                      Placement.Symbol != null &&
                                                      Placement.Symbol.OwnerRepresentation != null &&
                                                      Placement.Symbol.OwnerRepresentation.RepresentedIdea != null)
                                  .GroupBy(Placement => Placement.Symbol.OwnerRepresentation.RepresentedIdea)
                                  .ToDictionary(Group => Group.Key, Group => Group.First());
            var Infos = new List<SystemRelationshipInfo>();

            foreach (var Representation in (Graph == null ? Enumerable.Empty<RelationshipVisualRepresentation>() : Graph.RelationshipRepresentations)
                                           .Where(Representation => Representation != null)
                                           .Distinct())
            {
                var Relationship = Representation.RepresentedRelationship;
                if (Relationship == null || Relationship.Links == null)
                    continue;

                var Info = new SystemRelationshipInfo();
                Info.Representation = Representation;
                Info.Relationship = Relationship;
                Info.Origins = Relationship.Links
                                           .Where(Link => Link != null &&
                                                          Link.AssociatedIdea != null &&
                                                          Link.RoleDefinitor != null &&
                                                          Link.RoleDefinitor.RoleType == ERoleType.Origin &&
                                                          PlacementByIdea.ContainsKey(Link.AssociatedIdea))
                                           .Select(Link => PlacementByIdea[Link.AssociatedIdea])
                                           .Distinct()
                                           .ToList();
                Info.Targets = Relationship.Links
                                           .Where(Link => Link != null &&
                                                          Link.AssociatedIdea != null &&
                                                          Link.RoleDefinitor != null &&
                                                          Link.RoleDefinitor.RoleType == ERoleType.Target &&
                                                          PlacementByIdea.ContainsKey(Link.AssociatedIdea))
                                           .Select(Link => PlacementByIdea[Link.AssociatedIdea])
                                           .Distinct()
                                           .ToList();
                Info.Endpoints = Relationship.Links
                                             .Where(Link => Link != null &&
                                                            Link.AssociatedIdea != null &&
                                                            PlacementByIdea.ContainsKey(Link.AssociatedIdea))
                                             .Select(Link => PlacementByIdea[Link.AssociatedIdea])
                                             .Distinct()
                                             .ToList();

                ClassifySystemRelationship(Info);
                if (Info.IsCrossBoundary)
                    Result.CrossBoundaryRelationships++;

                Infos.Add(Info);
                Console.WriteLine("Appearance: System Map relationship classification: {0}; type={1}; side={2}; origins={3}; targets={4}; reason={5}.",
                                  DescribeIdea(Info.Relationship),
                                  FormatRelationshipKind(Info.Kind),
                                  Info.Side,
                                  DescribeRelationshipEndpoints(Info.Origins),
                                  DescribeRelationshipEndpoints(Info.Targets),
                                  Info.Reason);
            }

            Console.WriteLine("Appearance: System Map relationship classification summary; relationships={0}; crossBoundary={1}.",
                              Infos.Count,
                              Result.CrossBoundaryRelationships);
            return Infos;
        }

        private static void ClassifySystemRelationship(SystemRelationshipInfo Info)
        {
            var Origins = Info.Origins.Count > 0 ? Info.Origins : Info.Endpoints;
            var Targets = Info.Targets.Count > 0 ? Info.Targets : Info.Endpoints;
            var OriginExternal = Origins.Any(Placement => Placement.Role == SystemConceptRole.External);
            var TargetExternal = Targets.Any(Placement => Placement.Role == SystemConceptRole.External);
            var OriginInside = Origins.Any(Placement => Placement.IsInsideBoundary);
            var TargetInside = Targets.Any(Placement => Placement.IsInsideBoundary);
            var HasRoot = Info.Endpoints.Any(Placement => Placement.Role == SystemConceptRole.SystemRoot);
            var HasInternal = Info.Endpoints.Any(Placement => Placement.Role == SystemConceptRole.Internal || Placement.Role == SystemConceptRole.Ambiguous);

            if (OriginExternal && TargetInside)
            {
                Info.Kind = SystemRelationshipKind.ExternalToInternal;
                Info.Side = GetDominantExternalSide(Origins);
                Info.Reason = "directed external origin to internal target";
            }
            else
            if (OriginInside && TargetExternal)
            {
                Info.Kind = SystemRelationshipKind.InternalToExternal;
                Info.Side = GetDominantExternalSide(Targets);
                Info.Reason = "directed internal origin to external target";
            }
            else
            if (OriginExternal && TargetExternal)
            {
                Info.Kind = SystemRelationshipKind.ExternalExternal;
                Info.Side = GetDominantExternalSide(Info.Endpoints);
                Info.Reason = "all directed endpoints are external";
            }
            else
            if (HasRoot && HasInternal)
            {
                Info.Kind = SystemRelationshipKind.RootInternal;
                Info.Side = ExternalSide.Top;
                Info.Reason = "root/internal relationship";
            }
            else
            if (OriginInside && TargetInside)
            {
                Info.Kind = SystemRelationshipKind.InternalInternal;
                Info.Side = ExternalSide.Top;
                Info.Reason = "all directed endpoints are inside the system boundary";
            }
            else
            if (Info.Endpoints.Any(Placement => Placement.Role == SystemConceptRole.External) &&
                Info.Endpoints.Any(Placement => Placement.IsInsideBoundary))
            {
                Info.Kind = SystemRelationshipKind.Ambiguous;
                Info.Side = GetDominantExternalSide(Info.Endpoints);
                Info.Reason = "mixed external/internal endpoints without clear direction";
            }
            else
            {
                Info.Kind = SystemRelationshipKind.Ambiguous;
                Info.Side = ExternalSide.Top;
                Info.Reason = "insufficient endpoint role information";
            }
        }

        private static ExternalSide GetDominantExternalSide(IEnumerable<SystemConceptPlacement> Placements)
        {
            var ExternalPlacements = (Placements ?? Enumerable.Empty<SystemConceptPlacement>())
                                     .Where(Placement => Placement.Role == SystemConceptRole.External)
                                     .ToList();
            if (ExternalPlacements.Any(Placement => Placement.ExternalSide == ExternalSide.Left))
                return ExternalSide.Left;

            if (ExternalPlacements.Any(Placement => Placement.ExternalSide == ExternalSide.Right))
                return ExternalSide.Right;

            return ExternalPlacements.Select(Placement => Placement.ExternalSide).FirstOrDefault();
        }

        private static void PlaceCrossBoundaryRelationshipBubbles(View View,
                                                                  IList<SystemRelationshipInfo> RelationshipInfos,
                                                                  IList<SystemConceptPlacement> Placements,
                                                                  Rect Boundary,
                                                                  SystemMapLayoutOptions Options,
                                                                  SystemMapLayoutResult Result)
        {
            if (View == null || Boundary.IsEmpty)
                return;

            var CrossBoundary = (RelationshipInfos ?? Enumerable.Empty<SystemRelationshipInfo>())
                                .Where(Info => Info != null && Info.IsCrossBoundary &&
                                               Info.Representation != null &&
                                               Info.Representation.MainSymbol != null &&
                                               !Info.Representation.MainSymbol.IsHidden &&
                                               Info.Representation.MainSymbol.IsRelatedVisible)
                                .ToList();
            if (CrossBoundary.Count < 1)
                return;

            var ConceptObstacles = BuildSystemMapConceptObstacles(Placements, Options);

            foreach (var Group in CrossBoundary.GroupBy(Info => Info.Side)
                                               .OrderBy(Group => Group.Key.ToString()))
            {
                var SideInfos = Group.ToList();
                var Lane = BuildCrossBoundaryLane(Group.Key, SideInfos, Boundary, Options);
                Console.WriteLine("Appearance: System Map cross-boundary side lane: side={0}; lane={1}; externalBounds={2}; relationships={3}.",
                                  Group.Key,
                                  LayoutBoundsNormalizer.FormatRect(Lane.Bounds),
                                  LayoutBoundsNormalizer.FormatRect(Lane.ExternalBounds),
                                  SideInfos.Count);

                var Ordered = Group.Select(Info => new
                                    {
                                        Info = Info,
                                        Preferred = GetCrossBoundaryPreferredPosition(Info, Boundary, Lane, Options)
                                    })
                                   .OrderBy(Item => GetExternalEndpoint(Item.Info) == null ? Item.Preferred.Y : GetExternalEndpoint(Item.Info).Symbol.BaseCenter.Y)
                                   .ThenBy(Item => Item.Info.SortKey)
                                   .ToList();

                foreach (var Item in Ordered)
                {
                    var Info = Item.Info;
                    var Symbol = Info.Representation.MainSymbol;
                    var OldCenter = Symbol.BaseCenter;
                    var RelationshipObstacles = BuildSystemMapRelationshipBubbleObstacles(RelationshipInfos, Info, Options);
                    var Candidate = ChooseCrossBoundaryBubbleCandidate(Info, Lane, Item.Preferred, ConceptObstacles,
                                                                       RelationshipObstacles, Options, Result);

                    if (Candidate == null)
                    {
                        var Warning = "System Map could not find a non-overlapping side-lane position for cross-boundary relationship '" +
                                      Info.Relationship.TechName.ToStringAlways() + "'.";
                        Result.CrossBoundaryBubblePlacementWarnings++;
                        Result.AddWarning(Warning);
                        Console.WriteLine("Appearance: {0}", Warning);
                        continue;
                    }

                    var NewCenter = Candidate.Center;

                    if (Distance(OldCenter, NewCenter) > GeometryTolerance)
                    {
                        Symbol.MoveTo(NewCenter.X, NewCenter.Y, true);
                        Info.Representation.Render();
                        Result.CrossBoundaryRelationshipBubblesMoved++;
                    }
                    else
                        Symbol.RenderElement();

                    Console.WriteLine("Appearance: System Map cross-boundary bubble: {0}; type={1}; side={2}; external={3}; internal={4}; preferred=({5:0.##},{6:0.##}); chosen=({7:0.##},{8:0.##}); candidate={9}; score={10:0.##}; lane={11}; oldCenter=({12:0.##},{13:0.##}); final insideSideLane={14}.",
                                      DescribeIdea(Info.Relationship),
                                      FormatRelationshipKind(Info.Kind),
                                      Info.Side,
                                      DescribePlacement(GetExternalEndpoint(Info)),
                                      DescribePlacement(GetInternalEndpoint(Info)),
                                      Item.Preferred.X,
                                      Item.Preferred.Y,
                                      NewCenter.X,
                                      NewCenter.Y,
                                      Candidate.Label,
                                      Candidate.Score,
                                      LayoutBoundsNormalizer.FormatRect(Lane.Bounds),
                                      OldCenter.X,
                                      OldCenter.Y,
                                      IsPointInsideRect(NewCenter, Lane.Bounds) ? "true" : "false");
                }
            }
        }

        private static IList<SystemMapObstacle> BuildSystemMapConceptObstacles(IList<SystemConceptPlacement> Placements,
                                                                               SystemMapLayoutOptions Options)
        {
            return (Placements ?? Enumerable.Empty<SystemConceptPlacement>())
                   .Where(Placement => Placement != null && Placement.Symbol != null && !Placement.Symbol.TotalArea.IsEmpty)
                   .Select(Placement => new SystemMapObstacle
                   {
                       Bounds = Inflate(Placement.Symbol.TotalArea, Options.CrossBoundaryBubbleObstaclePadding),
                       Description = "concept " + GetIdeaName(Placement.Symbol),
                       IsConcept = true
                   })
                   .ToList();
        }

        private static IList<SystemMapObstacle> BuildSystemMapRelationshipBubbleObstacles(IList<SystemRelationshipInfo> RelationshipInfos,
                                                                                          SystemRelationshipInfo Current,
                                                                                          SystemMapLayoutOptions Options)
        {
            return (RelationshipInfos ?? Enumerable.Empty<SystemRelationshipInfo>())
                   .Where(Info => Info != null && Info != Current &&
                                  Info.Representation != null &&
                                  Info.Representation.MainSymbol != null &&
                                  !Info.Representation.MainSymbol.IsHidden &&
                                  Info.Representation.MainSymbol.IsRelatedVisible &&
                                  !Info.Representation.MainSymbol.TotalArea.IsEmpty)
                   .Select(Info => new SystemMapObstacle
                   {
                       Bounds = Inflate(Info.Representation.MainSymbol.TotalArea, Options.CrossBoundaryBubbleObstaclePadding),
                       Description = "relationship bubble " + Info.Relationship.TechName.ToStringAlways(),
                       IsRelationshipBubble = true
                   })
                   .ToList();
        }

        private static CrossBoundaryLane BuildCrossBoundaryLane(ExternalSide Side, IList<SystemRelationshipInfo> Infos,
                                                                Rect Boundary, SystemMapLayoutOptions Options)
        {
            var ExternalBounds = Rect.Empty;
            foreach (var Info in Infos ?? Enumerable.Empty<SystemRelationshipInfo>())
            {
                var External = GetExternalEndpoint(Info);
                if (External == null || External.Symbol == null || External.Symbol.TotalArea.IsEmpty)
                    continue;

                if (ExternalBounds.IsEmpty)
                    ExternalBounds = External.Symbol.TotalArea;
                else
                    ExternalBounds.Union(External.Symbol.TotalArea);
            }

            if (ExternalBounds.IsEmpty)
                ExternalBounds = Boundary;

            var Top = Math.Min(Boundary.Top, ExternalBounds.Top) - Options.CrossBoundaryBubbleSpacingY;
            var Bottom = Math.Max(Boundary.Bottom, ExternalBounds.Bottom) + Options.CrossBoundaryBubbleSpacingY;
            var Left = Boundary.Left - Options.ExternalOffsetX;
            var Right = Boundary.Right + Options.ExternalOffsetX;

            if (Side == ExternalSide.Left)
            {
                Left = ExternalBounds.Right + Options.CrossBoundaryBubbleLanePaddingX;
                Right = Boundary.Left - Options.CrossBoundaryBubbleLanePaddingX;
                if (Right <= Left + GeometryTolerance)
                {
                    Left = ExternalBounds.Left - Options.ExternalOffsetX / 2.0;
                    Right = Boundary.Left - Options.CrossBoundaryBubbleLanePaddingX;
                }
            }
            else
            if (Side == ExternalSide.Right)
            {
                Left = Boundary.Right + Options.CrossBoundaryBubbleLanePaddingX;
                Right = ExternalBounds.Left - Options.CrossBoundaryBubbleLanePaddingX;
                if (Right <= Left + GeometryTolerance)
                {
                    Left = Boundary.Right + Options.CrossBoundaryBubbleLanePaddingX;
                    Right = ExternalBounds.Right + Options.ExternalOffsetX / 2.0;
                }
            }

            if (Right <= Left + GeometryTolerance)
                Right = Left + Math.Max(Options.ExternalOffsetX, 160.0);

            return new CrossBoundaryLane
            {
                Side = Side,
                Bounds = new Rect(Left, Top, Right - Left, Bottom - Top),
                ExternalBounds = ExternalBounds,
                BoundaryX = Side == ExternalSide.Left ? Boundary.Left : Boundary.Right
            };
        }

        private static CrossBoundaryBubbleCandidate ChooseCrossBoundaryBubbleCandidate(SystemRelationshipInfo Info,
                                                                                       CrossBoundaryLane Lane,
                                                                                       Point Preferred,
                                                                                       IList<SystemMapObstacle> ConceptObstacles,
                                                                                       IList<SystemMapObstacle> RelationshipObstacles,
                                                                                       SystemMapLayoutOptions Options,
                                                                                       SystemMapLayoutResult Result)
        {
            var Symbol = Info.Representation.MainSymbol;
            var Candidates = GenerateCrossBoundaryBubbleCandidates(Info, Symbol, Lane, Preferred, Options)
                             .Select(Candidate => EvaluateCrossBoundaryBubbleCandidate(Symbol, Candidate.Center,
                                                                                       Candidate.Label, Lane, Preferred,
                                                                                       ConceptObstacles,
                                                                                       RelationshipObstacles,
                                                                                       Options))
                             .ToList();

            foreach (var Candidate in Candidates.Where(Candidate => !Candidate.IsValid))
            {
                Result.CrossBoundaryBubbleCandidatesRejected++;
                Console.WriteLine("Appearance: System Map cross-boundary bubble rejected candidate: {0}; relationship={1}; center=({2:0.##},{3:0.##}); reason={4}.",
                                  Candidate.Label,
                                  Info.Relationship.TechName.ToStringAlways(),
                                  Candidate.Center.X,
                                  Candidate.Center.Y,
                                  String.Join("; ", Candidate.RejectionReasons.Take(3).ToArray()));
            }

            var Best = Candidates.Where(Candidate => Candidate.IsValid)
                                 .OrderBy(Candidate => Candidate.Score)
                                 .FirstOrDefault();
            if (Best != null)
                return Best;

            Best = Candidates.Where(Candidate => Candidate.ConceptOverlaps == 0)
                             .OrderBy(Candidate => Candidate.RelationshipOverlaps)
                             .ThenBy(Candidate => Candidate.Score)
                             .FirstOrDefault();
            if (Best != null)
            {
                var Warning = "System Map placed cross-boundary relationship '" +
                              Info.Relationship.TechName.ToStringAlways() +
                              "' in a side lane that still overlaps another relationship bubble; no concept-overlapping candidate was used.";
                Result.CrossBoundaryBubblePlacementWarnings++;
                Result.AddWarning(Warning);
                Console.WriteLine("Appearance: {0}", Warning);
                return Best;
            }

            return null;
        }

        private static IEnumerable<CrossBoundaryBubbleCandidate> GenerateCrossBoundaryBubbleCandidates(SystemRelationshipInfo Info,
                                                                                                      VisualSymbol Symbol,
                                                                                                      CrossBoundaryLane Lane,
                                                                                                      Point Preferred,
                                                                                                      SystemMapLayoutOptions Options)
        {
            var Centers = new List<CrossBoundaryBubbleCandidate>();
            var HalfWidth = Math.Max(Symbol.BaseWidth / 2.0, 20.0);
            var HalfHeight = Math.Max(Symbol.BaseHeight / 2.0, 10.0);
            var External = GetExternalEndpoint(Info);
            var ExternalBounds = External == null || External.Symbol == null ? Lane.ExternalBounds : External.Symbol.TotalArea;
            var ExternalCenter = External == null || External.Symbol == null ? Preferred : External.Symbol.BaseCenter;
            var LaneCenterX = Lane.Bounds.Left + Lane.Bounds.Width / 2.0;
            var BoundaryNearX = Lane.Side == ExternalSide.Left
                                ? Lane.BoundaryX - Options.CrossBoundaryBubbleLanePaddingX - HalfWidth
                                : Lane.BoundaryX + Options.CrossBoundaryBubbleLanePaddingX + HalfWidth;
            var ExternalNearX = Lane.Side == ExternalSide.Left
                                ? ExternalBounds.Right + Options.CrossBoundaryBubbleLanePaddingX + HalfWidth
                                : ExternalBounds.Left - Options.CrossBoundaryBubbleLanePaddingX - HalfWidth;
            var OutwardX = Lane.Side == ExternalSide.Left
                           ? ExternalBounds.Left - Options.CrossBoundaryBubbleLanePaddingX - HalfWidth
                           : ExternalBounds.Right + Options.CrossBoundaryBubbleLanePaddingX + HalfWidth;

            var XCandidates = new List<Tuple<double, string>>
            {
                Tuple.Create(Preferred.X, "preferred-midpoint"),
                Tuple.Create(LaneCenterX, "lane-center"),
                Tuple.Create(BoundaryNearX, "near-boundary"),
                Tuple.Create(ExternalNearX, "near-external"),
                Tuple.Create(OutwardX, "outward-slot")
            };

            var MaxSlots = Math.Max(2, Options.CrossBoundaryBubbleMaxOffsetSlots);
            var StepY = Math.Max(Options.CrossBoundaryBubbleCandidateStepY, HalfHeight * 1.5);
            var MinY = Math.Min(Lane.Bounds.Top + HalfHeight, Preferred.Y - StepY * MaxSlots);
            var MaxY = Math.Max(Lane.Bounds.Bottom - HalfHeight, Preferred.Y + StepY * MaxSlots);
            var YCandidates = new List<Tuple<double, string>>
            {
                Tuple.Create(Preferred.Y, "preferred-y"),
                Tuple.Create(ExternalCenter.Y, "external-y")
            };

            for (var Index = 1; Index <= MaxSlots; Index++)
            {
                YCandidates.Add(Tuple.Create(Preferred.Y - StepY * Index, "above-" + Index.ToString(CultureInfo.InvariantCulture)));
                YCandidates.Add(Tuple.Create(Preferred.Y + StepY * Index, "below-" + Index.ToString(CultureInfo.InvariantCulture)));
                YCandidates.Add(Tuple.Create(ExternalCenter.Y - StepY * Index, "above-external-" + Index.ToString(CultureInfo.InvariantCulture)));
                YCandidates.Add(Tuple.Create(ExternalCenter.Y + StepY * Index, "below-external-" + Index.ToString(CultureInfo.InvariantCulture)));
            }

            foreach (var XCandidate in XCandidates)
                foreach (var YCandidate in YCandidates)
                {
                    var Center = new Point(XCandidate.Item1, YCandidate.Item1.EnforceRange(MinY, MaxY));
                    if (Centers.Any(Item => Distance(Item.Center, Center) < GeometryTolerance))
                        continue;

                    Centers.Add(new CrossBoundaryBubbleCandidate
                    {
                        Center = Center,
                        Label = XCandidate.Item2 + "/" + YCandidate.Item2
                    });
                }

            return Centers;
        }

        private static CrossBoundaryBubbleCandidate EvaluateCrossBoundaryBubbleCandidate(VisualSymbol Symbol,
                                                                                         Point Center,
                                                                                         string Label,
                                                                                         CrossBoundaryLane Lane,
                                                                                         Point Preferred,
                                                                                         IList<SystemMapObstacle> ConceptObstacles,
                                                                                         IList<SystemMapObstacle> RelationshipObstacles,
                                                                                         SystemMapLayoutOptions Options)
        {
            var Candidate = new CrossBoundaryBubbleCandidate();
            Candidate.Center = Center;
            Candidate.Label = Label;

            var Bounds = GetSymbolBoundsAt(Symbol, Center);
            var InflatedBounds = Inflate(Bounds, Options.CrossBoundaryBubbleObstaclePadding);

            foreach (var Obstacle in ConceptObstacles ?? Enumerable.Empty<SystemMapObstacle>())
                if (InflatedBounds.IntersectsWith(Obstacle.Bounds))
                {
                    Candidate.ConceptOverlaps++;
                    Candidate.RejectionReasons.Add("overlaps " + Obstacle.Description);
                }

            foreach (var Obstacle in RelationshipObstacles ?? Enumerable.Empty<SystemMapObstacle>())
                if (InflatedBounds.IntersectsWith(Obstacle.Bounds))
                {
                    Candidate.RelationshipOverlaps++;
                    Candidate.RejectionReasons.Add("overlaps " + Obstacle.Description);
                }

            Candidate.IsValid = Candidate.ConceptOverlaps == 0 && Candidate.RelationshipOverlaps == 0;
            Candidate.Score = Distance(Center, Preferred);
            if (!IsPointInsideRect(Center, Lane.Bounds))
                Candidate.Score += 120.0;
            Candidate.Score += Math.Abs(Center.X - (Lane.Bounds.Left + Lane.Bounds.Width / 2.0)) * 0.15;
            Candidate.Score += Candidate.ConceptOverlaps * 100000.0 + Candidate.RelationshipOverlaps * 20000.0;

            return Candidate;
        }

        private static void ExpandGroupRegionForInternalRelationshipBubbles(VisualComplement GroupRegion,
                                                                            IList<SystemRelationshipInfo> RelationshipInfos,
                                                                            SystemMapLayoutOptions Options,
                                                                            SystemMapLayoutResult Result)
        {
            if (GroupRegion == null)
                return;

            var Required = GroupRegion.TotalArea;
            var InternalBubbles = (RelationshipInfos ?? Enumerable.Empty<SystemRelationshipInfo>())
                                  .Where(Info => Info != null &&
                                                 (Info.Kind == SystemRelationshipKind.InternalInternal ||
                                                  Info.Kind == SystemRelationshipKind.RootInternal) &&
                                                 Info.Representation != null &&
                                                 Info.Representation.MainSymbol != null &&
                                                 !Info.Representation.MainSymbol.IsHidden &&
                                                 Info.Representation.MainSymbol.IsRelatedVisible)
                                  .Select(Info => new
                                  {
                                      Info = Info,
                                      Bounds = Info.Representation.MainSymbol.TotalArea
                                  })
                                  .Where(Item => !Item.Bounds.IsEmpty &&
                                                 GroupRegion.TotalArea.Contains(GetRectCenter(Item.Bounds)))
                                  .ToList();
            if (InternalBubbles.Count < 1)
                return;

            var BubblePadding = Math.Max(24.0, Math.Min(48.0, Options.GroupRegionPadding / 3.0));
            foreach (var Bubble in InternalBubbles)
            {
                var BubbleRequired = Inflate(Bubble.Bounds, BubblePadding);
                Required.Union(BubbleRequired);
                Console.WriteLine("Appearance: System Map internal relationship bubble containment: {0}; bounds={1}; requiredPadding={2:0.##}.",
                                  DescribeIdea(Bubble.Info.Relationship),
                                  LayoutBoundsNormalizer.FormatRect(Bubble.Bounds),
                                  BubblePadding);
            }

            var Current = GroupRegion.TotalArea;
            if (Required.Left < Current.Left - GeometryTolerance ||
                Required.Top < Current.Top - GeometryTolerance ||
                Required.Right > Current.Right + GeometryTolerance ||
                Required.Bottom > Current.Bottom + GeometryTolerance)
            {
                GroupRegion.ResizeTo(Required.Width, Required.Height);
                GroupRegion.MoveTo(Required.Left + Required.Width / 2.0,
                                   Required.Top + Required.Height / 2.0,
                                   true);
                Result.BoundaryRectangle = Required;
                Result.GroupRegionContainmentExpansions++;
                Result.GroupRegionUpdated = true;

                Console.WriteLine("Appearance: System Map expanded Group Region for internal relationship bubbles; oldBounds={0}; newBounds={1}; bubbles={2}.",
                                  LayoutBoundsNormalizer.FormatRect(Current),
                                  LayoutBoundsNormalizer.FormatRect(Required),
                                  InternalBubbles.Count);
            }
        }

        private static Point GetCrossBoundaryPreferredPosition(SystemRelationshipInfo Info, Rect Boundary,
                                                               CrossBoundaryLane Lane,
                                                               SystemMapLayoutOptions Options)
        {
            var External = GetExternalEndpoint(Info);
            var Internal = GetInternalEndpoint(Info);
            var ExternalCenter = External == null || External.Symbol == null ? GetRectCenter(Lane.ExternalBounds) : External.Symbol.BaseCenter;
            var InternalCenter = Internal == null || Internal.Symbol == null ? GetRectCenter(Boundary) : Internal.Symbol.BaseCenter;
            var PreferredY = ((ExternalCenter.Y + InternalCenter.Y) / 2.0).EnforceRange(Lane.Bounds.Top, Lane.Bounds.Bottom);
            var PreferredX = Lane.Bounds.Left + Lane.Bounds.Width / 2.0;

            if (Info.Side == ExternalSide.Left)
            {
                var Left = External == null || External.Symbol == null ? Lane.Bounds.Left : External.Symbol.TotalArea.Right + Options.CrossBoundaryBubbleLanePaddingX;
                var Right = Boundary.Left - Options.CrossBoundaryBubbleLanePaddingX;
                if (Right > Left)
                    PreferredX = (Left + Right) / 2.0;
            }
            else
            if (Info.Side == ExternalSide.Right)
            {
                var Left = Boundary.Right + Options.CrossBoundaryBubbleLanePaddingX;
                var Right = External == null || External.Symbol == null ? Lane.Bounds.Right : External.Symbol.TotalArea.Left - Options.CrossBoundaryBubbleLanePaddingX;
                if (Right > Left)
                    PreferredX = (Left + Right) / 2.0;
            }

            return new Point(PreferredX, PreferredY);
        }

        private static SystemConceptPlacement GetExternalEndpoint(SystemRelationshipInfo Info)
        {
            if (Info == null)
                return null;

            var Source = Info.Kind == SystemRelationshipKind.ExternalToInternal
                         ? Info.Origins
                         : Info.Kind == SystemRelationshipKind.InternalToExternal
                           ? Info.Targets
                           : Info.Endpoints;

            return (Source ?? Enumerable.Empty<SystemConceptPlacement>())
                   .Where(Placement => Placement != null &&
                                       Placement.Role == SystemConceptRole.External &&
                                       (Placement.ExternalSide == Info.Side ||
                                        Info.Side == ExternalSide.Top ||
                                        Info.Side == ExternalSide.Bottom))
                   .OrderBy(Placement => GetSymbolSortKey(Placement.Symbol))
                   .FirstOrDefault()
                   ??
                   (Info.Endpoints ?? Enumerable.Empty<SystemConceptPlacement>())
                   .Where(Placement => Placement != null && Placement.Role == SystemConceptRole.External)
                   .OrderBy(Placement => GetSymbolSortKey(Placement.Symbol))
                   .FirstOrDefault();
        }

        private static SystemConceptPlacement GetInternalEndpoint(SystemRelationshipInfo Info)
        {
            if (Info == null)
                return null;

            var Source = Info.Kind == SystemRelationshipKind.ExternalToInternal
                         ? Info.Targets
                         : Info.Kind == SystemRelationshipKind.InternalToExternal
                           ? Info.Origins
                           : Info.Endpoints;

            return (Source ?? Enumerable.Empty<SystemConceptPlacement>())
                   .Where(Placement => Placement != null && Placement.IsInsideBoundary)
                   .OrderBy(Placement => GetSymbolSortKey(Placement.Symbol))
                   .FirstOrDefault()
                   ??
                   (Info.Endpoints ?? Enumerable.Empty<SystemConceptPlacement>())
                   .Where(Placement => Placement != null && Placement.IsInsideBoundary)
                   .OrderBy(Placement => GetSymbolSortKey(Placement.Symbol))
                   .FirstOrDefault();
        }

        private static LinkObstacleRoutingResult RouteScopeLinks(LayoutSelectionContext Context, SystemGraph Graph,
                                                                 IEnumerable<VisualSymbol> MovedSymbols)
        {
            var Moved = new HashSet<VisualSymbol>((MovedSymbols ?? Enumerable.Empty<VisualSymbol>()).Where(Symbol => Symbol != null));
            var Incident = Context.VisibleRelationshipRepresentations
                                  .Where(Representation => Representation != null &&
                                         Representation.VisualConnectors.Any(Connector => Connector != null &&
                                             (Moved.Contains(Connector.OriginSymbol) || Moved.Contains(Connector.TargetSymbol))));
            var Connectors = Graph.RelationshipRepresentations.Concat(Incident)
                                  .Distinct()
                                  .Where(Representation => Representation != null)
                                  .SelectMany(Representation => Representation.VisualConnectors)
                                  .Where(Connector => Connector != null)
                                  .Cast<VisualObject>()
                                  .Distinct()
                                  .ToList();

            if (Connectors.Count < 1)
            {
                Console.WriteLine("Appearance: System Map post-route skipped; no relationship connectors are in scope.");
                return null;
            }

            var RouteContext = LayoutSelectionContext.FromViewSelection(Context.Engine, Context.ActiveView, Connectors);
            var RouteOptions = new LinkObstacleRoutingOptions();
            RouteOptions.RouteSelectedConnectorsOnly = true;
            RouteOptions.PreserveExistingValidRoutes = false;
            RouteOptions.RouteIntent = RelationshipRouteIntent.Layout;
            RouteOptions.DirtyReason = "System Map moved endpoint symbols and relationship hubs";
            RouteOptions.Profile = RelationshipRoutingProfile.SystemMap;
            RouteOptions.IncludeRelationshipCentralSymbolsAsObstacles = true;
            return RelationshipRoutingCoordinator.Route(RouteContext, RouteOptions);
        }

        private static void ValidateSystemMapLayout(IList<SystemRelationshipInfo> RelationshipInfos,
                                                    IList<SystemConceptPlacement> Placements,
                                                    VisualComplement GroupRegion,
                                                    SystemMapLayoutOptions Options,
                                                    SystemMapLayoutResult Result)
        {
            var RegionBounds = GroupRegion == null ? Rect.Empty : GroupRegion.TotalArea;
            var ConceptBounds = (Placements ?? Enumerable.Empty<SystemConceptPlacement>())
                                .Where(Placement => Placement != null && Placement.Symbol != null)
                                .Select(Placement => new { Placement = Placement, Bounds = Placement.Symbol.TotalArea })
                                .Where(Item => !Item.Bounds.IsEmpty)
                                .ToList();
            var BubbleBounds = (RelationshipInfos ?? Enumerable.Empty<SystemRelationshipInfo>())
                               .Where(Info => Info != null &&
                                              Info.Representation != null &&
                                              Info.Representation.MainSymbol != null &&
                                              !Info.Representation.MainSymbol.IsHidden &&
                                              Info.Representation.MainSymbol.IsRelatedVisible)
                               .Select(Info => new { Info = Info, Bounds = Info.Representation.MainSymbol.TotalArea })
                               .Where(Item => !Item.Bounds.IsEmpty)
                               .ToList();

            if (!RegionBounds.IsEmpty)
            {
                foreach (var Concept in ConceptBounds.Where(Item => Item.Placement.IsInsideBoundary))
                {
                    var DistanceLeft = Concept.Bounds.Left - RegionBounds.Left;
                    var DistanceTop = Concept.Bounds.Top - RegionBounds.Top;
                    var DistanceRight = RegionBounds.Right - Concept.Bounds.Right;
                    var DistanceBottom = RegionBounds.Bottom - Concept.Bounds.Bottom;
                    if (DistanceLeft < Options.GroupRegionPaddingLeft - GeometryTolerance ||
                        DistanceTop < Options.GroupRegionPaddingTop - GeometryTolerance ||
                        DistanceRight < Options.GroupRegionPaddingRight - GeometryTolerance ||
                        DistanceBottom < Options.GroupRegionPaddingBottom - GeometryTolerance)
                        AddValidationWarning(Result, "System Map validation warning: internal concept '" +
                                             GetIdeaName(Concept.Placement.Symbol) +
                                             "' is too close to the Group Region boundary; distances left/top/right/bottom=" +
                                             FormatDistances(DistanceLeft, DistanceTop, DistanceRight, DistanceBottom) + ".");
                }

                foreach (var Concept in ConceptBounds.Where(Item => Item.Placement.Role == SystemConceptRole.External))
                    if (RegionBounds.IntersectsWith(Concept.Bounds))
                        AddValidationWarning(Result, "System Map validation warning: external concept '" +
                                             GetIdeaName(Concept.Placement.Symbol) +
                                             "' overlaps the Group Region boundary.");
            }

            for (var FirstIndex = 0; FirstIndex < BubbleBounds.Count; FirstIndex++)
                for (var SecondIndex = FirstIndex + 1; SecondIndex < BubbleBounds.Count; SecondIndex++)
                    if (Inflate(BubbleBounds[FirstIndex].Bounds, 8.0).IntersectsWith(Inflate(BubbleBounds[SecondIndex].Bounds, 8.0)))
                        AddValidationWarning(Result, "System Map validation warning: relationship bubble '" +
                                             BubbleBounds[FirstIndex].Info.Relationship.TechName.ToStringAlways() +
                                             "' overlaps '" +
                                             BubbleBounds[SecondIndex].Info.Relationship.TechName.ToStringAlways() + "'.");

            foreach (var Bubble in BubbleBounds)
                foreach (var Concept in ConceptBounds)
                    if (Bubble.Info.Endpoints.Any(Endpoint => Endpoint.Symbol == Concept.Placement.Symbol))
                        continue;
                    else
                    if (Inflate(Bubble.Bounds, 8.0).IntersectsWith(Inflate(Concept.Bounds, 8.0)))
                        AddValidationWarning(Result, "System Map validation warning: relationship bubble '" +
                                             Bubble.Info.Relationship.TechName.ToStringAlways() +
                                             "' overlaps concept '" +
                                             GetIdeaName(Concept.Placement.Symbol) + "'.");

            Console.WriteLine("Appearance: System Map validation summary; relationshipBubbles={0}; concepts={1}; warnings={2}.",
                              BubbleBounds.Count,
                              ConceptBounds.Count,
                              Result.SystemMapValidationWarnings);
        }

        private static void AddValidationWarning(SystemMapLayoutResult Result, string Warning)
        {
            Result.SystemMapValidationWarnings++;
            Result.AddWarning(Warning);
            Console.WriteLine(Warning);
        }

        private static void RevealArrangedBounds(View View, SystemMapLayoutResult Result)
        {
            if (View == null || View.Presenter == null || Result == null || Result.BoundsAfterNormalization.IsEmpty)
            {
                if (Result != null)
                    Result.RevealAction = "none";

                Console.WriteLine("Appearance: System Map reveal arranged bounds: view={0}; bounds={1}; action=none.",
                                  DescribeView(View),
                                  Result == null ? "<none>" : LayoutBoundsNormalizer.FormatRect(Result.BoundsAfterNormalization));
                return;
            }

            var Bounds = Result.BoundsAfterNormalization;
            Bounds.Inflate(Result.BoundsAfterNormalization.Width * 0.08 + 40.0,
                           Result.BoundsAfterNormalization.Height * 0.08 + 40.0);
            View.Presenter.BringIntoView(Bounds);
            Result.RevealAction = "BringIntoView";
            Console.WriteLine("Appearance: System Map reveal arranged bounds: view={0}; bounds={1}; action=BringIntoView.",
                              DescribeView(View), LayoutBoundsNormalizer.FormatRect(Bounds));
        }

        private static bool HasExternalKeyword(VisualSymbol Symbol)
        {
            var Text = GetIdeaText(Symbol);
            return ContainsAny(Text, "external", "client", "user", "customer", "supplier", "host", "agent",
                               "device", "environment", "endpoint", "source", "network");
        }

        private static int GetSystemKeywordScore(VisualSymbol Symbol)
        {
            var Text = GetIdeaText(Symbol);
            var Score = 0;
            if (ContainsAny(Text, "system"))
                Score += 40;
            if (ContainsAny(Text, "manager", "platform", "application", "service", "hub", "root", "control"))
                Score += 20;

            return Score;
        }

        private static bool ContainsAny(string Text, params string[] Keywords)
        {
            return Keywords.Any(Keyword => Text.IndexOf(Keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string GetIdeaText(VisualSymbol Symbol)
        {
            if (Symbol == null || Symbol.OwnerRepresentation == null || Symbol.OwnerRepresentation.RepresentedIdea == null)
                return String.Empty;

            var Idea = Symbol.OwnerRepresentation.RepresentedIdea;
            return Idea.Name.ToStringAlways() + " " + Idea.TechName.ToStringAlways();
        }

        private static bool IsStronglyInternal(VisualSymbol Symbol, VisualSymbol Root, SystemGraph Graph)
        {
            return Symbol != null && Root != null &&
                   Graph.Adjacency.ContainsKey(Root) &&
                   Graph.Adjacency[Root].Contains(Symbol) &&
                   GetSystemKeywordScore(Symbol) > 0;
        }

        private static bool IsLeafConnectedToNonExternal(VisualSymbol Symbol, VisualSymbol Root, SystemGraph Graph)
        {
            if (Symbol == null || Root == null || !Graph.Adjacency.ContainsKey(Symbol))
                return false;

            return Graph.Adjacency[Symbol].Count <= 1 &&
                   Graph.Adjacency[Symbol].Any(Neighbor => Neighbor != Root && !HasExternalKeyword(Neighbor));
        }

        private static ExternalSide DetermineExternalSide(VisualSymbol Symbol, SystemGraph Graph)
        {
            var Text = GetIdeaText(Symbol);
            if (ContainsAny(Text, "user", "client", "customer", "supplier", "source"))
                return ExternalSide.Left;

            if (ContainsAny(Text, "endpoint", "host", "agent", "device", "environment", "network"))
                return ExternalSide.Right;

            if (Graph.Children.ContainsKey(Symbol) && Graph.Children[Symbol].Any(Neighbor => !HasExternalKeyword(Neighbor)))
                return ExternalSide.Left;

            if (Graph.Parents.ContainsKey(Symbol) && Graph.Parents[Symbol].Any(Neighbor => !HasExternalKeyword(Neighbor)))
                return ExternalSide.Right;

            return ExternalSide.Left;
        }

        private static int GetDegree(SystemGraph Graph, VisualSymbol Symbol)
        {
            return Graph != null && Symbol != null && Graph.Adjacency.ContainsKey(Symbol) ? Graph.Adjacency[Symbol].Count : 0;
        }

        private static Rect ComputePlannedBounds(IDictionary<VisualSymbol, Point> Positions)
        {
            if (Positions == null || Positions.Count < 1)
                return Rect.Empty;

            Rect? Bounds = null;
            foreach (var Pair in Positions)
            {
                var Symbol = Pair.Key;
                var Center = Pair.Value;
                var Rect = new Rect(Center.X - Symbol.BaseWidth / 2.0,
                                    Center.Y - Symbol.BaseHeight / 2.0,
                                    Symbol.BaseWidth,
                                    Symbol.BaseHeight);
                Bounds = Bounds.HasValue ? Rect.Union(Bounds.Value, Rect) : Rect;
            }

            return Bounds ?? Rect.Empty;
        }

        private static Rect Inflate(Rect Bounds, double Padding)
        {
            if (Bounds.IsEmpty)
                return Bounds;

            Bounds.Inflate(Padding, Padding);
            return Bounds;
        }

        private static Rect Inflate(Rect Bounds, double Left, double Top, double Right, double Bottom)
        {
            if (Bounds.IsEmpty)
                return Bounds;

            return new Rect(Bounds.Left - Left,
                            Bounds.Top - Top,
                            Bounds.Width + Left + Right,
                            Bounds.Height + Top + Bottom);
        }

        private static Rect Translate(Rect Rect, Vector Translation)
        {
            if (Rect.IsEmpty)
                return Rect;

            Rect.Offset(Translation);
            return Rect;
        }

        private static Rect GetSymbolBoundsAt(VisualSymbol Symbol, Point Center)
        {
            if (Symbol == null)
                return Rect.Empty;

            var Width = Math.Max(Symbol.BaseWidth, Symbol.TotalArea.IsEmpty ? 0.0 : Symbol.TotalArea.Width);
            var Height = Math.Max(Symbol.BaseHeight, Symbol.TotalArea.IsEmpty ? 0.0 : Symbol.TotalArea.Height);
            if (Width <= GeometryTolerance)
                Width = Math.Max(Symbol.BaseWidth, 40.0);
            if (Height <= GeometryTolerance)
                Height = Math.Max(Symbol.BaseHeight, 24.0);

            return new Rect(Center.X - Width / 2.0,
                            Center.Y - Height / 2.0,
                            Width,
                            Height);
        }

        private static bool IsPointInsideRect(Point Point, Rect Rect)
        {
            return !Rect.IsEmpty &&
                   Point.X >= Rect.Left - GeometryTolerance &&
                   Point.X <= Rect.Right + GeometryTolerance &&
                   Point.Y >= Rect.Top - GeometryTolerance &&
                   Point.Y <= Rect.Bottom + GeometryTolerance;
        }

        private static Point GetRectCenter(Rect Rect)
        {
            return Rect.IsEmpty
                   ? new Point(0, 0)
                   : new Point(Rect.Left + Rect.Width / 2.0, Rect.Top + Rect.Height / 2.0);
        }

        private static Point DetermineLayoutCenter(LayoutSelectionContext Context, IList<VisualSymbol> Symbols,
                                                   SystemMapLayoutOptions Options)
        {
            if (IsUsablePoint(Context.CurrentViewportCenter))
                return new Point(Math.Max(Context.CurrentViewportCenter.X, Options.CanvasPadding + Options.ExternalOffsetX + Options.BoundaryPadding),
                                 Math.Max(Context.CurrentViewportCenter.Y, Options.CanvasPadding + Options.BoundaryPadding));

            var Center = DetermineClusterCenter(Symbols);
            return new Point(Math.Max(Center.X, Options.CanvasPadding + Options.ExternalOffsetX + Options.BoundaryPadding),
                             Math.Max(Center.Y, Options.CanvasPadding + Options.BoundaryPadding));
        }

        private static Point DetermineTieCenter(LayoutSelectionContext Context, IList<VisualSymbol> Symbols,
                                                SystemMapLayoutOptions Options)
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

        private static string GetIdeaName(VisualSymbol Symbol)
        {
            if (Symbol == null || Symbol.OwnerRepresentation == null || Symbol.OwnerRepresentation.RepresentedIdea == null)
                return "<no concept>";

            return Symbol.OwnerRepresentation.RepresentedIdea.Name.ToStringAlways();
        }

        private static string FormatRole(SystemConceptRole Role)
        {
            switch (Role)
            {
                case SystemConceptRole.SystemRoot:
                    return "system/root";
                case SystemConceptRole.Internal:
                    return "internal";
                case SystemConceptRole.External:
                    return "external";
                default:
                    return "ambiguous-internal";
            }
        }

        private static string FormatRelationshipKind(SystemRelationshipKind Kind)
        {
            switch (Kind)
            {
                case SystemRelationshipKind.InternalInternal:
                    return "internal-internal";
                case SystemRelationshipKind.ExternalToInternal:
                    return "external-to-internal";
                case SystemRelationshipKind.InternalToExternal:
                    return "internal-to-external";
                case SystemRelationshipKind.ExternalExternal:
                    return "external-external";
                case SystemRelationshipKind.RootInternal:
                    return "root/internal";
                default:
                    return "ambiguous";
            }
        }

        private static string DescribeRelationshipEndpoints(IEnumerable<SystemConceptPlacement> Placements)
        {
            var Names = (Placements ?? Enumerable.Empty<SystemConceptPlacement>())
                        .Where(Placement => Placement != null && Placement.Symbol != null)
                        .Select(Placement => GetIdeaName(Placement.Symbol))
                        .Distinct()
                        .ToList();
            return Names.Count < 1 ? "<none>" : String.Join(", ", Names.ToArray());
        }

        private static string DescribePlacement(SystemConceptPlacement Placement)
        {
            return Placement == null || Placement.Symbol == null
                   ? "<none>"
                   : GetIdeaName(Placement.Symbol) + " (" + FormatRole(Placement.Role) + ")";
        }

        private static string FormatDistances(double Left, double Top, double Right, double Bottom)
        {
            return String.Format(CultureInfo.InvariantCulture,
                                 "left={0:0.##}, top={1:0.##}, right={2:0.##}, bottom={3:0.##}",
                                 Left, Top, Right, Bottom);
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

        private static void LogSummary(SystemMapLayoutResult Result)
        {
            Console.WriteLine("Appearance: Arrange as System Map completed; concepts inspected={0}; arranged={1}; moved={2}; skipped={3}; relationships inspected={4}; relationship visuals in scope={5}; root={6}; internal={7}; external={8}; ambiguous={9}; links routed={10}; route skipped={11}; groupRegion={12}; warnings={13}.",
                              Result.ConceptsInspected,
                              Result.ConceptsArranged,
                              Result.ConceptsMoved,
                              Result.ConceptsSkipped,
                              Result.RelationshipsInspected,
                              Result.RelationshipVisualsInScope,
                              Result.SystemRootCount,
                              Result.InternalCount,
                              Result.ExternalCount,
                              Result.AmbiguousCount,
                              Result.LinksRouted,
                              Result.RoutingResult == null ? 0 : Result.RoutingResult.Skipped,
                              Result.GroupRegionStatus.ToStringAlways("skipped"),
                              Result.Warnings.Count);

            Console.WriteLine("Appearance: System Map refinement summary; groupRegionExpansions={0}; crossBoundaryRelationships={1}; crossBoundaryBubblesMoved={2}; bubbleCandidatesRejected={3}; bubblePlacementWarnings={4}; validationWarnings={5}.",
                              Result.GroupRegionContainmentExpansions,
                              Result.CrossBoundaryRelationships,
                              Result.CrossBoundaryRelationshipBubblesMoved,
                              Result.CrossBoundaryBubbleCandidatesRejected,
                              Result.CrossBoundaryBubblePlacementWarnings,
                              Result.SystemMapValidationWarnings);

            if (Result.AutoFitResult != null)
                Console.WriteLine("Appearance: System Map auto-fit summary; inspected={0}; fitted={1}; skipped={2}.",
                                  Result.AutoFitResult.SymbolsInspected,
                                  Result.AutoFitResult.SymbolsFitted,
                                  Result.AutoFitResult.SymbolsSkipped);

            if (Result.RelationshipNodeDeclutterResult != null)
                Console.WriteLine("Appearance: System Map relationship node declutter summary; inspected={0}; moved={1}; skipped={2}; warnings={3}.",
                                  Result.RelationshipNodeDeclutterResult.RelationshipSymbolsInspected,
                                  Result.RelationshipNodeDeclutterResult.RelationshipSymbolsMoved,
                                  Result.RelationshipNodeDeclutterResult.RelationshipSymbolsSkipped,
                                  Result.RelationshipNodeDeclutterResult.Warnings.Count);

            if (Result.RoutingResult != null)
                Console.WriteLine("Appearance: System Map route summary; connector routes inspected={0}; relationship routes inspected={1}; routed={2}; dogleg routed={3}; straightened={4}; unchanged={5}; skipped={6}.",
                                  Result.RoutingResult.ConnectorRoutesInspected,
                                  Result.RoutingResult.RelationshipRoutesInspected,
                                  Result.RoutingResult.Routed,
                                  Result.RoutingResult.DoglegRouted,
                                  Result.RoutingResult.Straightened,
                                  Result.RoutingResult.Unchanged,
                                  Result.RoutingResult.Skipped);

            Console.WriteLine("Appearance: System Map bounds summary; boundary={0}; beforeNormalize={1}; dx={2:0.##}; dy={3:0.##}; final={4}; withinSafeCanvas={5}; reveal={6}.",
                              LayoutBoundsNormalizer.FormatRect(Result.BoundaryRectangle),
                              LayoutBoundsNormalizer.FormatRect(Result.BoundsBeforeNormalization),
                              Result.NormalizationDelta.X,
                              Result.NormalizationDelta.Y,
                              LayoutBoundsNormalizer.FormatRect(Result.BoundsAfterNormalization),
                              Result.FinalBoundsWithinSafeCanvas ? "true" : "false",
                              Result.RevealAction.ToStringAlways("none"));

            foreach (var Warning in Result.Warnings)
                Console.WriteLine("Appearance System Map warning: {0}", Warning);
        }
    }
}
