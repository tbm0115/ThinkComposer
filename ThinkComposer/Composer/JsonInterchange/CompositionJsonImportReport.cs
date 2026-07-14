// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// JSON interchange import report.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Instrumind.ThinkComposer.Composer.Layout;

namespace Instrumind.ThinkComposer.Composer.JsonInterchange
{
    public class CompositionJsonImportReport
    {
        public CompositionJsonImportReport()
        {
            this.Warnings = new List<string>();
            this.SourceWarnings = new List<string>();
            this.ImportWarnings = new List<string>();
            this.Notes = new List<string>();
            this.SkippedMessages = new List<string>();
            this.Errors = new List<string>();
            this.InfoLogLines = new List<string>();
            this.AffectedViewNames = new List<string>();
        }

        public bool IsPreview { get; set; }

        /// <summary>
        /// Suppresses high-volume informational logging for native package rehydration.
        /// Warnings/skips are still retained in their report collections and a bounded
        /// sample is written to the application log; errors are always written.
        /// </summary>
        public bool QuietLogging { get; set; }

        private int QuietDiagnosticsWritten { get; set; }
        private const int MaximumQuietDiagnostics = 8;

        public int PlannedUpdated { get; set; }
        public int PlannedCreated { get; set; }
        public int PlannedConceptsCreated { get; set; }
        public int PlannedRelationshipsCreated { get; set; }
        public int PlannedDeleted { get; set; }
        public int PlannedSkipped { get; set; }

        public int AppliedUpdated { get; set; }
        public int AppliedCreated { get; set; }
        public int AppliedConceptsCreated { get; set; }
        public int AppliedRelationshipsCreated { get; set; }
        public int AppliedDeleted { get; set; }
        public int AppliedSkipped { get; set; }

        public int PlannedVisualsPlaced { get; set; }
        public int PlannedVisualsSkipped { get; set; }

        public int AppliedVisualsPlaced { get; set; }
        public int AppliedVisualsSkipped { get; set; }

        public int PlannedRepairedRelationships { get; set; }
        public int AppliedRepairedRelationships { get; set; }

        public int PlannedRepairedRecursiveVisuals { get; set; }
        public int AppliedRepairedRecursiveVisuals { get; set; }

        public int PlannedRepairedInvalidVisuals { get; set; }
        public int AppliedRepairedInvalidVisuals { get; set; }

        public int PlannedAutoFitConcepts { get; set; }
        public int AppliedAutoFitConcepts { get; set; }
        public int SkippedAutoFitConcepts { get; set; }

        public int PlannedAutoRouteLinks { get; set; }
        public int AppliedAutoRouteLinks { get; set; }
        public int SkippedAutoRouteLinks { get; set; }
        public int DoglegRoutedLinks { get; set; }
        public int RelationshipCentersInspected { get; set; }
        public int RelationshipCentersRecomputed { get; set; }
        public int RelationshipCentersPreserved { get; set; }
        public int SuspiciousRelationshipCenters { get; set; }
        public int RelationshipCentersSkipped { get; set; }
        public int GroupsPlanned { get; set; }
        public int GroupsCreated { get; set; }
        public int GroupsUpdated { get; set; }
        public int VisualsSuppressedByExplicitControl { get; set; }
        public int RelationshipsHiddenOrDeferredByExplicitControl { get; set; }
        public int ArrangementExclusionsByExplicitControl { get; set; }
        public int RoutingExclusionsByExplicitControl { get; set; }
        public string VisualStrategyMode { get; set; }
        public int VisualsSuppressedByStrategy { get; set; }
        public int AutoFitDeferredByStrategy { get; set; }
        public int AutoRouteDeferredByStrategy { get; set; }
        public bool ViewRefreshDeferredByStrategy { get; set; }
        public int RelationshipCompatibilitySkipped { get; set; }
        public int DetailsSkipped { get; set; }
        public bool CompatibilityBlocked { get; set; }
        public string CompatibilityBlockReason { get; set; }

        public int CurrentOperationIndex { get; set; }
        public int CurrentOperationTotal { get; set; }
        public string CurrentOperationSummary { get; set; }

