// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Runtime reporting for Domain JSON preview/apply.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Instrumind.Common;

namespace Instrumind.ThinkComposer.Definitor.DomainJsonInterchange
{
    public class DomainJsonImportReport
    {
        public DomainJsonImportReport()
        {
            this.EntityCreated = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            this.EntityUpdated = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            this.EntityDeleted = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            this.EntitySkipped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            this.Warnings = new List<string>();
            this.SourceWarnings = new List<string>();
            this.ImportWarnings = new List<string>();
            this.Notes = new List<string>();
            this.SkippedMessages = new List<string>();
            this.DangerousSkippedMessages = new List<string>();
            this.Errors = new List<string>();
            this.LogLines = new List<string>();
            this.FieldUpdates = new List<string>();
        }

        public int PlannedCreated { get; set; }

        /// <summary>
        /// Suppresses high-volume field/informational logging for native package
        /// rehydration while preserving report counts and diagnostic collections.
        /// </summary>
        public bool QuietLogging { get; set; }

        private int QuietDiagnosticsWritten { get; set; }
        private const int MaximumQuietDiagnostics = 8;

        public int PlannedUpdated { get; set; }
        public int PlannedDeleted { get; set; }
        public int PlannedSkipped { get; set; }
        public int AppliedCreated { get; set; }
        public int AppliedUpdated { get; set; }
        public int AppliedDeleted { get; set; }
        public int AppliedSkipped { get; set; }
        public int DangerousChangesSkipped { get; set; }
        public int Conflicts { get; set; }
        public int LegacyRetained { get; set; }
        public int CurrentOperationIndex { get; set; }
        public string CurrentOperationSummary { get; set; }
        public Dictionary<string, int> EntityCreated { get; private set; }
        public Dictionary<string, int> EntityUpdated { get; private set; }
        public Dictionary<string, int> EntityDeleted { get; private set; }
        public Dictionary<string, int> EntitySkipped { get; private set; }
        public List<string> Warnings { get; private set; }
        public List<string> SourceWarnings { get; private set; }
        public List<string> ImportWarnings { get; private set; }
        public List<string> Notes { get; private set; }
        public List<string> SkippedMessages { get; private set; }
        public List<string> DangerousSkippedMessages { get; private set; }
        public List<string> Errors { get; private set; }
        public List<string> LogLines { get; private set; }
        public List<string> FieldUpdates { get; private set; }

        public void CountCreated(string Entity, bool IsPreview)
        {
            if (IsPreview)
                this.PlannedCreated++;
            else
                this.AppliedCreated++;

            Increment(this.EntityCreated, Entity);
        }

        public void CountUpdated(string Entity, bool IsPreview)
        {
            if (IsPreview)
                this.PlannedUpdated++;
            else
                this.AppliedUpdated++;

            Increment(this.EntityUpdated, Entity);
        }

        public void CountDeleted(string Entity, bool IsPreview)
        {
            if (IsPreview)
                this.PlannedDeleted++;
            else
                this.AppliedDeleted++;

            Increment(this.EntityDeleted, Entity);
        }

        public void CountSkipped(string Entity, bool IsPreview)
        {
            if (IsPreview)
                this.PlannedSkipped++;
            else
                this.AppliedSkipped++;

            Increment(this.EntitySkipped, Entity);
        }

        public void Warn(string Warning)
        {
            this.ImportWarning(Warning);
        }

        public void SourceWarning(string Warning)
        {
            if (String.IsNullOrWhiteSpace(Warning))
                return;

            this.SourceWarnings.Add(Warning);
            this.Warnings.Add(Warning);
            this.Log("Domain JSON source warning: " + Warning);
            this.WriteQuietDiagnostic("Domain JSON source warning: " + Warning);
        }

        public void ImportWarning(string Warning)
        {
            if (String.IsNullOrWhiteSpace(Warning))
                return;

            this.ImportWarnings.Add(Warning);
            this.Warnings.Add(Warning);
            this.Log("Domain JSON import warning: " + Warning);
            this.WriteQuietDiagnostic("Domain JSON import warning: " + Warning);
        }

        public void Note(string Message)
        {
            if (String.IsNullOrWhiteSpace(Message))
                return;

            this.Notes.Add(Message);
            this.Log("Domain JSON note: " + Message);
        }

        public void Skipped(string Message, bool IsDangerous = false)
        {
            if (String.IsNullOrWhiteSpace(Message))
                return;

            this.SkippedMessages.Add(Message);
            if (IsDangerous)
                this.DangerousSkippedMessages.Add(Message);

            this.Warnings.Add(Message);
            this.Log("Domain JSON skipped: " + Message);
            this.WriteQuietDiagnostic("Domain JSON skipped: " + Message);
        }

        public void Error(string Error)
        {
            if (String.IsNullOrWhiteSpace(Error))
                return;

            this.Errors.Add(Error);
            this.Log("Domain JSON error: " + Error);
            if (this.QuietLogging)
                Console.WriteLine("Domain JSON error: " + Error);
        }

        public void Log(string Message)
        {
            if (String.IsNullOrWhiteSpace(Message))
                return;

            if (!this.QuietLogging)
            {
                this.LogLines.Add(Message);
                Console.WriteLine(Message);
            }
        }

