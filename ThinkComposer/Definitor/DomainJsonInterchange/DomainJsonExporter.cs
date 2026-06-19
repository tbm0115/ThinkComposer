// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Domain JSON exporter.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Instrumind.Common;
using Instrumind.Common.Visualization;

using Instrumind.ThinkComposer.Composer.Generation;
using Instrumind.ThinkComposer.MetaModel;
using Instrumind.ThinkComposer.MetaModel.Configurations;
using Instrumind.ThinkComposer.MetaModel.GraphMetaModel;
using Instrumind.ThinkComposer.MetaModel.InformationMetaModel;

namespace Instrumind.ThinkComposer.Definitor.DomainJsonInterchange
{
    public static class DomainJsonExporter
    {
        public static DomainJsonDocument Export(Domain Domain)
        {
            if (Domain == null)
                throw new ArgumentNullException("Domain");

            var Warnings = new List<string>();
            var WarningCollector = new DomainJsonExportWarningCollector();
            var Document = new DomainJsonDocument();
            Document.ExportedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            Document.Domain = ExportDomain(Domain);
            Document.Domain.CompatibilitySignature = DomainJsonCompatibility.ComputeSignature(Domain);

            Document.ExternalLanguages = Existing(Domain.ExternalLanguages).OrderBy(Item => StableKey(Item)).Select(ExportExternalLanguage).ToList();
            Document.LinkRoleVariants = Existing(Domain.LinkRoleVariants).OrderBy(Item => StableKey(Item)).Select(Item => ExportSimple(Item, "linkRoleVariant")).ToList();
            Document.ConceptDefinitionClusters = Existing(Domain.ConceptDefClusters).OrderBy(Item => StableKey(Item)).Select(Item => ExportFormal(Item, "conceptDefinitionCluster")).ToList();
            Document.RelationshipDefinitionClusters = Existing(Domain.RelationshipDefClusters).OrderBy(Item => StableKey(Item)).Select(Item => ExportFormal(Item, "relationshipDefinitionCluster")).ToList();
            Document.MarkerClusters = Existing(Domain.MarkerClusters).OrderBy(Item => StableKey(Item)).Select(Item => ExportSimple(Item, "markerCluster")).ToList();
            Document.MarkerDefinitions = Existing(Domain.MarkerDefinitions).OrderBy(Item => StableKey(Item)).Select(ExportMarkerDefinition).ToList();
            Document.TableDefinitionCategories = Existing(Domain.TableDefCategories).OrderBy(Item => StableKey(Item)).Select(Item => ExportFormal(Item, "tableDefinitionCategory")).ToList();
            Document.FieldDefinitionCategories = Existing(Domain.FieldDefCategories).OrderBy(Item => StableKey(Item)).Select(Item => ExportFormal(Item, "fieldDefinitionCategory")).ToList();
            Document.TableDefinitions = Existing(Domain.TableDefinitions).OrderBy(Item => StableKey(Item)).Select(Item => ExportTableDefinition(Item, WarningCollector)).ToList();
            Document.ConceptDefinitions = Existing(Domain.ConceptDefinitions).OrderBy(Item => StableKey(Item)).Select(ExportConceptDefinition).ToList();
            Document.RelationshipDefinitions = Existing(Domain.RelationshipDefinitions).OrderBy(Item => StableKey(Item)).Select(ExportRelationshipDefinition).ToList();
            Document.ConceptDefinitionOutputTemplates = ExportTemplates(Domain.OutputTemplatesForConcepts, "domainConcept", null)
                                                        .Concat(Existing(Domain.ConceptDefinitions).SelectMany(Definition => ExportTemplates(Definition.OutputTemplates, "conceptDefinition", Definition.TechName)))
                                                        .OrderBy(Item => StableKey(Item)).ToList();
            Document.RelationshipDefinitionOutputTemplates = ExportTemplates(Domain.OutputTemplatesForRelationships, "domainRelationship", null)
                                                             .Concat(Existing(Domain.RelationshipDefinitions).SelectMany(Definition => ExportTemplates(Definition.OutputTemplates, "relationshipDefinition", Definition.TechName)))
                                                             .OrderBy(Item => StableKey(Item)).ToList();
            Document.RelationshipCompatibility = DomainJsonCompatibility.ExportRelationshipCompatibility(Domain);

            Warnings.AddRange(WarningCollector.ToWarnings());
            Warnings.Add("Visual style details are summarized only; binary pictogram/image content is not inlined in Domain JSON.");
            Warnings.Add("Output templates are exported as text and are never executed by JSON import/export.");
            Document.Warnings = Warnings.OrderBy(Warning => Warning).Distinct().ToList();
            return Document;
        }

