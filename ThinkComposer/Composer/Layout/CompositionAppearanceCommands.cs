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
using System.Windows;

using Instrumind.Common;
using Instrumind.Common.Visualization;
using Instrumind.ThinkComposer.Model.VisualModel;

namespace Instrumind.ThinkComposer.Composer.Layout
{
    /// <summary>
    /// UI command facade for appearance/layout services.
    /// </summary>
    public static class CompositionAppearanceCommands
    {
        public static bool CanFitConceptWidthToText(CompositionEngine Engine)
        {
            return ConceptAutoFitService.CanFitSelection(Engine);
        }

        public static void FitConceptWidthToText(CompositionEngine Engine)
        {
            try
            {
                Console.WriteLine("Appearance command: Fit Concept Width to Text requested from menu.");
                ConceptAutoFitService.FitSelectedConceptWidths(Engine);
            }
            catch (Exception Problem)
            {
                Console.WriteLine("Appearance command failed: Fit Concept Width to Text. Problem: {0}", Problem.Message);
                Console.WriteLine(Problem.ToString());
                Display.DialogMessage("Fit Concept Width to Text",
                                      "Cannot fit concept width to text.\n\nProblem: " + Problem.Message,
                                      EMessageType.Error);
            }
        }

        public static void FitConceptWidthToText(CompositionEngine Engine, VisualSymbol Symbol, string Source)
        {
            try
            {
                Console.WriteLine("Appearance command: Fit Concept Width to Text requested from {0}.", Source ?? "visual gesture");
                ConceptAutoFitService.FitSingleConceptWidth(Engine, Symbol, Source);
            }
            catch (Exception Problem)
            {
                Console.WriteLine("Appearance command failed: Fit Concept Width to Text from {0}. Problem: {1}",
                                  Source ?? "visual gesture", Problem.Message);
                Console.WriteLine(Problem.ToString());
                Display.DialogMessage("Fit Concept Width to Text",
                                      "Cannot fit concept width to text.\n\nProblem: " + Problem.Message,
                                      EMessageType.Error);
            }
        }

        public static bool CanRouteLinksWithObstacleAvoidance(CompositionEngine Engine)
        {
            var Context = LayoutSelectionContext.FromActiveView(Engine);
            return Context.ActiveView != null && Context.VisibleRelationshipConnectors.Count > 0;
        }