        public List<string> Warnings { get; private set; }
        public List<string> SourceWarnings { get; private set; }
        public List<string> ImportWarnings { get; private set; }
        public List<string> Notes { get; private set; }
        public List<string> SkippedMessages { get; private set; }
        public List<string> Errors { get; private set; }
        public List<string> InfoLogLines { get; private set; }
        public List<string> AffectedViewNames { get; private set; }

        public int Updated
        {
            get { return this.IsPreview ? this.PlannedUpdated : this.AppliedUpdated; }
            set
            {
                if (this.IsPreview)
                    this.PlannedUpdated = value;
                else
                    this.AppliedUpdated = value;
            }
        }

        public int Created
        {
            get { return this.IsPreview ? this.PlannedCreated : this.AppliedCreated; }
            set
            {
                if (this.IsPreview)
                    this.PlannedCreated = value;
                else
                    this.AppliedCreated = value;
            }
        }

        public int Deleted
        {
            get { return this.IsPreview ? this.PlannedDeleted : this.AppliedDeleted; }
            set
            {
                if (this.IsPreview)
                    this.PlannedDeleted = value;
                else
                    this.AppliedDeleted = value;
            }
        }

        public int Skipped
        {
            get { return this.IsPreview ? this.PlannedSkipped : this.AppliedSkipped; }
            set
            {
                if (this.IsPreview)
                    this.PlannedSkipped = value;
                else
                    this.AppliedSkipped = value;
            }
        }

        public bool HasWarnings { get { return this.Warnings.Count > 0; } }
        public bool HasImportWarnings { get { return this.ImportWarnings.Count > 0; } }
        public bool HasErrors { get { return this.Errors.Count > 0; } }
        public bool HasRiskyChanges { get { return this.Created > 0 || this.Deleted > 0 || this.Updated > 25; } }
        public bool HasAppliedChanges { get { return this.AppliedUpdated > 0 || this.AppliedCreated > 0 || this.AppliedDeleted > 0 || this.AppliedVisualsPlaced > 0 || this.AppliedRepairedRelationships > 0 || this.AppliedRepairedRecursiveVisuals > 0 || this.AppliedRepairedInvalidVisuals > 0 || this.AppliedAutoFitConcepts > 0 || this.AppliedAutoRouteLinks > 0 || this.RelationshipCentersRecomputed > 0 || this.GroupsCreated > 0 || this.GroupsUpdated > 0; } }

        public void Log(string message)
        {
            if (String.IsNullOrEmpty(message))
                return;

            if (!this.QuietLogging)
            {
                this.InfoLogLines.Add(message);
                Console.WriteLine(message);
            }
        }

        public void Warn(string warning)
        {
            this.ImportWarning(warning);
        }

        public void SourceWarning(string warning)
        {
            if (String.IsNullOrEmpty(warning))
                return;

            this.SourceWarnings.Add(warning);
            this.Warnings.Add(warning);
            this.Log("JSON import source warning: " + warning);
            this.WriteQuietDiagnostic("JSON import source warning: " + warning);
        }

        public void ImportWarning(string warning)
        {
            if (String.IsNullOrEmpty(warning))
                return;

            this.ImportWarnings.Add(warning);
            this.Warnings.Add(warning);
            this.Log("JSON import warning: " + warning);
            this.WriteQuietDiagnostic("JSON import warning: " + warning);
        }

        public void Note(string message)
        {
            if (String.IsNullOrEmpty(message))
                return;

            this.Notes.Add(message);
            this.Log("JSON import note: " + message);
        }

        public void SkippedMessage(string message)
        {
            if (String.IsNullOrEmpty(message))
                return;

            this.SkippedMessages.Add(message);
            this.Warnings.Add(message);
            this.Log("JSON import skipped: " + message);
            this.WriteQuietDiagnostic("JSON import skipped: " + message);
        }

        public void Error(string error)
        {
            if (String.IsNullOrEmpty(error))
                return;

            this.Errors.Add(error);
            this.Log("JSON import error: " + error);
            if (this.QuietLogging)
                Console.WriteLine("JSON import error: " + error);
        }

        private void WriteQuietDiagnostic(string message)
        {
            if (!this.QuietLogging || this.QuietDiagnosticsWritten >= MaximumQuietDiagnostics)
                return;

            Console.WriteLine(message);
            this.QuietDiagnosticsWritten++;
            if (this.QuietDiagnosticsWritten == MaximumQuietDiagnostics)
                Console.WriteLine("JSON persistence rehydration: further warnings/skips are retained in the summary but omitted from the live log.");
        }