        private static DomainJsonElement ExportDomain(Domain Domain)
        {
            var Result = ExportFormal(Domain, "domain");
            Result.RepresentativeShape = Domain.RepresentativeShape;
            Result.IsComposable = Domain.IsComposable;
            Result.IsVersionable = Domain.IsVersionable;
            Result.DataTypeTechName = Domain.DefaultTableDef == null ? null : Domain.DefaultTableDef.TechName;
            Result.Set["viewGridSize"] = Domain.ViewGridSize;
            if (Domain.Version != null)
            {
                Result.Set["versionNumber"] = Domain.Version.VersionNumber == null ? null : Domain.Version.VersionNumber.ToString();
                Result.Set["versionSequence"] = Domain.Version.VersionSequence;
                Result.Set["lastModification"] = Domain.Version.LastModification.ToString("o", CultureInfo.InvariantCulture);
            }
            return Result;
        }

        private static DomainJsonElement ExportExternalLanguage(ExternalLanguageDeclaration Source)
        {
            var Result = ExportFormal(Source, "externalLanguage");
            Result.Set["metaId"] = Source.MetaId;
            Result.Set["alterability"] = Source.Alterability.ToString();
            return Result;
        }

        private static DomainJsonElement ExportMarkerDefinition(MarkerDefinition Source)
        {
            var Result = ExportSimple(Source, "markerDefinition");
            Result.ClusterTechName = Source.ClusterKey;
            Result.Set["clusterKey"] = Source.ClusterKey;
            return Result;
        }

        private static DomainJsonElement ExportTableDefinition(TableDefinition Source, DomainJsonExportWarningCollector Warnings)
        {
            var Result = ExportFormal(Source, "tableDefinition");
            Result.CategoryTechName = FirstCategoryTechName(Source.Categories, "tableDefinition", Source.TechName, Warnings);
            Result.Fields = Existing(Source.FieldDefinitions)
                .OrderBy(Field => Field.StorageIndex < 0 ? Int32.MaxValue : Field.StorageIndex)
                .ThenBy(Field => StableKey(Field))
                .Select(Field => ExportFieldDefinition(Field, Warnings))
                .ToList();
            return Result;
        }

        private static DomainJsonElement ExportFieldDefinition(FieldDefinition Source, DomainJsonExportWarningCollector Warnings)
        {
            var Result = ExportFormal(Source, "fieldDefinition");
            Result.OwnerTechName = Source.OwnerTableDef == null ? null : Source.OwnerTableDef.TechName;
            Result.DataTypeTechName = Source.FieldType == null ? null : Source.FieldType.TechName;
            Result.Order = Source.StorageIndex < 0 ? (int?)null : Source.StorageIndex;
            Result.CategoryTechName = FirstCategoryTechName(Source.Categories, "fieldDefinition", Source.TechName, Warnings);
            Result.Set["isRequired"] = Source.IsRequired;
            Result.Set["hideInDiagram"] = Source.HideInDiagram;
            if (Source.InitialStoreValue != null)
                Result.Set["initialStoreValue"] = Source.InitialStoreValue.ToStringAlways();
            if (Source.DefaultEmptyValue != null)
                Result.Set["defaultEmptyValue"] = Source.DefaultEmptyValue.ToStringAlways();
            return Result;
        }

