// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Deterministic JSON writer plus tolerant JSON reader for the interchange DTOs.
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

namespace Instrumind.ThinkComposer.Composer.JsonInterchange
{
    public static class CompositionJsonSerializer
    {
        public static void Save(CompositionJsonDocument Document, string FilePath)
        {
            File.WriteAllText(FilePath, Serialize(Document), Encoding.UTF8);
        }

        public static CompositionJsonDocument Load(string FilePath)
        {
            var Text = File.ReadAllText(FilePath, Encoding.UTF8);
            return Deserialize(Text);
        }

        public static string Serialize(CompositionJsonDocument Document)
        {
            var Builder = new StringBuilder();
            WriteJsonValue(Builder, ToGraph(Document), 0);
            Builder.AppendLine();
            return Builder.ToString();
        }

        public static CompositionJsonDocument Deserialize(string Text)
        {
            var Serializer = new JavaScriptSerializer();
            Serializer.MaxJsonLength = Int32.MaxValue;

            var Root = Serializer.DeserializeObject(Text) as IDictionary<string, object>;
            if (Root == null)
                throw new InvalidDataException("The JSON file must contain an object at the root.");

            var Document = new CompositionJsonDocument();
            Document.Format = GetString(Root, "format");
            Document.FormatVersion = GetInt(Root, "formatVersion", 0);
            Document.ExportedAtUtc = GetString(Root, "exportedAtUtc");
            Document.Application = GetString(Root, "application");
            Document.TargetContext = ReadTargetContext(GetDictionary(Root, "targetContext"));
            Document.Requires = ReadTargetContext(GetDictionary(Root, "requires"));
            Document.Composition = ReadComposition(GetDictionary(Root, "composition"));
            Document.ImportOptions = ReadImportOptions(GetDictionary(Root, "importOptions"));
            Document.VisualStrategy = ReadVisualStrategy(GetDictionary(Root, "visualStrategy"));
            Document.Ideas = ReadList(Root, "ideas", ReadIdea);
            Document.Relationships = ReadList(Root, "relationships", ReadRelationship);
            Document.Views = ReadList(Root, "views", ReadView);
            Document.Operations = ReadList(Root, "operations", ReadOperation);
            Document.Groups = ReadList(Root, "groups", ReadGroup);
            Document.Warnings = ReadWarningList(Root, "warnings");

            return Document;
        }

        public static void Validate(CompositionJsonDocument Document)
        {
            if (Document == null)
                throw new InvalidDataException("No JSON document was loaded.");

            if (Document.Format != CompositionJsonDocument.CurrentFormat)
                throw new InvalidDataException("Unsupported JSON format. Expected '" + CompositionJsonDocument.CurrentFormat + "'.");

            if (Document.FormatVersion != CompositionJsonDocument.CurrentFormatVersion)
                throw new InvalidDataException("Unsupported JSON formatVersion. Expected " + CompositionJsonDocument.CurrentFormatVersion + ".");
        }

        private static object ToGraph(CompositionJsonDocument Document)
        {
            var Obj = NewObject();
            Add(Obj, "format", Document.Format);
            Add(Obj, "formatVersion", Document.FormatVersion);
            AddIf(Obj, "exportedAtUtc", Document.ExportedAtUtc);
            Add(Obj, "application", Document.Application);
            AddIf(Obj, "targetContext", ToGraph(Document.TargetContext));
            AddIf(Obj, "requires", ToGraph(Document.Requires));
            AddIf(Obj, "composition", ToGraph(Document.Composition));
            AddIf(Obj, "importOptions", ToGraph(Document.ImportOptions));
            AddIf(Obj, "visualStrategy", ToGraph(Document.VisualStrategy));
            Add(Obj, "ideas", ToList(Document.Ideas, ToGraph));
            Add(Obj, "relationships", ToList(Document.Relationships, ToGraph));
            Add(Obj, "views", ToList(Document.Views, ToGraph));
            Add(Obj, "operations", ToList(Document.Operations, ToGraph));
            Add(Obj, "groups", ToList(Document.Groups, ToGraph));
            Add(Obj, "warnings", Document.Warnings ?? new List<string>());
            return Obj;
        }

        private static object ToGraph(CompositionJsonImportOptions ImportOptions)
        {
            if (ImportOptions == null)
                return null;

            var Obj = NewObject();
            AddIf(Obj, "autoPlaceNewItems", ImportOptions.AutoPlaceNewItems);
            AddIf(Obj, "autoFitPlacedConcepts", ImportOptions.AutoFitPlacedConcepts);
            AddIf(Obj, "autoRoutePlacedLinks", ImportOptions.AutoRoutePlacedLinks);
            AddIf(Obj, "useActiveCompositionAsContainer", ImportOptions.UseActiveCompositionAsContainer);
            AddIf(Obj, "treatMissingFullStateItemsAsCreates", ImportOptions.TreatMissingFullStateItemsAsCreates);
            AddIf(Obj, "relationshipDefinitionFallbackTechName", ImportOptions.RelationshipDefinitionFallbackTechName);
            AddIf(Obj, "detailFallbackMode", ImportOptions.DetailFallbackMode);
            AddIf(Obj, "domainCompatibilityPolicy", ImportOptions.DomainCompatibilityPolicy);
            AddIf(Obj, "compositionVersionPolicy", ImportOptions.CompositionVersionPolicy);
            AddIf(Obj, "strictRelationshipCompatibility", ImportOptions.StrictRelationshipCompatibility);
            AddIf(Obj, "abortOnRelationshipCompatibilityFailure", ImportOptions.AbortOnRelationshipCompatibilityFailure);
            AddIf(Obj, "strictDetailsCompatibility", ImportOptions.StrictDetailsCompatibility);
            AddIf(Obj, "abortOnDetailCompatibilityFailure", ImportOptions.AbortOnDetailCompatibilityFailure);
            AddIf(Obj, "relationshipVisualPlacementMode", ImportOptions.RelationshipVisualPlacementMode);
            AddIf(Obj, "recomputeSuspiciousRelationshipVisuals", ImportOptions.RecomputeSuspiciousRelationshipVisuals);
            AddIf(Obj, "hideGenericRelationshipCenters", ImportOptions.HideGenericRelationshipCenters);
            AddIf(Obj, "maxRelationshipCenterDisplacement", ImportOptions.MaxRelationshipCenterDisplacement);
            AddIf(Obj, "relationshipCenterObstaclePadding", ImportOptions.RelationshipCenterObstaclePadding);
            AddIf(Obj, "relationshipCenterOverlapPadding", ImportOptions.RelationshipCenterOverlapPadding);
            AddIf(Obj, "layoutMode", ImportOptions.LayoutMode);
            AddIf(Obj, "preventSelfRecursiveCompositeViews", ImportOptions.PreventSelfRecursiveCompositeViews);
            AddIf(Obj, "repairRecursiveVisuals", ImportOptions.RepairRecursiveVisuals);
            return Obj;
        }

