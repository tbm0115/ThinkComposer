// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// JSON interchange import report.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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

        public int PlannedUpdated { get; set; }
        public int PlannedCreated { get; set; }
        public int PlannedDeleted { get; set; }
        public int PlannedSkipped { get; set; }

        public int AppliedUpdated { get; set; }
        public int AppliedCreated { get; set; }
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

        public int PlannedAutoFitConcepts { get; set; }
        public int AppliedAutoFitConcepts { get; set; }
        public int SkippedAutoFitConcepts { get; set; }

        public int PlannedAutoRouteLinks { get; set; }
        public int AppliedAutoRouteLinks { get; set; }
        public int SkippedAutoRouteLinks { get; set; }
        public int DoglegRoutedLinks { get; set; }

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
        public bool HasAppliedChanges { get { return this.AppliedUpdated > 0 || this.AppliedCreated > 0 || this.AppliedDeleted > 0 || this.AppliedVisualsPlaced > 0 || this.AppliedRepairedRelationships > 0 || this.AppliedRepairedRecursiveVisuals > 0 || this.AppliedAutoFitConcepts > 0 || this.AppliedAutoRouteLinks > 0; } }

        public void Log(string message)
        {
            if (String.IsNullOrEmpty(message))
                return;

            this.InfoLogLines.Add(message);
            Console.WriteLine(message);
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
        }

        public void ImportWarning(string warning)
        {
            if (String.IsNullOrEmpty(warning))
                return;

            this.ImportWarnings.Add(warning);
            this.Warnings.Add(warning);
            this.Log("JSON import warning: " + warning);
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
        }

        public void Error(string error)
        {
            if (String.IsNullOrEmpty(error))
                return;

            this.Errors.Add(error);
            this.Log("JSON import error: " + error);
        }

        public void CountUpdated()
        {
            this.Updated++;
        }

        public void CountCreated()
        {
            this.Created++;
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
            this.PlannedDeleted = preview.PlannedDeleted;
            this.PlannedSkipped = preview.PlannedSkipped;
            this.PlannedVisualsPlaced = preview.PlannedVisualsPlaced;
            this.PlannedVisualsSkipped = preview.PlannedVisualsSkipped;
            this.PlannedRepairedRelationships = preview.PlannedRepairedRelationships;
            this.PlannedRepairedRecursiveVisuals = preview.PlannedRepairedRecursiveVisuals;
            this.PlannedAutoFitConcepts = preview.PlannedAutoFitConcepts;
            this.PlannedAutoRouteLinks = preview.PlannedAutoRouteLinks;
        }

        public string ToSummaryString(bool IncludeWarnings)
        {
            var Text = new StringBuilder();
            Text.AppendLine("Updated: " + this.Updated);
            Text.AppendLine("Created: " + this.Created);
            Text.AppendLine("Deleted: " + this.Deleted);
            Text.AppendLine("Skipped: " + this.Skipped);
            Text.AppendLine("Relationships repaired: " + (this.IsPreview ? this.PlannedRepairedRelationships : this.AppliedRepairedRelationships));
            Text.AppendLine("Recursive visuals repaired: " + (this.IsPreview ? this.PlannedRepairedRecursiveVisuals : this.AppliedRepairedRecursiveVisuals));
            Text.AppendLine("Visuals placed: " + (this.IsPreview ? this.PlannedVisualsPlaced : this.AppliedVisualsPlaced));
            Text.AppendLine("Visuals not placed: " + (this.IsPreview ? this.PlannedVisualsSkipped : this.AppliedVisualsSkipped));
            Text.AppendLine("Concepts auto-fit: " + (this.IsPreview ? this.PlannedAutoFitConcepts : this.AppliedAutoFitConcepts));
            Text.AppendLine("Concepts not auto-fit: " + this.SkippedAutoFitConcepts);
            Text.AppendLine("Links routed: " + (this.IsPreview ? this.PlannedAutoRouteLinks : this.AppliedAutoRouteLinks));
            Text.AppendLine("Links not routed: " + this.SkippedAutoRouteLinks);
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
                   ", deleted=" + this.PlannedDeleted +
                   ", skipped=" + this.PlannedSkipped +
                   ", repaired relationships=" + this.PlannedRepairedRelationships +
                   ", repaired recursive visuals=" + this.PlannedRepairedRecursiveVisuals +
                   ", visuals placed=" + this.PlannedVisualsPlaced +
                   ", visuals skipped=" + this.PlannedVisualsSkipped +
                   ", auto-fit concepts=" + this.PlannedAutoFitConcepts +
                   ", auto-route links=" + this.PlannedAutoRouteLinks +
                   "; applied updated=" + this.AppliedUpdated +
                   ", created=" + this.AppliedCreated +
                   ", deleted=" + this.AppliedDeleted +
                   ", skipped=" + this.AppliedSkipped +
                   ", repaired relationships=" + this.AppliedRepairedRelationships +
                   ", repaired recursive visuals=" + this.AppliedRepairedRecursiveVisuals +
                   ", visuals placed=" + this.AppliedVisualsPlaced +
                   ", visuals skipped=" + this.AppliedVisualsSkipped +
                   ", auto-fit concepts=" + this.AppliedAutoFitConcepts +
                   ", auto-fit skipped=" + this.SkippedAutoFitConcepts +
                   ", auto-route links=" + this.AppliedAutoRouteLinks +
                   ", auto-route skipped=" + this.SkippedAutoRouteLinks +
                   ", dogleg routed links=" + this.DoglegRoutedLinks +
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
