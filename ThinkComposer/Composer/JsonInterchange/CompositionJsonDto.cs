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
        public const int CurrentFormatVersion = 1;

        public CompositionJsonDocument()
        {
            this.Format = CurrentFormat;
            this.FormatVersion = CurrentFormatVersion;
            this.Application = "ThinkComposer";
            this.Ideas = new List<CompositionJsonIdea>();
            this.Relationships = new List<CompositionJsonRelationship>();
            this.Views = new List<CompositionJsonView>();
            this.Operations = new List<CompositionJsonOperation>();
            this.Warnings = new List<string>();
        }

        public string Format { get; set; }
        public int FormatVersion { get; set; }
        public string ExportedAtUtc { get; set; }
        public string Application { get; set; }
        public CompositionJsonComposition Composition { get; set; }
        public List<CompositionJsonIdea> Ideas { get; set; }
        public List<CompositionJsonRelationship> Relationships { get; set; }
        public List<CompositionJsonView> Views { get; set; }
        public List<CompositionJsonOperation> Operations { get; set; }
        public List<string> Warnings { get; set; }
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
        public List<CompositionJsonDefinition> Definitions { get; set; }
    }

    public class CompositionJsonDefinition
    {
        public string Id { get; set; }
        public string Kind { get; set; }
        public string Name { get; set; }
        public string TechName { get; set; }
        public string Summary { get; set; }
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
        public string DefinitionId { get; set; }
        public string DefinitionTechName { get; set; }
        public string DefinitionName { get; set; }
        public string ContainerId { get; set; }
        public string ContainerTechName { get; set; }
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
            this.TargetIdeaIds = new List<string>();
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
        public string DefinitionId { get; set; }
        public string DefinitionTechName { get; set; }
        public string DefinitionName { get; set; }
        public string ContainerId { get; set; }
        public string ContainerTechName { get; set; }
        public List<string> OriginIdeaIds { get; set; }
        public List<string> TargetIdeaIds { get; set; }
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
        }

        public string Id { get; set; }
        public string Name { get; set; }
        public string TechName { get; set; }
        public string Summary { get; set; }
        public string OwnerIdeaId { get; set; }
        public string OwnerIdeaTechName { get; set; }
        public List<CompositionJsonVisual> Visuals { get; set; }
    }

    public class CompositionJsonVisual
    {
        public string IdeaId { get; set; }
        public string IdeaTechName { get; set; }
        public string RepresentationId { get; set; }
        public bool IsShortcut { get; set; }
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }
    }

    public class CompositionJsonOperation
    {
        public CompositionJsonOperation()
        {
            this.Set = new Dictionary<string, object>();
            this.OriginIdeaIds = new List<string>();
            this.TargetIdeaIds = new List<string>();
            this.Links = new List<CompositionJsonRelationshipLink>();
        }

        public string Op { get; set; }
        public string Entity { get; set; }
        public string Id { get; set; }
        public string TechName { get; set; }
        public string DefinitionTechName { get; set; }
        public string ContainerId { get; set; }
        public string ContainerTechName { get; set; }
        public List<string> OriginIdeaIds { get; set; }
        public List<string> TargetIdeaIds { get; set; }
        public List<CompositionJsonRelationshipLink> Links { get; set; }
        public Dictionary<string, object> Set { get; set; }
    }
}
