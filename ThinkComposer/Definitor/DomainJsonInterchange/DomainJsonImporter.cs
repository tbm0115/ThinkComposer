// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Conservative Domain JSON preview/apply merge.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Instrumind.Common;
using Instrumind.Common.EntityDefinition;
using Instrumind.Common.Visualization;

using Instrumind.ThinkComposer.MetaModel;
using Instrumind.ThinkComposer.Composer.Generation;
using Instrumind.ThinkComposer.MetaModel.Configurations;
using Instrumind.ThinkComposer.MetaModel.GraphMetaModel;
using Instrumind.ThinkComposer.MetaModel.InformationMetaModel;
using Instrumind.ThinkComposer.MetaModel.VisualMetaModel;

namespace Instrumind.ThinkComposer.Definitor.DomainJsonInterchange
{
    public class DomainJsonImporter
    {
        private DomainJsonImporter(Domain TargetDomain, DomainJsonDocument Document, bool IsPreview, DomainJsonImportReport Report = null)
        {
            this.TargetDomain = TargetDomain;
            this.Document = Document;
            this.IsPreview = IsPreview;
            this.Report = Report ?? new DomainJsonImportReport();
            this.Resolver = new DomainJsonReferenceResolver(TargetDomain);
            this.PlannedTableDefinitionTechNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            this.PlannedConceptDefinitionTechNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            this.PlannedRelationshipDefinitionTechNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            this.PlannedExternalLanguageTechNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private Domain TargetDomain { get; set; }
        private DomainJsonDocument Document { get; set; }
        private bool IsPreview { get; set; }
        private DomainJsonImportReport Report { get; set; }
        private DomainJsonReferenceResolver Resolver { get; set; }
        private HashSet<string> PlannedTableDefinitionTechNames { get; set; }
        private HashSet<string> PlannedConceptDefinitionTechNames { get; set; }
        private HashSet<string> PlannedRelationshipDefinitionTechNames { get; set; }
        private HashSet<string> PlannedExternalLanguageTechNames { get; set; }

        public static DomainJsonImportReport Preview(Domain TargetDomain, DomainJsonDocument Document)
        {
            return new DomainJsonImporter(TargetDomain, Document, true).Execute();
        }

        public static DomainJsonImportReport Apply(Domain TargetDomain, DomainJsonDocument Document, DomainJsonImportReport ExistingReport = null)
        {
            return new DomainJsonImporter(TargetDomain, Document, false, ExistingReport).Execute();
        }

        private DomainJsonImportReport Execute()
        {
            DomainJsonSerializer.Validate(this.Document);

            this.Report.Log("Domain JSON " + (this.IsPreview ? "preview" : "apply") + " started for target domain " + Describe(this.TargetDomain));
            this.Report.Log("Domain JSON source sections: externalLanguages=" + Count(this.Document.ExternalLanguages) +
                            ", linkRoleVariants=" + Count(this.Document.LinkRoleVariants) +
                            ", conceptDefinitions=" + Count(this.Document.ConceptDefinitions) +
                            ", relationshipDefinitions=" + Count(this.Document.RelationshipDefinitions) +
                            ", tableDefinitions=" + Count(this.Document.TableDefinitions) +
                            ", operations=" + Count(this.Document.Operations));

            foreach (var Warning in this.Document.Warnings ?? new List<string>())
                this.Report.SourceWarning(Warning);

            if (this.Document.Domain != null)
                MergeDomain(this.Document.Domain);

            MergeList(this.Document.ExternalLanguages, "externalLanguage", MergeExternalLanguage);
            MergeList(this.Document.LinkRoleVariants, "linkRoleVariant", MergeLinkRoleVariant);
            MergeList(this.Document.MarkerClusters, "markerCluster", MergeMarkerCluster);
            MergeList(this.Document.ConceptDefinitionClusters, "conceptDefinitionCluster", MergeConceptDefinitionCluster);
            MergeList(this.Document.RelationshipDefinitionClusters, "relationshipDefinitionCluster", MergeRelationshipDefinitionCluster);
            MergeList(this.Document.TableDefinitionCategories, "tableDefinitionCategory", MergeTableDefinitionCategory);
            MergeList(this.Document.FieldDefinitionCategories, "fieldDefinitionCategory", MergeFieldDefinitionCategory);
            MergeList(this.Document.MarkerDefinitions, "markerDefinition", MergeMarkerDefinition);
            MergeList(this.Document.TableDefinitions, "tableDefinition", MergeTableDefinition);
            MergeList(this.Document.ConceptDefinitions, "conceptDefinition", MergeConceptDefinition);
            MergeList(this.Document.RelationshipDefinitions, "relationshipDefinition", MergeRelationshipDefinition);
            MergeList(this.Document.ConceptDefinitionOutputTemplates, "outputTemplate", MergeOutputTemplate);
            MergeList(this.Document.RelationshipDefinitionOutputTemplates, "outputTemplate", MergeOutputTemplate);

            ApplyOperations();

            if (!this.IsPreview)
            {
                this.TargetDomain.DeclareExtraCollections();
                this.Report.Log("Domain JSON output template base collections refreshed; output-template resolution caches are treated as dirty and will be rebuilt during next Preview/Generate Files run.");
            }

            this.Report.LegacyRetained = EstimateLegacyRetained();
            this.Report.Log("Domain JSON " + (this.IsPreview ? "preview" : "apply") + " completed. " +
                            (this.IsPreview ? this.Report.PreviewSummary().Replace("\n", "; ") : this.Report.ApplySummary().Replace("\n", "; ")) +
                            "; by entity: " + this.Report.EntitySummary());
            return this.Report;
        }

        private void MergeList(IEnumerable<DomainJsonElement> Items, string Entity, Action<DomainJsonElement> Merger)
        {
            if (Items == null)
                return;

            foreach (var Item in Items)
            {
                if (Item == null)
                    continue;

                Item.Entity = Item.Entity.NullDefault(Entity);
                Merger(Item);
            }
        }

        private void MergeDomain(DomainJsonElement Source)
        {
            var Changed = ApplyFormalFields(this.TargetDomain, Source, "domain", "active-domain");
            if (Changed)
                this.Report.CountUpdated("domain", this.IsPreview);
        }

        private void MergeExternalLanguage(DomainJsonElement Source)
        {
            var Existing = this.Resolver.ExternalLanguage(Source.Id, Source.TechName);
            if (Existing == null)
            {
                if (!RequireNameTech(Source, "externalLanguage"))
                    return;

                if (!this.IsPreview)
                {
                    Existing = new ExternalLanguageDeclaration(Source.Name, Source.TechName, Source.Summary.NullDefault(""));
                    ApplyFormalFields(Existing, Source, "externalLanguage", "create");
                    this.TargetDomain.ExternalLanguages.Add(Existing);
                }
                this.Report.CountCreated("externalLanguage", this.IsPreview);
                TrackPlanned(this.PlannedExternalLanguageTechNames, Source.TechName);
                return;
            }

            var MatchMethod = MatchMethodFor(Existing, Source);
            this.Report.Log("Domain JSON externalLanguage matched by " + MatchMethod + ": " + Describe(Existing));
            if (ApplyFormalFields(Existing, Source, "externalLanguage", MatchMethod))
                this.Report.CountUpdated("externalLanguage", this.IsPreview);
        }

        private void MergeLinkRoleVariant(DomainJsonElement Source)
        {
            var Existing = this.Resolver.LinkRoleVariant(Source.TechName);
            if (Existing == null)
            {
                if (!RequireNameTech(Source, "linkRoleVariant"))
                    return;

                if (!this.IsPreview)
                {
                    Existing = new SimplePresentationElement(Source.Name, Source.TechName, Source.Summary.NullDefault(""));
                    ApplySimpleFields(Existing, Source);
                    this.TargetDomain.LinkRoleVariants.Add(Existing);
                }
                this.Report.CountCreated("linkRoleVariant", this.IsPreview);
                return;
            }

            if (ApplySimpleFields(Existing, Source))
                this.Report.CountUpdated("linkRoleVariant", this.IsPreview);
        }

        private void MergeMarkerCluster(DomainJsonElement Source)
        {
            MergeSimplePresentation(Source, "markerCluster", this.TargetDomain.MarkerClusters, this.Resolver.MarkerCluster);
        }

        private void MergeConceptDefinitionCluster(DomainJsonElement Source)
        {
            MergeFormalPresentation(Source, "conceptDefinitionCluster", this.TargetDomain.ConceptDefClusters,
                                    (id, techName) => this.Resolver.ConceptDefinitionCluster(id, techName));
        }

        private void MergeRelationshipDefinitionCluster(DomainJsonElement Source)
        {
            MergeFormalPresentation(Source, "relationshipDefinitionCluster", this.TargetDomain.RelationshipDefClusters,
                                    (id, techName) => this.Resolver.RelationshipDefinitionCluster(id, techName));
        }

        private void MergeTableDefinitionCategory(DomainJsonElement Source)
        {
            var Existing = this.Resolver.TableDefinitionCategory(Source.Id, Source.TechName);
            if (Existing == null)
            {
                if (!RequireNameTech(Source, "tableDefinitionCategory"))
                    return;

                if (!this.IsPreview)
                {
                    Existing = new MetaCategory<TableDefinition>(Source.Name, Source.TechName, Source.Summary.NullDefault(""));
                    ApplyFormalFields(Existing, Source);
                    this.TargetDomain.TableDefCategories.Add(Existing);
                }
                this.Report.CountCreated("tableDefinitionCategory", this.IsPreview);
                return;
            }

            if (ApplyFormalFields(Existing, Source))
                this.Report.CountUpdated("tableDefinitionCategory", this.IsPreview);
        }

        private void MergeFieldDefinitionCategory(DomainJsonElement Source)
        {
            var Existing = this.Resolver.FieldDefinitionCategory(Source.Id, Source.TechName);
            if (Existing == null)
            {
                if (!RequireNameTech(Source, "fieldDefinitionCategory"))
                    return;

                if (!this.IsPreview)
                {
                    Existing = new MetaCategory<FieldDefinition>(Source.Name, Source.TechName, Source.Summary.NullDefault(""));
                    ApplyFormalFields(Existing, Source);
                    this.TargetDomain.FieldDefCategories.Add(Existing);
                }
                this.Report.CountCreated("fieldDefinitionCategory", this.IsPreview);
                return;
            }

            if (ApplyFormalFields(Existing, Source))
                this.Report.CountUpdated("fieldDefinitionCategory", this.IsPreview);
        }

        private void MergeMarkerDefinition(DomainJsonElement Source)
        {
            var Existing = this.Resolver.MarkerDefinition(Source.Id, Source.TechName);
            if (Existing == null)
            {
                if (!RequireNameTech(Source, "markerDefinition"))
                    return;

                if (!this.IsPreview)
                {
                    Existing = new MarkerDefinition(Source.Name, Source.TechName, Source.Summary.NullDefault(""), null, Source.ClusterTechName.NullDefault(MarkerDefinition.USERDEF_CODE));
                    ApplySimpleFields(Existing, Source);
                    this.TargetDomain.MarkerDefinitions.Add(Existing);
                }
                this.Report.CountCreated("markerDefinition", this.IsPreview);
                return;
            }

            var Changed = ApplySimpleFields(Existing, Source);
            if (!String.IsNullOrWhiteSpace(Source.ClusterTechName) && Existing.ClusterKey != Source.ClusterTechName)
            {
                if (!this.IsPreview)
                    Existing.ClusterKey = Source.ClusterTechName;
                Changed = true;
            }

            if (Changed)
                this.Report.CountUpdated("markerDefinition", this.IsPreview);
        }

        private void MergeTableDefinition(DomainJsonElement Source)
        {
            var Existing = this.Resolver.TableDefinition(Source.Id, Source.TechName);
            if (Existing == null)
            {
                if (!RequireNameTech(Source, "tableDefinition"))
                    return;

                if (!this.IsPreview)
                {
                    Existing = new TableDefinition(this.TargetDomain, Source.Name, Source.TechName, Source.Summary.NullDefault(""));
                    ApplyFormalFields(Existing, Source);
                    this.TargetDomain.TableDefinitions.Add(Existing);
                }
                this.Report.CountCreated("tableDefinition", this.IsPreview);
                TrackPlanned(this.PlannedTableDefinitionTechNames, Source.TechName);
            }
            else
            {
                if (ApplyFormalFields(Existing, Source))
                    this.Report.CountUpdated("tableDefinition", this.IsPreview);
            }

            if (Existing != null || this.IsPreview)
                foreach (var Field in Source.Fields ?? new List<DomainJsonElement>())
                    MergeFieldDefinition(Field, Existing, Source.TechName);

            if (!this.IsPreview && Existing != null)
                Existing.AlterStructure();
        }

        private void MergeFieldDefinition(DomainJsonElement Source, TableDefinition Owner, string OwnerTechName)
        {
            Source.OwnerTechName = Source.OwnerTechName.NullDefault(OwnerTechName);
            if (Owner == null)
                Owner = this.Resolver.TableDefinition(Source.OwnerId, Source.OwnerTechName);
            var Existing = Owner == null ? null : this.Resolver.FieldDefinition(Owner, Source.Id, Source.TechName);
            if (Existing == null)
            {
                if (!RequireNameTech(Source, "fieldDefinition"))
                    return;

                if (Owner == null)
                {
                    if (this.IsPreview && this.PlannedTableDefinitionTechNames.Contains(Source.OwnerTechName.NullDefault("")))
                    {
                        var DataType = ResolveFieldDataType(Source);
                        if (DataType == null)
                            return;

                        this.Report.Log("Domain JSON planned fieldDefinition create: techName=" + Source.TechName +
                                        " ownerTable=" + Source.OwnerTechName + " match=planned dataType=" + DataType.TechName +
                                        " dataTypeMatch=" + MatchMethodForDataType(DataType, Source.DataTypeTechName));
                        this.Report.CountCreated("fieldDefinition", this.IsPreview);
                        return;
                    }

                    Skip("fieldDefinition", "Cannot create field '" + Source.TechName + "' because owner table '" + Source.OwnerTechName + "' was not found.");
                    return;
                }

                var FieldType = ResolveFieldDataType(Source);
                if (FieldType == null)
                    return;

                this.Report.Log("Domain JSON " + (this.IsPreview ? "planned" : "applied") + " fieldDefinition owner/dataType: field=" +
                                Source.TechName + " ownerTable=" + Owner.TechName + " ownerMatch=" + MatchMethodFor(Owner, Source.OwnerTechName) +
                                " dataType=" + FieldType.TechName + " dataTypeMatch=" + MatchMethodForDataType(FieldType, Source.DataTypeTechName));

                if (!this.IsPreview)
                {
                    Existing = new FieldDefinition(Owner, Source.Name, Source.TechName, FieldType, Source.Summary.NullDefault(""));
                    if (Source.Order != null)
                        Existing.StorageIndex = Source.Order.Value;
                    ApplyFormalFields(Existing, Source);
                    ApplyFieldFlags(Existing, Source, false);
                    Owner.FieldDefinitions.Add(Existing);
                }
                this.Report.CountCreated("fieldDefinition", this.IsPreview);
                return;
            }

            if (!String.IsNullOrWhiteSpace(Source.DataTypeTechName) && Existing.FieldType != null &&
                this.Resolver.FindDataType(Source.DataTypeTechName) == null)
            {
                Skip("fieldDefinition", "Skipped field '" + Existing.TechName + "' because dataType '" + Source.DataTypeTechName +
                                        "' was not resolved. Valid dataType techNames: " + KnownDataTypeTechNames());
                return;
            }

            if (!String.IsNullOrWhiteSpace(Source.DataTypeTechName) && Existing.FieldType != null &&
                !String.Equals(Existing.FieldType.TechName, Source.DataTypeTechName, StringComparison.OrdinalIgnoreCase))
            {
                this.Report.DangerousChangesSkipped++;
                Skip("fieldDefinition", "Skipped incompatible field data type change for '" + Existing.TechName + "' from '" +
                                        Existing.FieldType.TechName + "' to '" + Source.DataTypeTechName + "'.");
                return;
            }

            var Changed = ApplyFormalFields(Existing, Source);
            Changed = ApplyFieldFlags(Existing, Source, Changed);
            if (Changed)
                this.Report.CountUpdated("fieldDefinition", this.IsPreview);
        }

        private void MergeConceptDefinition(DomainJsonElement Source)
        {
            var Existing = this.Resolver.ConceptDefinition(Source.Id, Source.TechName);
            if (Existing == null)
            {
                if (!RequireNameTech(Source, "conceptDefinition"))
                    return;

                if (!this.IsPreview)
                {
                    var Ancestor = this.Resolver.ConceptDefinition(null, Source.AncestorTechName)
                                   .NullDefault(this.TargetDomain.ConceptDefinitions.FirstOrDefault());
                    Existing = new ConceptDefinition(this.TargetDomain, Ancestor, Source.Name, Source.TechName,
                                                     Source.RepresentativeShape.NullDefault(Shapes.Rectangle),
                                                     Source.Summary.NullDefault(""));
                    ApplyIdeaDefinitionFields(Existing, Source);
                    this.TargetDomain.ConceptDefinitions.Add(Existing);
                }
                this.Report.CountCreated("conceptDefinition", this.IsPreview);
                TrackPlanned(this.PlannedConceptDefinitionTechNames, Source.TechName);
            }
            else
            {
                if (ApplyIdeaDefinitionFields(Existing, Source))
                    this.Report.CountUpdated("conceptDefinition", this.IsPreview);
            }

            if (Existing != null || this.IsPreview)
                foreach (var Template in Source.OutputTemplates ?? new List<DomainJsonElement>())
                {
                    Template.OwnerScope = "conceptDefinition";
                    Template.OwnerTechName = Template.OwnerTechName.NullDefault(Source.TechName);
                    MergeOutputTemplate(Template);
                }
        }

        private void MergeRelationshipDefinition(DomainJsonElement Source)
        {
            var Existing = this.Resolver.RelationshipDefinition(Source.Id, Source.TechName);
            if (Existing == null)
            {
                if (!RequireNameTech(Source, "relationshipDefinition"))
                    return;

                var OriginRole = BuildRoleForCreate(Source.RoleDefinitions, "Origin");
                var TargetRole = BuildRoleForCreate(Source.RoleDefinitions, "Target");

                if (!this.IsPreview)
                {
                    var Ancestor = this.Resolver.RelationshipDefinition(null, Source.AncestorTechName)
                                   .NullDefault(this.TargetDomain.RelationshipDefinitions.FirstOrDefault());
                    Existing = new RelationshipDefinition(this.TargetDomain, Ancestor, Source.Name, Source.TechName,
                                                          Source.RepresentativeShape.NullDefault(Shapes.Ellipse),
                                                          Source.Summary.NullDefault(""), null, OriginRole, TargetRole);
                    ApplyRelationshipDefinitionFields(Existing, Source);
                    this.TargetDomain.RelationshipDefinitions.Add(Existing);
                }
                this.Report.CountCreated("relationshipDefinition", this.IsPreview);
                TrackPlanned(this.PlannedRelationshipDefinitionTechNames, Source.TechName);
            }
            else
            {
                if (ApplyRelationshipDefinitionFields(Existing, Source))
                    this.Report.CountUpdated("relationshipDefinition", this.IsPreview);
            }

            foreach (var Role in Source.RoleDefinitions ?? new List<DomainJsonElement>())
                MergeRelationshipRole(Role, Existing, Source.TechName);

            if (Existing != null || this.IsPreview)
                foreach (var Template in Source.OutputTemplates ?? new List<DomainJsonElement>())
                {
                    Template.OwnerScope = "relationshipDefinition";
                    Template.OwnerTechName = Template.OwnerTechName.NullDefault(Source.TechName);
                    MergeOutputTemplate(Template);
                }
        }

        private LinkRoleDefinition BuildRoleForCreate(IEnumerable<DomainJsonElement> Roles, string RoleType)
        {
            var Source = Roles == null ? null : Roles.FirstOrDefault(Role => String.Equals(Role.RoleType, RoleType, StringComparison.OrdinalIgnoreCase));
            var Parsed = ParseRoleType(Source == null ? RoleType : Source.RoleType);
            var Name = Source == null ? RoleType : Source.Name.NullDefault(RoleType);
            var TechName = Source == null ? RoleType : Source.TechName.NullDefault(RoleType);
            var Summary = Source == null ? "" : Source.Summary.NullDefault("");
            return new LinkRoleDefinition(Parsed, Name, TechName, Summary);
        }

        private void MergeRelationshipRole(DomainJsonElement Source, RelationshipDefinition Owner, string OwnerTechName)
        {
            Source.OwnerTechName = Source.OwnerTechName.NullDefault(OwnerTechName);
            var Existing = this.Resolver.RelationshipRole(Owner, Source.Id, Source.TechName, Source.RoleType);
            if (Existing == null)
            {
                if (Owner == null)
                {
                    Skip("relationshipRole", "Cannot create role '" + Source.TechName + "' because owner relationship definition '" + Source.OwnerTechName + "' was not found.");
                    return;
                }

                var RoleType = ParseRoleType(Source.RoleType);
                if (!this.IsPreview)
                {
                    Existing = new LinkRoleDefinition(Owner, RoleType, Source.Name.NullDefault(RoleType.ToString()),
                                                      Source.TechName.NullDefault(RoleType.ToString()), Source.Summary.NullDefault(""));
                    ApplyRelationshipRoleFields(Existing, Source);
                    if (RoleType == ERoleType.Target)
                        Owner.TargetLinkRoleDef = Existing;
                    else
                        Owner.OriginOrParticipantLinkRoleDef = Existing;
                }
                this.Report.CountCreated("relationshipRole", this.IsPreview);
                return;
            }

            if (ApplyRelationshipRoleFields(Existing, Source))
                this.Report.CountUpdated("relationshipRole", this.IsPreview);
        }

        private void MergeOutputTemplate(DomainJsonElement Source)
        {
            var OwnerScope = NormalizeOwnerScope(Source.OwnerScope.NullDefault(GetSetString(Source.Set, "ownerKind")));
            var OwnerTechName = Source.OwnerTechName;
            var LanguageMatch = this.Resolver.ExternalLanguageMatch(null, Source.ExternalLanguageTechName);
            var Language = LanguageMatch.Item;
            var LanguageIsPlanned = (Language == null && this.IsPreview && !String.IsNullOrWhiteSpace(Source.ExternalLanguageTechName) &&
                                     this.PlannedExternalLanguageTechNames.Contains(Source.ExternalLanguageTechName));

            if (LanguageMatch.IsAmbiguous)
            {
                Skip("outputTemplate", "Cannot import output template '" + Source.TechName + "' because external language '" +
                                       Source.ExternalLanguageTechName + "' matched multiple normalized techNames: " +
                                       String.Join(", ", LanguageMatch.AmbiguousCandidates.Select(Item => Item.TechName).OrderBy(Text => Text).ToArray()));
                return;
            }

            if (Language == null && !String.IsNullOrWhiteSpace(Source.ExternalLanguageTechName) && !LanguageIsPlanned)
            {
                Skip("outputTemplate", "Cannot import output template '" + Source.TechName + "' because external language '" +
                                       Source.ExternalLanguageTechName + "' was not resolved. Valid external language techNames: " +
                                       KnownExternalLanguageTechNames());
                return;
            }

            if (Language == null && String.IsNullOrWhiteSpace(Source.ExternalLanguageTechName))
                Language = this.TargetDomain.CurrentExternalLanguage.NullDefault(this.TargetDomain.ExternalLanguages.FirstOrDefault());

            if (Language == null && !LanguageIsPlanned)
            {
                Skip("outputTemplate", "Cannot import output template '" + Source.TechName + "' because no external language is available.");
                return;
            }

            var TargetList = ResolveTemplateList(OwnerScope, OwnerTechName);
            if (TargetList == null)
            {
                if (this.IsPreview && IsPlannedTemplateOwner(OwnerScope, OwnerTechName))
                {
                    this.Report.Log("Domain JSON planned outputTemplate create: techName=" + Source.TechName +
                                    " ownerScope=" + OwnerScope + " owner=" + OwnerTechName +
                                    " ownerMatch=planned language=" + LanguageTechName(Language, Source) +
                                    " languageMatch=" + (LanguageIsPlanned ? "planned" : LanguageMatch.MatchMethod));
                    this.Report.CountCreated("outputTemplate", this.IsPreview);
                    return;
                }

                Skip("outputTemplate", "Cannot import output template '" + Source.TechName + "' because owner scope '" +
                                       OwnerScope + "' / owner '" + OwnerTechName + "' was not found.");
                return;
            }

            if (Language == null && LanguageIsPlanned)
            {
                this.Report.Log("Domain JSON planned outputTemplate create: techName=" + Source.TechName +
                                " ownerScope=" + OwnerScope + " owner=" + OwnerTechName +
                                " language=" + Source.ExternalLanguageTechName + " languageMatch=planned");
                this.Report.CountCreated("outputTemplate", this.IsPreview);
                return;
            }

            if (LanguageMatch.MatchMethod == "normalized techName")
                this.Report.Log("Domain JSON outputTemplate language matched by normalized techName: requested='" +
                                Source.ExternalLanguageTechName.ToStringAlways() + "' matched='" + Language.TechName + "'");

            var Existing = TargetList.FirstOrDefault(Template => Template.Language == Language);
            if (Existing == null)
            {
                this.Report.Log("Domain JSON " + (this.IsPreview ? "planned" : "applied") + " outputTemplate create: techName=" +
                                Source.TechName + " ownerScope=" + OwnerScope + " owner=" + OwnerTechName +
                                " language=" + Language.TechName + " languageMatch=" + LanguageMatch.MatchMethod);
                this.Report.Log(OutputTemplateImportDetails("create", Source, null, Language, OwnerScope, OwnerTechName, LanguageMatch.MatchMethod));
                if (!this.IsPreview)
                    TargetList.Add(new TextTemplate(Language, Source.TemplateText.NullDefault(""), Source.ExtendsBaseTemplate.GetValueOrDefault(true)));
                this.Report.CountCreated("outputTemplate", this.IsPreview);
                return;
            }

            var Changed = false;
            var OldText = Existing.Text;
            var OldExtendsBaseTemplate = Existing.ExtendsBaseTemplate;
            if (Source.TemplateText != null && Existing.Text != Source.TemplateText)
            {
                if (!this.IsPreview)
                    Existing.Text = Source.TemplateText;
                Changed = true;
            }

            if (Source.ExtendsBaseTemplate != null && Existing.ExtendsBaseTemplate != Source.ExtendsBaseTemplate.Value)
            {
                if (!this.IsPreview)
                    Existing.ExtendsBaseTemplate = Source.ExtendsBaseTemplate.Value;
                Changed = true;
            }

            if (Changed)
            {
                this.Report.Log(OutputTemplateImportDetails("update", Source, Existing, Language, OwnerScope, OwnerTechName, LanguageMatch.MatchMethod,
                                                            OldText, OldExtendsBaseTemplate));
                this.Report.CountUpdated("outputTemplate", this.IsPreview);
            }
        }

        private IList<TextTemplate> ResolveTemplateList(string OwnerScope, string OwnerTechName)
        {
            if (String.Equals(OwnerScope, "domainConcept", StringComparison.OrdinalIgnoreCase))
                return this.TargetDomain.OutputTemplatesForConcepts;

            if (String.Equals(OwnerScope, "domainRelationship", StringComparison.OrdinalIgnoreCase))
                return this.TargetDomain.OutputTemplatesForRelationships;

            if (String.Equals(OwnerScope, "conceptDefinition", StringComparison.OrdinalIgnoreCase))
            {
                var Owner = this.Resolver.ConceptDefinition(null, OwnerTechName);
                return Owner == null ? null : Owner.OutputTemplates;
            }

            if (String.Equals(OwnerScope, "relationshipDefinition", StringComparison.OrdinalIgnoreCase))
            {
                var Owner = this.Resolver.RelationshipDefinition(null, OwnerTechName);
                return Owner == null ? null : Owner.OutputTemplates;
            }

            return null;
        }

        private static string OutputTemplateImportDetails(string Action, DomainJsonElement Source, TextTemplate Existing,
                                                          ExternalLanguageDeclaration Language, string OwnerScope,
                                                          string OwnerTechName, string LanguageMatch,
                                                          string OldText = null, bool? OldExtendsBaseTemplate = null)
        {
            var NewText = Source.TemplateText.NullDefault(Existing == null ? "" : Existing.Text);
            var Directives = OutputTemplateDirectiveInfo.Parse(NewText);
            var SetRole = GetSetString(Source.Set, "templateRole");
            if (!SetRole.IsAbsent())
            {
                Directives.Role = OutputTemplateDirectiveInfo.ParseRole(SetRole);
                Directives.HasExplicitRole = true;
            }

            var TargetExtension = GetSetString(Source.Set, "targetFileExtension").NullDefault(Directives.TargetFileExtension);
            var TargetFileName = GetSetString(Source.Set, "targetFileName").NullDefault(Directives.TargetFileName);

            var Builder = new System.Text.StringBuilder();
            Builder.Append("Domain JSON " + Action + " outputTemplate details: ");
            Builder.Append("techName=" + Source.TechName.ToStringAlways());
            Builder.Append(" ownerScope=" + OwnerScope.ToStringAlways());
            Builder.Append(" ownerTechName=" + OwnerTechName.ToStringAlways());
            Builder.Append(" language=" + (Language == null ? Source.ExternalLanguageTechName.ToStringAlways() : Language.TechName.ToStringAlways()));
            Builder.Append(" languageMatch=" + LanguageMatch.ToStringAlways());
            Builder.Append(" sourceCollection=" + OwnerScope.ToStringAlways());
            Builder.Append(" oldLength=" + OldText.NullDefault("").Length.ToString(CultureInfo.InvariantCulture));
            Builder.Append(" newLength=" + NewText.NullDefault("").Length.ToString(CultureInfo.InvariantCulture));
            Builder.Append(" oldHash=" + (OldText == null ? "<none>" : OutputTemplateDiagnostics.HashText(OldText).Substring(0, 16)));
            Builder.Append(" newHash=" + OutputTemplateDiagnostics.HashText(NewText).Substring(0, 16));
            Builder.Append(" oldExtendsBaseTemplate=" + (OldExtendsBaseTemplate == null ? "<none>" : OldExtendsBaseTemplate.Value.ToString(CultureInfo.InvariantCulture)));
            Builder.Append(" extendsBaseTemplate=" + Source.ExtendsBaseTemplate.GetValueOrDefault(Existing == null ? true : Existing.ExtendsBaseTemplate).ToString(CultureInfo.InvariantCulture));
            Builder.Append(" targetFileName=" + TargetFileName.ToStringAlways());
            Builder.Append(" targetFileExtension=" + TargetExtension.ToStringAlways());
            Builder.Append(" templateRole=" + Directives.Role);
            return Builder.ToString();
        }

        private void ApplyOperations()
        {
            var Operations = this.Document.Operations ?? new List<DomainJsonOperation>();
            for (int Index = 0; Index < Operations.Count; Index++)
            {
                var Operation = Operations[Index];
                this.Report.CurrentOperationIndex = Index + 1;
                this.Report.CurrentOperationSummary = DescribeOperation(Operation);
                this.Report.Log("Domain JSON operation [" + (Index + 1).ToString(CultureInfo.InvariantCulture) + "/" +
                                Operations.Count.ToString(CultureInfo.InvariantCulture) + "] " +
                                this.Report.CurrentOperationSummary + " -> " + (this.IsPreview ? "plan start" : "apply start"));

                ApplyOperation(Operation);
            }
        }

        private void ApplyOperation(DomainJsonOperation Operation)
        {
            var Op = Operation.Op.NullDefault("").ToLowerInvariant();
            if (Op == "delete")
            {
                this.Report.DangerousChangesSkipped++;
                Skip(Operation.Entity, "Delete operation for domain entity '" + Operation.Entity + "' is skipped by default. No data was deleted.");
                return;
            }

            if (Op != "create" && Op != "update")
            {
                Skip(Operation.Entity, "Unsupported Domain JSON operation op '" + Operation.Op.ToStringAlways() + "'.");
                return;
            }

            var Source = ElementFromOperation(Operation);
            if (String.Equals(Operation.Entity, "domain", StringComparison.OrdinalIgnoreCase))
                MergeDomain(Source);
            else if (String.Equals(Operation.Entity, "externalLanguage", StringComparison.OrdinalIgnoreCase))
                MergeExternalLanguage(Source);
            else if (String.Equals(Operation.Entity, "linkRoleVariant", StringComparison.OrdinalIgnoreCase))
                MergeLinkRoleVariant(Source);
            else if (String.Equals(Operation.Entity, "markerCluster", StringComparison.OrdinalIgnoreCase))
                MergeMarkerCluster(Source);
            else if (String.Equals(Operation.Entity, "markerDefinition", StringComparison.OrdinalIgnoreCase))
                MergeMarkerDefinition(Source);
            else if (String.Equals(Operation.Entity, "conceptDefinitionCluster", StringComparison.OrdinalIgnoreCase))
                MergeConceptDefinitionCluster(Source);
            else if (String.Equals(Operation.Entity, "relationshipDefinitionCluster", StringComparison.OrdinalIgnoreCase))
                MergeRelationshipDefinitionCluster(Source);
            else if (String.Equals(Operation.Entity, "tableDefinitionCategory", StringComparison.OrdinalIgnoreCase))
                MergeTableDefinitionCategory(Source);
            else if (String.Equals(Operation.Entity, "fieldDefinitionCategory", StringComparison.OrdinalIgnoreCase))
                MergeFieldDefinitionCategory(Source);
            else if (String.Equals(Operation.Entity, "tableDefinition", StringComparison.OrdinalIgnoreCase))
                MergeTableDefinition(Source);
            else if (String.Equals(Operation.Entity, "fieldDefinition", StringComparison.OrdinalIgnoreCase))
                MergeFieldDefinition(Source, this.Resolver.TableDefinition(Operation.OwnerId, Operation.OwnerTechName), Operation.OwnerTechName);
            else if (String.Equals(Operation.Entity, "conceptDefinition", StringComparison.OrdinalIgnoreCase))
                MergeConceptDefinition(Source);
            else if (String.Equals(Operation.Entity, "relationshipDefinition", StringComparison.OrdinalIgnoreCase))
                MergeRelationshipDefinition(Source);
            else if (String.Equals(Operation.Entity, "relationshipRole", StringComparison.OrdinalIgnoreCase))
                MergeRelationshipRole(Source, this.Resolver.RelationshipDefinition(Operation.OwnerId, Operation.OwnerTechName), Operation.OwnerTechName);
            else if (String.Equals(Operation.Entity, "outputTemplate", StringComparison.OrdinalIgnoreCase))
                MergeOutputTemplate(Source);
            else
                Skip(Operation.Entity, "Unsupported Domain JSON entity '" + Operation.Entity + "'.");
        }

        private DomainJsonElement ElementFromOperation(DomainJsonOperation Operation)
        {
            var Source = new DomainJsonElement();
            Source.Entity = Operation.Entity;
            Source.Id = Operation.Id;
            Source.TechName = Operation.TechName.NullDefault(GetSetString(Operation.Set, "techName"));
            Source.OwnerId = Operation.OwnerId.NullDefault(GetSetString(Operation.Set, "ownerId"));
            Source.OwnerTechName = Operation.OwnerTechName
                .NullDefault(GetSetString(Operation.Set, "ownerTechName"))
                .NullDefault(GetSetString(Operation.Set, "tableDefinitionTechName"))
                .NullDefault(GetSetString(Operation.Set, "parentTechName"));
            Source.OwnerScope = Operation.OwnerScope
                .NullDefault(GetSetString(Operation.Set, "ownerScope"))
                .NullDefault(GetSetString(Operation.Set, "ownerKind"));
            Source.Name = GetSetString(Operation.Set, "name");
            Source.Summary = GetSetString(Operation.Set, "summary");
            Source.Description = GetSetString(Operation.Set, "description");
            Source.TechSpec = GetSetString(Operation.Set, "techSpec");
            Source.ClusterTechName = GetSetString(Operation.Set, "clusterTechName");
            Source.CategoryTechName = GetSetString(Operation.Set, "categoryTechName");
            Source.DataTypeTechName = GetSetString(Operation.Set, "dataTypeTechName")
                                      .NullDefault(GetSetString(Operation.Set, "customFieldsTableTechName"));
            Source.RepresentativeShape = GetSetString(Operation.Set, "representativeShape");
            Source.AncestorTechName = GetSetString(Operation.Set, "ancestorTechName");
            Source.RoleType = GetSetString(Operation.Set, "roleType");
            Source.ExternalLanguageTechName = GetSetString(Operation.Set, "externalLanguageTechName");
            Source.TemplateText = GetSetString(Operation.Set, "templateText");
            Source.IsComposable = GetSetBool(Operation.Set, "isComposable");
            Source.IsVersionable = GetSetBool(Operation.Set, "isVersionable");
            Source.CanAutomaticallyCreateRelatedConcepts = GetSetBool(Operation.Set, "canAutomaticallyCreateRelatedConcepts");
            Source.IsDirectional = GetSetBool(Operation.Set, "isDirectional");
            Source.IsSimple = GetSetBool(Operation.Set, "isSimple");
            Source.HideCentralSymbolWhenSimple = GetSetBool(Operation.Set, "hideCentralSymbolWhenSimple");
            Source.ShowNameIfHidingCentralSymbol = GetSetBool(Operation.Set, "showNameIfHidingCentralSymbol");
            Source.RelatedIdeasAreOrdered = GetSetBool(Operation.Set, "relatedIdeasAreOrdered");
            Source.ExtendsBaseTemplate = GetSetBool(Operation.Set, "extendsBaseTemplate");
            Source.MaxConnections = GetSetUInt(Operation.Set, "maxConnections");
            Source.Order = GetSetInt(Operation.Set, "order");
            Source.AllowedVariantTechNames = GetSetStringList(Operation.Set, "allowedVariantTechNames");
            Source.AssociableIdeaDefinitionTechNames = GetSetStringList(Operation.Set, "associableIdeaDefinitionTechNames");
            Source.Set = Operation.Set ?? new Dictionary<string, object>();
            return Source;
        }

        private bool ApplyIdeaDefinitionFields(IdeaDefinition Target, DomainJsonElement Source)
        {
            var Changed = ApplyFormalFields(Target, Source);

            if (!String.IsNullOrWhiteSpace(Source.ClusterTechName))
            {
                var Cluster = Target is RelationshipDefinition
                              ? (FormalPresentationElement)this.Resolver.RelationshipDefinitionCluster(null, Source.ClusterTechName)
                              : this.Resolver.ConceptDefinitionCluster(null, Source.ClusterTechName);
                if (Target.Cluster != Cluster)
                {
                    if (!this.IsPreview)
                        Target.Cluster = Cluster;
                    Changed = true;
                }
            }

            if (!String.IsNullOrWhiteSpace(Source.RepresentativeShape) && Target.RepresentativeShape != Source.RepresentativeShape)
            {
                if (!this.IsPreview)
                    Target.RepresentativeShape = Source.RepresentativeShape;
                Changed = true;
            }

            if (Source.IsComposable != null && Target.IsComposable != Source.IsComposable.Value)
            {
                if (!this.IsPreview)
                    Target.IsComposable = Source.IsComposable.Value;
                Changed = true;
            }

            if (Source.IsVersionable != null && Target.IsVersionable != Source.IsVersionable.Value)
            {
                if (!this.IsPreview)
                    Target.IsVersionable = Source.IsVersionable.Value;
                Changed = true;
            }

            if (Source.CanAutomaticallyCreateRelatedConcepts != null &&
                Target.CanAutomaticallyCreateRelatedConcepts != Source.CanAutomaticallyCreateRelatedConcepts.Value)
            {
                if (!this.IsPreview)
                    Target.CanAutomaticallyCreateRelatedConcepts = Source.CanAutomaticallyCreateRelatedConcepts.Value;
                Changed = true;
            }

            if (!String.IsNullOrWhiteSpace(Source.DataTypeTechName))
            {
                var TableDef = this.Resolver.TableDefinition(null, Source.DataTypeTechName);
                if (TableDef == null && this.IsPreview && this.PlannedTableDefinitionTechNames.Contains(Source.DataTypeTechName))
                {
                    this.Report.Log("Domain JSON planned customFieldsTable update for " + Describe(Target) + " -> " + Source.DataTypeTechName + " (planned table)");
                    Changed = true;
                }
                else if (TableDef == null)
                    Skip(Source.Entity.NullDefault("ideaDefinition"), "Custom fields table '" + Source.DataTypeTechName +
                                                              "' was not found for definition '" + Target.TechName + "'.");
                else if (Target.CustomFieldsTableDef != TableDef)
                {
                    this.Report.Log("Domain JSON " + (this.IsPreview ? "planned" : "applied") +
                                    " customFieldsTable update for " + Describe(Target) + " -> " + TableDef.TechName);
                    if (!this.IsPreview)
                        Target.CustomFieldsTableDef = TableDef;
                    Changed = true;
                }
            }

            return Changed;
        }

        private bool ApplyRelationshipDefinitionFields(RelationshipDefinition Target, DomainJsonElement Source)
        {
            var Changed = ApplyIdeaDefinitionFields(Target, Source);

            if (Source.IsDirectional != null && Target.IsDirectional != Source.IsDirectional.Value)
            {
                if (!this.IsPreview)
                    Target.IsDirectional = Source.IsDirectional.Value;
                Changed = true;
            }

            if (Source.IsSimple != null && Target.IsSimple != Source.IsSimple.Value)
            {
                if (!this.IsPreview)
                    Target.IsSimple = Source.IsSimple.Value;
                Changed = true;
            }

            if (Source.HideCentralSymbolWhenSimple != null && Target.HideCentralSymbolWhenSimple != Source.HideCentralSymbolWhenSimple.Value)
            {
                if (!this.IsPreview)
                    Target.HideCentralSymbolWhenSimple = Source.HideCentralSymbolWhenSimple.Value;
                Changed = true;
            }

            if (Source.ShowNameIfHidingCentralSymbol != null && Target.ShowNameIfHidingCentralSymbol != Source.ShowNameIfHidingCentralSymbol.Value)
            {
                if (!this.IsPreview)
                    Target.ShowNameIfHidingCentralSymbol = Source.ShowNameIfHidingCentralSymbol.Value;
                Changed = true;
            }

            return Changed;
        }

        private bool ApplyRelationshipRoleFields(LinkRoleDefinition Target, DomainJsonElement Source)
        {
            var Changed = ApplyFormalFields(Target, Source);

            if (!String.IsNullOrWhiteSpace(Source.RoleType))
            {
                var RoleType = ParseRoleType(Source.RoleType);
                if (Target.RoleType != RoleType)
                {
                    this.Report.DangerousChangesSkipped++;
                    Skip("relationshipRole", "Skipped role type change for '" + Target.TechName + "' because it could invalidate existing relationship links.");
                }
            }

            if (Source.MaxConnections != null && Target.MaxConnections != Source.MaxConnections.Value)
            {
                if (!this.IsPreview)
                    Target.MaxConnections = Source.MaxConnections.Value;
                Changed = true;
            }

            if (Source.RelatedIdeasAreOrdered != null && Target.RelatedIdeasAreOrdered != Source.RelatedIdeasAreOrdered.Value)
            {
                if (!this.IsPreview)
                    Target.RelatedIdeasAreOrdered = Source.RelatedIdeasAreOrdered.Value;
                Changed = true;
            }

            foreach (var VariantTechName in Source.AllowedVariantTechNames ?? new List<string>())
            {
                var Variant = this.Resolver.LinkRoleVariant(VariantTechName);
                if (Variant == null || Target.AllowedVariants.Contains(Variant))
                    continue;

                if (!this.IsPreview)
                    Target.AllowedVariants.Add(Variant);
                Changed = true;
            }

            foreach (var IdeaDefTechName in Source.AssociableIdeaDefinitionTechNames ?? new List<string>())
            {
                var IdeaDef = this.Resolver.IdeaDefinition(null, IdeaDefTechName);
                if (IdeaDef == null || Target.AssociableIdeaDefs.Contains(IdeaDef))
                    continue;

                if (!this.IsPreview)
                    Target.AssociableIdeaDefs.Add(IdeaDef);
                Changed = true;
            }

            return Changed;
        }

        private bool ApplyFieldFlags(FieldDefinition Target, DomainJsonElement Source, bool Changed)
        {
            bool? IsRequired = GetSetBool(Source.Set, "isRequired");
            bool? HideInDiagram = GetSetBool(Source.Set, "hideInDiagram");

            if (IsRequired != null && Target.IsRequired != IsRequired.Value)
            {
                if (!this.IsPreview)
                    Target.IsRequired = IsRequired.Value;
                Changed = true;
            }

            if (HideInDiagram != null && Target.HideInDiagram != HideInDiagram.Value)
            {
                if (!this.IsPreview)
                    Target.HideInDiagram = HideInDiagram.Value;
                Changed = true;
            }

            return Changed;
        }

        private DataType ResolveFieldDataType(DomainJsonElement Source)
        {
            if (String.IsNullOrWhiteSpace(Source.DataTypeTechName))
                return Instrumind.ThinkComposer.MetaModel.InformationMetaModel.DataType.DataTypeText;

            var Result = this.Resolver.FindDataType(Source.DataTypeTechName);
            if (Result == null)
                Skip("fieldDefinition", "Cannot import field '" + Source.TechName + "' because dataType '" +
                                        Source.DataTypeTechName + "' was not resolved. Valid dataType techNames: " +
                                        KnownDataTypeTechNames());

            return Result;
        }

        private bool ApplyFormalFields(FormalElement Target, DomainJsonElement Source, string Entity = null, string MatchMethod = null)
        {
            var Changed = false;
            Entity = Entity.NullDefault(Source == null ? null : Source.Entity).NullDefault("domainEntity");
            MatchMethod = MatchMethod.NullDefault("unspecified");

            if (Source.Name != null && Target.Name != Source.Name)
            {
                this.Report.LogFieldUpdate(Entity, "name", Describe(Target), MatchMethod, Target.Name, Source.Name, this.IsPreview);
                if (!this.IsPreview)
                    Target.Name = Source.Name;
                Changed = true;
            }

            if (Source.TechName != null && Target.TechName != Source.TechName)
            {
                this.Report.LogFieldUpdate(Entity, "techName", Describe(Target), MatchMethod, Target.TechName, Source.TechName, this.IsPreview);
                if (!this.IsPreview)
                    Target.TechName = Source.TechName;
                Changed = true;
            }

            if (Source.Summary != null && Target.Summary != Source.Summary)
            {
                this.Report.LogFieldUpdate(Entity, "summary", Describe(Target), MatchMethod, Target.Summary, Source.Summary, this.IsPreview);
                if (!this.IsPreview)
                    Target.Summary = Source.Summary;
                Changed = true;
            }

            if (Source.TechSpec != null && Target.TechSpec != Source.TechSpec)
            {
                this.Report.LogFieldUpdate(Entity, "techSpec", Describe(Target), MatchMethod, Target.TechSpec, Source.TechSpec, this.IsPreview);
                if (!this.IsPreview)
                    Target.TechSpec = Source.TechSpec;
                Changed = true;
            }

            if (Source.Description != null)
            {
                var StorageDescription = Display.PlainTextToXamlRichText(Source.Description);
                if (Target.Description != StorageDescription)
                {
                    this.Report.LogFieldUpdate(Entity, "description", Describe(Target), MatchMethod,
                                               Display.XamlRichTextToPlainTextOrSelf(Target.Description),
                                               Source.Description, this.IsPreview);
                    if (!this.IsPreview)
                        Target.Description = StorageDescription;
                    Changed = true;
                }
            }

            return Changed;
        }

        private bool ApplySimpleFields(SimpleElement Target, DomainJsonElement Source, string Entity = null, string MatchMethod = null)
        {
            var Changed = false;
            Entity = Entity.NullDefault(Source == null ? null : Source.Entity).NullDefault("domainEntity");
            MatchMethod = MatchMethod.NullDefault("unspecified");

            if (Source.Name != null && Target.Name != Source.Name)
            {
                this.Report.LogFieldUpdate(Entity, "name", Describe(Target), MatchMethod, Target.Name, Source.Name, this.IsPreview);
                if (!this.IsPreview)
                    Target.Name = Source.Name;
                Changed = true;
            }

            if (Source.TechName != null && Target.TechName != Source.TechName)
            {
                this.Report.LogFieldUpdate(Entity, "techName", Describe(Target), MatchMethod, Target.TechName, Source.TechName, this.IsPreview);
                if (!this.IsPreview)
                    Target.TechName = Source.TechName;
                Changed = true;
            }

            if (Source.Summary != null && Target.Summary != Source.Summary)
            {
                this.Report.LogFieldUpdate(Entity, "summary", Describe(Target), MatchMethod, Target.Summary, Source.Summary, this.IsPreview);
                if (!this.IsPreview)
                    Target.Summary = Source.Summary;
                Changed = true;
            }

            if (Source.TechSpec != null && Target.TechSpec != Source.TechSpec)
            {
                this.Report.LogFieldUpdate(Entity, "techSpec", Describe(Target), MatchMethod, Target.TechSpec, Source.TechSpec, this.IsPreview);
                if (!this.IsPreview)
                    Target.TechSpec = Source.TechSpec;
                Changed = true;
            }

            return Changed;
        }

        private void MergeSimplePresentation(DomainJsonElement Source, string Entity, IList<SimplePresentationElement> TargetList, Func<string, SimplePresentationElement> Resolver)
        {
            var Existing = Resolver(Source.TechName);
            if (Existing == null)
            {
                if (!RequireNameTech(Source, Entity))
                    return;

                if (!this.IsPreview)
                {
                    Existing = new SimplePresentationElement(Source.Name, Source.TechName, Source.Summary.NullDefault(""));
                    ApplySimpleFields(Existing, Source);
                    TargetList.Add(Existing);
                }
                this.Report.CountCreated(Entity, this.IsPreview);
                return;
            }

            if (ApplySimpleFields(Existing, Source))
                this.Report.CountUpdated(Entity, this.IsPreview);
        }

        private void MergeFormalPresentation(DomainJsonElement Source, string Entity, IList<FormalPresentationElement> TargetList, Func<string, string, FormalPresentationElement> Resolver)
        {
            var Existing = Resolver(Source.Id, Source.TechName);
            if (Existing == null)
            {
                if (!RequireNameTech(Source, Entity))
                    return;

                if (!this.IsPreview)
                {
                    Existing = new FormalPresentationElement(Source.Name, Source.TechName, Source.Summary.NullDefault(""));
                    ApplyFormalFields(Existing, Source);
                    TargetList.Add(Existing);
                }
                this.Report.CountCreated(Entity, this.IsPreview);
                return;
            }

            if (ApplyFormalFields(Existing, Source))
                this.Report.CountUpdated(Entity, this.IsPreview);
        }

        private bool RequireNameTech(DomainJsonElement Source, string Entity)
        {
            if (!String.IsNullOrWhiteSpace(Source.Name) && !String.IsNullOrWhiteSpace(Source.TechName))
                return true;

            Skip(Entity, "Cannot create " + Entity + " because name and techName are required.");
            return false;
        }

        private void Skip(string Entity, string Reason)
        {
            this.Report.CountSkipped(Entity, this.IsPreview);
            var Prefix = this.Report.CurrentOperationIndex > 0
                         ? "Operation [" + this.Report.CurrentOperationIndex.ToString(CultureInfo.InvariantCulture) + "] " +
                           this.Report.CurrentOperationSummary.ToStringAlways() + " -> skipped: "
                         : "";
            this.Report.Skipped(Prefix + Reason);
        }

        private int EstimateLegacyRetained()
        {
            return this.TargetDomain.Definitions.Count() + this.TargetDomain.TableDefinitions.Count() + this.TargetDomain.MarkerDefinitions.Count();
        }

        private static ERoleType ParseRoleType(string RoleType)
        {
            ERoleType Parsed;
            return Enum.TryParse<ERoleType>(RoleType.NullDefault("Origin"), true, out Parsed) ? Parsed : ERoleType.Origin;
        }

        private static void TrackPlanned(HashSet<string> Target, string TechName)
        {
            if (Target != null && !String.IsNullOrWhiteSpace(TechName))
                Target.Add(TechName);
        }

        private static string NormalizeOwnerScope(string OwnerScope)
        {
            if (String.Equals(OwnerScope, "concept", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(OwnerScope, "conceptDefinition", StringComparison.OrdinalIgnoreCase))
                return "conceptDefinition";

            if (String.Equals(OwnerScope, "relationship", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(OwnerScope, "relationshipDefinition", StringComparison.OrdinalIgnoreCase))
                return "relationshipDefinition";

            if (String.Equals(OwnerScope, "domainConcept", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(OwnerScope, "conceptDefinitions", StringComparison.OrdinalIgnoreCase))
                return "domainConcept";

            if (String.Equals(OwnerScope, "domainRelationship", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(OwnerScope, "relationshipDefinitions", StringComparison.OrdinalIgnoreCase))
                return "domainRelationship";

            return OwnerScope.NullDefault("");
        }

        private bool IsPlannedTemplateOwner(string OwnerScope, string OwnerTechName)
        {
            if (String.Equals(OwnerScope, "conceptDefinition", StringComparison.OrdinalIgnoreCase))
                return this.PlannedConceptDefinitionTechNames.Contains(OwnerTechName.NullDefault(""));

            if (String.Equals(OwnerScope, "relationshipDefinition", StringComparison.OrdinalIgnoreCase))
                return this.PlannedRelationshipDefinitionTechNames.Contains(OwnerTechName.NullDefault(""));

            if (String.Equals(OwnerScope, "domainConcept", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(OwnerScope, "domainRelationship", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private string KnownDataTypeTechNames()
        {
            return String.Join(", ", this.TargetDomain.AvailableDataTypes.Select(Type => Type.TechName).OrderBy(Text => Text).ToArray());
        }

        private string KnownExternalLanguageTechNames()
        {
            return String.Join(", ", this.TargetDomain.ExternalLanguages.Select(Language => Language.TechName).OrderBy(Text => Text).ToArray());
        }

        private static string LanguageTechName(ExternalLanguageDeclaration Language, DomainJsonElement Source)
        {
            return Language == null ? Source.ExternalLanguageTechName.ToStringAlways() : Language.TechName.ToStringAlways();
        }

        private static string GetSetString(IDictionary<string, object> Set, string Key)
        {
            if (Set == null || !Set.ContainsKey(Key) || Set[Key] == null)
                return null;
            return Convert.ToString(Set[Key], CultureInfo.InvariantCulture);
        }

        private static bool? GetSetBool(IDictionary<string, object> Set, string Key)
        {
            return DomainJsonSerializer.GetNullableBool(Set, Key);
        }

        private static uint? GetSetUInt(IDictionary<string, object> Set, string Key)
        {
            if (Set == null || !Set.ContainsKey(Key) || Set[Key] == null)
                return null;

            uint Result;
            return UInt32.TryParse(Convert.ToString(Set[Key], CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out Result)
                   ? (uint?)Result : null;
        }

        private static int? GetSetInt(IDictionary<string, object> Set, string Key)
        {
            if (Set == null || !Set.ContainsKey(Key) || Set[Key] == null)
                return null;

            int Result;
            return Int32.TryParse(Convert.ToString(Set[Key], CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out Result)
                   ? (int?)Result : null;
        }

        private static List<string> GetSetStringList(IDictionary<string, object> Set, string Key)
        {
            var Result = new List<string>();
            if (Set == null || !Set.ContainsKey(Key) || Set[Key] == null)
                return Result;

            var Text = Set[Key] as string;
            if (Text != null)
            {
                if (!String.IsNullOrEmpty(Text))
                    Result.Add(Text);
                return Result;
            }

            var Items = Set[Key] as System.Collections.IEnumerable;
            if (Items == null)
                return Result;

            foreach (var Item in Items)
                if (Item != null)
                    Result.Add(Convert.ToString(Item, CultureInfo.InvariantCulture));

            return Result;
        }

        private static string DescribeOperation(DomainJsonOperation Operation)
        {
            return "op=" + Operation.Op.ToStringAlways() +
                   " entity=" + Operation.Entity.ToStringAlways() +
                   " id=" + Operation.Id.ToStringAlways() +
                   " techName=" + Operation.TechName.ToStringAlways() +
                   " owner=" + Operation.OwnerTechName.ToStringAlways();
        }

        private static string Describe(IIdentifiableElement Element)
        {
            if (Element == null)
                return "<none>";

            var Unique = Element as UniqueElement;
            return "name='" + Element.Name.ToStringAlways() + "' techName='" + Element.TechName.ToStringAlways() +
                   "' id=" + (Unique == null ? "<none>" : Unique.GlobalId.ToString("D"));
        }

        private static string MatchMethodFor(IIdentifiableElement Existing, DomainJsonElement Source)
        {
            var Unique = Existing as UniqueElement;
            Guid Parsed;
            if (Unique != null && Source != null && !String.IsNullOrWhiteSpace(Source.Id) &&
                Guid.TryParse(Source.Id, out Parsed) && Unique.GlobalId == Parsed)
                return "id";

            if (Source != null && !String.IsNullOrWhiteSpace(Source.TechName) && Existing != null &&
                String.Equals(Existing.TechName, Source.TechName, StringComparison.OrdinalIgnoreCase))
                return "techName";

            return "unknown";
        }

        private static string MatchMethodFor(IIdentifiableElement Existing, string TechName)
        {
            if (Existing != null && !String.IsNullOrWhiteSpace(TechName) &&
                String.Equals(Existing.TechName, TechName, StringComparison.OrdinalIgnoreCase))
                return "techName";

            return String.IsNullOrWhiteSpace(TechName) ? "default" : "unknown";
        }

        private static string MatchMethodForDataType(DataType Existing, string TechName)
        {
            if (Existing != null && !String.IsNullOrWhiteSpace(TechName) &&
                String.Equals(Existing.TechName, TechName, StringComparison.OrdinalIgnoreCase))
                return "techName";

            return String.IsNullOrWhiteSpace(TechName) ? "default" : "unknown";
        }

        private static int Count<T>(ICollection<T> Items)
        {
            return Items == null ? 0 : Items.Count;
        }
    }
}