        public void CountUpdated()
        {
            this.Updated++;
        }

        public void CountCreated()
        {
            this.Created++;
        }

        public void CountCreatedConcept()
        {
            this.CountCreated();
            if (this.IsPreview)
                this.PlannedConceptsCreated++;
            else
                this.AppliedConceptsCreated++;
        }

        public void CountCreatedRelationship()
        {
            this.CountCreated();
            if (this.IsPreview)
                this.PlannedRelationshipsCreated++;
            else
                this.AppliedRelationshipsCreated++;
        }

        public void CountDeleted()
        {
            this.Deleted++;
        }

        public void CountSkipped()
        {
            this.Skipped++;
        }

        public void CountVisualPlaced()
        {
            if (this.IsPreview)
                this.PlannedVisualsPlaced++;
            else
                this.AppliedVisualsPlaced++;
        }

        public void CountVisualSkipped()
        {
            if (this.IsPreview)
                this.PlannedVisualsSkipped++;
            else
                this.AppliedVisualsSkipped++;
        }

        public void CountRepairedRelationship()
        {
            if (this.IsPreview)
                this.PlannedRepairedRelationships++;
            else
                this.AppliedRepairedRelationships++;
        }

        public void CountRepairedRecursiveVisual()
        {
            if (this.IsPreview)
                this.PlannedRepairedRecursiveVisuals++;
            else
                this.AppliedRepairedRecursiveVisuals++;
        }

        public void CountRepairedInvalidVisual()
        {
            if (this.IsPreview)
                this.PlannedRepairedInvalidVisuals++;
            else
                this.AppliedRepairedInvalidVisuals++;
        }

        public void CountAutoFitConcept()
        {
            if (this.IsPreview)
                this.PlannedAutoFitConcepts++;
            else
                this.AppliedAutoFitConcepts++;
        }

        public void CountAutoFitConceptSkipped()
        {
            this.SkippedAutoFitConcepts++;
        }

        public void CountAutoRouteLink()
        {
            if (this.IsPreview)
                this.PlannedAutoRouteLinks++;
            else
                this.AppliedAutoRouteLinks++;
        }

        public void CountAutoRouteLinkSkipped()
        {
            this.SkippedAutoRouteLinks++;
        }

        public void CountDoglegRoutedLink()
        {
            this.DoglegRoutedLinks++;
        }

        public void AddRelationshipCenterPlacement(RelationshipVisualPlacementResult Result)
        {
            if (Result == null)
                return;

            this.RelationshipCentersInspected += Result.RelationshipCentersInspected;
            this.RelationshipCentersRecomputed += Result.RelationshipCentersRecomputed;
            this.RelationshipCentersPreserved += Result.RelationshipCentersPreserved;
            this.SuspiciousRelationshipCenters += Result.SuspiciousRelationshipCenters;
            this.RelationshipCentersSkipped += Result.RelationshipCentersSkipped;
        }

        public void CountGroupPlanned()
        {
            this.GroupsPlanned++;
        }

        public void CountGroupCreated()
        {
            this.GroupsCreated++;
        }

        public void CountGroupUpdated()
        {
            this.GroupsUpdated++;
        }

        public void CountVisualSuppressedByExplicitControl()
        {
            this.VisualsSuppressedByExplicitControl++;
        }

        public void CountRelationshipHiddenOrDeferredByExplicitControl()
        {
            this.RelationshipsHiddenOrDeferredByExplicitControl++;
        }

        public void CountArrangementExclusionByExplicitControl()
        {
            this.ArrangementExclusionsByExplicitControl++;
        }

        public void CountRoutingExclusionByExplicitControl()
        {
            this.RoutingExclusionsByExplicitControl++;
        }

        public void CountRelationshipCompatibilitySkipped()
        {
            this.RelationshipCompatibilitySkipped++;
        }

        public void CountDetailSkipped()
        {
            this.DetailsSkipped++;
        }

        public void AddAffectedView(string viewName)
        {
            if (String.IsNullOrEmpty(viewName) || this.AffectedViewNames.Contains(viewName))
                return;

            this.AffectedViewNames.Add(viewName);
        }