        private static object ToGraph(CompositionJsonVisualStrategy Strategy)
        {
            if (Strategy == null)
                return null;

            var Obj = NewObject();
            AddIf(Obj, "mode", Strategy.Mode);
            AddIf(Obj, "largeModelThresholds", ToGraph(Strategy.LargeModelThresholds));
            AddIf(Obj, "fullModelVisuals", Strategy.FullModelVisuals);
            AddIf(Obj, "overviewView", Strategy.OverviewView);
            AddIf(Obj, "overviewViewTechName", Strategy.OverviewViewTechName);
            AddIf(Obj, "maxOverviewConcepts", Strategy.MaxOverviewConcepts);
            AddIf(Obj, "maxOverviewRelationships", Strategy.MaxOverviewRelationships);
            Add(Obj, "groupBy", Strategy.GroupBy ?? new List<string>());
            AddIf(Obj, "deferRouting", Strategy.DeferRouting);
            AddIf(Obj, "deferAutoFit", Strategy.DeferAutoFit);
            AddIf(Obj, "deferViewRefresh", Strategy.DeferViewRefresh);
            AddIf(Obj, "relationshipVisualPlacement", Strategy.RelationshipVisualPlacement);
            return Obj;
        }

        private static object ToGraph(CompositionJsonLargeModelThresholds Thresholds)
        {
            if (Thresholds == null)
                return null;

            var Obj = NewObject();
            AddIf(Obj, "concepts", Thresholds.Concepts);
            AddIf(Obj, "relationships", Thresholds.Relationships);
            AddIf(Obj, "visuals", Thresholds.Visuals);
            return Obj;
        }

        private static object ToGraph(CompositionJsonTargetContext Context)
        {
            if (Context == null)
                return null;

            var Obj = NewObject();
            AddIf(Obj, "composition", ToGraph(Context.Composition));
            AddIf(Obj, "domain", ToGraph(Context.Domain));
            return Obj;
        }

        private static object ToGraph(CompositionJsonContextElement Element)
        {
            if (Element == null)
                return null;

            var Obj = NewObject();
            AddIf(Obj, "id", Element.Id);
            AddIf(Obj, "name", Element.Name);
            AddIf(Obj, "techName", Element.TechName);
            AddIf(Obj, "versionNumber", Element.VersionNumber);
            AddIf(Obj, "versionSequence", Element.VersionSequence);
            AddIf(Obj, "lastModification", Element.LastModification);
            AddIf(Obj, "compatibilitySignature", Element.CompatibilitySignature);
            return Obj;
        }

        private static object ToGraph(CompositionJsonComposition Composition)
        {
            if (Composition == null)
                return null;

            var Obj = NewObject();
            AddIf(Obj, "id", Composition.Id);
            AddIf(Obj, "name", Composition.Name);
            AddIf(Obj, "techName", Composition.TechName);
            AddIf(Obj, "summary", Composition.Summary);
            AddIf(Obj, "techSpec", Composition.TechSpec);
            AddIf(Obj, "viewsPrefix", Composition.ViewsPrefix);
            AddIf(Obj, "rootViewId", Composition.RootViewId);
            AddIf(Obj, "activeViewId", Composition.ActiveViewId);
            AddIf(Obj, "version", ToGraph(Composition.Version));
            AddIf(Obj, "domain", ToGraph(Composition.Domain));
            return Obj;
        }

        private static object ToGraph(CompositionJsonDomain Domain)
        {
            if (Domain == null)
                return null;

            var Obj = NewObject();
            AddIf(Obj, "id", Domain.Id);
            AddIf(Obj, "name", Domain.Name);
            AddIf(Obj, "techName", Domain.TechName);
            AddIf(Obj, "summary", Domain.Summary);
            AddIf(Obj, "techSpec", Domain.TechSpec);
            AddIf(Obj, "compatibilitySignature", Domain.CompatibilitySignature);
            Add(Obj, "definitions", ToList(Domain.Definitions, ToGraph));
            return Obj;
        }

        private static object ToGraph(CompositionJsonDefinition Definition)
        {
            var Obj = NewObject();
            AddIf(Obj, "id", Definition.Id);
            AddIf(Obj, "kind", Definition.Kind);
            AddIf(Obj, "name", Definition.Name);
            AddIf(Obj, "techName", Definition.TechName);
            AddIf(Obj, "summary", Definition.Summary);
            AddIf(Obj, "techSpec", Definition.TechSpec);
            return Obj;
        }

        private static object ToGraph(CompositionJsonVersion Version)
        {
            if (Version == null)
                return null;

            var Obj = NewObject();
            AddIf(Obj, "versionSequence", Version.VersionSequence);
            AddIf(Obj, "versionNumber", Version.VersionNumber);
            AddIf(Obj, "annotation", Version.Annotation);
            AddIf(Obj, "creator", Version.Creator);
            AddIf(Obj, "lastModifier", Version.LastModifier);
            AddIf(Obj, "creation", Version.Creation);
            AddIf(Obj, "lastModification", Version.LastModification);
            return Obj;
        }

