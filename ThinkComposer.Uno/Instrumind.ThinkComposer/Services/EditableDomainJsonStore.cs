#nullable disable

namespace Instrumind.ThinkComposer.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Instrumind.Common.Platform;
using Instrumind.Common.Portable;

public sealed class EditableDomainJsonStore : IEditableDomainStore
{
    public string GetSidecarPath(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return string.Empty;

        var extension = Path.GetExtension(sourcePath);
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            return sourcePath;

        if (extension.Equals(".tdom", StringComparison.OrdinalIgnoreCase))
            return sourcePath + ".json";

        if (extension.Equals(".tcom", StringComparison.OrdinalIgnoreCase))
            return sourcePath + ".domain.json";

        return sourcePath + ".domain.json";
    }

    public async Task<EditableDomainModel> TryLoadAsync(string sourcePath, CancellationToken cancellationToken)
    {
        var path = GetSidecarPath(sourcePath);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var document = JsonSerializer.Deserialize(json, EditableDomainJsonContext.Default.DomainJsonDocument);
        if (document == null)
            return null;

        var model = FromDocument(document);
        model.SourcePath = sourcePath;
        model.SidecarPath = path;
        model.IsProjectedFromLegacyPackage = false;
        model.IsDirty = false;
        return model;
    }

    public async Task SaveAsync(EditableDomainModel domain, CancellationToken cancellationToken)
    {
        if (domain == null)
            throw new ArgumentNullException(nameof(domain));

        var path = string.IsNullOrWhiteSpace(domain.SidecarPath)
            ? GetSidecarPath(domain.SourcePath)
            : domain.SidecarPath;

        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("The editable domain does not have a source or sidecar path.");

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(ToDocument(domain), EditableDomainJsonContext.Default.DomainJsonDocument);
        await File.WriteAllTextAsync(path, json, cancellationToken);
        domain.SidecarPath = path;
        domain.IsDirty = false;
        domain.IsProjectedFromLegacyPackage = false;
    }

    private static EditableDomainModel FromDocument(DomainJsonDocument document)
    {
        var domainElement = document.Domain ?? new DomainJsonElement();
        var model = new EditableDomainModel
        {
            Id = FirstText(domainElement.Id, Guid.NewGuid().ToString("D")),
            Name = FirstText(domainElement.Name, "Untitled Domain"),
            TechName = FirstText(domainElement.TechName, EditableDomainNaming.ToTechName(domainElement.Name)),
            Summary = FirstText(domainElement.Summary, string.Empty),
            ConceptDefinitions = (document.ConceptDefinitions ?? new List<DomainJsonElement>())
                .Select(FromConceptElement)
                .ToList(),
            RelationshipDefinitions = (document.RelationshipDefinitions ?? new List<DomainJsonElement>())
                .Select(FromRelationshipElement)
                .ToList(),
            MarkerDefinitions = (document.MarkerDefinitions ?? new List<DomainJsonElement>())
                .Select(FromMarkerElement)
                .ToList(),
            ComplementDefinitions = (document.ComplementDefinitions ?? new List<DomainJsonElement>())
                .Select(FromComplementElement)
                .ToList()
        };

        return model;
    }

    private static DomainJsonDocument ToDocument(EditableDomainModel model)
    {
        return new DomainJsonDocument
        {
            Format = "ThinkComposer.Domain.Editable.v1",
            Domain = new DomainJsonElement
            {
                Id = model.Id,
                Entity = "Domain",
                Name = model.Name,
                TechName = model.TechName,
                Summary = model.Summary
            },
            ConceptDefinitions = model.ConceptDefinitions
                .Select(ToConceptElement)
                .ToList(),
            RelationshipDefinitions = model.RelationshipDefinitions
                .Select(ToRelationshipElement)
                .ToList(),
            MarkerDefinitions = model.MarkerDefinitions
                .Select(ToMarkerElement)
                .ToList(),
            ComplementDefinitions = model.ComplementDefinitions
                .Select(ToComplementElement)
                .ToList()
        };
    }

