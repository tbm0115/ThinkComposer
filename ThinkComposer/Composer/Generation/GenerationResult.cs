using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Instrumind.Common;

namespace Instrumind.ThinkComposer.Composer.Generation
{
    public class GenerationResult
    {
        public GenerationResult(string FileName, string SourceText, bool PreferGeneratedFilename = true)
        {
            this.FileName = FileName.AbsentDefault(Guid.NewGuid().ToString() + GenerationManager.DEFAULT_GEN_EXT);

            var TextAndParameters = ExtractTextAndParameters(SourceText);

            this.GeneratedText = TextAndParameters.Item1;
            this.Parameters = TextAndParameters.Item2;

            if (PreferGeneratedFilename && TextAndParameters.Item2.ContainsKey(GenerationManager.GENKEY_VAR_FILENAME))
                this.FileName = TextAndParameters.Item2[GenerationManager.GENKEY_VAR_FILENAME];
        }

        public string FileName { get; set; }

        public string GeneratedText { get; private set; }

        public IDictionary<string, string> Parameters { get; private set; }

        public string DiagnosticsText { get; set; }

        public string ValidationSummary { get; set; }

        public void ReplaceGeneratedText(string GeneratedText)
        {
            this.GeneratedText = GeneratedText.NullDefault("");
        }

        private Tuple<string, Dictionary<string, string>> ExtractTextAndParameters(string SourceText)
        {
            SourceText = SourceText.NullDefault("");
            var Text = new StringBuilder(SourceText.Length);
            var Parameters = new Dictionary<string, string>();

            var Lines = General.ToStrings(SourceText);
            foreach (var Line in Lines)
                if (Line.TrimStart().StartsWith(GenerationManager.GENPAR_PREFIX))
                {
                    var Declaration = Line.Substring(Line.IndexOf(GenerationManager.GENPAR_PREFIX) +
                                                                  GenerationManager.GENPAR_PREFIX.Length).Segment(GenerationManager.GENPAR_ASSIGN);
                    // Notice that only variable assignments have two segments
                    if (Declaration.Length > 1)
                        Parameters[Declaration[0].Trim().ToUpper()] = Declaration[1];
                }
                else
                    Text.AppendLine(Line);

            var Result = Tuple.Create(Text.ToString(), Parameters);
            return Result;
        }
    }
}