        private static object ToGraph(CompositionJsonIdea Idea)
        {
            var Obj = NewObject();
            AddIf(Obj, "id", Idea.Id);
            Add(Obj, "kind", Idea.Kind ?? "Concept");
            AddIfTrue(Obj, "isNew", Idea.IsNew);
            AddIfTrue(Obj, "delete", Idea.Delete);
            AddIf(Obj, "definitionId", Idea.DefinitionId);
            AddIf(Obj, "definitionTechName", Idea.DefinitionTechName);
            AddIf(Obj, "definitionName", Idea.DefinitionName);
            AddIf(Obj, "name", Idea.Name);
            AddIf(Obj, "techName", Idea.TechName);
            AddIf(Obj, "summary", Idea.Summary);
            AddIf(Obj, "techSpec", Idea.TechSpec);
            AddIf(Obj, "containerId", Idea.ContainerId);
            AddIf(Obj, "containerTechName", Idea.ContainerTechName);
            AddIf(Obj, "visual", ToGraph(Idea.Visual));
            Add(Obj, "childIdeaIds", Idea.ChildIdeaIds ?? new List<string>());
            Add(Obj, "compositeViewIds", Idea.CompositeViewIds ?? new List<string>());
            Add(Obj, "details", ToList(Idea.Details, ToGraph));
            Add(Obj, "markers", ToList(Idea.Markers, ToGraph));
            return Obj;
        }

        private static object ToGraph(CompositionJsonRelationship Relationship)
        {
            var Obj = NewObject();
            AddIf(Obj, "id", Relationship.Id);
            Add(Obj, "kind", Relationship.Kind ?? "Relationship");
            AddIfTrue(Obj, "isNew", Relationship.IsNew);
            AddIfTrue(Obj, "delete", Relationship.Delete);
            AddIf(Obj, "definitionId", Relationship.DefinitionId);
            AddIf(Obj, "definitionTechName", Relationship.DefinitionTechName);
            AddIf(Obj, "definitionName", Relationship.DefinitionName);
            AddIf(Obj, "name", Relationship.Name);
            AddIf(Obj, "techName", Relationship.TechName);
            AddIf(Obj, "summary", Relationship.Summary);
            AddIf(Obj, "techSpec", Relationship.TechSpec);
            AddIf(Obj, "containerId", Relationship.ContainerId);
            AddIf(Obj, "containerTechName", Relationship.ContainerTechName);
            AddIf(Obj, "layoutRole", Relationship.LayoutRole);
            AddIf(Obj, "visual", ToGraph(Relationship.Visual));
            Add(Obj, "originIdeaIds", Relationship.OriginIdeaIds ?? new List<string>());
            Add(Obj, "originIdeaTechNames", Relationship.OriginIdeaTechNames ?? new List<string>());
            Add(Obj, "targetIdeaIds", Relationship.TargetIdeaIds ?? new List<string>());
            Add(Obj, "targetIdeaTechNames", Relationship.TargetIdeaTechNames ?? new List<string>());
            Add(Obj, "links", ToList(Relationship.Links, ToGraph));
            Add(Obj, "childIdeaIds", Relationship.ChildIdeaIds ?? new List<string>());
            Add(Obj, "compositeViewIds", Relationship.CompositeViewIds ?? new List<string>());
            Add(Obj, "details", ToList(Relationship.Details, ToGraph));
            Add(Obj, "markers", ToList(Relationship.Markers, ToGraph));
            return Obj;
        }

        private static object ToGraph(CompositionJsonRelationshipLink Link)
        {
            var Obj = NewObject();
            AddIf(Obj, "id", Link.Id);
            AddIf(Obj, "roleType", Link.RoleType);
            AddIf(Obj, "roleDefinitionId", Link.RoleDefinitionId);
            AddIf(Obj, "roleDefinitionTechName", Link.RoleDefinitionTechName);
            AddIf(Obj, "roleDefinitionName", Link.RoleDefinitionName);
            AddIf(Obj, "roleVariantTechName", Link.RoleVariantTechName);
            AddIf(Obj, "roleVariantName", Link.RoleVariantName);
            AddIf(Obj, "descriptorName", Link.DescriptorName);
            AddIf(Obj, "descriptorTechName", Link.DescriptorTechName);
            AddIf(Obj, "descriptorSummary", Link.DescriptorSummary);
            AddIf(Obj, "ideaId", Link.IdeaId);
            AddIf(Obj, "ideaTechName", Link.IdeaTechName);
            return Obj;
        }

        private static object ToGraph(CompositionJsonMarker Marker)
        {
            var Obj = NewObject();
            AddIfTrue(Obj, "delete", Marker.Delete);
            AddIf(Obj, "definitionId", Marker.DefinitionId);
            AddIf(Obj, "definitionTechName", Marker.DefinitionTechName);
            AddIf(Obj, "definitionName", Marker.DefinitionName);
            AddIf(Obj, "descriptorName", Marker.DescriptorName);
            AddIf(Obj, "descriptorTechName", Marker.DescriptorTechName);
            AddIf(Obj, "descriptorSummary", Marker.DescriptorSummary);
            return Obj;
        }

        private static object ToGraph(CompositionJsonDetail Detail)
        {
            var Obj = NewObject();
            AddIfTrue(Obj, "delete", Detail.Delete);
            AddIf(Obj, "kind", Detail.Kind);
            AddIf(Obj, "designatorId", Detail.DesignatorId);
            AddIf(Obj, "designatorTechName", Detail.DesignatorTechName);
            AddIf(Obj, "designatorName", Detail.DesignatorName);
            AddIf(Obj, "text", Detail.Text);
            AddIf(Obj, "targetAddress", Detail.TargetAddress);
            AddIf(Obj, "targetPropertyTechName", Detail.TargetPropertyTechName);
            AddIf(Obj, "source", Detail.Source);
            AddIf(Obj, "mimeType", Detail.MimeType);
            Add(Obj, "fields", ToList(Detail.Fields, ToGraph));
            Add(Obj, "records", ToRecordList(Detail.Records));
            return Obj;
        }