    private static EditableConceptDefinition FromConceptElement(DomainJsonElement element)
    {
        var concept = EditableConceptDefinition.CreateDefault(
            FirstText(element.Name, "Concept"),
            ThinkComposerVisualCatalog.NormalizeShapeTechName(element.RepresentativeShape, "Capsule"),
            element.SymbolFormat?.FillColorHex ?? "#FFFFE540",
            element.SymbolFormat?.StrokeColorHex ?? "#FFD4A900");

        concept.Id = FirstText(element.Id, Guid.NewGuid().ToString("D"));
        concept.TechName = FirstText(element.TechName, EditableDomainNaming.ToTechName(concept.Name));
        concept.Summary = FirstText(element.Summary, string.Empty);
        concept.Description = FirstText(element.Description, string.Empty);
        concept.TechSpec = FirstText(element.TechSpec, string.Empty);
        concept.PictogramAsset = FirstText(element.PictogramAsset, string.Empty);
        concept.ClusterTechName = FirstText(element.ClusterTechName, string.Empty);
        concept.IsComposable = element.IsComposable ?? true;
        concept.IsVersionable = element.IsVersionable ?? false;
        concept.PreciseConnectByDefault = element.PreciseConnectByDefault ?? false;
        concept.HasGroupRegion = element.HasGroupRegion ?? false;
        concept.HasGroupLine = element.HasGroupLine ?? false;
        concept.CanAutomaticallyCreateRelatedConcepts = element.CanAutomaticallyCreateRelatedConcepts ?? false;
        concept.AutomaticCreationConceptTechName = FirstText(element.AutomaticCreationConceptTechName, string.Empty);
        concept.AutomaticCreationRelationshipTechName = FirstText(element.AutomaticCreationRelationshipTechName, string.Empty);
        concept.AutomaticCreationPositioningIsRadialized = element.AutomaticCreationPositioningIsRadialized ?? true;
        concept.AutomaticCreationPositioningMode = FirstText(element.AutomaticCreationPositioningMode, "Vertical Alternated");
        concept.CanGroupIntersectingObjects = element.CanGroupIntersectingObjects ?? false;
        concept.CanAutomaticallyCreateGroupedConcepts = element.CanAutomaticallyCreateGroupedConcepts ?? false;
        concept.AutomaticGroupedConceptTechName = FirstText(element.AutomaticGroupedConceptTechName, string.Empty);

        if (element.SymbolFormat != null)
        {
            concept.Symbol = new EditableConceptSymbolFormat
            {
                Shape = ThinkComposerVisualCatalog.NormalizeShapeTechName(element.SymbolFormat.Shape, concept.RepresentativeShape),
                FillColorHex = FirstText(element.SymbolFormat.FillColorHex, concept.Symbol.FillColorHex),
                StrokeColorHex = FirstText(element.SymbolFormat.StrokeColorHex, concept.Symbol.StrokeColorHex),
                LineThickness = element.SymbolFormat.LineThickness ?? concept.Symbol.LineThickness,
                InitialWidth = element.SymbolFormat.InitialWidth ?? concept.Symbol.InitialWidth,
                InitialHeight = element.SymbolFormat.InitialHeight ?? concept.Symbol.InitialHeight,
                UseNameAsMainTitle = element.SymbolFormat.UseNameAsMainTitle ?? true,
                ShowGlobalDetailsFirst = element.SymbolFormat.ShowGlobalDetailsFirst ?? true,
                SubtitleVisualDisposition = FirstText(element.SymbolFormat.SubtitleVisualDisposition, "Hidden"),
                PictogramVisualDisposition = FirstText(element.SymbolFormat.PictogramVisualDisposition, "Right"),
                FlippedHorizontally = element.SymbolFormat.FlippedHorizontally ?? false,
                FlippedVertically = element.SymbolFormat.FlippedVertically ?? false,
                Tilted = element.SymbolFormat.Tilted ?? false,
                AsMultiple = element.SymbolFormat.AsMultiple ?? false
            };
        }

        if (element.Details != null && element.Details.Count > 0)
            concept.Details = element.Details.Select(FromDetailElement).ToList();

        if (element.OutputTemplates != null && element.OutputTemplates.Count > 0)
            concept.OutputTemplates = element.OutputTemplates.Select(FromOutputTemplateElement).ToList();

        return concept;
    }