        private static DomainJsonElement ExportConceptDefinition(ConceptDefinition Source)
        {
            var Result = ExportIdeaDefinition(Source, "conceptDefinition");
            Result.AncestorTechName = Source.AncestorConceptDef == null ? null : Source.AncestorConceptDef.TechName;
            Result.Set["automaticCreationConceptDefinitionTechName"] = Source.AutomaticCreationConceptDef == null ? null : Source.AutomaticCreationConceptDef.TechName;
            Result.Set["automaticCreationRelationshipDefinitionTechName"] = Source.AutomaticCreationRelationshipDef == null ? null : Source.AutomaticCreationRelationshipDef.TechName;
            Result.Set["automaticCreationPositioningMode"] = Source.AutomaticCreationPositioningMode.ToString();
            Result.Set["automaticCreationPositioningIsRadialized"] = Source.AutomaticCreationPositioningIsRadialized;
            Result.OutputTemplates = ExportTemplates(Source.OutputTemplates, "conceptDefinition", Source.TechName).ToList();
            return Result;
        }

        private static DomainJsonElement ExportRelationshipDefinition(RelationshipDefinition Source)
        {
            var Result = ExportIdeaDefinition(Source, "relationshipDefinition");
            Result.AncestorTechName = Source.AncestorRelationshipDef == null ? null : Source.AncestorRelationshipDef.TechName;
            Result.IsDirectional = Source.IsDirectional;
            Result.IsSimple = Source.IsSimple;
            Result.HideCentralSymbolWhenSimple = Source.HideCentralSymbolWhenSimple;
            Result.ShowNameIfHidingCentralSymbol = Source.ShowNameIfHidingCentralSymbol;
            Result.RoleDefinitions = new[] { Source.OriginOrParticipantLinkRoleDef, Source.TargetLinkRoleDef }
                .Where(Role => Role != null)
                .OrderBy(Role => Role.RoleType.ToString())
                .ThenBy(Role => StableKey(Role))
                .Select(ExportRelationshipRole)
                .ToList();
            Result.OutputTemplates = ExportTemplates(Source.OutputTemplates, "relationshipDefinition", Source.TechName).ToList();
            return Result;
        }

        private static DomainJsonElement ExportIdeaDefinition(IdeaDefinition Source, string Entity)
        {
            var Result = ExportFormal(Source, Entity);
            Result.ClusterTechName = Source.Cluster == null ? null : Source.Cluster.TechName;
            Result.RepresentativeShape = Source.RepresentativeShape;
            Result.IsComposable = Source.IsComposable;
            Result.IsVersionable = Source.IsVersionable;
            Result.CanAutomaticallyCreateRelatedConcepts = Source.CanAutomaticallyCreateRelatedConcepts;
            Result.DataTypeTechName = Source.CustomFieldsTableDef == null ? null : Source.CustomFieldsTableDef.TechName;
            return Result;
        }

        private static DomainJsonElement ExportRelationshipRole(LinkRoleDefinition Source)
        {
            var Result = ExportFormal(Source, "relationshipRole");
            Result.OwnerTechName = Source.OwnerRelationshipDef == null ? null : Source.OwnerRelationshipDef.TechName;
            Result.RoleType = Source.RoleType.ToString();
            Result.MaxConnections = Source.MaxConnections;
            Result.RelatedIdeasAreOrdered = Source.RelatedIdeasAreOrdered;
            Result.AllowedVariantTechNames = Existing(Source.AllowedVariants).Select(Variant => Variant.TechName).OrderBy(Text => Text).ToList();
            Result.AssociableIdeaDefinitionTechNames = Existing(Source.AssociableIdeaDefs).Select(Definition => Definition.TechName).OrderBy(Text => Text).ToList();
            return Result;
        }