        private static object ToGraph(CompositionJsonField Field)
        {
            var Obj = NewObject();
            AddIf(Obj, "id", Field.Id);
            AddIf(Obj, "name", Field.Name);
            AddIf(Obj, "techName", Field.TechName);
            AddIf(Obj, "dataType", Field.DataType);
            return Obj;
        }

        private static object ToGraph(CompositionJsonView View)
        {
            var Obj = NewObject();
            AddIf(Obj, "id", View.Id);
            AddIf(Obj, "name", View.Name);
            AddIf(Obj, "techName", View.TechName);
            AddIf(Obj, "summary", View.Summary);
            AddIf(Obj, "ownerIdeaId", View.OwnerIdeaId);
            AddIf(Obj, "ownerIdeaTechName", View.OwnerIdeaTechName);
            Add(Obj, "visuals", ToList(View.Visuals, ToGraph));
            return Obj;
        }

        private static object ToGraph(CompositionJsonVisual Visual)
        {
            var Obj = NewObject();
            AddIf(Obj, "ideaId", Visual.IdeaId);
            AddIf(Obj, "ideaTechName", Visual.IdeaTechName);
            AddIf(Obj, "representationId", Visual.RepresentationId);
            AddIfTrue(Obj, "isShortcut", Visual.IsShortcut);
            AddIf(Obj, "x", Visual.X);
            AddIf(Obj, "y", Visual.Y);
            AddIf(Obj, "width", Visual.Width);
            AddIf(Obj, "height", Visual.Height);
            AddIf(Obj, "visual", ToGraph(Visual.Visual));
            return Obj;
        }

        private static object ToGraph(CompositionJsonOperation Operation)
        {
            var Obj = NewObject();
            AddIf(Obj, "op", Operation.Op);
            AddIf(Obj, "entity", Operation.Entity);
            AddIf(Obj, "id", Operation.Id);
            AddIf(Obj, "representationId", Operation.RepresentationId);
            AddIf(Obj, "techName", Operation.TechName);
            AddIf(Obj, "definitionTechName", Operation.DefinitionTechName);
            AddIf(Obj, "fallbackDefinitionTechName", Operation.FallbackDefinitionTechName);
            AddIf(Obj, "strictDefinition", Operation.StrictDefinition);
            AddIf(Obj, "containerId", Operation.ContainerId);
            AddIf(Obj, "containerTechName", Operation.ContainerTechName);
            AddIf(Obj, "viewId", Operation.ViewId);
            AddIf(Obj, "viewTechName", Operation.ViewTechName);
            AddIf(Obj, "x", Operation.X);
            AddIf(Obj, "y", Operation.Y);
            AddIf(Obj, "width", Operation.Width);
            AddIf(Obj, "height", Operation.Height);
            AddIf(Obj, "autoPlace", Operation.AutoPlace);
            AddIf(Obj, "autoFit", Operation.AutoFit);
            AddIf(Obj, "autoRoute", Operation.AutoRoute);
            AddIf(Obj, "isShortcut", Operation.IsShortcut);
            AddIf(Obj, "layoutRole", Operation.LayoutRole);
            AddIf(Obj, "visual", ToGraph(Operation.Visual));
            Add(Obj, "originIdeaIds", Operation.OriginIdeaIds ?? new List<string>());
            Add(Obj, "originIdeaTechNames", Operation.OriginIdeaTechNames ?? new List<string>());
            Add(Obj, "targetIdeaIds", Operation.TargetIdeaIds ?? new List<string>());
            Add(Obj, "targetIdeaTechNames", Operation.TargetIdeaTechNames ?? new List<string>());
            Add(Obj, "links", ToList(Operation.Links, ToGraph));
            Add(Obj, "details", ToList(Operation.Details, ToGraph));
            Add(Obj, "markers", ToList(Operation.Markers, ToGraph));
            Add(Obj, "set", ToOrderedDictionary(Operation.Set));
            return Obj;
        }

        private static object ToGraph(CompositionJsonVisualControl Visual)
        {
            if (Visual == null)
                return null;

            var Obj = NewObject();
            AddIf(Obj, "role", Visual.Role);
            AddIf(Obj, "display", Visual.Display);
            AddIf(Obj, "includeInView", Visual.IncludeInView);
            AddIf(Obj, "includeInArrangement", Visual.IncludeInArrangement);
            AddIf(Obj, "includeInRouting", Visual.IncludeInRouting);
            AddIf(Obj, "includeInAutoFit", Visual.IncludeInAutoFit);
            AddIf(Obj, "includeInOverview", Visual.IncludeInOverview);
            AddIf(Obj, "includeInFullView", Visual.IncludeInFullView);
            AddIf(Obj, "isShortcut", Visual.IsShortcut);
            AddIf(Obj, "relationshipCenterPlacement", Visual.RelationshipCenterPlacement);
            return Obj;
        }

        private static object ToGraph(CompositionJsonGroup Group)
        {
            if (Group == null)
                return null;

            var Obj = NewObject();
            AddIf(Obj, "id", Group.Id);
            AddIf(Obj, "name", Group.Name);
            AddIf(Obj, "techName", Group.TechName);
            Add(Obj, "memberIds", Group.MemberIds ?? new List<string>());
            Add(Obj, "memberTechNames", Group.MemberTechNames ?? new List<string>());
            AddIf(Obj, "headerConceptId", Group.HeaderConceptId);
            AddIf(Obj, "headerConceptTechName", Group.HeaderConceptTechName);
            AddIf(Obj, "createGroupRegion", Group.CreateGroupRegion);
            AddIf(Obj, "padding", Group.Padding);
            AddIf(Obj, "sendToBack", Group.SendToBack);
            return Obj;
        }