        public static void RouteLinksWithObstacleAvoidance(CompositionEngine Engine)
        {
            var Context = LayoutSelectionContext.FromActiveView(Engine);
            if (Context.ActiveView == null)
                return;

            var Options = new LinkObstacleRoutingOptions();
            Options.IncludeRelationshipCentralSymbolsAsObstacles = true;
            Options.CorrectRelationshipCentersBeforeRouting = true;
            // This is an explicit user request to recompute the selected routes.  A valid
            // hand route must remain untouched during incidental edits, but preserving it
            // here would make Auto-route appear to do nothing.
            Options.PreserveExistingValidRoutes = false;
            Options.RouteIntent = RelationshipRouteIntent.Layout;
            Options.DirtyReason = "explicit Route Links with Obstacle Avoidance command";
            var SelectedConnectors = Context.SelectedRouteableConnectors;
            if (SelectedConnectors.Count < 1)
            {
                var Confirmation = Display.DialogMessage("Route Links with Obstacle Avoidance",
                                                         "No links are selected. Route all visible links in the active view?",
                                                         EMessageType.Question, MessageBoxButton.YesNo, MessageBoxResult.No);
                if (Confirmation != MessageBoxResult.Yes)
                    return;

                Options.RouteSelectedConnectorsOnly = false;
            }

            var LocalCommand = !Context.ActiveView.EditEngine.IsVariating;
            try
            {
                Console.WriteLine("Appearance command: Route Links with Obstacle Avoidance requested. View={0} ({1}) id={2}; scope={3}.",
                                  Context.ActiveView.Name, Context.ActiveView.TechName, Context.ActiveView.GlobalId,
                                  Options.RouteSelectedConnectorsOnly ? "selected links" : "all visible links");

                if (LocalCommand)
                    Context.ActiveView.EditEngine.StartCommandVariation("Route Links with Obstacle Avoidance");

                var Result = RelationshipRoutingCoordinator.Route(Context, Options);

                if (Result.HasMutations)
                    Context.ActiveView.UpdateVersion();

                if (LocalCommand)
                    Context.ActiveView.EditEngine.CompleteCommandVariation();

                Display.DialogMessage("Route Links with Obstacle Avoidance",
                                      "Connector routes inspected: " + Result.ConnectorRoutesInspected + "\n" +
                                      "Relationship routes inspected: " + Result.RelationshipRoutesInspected + "\n" +
                                      FormatRelationshipCenterPlacementForDialog(Result.RelationshipCenterPlacementResult) +
                                      "Routed links: " + Result.Routed + "\n" +
                                      "Dogleg routed links: " + Result.DoglegRouted + "\n" +
                                      "Straightened links: " + Result.Straightened + "\n" +
                                      "Unchanged: " + Result.Unchanged + "\n" +
                                      "Skipped: " + Result.Skipped + "\n" +
                                      "Layout warnings: " + Result.Warnings.Count + "\n\n" +
                                      "See the application log for details.",
                                      Result.Warnings.Count > 0 ? EMessageType.Warning : EMessageType.Information);
            }
            catch (Exception Problem)
            {
                Console.WriteLine("Appearance command failed: Route Links with Obstacle Avoidance. Problem: {0}", Problem.Message);
                Console.WriteLine(Problem.ToString());

                if (LocalCommand && Context.ActiveView.EditEngine.IsVariating)
                {
                    try
                    {
                        Context.ActiveView.EditEngine.DiscardCommandVariation();
                        Console.WriteLine("Appearance command: discarded Route Links command variation after failure.");
                    }
                    catch (Exception DiscardProblem)
                    {
                        Console.WriteLine("Appearance command: could not discard Route Links variation. Problem: {0}", DiscardProblem.Message);
                        Console.WriteLine(DiscardProblem.ToString());
                    }
                }

                Display.DialogMessage("Route Links with Obstacle Avoidance",
                                      "Cannot route links.\n\nProblem: " + Problem.Message,
                                      EMessageType.Error);
            }
        }

        public static bool CanArrangeAsSpiderMap(CompositionEngine Engine)
        {
            var Context = LayoutSelectionContext.FromActiveView(Engine);
            return SpiderMapLayoutService.CanArrange(Context);
        }

        public static void ArrangeAsSpiderMap(CompositionEngine Engine)
        {
            var Context = LayoutSelectionContext.FromActiveView(Engine);
            if (Context.ActiveView == null)
                return;

            var Options = new SpiderMapLayoutOptions();
            Options.ArrangeSelectedConceptsOnly = Context.SelectedConceptSymbols.Count > 0;

            if (!Options.ArrangeSelectedConceptsOnly)
            {
                var Confirmation = Display.DialogMessage("Arrange as Spider Map",
                                                         "No concepts are selected. Arrange all visible concepts in the active view as a Spider Map?",
                                                         EMessageType.Question, MessageBoxButton.YesNo, MessageBoxResult.No);
                if (Confirmation != MessageBoxResult.Yes)
                    return;
            }

            try
            {
                Console.WriteLine("Appearance command: Arrange as Spider Map requested. View={0} ({1}) id={2}; scope={3}.",
                                  Context.ActiveView.Name, Context.ActiveView.TechName, Context.ActiveView.GlobalId,
                                  Options.ArrangeSelectedConceptsOnly ? "selected concepts" : "all visible concepts");

                var Result = SpiderMapLayoutService.Arrange(Context, Options);

                Display.DialogMessage("Arrange as Spider Map",
                                      "Concepts arranged: " + Result.ConceptsArranged + "\n" +
                                      "Links routed: " + Result.LinksRouted + "\n" +
                                      "Skipped: " + Result.SkippedTotal + "\n" +
                                      "Layout warnings: " + Result.Warnings.Count + "\n\n" +
                                      "See the application log for details.",
                                      Result.Warnings.Count > 0 ? EMessageType.Warning : EMessageType.Information);
            }
            catch (Exception Problem)
            {
                Console.WriteLine("Appearance command failed: Arrange as Spider Map. Problem: {0}", Problem.Message);
                Console.WriteLine(Problem.ToString());
                Display.DialogMessage("Arrange as Spider Map",
                                      "Cannot arrange as Spider Map.\n\nProblem: " + Problem.Message,
                                      EMessageType.Error);
            }
        }