        private static IEnumerable<DomainJsonElement> ExportTemplates(IEnumerable<TextTemplate> Source, string OwnerScope, string OwnerTechName)
        {
            if (Source == null)
                yield break;

            var Index = 0;
            foreach (var Template in Existing(Source).OrderBy(Template => Template.Language == null ? "" : Template.Language.TechName)
                                           .ThenBy(Template => Template.Text ?? ""))
            {
                var LanguageTechName = Template.Language == null ? null : Template.Language.TechName;
                var Result = new DomainJsonElement();
                Result.Entity = "outputTemplate";
                Result.Name = (LanguageTechName ?? "Template") + " Output Template";
                Result.TechName = ((OwnerTechName ?? OwnerScope) + "_" + (LanguageTechName ?? "Template")).TextToIdentifier();
                Result.OwnerScope = OwnerScope;
                Result.OwnerTechName = OwnerTechName;
                Result.ExternalLanguageTechName = LanguageTechName;
                Result.TemplateText = Template.Text;
                Result.ExtendsBaseTemplate = Template.ExtendsBaseTemplate;
                Result.Order = Index++;
                var Directives = OutputTemplateDirectiveInfo.Parse(Template.Text);
                Result.Set["templateRole"] = Directives.Role.ToString();
                if (!Directives.TargetFileName.IsAbsent())
                    Result.Set["targetFileName"] = Directives.TargetFileName;
                if (!Directives.TargetFileExtension.IsAbsent())
                    Result.Set["targetFileExtension"] = Directives.TargetFileExtension;
                Result.Set["templateHash"] = OutputTemplateDiagnostics.HashText(Template.Text).Substring(0, 16);
                yield return Result;
            }
        }

        private static DomainJsonElement ExportFormal(FormalElement Source, string Entity)
        {
            var Result = new DomainJsonElement();
            Result.Entity = Entity;
            Result.Id = IdOf(Source);
            Result.Name = Source.Name;
            Result.TechName = Source.TechName;
            Result.Summary = Source.Summary;
            Result.Description = Display.XamlRichTextToPlainTextOrSelf(Source.Description);
            Result.TechSpec = Source.TechSpec;
            return Result;
        }

        private static DomainJsonElement ExportSimple(SimpleElement Source, string Entity)
        {
            var Result = new DomainJsonElement();
            Result.Entity = Entity;
            Result.Name = Source.Name;
            Result.TechName = Source.TechName;
            Result.Summary = Source.Summary;
            Result.TechSpec = Source.TechSpec;
            return Result;
        }

        private static string IdOf(object Source)
        {
            var Unique = Source as UniqueElement;
            return Unique == null ? null : Unique.GlobalId.ToString("D");
        }

        private static string StableKey(IIdentifiableElement Element)
        {
            return (Element == null ? "" : (Element.TechName.NullDefault(Element.Name).NullDefault("") + "|" + Element.Name.NullDefault("")));
        }

        private static string StableKey(TextTemplate Template)
        {
            return (Template == null || Template.Language == null ? "" : Template.Language.TechName.NullDefault(Template.Language.Name)) + "|" +
                   (Template == null ? "" : Template.Text.NullDefault(""));
        }

        private static string StableKey(DomainJsonElement Element)
        {
            return Element == null ? "" : Element.TechName.NullDefault(Element.Name).NullDefault("") + "|" + Element.Name.NullDefault("");
        }

        private static IEnumerable<T> Existing<T>(IEnumerable<T> Source)
            where T : class
        {
            return Source == null ? Enumerable.Empty<T>() : Source.Where(Item => Item != null);
        }

        private static string FirstCategoryTechName<TDefinitor>(IEnumerable<MetaCategory<TDefinitor>> Categories, string Entity, string OwnerTechName, DomainJsonExportWarningCollector Warnings)
        {
            var Category = Existing(Categories).FirstOrDefault();
            if (Category == null)
            {
                Warnings.RecordMissingCategory(Entity, OwnerTechName);
                return null;
            }

            return Category.TechName;
        }

        private sealed class DomainJsonExportWarningCollector
        {
            private readonly Dictionary<string, List<string>> MissingCategoryOwners =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            public void RecordMissingCategory(string Entity, string OwnerTechName)
            {
                Entity = Entity.NullDefault("entity");
                OwnerTechName = OwnerTechName.NullDefault("<unnamed>");

                List<string> Owners;
                if (!this.MissingCategoryOwners.TryGetValue(Entity, out Owners))
                {
                    Owners = new List<string>();
                    this.MissingCategoryOwners[Entity] = Owners;
                }

                Owners.Add(OwnerTechName);
            }