        public void CopyPlanFrom(CompositionJsonImportReport preview)
        {
            if (preview == null)
                return;

            this.PlannedUpdated = preview.PlannedUpdated;
            this.PlannedCreated = preview.PlannedCreated;
            this.PlannedConceptsCreated = preview.PlannedConceptsCreated;
            this.PlannedRelationshipsCreated = preview.PlannedRelationshipsCreated;
            this.PlannedDeleted = preview.PlannedDeleted;
            this.PlannedSkipped = preview.PlannedSkipped;
            this.PlannedVisualsPlaced = preview.PlannedVisualsPlaced;
            this.PlannedVisualsSkipped = preview.PlannedVisualsSkipped;
            this.PlannedRepairedRelationships = preview.PlannedRepairedRelationships;
            this.PlannedRepairedRecursiveVisuals = preview.PlannedRepairedRecursiveVisuals;
            this.PlannedRepairedInvalidVisuals = preview.PlannedRepairedInvalidVisuals;
            this.PlannedAutoFitConcepts = preview.PlannedAutoFitConcepts;
            this.PlannedAutoRouteLinks = preview.PlannedAutoRouteLinks;
            this.RelationshipCentersInspected = preview.RelationshipCentersInspected;
            this.RelationshipCentersRecomputed = preview.RelationshipCentersRecomputed;
            this.RelationshipCentersPreserved = preview.RelationshipCentersPreserved;
            this.SuspiciousRelationshipCenters = preview.SuspiciousRelationshipCenters;
            this.RelationshipCentersSkipped = preview.RelationshipCentersSkipped;
            this.GroupsPlanned = preview.GroupsPlanned;
            this.VisualsSuppressedByExplicitControl = preview.VisualsSuppressedByExplicitControl;
            this.RelationshipsHiddenOrDeferredByExplicitControl = preview.RelationshipsHiddenOrDeferredByExplicitControl;
            this.ArrangementExclusionsByExplicitControl = preview.ArrangementExclusionsByExplicitControl;
            this.RoutingExclusionsByExplicitControl = preview.RoutingExclusionsByExplicitControl;
            this.VisualStrategyMode = preview.VisualStrategyMode;
            this.VisualsSuppressedByStrategy = preview.VisualsSuppressedByStrategy;
            this.AutoFitDeferredByStrategy = preview.AutoFitDeferredByStrategy;
            this.AutoRouteDeferredByStrategy = preview.AutoRouteDeferredByStrategy;
            this.ViewRefreshDeferredByStrategy = preview.ViewRefreshDeferredByStrategy;
            this.RelationshipCompatibilitySkipped = preview.RelationshipCompatibilitySkipped;
            this.DetailsSkipped = preview.DetailsSkipped;
            this.CompatibilityBlocked = preview.CompatibilityBlocked;
            this.CompatibilityBlockReason = preview.CompatibilityBlockReason;
        }

