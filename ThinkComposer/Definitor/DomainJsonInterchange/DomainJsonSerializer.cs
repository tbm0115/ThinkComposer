// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Deterministic JSON writer plus tolerant reader for Domain JSON interchange DTOs.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Instrumind.ThinkComposer.Definitor.DomainJsonInterchange
{
    public static class DomainJsonSerializer
    {
        public static void Save(DomainJsonDocument Document, string FilePath)
        {
            File.WriteAllText(FilePath, Serialize(Document), Encoding.UTF8);
        }

        public static DomainJsonDocument Load(string FilePath)
        {
            return Deserialize(File.ReadAllText(FilePath, Encoding.UTF8));
        }

        public static string Serialize(DomainJsonDocument Document)
        {
            var Builder = new StringBuilder();
            WriteJsonValue(Builder, ToGraph(Document), 0);
            Builder.AppendLine();
            return Builder.ToString();
        }

        public static DomainJsonDocument Deserialize(string Text)
        {
            var Serializer = new JavaScriptSerializer();
            Serializer.MaxJsonLength = Int32.MaxValue;

            var Root = Serializer.DeserializeObject(Text) as IDictionary<string, object>;
            if (Root == null)
                throw new InvalidDataException("The Domain JSON file must contain an object at the root.");

            var Document = new DomainJsonDocument();
            Document.Format = GetString(Root, "format");
            Document.FormatVersion = GetInt(Root, "formatVersion", 0);
            Document.ExportedAtUtc = GetString(Root, "exportedAtUtc");
            Document.Application = GetString(Root, "application");
            Document.Domain = ReadElement(GetDictionary(Root, "domain"));
            Document.ExternalLanguages = ReadList(Root, "externalLanguages", ReadElement);
            Document.LinkRoleVariants = ReadList(Root, "linkRoleVariants", ReadElement);
            Document.ConceptDefinitionClusters = ReadList(Root, "conceptDefinitionClusters", ReadElement);
            Document.RelationshipDefinitionClusters = ReadList(Root, "relationshipDefinitionClusters", ReadElement);
            Document.MarkerClusters = ReadList(Root, "markerClusters", ReadElement);
            Document.MarkerDefinitions = ReadList(Root, "markerDefinitions", ReadElement);
            Document.TableDefinitionCategories = ReadList(Root, "tableDefinitionCategories", ReadElement);
            Document.FieldDefinitionCategories = ReadList(Root, "fieldDefinitionCategories", ReadElement);
            Document.TableDefinitions = ReadList(Root, "tableDefinitions", ReadElement);
            Document.ConceptDefinitions = ReadList(Root, "conceptDefinitions", ReadElement);
            Document.RelationshipDefinitions = ReadList(Root, "relationshipDefinitions", ReadElement);
            Document.ConceptDefinitionOutputTemplates = ReadList(Root, "conceptDefinitionOutputTemplates", ReadElement);
            Document.RelationshipDefinitionOutputTemplates = ReadList(Root, "relationshipDefinitionOutputTemplates", ReadElement);
            Document.RelationshipCompatibility = ReadList(Root, "relationshipCompatibility", ReadRelationshipCompatibility);
            Document.Operations = ReadList(Root, "operations", ReadOperation);
            Document.Warnings = ReadWarningList(Root, "warnings");
            return Document;
        }

        public static void Validate(DomainJsonDocument Document)
        {
            if (Document == null)
                throw new InvalidDataException("No Domain JSON document was loaded.");

            if (Document.Format != DomainJsonDocument.CurrentFormat)
                throw new InvalidDataException("Unsupported Domain JSON format. Expected '" + DomainJsonDocument.CurrentFormat + "'.");

            if (Document.FormatVersion != DomainJsonDocument.CurrentFormatVersion)
                throw new InvalidDataException("Unsupported Domain JSON formatVersion. Expected " + DomainJsonDocument.CurrentFormatVersion + ".");
        }

        private static object ToGraph(DomainJsonDocument Document)
        {
            var Obj = NewObject();
            Add(Obj, "format", Document.Format);
            Add(Obj, "formatVersion", Document.FormatVersion);
            AddIf(Obj, "exportedAtUtc", Document.ExportedAtUtc);
            Add(Obj, "application", Document.Application);
            AddIf(Obj, "domain", ToGraph(Document.Domain));
            Add(Obj, "externalLanguages", ToList(Document.ExternalLanguages, ToGraph));
            Add(Obj, "linkRoleVariants", ToList(Document.LinkRoleVariants, ToGraph));
            Add(Obj, "conceptDefinitionClusters", ToList(Document.ConceptDefinitionClusters, ToGraph));
            Add(Obj, "relationshipDefinitionClusters", ToList(Document.RelationshipDefinitionClusters, ToGraph));
            Add(Obj, "markerClusters", ToList(Document.MarkerClusters, ToGraph));
            Add(Obj, "markerDefinitions", ToList(Document.MarkerDefinitions, ToGraph));
            Add(Obj, "tableDefinitionCategories", ToList(Document.TableDefinitionCategories, ToGraph));
            Add(Obj, "fieldDefinitionCategories", ToList(Document.FieldDefinitionCategories, ToGraph));
            Add(Obj, "tableDefinitions", ToList(Document.TableDefinitions, ToGraph));
            Add(Obj, "conceptDefinitions", ToList(Document.ConceptDefinitions, ToGraph));
            Add(Obj, "relationshipDefinitions", ToList(Document.RelationshipDefinitions, ToGraph));
            Add(Obj, "conceptDefinitionOutputTemplates", ToList(Document.ConceptDefinitionOutputTemplates, ToGraph));
            Add(Obj, "relationshipDefinitionOutputTemplates", ToList(Document.RelationshipDefinitionOutputTemplates, ToGraph));
            Add(Obj, "relationshipCompatibility", ToList(Document.RelationshipCompatibility, ToGraph));
            Add(Obj, "operations", ToList(Document.Operations, ToGraph));
            Add(Obj, "warnings", Document.Warnings ?? new List<string>());
            return Obj;
        }

        private static object ToGraph(DomainJsonElement Element)
        {
            if (Element == null)
                return null;

            var Obj = NewObject();
            AddIf(Obj, "id", Element.Id);
            AddIf(Obj, "entity", Element.Entity);
            AddIf(Obj, "name", Element.Name);
            AddIf(Obj, "techName", Element.TechName);
            AddIf(Obj, "summary", Element.Summary);
            AddIf(Obj, "description", Element.Description);
            AddIf(Obj, "techSpec", Element.TechSpec);
            AddIf(Obj, "compatibilitySignature", Element.CompatibilitySignature);
            AddIf(Obj, "ownerId", Element.OwnerId);
            AddIf(Obj, "ownerTechName", Element.OwnerTechName);
            AddIf(Obj, "ownerScope", Element.OwnerScope);
            AddIf(Obj, "clusterTechName", Element.ClusterTechName);
            AddIf(Obj, "categoryTechName", Element.CategoryTechName);
            AddIf(Obj, "dataTypeTechName", Element.DataTypeTechName);
            AddIf(Obj, "representativeShape", Element.RepresentativeShape);
            AddIf(Obj, "ancestorTechName", Element.AncestorTechName);
            AddIf(Obj, "isComposable", Element.IsComposable);
            AddIf(Obj, "isVersionable", Element.IsVersionable);
            AddIf(Obj, "canAutomaticallyCreateRelatedConcepts", Element.CanAutomaticallyCreateRelatedConcepts);
            AddIf(Obj, "isDirectional", Element.IsDirectional);
            AddIf(Obj, "isSimple", Element.IsSimple);
            AddIf(Obj, "hideCentralSymbolWhenSimple", Element.HideCentralSymbolWhenSimple);
            AddIf(Obj, "showNameIfHidingCentralSymbol", Element.ShowNameIfHidingCentralSymbol);
            AddIf(Obj, "roleType", Element.RoleType);
            AddIf(Obj, "maxConnections", Element.MaxConnections);
            AddIf(Obj, "relatedIdeasAreOrdered", Element.RelatedIdeasAreOrdered);
            AddIf(Obj, "externalLanguageTechName", Element.ExternalLanguageTechName);
            AddIf(Obj, "templateText", Element.TemplateText);
            AddIf(Obj, "extendsBaseTemplate", Element.ExtendsBaseTemplate);
            AddIf(Obj, "order", Element.Order);
            AddIfNotEmpty(Obj, "allowedVariantTechNames", Element.AllowedVariantTechNames);
            AddIfNotEmpty(Obj, "associableIdeaDefinitionTechNames", Element.AssociableIdeaDefinitionTechNames);
            AddIfNotEmpty(Obj, "fields", ToList(Element.Fields, ToGraph));
            AddIfNotEmpty(Obj, "roleDefinitions", ToList(Element.RoleDefinitions, ToGraph));
            AddIfNotEmpty(Obj, "outputTemplates", ToList(Element.OutputTemplates, ToGraph));
            AddIfNotEmpty(Obj, "set", ToOrderedDictionary(Element.Set));
            return Obj;
        }

        private static object ToGraph(DomainJsonRelationshipCompatibility Compatibility)
        {
            if (Compatibility == null)
                return null;

            var Obj = NewObject();
            AddIf(Obj, "relationshipDefinitionId", Compatibility.RelationshipDefinitionId);
            AddIf(Obj, "relationshipDefinitionTechName", Compatibility.RelationshipDefinitionTechName);
            AddIf(Obj, "relationshipDefinitionName", Compatibility.RelationshipDefinitionName);
            AddIf(Obj, "originRoleTechName", Compatibility.OriginRoleTechName);
            AddIf(Obj, "originRoleName", Compatibility.OriginRoleName);
            AddIf(Obj, "targetRoleTechName", Compatibility.TargetRoleTechName);
            AddIf(Obj, "targetRoleName", Compatibility.TargetRoleName);
            AddIfNotEmpty(Obj, "allowedOriginConceptDefinitionTechNames", Compatibility.AllowedOriginConceptDefinitionTechNames);
            AddIfNotEmpty(Obj, "allowedTargetConceptDefinitionTechNames", Compatibility.AllowedTargetConceptDefinitionTechNames);
            AddIfNotEmpty(Obj, "allowedOriginVariantTechNames", Compatibility.AllowedOriginVariantTechNames);
            AddIfNotEmpty(Obj, "allowedTargetVariantTechNames", Compatibility.AllowedTargetVariantTechNames);
            AddIf(Obj, "isDirectional", Compatibility.IsDirectional);
            AddIf(Obj, "isSimple", Compatibility.IsSimple);
            AddIf(Obj, "hideCentralSymbolWhenSimple", Compatibility.HideCentralSymbolWhenSimple);
            return Obj;
        }

        private static object ToGraph(DomainJsonOperation Operation)
        {
            var Obj = NewObject();
            AddIf(Obj, "op", Operation.Op);
            AddIf(Obj, "entity", Operation.Entity);
            AddIf(Obj, "id", Operation.Id);
            AddIf(Obj, "techName", Operation.TechName);
            AddIf(Obj, "ownerId", Operation.OwnerId);
            AddIf(Obj, "ownerTechName", Operation.OwnerTechName);
            AddIf(Obj, "ownerScope", Operation.OwnerScope);
            AddIfNotEmpty(Obj, "set", ToOrderedDictionary(Operation.Set));
            return Obj;
        }

        private static DomainJsonElement ReadElement(IDictionary<string, object> Source)
        {
            if (Source == null)
                return null;

            var Result = new DomainJsonElement();
            Result.Id = GetString(Source, "id");
            Result.Entity = GetString(Source, "entity");
            Result.Name = GetString(Source, "name");
            Result.TechName = GetString(Source, "techName");
            Result.Summary = GetString(Source, "summary");
            Result.Description = GetString(Source, "description");
            Result.TechSpec = GetString(Source, "techSpec");
            Result.CompatibilitySignature = GetString(Source, "compatibilitySignature");
            Result.OwnerId = GetString(Source, "ownerId");
            Result.OwnerTechName = GetString(Source, "ownerTechName");
            Result.OwnerScope = GetString(Source, "ownerScope");
            Result.ClusterTechName = GetString(Source, "clusterTechName");
            Result.CategoryTechName = GetString(Source, "categoryTechName");
            Result.DataTypeTechName = GetString(Source, "dataTypeTechName");
            Result.RepresentativeShape = GetString(Source, "representativeShape");
            Result.AncestorTechName = GetString(Source, "ancestorTechName");
            Result.IsComposable = GetNullableBool(Source, "isComposable");
            Result.IsVersionable = GetNullableBool(Source, "isVersionable");
            Result.CanAutomaticallyCreateRelatedConcepts = GetNullableBool(Source, "canAutomaticallyCreateRelatedConcepts");
            Result.IsDirectional = GetNullableBool(Source, "isDirectional");
            Result.IsSimple = GetNullableBool(Source, "isSimple");
            Result.HideCentralSymbolWhenSimple = GetNullableBool(Source, "hideCentralSymbolWhenSimple");
            Result.ShowNameIfHidingCentralSymbol = GetNullableBool(Source, "showNameIfHidingCentralSymbol");
            Result.RoleType = GetString(Source, "roleType");
            Result.MaxConnections = GetNullableUInt(Source, "maxConnections");
            Result.RelatedIdeasAreOrdered = GetNullableBool(Source, "relatedIdeasAreOrdered");
            Result.ExternalLanguageTechName = GetString(Source, "externalLanguageTechName");
            Result.TemplateText = GetString(Source, "templateText");
            Result.ExtendsBaseTemplate = GetNullableBool(Source, "extendsBaseTemplate");
            Result.Order = GetNullableInt(Source, "order");
            Result.AllowedVariantTechNames = ReadStringList(Source, "allowedVariantTechNames");
            Result.AssociableIdeaDefinitionTechNames = ReadStringList(Source, "associableIdeaDefinitionTechNames");
            Result.Fields = ReadList(Source, "fields", ReadElement);
            Result.RoleDefinitions = ReadList(Source, "roleDefinitions", ReadElement);
            Result.OutputTemplates = ReadList(Source, "outputTemplates", ReadElement);
            Result.Set = GetObjectDictionary(Source, "set");
            return Result;
        }

        private static DomainJsonRelationshipCompatibility ReadRelationshipCompatibility(IDictionary<string, object> Source)
        {
            var Result = new DomainJsonRelationshipCompatibility();
            Result.RelationshipDefinitionId = GetString(Source, "relationshipDefinitionId");
            Result.RelationshipDefinitionTechName = GetString(Source, "relationshipDefinitionTechName");
            Result.RelationshipDefinitionName = GetString(Source, "relationshipDefinitionName");
            Result.OriginRoleTechName = GetString(Source, "originRoleTechName");
            Result.OriginRoleName = GetString(Source, "originRoleName");
            Result.TargetRoleTechName = GetString(Source, "targetRoleTechName");
            Result.TargetRoleName = GetString(Source, "targetRoleName");
            Result.AllowedOriginConceptDefinitionTechNames = ReadStringList(Source, "allowedOriginConceptDefinitionTechNames");
            Result.AllowedTargetConceptDefinitionTechNames = ReadStringList(Source, "allowedTargetConceptDefinitionTechNames");
            Result.AllowedOriginVariantTechNames = ReadStringList(Source, "allowedOriginVariantTechNames");
            Result.AllowedTargetVariantTechNames = ReadStringList(Source, "allowedTargetVariantTechNames");
            Result.IsDirectional = GetNullableBool(Source, "isDirectional");
            Result.IsSimple = GetNullableBool(Source, "isSimple");
            Result.HideCentralSymbolWhenSimple = GetNullableBool(Source, "hideCentralSymbolWhenSimple");
            return Result;
        }

        private static DomainJsonOperation ReadOperation(IDictionary<string, object> Source)
        {
            var Result = new DomainJsonOperation();
            Result.Op = GetString(Source, "op");
            Result.Entity = GetString(Source, "entity");
            Result.Id = GetString(Source, "id");
            Result.TechName = GetString(Source, "techName");
            Result.OwnerId = GetString(Source, "ownerId");
            Result.OwnerTechName = GetString(Source, "ownerTechName");
            Result.OwnerScope = GetString(Source, "ownerScope");
            Result.Set = GetObjectDictionary(Source, "set");
            return Result;
        }

        private static List<TTarget> ReadList<TTarget>(IDictionary<string, object> Source, string Key, Func<IDictionary<string, object>, TTarget> Reader)
        {
            var Result = new List<TTarget>();
            if (Source == null || !Source.ContainsKey(Key) || Source[Key] == null)
                return Result;

            var Items = Source[Key] as IEnumerable;
            if (Items == null || Source[Key] is string)
                return Result;

            foreach (var Item in Items)
            {
                var Dictionary = Item as IDictionary<string, object>;
                if (Dictionary != null)
                    Result.Add(Reader(Dictionary));
            }

            return Result;
        }

        private static List<string> ReadStringList(IDictionary<string, object> Source, string Key)
        {
            var Result = new List<string>();
            if (Source == null || !Source.ContainsKey(Key) || Source[Key] == null)
                return Result;

            var Items = Source[Key] as IEnumerable;
            if (Items == null || Source[Key] is string)
            {
                Result.Add(Convert.ToString(Source[Key], CultureInfo.InvariantCulture));
                return Result;
            }

            foreach (var Item in Items)
                if (Item != null)
                    Result.Add(Convert.ToString(Item, CultureInfo.InvariantCulture));

            return Result;
        }

        private static List<string> ReadWarningList(IDictionary<string, object> Source, string Key)
        {
            var Result = new List<string>();
            if (Source == null || !Source.ContainsKey(Key) || Source[Key] == null)
                return Result;

            var Items = Source[Key] as IEnumerable;
            if (Items == null || Source[Key] is string)
            {
                Result.Add(DomainJsonWarningFormatter.Format(Source[Key], Key));
                return Result;
            }

            var Index = 0;
            foreach (var Item in Items)
            {
                Result.Add(DomainJsonWarningFormatter.Format(Item, Key + "[" + Index.ToString(CultureInfo.InvariantCulture) + "]"));
                Index++;
            }

            return Result;
        }

        public static IDictionary<string, object> GetDictionary(IDictionary<string, object> Source, string Key)
        {
            if (Source == null || !Source.ContainsKey(Key))
                return null;
            return Source[Key] as IDictionary<string, object>;
        }

        public static Dictionary<string, object> GetObjectDictionary(IDictionary<string, object> Source, string Key)
        {
            var Dictionary = GetDictionary(Source, Key);
            return Dictionary == null ? new Dictionary<string, object>() : Dictionary.ToDictionary(Pair => Pair.Key, Pair => Pair.Value);
        }

        public static string GetString(IDictionary<string, object> Source, string Key)
        {
            if (Source == null || !Source.ContainsKey(Key) || Source[Key] == null)
                return null;
            return Convert.ToString(Source[Key], CultureInfo.InvariantCulture);
        }

        public static int GetInt(IDictionary<string, object> Source, string Key, int DefaultValue)
        {
            int Result;
            return TryGetInt(Source, Key, out Result) ? Result : DefaultValue;
        }

        private static int? GetNullableInt(IDictionary<string, object> Source, string Key)
        {
            int Result;
            return TryGetInt(Source, Key, out Result) ? (int?)Result : null;
        }

        private static uint? GetNullableUInt(IDictionary<string, object> Source, string Key)
        {
            if (Source == null || !Source.ContainsKey(Key) || Source[Key] == null)
                return null;

            uint Result;
            return UInt32.TryParse(Convert.ToString(Source[Key], CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out Result)
                   ? (uint?)Result : null;
        }

        private static bool TryGetInt(IDictionary<string, object> Source, string Key, out int Result)
        {
            Result = 0;
            if (Source == null || !Source.ContainsKey(Key) || Source[Key] == null)
                return false;

            if (Source[Key] is int)
            {
                Result = (int)Source[Key];
                return true;
            }

            return Int32.TryParse(Convert.ToString(Source[Key], CultureInfo.InvariantCulture),
                                  NumberStyles.Integer, CultureInfo.InvariantCulture, out Result);
        }

        public static bool? GetNullableBool(IDictionary<string, object> Source, string Key)
        {
            if (Source == null || !Source.ContainsKey(Key) || Source[Key] == null)
                return null;

            if (Source[Key] is bool)
                return (bool)Source[Key];

            bool Result;
            return Boolean.TryParse(Convert.ToString(Source[Key], CultureInfo.InvariantCulture), out Result)
                   ? (bool?)Result : null;
        }

        private static OrderedDictionary NewObject()
        {
            return new OrderedDictionary(StringComparer.Ordinal);
        }

        private static void Add(OrderedDictionary Object, string Key, object Value)
        {
            Object.Add(Key, Value);
        }

        private static void AddIf(OrderedDictionary Object, string Key, object Value)
        {
            if (Value == null)
                return;

            var Text = Value as string;
            if (Text != null && Text.Length == 0)
                return;

            Object.Add(Key, Value);
        }

        private static void AddIfNotEmpty(OrderedDictionary Object, string Key, object Value)
        {
            if (Value == null)
                return;

            var Dictionary = Value as IDictionary;
            if (Dictionary != null && Dictionary.Count < 1)
                return;

            var Items = Value as ICollection;
            if (Items != null && Items.Count < 1)
                return;

            Object.Add(Key, Value);
        }

        private static List<object> ToList<TSource>(IEnumerable<TSource> Items, Func<TSource, object> Converter)
        {
            var Result = new List<object>();
            if (Items == null)
                return Result;

            foreach (var Item in Items)
                Result.Add(Converter(Item));

            return Result;
        }

        private static OrderedDictionary ToOrderedDictionary(Dictionary<string, object> Source)
        {
            var Result = NewObject();
            if (Source == null)
                return Result;

            foreach (var Pair in Source.OrderBy(Pair => Pair.Key))
                Add(Result, Pair.Key, NormalizeUnknownValue(Pair.Value));

            return Result;
        }

        private static object NormalizeUnknownValue(object Value)
        {
            var Dictionary = Value as IDictionary<string, object>;
            if (Dictionary != null)
                return ToOrderedDictionary(Dictionary.ToDictionary(Pair => Pair.Key, Pair => Pair.Value));

            var Items = Value as IEnumerable;
            if (Items != null && !(Value is string))
            {
                var Result = new List<object>();
                foreach (var Item in Items)
                    Result.Add(NormalizeUnknownValue(Item));
                return Result;
            }

            return Value;
        }

        private static void WriteJsonValue(StringBuilder Builder, object Value, int Indent)
        {
            if (Value == null)
            {
                Builder.Append("null");
                return;
            }

            if (Value is string)
            {
                WriteJsonString(Builder, (string)Value);
                return;
            }

            if (Value is bool)
            {
                Builder.Append(((bool)Value) ? "true" : "false");
                return;
            }

            if (Value is int || Value is long || Value is short || Value is byte ||
                Value is uint || Value is ulong || Value is ushort || Value is sbyte ||
                Value is double || Value is float || Value is decimal)
            {
                Builder.Append(Convert.ToString(Value, CultureInfo.InvariantCulture));
                return;
            }

            var Dictionary = Value as IDictionary;
            if (Dictionary != null)
            {
                WriteJsonObject(Builder, Dictionary, Indent);
                return;
            }

            var Items = Value as IEnumerable;
            if (Items != null)
            {
                WriteJsonArray(Builder, Items, Indent);
                return;
            }

            WriteJsonString(Builder, Convert.ToString(Value, CultureInfo.InvariantCulture));
        }

        private static void WriteJsonObject(StringBuilder Builder, IDictionary Object, int Indent)
        {
            Builder.Append("{");
            if (Object.Count > 0)
                Builder.AppendLine();

            var Index = 0;
            foreach (DictionaryEntry Entry in Object)
            {
                WriteIndent(Builder, Indent + 1);
                WriteJsonString(Builder, Convert.ToString(Entry.Key, CultureInfo.InvariantCulture));
                Builder.Append(": ");
                WriteJsonValue(Builder, Entry.Value, Indent + 1);

                Index++;
                if (Index < Object.Count)
                    Builder.Append(",");
                Builder.AppendLine();
            }

            if (Object.Count > 0)
                WriteIndent(Builder, Indent);
            Builder.Append("}");
        }

        private static void WriteJsonArray(StringBuilder Builder, IEnumerable Items, int Indent)
        {
            var Materialized = new List<object>();
            foreach (var Item in Items)
                Materialized.Add(Item);

            Builder.Append("[");
            if (Materialized.Count > 0)
                Builder.AppendLine();

            for (int Index = 0; Index < Materialized.Count; Index++)
            {
                WriteIndent(Builder, Indent + 1);
                WriteJsonValue(Builder, Materialized[Index], Indent + 1);
                if (Index < Materialized.Count - 1)
                    Builder.Append(",");
                Builder.AppendLine();
            }

            if (Materialized.Count > 0)
                WriteIndent(Builder, Indent);
            Builder.Append("]");
        }

        private static void WriteJsonString(StringBuilder Builder, string Text)
        {
            Builder.Append("\"");
            foreach (var Character in Text ?? "")
            {
                switch (Character)
                {
                    case '"': Builder.Append("\\\""); break;
                    case '\\': Builder.Append("\\\\"); break;
                    case '\b': Builder.Append("\\b"); break;
                    case '\f': Builder.Append("\\f"); break;
                    case '\n': Builder.Append("\\n"); break;
                    case '\r': Builder.Append("\\r"); break;
                    case '\t': Builder.Append("\\t"); break;
                    default:
                        if (Character < 32)
                            Builder.Append("\\u" + ((int)Character).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            Builder.Append(Character);
                        break;
                }
            }
            Builder.Append("\"");
        }

        private static void WriteIndent(StringBuilder Builder, int Indent)
        {
            Builder.Append(new string(' ', Indent * 2));
        }
    }
}