    private static DomainJsonElement ToConceptElement(EditableConceptDefinition concept)
    {
        return new DomainJsonElement
        {
            Id = concept.Id,
            Entity = "ConceptDefinition",
            Name = concept.Name,
            TechName = concept.TechName,
            Summary = concept.Summary,
            Description = concept.Description,
            TechSpec = concept.TechSpec,
            PictogramAsset = concept.PictogramAsset,
            ClusterTechName = concept.ClusterTechName,
            RepresentativeShape = ThinkComposerVisualCatalog.NormalizeShapeTechName(concept.RepresentativeShape, "Capsule"),
            IsComposable = concept.IsComposable,
            IsVersionable = concept.IsVersionable,
            PreciseConnectByDefault = concept.PreciseConnectByDefault,
            HasGroupRegion = concept.HasGroupRegion,
            HasGroupLine = concept.HasGroupLine,
            CanAutomaticallyCreateRelatedConcepts = concept.CanAutomaticallyCreateRelatedConcepts,
            AutomaticCreationConceptTechName = concept.AutomaticCreationConceptTechName,
            AutomaticCreationRelationshipTechName = concept.AutomaticCreationRelationshipTechName,
            AutomaticCreationPositioningIsRadialized = concept.AutomaticCreationPositioningIsRadialized,
            AutomaticCreationPositioningMode = concept.AutomaticCreationPositioningMode,
            CanGroupIntersectingObjects = concept.CanGroupIntersectingObjects,
            CanAutomaticallyCreateGroupedConcepts = concept.CanAutomaticallyCreateGroupedConcepts,
            AutomaticGroupedConceptTechName = concept.AutomaticGroupedConceptTechName,
            SymbolFormat = ToSymbolElement(concept.Symbol),
            Details = concept.Details.Select(ToDetailElement).ToList(),
            OutputTemplates = concept.OutputTemplates.Select(ToOutputTemplateElement).ToList()
        };
    }

    private static EditableRelationshipDefinition FromRelationshipElement(DomainJsonElement element)
    {
        var relationship = EditableRelationshipDefinition.CreateDefault(
            FirstText(element.Name, "Relationship"),
            element.SymbolFormat?.StrokeColorHex ?? element.ConnectorFormat?.LineColorHex ?? "#FF64748B");

        relationship.Id = FirstText(element.Id, Guid.NewGuid().ToString("D"));
        relationship.TechName = FirstText(element.TechName, EditableDomainNaming.ToTechName(relationship.Name));
        relationship.Summary = FirstText(element.Summary, string.Empty);
        relationship.Description = FirstText(element.Description, string.Empty);
        relationship.TechSpec = FirstText(element.TechSpec, string.Empty);
        relationship.PictogramAsset = FirstText(element.PictogramAsset, string.Empty);
        relationship.ClusterTechName = FirstText(element.ClusterTechName, string.Empty);
        relationship.AncestorRelationshipTechName = FirstText(element.AncestorRelationshipTechName, string.Empty);
        relationship.RepresentativeShape = ThinkComposerVisualCatalog.NormalizeShapeTechName(element.RepresentativeShape, "Ellipse");
        relationship.IsComposable = element.IsComposable ?? true;
        relationship.IsVersionable = element.IsVersionable ?? false;
        relationship.PreciseConnectByDefault = element.PreciseConnectByDefault ?? false;
        relationship.HasGroupRegion = element.HasGroupRegion ?? false;
        relationship.HasGroupLine = element.HasGroupLine ?? false;
        relationship.CanAutomaticallyCreateRelatedConcepts = element.CanAutomaticallyCreateRelatedConcepts ?? false;
        relationship.CanGroupIntersectingObjects = element.CanGroupIntersectingObjects ?? false;
        relationship.CanAutomaticallyCreateGroupedConcepts = element.CanAutomaticallyCreateGroupedConcepts ?? false;
        relationship.AutomaticGroupedConceptTechName = FirstText(element.AutomaticGroupedConceptTechName, string.Empty);
        relationship.IsDirectional = element.IsDirectional ?? true;
        relationship.IsSimple = element.IsSimple ?? false;
        relationship.HideCentralSymbolWhenSimple = element.HideCentralSymbolWhenSimple ?? false;
        relationship.ShowNameIfHidingCentralSymbol = element.ShowNameIfHidingCentralSymbol ?? true;

        if (element.SymbolFormat != null)
            relationship.Symbol = FromSymbolElement(element.SymbolFormat, relationship.RepresentativeShape, "#FFE5E7EB", "#FF64748B");

        if (element.ConnectorFormat != null)
            relationship.Connector = FromConnectorElement(element.ConnectorFormat);

        if (element.OriginRole != null)
            relationship.OriginRole = FromLinkRoleElement(element.OriginRole, "Origin");

        if (element.TargetRole != null)
            relationship.TargetRole = FromLinkRoleElement(element.TargetRole, "Target");

        if (element.Details != null && element.Details.Count > 0)
            relationship.Details = element.Details.Select(FromDetailElement).ToList();

        if (element.OutputTemplates != null && element.OutputTemplates.Count > 0)
            relationship.OutputTemplates = element.OutputTemplates.Select(FromOutputTemplateElement).ToList();

        return relationship;
    }