            public IEnumerable<string> ToWarnings()
            {
                foreach (var Pair in this.MissingCategoryOwners.OrderBy(Item => Item.Key))
                {
                    var Examples = Pair.Value.Where(Text => !String.IsNullOrWhiteSpace(Text))
                                             .Distinct(StringComparer.OrdinalIgnoreCase)
                                             .OrderBy(Text => Text)
                                             .Take(5)
                                             .ToList();

                    yield return Pair.Value.Count.ToString(CultureInfo.InvariantCulture) + " " +
                                 Pair.Key + (Pair.Value.Count == 1 ? " has" : "s have") +
                                 " no category; categoryTechName omitted. Examples: " +
                                 String.Join(", ", Examples.ToArray()) + ".";
                }
            }
        }
    }

    public static class DomainJsonCompatibility
    {
        public static string ComputeSignature(Domain Domain)
        {
            if (Domain == null)
                return null;

            var Lines = new List<string>();
            Lines.Add("domain|id=" + IdOf(Domain) +
                      "|tech=" + Domain.TechName.NullDefault("") +
                      "|version=" + (Domain.Version == null ? "" : Domain.Version.VersionSequence.ToString(CultureInfo.InvariantCulture)) +
                      "|modified=" + (Domain.Version == null ? "" : Domain.Version.LastModification.ToString("o", CultureInfo.InvariantCulture)));

            foreach (var Language in Existing(Domain.ExternalLanguages).OrderBy(Item => StableKey(Item)))
                Lines.Add("externalLanguage|id=" + IdOf(Language) + "|tech=" + Language.TechName.NullDefault(""));

            foreach (var ConceptDef in Existing(Domain.ConceptDefinitions).OrderBy(Item => StableKey(Item)))
                Lines.Add("conceptDefinition|id=" + IdOf(ConceptDef) +
                          "|tech=" + ConceptDef.TechName.NullDefault("") +
                          "|ancestor=" + (ConceptDef.AncestorConceptDef == null ? "" : ConceptDef.AncestorConceptDef.TechName.NullDefault("")) +
                          "|table=" + (ConceptDef.CustomFieldsTableDef == null ? "" : ConceptDef.CustomFieldsTableDef.TechName.NullDefault("")));

            foreach (var RelDef in Existing(Domain.RelationshipDefinitions).OrderBy(Item => StableKey(Item)))
            {
                Lines.Add("relationshipDefinition|id=" + IdOf(RelDef) +
                          "|tech=" + RelDef.TechName.NullDefault("") +
                          "|simple=" + RelDef.IsSimple.ToString(CultureInfo.InvariantCulture) +
                          "|directional=" + RelDef.IsDirectional.ToString(CultureInfo.InvariantCulture) +
                          "|hideCentral=" + RelDef.HideCentralSymbolWhenSimple.ToString(CultureInfo.InvariantCulture));

                foreach (var Role in RolesOf(RelDef))
                    Lines.Add("relationshipRole|owner=" + RelDef.TechName.NullDefault("") +
                              "|id=" + IdOf(Role) +
                              "|tech=" + Role.TechName.NullDefault("") +
                              "|type=" + Role.RoleType.ToString() +
                              "|allowedIdeas=" + String.Join(",", Existing(Role.AssociableIdeaDefs).Select(Def => Def.TechName.NullDefault("")).OrderBy(Text => Text).ToArray()) +
                              "|allowedVariants=" + String.Join(",", Existing(Role.AllowedVariants).Select(Variant => Variant.TechName.NullDefault("")).OrderBy(Text => Text).ToArray()));
            }

            foreach (var TableDef in Existing(Domain.TableDefinitions).OrderBy(Item => StableKey(Item)))
            {
                Lines.Add("tableDefinition|id=" + IdOf(TableDef) + "|tech=" + TableDef.TechName.NullDefault(""));
                foreach (var FieldDef in Existing(TableDef.FieldDefinitions)
                    .OrderBy(Field => Field.StorageIndex < 0 ? Int32.MaxValue : Field.StorageIndex)
                    .ThenBy(Field => StableKey(Field)))
                    Lines.Add("fieldDefinition|owner=" + TableDef.TechName.NullDefault("") +
                              "|id=" + IdOf(FieldDef) +
                              "|tech=" + FieldDef.TechName.NullDefault("") +
                              "|type=" + (FieldDef.FieldType == null ? "" : FieldDef.FieldType.TechName.NullDefault("")) +
                              "|order=" + FieldDef.StorageIndex.ToString(CultureInfo.InvariantCulture));
            }

            Lines.Sort(StringComparer.Ordinal);
            using (var Hash = SHA256.Create())
            {
                var Bytes = Encoding.UTF8.GetBytes(String.Join("\n", Lines.ToArray()));
                return BytesToHex(Hash.ComputeHash(Bytes));
            }
        }