        public static bool CanArrangeAsHierarchyMap(CompositionEngine Engine)
        {
            var Context = LayoutSelectionContext.FromActiveView(Engine);
            return HierarchyMapLayoutService.CanArrange(Context);
        }

        public static bool CanArrangeAsFlowchart(CompositionEngine Engine)
        {
            var Context = LayoutSelectionContext.FromActiveView(Engine);
            return FlowchartLayoutService.CanArrange(Context);
        }

        public static bool CanArrangeAsSystemMap(CompositionEngine Engine)
        {
            var Context = LayoutSelectionContext.FromActiveView(Engine);
            return SystemMapLayoutService.CanArrange(Context);
        }

        public static void ArrangeAsFlowchart(CompositionEngine Engine)
        {
            var Context = LayoutSelectionContext.FromActiveView(Engine);
            if (Context.ActiveView == null)
                return;

            var Options = new FlowchartLayoutOptions();
            Options.ArrangeSelectedConceptsOnly = Context.SelectedConceptSymbols.Count > 0;

            if (!Options.ArrangeSelectedConceptsOnly)
            {
                var Confirmation = Display.DialogMessage("Arrange as Flowchart",
                                                         "No concepts are selected. Arrange all visible concepts in the active view as a Flowchart?",
                                                         EMessageType.Question, MessageBoxButton.YesNo, MessageBoxResult.No);
                if (Confirmation != MessageBoxResult.Yes)
                    return;
            }

            try
            {
                Console.WriteLine("Appearance command: Arrange as Flowchart requested. View={0} ({1}) id={2}; scope={3}.",
                                  Context.ActiveView.Name, Context.ActiveView.TechName, Context.ActiveView.GlobalId,
                                  Options.ArrangeSelectedConceptsOnly ? "selected concepts" : "all visible concepts");

                var Result = FlowchartLayoutService.Arrange(Context, Options);

                Display.DialogMessage("Arrange as Flowchart",
                                      "Concepts arranged: " + Result.ConceptsArranged + "\n" +
                                      "Starts: " + Result.StartCount + "\n" +
                                      "Flow steps: " + Result.StepCount + "\n" +
                                      "Links routed: " + Result.LinksRouted + "\n" +
                                      "Skipped: " + Result.SkippedTotal + "\n" +
                                      "Layout warnings: " + Result.Warnings.Count + "\n\n" +
                                      "See the application log for details.",
                                      Result.Warnings.Count > 0 ? EMessageType.Warning : EMessageType.Information);
            }
            catch (Exception Problem)
            {
                Console.WriteLine("Appearance command failed: Arrange as Flowchart. Problem: {0}", Problem.Message);
                Console.WriteLine(Problem.ToString());
                Display.DialogMessage("Arrange as Flowchart",
                                      "Cannot arrange as Flowchart.\n\nProblem: " + Problem.Message,
                                      EMessageType.Error);
            }
        }

        public static void ArrangeAsSystemMap(CompositionEngine Engine)
        {
            var Context = LayoutSelectionContext.FromActiveView(Engine);
            if (Context.ActiveView == null)
                return;

            var Options = new SystemMapLayoutOptions();
            Options.ArrangeSelectedConceptsOnly = Context.SelectedConceptSymbols.Count > 0;

            if (!Options.ArrangeSelectedConceptsOnly)
            {
                var Confirmation = Display.DialogMessage("Arrange as System Map",
                                                         "No concepts are selected. Arrange all visible concepts in the active view as a System Map?",
                                                         EMessageType.Question, MessageBoxButton.YesNo, MessageBoxResult.No);
                if (Confirmation != MessageBoxResult.Yes)
                    return;
            }

            try
            {
                Console.WriteLine("Appearance command: Arrange as System Map requested. View={0} ({1}) id={2}; scope={3}.",
                                  Context.ActiveView.Name, Context.ActiveView.TechName, Context.ActiveView.GlobalId,
                                  Options.ArrangeSelectedConceptsOnly ? "selected concepts" : "all visible concepts");

                var Result = SystemMapLayoutService.Arrange(Context, Options);

                Display.DialogMessage("Arrange as System Map",
                                      "Concepts arranged: " + Result.ConceptsArranged + "\n" +
                                      "Internal: " + (Result.InternalCount + Result.AmbiguousCount) + "\n" +
                                      "External: " + Result.ExternalCount + "\n" +
                                      "Links routed: " + Result.LinksRouted + "\n" +
                                      "Group region: " + Result.GroupRegionStatus + "\n" +
                                      "Skipped: " + Result.SkippedTotal + "\n" +
                                      "Layout warnings: " + Result.Warnings.Count + "\n\n" +
                                      "See the application log for details.",
                                      Result.Warnings.Count > 0 ? EMessageType.Warning : EMessageType.Information);
            }
            catch (Exception Problem)
            {
                Console.WriteLine("Appearance command failed: Arrange as System Map. Problem: {0}", Problem.Message);
                Console.WriteLine(Problem.ToString());
                Display.DialogMessage("Arrange as System Map",
                                      "Cannot arrange as System Map.\n\nProblem: " + Problem.Message,
                                      EMessageType.Error);
            }
        }

