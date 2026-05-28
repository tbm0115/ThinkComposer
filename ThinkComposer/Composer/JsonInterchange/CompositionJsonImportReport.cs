// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// JSON interchange import report.
// -------------------------------------------------------------------------------------------

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
        }

        public int Updated { get; set; }
        public int Created { get; set; }
        public int Deleted { get; set; }
        public int Skipped { get; set; }
        public List<string> Warnings { get; private set; }
        public List<string> Errors { get; private set; }

        public bool HasWarnings { get { return this.Warnings.Count > 0; } }
        public bool HasErrors { get { return this.Errors.Count > 0; } }
        public bool HasRiskyChanges { get { return this.Created > 0 || this.Deleted > 0 || this.Updated > 25; } }

        public void Warn(string warning)
        {
            if (!string.IsNullOrEmpty(warning))
                this.Warnings.Add(warning);
        }

        public void Error(string error)
        {
            if (!string.IsNullOrEmpty(error))
                this.Errors.Add(error);
        }

        public string ToSummaryString(bool IncludeWarnings)
        {
            var Text = new StringBuilder();
            Text.AppendLine("Updated: " + this.Updated);
            Text.AppendLine("Created: " + this.Created);
            Text.AppendLine("Deleted: " + this.Deleted);
            Text.AppendLine("Skipped: " + this.Skipped);
            Text.AppendLine("Warnings: " + this.Warnings.Count);

            if (IncludeWarnings && this.Warnings.Count > 0)
            {
                Text.AppendLine();
                Text.AppendLine("Warnings:");
                var Limit = System.Math.Min(this.Warnings.Count, 12);
                for (int Index = 0; Index < Limit; Index++)
                    Text.AppendLine("- " + this.Warnings[Index]);

                if (this.Warnings.Count > Limit)
                    Text.AppendLine("- ...and " + (this.Warnings.Count - Limit) + " more.");
            }

            return Text.ToString();
        }
    }
}
