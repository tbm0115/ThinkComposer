using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Instrumind.Common.Portable
{
    public sealed class EditableDomainModel
    {
        public EditableDomainModel()
        {
            Id = Guid.NewGuid().ToString("D");
            Name = "Untitled Domain";
            TechName = "Untitled_Domain";
            ConceptDefinitions = new List<EditableConceptDefinition>();
            RelationshipDefinitions = new List<EditableRelationshipDefinition>();
            MarkerDefinitions = new List<EditableMarkerDefinition>();
            ComplementDefinitions = new List<EditableComplementDefinition>();
        }

        public string Id { get; set; }

        public string Name { get; set; }

        public string TechName { get; set; }

        public string Summary { get; set; }

        public string SourcePath { get; set; }

        public string SidecarPath { get; set; }

        public bool IsProjectedFromLegacyPackage { get; set; }

        public bool IsDirty { get; set; }

        public List<EditableConceptDefinition> ConceptDefinitions { get; set; }

        public List<EditableRelationshipDefinition> RelationshipDefinitions { get; set; }

        public List<EditableMarkerDefinition> MarkerDefinitions { get; set; }

        public List<EditableComplementDefinition> ComplementDefinitions { get; set; }
    }

    public sealed class EditableDefinitionReference
    {
        public EditableDefinitionReference()
        {
            Id = Guid.NewGuid().ToString("D");
            Name = string.Empty;
            TechName = string.Empty;
            AccentColorHex = "#FF64748B";
        }

        public string Id { get; set; }

        public string Name { get; set; }

        public string TechName { get; set; }

        public string AccentColorHex { get; set; }

        public static EditableDefinitionReference Create(string name, string accentColorHex)
        {
            return new EditableDefinitionReference
            {
                Name = name ?? string.Empty,
                TechName = EditableDomainNaming.ToTechName(name),
                AccentColorHex = string.IsNullOrWhiteSpace(accentColorHex) ? "#FF64748B" : accentColorHex
            };
        }
    }

    public sealed class EditableRelationshipDefinition
    {
        public EditableRelationshipDefinition()
        {
            Id = Guid.NewGuid().ToString("D");
            Name = "Relationship";
            TechName = "Relationship";
            Summary = string.Empty;
            Description = string.Empty;
            TechSpec = string.Empty;
            PictogramAsset = string.Empty;
            ClusterTechName = string.Empty;
            AncestorRelationshipTechName = string.Empty;
            RepresentativeShape = "Ellipse";
            IsComposable = true;
            IsVersionable = false;
            PreciseConnectByDefault = false;
            HasGroupRegion = false;
            HasGroupLine = false;
            CanAutomaticallyCreateRelatedConcepts = false;
            CanGroupIntersectingObjects = false;
            CanAutomaticallyCreateGroupedConcepts = false;
            AutomaticGroupedConceptTechName = string.Empty;
            IsDirectional = true;
            IsSimple = false;
            HideCentralSymbolWhenSimple = false;
            ShowNameIfHidingCentralSymbol = true;
            Symbol = EditableConceptSymbolFormat.CreateDefault();
            Symbol.Shape = RepresentativeShape;
            Symbol.FillColorHex = "#FFE5E7EB";
            Symbol.StrokeColorHex = "#FF64748B";
            Connector = EditableConnectorFormat.CreateDefault();
            OriginRole = EditableLinkRoleDefinition.Create("Origin");
            TargetRole = EditableLinkRoleDefinition.Create("Target");
            Details = new List<EditableDetailDesignator>
            {
                EditableDetailDesignator.Create("Summary", "Link", true, true)
            };
            OutputTemplates = new List<EditableOutputTemplate>
            {
                new EditableOutputTemplate()
            };
        }

        public string Id { get; set; }

        public string Name { get; set; }

        public string TechName { get; set; }

        public string Summary { get; set; }

        public string Description { get; set; }

        public string TechSpec { get; set; }

        public string PictogramAsset { get; set; }

        public string ClusterTechName { get; set; }

        public string AncestorRelationshipTechName { get; set; }

        public string RepresentativeShape { get; set; }

        public bool IsComposable { get; set; }

        public bool IsVersionable { get; set; }

        public bool PreciseConnectByDefault { get; set; }

        public bool HasGroupRegion { get; set; }

        public bool HasGroupLine { get; set; }

        public bool CanAutomaticallyCreateRelatedConcepts { get; set; }

        public bool CanGroupIntersectingObjects { get; set; }

        public bool CanAutomaticallyCreateGroupedConcepts { get; set; }

        public string AutomaticGroupedConceptTechName { get; set; }

        public bool IsDirectional { get; set; }

        public bool IsSimple { get; set; }

        public bool HideCentralSymbolWhenSimple { get; set; }

        public bool ShowNameIfHidingCentralSymbol { get; set; }

        public EditableConceptSymbolFormat Symbol { get; set; }

        public EditableConnectorFormat Connector { get; set; }

        public EditableLinkRoleDefinition OriginRole { get; set; }

        public EditableLinkRoleDefinition TargetRole { get; set; }

        public List<EditableDetailDesignator> Details { get; set; }

        public List<EditableOutputTemplate> OutputTemplates { get; set; }

        public EditableRelationshipDefinition Clone()
        {
            return new EditableRelationshipDefinition
            {
                Id = Id,
                Name = Name,
                TechName = TechName,
                Summary = Summary,
                Description = Description,
                TechSpec = TechSpec,
                PictogramAsset = PictogramAsset,
                ClusterTechName = ClusterTechName,
                AncestorRelationshipTechName = AncestorRelationshipTechName,
                RepresentativeShape = RepresentativeShape,
                IsComposable = IsComposable,
                IsVersionable = IsVersionable,
                PreciseConnectByDefault = PreciseConnectByDefault,
                HasGroupRegion = HasGroupRegion,
                HasGroupLine = HasGroupLine,
                CanAutomaticallyCreateRelatedConcepts = CanAutomaticallyCreateRelatedConcepts,
                CanGroupIntersectingObjects = CanGroupIntersectingObjects,
                CanAutomaticallyCreateGroupedConcepts = CanAutomaticallyCreateGroupedConcepts,
                AutomaticGroupedConceptTechName = AutomaticGroupedConceptTechName,
                IsDirectional = IsDirectional,
                IsSimple = IsSimple,
                HideCentralSymbolWhenSimple = HideCentralSymbolWhenSimple,
                ShowNameIfHidingCentralSymbol = ShowNameIfHidingCentralSymbol,
                Symbol = Symbol == null ? EditableConceptSymbolFormat.CreateDefault() : Symbol.Clone(),
                Connector = Connector == null ? EditableConnectorFormat.CreateDefault() : Connector.Clone(),
                OriginRole = OriginRole == null ? EditableLinkRoleDefinition.Create("Origin") : OriginRole.Clone(),
                TargetRole = TargetRole == null ? EditableLinkRoleDefinition.Create("Target") : TargetRole.Clone(),
                Details = Details == null
                    ? new List<EditableDetailDesignator>()
                    : Details.Select(detail => detail.Clone()).ToList(),
                OutputTemplates = OutputTemplates == null
                    ? new List<EditableOutputTemplate>()
                    : OutputTemplates.Select(template => template.Clone()).ToList()
            };
        }

        public static EditableRelationshipDefinition CreateDefault(string name, string accentColorHex)
        {
            var relationship = new EditableRelationshipDefinition
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Relationship" : name.Trim(),
                TechName = EditableDomainNaming.ToTechName(name)
            };

            var accent = string.IsNullOrWhiteSpace(accentColorHex) ? "#FF64748B" : accentColorHex;
            relationship.Symbol.StrokeColorHex = accent;
            relationship.Connector.LineColorHex = accent;
            return relationship;
        }
    }

    public sealed class EditableLinkRoleDefinition
    {
        public EditableLinkRoleDefinition()
        {
            Name = string.Empty;
            TechName = string.Empty;
            Summary = string.Empty;
            RoleType = string.Empty;
            PictogramAsset = string.Empty;
            MaxConnections = 1;
            RelatedIdeasAreOrdered = false;
            AllowedVariants = "Standard";
            AssociableConcepts = string.Empty;
        }

        public string Name { get; set; }

        public string TechName { get; set; }

        public string Summary { get; set; }

        public string RoleType { get; set; }

        public string PictogramAsset { get; set; }

        public int MaxConnections { get; set; }

        public bool RelatedIdeasAreOrdered { get; set; }

        public string AllowedVariants { get; set; }

        public string AssociableConcepts { get; set; }

        public EditableLinkRoleDefinition Clone()
        {
            return new EditableLinkRoleDefinition
            {
                Name = Name,
                TechName = TechName,
                Summary = Summary,
                RoleType = RoleType,
                PictogramAsset = PictogramAsset,
                MaxConnections = MaxConnections,
                RelatedIdeasAreOrdered = RelatedIdeasAreOrdered,
                AllowedVariants = AllowedVariants,
                AssociableConcepts = AssociableConcepts
            };
        }

        public static EditableLinkRoleDefinition Create(string name)
        {
            return new EditableLinkRoleDefinition
            {
                Name = name ?? string.Empty,
                TechName = EditableDomainNaming.ToTechName(name),
                Summary = string.Empty,
                RoleType = string.Equals(name, "Target", StringComparison.OrdinalIgnoreCase) ? "Target" : "Origin",
                AllowedVariants = "Standard",
                MaxConnections = 1
            };
        }
    }

    public sealed class EditableConnectorFormat
    {
        public EditableConnectorFormat()
        {
            LineColorHex = "#FF111827";
            MainBackgroundColorHex = "#00FFFFFF";
            LineThickness = 1.5;
            LineDash = "Solid";
            HeadPlug = "SimpleArrow";
            TailPlug = "None";
            HeadVariantTechName = "Standard";
            TailVariantTechName = "Standard";
            PathStyle = "Straight";
            PathCorner = "Sharp";
            LabelLinkDescriptor = true;
            LabelLinkDefinitor = false;
            LabelLinkVariant = false;
        }

        public string LineColorHex { get; set; }

        public string MainBackgroundColorHex { get; set; }

        public double LineThickness { get; set; }

        public string LineDash { get; set; }

        public string HeadPlug { get; set; }

        public string TailPlug { get; set; }

        public string HeadVariantTechName { get; set; }

        public string TailVariantTechName { get; set; }

        public string PathStyle { get; set; }

        public string PathCorner { get; set; }

        public bool LabelLinkDescriptor { get; set; }

        public bool LabelLinkDefinitor { get; set; }

        public bool LabelLinkVariant { get; set; }

        public EditableConnectorFormat Clone()
        {
            return new EditableConnectorFormat
            {
                LineColorHex = LineColorHex,
                MainBackgroundColorHex = MainBackgroundColorHex,
                LineThickness = LineThickness,
                LineDash = LineDash,
                HeadPlug = HeadPlug,
                TailPlug = TailPlug,
                HeadVariantTechName = HeadVariantTechName,
                TailVariantTechName = TailVariantTechName,
                PathStyle = PathStyle,
                PathCorner = PathCorner,
                LabelLinkDescriptor = LabelLinkDescriptor,
                LabelLinkDefinitor = LabelLinkDefinitor,
                LabelLinkVariant = LabelLinkVariant
            };
        }

        public static EditableConnectorFormat CreateDefault()
        {
            return new EditableConnectorFormat();
        }
    }

    public sealed class EditableMarkerDefinition
    {
        public EditableMarkerDefinition()
        {
            Id = Guid.NewGuid().ToString("D");
            Name = "Marker";
            TechName = "Marker";
            Summary = string.Empty;
            PictogramAsset = string.Empty;
            ClusterKey = "UserDef";
            BackgroundColorHex = "#FFFBBF24";
            ForegroundColorHex = "#FF111827";
        }

        public string Id { get; set; }

        public string Name { get; set; }

        public string TechName { get; set; }

        public string Summary { get; set; }

        public string PictogramAsset { get; set; }

        public string ClusterKey { get; set; }

        public string BackgroundColorHex { get; set; }

        public string ForegroundColorHex { get; set; }

        public EditableMarkerDefinition Clone()
        {
            return new EditableMarkerDefinition
            {
                Id = Id,
                Name = Name,
                TechName = TechName,
                Summary = Summary,
                PictogramAsset = PictogramAsset,
                ClusterKey = ClusterKey,
                BackgroundColorHex = BackgroundColorHex,
                ForegroundColorHex = ForegroundColorHex
            };
        }

        public static EditableMarkerDefinition CreateDefault(string name, string backgroundColorHex)
        {
            return new EditableMarkerDefinition
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Marker" : name.Trim(),
                TechName = EditableDomainNaming.ToTechName(name),
                BackgroundColorHex = string.IsNullOrWhiteSpace(backgroundColorHex) ? "#FFFBBF24" : backgroundColorHex
            };
        }
    }

    public sealed class EditableComplementDefinition
    {
        public EditableComplementDefinition()
        {
            Id = Guid.NewGuid().ToString("D");
            Name = "Text";
            TechName = "Text";
            Summary = string.Empty;
            PictogramAsset = string.Empty;
            Kind = "Text";
            Text = string.Empty;
            ImageAsset = string.Empty;
            ForegroundColorHex = "#FF1D4ED8";
            BackgroundColorHex = "#FFF8FAFC";
            LineDash = "Solid";
            LineThickness = 1.5;
            Orientation = "Horizontal";
            Quadrant = "TopRight";
            OffsetX = 0;
            OffsetY = 0;
            InitialWidth = 180;
            InitialHeight = 80;
        }

        public string Id { get; set; }

        public string Name { get; set; }

        public string TechName { get; set; }

        public string Summary { get; set; }

        public string PictogramAsset { get; set; }

        public string Kind { get; set; }

        public string Text { get; set; }

        public string ImageAsset { get; set; }

        public string ForegroundColorHex { get; set; }

        public string BackgroundColorHex { get; set; }

        public string LineDash { get; set; }

        public double LineThickness { get; set; }

        public string Orientation { get; set; }

        public string Quadrant { get; set; }

        public double OffsetX { get; set; }

        public double OffsetY { get; set; }

        public double InitialWidth { get; set; }

        public double InitialHeight { get; set; }

        public EditableComplementDefinition Clone()
        {
            return new EditableComplementDefinition
            {
                Id = Id,
                Name = Name,
                TechName = TechName,
                Summary = Summary,
                PictogramAsset = PictogramAsset,
                Kind = Kind,
                Text = Text,
                ImageAsset = ImageAsset,
                ForegroundColorHex = ForegroundColorHex,
                BackgroundColorHex = BackgroundColorHex,
                LineDash = LineDash,
                LineThickness = LineThickness,
                Orientation = Orientation,
                Quadrant = Quadrant,
                OffsetX = OffsetX,
                OffsetY = OffsetY,
                InitialWidth = InitialWidth,
                InitialHeight = InitialHeight
            };
        }

        public static EditableComplementDefinition CreateDefault(string name, string fillColorHex)
        {
            var complement = new EditableComplementDefinition
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Text" : name.Trim(),
                TechName = EditableDomainNaming.ToTechName(name),
                Kind = string.IsNullOrWhiteSpace(name) ? "Text" : name.Trim(),
                BackgroundColorHex = string.IsNullOrWhiteSpace(fillColorHex) ? "#FFF8FAFC" : fillColorHex
            };

            complement.Summary = complement.Kind + " complement default";
            return complement;
        }
    }

    public sealed class EditableConceptDefinition
    {
        public EditableConceptDefinition()
        {
            Id = Guid.NewGuid().ToString("D");
            Name = "Concept";
            TechName = "Concept";
            Summary = string.Empty;
            Description = string.Empty;
            TechSpec = string.Empty;
            PictogramAsset = string.Empty;
            ClusterTechName = string.Empty;
            RepresentativeShape = "Capsule";
            IsComposable = true;
            IsVersionable = false;
            PreciseConnectByDefault = false;
            HasGroupRegion = false;
            HasGroupLine = false;
            CanAutomaticallyCreateRelatedConcepts = false;
            AutomaticCreationConceptTechName = string.Empty;
            AutomaticCreationRelationshipTechName = string.Empty;
            AutomaticCreationPositioningIsRadialized = true;
            AutomaticCreationPositioningMode = "Vertical Alternated";
            CanGroupIntersectingObjects = false;
            CanAutomaticallyCreateGroupedConcepts = false;
            AutomaticGroupedConceptTechName = string.Empty;
            Symbol = EditableConceptSymbolFormat.CreateDefault();
            Details = new List<EditableDetailDesignator>
            {
                EditableDetailDesignator.Create("Summary", "Link", true, true),
                EditableDetailDesignator.Create("Custom-Fields", "Table", true, false)
            };
            OutputTemplates = new List<EditableOutputTemplate>
            {
                new EditableOutputTemplate()
            };
        }

        public string Id { get; set; }

        public string Name { get; set; }

        public string TechName { get; set; }

        public string Summary { get; set; }

        public string Description { get; set; }

        public string TechSpec { get; set; }

        public string PictogramAsset { get; set; }

        public string ClusterTechName { get; set; }

        public string RepresentativeShape { get; set; }

        public bool IsComposable { get; set; }

        public bool IsVersionable { get; set; }

        public bool PreciseConnectByDefault { get; set; }

        public bool HasGroupRegion { get; set; }

        public bool HasGroupLine { get; set; }

        public bool CanAutomaticallyCreateRelatedConcepts { get; set; }

        public string AutomaticCreationConceptTechName { get; set; }

        public string AutomaticCreationRelationshipTechName { get; set; }

        public bool AutomaticCreationPositioningIsRadialized { get; set; }

        public string AutomaticCreationPositioningMode { get; set; }

        public bool CanGroupIntersectingObjects { get; set; }

        public bool CanAutomaticallyCreateGroupedConcepts { get; set; }

        public string AutomaticGroupedConceptTechName { get; set; }

        public EditableConceptSymbolFormat Symbol { get; set; }

        public List<EditableDetailDesignator> Details { get; set; }

        public List<EditableOutputTemplate> OutputTemplates { get; set; }

        public EditableConceptDefinition Clone()
        {
            return new EditableConceptDefinition
            {
                Id = Id,
                Name = Name,
                TechName = TechName,
                Summary = Summary,
                Description = Description,
                TechSpec = TechSpec,
                PictogramAsset = PictogramAsset,
                ClusterTechName = ClusterTechName,
                RepresentativeShape = RepresentativeShape,
                IsComposable = IsComposable,
                IsVersionable = IsVersionable,
                PreciseConnectByDefault = PreciseConnectByDefault,
                HasGroupRegion = HasGroupRegion,
                HasGroupLine = HasGroupLine,
                CanAutomaticallyCreateRelatedConcepts = CanAutomaticallyCreateRelatedConcepts,
                AutomaticCreationConceptTechName = AutomaticCreationConceptTechName,
                AutomaticCreationRelationshipTechName = AutomaticCreationRelationshipTechName,
                AutomaticCreationPositioningIsRadialized = AutomaticCreationPositioningIsRadialized,
                AutomaticCreationPositioningMode = AutomaticCreationPositioningMode,
                CanGroupIntersectingObjects = CanGroupIntersectingObjects,
                CanAutomaticallyCreateGroupedConcepts = CanAutomaticallyCreateGroupedConcepts,
                AutomaticGroupedConceptTechName = AutomaticGroupedConceptTechName,
                Symbol = Symbol == null ? EditableConceptSymbolFormat.CreateDefault() : Symbol.Clone(),
                Details = Details == null
                    ? new List<EditableDetailDesignator>()
                    : Details.Select(detail => detail.Clone()).ToList(),
                OutputTemplates = OutputTemplates == null
                    ? new List<EditableOutputTemplate>()
                    : OutputTemplates.Select(template => template.Clone()).ToList()
            };
        }

        public static EditableConceptDefinition CreateDefault(string name, string shape, string fillColorHex, string strokeColorHex)
        {
            var concept = new EditableConceptDefinition
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Concept" : name.Trim(),
                TechName = EditableDomainNaming.ToTechName(name),
                RepresentativeShape = ThinkComposerVisualCatalog.NormalizeShapeTechName(shape, "Capsule")
            };

            concept.Symbol.Shape = concept.RepresentativeShape;
            concept.Symbol.FillColorHex = string.IsNullOrWhiteSpace(fillColorHex) ? "#FFFFE540" : fillColorHex;
            concept.Symbol.StrokeColorHex = string.IsNullOrWhiteSpace(strokeColorHex) ? "#FFD4A900" : strokeColorHex;
            return concept;
        }
    }

    public sealed class EditableConceptSymbolFormat
    {
        public string Shape { get; set; }

        public string FillColorHex { get; set; }

        public string StrokeColorHex { get; set; }

        public double LineThickness { get; set; }

        public double InitialWidth { get; set; }

        public double InitialHeight { get; set; }

        public bool UseNameAsMainTitle { get; set; }

        public bool ShowGlobalDetailsFirst { get; set; }

        public string SubtitleVisualDisposition { get; set; }

        public string PictogramVisualDisposition { get; set; }

        public bool FlippedHorizontally { get; set; }

        public bool FlippedVertically { get; set; }

        public bool Tilted { get; set; }

        public bool AsMultiple { get; set; }

        public static EditableConceptSymbolFormat CreateDefault()
        {
            return new EditableConceptSymbolFormat
            {
                Shape = "Capsule",
                FillColorHex = "#FFFFE540",
                StrokeColorHex = "#FFD4A900",
                LineThickness = 1.5,
                InitialWidth = 110,
                InitialHeight = 38,
                UseNameAsMainTitle = true,
                ShowGlobalDetailsFirst = true,
                SubtitleVisualDisposition = "Hidden",
                PictogramVisualDisposition = "Right"
            };
        }

        public EditableConceptSymbolFormat Clone()
        {
            return new EditableConceptSymbolFormat
            {
                Shape = Shape,
                FillColorHex = FillColorHex,
                StrokeColorHex = StrokeColorHex,
                LineThickness = LineThickness,
                InitialWidth = InitialWidth,
                InitialHeight = InitialHeight,
                UseNameAsMainTitle = UseNameAsMainTitle,
                ShowGlobalDetailsFirst = ShowGlobalDetailsFirst,
                SubtitleVisualDisposition = SubtitleVisualDisposition,
                PictogramVisualDisposition = PictogramVisualDisposition,
                FlippedHorizontally = FlippedHorizontally,
                FlippedVertically = FlippedVertically,
                Tilted = Tilted,
                AsMultiple = AsMultiple
            };
        }
    }

    public sealed class EditableDetailDesignator
    {
        public string Title { get; set; }

        public string Kind { get; set; }

        public bool IsDisplayed { get; set; }

        public bool ShowTitle { get; set; }

        public bool IsMultiRecord { get; set; }

        public string Layout { get; set; }

        public bool ShowFieldTitles { get; set; }

        public static EditableDetailDesignator Create(string title, string kind, bool displayed, bool showTitle)
        {
            return new EditableDetailDesignator
            {
                Title = title ?? string.Empty,
                Kind = kind ?? "Link",
                IsDisplayed = displayed,
                ShowTitle = showTitle,
                Layout = "Transposed",
                ShowFieldTitles = true
            };
        }

        public EditableDetailDesignator Clone()
        {
            return new EditableDetailDesignator
            {
                Title = Title,
                Kind = Kind,
                IsDisplayed = IsDisplayed,
                ShowTitle = ShowTitle,
                IsMultiRecord = IsMultiRecord,
                Layout = Layout,
                ShowFieldTitles = ShowFieldTitles
            };
        }
    }

    public sealed class EditableOutputTemplate
    {
        public EditableOutputTemplate()
        {
            Language = "Text";
            TemplateText = string.Empty;
            ExtendsBaseTemplate = true;
        }

        public string Language { get; set; }

        public string TemplateText { get; set; }

        public bool ExtendsBaseTemplate { get; set; }

        public EditableOutputTemplate Clone()
        {
            return new EditableOutputTemplate
            {
                Language = Language,
                TemplateText = TemplateText,
                ExtendsBaseTemplate = ExtendsBaseTemplate
            };
        }
    }

    public static class EditableDomainNaming
    {
        public static string ToTechName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Concept";

            var builder = new StringBuilder();
            var lastWasSeparator = false;

            foreach (var character in name.Trim())
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                    lastWasSeparator = false;
                }
                else if (!lastWasSeparator && builder.Length > 0)
                {
                    builder.Append('_');
                    lastWasSeparator = true;
                }
            }

            var result = builder.ToString().Trim('_');
            if (result.Length == 0)
                result = "Concept";

            if (char.IsDigit(result[0]))
                result = "Concept_" + result;

            return result;
        }

        public static string MakeUniqueTechName(string baseName, IEnumerable<string> existingTechNames)
        {
            var normalized = ToTechName(baseName);
            var existing = new HashSet<string>(existingTechNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            if (!existing.Contains(normalized))
                return normalized;

            for (var index = 2; index < 10000; index++)
            {
                var candidate = normalized + "_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!existing.Contains(candidate))
                    return candidate;
            }

            return normalized + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }
    }
}
