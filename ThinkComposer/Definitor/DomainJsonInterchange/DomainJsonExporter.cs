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
using System.Windows.Markup;
using System.Windows.Media;

using Instrumind.Common;
using Instrumind.Common.Visualization;

using Instrumind.ThinkComposer.Composer.Generation;
using Instrumind.ThinkComposer.MetaModel;
using Instrumind.ThinkComposer.MetaModel.Configurations;
using Instrumind.ThinkComposer.MetaModel.GraphMetaModel;
using Instrumind.ThinkComposer.MetaModel.InformationMetaModel;
using Instrumind.ThinkComposer.MetaModel.VisualMetaModel;

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
            Warnings.Add("Native visual formats, including text formats and WPF brush payloads, are exported for JSON persistence; custom domain shape resources, rich text content beyond plain text, and domain-level binary pictogram/image resources remain summarized only and are not reconstructed by JSON persistence.");
            Warnings.Add("Output templates are exported as text and are never executed by JSON import/export.");
            Document.Warnings = Warnings.OrderBy(Warning => Warning).Distinct().ToList();
            return Document;
        }

        private static DomainJsonElement ExportDomain(Domain Domain)
        {
            var Result = ExportFormal(Domain, "domain", true);
            Result.RepresentativeShape = Domain.RepresentativeShape;
            Result.IsComposable = Domain.IsComposable;
            Result.IsVersionable = Domain.IsVersionable;
            Result.DataTypeTechName = Domain.DefaultTableDef == null ? null : Domain.DefaultTableDef.TechName;
            Result.Set["modelRevision"] = Domain.ModelRevision;
            Result.Set["viewGridSize"] = Domain.ViewGridSize;
            if (Domain.ReportingConfiguration != null)
                Result.Set["reportingConfiguration"] = ExportReportConfiguration(Domain.ReportingConfiguration);
            return Result;
        }

        private static Dictionary<string, object> ExportReportConfiguration(ReportConfiguration Source)
        {
            var Result = new Dictionary<string, object>();
            if (Source == null)
                return Result;

            Result["documentTitle"] = Source.Document_Title;
            Result["documentSubtitle"] = Source.Document_Subtitle;
            Result["pageHeaderLeft"] = Source.PageHeader_Left;
            Result["pageHeaderCenter"] = Source.PageHeader_Center;
            Result["pageHeaderRight"] = Source.PageHeader_Right;
            Result["pageFooterLeft"] = Source.PageFooter_Left;
            Result["pageFooterCenter"] = Source.PageFooter_Center;
            Result["pageFooterRight"] = Source.PageFooter_Right;
            Result["docSectionTitlePage"] = Source.DocSection_TitlePage;
            Result["docSectionTableOfContents"] = Source.DocSection_TableOfContents;
            Result["docSectionComposition"] = Source.DocSection_Composition;
            Result["docSectionDomain"] = Source.DocSection_Domain;
            Result["compositionCard"] = ExportDisplayCard(Source.Composition_Card);
            Result["compositeIdeaViewCard"] = ExportDisplayCard(Source.CompositeIdea_View_Card);
            Result["compositeIdeaViewDiagram"] = Source.CompositeIdea_View_Diagram;
            Result["compositeIdeaConceptsList"] = ExportDisplayList(Source.CompositeIdea_Concepts_List);
            Result["compositeIdeaConceptsCard"] = ExportDisplayCard(Source.CompositeIdea_Concepts_Card);
            Result["compositeIdeaConceptsReportCompositeContent"] = Source.CompositeIdea_Concepts_ReportCompositeContent;
            Result["compositeIdeaRelationshipsList"] = ExportDisplayList(Source.CompositeIdea_Relationships_List);
            Result["compositeIdeaRelationshipsCard"] = ExportDisplayCard(Source.CompositeIdea_Relationships_Card);
            Result["compositeIdeaRelationshipsReportCompositeContent"] = Source.CompositeIdea_Relationships_ReportCompositeContent;
            Result["compositeIdeaMarkersList"] = ExportDisplayList(Source.CompositeIdea_Markers_List);
            Result["compositeIdeaMarkersCard"] = ExportDisplayCard(Source.CompositeIdea_Markers_Card);
            Result["compositeIdeaComplements"] = Source.CompositeIdea_Complements;
            Result["compositeIdeaGroupedIdeasList"] = ExportDisplayList(Source.CompositeIdea_GroupedIdeas_List);
            Result["compositeIdeaDetails"] = Source.CompositeIdea_Details;
            Result["compositeIdeaDetailsIncludeLinksTarget"] = Source.CompositeIdea_DetailsIncludeLinksTarget;
            Result["compositeIdeaDetailsIncludeAttachmentsContent"] = Source.CompositeIdea_DetailsIncludeAttachmentsContent;
            Result["compositeIdeaDetailsIncludeTablesData"] = Source.CompositeIdea_DetailsIncludeTablesData;
            Result["compositeIdeaRelatedFromCollection"] = Source.CompositeIdea_RelatedFrom_Collection;
            Result["compositeIdeaIncludeTargetCompanions"] = Source.CompositeIdea_IncludeTargetCompanions;
            Result["compositeIdeaRelatedToCollection"] = Source.CompositeIdea_RelatedTo_Collection;
            Result["compositeIdeaIncludeOriginCompanions"] = Source.CompositeIdea_IncludeOriginCompanions;
            Result["compositeRelationshipLinksCollection"] = Source.CompositeRelationship_Links_Collection;
            Result["compositeRelationshipLinksCard"] = ExportDisplayCard(Source.CompositeRelationship_Links_Card);
            Result["domainConceptDefs"] = Source.Domain_Concept_Defs;
            Result["domainConceptDefsList"] = ExportDisplayList(Source.Domain_Concept_Defs_List);
            Result["domainConceptDefsCard"] = ExportDisplayCard(Source.Domain_Concept_Defs_Card);
            Result["domainRelationshipDefs"] = Source.Domain_Relationship_Defs;
            Result["domainRelationshipDefsList"] = ExportDisplayList(Source.Domain_Relationship_Defs_List);
            Result["domainRelationshipDefsCard"] = ExportDisplayCard(Source.Domain_Relationship_Defs_Card);
            Result["domainLinkRoleVariants"] = Source.Domain_LinkRole_Variants;
            Result["domainMarkerDefs"] = Source.Domain_Marker_Defs;
            Result["domainMarkerDefsList"] = ExportDisplayList(Source.Domain_Marker_Defs_List);
            Result["domainMarkerDefsCard"] = ExportDisplayCard(Source.Domain_Marker_Defs_Card);
            Result["domainTableStructDefs"] = Source.Domain_TableStruct_Defs;
            Result["domainBaseTables"] = Source.Domain_BaseTables;
            return Result;
        }

        private static Dictionary<string, object> ExportDisplayList(DisplayList Source)
        {
            var Result = new Dictionary<string, object>();
            if (Source == null)
                return Result;

            Result["show"] = Source.Show;
            Result["propName"] = Source.PropName;
            Result["propTechName"] = Source.PropTechName;
            Result["propSummary"] = Source.PropSummary;
            Result["propPictogram"] = Source.PropPictogram;
            Result["definitor"] = Source.Definitor;
            return Result;
        }

        private static Dictionary<string, object> ExportDisplayCard(DisplayCard Source)
        {
            var Result = new Dictionary<string, object>();
            if (Source == null)
                return Result;

            Result["show"] = Source.Show;
            Result["route"] = Source.Route;
            Result["definitor"] = Source.Definitor;
            Result["propGlobalId"] = Source.PropGlobalId;
            Result["propName"] = Source.PropName;
            Result["propTechName"] = Source.PropTechName;
            Result["propSummary"] = Source.PropSummary;
            Result["propTechSpec"] = Source.PropTechSpec;
            Result["propPictogram"] = Source.PropPictogram;
            Result["propDescription"] = Source.PropDescription;
            Result["propVersioning"] = Source.PropVersioning;
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
            Result.Set["visualConnectorsFormat"] = ExportConnectorsFormat(Source.DefaultConnectorsFormat);
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
            Result.Set["visualSymbolFormat"] = ExportSymbolFormat(Source.DefaultSymbolFormat);
            return Result;
        }

        private static Dictionary<string, object> ExportSymbolFormat(VisualSymbolFormat Source)
        {
            var Result = ExportElementFormat(Source);
            if (Source == null)
                return Result;

            Result["initialWidth"] = Source.InitialWidth;
            Result["initialHeight"] = Source.InitialHeight;
            Result["hasFixedWidth"] = Source.HasFixedWidth;
            Result["hasFixedHeight"] = Source.HasFixedHeight;
            Result["useNameAsMainTitle"] = Source.UseNameAsMainTitle;
            Result["subtitleVisualDisposition"] = Source.SubtitleVisualDisposition.ToString();
            Result["pictogramVisualDisposition"] = Source.PictogramVisualDisposition.ToString();
            Result["useDefinitorPictogramAsNullDefault"] = Source.UseDefinitorPictogramAsNullDefault;
            Result["usePictogramAsSymbol"] = Source.UsePictogramAsSymbol;
            Result["detailsPosterIsHanging"] = Source.DetailsPosterIsHanging;
            Result["includeDetailsSeparators"] = Source.IncludeDetailsSeparators;
            Result["initiallyFlippedHorizontally"] = Source.InitiallyFlippedHorizontally;
            Result["initiallyFlippedVertically"] = Source.InitiallyFlippedVertically;
            Result["initiallyTilted"] = Source.InitiallyTilted;
            Result["asMultiple"] = Source.AsMultiple;
            Result["textFormats"] = ExportTextFormats(Source);
            AddBrush(Result, "regionBackground", Source.RegionBackground);
            AddBrush(Result, "regionForeground", Source.RegionForeground);
            AddDash(Result, "regionDash", Source.RegionDash);
            Result["regionThickness"] = Source.RegionThickness;
            Result["initialGroupRegionPlacementHorizontal"] = Source.InitialGroupRegionPlacementHorizontal.ToString();
            return Result;
        }

        private static Dictionary<string, object> ExportTextFormats(VisualSymbolFormat Source)
        {
            var Result = new Dictionary<string, object>();
            if (Source == null)
                return Result;

            foreach (ETextPurpose Purpose in Enum.GetValues(typeof(ETextPurpose)))
            {
                var Format = Source.GetTextFormat(Purpose);
                if (Format != null)
                    Result[Purpose.ToString()] = ExportTextFormat(Format);
            }

            return Result;
        }

        private static Dictionary<string, object> ExportTextFormat(TextFormat Format)
        {
            if (Format == null)
                return null;

            var Result = new Dictionary<string, object>();
            Result["type"] = "textFormat";
            Result["fontFamilyName"] = Format.FontFamilyName;
            Result["fontSize"] = Format.FontSize;
            Result["foregroundBrush"] = ExportBrush(Format.ForegroundBrush);
            Result["isBold"] = Format.IsBold;
            Result["isItalic"] = Format.IsItalic;
            Result["isUnderline"] = Format.IsUnderline;
            Result["isStrikethrough"] = Format.IsStrikethrough;
            Result["alignment"] = Format.Alignment.ToString();
            return Result;
        }

        private static Dictionary<string, object> ExportConnectorsFormat(VisualConnectorsFormat Source)
        {
            var Result = ExportElementFormat(Source);
            if (Source == null)
                return Result;

            Result["pathStyle"] = Source.PathStyle.ToString();
            Result["pathCorner"] = Source.PathCorner.ToString();
            Result["labelLinkVariant"] = Source.LabelLinkVariant;
            Result["labelLinkDefinitor"] = Source.LabelLinkDefinitor;
            Result["labelLinkDescriptor"] = Source.LabelLinkDescriptor;
            Result["headPlugs"] = ExportPlugMap(Source.HeadPlugs);
            Result["tailPlugs"] = ExportPlugMap(Source.TailPlugs);
            return Result;
        }

        private static Dictionary<string, object> ExportElementFormat(VisualElementFormat Source)
        {
            var Result = new Dictionary<string, object>();
            if (Source == null)
                return Result;

            AddBrush(Result, "mainBackground", Source.MainBackground);
            AddBrush(Result, "lineBrush", Source.LineBrush);
            AddDash(Result, "lineDash", Source.LineDash);
            Result["lineCap"] = Source.LineCap.ToString();
            Result["lineJoin"] = Source.LineJoin.ToString();
            Result["lineThickness"] = Source.LineThickness;
            Result["opacity"] = Source.Opacity;
            return Result;
        }

        private static Dictionary<string, object> ExportPlugMap(IDictionary<SimplePresentationElement, string> Source)
        {
            var Result = new Dictionary<string, object>();
            if (Source == null)
                return Result;

            foreach (var Pair in Source.OrderBy(Pair => Pair.Key == null ? "" : Pair.Key.TechName))
                if (Pair.Key != null && Pair.Value != null)
                    Result[Pair.Key.TechName] = Pair.Value;

            return Result;
        }

        private static void AddBrush(Dictionary<string, object> Target, string Key, Brush Brush)
        {
            Target[Key] = ExportBrush(Brush);
        }

        private static void AddDash(Dictionary<string, object> Target, string Key, DashStyle Dash)
        {
            var Text = ExportDashStyle(Dash);
            if (Text != null)
                Target[Key] = Text;
        }

        private static object ExportBrush(Brush Brush)
        {
            if (Brush == null)
                return null;

            var Text = default(string);
            try
            {
                var Converter = new BrushConverter();
                if (Converter.CanConvertTo(typeof(string)))
                    Text = (string)Converter.ConvertTo(null, CultureInfo.InvariantCulture, Brush, typeof(string));
            }
            catch
            {
            }

            if (!CanImportBrushText(Text))
            {
                var Xaml = ExportBrushXaml(Brush);
                if (!String.IsNullOrWhiteSpace(Xaml))
                    return new Dictionary<string, object>
                    {
                        { "type", "brush" },
                        { "xaml", Xaml }
                    };

                if (String.IsNullOrWhiteSpace(Text))
                    return null;
            }

            if (Math.Abs(Brush.Opacity - 1.0) < 0.0001)
                return Text;

            return new Dictionary<string, object>
            {
                { "color", Text },
                { "opacity", Brush.Opacity }
            };
        }

        private static bool CanImportBrushText(string Text)
        {
            if (String.IsNullOrWhiteSpace(Text))
                return false;

            try
            {
                return new BrushConverter().ConvertFromString(null, CultureInfo.InvariantCulture, Text) is Brush;
            }
            catch
            {
                return false;
            }
        }

        private static string ExportBrushXaml(Brush Brush)
        {
            if (Brush == null)
                return null;

            try
            {
                return XamlWriter.Save(Brush);
            }
            catch
            {
                return null;
            }
        }

        private static string ExportDashStyle(DashStyle Dash)
        {
            if (Dash == null)
                return null;

            var Declared = Display.DeclaredDashStyles.FirstOrDefault(Item => Item.Item1.IsEqual(Dash));
            return Declared == null ? null : Declared.Item2;
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

        private static DomainJsonElement ExportFormal(FormalElement Source, string Entity, bool ForceVersionExport = false)
        {
            var Result = new DomainJsonElement();
            Result.Entity = Entity;
            Result.Id = IdOf(Source);
            Result.Name = Source.Name;
            Result.TechName = Source.TechName;
            Result.Summary = Source.Summary;
            Result.Description = Display.XamlRichTextToPlainTextOrSelf(Source.Description);
            Result.TechSpec = Source.TechSpec;
            ExportVersion(Result.Set, Source.Version, ForceVersionExport);
            return Result;
        }

        private static void ExportVersion(Dictionary<string, object> Target, VersionCard Version, bool Force = false)
        {
            if (Target == null || Version == null)
                return;

            if (!Force && IsDefaultTransientVersion(Version))
                return;

            Target["versionNumber"] = Version.VersionNumber == null ? null : Version.VersionNumber.ToString();
            Target["versionAnnotation"] = Version.Annotation;
            Target["versionSequence"] = Version.VersionSequence;
            Target["creation"] = Version.Creation.ToString("o", CultureInfo.InvariantCulture);
            Target["creator"] = Version.Creator;
            Target["lastModification"] = Version.LastModification.ToString("o", CultureInfo.InvariantCulture);
            Target["lastModifier"] = Version.LastModifier;
        }

        private static bool IsDefaultTransientVersion(VersionCard Version)
        {
            if (Version == null)
                return true;

            return Version.VersionSequence == 1 &&
                   Version.VersionNumber == VersionCard.DEFAULT_VERSION &&
                   Version.Annotation.IsAbsent() &&
                   Version.Creator == AppExec.SessionUserName &&
                   Version.LastModifier == Version.Creator &&
                   Version.Creation == Version.LastModification;
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