    private static DomainJsonElement ToRelationshipElement(EditableRelationshipDefinition relationship)
    {
        return new DomainJsonElement
        {
            Id = relationship.Id,
            Entity = "RelationshipDefinition",
            Name = relationship.Name,
            TechName = relationship.TechName,
            Summary = relationship.Summary,
            Description = relationship.Description,
            TechSpec = relationship.TechSpec,
            PictogramAsset = relationship.PictogramAsset,
            ClusterTechName = relationship.ClusterTechName,
            AncestorRelationshipTechName = relationship.AncestorRelationshipTechName,
            RepresentativeShape = ThinkComposerVisualCatalog.NormalizeShapeTechName(relationship.RepresentativeShape, "Ellipse"),
            IsComposable = relationship.IsComposable,
            IsVersionable = relationship.IsVersionable,
            PreciseConnectByDefault = relationship.PreciseConnectByDefault,
            HasGroupRegion = relationship.HasGroupRegion,
            HasGroupLine = relationship.HasGroupLine,
            CanAutomaticallyCreateRelatedConcepts = relationship.CanAutomaticallyCreateRelatedConcepts,
            CanGroupIntersectingObjects = relationship.CanGroupIntersectingObjects,
            CanAutomaticallyCreateGroupedConcepts = relationship.CanAutomaticallyCreateGroupedConcepts,
            AutomaticGroupedConceptTechName = relationship.AutomaticGroupedConceptTechName,
            IsDirectional = relationship.IsDirectional,
            IsSimple = relationship.IsSimple,
            HideCentralSymbolWhenSimple = relationship.HideCentralSymbolWhenSimple,
            ShowNameIfHidingCentralSymbol = relationship.ShowNameIfHidingCentralSymbol,
            SymbolFormat = ToSymbolElement(relationship.Symbol),
            ConnectorFormat = ToConnectorElement(relationship.Connector),
            OriginRole = ToLinkRoleElement(relationship.OriginRole),
            TargetRole = ToLinkRoleElement(relationship.TargetRole),
            Details = relationship.Details.Select(ToDetailElement).ToList(),
            OutputTemplates = relationship.OutputTemplates.Select(ToOutputTemplateElement).ToList()
        };
    }

    private static EditableMarkerDefinition FromMarkerElement(DomainJsonElement element)
    {
        var marker = EditableMarkerDefinition.CreateDefault(
            FirstText(element.Name, "Marker"),
            FirstText(element.BackgroundColorHex, element.AccentColorHex ?? "#FFFBBF24"));

        marker.Id = FirstText(element.Id, Guid.NewGuid().ToString("D"));
        marker.TechName = FirstText(element.TechName, EditableDomainNaming.ToTechName(marker.Name));
        marker.Summary = FirstText(element.Summary, string.Empty);
        marker.PictogramAsset = FirstText(element.PictogramAsset, string.Empty);
        marker.ClusterKey = FirstText(element.ClusterKey, "UserDef");
        marker.BackgroundColorHex = FirstText(element.BackgroundColorHex, marker.BackgroundColorHex);
        marker.ForegroundColorHex = FirstText(element.ForegroundColorHex, "#FF111827");
        return marker;
    }

    private static DomainJsonElement ToMarkerElement(EditableMarkerDefinition marker)
    {
        return new DomainJsonElement
        {
            Id = marker.Id,
            Entity = "MarkerDefinition",
            Name = marker.Name,
            TechName = marker.TechName,
            Summary = marker.Summary,
            PictogramAsset = marker.PictogramAsset,
            ClusterKey = marker.ClusterKey,
            BackgroundColorHex = marker.BackgroundColorHex,
            ForegroundColorHex = marker.ForegroundColorHex,
            AccentColorHex = marker.BackgroundColorHex
        };
    }