        public string ToSummaryString(bool IncludeWarnings)
        {
            var Text = new StringBuilder();
            if (this.CompatibilityBlocked)
            {
                Text.AppendLine("Import blocked by compatibility policy.");
                if (!String.IsNullOrWhiteSpace(this.CompatibilityBlockReason))
                    Text.AppendLine(this.CompatibilityBlockReason);
                Text.AppendLine();
            }

            Text.AppendLine("Updated: " + this.Updated);
            Text.AppendLine("Created: " + this.Created);
            Text.AppendLine("Concepts created: " + (this.IsPreview ? this.PlannedConceptsCreated : this.AppliedConceptsCreated));
            Text.AppendLine("Relationships created: " + (this.IsPreview ? this.PlannedRelationshipsCreated : this.AppliedRelationshipsCreated));
            Text.AppendLine("Deleted: " + this.Deleted);
            Text.AppendLine("Skipped: " + this.Skipped);
            Text.AppendLine("Relationships skipped by compatibility: " + this.RelationshipCompatibilitySkipped);
            Text.AppendLine("Details skipped: " + this.DetailsSkipped);
            Text.AppendLine("Relationships repaired: " + (this.IsPreview ? this.PlannedRepairedRelationships : this.AppliedRepairedRelationships));
            Text.AppendLine("Recursive visuals repaired: " + (this.IsPreview ? this.PlannedRepairedRecursiveVisuals : this.AppliedRepairedRecursiveVisuals));
            Text.AppendLine("Invalid visuals repaired: " + (this.IsPreview ? this.PlannedRepairedInvalidVisuals : this.AppliedRepairedInvalidVisuals));
            Text.AppendLine("Visuals placed: " + (this.IsPreview ? this.PlannedVisualsPlaced : this.AppliedVisualsPlaced));
            Text.AppendLine("Visuals not placed: " + (this.IsPreview ? this.PlannedVisualsSkipped : this.AppliedVisualsSkipped));
            Text.AppendLine("Concepts auto-fit: " + (this.IsPreview ? this.PlannedAutoFitConcepts : this.AppliedAutoFitConcepts));
            Text.AppendLine("Concepts not auto-fit: " + this.SkippedAutoFitConcepts);
            Text.AppendLine("Links routed: " + (this.IsPreview ? this.PlannedAutoRouteLinks : this.AppliedAutoRouteLinks));
            Text.AppendLine("Links not routed: " + this.SkippedAutoRouteLinks);
            Text.AppendLine("Relationship centers inspected: " + this.RelationshipCentersInspected);
            Text.AppendLine("Relationship centers recomputed: " + this.RelationshipCentersRecomputed);
            Text.AppendLine("Suspicious relationship centers: " + this.SuspiciousRelationshipCenters);
            Text.AppendLine("Groups planned/created/updated: " + this.GroupsPlanned + "/" + this.GroupsCreated + "/" + this.GroupsUpdated);
            Text.AppendLine("Visuals suppressed by explicit controls: " + this.VisualsSuppressedByExplicitControl);
            Text.AppendLine("Relationships hidden/deferred by explicit controls: " + this.RelationshipsHiddenOrDeferredByExplicitControl);
            Text.AppendLine("Routing exclusions by explicit controls: " + this.RoutingExclusionsByExplicitControl);
            if (!String.IsNullOrEmpty(this.VisualStrategyMode))
            {
                Text.AppendLine("Visual strategy: " + this.VisualStrategyMode);
                Text.AppendLine("Visuals suppressed by strategy: " + this.VisualsSuppressedByStrategy);
                Text.AppendLine("Auto-fit deferred by strategy: " + this.AutoFitDeferredByStrategy);
                Text.AppendLine("Auto-route deferred by strategy: " + this.AutoRouteDeferredByStrategy);
                Text.AppendLine("View refresh deferred by strategy: " + (this.ViewRefreshDeferredByStrategy ? "yes" : "no"));
            }
            Text.AppendLine("Source warnings: " + this.SourceWarnings.Count);
            Text.AppendLine("Import warnings: " + this.ImportWarnings.Count);
            Text.AppendLine("Notes: " + this.Notes.Count);
            Text.AppendLine("Errors: " + this.Errors.Count);

            if (!this.IsPreview && this.AffectedViewNames.Count > 0)
            {
                Text.AppendLine();
                Text.AppendLine("Visuals placed in:");
                foreach (var ViewName in this.AffectedViewNames.Take(8))
                    Text.AppendLine("- " + ViewName);

                if (this.AffectedViewNames.Count > 8)
                    Text.AppendLine("- ... " + (this.AffectedViewNames.Count - 8) + " more");
            }

            if (IncludeWarnings && (this.SourceWarnings.Count + this.ImportWarnings.Count + this.SkippedMessages.Count + this.Errors.Count) > 0)
            {
                AppendPreviewLines(Text, "Source warnings", this.SourceWarnings, 6);
                AppendPreviewLines(Text, "Import warnings", this.ImportWarnings, 8);
                AppendPreviewLines(Text, "Skipped operations", this.SkippedMessages, 6);
                AppendPreviewLines(Text, "Errors", this.Errors, 6);
            }

            return Text.ToString();
        }

