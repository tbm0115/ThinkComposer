// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// JSON interchange import report.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;

namespace Instrumind.ThinkComposer.Composer.JsonInterchange
{
    public class CompositionJsonImportReport
    {
        public CompositionJsonImportReport()
        {
            this.Warnings = new List<string>();
            this.Errors = new List<string>();
            this.InfoLogLines = new List<string>();
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

        public int CurrentOperationIndex { get; set; }
        public int CurrentOperationTotal { get; set; }
        public string CurrentOperationSummary { get; set; }

        public List<string> Warnings { get; private set; }
        public List<string> Errors { get; private set; }
        public List<string> InfoLogLines { get; private set; }

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
        public bool HasErrors { get { return this.Errors.Count > 0; } }
        public bool HasRiskyChanges { get { return this.Created > 0 || this.Deleted > 0 || this.Updated > 25; } }
        public bool HasAppliedChanges { get { return this.AppliedUpdated > 0 || this.AppliedCreated > 0 || this.AppliedDeleted > 0 || this.AppliedVisualsPlaced > 0; } }

        public void Log(string message)
        {
            if (String.IsNullOrEmpty(message))
                return;

            this.InfoLogLines.Add(message);
            Console.WriteLine(message);
        }

        public void Warn(string warning)
        {
            if (String.IsNullOrEmpty(warning))
                return;

            this.Warnings.Add(warning);
            this.Log("JSON import warning: " + warning);
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

        public string ToSummaryString(bool IncludeWarnings)
        {
            var Text = new StringBuilder();
            Text.AppendLine("Updated: " + this.Updated);
            Text.AppendLine("Created: " + this.Created);
            Text.AppendLine("Deleted: " + this.Deleted);
            Text.AppendLine("Skipped: " + this.Skipped);
            Text.AppendLine("Visuals placed: " + (this.IsPreview ? this.PlannedVisualsPlaced : this.AppliedVisualsPlaced));
            Text.AppendLine("Visuals not placed: " + (this.IsPreview ? this.PlannedVisualsSkipped : this.AppliedVisualsSkipped));
            Text.AppendLine("Warnings: " + this.Warnings.Count);

            if (IncludeWarnings && this.Warnings.Count > 0)
            {
                Text.AppendLine();
                Text.AppendLine("Warnings:");
                var Limit = Math.Min(this.Warnings.Count, 12);
                for (int Index = 0; Index < Limit; Index++)
                    Text.AppendLine("- " + this.Warnings[Index]);

                if (this.Warnings.Count > Limit)
                    Text.AppendLine("- ...and " + (this.Warnings.Count - Limit) + " more.");
            }

            return Text.ToString();
        }

        public string ToDetailedCountsString()
        {
            return "planned updated=" + this.PlannedUpdated +
                   ", created=" + this.PlannedCreated +
                   ", deleted=" + this.PlannedDeleted +
                   ", skipped=" + this.PlannedSkipped +
                   ", visuals placed=" + this.PlannedVisualsPlaced +
                   ", visuals skipped=" + this.PlannedVisualsSkipped +
                   "; applied updated=" + this.AppliedUpdated +
                   ", created=" + this.AppliedCreated +
                   ", deleted=" + this.AppliedDeleted +
                   ", skipped=" + this.AppliedSkipped +
                   ", visuals placed=" + this.AppliedVisualsPlaced +
                   ", visuals skipped=" + this.AppliedVisualsSkipped +
                   "; warnings=" + this.Warnings.Count +
                   ", errors=" + this.Errors.Count;
        }
    }
}
