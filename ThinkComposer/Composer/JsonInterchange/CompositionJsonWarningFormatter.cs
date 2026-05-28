// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Formats warning values found in JSON interchange documents.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Instrumind.ThinkComposer.Composer.JsonInterchange
{
    public static class CompositionJsonWarningFormatter
    {
        public static string Format(object Warning, string Path)
        {
            var Prefix = String.IsNullOrEmpty(Path) ? "" : Path + ": ";
            return Prefix + FormatValue(Warning);
        }

        private static string FormatValue(object Value)
        {
            if (Value == null)
                return "null";

            var Text = Value as string;
            if (Text != null)
                return Text;

            var Dictionary = Value as IDictionary<string, object>;
            if (Dictionary != null)
                return FormatDictionary(Dictionary);

            var UntypedDictionary = Value as IDictionary;
            if (UntypedDictionary != null)
            {
                var Converted = new Dictionary<string, object>();
                foreach (DictionaryEntry Entry in UntypedDictionary)
                    Converted[Convert.ToString(Entry.Key, CultureInfo.InvariantCulture)] = Entry.Value;

                return FormatDictionary(Converted);
            }

            var Items = Value as IEnumerable;
            if (Items != null)
            {
                var Parts = new List<string>();
                foreach (var Item in Items)
                    Parts.Add(FormatValue(Item));

                return "[" + String.Join(", ", Parts.ToArray()) + "]";
            }

            return Convert.ToString(Value, CultureInfo.InvariantCulture);
        }

        private static string FormatDictionary(IDictionary<string, object> Dictionary)
        {
            var Builder = new StringBuilder();
            Builder.Append("{");

            var Index = 0;
            foreach (var Pair in Dictionary.OrderBy(Pair => Pair.Key))
            {
                if (Index > 0)
                    Builder.Append(", ");

                Builder.Append(Pair.Key);
                Builder.Append("=");
                Builder.Append(FormatValue(Pair.Value));
                Index++;
            }

            Builder.Append("}");
            return Builder.ToString();
        }
    }
}
