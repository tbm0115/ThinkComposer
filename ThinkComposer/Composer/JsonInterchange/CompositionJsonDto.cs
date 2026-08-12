// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// JSON interchange DTOs for GPT/editable composition import-export.
// -------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Instrumind.ThinkComposer.Composer.JsonInterchange
{
    public class CompositionJsonDocument
    {
        public const string CurrentFormat = "ThinkComposer.JsonInterchange";
        public const int MinimumSupportedFormatVersion = 1;
        public const int CurrentFormatVersion = 2;

        public CompositionJsonDocument()
        {
            this.Format = CurrentFormat;
            this.FormatVersion = CurrentFormatVersion;
            this.Application = "ThinkComposer";
            this.Ideas = new List<CompositionJsonIdea>();
            this.Relationships = new List<CompositionJsonRelationship>();
            this.Views = new List<CompositionJsonView>();
            this.Operations = new List<CompositionJsonOperation>();
            this.Groups = new List<CompositionJsonGroup>();
            this.Warnings = new List<string>();
        }

        public string Format { get; set; }
        public int FormatVersion { get; set; }
        public string ExportedAtUtc { get; set; }
        public string Application { get; set; }
        public CompositionJsonTargetContext TargetContext { get; set; }
        public CompositionJsonTargetContext Requires { get; set; }
        public CompositionJsonComposition Composition { get; set; }
        public CompositionJsonImportOptions ImportOptions { get; set; }
        public CompositionJsonVisualStrategy VisualStrategy { get; set; }
        public List<CompositionJsonIdea> Ideas { get; set; }
        public List<CompositionJsonRelationship> Relationships { get; set; }
        public List<CompositionJsonView> Views { get; set; }
        public List<CompositionJsonOperation> Operations { get; set; }
        public List<CompositionJsonGroup> Groups { get; set; }
        public List<string> Warnings { get; set; }
    }

    public class CompositionJsonImportOptions
    {
        public bool? AutoPlaceNewItems { get; set; }
        public bool? AutoFitPlacedConcepts { get; set; }
        public bool? AutoRoutePlacedLinks { get; set; }
        public bool? UseActiveCompositionAsContainer { get; set; }
        public bool? TreatMissingFullStateItemsAsCreates { get; set; }
        public string RelationshipDefinitionFallbackTechName { get; set; }
        public string DetailFallbackMode { get; set; }
        public string DomainCompatibilityPolicy { get; set; }
        public string CompositionVersionPolicy { get; set; }
        public bool? StrictRelationshipCompatibility { get; set; }
        public bool? AbortOnRelationshipCompatibilityFailure { get; set; }
        public bool? StrictDetailsCompatibility { get; set; }
        public bool? AbortOnDetailCompatibilityFailure { get; set; }
        public string RelationshipVisualPlacementMode { get; set; }
        public bool? RecomputeSuspiciousRelationshipVisuals { get; set; }
        public bool? HideGenericRelationshipCenters { get; set; }
        public double? MaxRelationshipCenterDisplacement { get; set; }
        public double? RelationshipCenterObstaclePadding { get; set; }
        public double? RelationshipCenterOverlapPadding { get; set; }
        public string LayoutMode { get; set; }
        public bool? PreventSelfRecursiveCompositeViews { get; set; }
        public bool? RepairRecursiveVisuals { get; set; }
    }

    public class CompositionJsonVisualStrategy
    {
        public CompositionJsonVisualStrategy()
        {
            this.LargeModelThresholds = new CompositionJsonLargeModelThresholds();
            this.GroupBy = new List<string>();
        }

        public string Mode { get; set; }
        public CompositionJsonLargeModelThresholds LargeModelThresholds { get; set; }
        public bool? FullModelVisuals { get; set; }
        public bool? OverviewView { get; set; }
        public string OverviewViewTechName { get; set; }
        public int? MaxOverviewConcepts { get; set; }
        public int? MaxOverviewRelationships { get; set; }
        public List<string> GroupBy { get; set; }
        public bool? DeferRouting { get; set; }
        public bool? DeferAutoFit { get; set; }
        public bool? DeferViewRefresh { get; set; }
        public string RelationshipVisualPlacement { get; set; }
    }

    public class CompositionJsonLargeModelThresholds
    {
        public int? Concepts { get; set; }
        public int? Relationships { get; set; }
        public int? Visuals { get; set; }
    }

    public class CompositionJsonTargetContext
    {
        public CompositionJsonContextElement Composition { get; set; }
        public CompositionJsonContextElement Domain { get; set; }
    }

    public class CompositionJsonContextElement
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string TechName { get; set; }
        public string VersionNumber { get; set; }
        public int? VersionSequence { get; set; }
        public string LastModification { get; set; }
        public string CompatibilitySignature { get; set; }
    }

    public class CompositionJsonComposition
    {
        public CompositionJsonComposition()
        {
            this.Version = new CompositionJsonVersion();
            this.Domain = new CompositionJsonDomain();
        }

        public string Id { get; set; }
        public string Name { get; set; }
        public string TechName { get; set; }
        public string Summary { get; set; }
        public string Description { get; set; }
        public string TechSpec { get; set; }
        public string ViewsPrefix { get; set; }
        public string RootViewId { get; set; }
        public string ActiveViewId { get; set; }
        public CompositionJsonVersion Version { get; set; }
        public CompositionJsonDomain Domain { get; set; }
    }

    public class CompositionJsonDomain
    {
        public CompositionJsonDomain()
        {
            this.Definitions = new List<CompositionJsonDefinition>();
        }

        public string Id { get; set; }
        public string Name { get; set; }
        public string TechName { get; set; }
        public string Summary { get; set; }
        public string Description { get; set; }
        public string TechSpec { get; set; }
        public string CompatibilitySignature { get; set; }
        public List<CompositionJsonDefinition> Definitions { get; set; }
    }

    public class CompositionJsonDefinition
    {
        public string Id { get; set; }
        public string Kind { get; set; }
        public string Name { get; set; }
        public string TechName { get; set; }
        public string Summary { get; set; }
        public string Description { get; set; }
        public string TechSpec { get; set; }
    }

    public class CompositionJsonVersion
    {
        public int? VersionSequence { get; set; }
        public string VersionNumber { get; set; }
        public string Annotation { get; set; }
        public string Creator { get; set; }
        public string LastModifier { get; set; }
        public string Creation { get; set; }
        public string LastModification { get; set; }
    }

    public class CompositionJsonIdea
    {
        public CompositionJsonIdea()
        {
            this.Kind = "Concept";
            this.ChildIdeaIds = new List<string>();
            this.CompositeViewIds = new List<string>();
            this.Details = new List<CompositionJsonDetail>();
            this.Markers = new List<CompositionJsonMarker>();
        }

        public string Id { get; set; }
        public string Kind { get; set; }
        public bool IsNew { get; set; }
        public bool Delete { get; set; }
        public string Name { get; set; }
        public string TechName { get; set; }
        public string Summary { get; set; }
        public string Description { get; set; }
        public string TechSpec { get; set; }
        public Dictionary<string, object> Pictogram { get; set; }
        public string DefinitionId { get; set; }
        public string DefinitionTechName { get; set; }
        public string DefinitionName { get; set; }
        public string ContainerId { get; set; }
        public string ContainerTechName { get; set; }
        public CompositionJsonVisualControl Visual { get; set; }
        public List<string> ChildIdeaIds { get; set; }
        public List<string> CompositeViewIds { get; set; }
        public List<CompositionJsonDetail> Details { get; set; }
        public List<CompositionJsonMarker> Markers { get; set; }
    }

    public class CompositionJsonRelationship
    {
        public CompositionJsonRelationship()
        {
            this.Kind = "Relationship";
            this.OriginIdeaIds = new List<string>();
            this.OriginIdeaTechNames = new List<string>();
            this.TargetIdeaIds = new List<string>();
            this.TargetIdeaTechNames = new List<string>();
            this.Links = new List<CompositionJsonRelationshipLink>();
            this.ChildIdeaIds = new List<string>();
            this.CompositeViewIds = new List<string>();
            this.Details = new List<CompositionJsonDetail>();
            this.Markers = new List<CompositionJsonMarker>();
        }

        public string Id { get; set; }
        public string Kind { get; set; }
        public bool IsNew { get; set; }
        public bool Delete { get; set; }
        public string Name { get; set; }
        public string TechName { get; set; }
        public string Summary { get; set; }
        public string Description { get; set; }
        public string TechSpec { get; set; }
        public Dictionary<string, object> Pictogram { get; set; }
        public string DefinitionId { get; set; }
        public string DefinitionTechName { get; set; }
        public string DefinitionName { get; set; }
        public string FallbackDefinitionTechName { get; set; }
        public bool? StrictDefinition { get; set; }
        public string ContainerId { get; set; }
        public string ContainerTechName { get; set; }
        public string LayoutRole { get; set; }
        public CompositionJsonVisualControl Visual { get; set; }
        public List<string> OriginIdeaIds { get; set; }
        public List<string> OriginIdeaTechNames { get; set; }
        public List<string> TargetIdeaIds { get; set; }
        public List<string> TargetIdeaTechNames { get; set; }
        public List<CompositionJsonRelationshipLink> Links { get; set; }
        public List<string> ChildIdeaIds { get; set; }
        public List<string> CompositeViewIds { get; set; }
        public List<CompositionJsonDetail> Details { get; set; }
        public List<CompositionJsonMarker> Markers { get; set; }
    }

    public class CompositionJsonRelationshipLink
    {
        public string Id { get; set; }
        public string RoleType { get; set; }
        public string RoleDefinitionId { get; set; }
        public string RoleDefinitionTechName { get; set; }
        public string RoleDefinitionName { get; set; }
        public string RoleVariantTechName { get; set; }
        public string RoleVariantName { get; set; }
        public string DescriptorName { get; set; }
        public string DescriptorTechName { get; set; }
        public string DescriptorSummary { get; set; }
        public string IdeaId { get; set; }
        public string IdeaTechName { get; set; }
    }

    public class CompositionJsonMarker
    {
        public bool Delete { get; set; }
        public string DefinitionId { get; set; }
        public string DefinitionTechName { get; set; }
        public string DefinitionName { get; set; }
        public string DescriptorName { get; set; }
        public string DescriptorTechName { get; set; }
        public string DescriptorSummary { get; set; }
    }

    public class CompositionJsonDetail
    {
        public CompositionJsonDetail()
        {
            this.Fields = new List<CompositionJsonField>();
            this.Records = new List<Dictionary<string, object>>();
        }

        public bool Delete { get; set; }
        public string Kind { get; set; }
        public string DesignatorId { get; set; }
        public string DesignatorTechName { get; set; }
        public string DesignatorName { get; set; }
        public string Text { get; set; }
        public string TargetAddress { get; set; }
        public string TargetPropertyTechName { get; set; }
        public string Source { get; set; }
        public string MimeType { get; set; }
        public List<CompositionJsonField> Fields { get; set; }
        public List<Dictionary<string, object>> Records { get; set; }
    }

    public class CompositionJsonField
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string TechName { get; set; }
        public string DataType { get; set; }
    }

    public class CompositionJsonView
    {
        public CompositionJsonView()
        {
            this.Visuals = new List<CompositionJsonVisual>();
            this.Complements = new List<CompositionJsonComplement>();
        }

        public string Id { get; set; }
        public string Name { get; set; }
        public string TechName { get; set; }
        public string Summary { get; set; }
        public string Description { get; set; }
        public string OwnerIdeaId { get; set; }
        public string OwnerIdeaTechName { get; set; }
        public List<CompositionJsonVisual> Visuals { get; set; }
        public List<CompositionJsonComplement> Complements { get; set; }
    }

    public class CompositionJsonVisual
    {
        public CompositionJsonVisual()
        {
            this.Connectors = new List<CompositionJsonConnector>();
            this.Complements = new List<CompositionJsonComplement>();
            this.CustomFormatValues = new Dictionary<string, object>();
        }

        public string IdeaId { get; set; }
        public string IdeaTechName { get; set; }
        public string RepresentationId { get; set; }
        public bool IsShortcut { get; set; }
        public int? ZOrder { get; set; }
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }
        public bool? AreDetailsShown { get; set; }
        public bool? ShowCompositeContentAsDetails { get; set; }
        public double? DetailsPosterHeight { get; set; }
        public bool? ShowAsMultiple { get; set; }
        public bool? IsHorizontallyFlipped { get; set; }
        public bool? IsVerticallyFlipped { get; set; }
        public bool? IsTilted { get; set; }
        public CompositionJsonVisualControl Visual { get; set; }
        public List<CompositionJsonConnector> Connectors { get; set; }
        public List<CompositionJsonComplement> Complements { get; set; }
        public Dictionary<string, object> CustomFormatValues { get; set; }
    }

    public class CompositionJsonPoint
    {
        public double? X { get; set; }
        public double? Y { get; set; }
    }

    public class CompositionJsonConnector
    {
        public const int MaximumRoutePoints = 32;

        public string Id { get; set; }
        public string LinkId { get; set; }
        public string RoleType { get; set; }
        public string RoleDefinitionTechName { get; set; }
        public string RoleVariantTechName { get; set; }
        public string AssociatedIdeaId { get; set; }
        public string AssociatedIdeaTechName { get; set; }
        public int? ZOrder { get; set; }
        public string OriginRepresentationId { get; set; }
        public string OriginIdeaId { get; set; }
        public string OriginIdeaTechName { get; set; }
        public string TargetRepresentationId { get; set; }
        public string TargetIdeaId { get; set; }
        public string TargetIdeaTechName { get; set; }
        public CompositionJsonPoint OriginPosition { get; set; }
        public CompositionJsonPoint OriginEdgePosition { get; set; }
        public CompositionJsonPoint TargetPosition { get; set; }
        public CompositionJsonPoint TargetEdgePosition { get; set; }
        /// <summary>
        /// Ordered interior route points in origin-to-target order.  In formatVersion 2 this
        /// collection is authoritative whenever it is present, including when it is empty.
        /// </summary>
        public List<CompositionJsonPoint> RoutePoints { get; set; }
        public bool RoutePointsSpecified { get; set; }

        /// <summary>
        /// Legacy formatVersion 1 single-bend field.  New exports use RoutePoints.
        /// </summary>
        public CompositionJsonPoint IntermediatePosition { get; set; }
        public bool IntermediatePositionSpecified { get; set; }
    }

    public class CompositionJsonComplement
    {
        public CompositionJsonComplement()
        {
            this.Set = new Dictionary<string, object>();
        }

        public string Id { get; set; }
        public string KindTechName { get; set; }
        public string KindName { get; set; }
        public int? ZOrder { get; set; }
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }
        public Dictionary<string, object> Set { get; set; }
    }

    public class CompositionJsonOperation
    {
        public CompositionJsonOperation()
        {
            this.Set = new Dictionary<string, object>();
            this.OriginIdeaIds = new List<string>();
            this.OriginIdeaTechNames = new List<string>();
            this.TargetIdeaIds = new List<string>();
            this.TargetIdeaTechNames = new List<string>();
            this.Links = new List<CompositionJsonRelationshipLink>();
            this.Details = new List<CompositionJsonDetail>();
            this.Markers = new List<CompositionJsonMarker>();
        }

        public string Op { get; set; }
        public string Entity { get; set; }
        public string Id { get; set; }
        public string RepresentationId { get; set; }
        public string TechName { get; set; }
        public string DefinitionTechName { get; set; }
        public string FallbackDefinitionTechName { get; set; }
        public bool? StrictDefinition { get; set; }
        public string ContainerId { get; set; }
        public string ContainerTechName { get; set; }
        public string ViewId { get; set; }
        public string ViewTechName { get; set; }
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }
        public bool? AutoPlace { get; set; }
        public bool? AutoFit { get; set; }
        public bool? AutoRoute { get; set; }
        public bool? IsShortcut { get; set; }
        public string LayoutRole { get; set; }
        public CompositionJsonVisualControl Visual { get; set; }
        public List<string> OriginIdeaIds { get; set; }
        public List<string> OriginIdeaTechNames { get; set; }
        public List<string> TargetIdeaIds { get; set; }
        public List<string> TargetIdeaTechNames { get; set; }
        public List<CompositionJsonRelationshipLink> Links { get; set; }
        public List<CompositionJsonDetail> Details { get; set; }
        public List<CompositionJsonMarker> Markers { get; set; }
        public Dictionary<string, object> Set { get; set; }
    }

    public class CompositionJsonVisualControl
    {
        public string Role { get; set; }
        public string Display { get; set; }
        public bool? IncludeInView { get; set; }
        public bool? IncludeInArrangement { get; set; }
        public bool? IncludeInRouting { get; set; }
        public bool? IncludeInAutoFit { get; set; }
        public bool? IncludeInOverview { get; set; }
        public bool? IncludeInFullView { get; set; }
        public bool? IsShortcut { get; set; }
        public string RelationshipCenterPlacement { get; set; }
    }

    public class CompositionJsonGroup
    {
        public CompositionJsonGroup()
        {
            this.MemberIds = new List<string>();
            this.MemberTechNames = new List<string>();
        }

        public string Id { get; set; }
        public string Name { get; set; }
        public string TechName { get; set; }
        public List<string> MemberIds { get; set; }
        public List<string> MemberTechNames { get; set; }
        public string HeaderConceptId { get; set; }
        public string HeaderConceptTechName { get; set; }
        public bool? CreateGroupRegion { get; set; }
        public double? Padding { get; set; }
        public bool? SendToBack { get; set; }
    }
}