    private static EditableComplementDefinition FromComplementElement(DomainJsonElement element)
    {
        var complement = EditableComplementDefinition.CreateDefault(
            FirstText(element.Name, "Text"),
            FirstText(element.BackgroundColorHex, element.AccentColorHex ?? "#FF3B82F6"));

        complement.Id = FirstText(element.Id, Guid.NewGuid().ToString("D"));
        complement.TechName = FirstText(element.TechName, EditableDomainNaming.ToTechName(complement.Name));
        complement.Summary = FirstText(element.Summary, string.Empty);
        complement.PictogramAsset = FirstText(element.PictogramAsset, string.Empty);
        complement.Kind = FirstText(element.Kind, complement.Name);
        complement.Text = FirstText(element.Text, string.Empty);
        complement.ImageAsset = FirstText(element.ImageAsset, string.Empty);
        complement.ForegroundColorHex = FirstText(element.ForegroundColorHex, complement.ForegroundColorHex);
        complement.BackgroundColorHex = FirstText(element.BackgroundColorHex, complement.BackgroundColorHex);
        complement.LineDash = FirstText(element.LineDash, "Solid");
        complement.LineThickness = element.LineThickness ?? 1.5;
        complement.Orientation = FirstText(element.Orientation, "Horizontal");
        complement.Quadrant = FirstText(element.Quadrant, "TopRight");
        complement.OffsetX = element.OffsetX ?? 0;
        complement.OffsetY = element.OffsetY ?? 0;
        complement.InitialWidth = element.InitialWidth ?? 180;
        complement.InitialHeight = element.InitialHeight ?? 80;
        return complement;
    }

    private static DomainJsonElement ToComplementElement(EditableComplementDefinition complement)
    {
        return new DomainJsonElement
        {
            Id = complement.Id,
            Entity = "ComplementDefinition",
            Name = complement.Name,
            TechName = complement.TechName,
            Summary = complement.Summary,
            PictogramAsset = complement.PictogramAsset,
            Kind = complement.Kind,
            Text = complement.Text,
            ImageAsset = complement.ImageAsset,
            ForegroundColorHex = complement.ForegroundColorHex,
            BackgroundColorHex = complement.BackgroundColorHex,
            AccentColorHex = complement.BackgroundColorHex,
            LineDash = complement.LineDash,
            LineThickness = complement.LineThickness,
            Orientation = complement.Orientation,
            Quadrant = complement.Quadrant,
            OffsetX = complement.OffsetX,
            OffsetY = complement.OffsetY,
            InitialWidth = complement.InitialWidth,
            InitialHeight = complement.InitialHeight
        };
    }

    private static EditableDefinitionReference FromReferenceElement(DomainJsonElement element, string defaultAccent)
    {
        return new EditableDefinitionReference
        {
            Id = FirstText(element.Id, Guid.NewGuid().ToString("D")),
            Name = FirstText(element.Name, string.Empty),
            TechName = FirstText(element.TechName, EditableDomainNaming.ToTechName(element.Name)),
            AccentColorHex = FirstText(element.AccentColorHex, defaultAccent)
        };
    }

    private static DomainJsonElement ToReferenceElement(EditableDefinitionReference reference, string entity)
    {
        return new DomainJsonElement
        {
            Id = reference.Id,
            Entity = entity,
            Name = reference.Name,
            TechName = reference.TechName,
            AccentColorHex = reference.AccentColorHex
        };
    }

    private static EditableConceptSymbolFormat FromSymbolElement(
        DomainJsonSymbolFormat symbol,
        string defaultShape,
        string defaultFill,
        string defaultStroke)
    {
        var result = EditableConceptSymbolFormat.CreateDefault();
        result.Shape = ThinkComposerVisualCatalog.NormalizeShapeTechName(symbol.Shape, defaultShape);
        result.FillColorHex = FirstText(symbol.FillColorHex, defaultFill);
        result.StrokeColorHex = FirstText(symbol.StrokeColorHex, defaultStroke);
        result.LineThickness = symbol.LineThickness ?? result.LineThickness;
        result.InitialWidth = symbol.InitialWidth ?? result.InitialWidth;
        result.InitialHeight = symbol.InitialHeight ?? result.InitialHeight;
        result.UseNameAsMainTitle = symbol.UseNameAsMainTitle ?? true;
        result.ShowGlobalDetailsFirst = symbol.ShowGlobalDetailsFirst ?? true;
        result.SubtitleVisualDisposition = FirstText(symbol.SubtitleVisualDisposition, "Hidden");
        result.PictogramVisualDisposition = FirstText(symbol.PictogramVisualDisposition, "Right");
        result.FlippedHorizontally = symbol.FlippedHorizontally ?? false;
        result.FlippedVertically = symbol.FlippedVertically ?? false;
        result.Tilted = symbol.Tilted ?? false;
        result.AsMultiple = symbol.AsMultiple ?? false;
        return result;
    }

