// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Warning normalization for Domain JSON import/export.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Instrumind.ThinkComposer.Definitor.DomainJsonInterchange
{
    public static class DomainJsonWarningFormatter
    {
        public static string Format(object Warning, string Path)
        {
            if (Warning == null)
                return Path + ": null";

            var Text = Warning as string;
            if (Text != null)
                return String.IsNullOrEmpty(Path) ? Text : Path + ": " + Text;

            var Dictionary = Warning as IDictionary<string, object>;
            if (Dictionary != null)
                return Path + ": " + FormatDictionary(Dictionary);

            var Items = Warning as IEnumerable;
            if (Items != null)
                return Path + ": [" + String.Join(", ", Items.Cast<object>().Select(Item => FormatValue(Item))) + "]";

            return Path + ": " + Convert.ToString(Warning, CultureInfo.InvariantCulture);
        }

        private static string FormatDictionary(IDictionary<string, object> Source)
        {
            return "{" + String.Join(", ", Source.OrderBy(Pair => Pair.Key)
                                           .Select(Pair => Pair.Key + "=" + FormatValue(Pair.Value))) + "}";
        }

        private static string FormatValue(object Value)
        {
            if (Value == null)
                return "null";

            var Text = Value as string;
            if (Text != null)
                return "\"" + Text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

            var Dictionary = Value as IDictionary<string, object>;
            if (Dictionary != null)
                return FormatDictionary(Dictionary);

            var Items = Value as IEnumerable;
            if (Items != null && !(Value is string))
                return "[" + String.Join(", ", Items.Cast<object>().Select(Item => FormatValue(Item))) + "]";

            return Convert.ToString(Value, CultureInfo.InvariantCulture);
        }
    }
}