        public static void ArrangeAsHierarchyMap(CompositionEngine Engine)
        {
            var Context = LayoutSelectionContext.FromActiveView(Engine);
            if (Context.ActiveView == null)
                return;

            var Options = new HierarchyMapLayoutOptions();
            Options.ArrangeSelectedConceptsOnly = Context.SelectedConceptSymbols.Count > 0;

            if (!Options.ArrangeSelectedConceptsOnly)
            {
                var Confirmation = Display.DialogMessage("Arrange as Hierarchy Map",
                                                         "No concepts are selected. Arrange all visible concepts in the active view as a Hierarchy Map?",
                                                         EMessageType.Question, MessageBoxButton.YesNo, MessageBoxResult.No);
                if (Confirmation != MessageBoxResult.Yes)
                    return;
            }

            try
            {
                Console.WriteLine("Appearance command: Arrange as Hierarchy Map requested. View={0} ({1}) id={2}; scope={3}.",
                                  Context.ActiveView.Name, Context.ActiveView.TechName, Context.ActiveView.GlobalId,
                                  Options.ArrangeSelectedConceptsOnly ? "selected concepts" : "all visible concepts");

                var Result = HierarchyMapLayoutService.Arrange(Context, Options);

                Display.DialogMessage("Arrange as Hierarchy Map",
                                      "Concepts arranged: " + Result.ConceptsArranged + "\n" +
                                      "Roots: " + Result.RootCount + "\n" +
                                      "Levels: " + Result.LevelCount + "\n" +
                                      "Links routed: " + Result.LinksRouted + "\n" +
                                      "Skipped: " + Result.SkippedTotal + "\n" +
                                      "Layout warnings: " + Result.Warnings.Count + "\n\n" +
                                      "See the application log for details.",
                                      Result.Warnings.Count > 0 ? EMessageType.Warning : EMessageType.Information);
            }
            catch (Exception Problem)
            {
                Console.WriteLine("Appearance command failed: Arrange as Hierarchy Map. Problem: {0}", Problem.Message);
                Console.WriteLine(Problem.ToString());
                Display.DialogMessage("Arrange as Hierarchy Map",
                                      "Cannot arrange as Hierarchy Map.\n\nProblem: " + Problem.Message,
                                      EMessageType.Error);
            }
        }

        public static bool IsFutureAppearanceToolEnabled(CompositionEngine Engine)
        {
            return false;
        }

        private static string FormatRelationshipCenterPlacementForDialog(RelationshipVisualPlacementResult Result)
        {
            if (Result == null)
                return "";

            return "Relationship centers inspected: " + Result.RelationshipCentersInspected + "\n" +
                   "Relationship centers recomputed: " + Result.RelationshipCentersRecomputed + "\n" +
                   "Suspicious relationship centers: " + Result.SuspiciousRelationshipCenters + "\n";
        }

        public static void ShowFutureAppearanceToolMessage(string CommandName)
        {
            Console.WriteLine("Appearance command planned but not implemented yet: {0}.", CommandName);
            Display.DialogMessage("Appearance", CommandName + " is planned, but not implemented yet.", EMessageType.Information);
        }
    }
}