        public void LogFieldUpdate(string Entity, string FieldName, string Target, string MatchMethod, object OldValue, object NewValue, bool IsPreview)
        {
            if (this.QuietLogging)
                return;

            var Message = "Domain JSON " + (IsPreview ? "planned" : "applied") +
                          " field update: entity=" + Entity.ToStringAlways() +
                          " match=" + MatchMethod.ToStringAlways() +
                          " field=" + FieldName.ToStringAlways() +
                          " target=" + Target.ToStringAlways() +
                          " old='" + Compact(OldValue) + "'" +
                          " new='" + Compact(NewValue) + "'";

            this.FieldUpdates.Add(Message);
            this.Log(Message);
        }

        private void WriteQuietDiagnostic(string Message)
        {
            if (!this.QuietLogging || this.QuietDiagnosticsWritten >= MaximumQuietDiagnostics)
                return;

            Console.WriteLine(Message);
            this.QuietDiagnosticsWritten++;
            if (this.QuietDiagnosticsWritten == MaximumQuietDiagnostics)
                Console.WriteLine("Domain JSON persistence rehydration: further warnings/skips are retained in the summary but omitted from the live log.");
        }

        public string FieldUpdatePreview(int Maximum)
        {
            if (this.FieldUpdates.Count < 1)
                return "";

            var Lines = this.FieldUpdates.Take(Maximum).ToList();
            return String.Join("\n", Lines) + (this.FieldUpdates.Count > Maximum ? "\n..." : "");
        }

        public string PreviewSummary()
        {
            return "Planned created: " + this.PlannedCreated.ToString(CultureInfo.InvariantCulture) + "\n" +
                   "Planned updated: " + this.PlannedUpdated.ToString(CultureInfo.InvariantCulture) + "\n" +
                   "Planned deleted: " + this.PlannedDeleted.ToString(CultureInfo.InvariantCulture) + "\n" +
                   "Planned skipped: " + this.PlannedSkipped.ToString(CultureInfo.InvariantCulture) + "\n" +
                   "Dangerous skipped: " + this.DangerousChangesSkipped.ToString(CultureInfo.InvariantCulture) + "\n" +
                   "Source warnings: " + this.SourceWarnings.Count.ToString(CultureInfo.InvariantCulture) + "\n" +
                   "Import warnings: " + this.ImportWarnings.Count.ToString(CultureInfo.InvariantCulture) + "\n" +
                   "Notes: " + this.Notes.Count.ToString(CultureInfo.InvariantCulture) + "\n" +
                   "Errors: " + this.Errors.Count.ToString(CultureInfo.InvariantCulture);
        }

        public string ApplySummary()
        {
            return "Applied created: " + this.AppliedCreated.ToString(CultureInfo.InvariantCulture) + "\n" +
                   "Applied updated: " + this.AppliedUpdated.ToString(CultureInfo.InvariantCulture) + "\n" +
                   "Applied deleted: " + this.AppliedDeleted.ToString(CultureInfo.InvariantCulture) + "\n" +
                   "Applied skipped: " + this.AppliedSkipped.ToString(CultureInfo.InvariantCulture) + "\n" +
                   "Dangerous skipped: " + this.DangerousChangesSkipped.ToString(CultureInfo.InvariantCulture) + "\n" +
                   "Source warnings: " + this.SourceWarnings.Count.ToString(CultureInfo.InvariantCulture) + "\n" +
                   "Import warnings: " + this.ImportWarnings.Count.ToString(CultureInfo.InvariantCulture) + "\n" +
                   "Notes: " + this.Notes.Count.ToString(CultureInfo.InvariantCulture) + "\n" +
                   "Errors: " + this.Errors.Count.ToString(CultureInfo.InvariantCulture);
        }

        public string EntitySummary()
        {
            var Names = this.EntityCreated.Keys
                .Concat(this.EntityUpdated.Keys)
                .Concat(this.EntityDeleted.Keys)
                .Concat(this.EntitySkipped.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(Name => Name)
                .ToList();

            if (Names.Count < 1)
                return "<none>";

            return String.Join("; ", Names.Select(Name => Name + ": +" + GetCount(this.EntityCreated, Name).ToString(CultureInfo.InvariantCulture) +
                                                        " ~" + GetCount(this.EntityUpdated, Name).ToString(CultureInfo.InvariantCulture) +
                                                        " -" + GetCount(this.EntityDeleted, Name).ToString(CultureInfo.InvariantCulture) +
                                                        " skip " + GetCount(this.EntitySkipped, Name).ToString(CultureInfo.InvariantCulture)));
        }

        private static void Increment(Dictionary<string, int> Target, string Key)
        {
            Key = String.IsNullOrWhiteSpace(Key) ? "unknown" : Key;
            if (!Target.ContainsKey(Key))
                Target[Key] = 0;
            Target[Key]++;
        }

        private static int GetCount(Dictionary<string, int> Source, string Key)
        {
            int Result;
            return Source.TryGetValue(Key, out Result) ? Result : 0;
        }

        private static string Compact(object Value)
        {
            var Text = Value == null ? "" : Convert.ToString(Value, CultureInfo.InvariantCulture).NullDefault("");
            Text = Text.Replace("\r", "\\r").Replace("\n", "\\n");
            return Text.Length <= 160 ? Text : Text.Substring(0, 157) + "...";
        }
    }
}