        public static List<DomainJsonRelationshipCompatibility> ExportRelationshipCompatibility(Domain Domain)
        {
            var Result = new List<DomainJsonRelationshipCompatibility>();
            if (Domain == null)
                return Result;

            foreach (var Definition in Existing(Domain.RelationshipDefinitions).OrderBy(Item => StableKey(Item)))
            {
                var OriginRole = Definition.OriginOrParticipantLinkRoleDef;
                var TargetRole = Definition.TargetLinkRoleDef;
                if (OriginRole == null || TargetRole == null)
                    continue;

                Result.Add(new DomainJsonRelationshipCompatibility
                {
                    RelationshipDefinitionId = IdOf(Definition),
                    RelationshipDefinitionTechName = Definition.TechName,
                    RelationshipDefinitionName = Definition.Name,
                    OriginRoleTechName = OriginRole.TechName,
                    OriginRoleName = OriginRole.Name,
                    TargetRoleTechName = TargetRole.TechName,
                    TargetRoleName = TargetRole.Name,
                    AllowedOriginConceptDefinitionTechNames = Existing(OriginRole.AssociableIdeaDefs).Select(Def => Def.TechName).Where(Text => !String.IsNullOrWhiteSpace(Text)).OrderBy(Text => Text).ToList(),
                    AllowedTargetConceptDefinitionTechNames = Existing(TargetRole.AssociableIdeaDefs).Select(Def => Def.TechName).Where(Text => !String.IsNullOrWhiteSpace(Text)).OrderBy(Text => Text).ToList(),
                    AllowedOriginVariantTechNames = Existing(OriginRole.AllowedVariants).Select(Variant => Variant.TechName).Where(Text => !String.IsNullOrWhiteSpace(Text)).OrderBy(Text => Text).ToList(),
                    AllowedTargetVariantTechNames = Existing(TargetRole.AllowedVariants).Select(Variant => Variant.TechName).Where(Text => !String.IsNullOrWhiteSpace(Text)).OrderBy(Text => Text).ToList(),
                    IsDirectional = Definition.IsDirectional,
                    IsSimple = Definition.IsSimple,
                    HideCentralSymbolWhenSimple = Definition.HideCentralSymbolWhenSimple
                });
            }

            return Result;
        }

        private static IEnumerable<LinkRoleDefinition> RolesOf(RelationshipDefinition Definition)
        {
            if (Definition == null)
                yield break;
            if (Definition.OriginOrParticipantLinkRoleDef != null)
                yield return Definition.OriginOrParticipantLinkRoleDef;
            if (Definition.TargetLinkRoleDef != null)
                yield return Definition.TargetLinkRoleDef;
        }

        private static IEnumerable<T> Existing<T>(IEnumerable<T> Source)
            where T : class
        {
            return Source == null ? Enumerable.Empty<T>() : Source.Where(Item => Item != null);
        }

        private static string StableKey(IIdentifiableElement Element)
        {
            return (Element == null ? "" : (Element.TechName.NullDefault(Element.Name).NullDefault("") + "|" + Element.Name.NullDefault("")));
        }

        private static string IdOf(object Source)
        {
            var Unique = Source as UniqueElement;
            return Unique == null ? null : Unique.GlobalId.ToString("D");
        }

        private static string BytesToHex(byte[] Bytes)
        {
            var Builder = new StringBuilder(Bytes.Length * 2);
            foreach (var Byte in Bytes)
                Builder.Append(Byte.ToString("x2", CultureInfo.InvariantCulture));
            return Builder.ToString();
        }
    }
}