        private static CompositionJsonImportOptions ReadImportOptions(IDictionary<string, object> Source)
        {
            if (Source == null)
                return null;

            var Result = new CompositionJsonImportOptions();
            Result.AutoPlaceNewItems = GetNullableBool(Source, "autoPlaceNewItems");
            Result.AutoFitPlacedConcepts = GetNullableBool(Source, "autoFitPlacedConcepts");
            Result.AutoRoutePlacedLinks = GetNullableBool(Source, "autoRoutePlacedLinks");
            Result.UseActiveCompositionAsContainer = GetNullableBool(Source, "useActiveCompositionAsContainer");
            Result.TreatMissingFullStateItemsAsCreates = GetNullableBool(Source, "treatMissingFullStateItemsAsCreates");
            Result.RelationshipDefinitionFallbackTechName = GetString(Source, "relationshipDefinitionFallbackTechName");
            Result.DetailFallbackMode = GetString(Source, "detailFallbackMode");
            Result.DomainCompatibilityPolicy = GetString(Source, "domainCompatibilityPolicy");
            Result.CompositionVersionPolicy = GetString(Source, "compositionVersionPolicy");
            Result.StrictRelationshipCompatibility = GetNullableBool(Source, "strictRelationshipCompatibility");
            Result.AbortOnRelationshipCompatibilityFailure = GetNullableBool(Source, "abortOnRelationshipCompatibilityFailure");
            Result.StrictDetailsCompatibility = GetNullableBool(Source, "strictDetailsCompatibility");
            Result.AbortOnDetailCompatibilityFailure = GetNullableBool(Source, "abortOnDetailCompatibilityFailure");
            Result.RelationshipVisualPlacementMode = GetString(Source, "relationshipVisualPlacementMode");
            Result.RecomputeSuspiciousRelationshipVisuals = GetNullableBool(Source, "recomputeSuspiciousRelationshipVisuals");
            Result.HideGenericRelationshipCenters = GetNullableBool(Source, "hideGenericRelationshipCenters");
            Result.MaxRelationshipCenterDisplacement = GetNullableDouble(Source, "maxRelationshipCenterDisplacement");
            Result.RelationshipCenterObstaclePadding = GetNullableDouble(Source, "relationshipCenterObstaclePadding");
            Result.RelationshipCenterOverlapPadding = GetNullableDouble(Source, "relationshipCenterOverlapPadding");
            Result.LayoutMode = GetString(Source, "layoutMode");
            Result.PreventSelfRecursiveCompositeViews = GetNullableBool(Source, "preventSelfRecursiveCompositeViews");
            Result.RepairRecursiveVisuals = GetNullableBool(Source, "repairRecursiveVisuals");
            return Result;
        }

        private static CompositionJsonVisualStrategy ReadVisualStrategy(IDictionary<string, object> Source)
        {
            if (Source == null)
                return null;

            var Result = new CompositionJsonVisualStrategy();
            Result.Mode = GetString(Source, "mode");
            Result.LargeModelThresholds = ReadLargeModelThresholds(GetDictionary(Source, "largeModelThresholds"));
            Result.FullModelVisuals = GetNullableBool(Source, "fullModelVisuals");
            Result.OverviewView = GetNullableBool(Source, "overviewView");
            Result.OverviewViewTechName = GetString(Source, "overviewViewTechName");
            Result.MaxOverviewConcepts = GetNullableInt(Source, "maxOverviewConcepts");
            Result.MaxOverviewRelationships = GetNullableInt(Source, "maxOverviewRelationships");
            Result.GroupBy = ReadStringList(Source, "groupBy");
            Result.DeferRouting = GetNullableBool(Source, "deferRouting");
            Result.DeferAutoFit = GetNullableBool(Source, "deferAutoFit");
            Result.DeferViewRefresh = GetNullableBool(Source, "deferViewRefresh");
            Result.RelationshipVisualPlacement = GetString(Source, "relationshipVisualPlacement");
            return Result;
        }

        private static CompositionJsonLargeModelThresholds ReadLargeModelThresholds(IDictionary<string, object> Source)
        {
            if (Source == null)
                return null;

            var Result = new CompositionJsonLargeModelThresholds();
            Result.Concepts = GetNullableInt(Source, "concepts");
            Result.Relationships = GetNullableInt(Source, "relationships");
            Result.Visuals = GetNullableInt(Source, "visuals");
            return Result;
        }

        private static CompositionJsonTargetContext ReadTargetContext(IDictionary<string, object> Source)
        {
            if (Source == null)
                return null;

            var Result = new CompositionJsonTargetContext();
            Result.Composition = ReadContextElement(GetDictionary(Source, "composition"));
            Result.Domain = ReadContextElement(GetDictionary(Source, "domain"));
            return Result;
        }

        private static CompositionJsonContextElement ReadContextElement(IDictionary<string, object> Source)
        {
            if (Source == null)
                return null;

            var Result = new CompositionJsonContextElement();
            Result.Id = GetString(Source, "id");
            Result.Name = GetString(Source, "name");
            Result.TechName = GetString(Source, "techName");
            Result.VersionNumber = GetString(Source, "versionNumber");
            int VersionSequence;
            if (TryGetInt(Source, "versionSequence", out VersionSequence))
                Result.VersionSequence = VersionSequence;
            Result.LastModification = GetString(Source, "lastModification");
            Result.CompatibilitySignature = GetString(Source, "compatibilitySignature");
            return Result;
        }