    private static EditableConnectorFormat FromConnectorElement(DomainJsonConnectorFormat connector)
    {
        return new EditableConnectorFormat
        {
            LineColorHex = FirstText(connector.LineColorHex, "#FF111827"),
            MainBackgroundColorHex = FirstText(connector.MainBackgroundColorHex, "#00FFFFFF"),
            LineThickness = connector.LineThickness ?? 1.5,
            LineDash = FirstText(connector.LineDash, "Solid"),
            HeadPlug = ThinkComposerVisualCatalog.NormalizeConnectorPlugTechName(connector.HeadPlug, "SimpleArrow"),
            TailPlug = ThinkComposerVisualCatalog.NormalizeConnectorPlugTechName(connector.TailPlug, "None"),
            HeadVariantTechName = ThinkComposerVisualCatalog.NormalizeLinkRoleVariantTechName(connector.HeadVariantTechName, "Standard"),
            TailVariantTechName = ThinkComposerVisualCatalog.NormalizeLinkRoleVariantTechName(connector.TailVariantTechName, "Standard"),
            PathStyle = FirstText(connector.PathStyle, "Straight"),
            PathCorner = FirstText(connector.PathCorner, "Sharp"),
            LabelLinkDescriptor = connector.LabelLinkDescriptor ?? true,
            LabelLinkDefinitor = connector.LabelLinkDefinitor ?? false,
            LabelLinkVariant = connector.LabelLinkVariant ?? false
        };
    }

    private static DomainJsonConnectorFormat ToConnectorElement(EditableConnectorFormat connector)
    {
        connector ??= EditableConnectorFormat.CreateDefault();

        return new DomainJsonConnectorFormat
        {
            LineColorHex = connector.LineColorHex,
            MainBackgroundColorHex = connector.MainBackgroundColorHex,
            LineThickness = connector.LineThickness,
            LineDash = connector.LineDash,
            HeadPlug = connector.HeadPlug,
            TailPlug = connector.TailPlug,
            HeadVariantTechName = connector.HeadVariantTechName,
            TailVariantTechName = connector.TailVariantTechName,
            PathStyle = connector.PathStyle,
            PathCorner = connector.PathCorner,
            LabelLinkDescriptor = connector.LabelLinkDescriptor,
            LabelLinkDefinitor = connector.LabelLinkDefinitor,
            LabelLinkVariant = connector.LabelLinkVariant
        };
    }

    private static EditableLinkRoleDefinition FromLinkRoleElement(DomainJsonLinkRole role, string fallbackName)
    {
        return new EditableLinkRoleDefinition
        {
            Name = FirstText(role.Name, fallbackName),
            TechName = FirstText(role.TechName, EditableDomainNaming.ToTechName(fallbackName)),
            Summary = FirstText(role.Summary, string.Empty),
            RoleType = FirstText(role.RoleType, string.Equals(fallbackName, "Target", StringComparison.OrdinalIgnoreCase) ? "Target" : "Origin"),
            PictogramAsset = FirstText(role.PictogramAsset, string.Empty),
            MaxConnections = role.MaxConnections ?? 1,
            RelatedIdeasAreOrdered = role.RelatedIdeasAreOrdered ?? false,
            AllowedVariants = FirstText(role.AllowedVariants, "Standard"),
            AssociableConcepts = FirstText(role.AssociableConcepts, string.Empty)
        };
    }

    private static DomainJsonLinkRole ToLinkRoleElement(EditableLinkRoleDefinition role)
    {
        role ??= EditableLinkRoleDefinition.Create("Role");

        return new DomainJsonLinkRole
        {
            Name = role.Name,
            TechName = role.TechName,
            Summary = role.Summary,
            RoleType = role.RoleType,
            PictogramAsset = role.PictogramAsset,
            MaxConnections = role.MaxConnections,
            RelatedIdeasAreOrdered = role.RelatedIdeasAreOrdered,
            AllowedVariants = role.AllowedVariants,
            AssociableConcepts = role.AssociableConcepts
        };
    }

