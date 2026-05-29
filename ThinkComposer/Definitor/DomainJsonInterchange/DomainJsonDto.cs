// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// JSON interchange DTOs for editable Domain import/export.
// -------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Instrumind.ThinkComposer.Definitor.DomainJsonInterchange
{
    public class DomainJsonDocument
    {
        public const string CurrentFormat = "ThinkComposer.DomainJsonInterchange";
        public const int CurrentFormatVersion = 1;

        public DomainJsonDocument()
        {
            this.Format = CurrentFormat;
            this.FormatVersion = CurrentFormatVersion;
            this.Application = "ThinkComposer";
            this.ExternalLanguages = new List<DomainJsonElement>();
            this.LinkRoleVariants = new List<DomainJsonElement>();
            this.ConceptDefinitionClusters = new List<DomainJsonElement>();
            this.RelationshipDefinitionClusters = new List<DomainJsonElement>();
            this.MarkerClusters = new List<DomainJsonElement>();
            this.MarkerDefinitions = new List<DomainJsonElement>();
            this.TableDefinitionCategories = new List<DomainJsonElement>();
            this.FieldDefinitionCategories = new List<DomainJsonElement>();
            this.TableDefinitions = new List<DomainJsonElement>();
            this.ConceptDefinitions = new List<DomainJsonElement>();
            this.RelationshipDefinitions = new List<DomainJsonElement>();
            this.ConceptDefinitionOutputTemplates = new List<DomainJsonElement>();
            this.RelationshipDefinitionOutputTemplates = new List<DomainJsonElement>();
            this.Operations = new List<DomainJsonOperation>();
            this.Warnings = new List<string>();
        }

        public string Format { get; set; }
        public int FormatVersion { get; set; }
        public string ExportedAtUtc { get; set; }
        public string Application { get; set; }
        public DomainJsonElement Domain { get; set; }
        public List<DomainJsonElement> ExternalLanguages { get; set; }
        public List<DomainJsonElement> LinkRoleVariants { get; set; }
        public List<DomainJsonElement> ConceptDefinitionClusters { get; set; }
        public List<DomainJsonElement> RelationshipDefinitionClusters { get; set; }
        public List<DomainJsonElement> MarkerClusters { get; set; }
        public List<DomainJsonElement> MarkerDefinitions { get; set; }
        public List<DomainJsonElement> TableDefinitionCategories { get; set; }
        public List<DomainJsonElement> FieldDefinitionCategories { get; set; }
        public List<DomainJsonElement> TableDefinitions { get; set; }
        public List<DomainJsonElement> ConceptDefinitions { get; set; }
        public List<DomainJsonElement> RelationshipDefinitions { get; set; }
        public List<DomainJsonElement> ConceptDefinitionOutputTemplates { get; set; }
        public List<DomainJsonElement> RelationshipDefinitionOutputTemplates { get; set; }
        public List<DomainJsonOperation> Operations { get; set; }
        public List<string> Warnings { get; set; }
    }

    public class DomainJsonElement
    {
        public DomainJsonElement()
        {
            this.Fields = new List<DomainJsonElement>();
            this.RoleDefinitions = new List<DomainJsonElement>();
            this.OutputTemplates = new List<DomainJsonElement>();
            this.AllowedVariantTechNames = new List<string>();
            this.AssociableIdeaDefinitionTechNames = new List<string>();
            this.Set = new Dictionary<string, object>();
        }

        public string Id { get; set; }
        public string Entity { get; set; }
        public string Name { get; set; }
        public string TechName { get; set; }
        public string Summary { get; set; }
        public string Description { get; set; }
        public string TechSpec { get; set; }
        public string OwnerId { get; set; }
        public string OwnerTechName { get; set; }
        public string OwnerScope { get; set; }
        public string ClusterTechName { get; set; }
        public string CategoryTechName { get; set; }
        public string DataTypeTechName { get; set; }
        public string RepresentativeShape { get; set; }
        public string AncestorTechName { get; set; }
        public bool? IsComposable { get; set; }
        public bool? IsVersionable { get; set; }
        public bool? CanAutomaticallyCreateRelatedConcepts { get; set; }
        public bool? IsDirectional { get; set; }
        public bool? IsSimple { get; set; }
        public bool? HideCentralSymbolWhenSimple { get; set; }
        public bool? ShowNameIfHidingCentralSymbol { get; set; }
        public string RoleType { get; set; }
        public uint? MaxConnections { get; set; }
        public bool? RelatedIdeasAreOrdered { get; set; }
        public string ExternalLanguageTechName { get; set; }
        public string TemplateText { get; set; }
        public bool? ExtendsBaseTemplate { get; set; }
        public int? Order { get; set; }
        public List<string> AllowedVariantTechNames { get; set; }
        public List<string> AssociableIdeaDefinitionTechNames { get; set; }
        public List<DomainJsonElement> Fields { get; set; }
        public List<DomainJsonElement> RoleDefinitions { get; set; }
        public List<DomainJsonElement> OutputTemplates { get; set; }
        public Dictionary<string, object> Set { get; set; }
    }

    public class DomainJsonOperation
    {
        public DomainJsonOperation()
        {
            this.Set = new Dictionary<string, object>();
        }

        public string Op { get; set; }
        public string Entity { get; set; }
        public string Id { get; set; }
        public string TechName { get; set; }
        public string OwnerId { get; set; }
        public string OwnerTechName { get; set; }
        public string OwnerScope { get; set; }
        public Dictionary<string, object> Set { get; set; }
    }
}