        private static CompositionJsonComposition ReadComposition(IDictionary<string, object> Source)
        {
            if (Source == null)
                return null;

            var Result = new CompositionJsonComposition();
            Result.Id = GetString(Source, "id");
            Result.Name = GetString(Source, "name");
            Result.TechName = GetString(Source, "techName");
            Result.Summary = GetString(Source, "summary");
            Result.TechSpec = GetString(Source, "techSpec");
            Result.ViewsPrefix = GetString(Source, "viewsPrefix");
            Result.RootViewId = GetString(Source, "rootViewId");
            Result.ActiveViewId = GetString(Source, "activeViewId");
            Result.Version = ReadVersion(GetDictionary(Source, "version"));
            Result.Domain = ReadDomain(GetDictionary(Source, "domain"));
            return Result;
        }

        private static CompositionJsonDomain ReadDomain(IDictionary<string, object> Source)
        {
            if (Source == null)
                return null;

            var Result = new CompositionJsonDomain();
            Result.Id = GetString(Source, "id");
            Result.Name = GetString(Source, "name");
            Result.TechName = GetString(Source, "techName");
            Result.Summary = GetString(Source, "summary");
            Result.TechSpec = GetString(Source, "techSpec");
            Result.CompatibilitySignature = GetString(Source, "compatibilitySignature");
            Result.Definitions = ReadList(Source, "definitions", ReadDefinition);
            return Result;
        }

        private static CompositionJsonDefinition ReadDefinition(IDictionary<string, object> Source)
        {
            var Result = new CompositionJsonDefinition();
            Result.Id = GetString(Source, "id");
            Result.Kind = GetString(Source, "kind");
            Result.Name = GetString(Source, "name");
            Result.TechName = GetString(Source, "techName");
            Result.Summary = GetString(Source, "summary");
            Result.TechSpec = GetString(Source, "techSpec");
            return Result;
        }

        private static CompositionJsonVersion ReadVersion(IDictionary<string, object> Source)
        {
            if (Source == null)
                return null;

            var Result = new CompositionJsonVersion();
            int VersionSequence;
            if (TryGetInt(Source, "versionSequence", out VersionSequence))
                Result.VersionSequence = VersionSequence;

            Result.VersionNumber = GetString(Source, "versionNumber");
            Result.Annotation = GetString(Source, "annotation");
            Result.Creator = GetString(Source, "creator");
            Result.LastModifier = GetString(Source, "lastModifier");
            Result.Creation = GetString(Source, "creation");
            Result.LastModification = GetString(Source, "lastModification");
            return Result;
        }

        private static CompositionJsonIdea ReadIdea(IDictionary<string, object> Source)
        {
            var Result = new CompositionJsonIdea();
            Result.Id = GetString(Source, "id");
            Result.Kind = GetString(Source, "kind") ?? GetString(Source, "entity") ?? "Concept";
            Result.IsNew = GetBool(Source, "isNew", false) || GetBool(Source, "new", false);
            Result.Delete = GetBool(Source, "delete", false);
            Result.DefinitionId = GetString(Source, "definitionId");
            Result.DefinitionTechName = GetString(Source, "definitionTechName");
            Result.DefinitionName = GetString(Source, "definitionName");
            Result.Name = GetString(Source, "name");
            Result.TechName = GetString(Source, "techName");
            Result.Summary = GetString(Source, "summary");
            Result.TechSpec = GetString(Source, "techSpec");
            Result.ContainerId = GetString(Source, "containerId");
            Result.ContainerTechName = GetString(Source, "containerTechName");
            Result.Visual = ReadVisualControl(GetDictionary(Source, "visual"));
            Result.ChildIdeaIds = ReadStringList(Source, "childIdeaIds");
            Result.CompositeViewIds = ReadStringList(Source, "compositeViewIds");
            Result.Details = ReadList(Source, "details", ReadDetail);
            Result.Markers = ReadList(Source, "markers", ReadMarker);
            return Result;
        }

        private static CompositionJsonRelationship ReadRelationship(IDictionary<string, object> Source)
        {
            var Result = new CompositionJsonRelationship();
            Result.Id = GetString(Source, "id");
            Result.Kind = GetString(Source, "kind") ?? GetString(Source, "entity") ?? "Relationship";
            Result.IsNew = GetBool(Source, "isNew", false) || GetBool(Source, "new", false);
            Result.Delete = GetBool(Source, "delete", false);
            Result.DefinitionId = GetString(Source, "definitionId");
            Result.DefinitionTechName = GetString(Source, "definitionTechName");
            Result.DefinitionName = GetString(Source, "definitionName");
            Result.Name = GetString(Source, "name");
            Result.TechName = GetString(Source, "techName");
            Result.Summary = GetString(Source, "summary");
            Result.TechSpec = GetString(Source, "techSpec");
            Result.ContainerId = GetString(Source, "containerId");
            Result.ContainerTechName = GetString(Source, "containerTechName");
            Result.LayoutRole = GetString(Source, "layoutRole");
            Result.Visual = ReadVisualControl(GetDictionary(Source, "visual"));
            Result.OriginIdeaIds = ReadStringList(Source, "originIdeaIds");
            Result.OriginIdeaTechNames = ReadStringList(Source, "originIdeaTechNames");
            Result.TargetIdeaIds = ReadStringList(Source, "targetIdeaIds");
            Result.TargetIdeaTechNames = ReadStringList(Source, "targetIdeaTechNames");
            Result.Links = ReadList(Source, "links", ReadRelationshipLink);
            Result.ChildIdeaIds = ReadStringList(Source, "childIdeaIds");
            Result.CompositeViewIds = ReadStringList(Source, "compositeViewIds");
            Result.Details = ReadList(Source, "details", ReadDetail);
            Result.Markers = ReadList(Source, "markers", ReadMarker);
            return Result;
        }