    private static DomainJsonSymbolFormat ToSymbolElement(EditableConceptSymbolFormat symbol)
    {
        symbol ??= EditableConceptSymbolFormat.CreateDefault();

        return new DomainJsonSymbolFormat
        {
            Shape = ThinkComposerVisualCatalog.NormalizeShapeTechName(symbol.Shape, "Rectangle"),
            FillColorHex = symbol.FillColorHex,
            StrokeColorHex = symbol.StrokeColorHex,
            LineThickness = symbol.LineThickness,
            InitialWidth = symbol.InitialWidth,
            InitialHeight = symbol.InitialHeight,
            UseNameAsMainTitle = symbol.UseNameAsMainTitle,
            ShowGlobalDetailsFirst = symbol.ShowGlobalDetailsFirst,
            SubtitleVisualDisposition = symbol.SubtitleVisualDisposition,
            PictogramVisualDisposition = symbol.PictogramVisualDisposition,
            FlippedHorizontally = symbol.FlippedHorizontally,
            FlippedVertically = symbol.FlippedVertically,
            Tilted = symbol.Tilted,
            AsMultiple = symbol.AsMultiple
        };
    }

    private static EditableDetailDesignator FromDetailElement(DomainJsonDetailDesignator detail)
    {
        return new EditableDetailDesignator
        {
            Title = FirstText(detail.Title, string.Empty),
            Kind = FirstText(detail.Kind, "Link"),
            IsDisplayed = detail.IsDisplayed ?? true,
            ShowTitle = detail.ShowTitle ?? true,
            IsMultiRecord = detail.IsMultiRecord ?? false,
            Layout = FirstText(detail.Layout, "Transposed"),
            ShowFieldTitles = detail.ShowFieldTitles ?? true
        };
    }

    private static DomainJsonDetailDesignator ToDetailElement(EditableDetailDesignator detail)
    {
        return new DomainJsonDetailDesignator
        {
            Title = detail.Title,
            Kind = detail.Kind,
            IsDisplayed = detail.IsDisplayed,
            ShowTitle = detail.ShowTitle,
            IsMultiRecord = detail.IsMultiRecord,
            Layout = detail.Layout,
            ShowFieldTitles = detail.ShowFieldTitles
        };
    }

    private static EditableOutputTemplate FromOutputTemplateElement(DomainJsonOutputTemplate template)
    {
        return new EditableOutputTemplate
        {
            Language = FirstText(template.Language, "Text"),
            TemplateText = FirstText(template.TemplateText, string.Empty),
            ExtendsBaseTemplate = template.ExtendsBaseTemplate ?? true
        };
    }

    private static DomainJsonOutputTemplate ToOutputTemplateElement(EditableOutputTemplate template)
    {
        return new DomainJsonOutputTemplate
        {
            Language = template.Language,
            TemplateText = template.TemplateText,
            ExtendsBaseTemplate = template.ExtendsBaseTemplate
        };
    }

    private static string FirstText(string first, string fallback)
    {
        return string.IsNullOrWhiteSpace(first) ? fallback ?? string.Empty : first.Trim();
    }
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(DomainJsonDocument))]
internal sealed partial class EditableDomainJsonContext : JsonSerializerContext
{
}

internal sealed class DomainJsonDocument
{
    public string Format { get; set; }

    public DomainJsonElement Domain { get; set; }

    public List<DomainJsonElement> ConceptDefinitions { get; set; }

    public List<DomainJsonElement> RelationshipDefinitions { get; set; }

    public List<DomainJsonElement> MarkerDefinitions { get; set; }

    public List<DomainJsonElement> ComplementDefinitions { get; set; }
}

