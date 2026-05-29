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
    /// Local overlap-resolution pass for visible relationship central symbols.
    /// </summary>
    public static class RelationshipNodeDeclutterService
    {
        private const double GeometryTolerance = 0.5;

        private class RelationshipNodeItem
        {
            public RelationshipVisualRepresentation Representation;
            public Relationship Relationship;
            public VisualSymbol Symbol;
            public List<VisualSymbol> OriginSymbols;
            public List<VisualSymbol> TargetSymbols;
            public int SourceLevel;
            public int TargetLevel;
            public int BandStartLevel;
            public int BandEndLevel;
            public Point OriginCenter;
            public Point TargetCenter;
            public Point PreferredCenter;
            public Point NewCenter;
            public Rect EndpointCorridor;
            public string EdgePriority;
            public bool IsLocalShortEdge;
            public double BandTop;
            public double BandBottom;

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

        private class PlacementCandidate
        {
            public Point Center;
            public string Label;
            public double Score;
            public double Displacement;
            public bool InsideCorridor;
            public string RejectionReason;
        }

        public static RelationshipNodeDeclutterResult Declutter(View View,
                                                                IEnumerable<RelationshipVisualRepresentation> RelationshipRepresentations,
                                                                IEnumerable<VisualSymbol> ScopeConceptSymbols,
                                                                IDictionary<VisualSymbol, int> ConceptLevels,
                                                                RelationshipNodeDeclutterOptions Options)
        {
            Options = Options ?? new RelationshipNodeDeclutterOptions();
            var Result = new RelationshipNodeDeclutterResult();
            var ConceptSymbols = (ScopeConceptSymbols ?? Enumerable.Empty<VisualSymbol>())
                                 .Where(Symbol => Symbol != null)
                                 .Distinct()
                                 .ToList();
            var Levels = ConceptLevels ?? new Dictionary<VisualSymbol, int>();
            var ConceptBounds = ConceptSymbols.Select(Symbol => Symbol.TotalArea)
                                              .Where(Rectangle => !Rectangle.IsEmpty)
                                              .Select(Rectangle => Inflate(Rectangle, Options.ConceptAvoidancePadding))
                                              .ToList();
            var Items = BuildItems(RelationshipRepresentations, ConceptSymbols, Levels, Options, Result);

            Console.WriteLine("Appearance: Relationship node declutter starting; view={0}; relationship symbols inspected={1}; candidates={2}; options bandPaddingY={3:0.##}, nodeSpacingX={4:0.##}, maxVerticalJitter={5:0.##}, bubblePadding={6:0.##}, conceptPadding={7:0.##}, corridorPadding=({8:0.##},{9:0.##}), maxPreferredDisplacement={10:0.##}, hardMaxDisplacement={11:0.##}, maxGlobalPasses={12}, avoidConceptBounds={13}.",
                              DescribeView(View),
                              Result.RelationshipSymbolsInspected,
                              Items.Count,
                              Options.RelationshipBandPaddingY,
                              Options.RelationshipNodeSpacingX,
                              Options.MaxVerticalJitter,
                              Options.RelationshipBubblePadding,
                              Options.ConceptAvoidancePadding,
                              Options.CorridorPaddingX,
                              Options.CorridorPaddingY,
                              Options.MaxPreferredDisplacement,
                              Options.HardMaxDisplacement,
                              Options.MaxGlobalDeclutterPasses,
                              Options.AvoidConceptBounds ? "true" : "false");

            Result.InitialOverlapCount = CountRelationshipOverlaps(Items, Options.RelationshipBubblePadding);
            Result.OverlapGroupsDetected = CountOverlapGroups(Items.Select(Item => GetInflatedBounds(Item, Item.PreferredCenter, Options.RelationshipBubblePadding)).ToList());
            Console.WriteLine("Appearance: relationship node declutter initial overlaps={0}; overlapGroups={1}.",
                              Result.InitialOverlapCount,
                              Result.OverlapGroupsDetected);

            foreach (var Group in Items.GroupBy(GetBandKey)
                                       .OrderBy(Group => Group.Key))
            {
                var GroupItems = Group.OrderBy(Item => Item.PreferredCenter.X)
                                      .ThenBy(Item => Item.SortKey)
                                      .ToList();
                Console.WriteLine("Appearance: relationship node declutter band={0}; symbols={1}; unorderedLevelBand=true.",
                                  Group.Key, GroupItems.Count);
                PlaceBand(GroupItems, ConceptBounds, Options, Result);
            }

            LogRelationshipOverlaps("after band declutter", Items, Options);
            RunGlobalDeclutter(Items, ConceptBounds, Options, Result);
            CorrectCorridorViolations(Items, ConceptBounds, Options, Result);
            ValidateFinalPlacement(Items, ConceptBounds, Options, Result);

            foreach (var Item in Items.OrderBy(Item => Item.SortKey))
                ApplyMove(Item, Result);

            Console.WriteLine("Appearance: Relationship node declutter completed; inspected={0}; moved={1}; skipped={2}; initialOverlaps={3}; overlapGroups={4}; globalPasses={5}; globalMoves={6}; corridorCorrections={7}; corridorViolations={8}; finalBubbleOverlaps={9}; finalConceptOverlaps={10}; warnings={11}.",
                              Result.RelationshipSymbolsInspected,
                              Result.RelationshipSymbolsMoved,
                              Result.RelationshipSymbolsSkipped,
                              Result.InitialOverlapCount,
                              Result.OverlapGroupsDetected,
                              Result.GlobalDeclutterPasses,
                              Result.GlobalDeclutterMoves,
                              Result.CorridorCorrections,
                              Result.CorridorViolations,
                              Result.FinalOverlapCount,
                              Result.FinalConceptOverlapCount,
                              Result.Warnings.Count);
            foreach (var Warning in Result.Warnings)
                Console.WriteLine("Appearance relationship node declutter warning: {0}", Warning);

            return Result;
        }

        private static List<RelationshipNodeItem> BuildItems(IEnumerable<RelationshipVisualRepresentation> RelationshipRepresentations,
                                                             IList<VisualSymbol> ScopeConceptSymbols,
                                                             IDictionary<VisualSymbol, int> Levels,
                                                             RelationshipNodeDeclutterOptions Options,
                                                             RelationshipNodeDeclutterResult Result)
        {
            var SymbolByIdea = ScopeConceptSymbols.Select(Symbol => new
                                  {
                                      Symbol = Symbol,
                                      Idea = Symbol.OwnerRepresentation == null ? null : Symbol.OwnerRepresentation.RepresentedIdea
                                  })
                                  .Where(Item => Item.Idea != null)
                                  .GroupBy(Item => Item.Idea)
                                  .ToDictionary(Group => Group.Key, Group => Group.First().Symbol);
            var Items = new List<RelationshipNodeItem>();

            foreach (var Representation in (RelationshipRepresentations ?? Enumerable.Empty<RelationshipVisualRepresentation>())
                                           .Where(Representation => Representation != null)
                                           .Distinct())
            {
                Result.RelationshipSymbolsInspected++;

                var Relationship = Representation.RepresentedRelationship;
                var Symbol = Representation.MainSymbol;
                if (Relationship == null || Symbol == null)
                {
                    Skip(Result, "relationship representation without relationship or main symbol");
                    continue;
                }

                if (Options.IncludeOnlyVisibleRelationshipSymbols &&
                    (Symbol.IsHidden || !Symbol.IsRelatedVisible))
                {
                    Skip(Result, "relationship '" + Relationship.TechName.ToStringAlways() + "' central symbol is hidden");
                    continue;
                }

                if (Relationship.Links == null)
                {
                    Skip(Result, "relationship '" + Relationship.TechName.ToStringAlways() + "' has no links");
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

                if (Origins.Count < 1 || Targets.Count < 1)
                {
                    Skip(Result, "relationship '" + Relationship.TechName.ToStringAlways() +
                                 "' does not have both origin and target concepts in the arranged scope");
                    continue;
                }

                if (Origins.Any(Origin => !Levels.ContainsKey(Origin)) ||
                    Targets.Any(Target => !Levels.ContainsKey(Target)))
                {
                    Skip(Result, "relationship '" + Relationship.TechName.ToStringAlways() +
                                 "' has endpoints without hierarchy levels");
                    continue;
                }

                var Item = new RelationshipNodeItem();
                Item.Representation = Representation;
                Item.Relationship = Relationship;
                Item.Symbol = Symbol;
                Item.OriginSymbols = Origins;
                Item.TargetSymbols = Targets;
                Item.SourceLevel = Origins.Min(Origin => Levels[Origin]);
                Item.TargetLevel = Targets.Min(Target => Levels[Target]);
                Item.BandStartLevel = Math.Min(Item.SourceLevel, Item.TargetLevel);
                Item.BandEndLevel = Math.Max(Item.SourceLevel, Item.TargetLevel);
                Item.OriginCenter = AveragePoint(Origins.Select(OriginSymbol => OriginSymbol.BaseCenter));
                Item.TargetCenter = AveragePoint(Targets.Select(TargetSymbol => TargetSymbol.BaseCenter));
                Item.PreferredCenter = GetMidpoint(Item.OriginCenter, Item.TargetCenter);
                Item.EndpointCorridor = GetEndpointCorridor(Origins, Targets, Options);
                Item.IsLocalShortEdge = Distance(Item.OriginCenter, Item.TargetCenter) <= Options.ShortEdgeMaxDistance;
                Item.EdgePriority = GetEdgePriorityLabel(Item);
                SetBand(Item, Options);
                Item.NewCenter = ClampToBand(Item, Item.PreferredCenter);
                Items.Add(Item);
            }

            return Items;
        }

        private static void PlaceBand(IList<RelationshipNodeItem> Items, IList<Rect> ConceptBounds,
                                      RelationshipNodeDeclutterOptions Options,
                                      RelationshipNodeDeclutterResult Result)
        {
            var PlacedBounds = new List<Rect>();
            var LastRight = Double.NegativeInfinity;

            foreach (var Item in Items)
            {
                var Center = ClampToBand(Item, Item.PreferredCenter);
                var Rect = GetInflatedBounds(Item, Center, Options.RelationshipBubblePadding);

                if (Rect.Left < LastRight + Options.RelationshipNodeSpacingX)
                {
                    var Shift = LastRight + Options.RelationshipNodeSpacingX - Rect.Left;
                    Center = new Point(Center.X + Shift, Center.Y);
                    Rect = GetInflatedBounds(Item, Center, Options.RelationshipBubblePadding);
                }

                Center = ApplyVerticalJitter(Item, Center, PlacedBounds, ConceptBounds, Options, Result);
                Rect = GetInflatedBounds(Item, Center, Options.RelationshipBubblePadding);

                while (PlacedBounds.Any(Bounds => Bounds.IntersectsWith(Rect)))
                {
                    Center = new Point(Center.X + Item.Symbol.BaseWidth + Options.RelationshipNodeSpacingX, Center.Y);
                    Center = ApplyVerticalJitter(Item, Center, PlacedBounds, ConceptBounds, Options, Result);
                    Rect = GetInflatedBounds(Item, Center, Options.RelationshipBubblePadding);
                }

                Item.NewCenter = Center;
                PlacedBounds.Add(Rect);
                LastRight = Math.Max(LastRight, Rect.Right);
            }
        }

        private static Point ApplyVerticalJitter(RelationshipNodeItem Item, Point Center, IList<Rect> PlacedBounds,
                                                 IList<Rect> ConceptBounds, RelationshipNodeDeclutterOptions Options,
                                                 RelationshipNodeDeclutterResult Result)
        {
            if (!Options.AvoidConceptBounds)
                return Center;

            var Step = Math.Max(8.0, Math.Min(20.0, Options.MaxVerticalJitter / 2.0));
            var Offsets = new List<double> { 0.0 };
            for (var Offset = Step; Offset <= Options.MaxVerticalJitter + 0.001; Offset += Step)
            {
                Offsets.Add(-Offset);
                Offsets.Add(Offset);
            }

            foreach (var Offset in Offsets)
            {
                var Candidate = ClampToBand(Item, new Point(Center.X, Center.Y + Offset));
                var Rect = GetInflatedBounds(Item, Candidate, Options.RelationshipBubblePadding);
                if (!ConceptBounds.Any(Bounds => Bounds.IntersectsWith(Rect)) &&
                    !PlacedBounds.Any(Bounds => Bounds.IntersectsWith(Rect)))
                    return Candidate;
            }

            if (ConceptBounds.Any(Bounds => Bounds.IntersectsWith(GetInflatedBounds(Item, Center, Options.RelationshipBubblePadding))))
                Result.AddWarning("Relationship '" + Item.Relationship.TechName.ToStringAlways() +
                                  "' central symbol could not avoid all concept bounds within vertical jitter limit.");

            return Center;
        }

        private static void RunGlobalDeclutter(IList<RelationshipNodeItem> Items, IList<Rect> ConceptBounds,
                                               RelationshipNodeDeclutterOptions Options,
                                               RelationshipNodeDeclutterResult Result)
        {
            var MaxPasses = Math.Max(0, Options.MaxGlobalDeclutterPasses);
            for (var Pass = 1; Pass <= MaxPasses; Pass++)
            {
                var RelationshipOverlaps = GetRelationshipOverlapPairs(Items, Options.RelationshipBubblePadding);
                var ConceptOverlaps = GetConceptOverlapItems(Items, ConceptBounds, Options).ToList();
                var TotalOverlaps = RelationshipOverlaps.Count + ConceptOverlaps.Count;
                Console.WriteLine("Appearance: relationship declutter global pass {0}: relationshipOverlaps={1}; conceptOverlaps={2}.",
                                  Pass, RelationshipOverlaps.Count, ConceptOverlaps.Count);

                if (TotalOverlaps < 1)
                    break;

                Result.GlobalDeclutterPasses = Pass;
                var MovedThisPass = 0;
                var ItemsToTry = RelationshipOverlaps.Select(Pair => ChooseItemToMove(Pair.Item1, Pair.Item2))
                                                     .Concat(ConceptOverlaps)
                                                     .Distinct()
                                                     .OrderByDescending(GetMovePriority)
                                                     .ThenBy(Item => Item.SortKey)
                                                     .ToList();

                foreach (var Item in ItemsToTry)
                {
                    Point NewCenter;
                    string CandidateLabel;
                    double CandidateScore;
                    string Reason = DescribeBlockingReason(Item, Items, ConceptBounds, Options);

                    if (TryFindClearCandidate(Item, Items, ConceptBounds, Options, false,
                                              out NewCenter, out CandidateLabel, out CandidateScore))
                    {
                        if (Distance(Item.NewCenter, NewCenter) <= GeometryTolerance)
                            continue;

                        Console.WriteLine("Appearance: relationship declutter global pass {0}: moving {1} from {2} to {3}; reason={4}; candidate={5}; score={6:0.###}; priority={7}; insideCorridor={8}; displacement={9:0.##}; corridor={10}.",
                                          Pass,
                                          DescribeIdea(Item.Relationship),
                                          FormatPoint(Item.NewCenter),
                                          FormatPoint(NewCenter),
                                          Reason,
                                          CandidateLabel,
                                          CandidateScore,
                                          Item.EdgePriority,
                                          Item.EndpointCorridor.Contains(NewCenter) ? "true" : "false",
                                          Distance(NewCenter, Item.PreferredCenter),
                                          FormatRect(Item.EndpointCorridor));
                        Item.NewCenter = NewCenter;
                        Result.GlobalDeclutterMoves++;
                        MovedThisPass++;
                    }
                    else
                    {
                        var Warning = "Relationship '" + Item.Relationship.TechName.ToStringAlways() +
                                      "' central symbol still overlaps after global declutter search; " + Reason + ".";
                        Result.AddWarning(Warning);
                        Console.WriteLine("Appearance relationship node declutter warning: {0}", Warning);
                    }
                }

                if (MovedThisPass < 1)
                    break;
            }
        }

        private static bool TryFindClearCandidate(RelationshipNodeItem Item, IList<RelationshipNodeItem> Items,
                                                  IList<Rect> ConceptBounds, RelationshipNodeDeclutterOptions Options,
                                                  bool RequireInsideCorridor,
                                                  out Point NewCenter, out string CandidateLabel, out double CandidateScore)
        {
            foreach (var Candidate in BuildCandidates(Item, Options).OrderBy(Candidate => Candidate.Score))
            {
                if (RequireInsideCorridor && !Candidate.InsideCorridor)
                {
                    Console.WriteLine("Appearance: relationship declutter candidate rejected; relationship={0}; candidate={1}; center={2}; reason=outside endpoint corridor for correction.",
                                      DescribeIdea(Item.Relationship),
                                      Candidate.Label,
                                      FormatPoint(Candidate.Center));
                    continue;
                }

                string RejectionReason;
                if (IsCandidateClear(Item, Candidate.Center, Items, ConceptBounds, Options, out RejectionReason))
                {
                    NewCenter = Candidate.Center;
                    CandidateLabel = Candidate.Label;
                    CandidateScore = Candidate.Score;
                    return true;
                }

                Candidate.RejectionReason = RejectionReason;
                Console.WriteLine("Appearance: relationship declutter candidate rejected; relationship={0}; candidate={1}; center={2}; reason={3}.",
                                  DescribeIdea(Item.Relationship),
                                  Candidate.Label,
                                  FormatPoint(Candidate.Center),
                                  RejectionReason.ToStringAlways());
            }

            NewCenter = Item.NewCenter;
            CandidateLabel = "<none>";
            CandidateScore = 0.0;
            return false;
        }

        private static IEnumerable<PlacementCandidate> BuildCandidates(RelationshipNodeItem Item, RelationshipNodeDeclutterOptions Options)
        {
            var Candidates = new List<PlacementCandidate>();
            var HorizontalStep = Math.Max(Options.RelationshipNodeSpacingX + Item.Symbol.BaseWidth / 2.0,
                                          32.0);
            var VerticalStep = Math.Max(Options.RelationshipBandPaddingY,
                                        Item.Symbol.BaseHeight + Options.RelationshipNodeSpacingX / 2.0);
            var Steps = Math.Max(1, Options.CandidateShiftSteps);

            AddCandidate(Candidates, Item, Options, Item.PreferredCenter, "preferred-midpoint");
            AddCandidate(Candidates, Item, Options, Item.NewCenter, "current-planned");
            AddCandidate(Candidates, Item, Options, Item.Symbol.BaseCenter, "current-symbol");

            for (var Step = 1; Step <= Steps; Step++)
            {
                AddCandidate(Candidates, Item, Options, new Point(Item.PreferredCenter.X - HorizontalStep * Step, Item.PreferredCenter.Y),
                             "left-" + Step.ToString(CultureInfo.InvariantCulture));
                AddCandidate(Candidates, Item, Options, new Point(Item.PreferredCenter.X + HorizontalStep * Step, Item.PreferredCenter.Y),
                             "right-" + Step.ToString(CultureInfo.InvariantCulture));
                AddCandidate(Candidates, Item, Options, new Point(Item.PreferredCenter.X, Item.PreferredCenter.Y - VerticalStep * Step),
                             "up-" + Step.ToString(CultureInfo.InvariantCulture));
                AddCandidate(Candidates, Item, Options, new Point(Item.PreferredCenter.X, Item.PreferredCenter.Y + VerticalStep * Step),
                             "down-" + Step.ToString(CultureInfo.InvariantCulture));
                AddCandidate(Candidates, Item, Options, new Point(Item.PreferredCenter.X - HorizontalStep * Step, Item.PreferredCenter.Y - VerticalStep),
                             "left-up-" + Step.ToString(CultureInfo.InvariantCulture));
                AddCandidate(Candidates, Item, Options, new Point(Item.PreferredCenter.X - HorizontalStep * Step, Item.PreferredCenter.Y + VerticalStep),
                             "left-down-" + Step.ToString(CultureInfo.InvariantCulture));
                AddCandidate(Candidates, Item, Options, new Point(Item.PreferredCenter.X + HorizontalStep * Step, Item.PreferredCenter.Y - VerticalStep),
                             "right-up-" + Step.ToString(CultureInfo.InvariantCulture));
                AddCandidate(Candidates, Item, Options, new Point(Item.PreferredCenter.X + HorizontalStep * Step, Item.PreferredCenter.Y + VerticalStep),
                             "right-down-" + Step.ToString(CultureInfo.InvariantCulture));
            }

            var DeltaX = Item.TargetCenter.X - Item.OriginCenter.X;
            var DeltaY = Item.TargetCenter.Y - Item.OriginCenter.Y;
            var Length = Math.Sqrt(DeltaX * DeltaX + DeltaY * DeltaY);
            if (Length > GeometryTolerance)
            {
                var PerpX = -DeltaY / Length;
                var PerpY = DeltaX / Length;
                var Fractions = new[] { 0.4, 0.5, 0.6 };
                foreach (var Fraction in Fractions)
                {
                    var Along = new Point(Item.OriginCenter.X + DeltaX * Fraction,
                                          Item.OriginCenter.Y + DeltaY * Fraction);
                    for (var Step = 1; Step <= Steps; Step++)
                    {
                        var Offset = VerticalStep * Step;
                        AddCandidate(Candidates, Item, Options, new Point(Along.X + PerpX * Offset, Along.Y + PerpY * Offset),
                                     "perpendicular+" + Step.ToString(CultureInfo.InvariantCulture));
                        AddCandidate(Candidates, Item, Options, new Point(Along.X - PerpX * Offset, Along.Y - PerpY * Offset),
                                     "perpendicular-" + Step.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }

            return Candidates.GroupBy(Candidate => Math.Round(Candidate.Center.X, 1).ToString(CultureInfo.InvariantCulture) + "|" +
                                                   Math.Round(Candidate.Center.Y, 1).ToString(CultureInfo.InvariantCulture))
                             .Select(Group => Group.First());
        }

        private static void AddCandidate(IList<PlacementCandidate> Candidates, RelationshipNodeItem Item,
                                         RelationshipNodeDeclutterOptions Options, Point Center, string Label)
        {
            Center = ClampToBand(Item, Center);
            if (!IsUsablePoint(Center))
                return;

            var Displacement = Distance(Center, Item.PreferredCenter);
            var InsideCorridor = Item.EndpointCorridor.Contains(Center);
            var OutsideCorridorPenalty = InsideCorridor ? 0.0 : Options.OutsideCorridorPenalty;
            var PreferredPenalty = Math.Max(0.0, Displacement - Options.MaxPreferredDisplacement) * 20.0;

            Candidates.Add(new PlacementCandidate
            {
                Center = Center,
                Label = Label,
                Displacement = Displacement,
                InsideCorridor = InsideCorridor,
                Score = Displacement +
                        Distance(Center, Item.Symbol.BaseCenter) * 0.25 +
                        Math.Abs(Center.Y - Item.PreferredCenter.Y) * 0.1 +
                        OutsideCorridorPenalty +
                        PreferredPenalty
            });
        }

        private static bool IsCandidateClear(RelationshipNodeItem Item, Point Center, IList<RelationshipNodeItem> Items,
                                             IList<Rect> ConceptBounds, RelationshipNodeDeclutterOptions Options,
                                             out string RejectionReason)
        {
            var Bounds = GetInflatedBounds(Item, Center, Options.RelationshipBubblePadding);
            var Displacement = Distance(Center, Item.PreferredCenter);

            if (Options.HardMaxDisplacement > 0.0 &&
                Displacement > Options.HardMaxDisplacement + GeometryTolerance)
            {
                RejectionReason = "exceeds hard displacement limit (" +
                                  Displacement.ToString("0.##", CultureInfo.InvariantCulture) + " > " +
                                  Options.HardMaxDisplacement.ToString("0.##", CultureInfo.InvariantCulture) + ")";
                return false;
            }

            if (Options.HardRejectOutsideCorridorForAnchoredEdges &&
                IsAnchoredEdge(Item) &&
                !Item.EndpointCorridor.Contains(Center))
            {
                RejectionReason = "outside endpoint corridor for anchored " + Item.EdgePriority +
                                  " relationship; corridor=" + FormatRect(Item.EndpointCorridor);
                return false;
            }

            if (Options.AvoidConceptBounds)
            {
                foreach (var ConceptCollision in ConceptBounds.Where(Rectangle => Rectangle.IntersectsWith(Bounds)))
                {
                    RejectionReason = "overlaps concept bounds " + FormatRect(ConceptCollision);
                    return false;
                }
            }

            var RelationshipCollision = Items.Where(Other => Other != Item)
                                             .FirstOrDefault(Other => GetInflatedBounds(Other, Other.NewCenter, Options.RelationshipBubblePadding)
                                                                      .IntersectsWith(Bounds));
            if (RelationshipCollision != null)
            {
                RejectionReason = "overlaps relationship '" + RelationshipCollision.Relationship.TechName.ToStringAlways() + "'";
                return false;
            }

            RejectionReason = null;
            return true;
        }

        private static void ValidateFinalPlacement(IList<RelationshipNodeItem> Items, IList<Rect> ConceptBounds,
                                                   RelationshipNodeDeclutterOptions Options,
                                                   RelationshipNodeDeclutterResult Result)
        {
            var RelationshipOverlaps = GetRelationshipOverlapPairs(Items, Options.RelationshipBubblePadding);
            var ConceptOverlaps = GetConceptOverlapItems(Items, ConceptBounds, Options).ToList();
            var CorridorViolations = Items.Where(Item => !Item.EndpointCorridor.Contains(Item.NewCenter)).ToList();

            Result.FinalOverlapCount = RelationshipOverlaps.Count;
            Result.FinalConceptOverlapCount = ConceptOverlaps.Count;
            Result.CorridorViolations = CorridorViolations.Count;

            foreach (var Pair in RelationshipOverlaps)
            {
                var Warning = "Remaining relationship bubble overlap after declutter: " +
                              Pair.Item1.Relationship.TechName.ToStringAlways() + " vs " +
                              Pair.Item2.Relationship.TechName.ToStringAlways() + ".";
                Result.AddWarning(Warning);
                Console.WriteLine("Appearance relationship node declutter warning: {0}", Warning);
            }

            foreach (var Item in ConceptOverlaps)
            {
                var Warning = "Remaining relationship bubble to concept overlap after declutter: " +
                              Item.Relationship.TechName.ToStringAlways() + ".";
                Result.AddWarning(Warning);
                Console.WriteLine("Appearance relationship node declutter warning: {0}", Warning);
            }

            foreach (var Item in CorridorViolations)
            {
                var Warning = "Relationship bubble outside endpoint corridor: '" +
                              Item.Relationship.TechName.ToStringAlways() + "' midpoint=" +
                              FormatPoint(Item.PreferredCenter) + "; final=" + FormatPoint(Item.NewCenter) +
                              "; displacement=" + Distance(Item.NewCenter, Item.PreferredCenter).ToString("0.##", CultureInfo.InvariantCulture) +
                              "; corridor=" + FormatRect(Item.EndpointCorridor) + ".";
                Result.AddWarning(Warning);
                Console.WriteLine("Appearance relationship node declutter warning: {0}", Warning);
            }
        }

        private static void CorrectCorridorViolations(IList<RelationshipNodeItem> Items, IList<Rect> ConceptBounds,
                                                      RelationshipNodeDeclutterOptions Options,
                                                      RelationshipNodeDeclutterResult Result)
        {
            foreach (var Item in Items.Where(Item => !Item.EndpointCorridor.Contains(Item.NewCenter))
                                      .OrderBy(Item => Item.EdgePriority)
                                      .ThenBy(Item => Item.SortKey)
                                      .ToList())
            {
                Point NewCenter;
                string CandidateLabel;
                double CandidateScore;

                if (!TryFindClearCandidate(Item, Items, ConceptBounds, Options, true,
                                           out NewCenter, out CandidateLabel, out CandidateScore))
                {
                    Console.WriteLine("Appearance: relationship corridor correction skipped; relationship={0}; final={1}; corridor={2}; reason=no clear in-corridor candidate.",
                                      DescribeIdea(Item.Relationship),
                                      FormatPoint(Item.NewCenter),
                                      FormatRect(Item.EndpointCorridor));
                    continue;
                }

                if (Distance(Item.NewCenter, NewCenter) <= GeometryTolerance)
                    continue;

                Console.WriteLine("Appearance: relationship corridor correction: moving {0} from {1} to {2}; candidate={3}; score={4:0.###}; priority={5}; midpoint={6}; corridor={7}.",
                                  DescribeIdea(Item.Relationship),
                                  FormatPoint(Item.NewCenter),
                                  FormatPoint(NewCenter),
                                  CandidateLabel,
                                  CandidateScore,
                                  Item.EdgePriority,
                                  FormatPoint(Item.PreferredCenter),
                                  FormatRect(Item.EndpointCorridor));
                Item.NewCenter = NewCenter;
                Result.CorridorCorrections++;
            }
        }

        private static void LogRelationshipOverlaps(string Stage, IList<RelationshipNodeItem> Items,
                                                    RelationshipNodeDeclutterOptions Options)
        {
            foreach (var Pair in GetRelationshipOverlapPairs(Items, Options.RelationshipBubblePadding))
                Console.WriteLine("Appearance: Relationship bubble overlap {0}: '{1}' overlaps '{2}'; boundsA={3}; boundsB={4}.",
                                  Stage,
                                  Pair.Item1.Relationship.TechName.ToStringAlways(),
                                  Pair.Item2.Relationship.TechName.ToStringAlways(),
                                  FormatRect(GetInflatedBounds(Pair.Item1, Pair.Item1.NewCenter, Options.RelationshipBubblePadding)),
                                  FormatRect(GetInflatedBounds(Pair.Item2, Pair.Item2.NewCenter, Options.RelationshipBubblePadding)));
        }

        private static IList<Tuple<RelationshipNodeItem, RelationshipNodeItem>> GetRelationshipOverlapPairs(IList<RelationshipNodeItem> Items,
                                                                                                            double Padding)
        {
            var Result = new List<Tuple<RelationshipNodeItem, RelationshipNodeItem>>();
            for (var Index = 0; Index < Items.Count; Index++)
                for (var Other = Index + 1; Other < Items.Count; Other++)
                    if (GetInflatedBounds(Items[Index], Items[Index].NewCenter, Padding)
                        .IntersectsWith(GetInflatedBounds(Items[Other], Items[Other].NewCenter, Padding)))
                        Result.Add(Tuple.Create(Items[Index], Items[Other]));

            return Result;
        }

        private static int CountRelationshipOverlaps(IList<RelationshipNodeItem> Items, double Padding)
        {
            return GetRelationshipOverlapPairs(Items, Padding).Count;
        }

        private static IEnumerable<RelationshipNodeItem> GetConceptOverlapItems(IList<RelationshipNodeItem> Items,
                                                                                IList<Rect> ConceptBounds,
                                                                                RelationshipNodeDeclutterOptions Options)
        {
            if (!Options.AvoidConceptBounds)
                return Enumerable.Empty<RelationshipNodeItem>();

            return Items.Where(Item => ConceptBounds.Any(Bounds => Bounds.IntersectsWith(GetInflatedBounds(Item, Item.NewCenter, Options.RelationshipBubblePadding))));
        }

        private static RelationshipNodeItem ChooseItemToMove(RelationshipNodeItem First, RelationshipNodeItem Second)
        {
            var FirstPriority = GetMovePriority(First);
            var SecondPriority = GetMovePriority(Second);

            if (FirstPriority != SecondPriority)
                return FirstPriority > SecondPriority ? First : Second;

            return String.CompareOrdinal(First.SortKey, Second.SortKey) > 0 ? First : Second;
        }

        private static int GetMovePriority(RelationshipNodeItem Item)
        {
            var Priority = 0;
            if (Item.SourceLevel > Item.TargetLevel)
                Priority += 100;

            if (Math.Abs(Item.SourceLevel - Item.TargetLevel) != 1)
                Priority += 70;

            if (Item.SourceLevel == Item.TargetLevel)
                Priority += Item.IsLocalShortEdge ? 20 : 60;

            if (Item.SourceLevel + 1 == Item.TargetLevel)
                Priority -= 40;

            if (Item.IsLocalShortEdge)
                Priority -= 30;

            return Priority;
        }

        private static string DescribeBlockingReason(RelationshipNodeItem Item, IList<RelationshipNodeItem> Items,
                                                     IList<Rect> ConceptBounds, RelationshipNodeDeclutterOptions Options)
        {
            var Bounds = GetInflatedBounds(Item, Item.NewCenter, Options.RelationshipBubblePadding);
            var RelationshipCollision = Items.Where(Other => Other != Item)
                                             .FirstOrDefault(Other => GetInflatedBounds(Other, Other.NewCenter, Options.RelationshipBubblePadding)
                                                                      .IntersectsWith(Bounds));
            if (RelationshipCollision != null)
                return "overlap with '" + RelationshipCollision.Relationship.TechName.ToStringAlways() + "'";

            if (Options.AvoidConceptBounds && ConceptBounds.Any(Rectangle => Rectangle.IntersectsWith(Bounds)))
                return "overlap with concept bounds";

            return "overlap detected";
        }

        private static void ApplyMove(RelationshipNodeItem Item, RelationshipNodeDeclutterResult Result)
        {
            var OldCenter = Item.Symbol.BaseCenter;
            if (Distance(OldCenter, Item.NewCenter) > GeometryTolerance)
            {
                Item.Symbol.MoveTo(Item.NewCenter.X, Item.NewCenter.Y, true);
                Item.Representation.Render();
                Result.RelationshipSymbolsMoved++;
            }
            else
                Item.Symbol.RenderElement();

            Console.WriteLine("Appearance: relationship node declutter relationship={0}; endpoints={1}; priority={2}; oldCenter=({3:0.##},{4:0.##}); preferred=({5:0.##},{6:0.##}); newCenter=({7:0.##},{8:0.##}); displacement={9:0.##}; insideCorridor={10}; corridor={11}; directedLevels={12}->{13}; visualBand={14}-{15}.",
                              DescribeIdea(Item.Relationship),
                              DescribeEndpoints(Item),
                              Item.EdgePriority,
                              OldCenter.X,
                              OldCenter.Y,
                              Item.PreferredCenter.X,
                              Item.PreferredCenter.Y,
                              Item.NewCenter.X,
                              Item.NewCenter.Y,
                              Distance(Item.NewCenter, Item.PreferredCenter),
                              Item.EndpointCorridor.Contains(Item.NewCenter) ? "true" : "false",
                              FormatRect(Item.EndpointCorridor),
                              Item.SourceLevel,
                              Item.TargetLevel,
                              Item.BandStartLevel,
                              Item.BandEndLevel);
        }

        private static void SetBand(RelationshipNodeItem Item, RelationshipNodeDeclutterOptions Options)
        {
            var TopSymbols = Item.OriginSymbols.Concat(Item.TargetSymbols)
                                               .OrderBy(Symbol => Symbol.BaseCenter.Y)
                                               .Take(Math.Max(Item.OriginSymbols.Count, 1))
                                               .ToList();
            var BottomSymbols = Item.OriginSymbols.Concat(Item.TargetSymbols)
                                                  .OrderByDescending(Symbol => Symbol.BaseCenter.Y)
                                                  .Take(Math.Max(Item.TargetSymbols.Count, 1))
                                                  .ToList();
            var Top = TopSymbols.Max(Symbol => Symbol.TotalArea.Bottom) + Options.RelationshipBandPaddingY;
            var Bottom = BottomSymbols.Min(Symbol => Symbol.TotalArea.Top) - Options.RelationshipBandPaddingY;

            if (Bottom >= Top)
            {
                Item.BandTop = Top;
                Item.BandBottom = Bottom;
            }
            else
            {
                Item.BandTop = Item.PreferredCenter.Y - Options.MaxVerticalJitter;
                Item.BandBottom = Item.PreferredCenter.Y + Options.MaxVerticalJitter;
            }
        }

        private static Point ClampToBand(RelationshipNodeItem Item, Point Center)
        {
            return new Point(Center.X, Center.Y.EnforceRange(Item.BandTop, Item.BandBottom));
        }

        private static Point GetMidpoint(IList<VisualSymbol> Origins, IList<VisualSymbol> Targets)
        {
            var Origin = AveragePoint(Origins.Select(Symbol => Symbol.BaseCenter));
            var Target = AveragePoint(Targets.Select(Symbol => Symbol.BaseCenter));
            return new Point((Origin.X + Target.X) / 2.0, (Origin.Y + Target.Y) / 2.0);
        }

        private static Point GetMidpoint(Point Origin, Point Target)
        {
            return new Point((Origin.X + Target.X) / 2.0, (Origin.Y + Target.Y) / 2.0);
        }

        private static Rect GetEndpointCorridor(IList<VisualSymbol> Origins, IList<VisualSymbol> Targets,
                                                RelationshipNodeDeclutterOptions Options)
        {
            Rect? Corridor = null;
            foreach (var Symbol in Origins.Concat(Targets).Where(EndpointSymbol => EndpointSymbol != null))
            {
                var Area = Symbol.TotalArea;
                if (Area.IsEmpty)
                    continue;

                Corridor = Corridor.HasValue ? Rect.Union(Corridor.Value, Area) : Area;
            }

            var Result = Corridor ?? Rect.Empty;
            if (!Result.IsEmpty)
                Result.Inflate(Options.CorridorPaddingX, Options.CorridorPaddingY);

            return Result;
        }

        private static bool IsAnchoredEdge(RelationshipNodeItem Item)
        {
            return Item != null &&
                   (Item.SourceLevel + 1 == Item.TargetLevel ||
                    Item.IsLocalShortEdge);
        }

        private static string GetEdgePriorityLabel(RelationshipNodeItem Item)
        {
            if (Item.SourceLevel + 1 == Item.TargetLevel)
                return Item.IsLocalShortEdge ? "primary-tree-local" : "primary-tree";

            if (Item.SourceLevel > Item.TargetLevel)
                return Item.IsLocalShortEdge ? "reverse-level-local" : "reverse-level";

            if (Item.SourceLevel == Item.TargetLevel)
                return Item.IsLocalShortEdge ? "same-level-local" : "same-level";

            if (Math.Abs(Item.SourceLevel - Item.TargetLevel) > 1)
                return Item.IsLocalShortEdge ? "cross-link-local" : "cross-link";

            return Item.IsLocalShortEdge ? "local" : "unknown";
        }

        private static Point AveragePoint(IEnumerable<Point> Points)
        {
            var List = Points.ToList();
            return new Point(List.Average(Point => Point.X), List.Average(Point => Point.Y));
        }

        private static Rect GetBounds(RelationshipNodeItem Item, Point Center)
        {
            return new Rect(Center.X - Item.Symbol.BaseWidth / 2.0,
                            Center.Y - Item.Symbol.BaseHeight / 2.0,
                            Item.Symbol.BaseWidth,
                            Item.Symbol.BaseHeight);
        }

        private static Rect GetInflatedBounds(RelationshipNodeItem Item, Point Center, double Padding)
        {
            return Inflate(GetBounds(Item, Center), Padding);
        }

        private static Rect Inflate(Rect Rectangle, double Padding)
        {
            if (Rectangle.IsEmpty)
                return Rectangle;

            Rectangle.Inflate(Math.Max(0.0, Padding), Math.Max(0.0, Padding));
            return Rectangle;
        }

        private static string GetBandKey(RelationshipNodeItem Item)
        {
            return Item.BandStartLevel.ToString(CultureInfo.InvariantCulture) + "-" +
                   Item.BandEndLevel.ToString(CultureInfo.InvariantCulture);
        }

        private static int CountOverlapGroups(IList<Rect> Rectangles)
        {
            var Remaining = new HashSet<int>(Enumerable.Range(0, Rectangles.Count));
            var Groups = 0;

            while (Remaining.Count > 0)
            {
                var Seed = Remaining.First();
                var Queue = new Queue<int>();
                var Size = 0;
                Queue.Enqueue(Seed);
                Remaining.Remove(Seed);

                while (Queue.Count > 0)
                {
                    var Current = Queue.Dequeue();
                    Size++;
                    foreach (var Other in Remaining.ToList())
                        if (Rectangles[Current].IntersectsWith(Rectangles[Other]))
                        {
                            Remaining.Remove(Other);
                            Queue.Enqueue(Other);
                        }
                }

                if (Size > 1)
                    Groups++;
            }

            return Groups;
        }

        private static int CountOverlaps(IList<Rect> Rectangles)
        {
            var Result = 0;
            for (int Index = 0; Index < Rectangles.Count; Index++)
                for (int Other = Index + 1; Other < Rectangles.Count; Other++)
                    if (Rectangles[Index].IntersectsWith(Rectangles[Other]))
                        Result++;

            return Result;
        }

        private static void Skip(RelationshipNodeDeclutterResult Result, string Reason)
        {
            Result.RelationshipSymbolsSkipped++;
            Console.WriteLine("Appearance: relationship node declutter skipped: {0}.", Reason.ToStringAlways());
        }

        private static double Distance(Point First, Point Second)
        {
            var DeltaX = First.X - Second.X;
            var DeltaY = First.Y - Second.Y;
            return Math.Sqrt(DeltaX * DeltaX + DeltaY * DeltaY);
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

        private static string DescribeView(View View)
        {
            return View == null
                   ? "<no view>"
                   : View.Name.ToStringAlways() + " (" + View.TechName.ToStringAlways() + ", id=" + View.GlobalId + ")";
        }

        private static string DescribeIdea(Idea Idea)
        {
            return Idea == null
                   ? "<no idea>"
                   : Idea.Name.ToStringAlways() + " (" + Idea.TechName.ToStringAlways() + ", id=" + Idea.GlobalId + ")";
        }

        private static string DescribeEndpoints(RelationshipNodeItem Item)
        {
            if (Item == null)
                return "<none>";

            return "'" + String.Join(",", Item.OriginSymbols.Select(OriginSymbol => OriginSymbol.OwnerRepresentation == null ||
                                                                              OriginSymbol.OwnerRepresentation.RepresentedIdea == null
                                                                              ? "<origin>"
                                                                              : OriginSymbol.OwnerRepresentation.RepresentedIdea.Name.ToStringAlways()).ToArray()) +
                   "' -> '" +
                   String.Join(",", Item.TargetSymbols.Select(TargetSymbol => TargetSymbol.OwnerRepresentation == null ||
                                                                        TargetSymbol.OwnerRepresentation.RepresentedIdea == null
                                                                        ? "<target>"
                                                                        : TargetSymbol.OwnerRepresentation.RepresentedIdea.Name.ToStringAlways()).ToArray()) + "'";
        }

        private static string FormatPoint(Point Point)
        {
            if (!IsUsablePoint(Point))
                return "<invalid>";

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
    }
}