        private static CompositionJsonRelationshipLink ReadRelationshipLink(IDictionary<string, object> Source)
        {
            var Result = new CompositionJsonRelationshipLink();
            Result.Id = GetString(Source, "id");
            Result.RoleType = GetString(Source, "roleType");
            Result.RoleDefinitionId = GetString(Source, "roleDefinitionId");
            Result.RoleDefinitionTechName = GetString(Source, "roleDefinitionTechName");
            Result.RoleDefinitionName = GetString(Source, "roleDefinitionName");
            Result.RoleVariantTechName = GetString(Source, "roleVariantTechName");
            Result.RoleVariantName = GetString(Source, "roleVariantName");
            Result.DescriptorName = GetString(Source, "descriptorName");
            Result.DescriptorTechName = GetString(Source, "descriptorTechName");
            Result.DescriptorSummary = GetString(Source, "descriptorSummary");
            Result.IdeaId = GetString(Source, "ideaId");
            Result.IdeaTechName = GetString(Source, "ideaTechName");
            return Result;
        }

        private static CompositionJsonMarker ReadMarker(IDictionary<string, object> Source)
        {
            var Result = new CompositionJsonMarker();
            Result.Delete = GetBool(Source, "delete", false);
            Result.DefinitionId = GetString(Source, "definitionId");
            Result.DefinitionTechName = GetString(Source, "definitionTechName");
            Result.DefinitionName = GetString(Source, "definitionName");
            Result.DescriptorName = GetString(Source, "descriptorName");
            Result.DescriptorTechName = GetString(Source, "descriptorTechName");
            Result.DescriptorSummary = GetString(Source, "descriptorSummary");
            return Result;
        }

        private static CompositionJsonDetail ReadDetail(IDictionary<string, object> Source)
        {
            var Result = new CompositionJsonDetail();
            Result.Delete = GetBool(Source, "delete", false);
            Result.Kind = GetString(Source, "kind");
            Result.DesignatorId = GetString(Source, "designatorId");
            Result.DesignatorTechName = GetString(Source, "designatorTechName");
            Result.DesignatorName = GetString(Source, "designatorName");
            Result.Text = GetString(Source, "text");
            Result.TargetAddress = GetString(Source, "targetAddress");
            Result.TargetPropertyTechName = GetString(Source, "targetPropertyTechName");
            Result.Source = GetString(Source, "source");
            Result.MimeType = GetString(Source, "mimeType");
            Result.Fields = ReadList(Source, "fields", ReadField);
            Result.Records = ReadRecordList(Source, "records");
            return Result;
        }

        private static CompositionJsonField ReadField(IDictionary<string, object> Source)
        {
            var Result = new CompositionJsonField();
            Result.Id = GetString(Source, "id");
            Result.Name = GetString(Source, "name");
            Result.TechName = GetString(Source, "techName");
            Result.DataType = GetString(Source, "dataType");
            return Result;
        }

        private static CompositionJsonView ReadView(IDictionary<string, object> Source)
        {
            var Result = new CompositionJsonView();
            Result.Id = GetString(Source, "id");
            Result.Name = GetString(Source, "name");
            Result.TechName = GetString(Source, "techName");
            Result.Summary = GetString(Source, "summary");
            Result.OwnerIdeaId = GetString(Source, "ownerIdeaId");
            Result.OwnerIdeaTechName = GetString(Source, "ownerIdeaTechName");
            Result.Visuals = ReadList(Source, "visuals", ReadVisual);
            return Result;
        }

        private static CompositionJsonVisual ReadVisual(IDictionary<string, object> Source)
        {
            var Result = new CompositionJsonVisual();
            Result.IdeaId = GetString(Source, "ideaId");
            Result.IdeaTechName = GetString(Source, "ideaTechName");
            Result.RepresentationId = GetString(Source, "representationId");
            Result.IsShortcut = GetBool(Source, "isShortcut", false);
            Result.X = GetNullableDouble(Source, "x");
            Result.Y = GetNullableDouble(Source, "y");
            Result.Width = GetNullableDouble(Source, "width");
            Result.Height = GetNullableDouble(Source, "height");
            Result.Visual = ReadVisualControl(GetDictionary(Source, "visual"));
            return Result;
        }

        private static CompositionJsonOperation ReadOperation(IDictionary<string, object> Source)
        {
            var Result = new CompositionJsonOperation();
            Result.Op = GetString(Source, "op");
            Result.Entity = GetString(Source, "entity");
            Result.Id = GetString(Source, "id");
            Result.RepresentationId = GetString(Source, "representationId");
            Result.TechName = GetString(Source, "techName");
            Result.DefinitionTechName = GetString(Source, "definitionTechName");
            Result.FallbackDefinitionTechName = GetString(Source, "fallbackDefinitionTechName");
            Result.StrictDefinition = GetNullableBool(Source, "strictDefinition");
            Result.ContainerId = GetString(Source, "containerId");
            Result.ContainerTechName = GetString(Source, "containerTechName");
            Result.ViewId = GetString(Source, "viewId");
            Result.ViewTechName = GetString(Source, "viewTechName");
            Result.X = GetNullableDouble(Source, "x");
            Result.Y = GetNullableDouble(Source, "y");
            Result.Width = GetNullableDouble(Source, "width");
            Result.Height = GetNullableDouble(Source, "height");
            Result.AutoPlace = GetNullableBool(Source, "autoPlace");
            Result.AutoFit = GetNullableBool(Source, "autoFit");
            Result.AutoRoute = GetNullableBool(Source, "autoRoute");
            Result.IsShortcut = GetNullableBool(Source, "isShortcut");
            Result.LayoutRole = GetString(Source, "layoutRole");
            Result.Visual = ReadVisualControl(GetDictionary(Source, "visual"));
            Result.OriginIdeaIds = ReadStringList(Source, "originIdeaIds");
            Result.OriginIdeaTechNames = ReadStringList(Source, "originIdeaTechNames");
            Result.TargetIdeaIds = ReadStringList(Source, "targetIdeaIds");
            Result.TargetIdeaTechNames = ReadStringList(Source, "targetIdeaTechNames");
            Result.Links = ReadList(Source, "links", ReadRelationshipLink);
            Result.Details = ReadList(Source, "details", ReadDetail);
            Result.Markers = ReadList(Source, "markers", ReadMarker);
            Result.Set = GetObjectDictionary(Source, "set");
            return Result;
        }