internal sealed class DomainJsonElement
{
    public string Id { get; set; }
    public string Entity { get; set; }
    public string Name { get; set; }
    public string TechName { get; set; }
    public string Summary { get; set; }
    public string Description { get; set; }
    public string TechSpec { get; set; }
    public string PictogramAsset { get; set; }
    public string ClusterTechName { get; set; }
    public string CategoryTechName { get; set; }
    public string AncestorRelationshipTechName { get; set; }
    public string RepresentativeShape { get; set; }
    public string AccentColorHex { get; set; }
    public string ClusterKey { get; set; }
    public string BackgroundColorHex { get; set; }
    public string ForegroundColorHex { get; set; }
    public string Kind { get; set; }
    public string Text { get; set; }
    public string ImageAsset { get; set; }
    public string LineDash { get; set; }
    public double? LineThickness { get; set; }
    public string Orientation { get; set; }
    public string Quadrant { get; set; }
    public double? OffsetX { get; set; }
    public double? OffsetY { get; set; }
    public double? InitialWidth { get; set; }
    public double? InitialHeight { get; set; }
    public bool? IsComposable { get; set; }
    public bool? IsVersionable { get; set; }
    public bool? PreciseConnectByDefault { get; set; }
    public bool? HasGroupRegion { get; set; }
    public bool? HasGroupLine { get; set; }
    public bool? CanAutomaticallyCreateRelatedConcepts { get; set; }
    public string AutomaticCreationConceptTechName { get; set; }
    public string AutomaticCreationRelationshipTechName { get; set; }
    public bool? AutomaticCreationPositioningIsRadialized { get; set; }
    public string AutomaticCreationPositioningMode { get; set; }
    public bool? CanGroupIntersectingObjects { get; set; }
    public bool? CanAutomaticallyCreateGroupedConcepts { get; set; }
    public string AutomaticGroupedConceptTechName { get; set; }
    public bool? IsDirectional { get; set; }
    public bool? IsSimple { get; set; }
    public bool? HideCentralSymbolWhenSimple { get; set; }
    public bool? ShowNameIfHidingCentralSymbol { get; set; }
    public DomainJsonSymbolFormat SymbolFormat { get; set; }
    public DomainJsonConnectorFormat ConnectorFormat { get; set; }
    public DomainJsonLinkRole OriginRole { get; set; }
    public DomainJsonLinkRole TargetRole { get; set; }
    public List<DomainJsonDetailDesignator> Details { get; set; }
    public List<DomainJsonOutputTemplate> OutputTemplates { get; set; }
}

internal sealed class DomainJsonConnectorFormat
{
    public string LineColorHex { get; set; }
    public string MainBackgroundColorHex { get; set; }
    public double? LineThickness { get; set; }
    public string LineDash { get; set; }
    public string HeadPlug { get; set; }
    public string TailPlug { get; set; }
    public string HeadVariantTechName { get; set; }
    public string TailVariantTechName { get; set; }
    public string PathStyle { get; set; }
    public string PathCorner { get; set; }
    public bool? LabelLinkDescriptor { get; set; }
    public bool? LabelLinkDefinitor { get; set; }
    public bool? LabelLinkVariant { get; set; }
}

internal sealed class DomainJsonLinkRole
{
    public string Name { get; set; }
    public string TechName { get; set; }
    public string Summary { get; set; }
    public string RoleType { get; set; }
    public string PictogramAsset { get; set; }
    public int? MaxConnections { get; set; }
    public bool? RelatedIdeasAreOrdered { get; set; }
    public string AllowedVariants { get; set; }
    public string AssociableConcepts { get; set; }
}

internal sealed class DomainJsonSymbolFormat
{
    public string Shape { get; set; }
    public string FillColorHex { get; set; }
    public string StrokeColorHex { get; set; }
    public double? LineThickness { get; set; }
    public double? InitialWidth { get; set; }
    public double? InitialHeight { get; set; }
    public bool? UseNameAsMainTitle { get; set; }
    public bool? ShowGlobalDetailsFirst { get; set; }
    public string SubtitleVisualDisposition { get; set; }
    public string PictogramVisualDisposition { get; set; }
    public bool? FlippedHorizontally { get; set; }
    public bool? FlippedVertically { get; set; }
    public bool? Tilted { get; set; }
    public bool? AsMultiple { get; set; }
}

internal sealed class DomainJsonDetailDesignator
{
    public string Title { get; set; }
    public string Kind { get; set; }
    public bool? IsDisplayed { get; set; }
    public bool? ShowTitle { get; set; }
    public bool? IsMultiRecord { get; set; }
    public string Layout { get; set; }
    public bool? ShowFieldTitles { get; set; }
}

internal sealed class DomainJsonOutputTemplate
{
    public string Language { get; set; }
    public string TemplateText { get; set; }
    public bool? ExtendsBaseTemplate { get; set; }
}