        public string ToDetailedCountsString()
        {
            return "planned updated=" + this.PlannedUpdated +
                   ", created=" + this.PlannedCreated +
                   ", concepts created=" + this.PlannedConceptsCreated +
                   ", relationships created=" + this.PlannedRelationshipsCreated +
                   ", deleted=" + this.PlannedDeleted +
                   ", skipped=" + this.PlannedSkipped +
                   ", repaired relationships=" + this.PlannedRepairedRelationships +
                   ", repaired recursive visuals=" + this.PlannedRepairedRecursiveVisuals +
                   ", repaired invalid visuals=" + this.PlannedRepairedInvalidVisuals +
                   ", visuals placed=" + this.PlannedVisualsPlaced +
                   ", visuals skipped=" + this.PlannedVisualsSkipped +
                   ", auto-fit concepts=" + this.PlannedAutoFitConcepts +
                   ", auto-route links=" + this.PlannedAutoRouteLinks +
                   ", relationship centers inspected=" + this.RelationshipCentersInspected +
                   ", relationship centers recomputed=" + this.RelationshipCentersRecomputed +
                   ", suspicious relationship centers=" + this.SuspiciousRelationshipCenters +
                   ", groups planned=" + this.GroupsPlanned +
                   ", visual controls suppressed=" + this.VisualsSuppressedByExplicitControl +
                   ", relationship controls hidden/deferred=" + this.RelationshipsHiddenOrDeferredByExplicitControl +
                   ", routing controls excluded=" + this.RoutingExclusionsByExplicitControl +
                   ", visual strategy=" + (String.IsNullOrEmpty(this.VisualStrategyMode) ? "<none>" : this.VisualStrategyMode) +
                   ", visuals suppressed by strategy=" + this.VisualsSuppressedByStrategy +
                   ", auto-fit deferred by strategy=" + this.AutoFitDeferredByStrategy +
                   ", auto-route deferred by strategy=" + this.AutoRouteDeferredByStrategy +
                   ", view refresh deferred by strategy=" + (this.ViewRefreshDeferredByStrategy ? "true" : "false") +
                   "; applied updated=" + this.AppliedUpdated +
                   ", created=" + this.AppliedCreated +
                   ", concepts created=" + this.AppliedConceptsCreated +
                   ", relationships created=" + this.AppliedRelationshipsCreated +
                   ", deleted=" + this.AppliedDeleted +
                   ", skipped=" + this.AppliedSkipped +
                   ", repaired relationships=" + this.AppliedRepairedRelationships +
                   ", repaired recursive visuals=" + this.AppliedRepairedRecursiveVisuals +
                   ", repaired invalid visuals=" + this.AppliedRepairedInvalidVisuals +
                   ", visuals placed=" + this.AppliedVisualsPlaced +
                   ", visuals skipped=" + this.AppliedVisualsSkipped +
                   ", auto-fit concepts=" + this.AppliedAutoFitConcepts +
                   ", auto-fit skipped=" + this.SkippedAutoFitConcepts +
                   ", auto-route links=" + this.AppliedAutoRouteLinks +
                   ", auto-route skipped=" + this.SkippedAutoRouteLinks +
                   ", dogleg routed links=" + this.DoglegRoutedLinks +
                   ", relationship centers inspected=" + this.RelationshipCentersInspected +
                   ", relationship centers recomputed=" + this.RelationshipCentersRecomputed +
                   ", relationship centers preserved=" + this.RelationshipCentersPreserved +
                   ", relationship centers skipped=" + this.RelationshipCentersSkipped +
                   ", suspicious relationship centers=" + this.SuspiciousRelationshipCenters +
                   ", groups created=" + this.GroupsCreated +
                   ", groups updated=" + this.GroupsUpdated +
                   ", visual controls suppressed=" + this.VisualsSuppressedByExplicitControl +
                   ", relationship controls hidden/deferred=" + this.RelationshipsHiddenOrDeferredByExplicitControl +
                   ", routing controls excluded=" + this.RoutingExclusionsByExplicitControl +
                   ", relationship compatibility skipped=" + this.RelationshipCompatibilitySkipped +
                   ", details skipped=" + this.DetailsSkipped +
                   "; source warnings=" + this.SourceWarnings.Count +
                   ", import warnings=" + this.ImportWarnings.Count +
                   ", notes=" + this.Notes.Count +
                   ", errors=" + this.Errors.Count;
        }

        private static void AppendPreviewLines(StringBuilder Text, string Title, IList<string> Messages, int Limit)
        {
            if (Messages == null || Messages.Count < 1)
                return;

            Text.AppendLine();
            Text.AppendLine(Title + ":");
            var Count = Math.Min(Messages.Count, Limit);
            for (int Index = 0; Index < Count; Index++)
                Text.AppendLine("- " + Messages[Index]);

            if (Messages.Count > Count)
                Text.AppendLine("- ...and " + (Messages.Count - Count) + " more.");
        }
    }
}