        private static CompositionJsonVisualControl ReadVisualControl(IDictionary<string, object> Source)
        {
            if (Source == null)
                return null;

            var Result = new CompositionJsonVisualControl();
            Result.Role = GetString(Source, "role");
            Result.Display = GetString(Source, "display");
            Result.IncludeInView = GetNullableBool(Source, "includeInView");
            Result.IncludeInArrangement = GetNullableBool(Source, "includeInArrangement");
            Result.IncludeInRouting = GetNullableBool(Source, "includeInRouting");
            Result.IncludeInAutoFit = GetNullableBool(Source, "includeInAutoFit");
            Result.IncludeInOverview = GetNullableBool(Source, "includeInOverview");
            Result.IncludeInFullView = GetNullableBool(Source, "includeInFullView");
            Result.IsShortcut = GetNullableBool(Source, "isShortcut");
            Result.RelationshipCenterPlacement = GetString(Source, "relationshipCenterPlacement");
            return Result;
        }

        private static CompositionJsonGroup ReadGroup(IDictionary<string, object> Source)
        {
            var Result = new CompositionJsonGroup();
            Result.Id = GetString(Source, "id");
            Result.Name = GetString(Source, "name");
            Result.TechName = GetString(Source, "techName");
            Result.MemberIds = ReadStringList(Source, "memberIds");
            Result.MemberTechNames = ReadStringList(Source, "memberTechNames");
            Result.HeaderConceptId = GetString(Source, "headerConceptId");
            Result.HeaderConceptTechName = GetString(Source, "headerConceptTechName");
            Result.CreateGroupRegion = GetNullableBool(Source, "createGroupRegion");
            Result.Padding = GetNullableDouble(Source, "padding");
            Result.SendToBack = GetNullableBool(Source, "sendToBack");
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
                var ItemDictionary = Item as IDictionary<string, object>;
                if (ItemDictionary != null)
                    Result.Add(Reader(ItemDictionary));
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
                Result.Add(CompositionJsonWarningFormatter.Format(Source[Key], Key));
                return Result;
            }

            var Index = 0;
            foreach (var Item in Items)
            {
                Result.Add(CompositionJsonWarningFormatter.Format(Item, Key + "[" + Index.ToString(CultureInfo.InvariantCulture) + "]"));
                Index++;
            }

            return Result;
        }

        private static List<Dictionary<string, object>> ReadRecordList(IDictionary<string, object> Source, string Key)
        {
            var Result = new List<Dictionary<string, object>>();
            if (Source == null || !Source.ContainsKey(Key) || Source[Key] == null)
                return Result;

            var Items = Source[Key] as IEnumerable;
            if (Items == null || Source[Key] is string)
                return Result;

            foreach (var Item in Items)
            {
                var Dictionary = Item as IDictionary<string, object>;
                if (Dictionary == null)
                    continue;

                Result.Add(Dictionary.ToDictionary(Pair => Pair.Key, Pair => Pair.Value));
            }

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

        private static object ToRecordList(IEnumerable<Dictionary<string, object>> Records)
        {
            var Result = new List<object>();
            if (Records == null)
                return Result;

            foreach (var Record in Records)
                Result.Add(ToOrderedDictionary(Record));

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

        private static void AddIfTrue(OrderedDictionary Object, string Key, bool Value)
        {
            if (Value)
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
                    case '"':
                        Builder.Append("\\\"");
                        break;
                    case '\\':
                        Builder.Append("\\\\");
                        break;
                    case '\b':
                        Builder.Append("\\b");
                        break;
                    case '\f':
                        Builder.Append("\\f");
                        break;
                    case '\n':
                        Builder.Append("\\n");
                        break;
                    case '\r':
                        Builder.Append("\\r");
                        break;
                    case '\t':
                        Builder.Append("\\t");
                        break;
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

        public static IDictionary<string, object> GetDictionary(IDictionary<string, object> Source, string Key)
        {
            if (Source == null || !Source.ContainsKey(Key))
                return null;

            return Source[Key] as IDictionary<string, object>;
        }

        public static Dictionary<string, object> GetObjectDictionary(IDictionary<string, object> Source, string Key)
        {
            var Dictionary = GetDictionary(Source, Key);
            if (Dictionary == null)
                return new Dictionary<string, object>();

            return Dictionary.ToDictionary(Pair => Pair.Key, Pair => Pair.Value);
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

        public static int? GetNullableInt(IDictionary<string, object> Source, string Key)
        {
            int Result;
            return TryGetInt(Source, Key, out Result) ? (int?)Result : null;
        }

        public static bool TryGetInt(IDictionary<string, object> Source, string Key, out int Result)
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

        public static bool GetBool(IDictionary<string, object> Source, string Key, bool DefaultValue)
        {
            if (Source == null || !Source.ContainsKey(Key) || Source[Key] == null)
                return DefaultValue;

            if (Source[Key] is bool)
                return (bool)Source[Key];

            bool Result;
            return Boolean.TryParse(Convert.ToString(Source[Key], CultureInfo.InvariantCulture), out Result) ? Result : DefaultValue;
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

        public static double? GetNullableDouble(IDictionary<string, object> Source, string Key)
        {
            if (Source == null || !Source.ContainsKey(Key) || Source[Key] == null)
                return null;

            if (Source[Key] is double)
                return (double)Source[Key];

            if (Source[Key] is int)
                return Convert.ToDouble(Source[Key], CultureInfo.InvariantCulture);

            double Result;
            return Double.TryParse(Convert.ToString(Source[Key], CultureInfo.InvariantCulture),
                                   NumberStyles.Float, CultureInfo.InvariantCulture, out Result)
                   ? (double?)Result : null;
        }
    }
}
