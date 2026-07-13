// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Conservative Domain JSON preview/apply merge.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml;

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
        private DomainJsonImporter(Domain TargetDomain, DomainJsonDocument Document, bool IsPreview, DomainJsonImportReport Report = null, bool PreserveSourceIds = false)
        {
            this.TargetDomain = TargetDomain;
            this.Document = Document;
            this.IsPreview = IsPreview;
            this.Report = Report ?? new DomainJsonImportReport();
            this.Resolver = new DomainJsonReferenceResolver(TargetDomain);
            this.PreserveSourceIds = PreserveSourceIds;
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
        private bool PreserveSourceIds { get; set; }
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

        public static DomainJsonImportReport ApplyPreservingIds(Domain TargetDomain, DomainJsonDocument Document, DomainJsonImportReport ExistingReport = null)
        {
            return new DomainJsonImporter(TargetDomain, Document, false, ExistingReport, true).Execute();
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
            var Changed = AssignImportedId(this.TargetDomain, Source, "domain", "active-domain");
            Changed = ApplyFormalFields(this.TargetDomain, Source, "domain", "active-domain") || Changed;
            Changed = ApplyDomainFields(Source) || Changed;
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
                    AssignImportedId(Existing, Source, "externalLanguage", "create");
                    ApplyFormalFields(Existing, Source, "externalLanguage", "create");
                    this.TargetDomain.ExternalLanguages.Add(Existing);
                }
                this.Report.CountCreated("externalLanguage", this.IsPreview);
                TrackPlanned(this.PlannedExternalLanguageTechNames, Source.TechName);
                return;
            }

            var MatchMethod = MatchMethodFor(Existing, Source);
            this.Report.Log("Domain JSON externalLanguage matched by " + MatchMethod + ": " + Describe(Existing));
            var Changed = AssignImportedId(Existing, Source, "externalLanguage", MatchMethod);
            Changed = ApplyFormalFields(Existing, Source, "externalLanguage", MatchMethod) || Changed;
            if (Changed)
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
                    AssignImportedId(Existing, Source, "tableDefinitionCategory", "create");
                    ApplyFormalFields(Existing, Source);
                    this.TargetDomain.TableDefCategories.Add(Existing);
                }
                this.Report.CountCreated("tableDefinitionCategory", this.IsPreview);
                return;
            }

            var Changed = AssignImportedId(Existing, Source, "tableDefinitionCategory", MatchMethodFor(Existing, Source));
            Changed = ApplyFormalFields(Existing, Source) || Changed;
            if (Changed)
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
                    AssignImportedId(Existing, Source, "fieldDefinitionCategory", "create");
                    ApplyFormalFields(Existing, Source);
                    this.TargetDomain.FieldDefCategories.Add(Existing);
                }
                this.Report.CountCreated("fieldDefinitionCategory", this.IsPreview);
                return;
            }

            var Changed = AssignImportedId(Existing, Source, "fieldDefinitionCategory", MatchMethodFor(Existing, Source));
            Changed = ApplyFormalFields(Existing, Source) || Changed;
            if (Changed)
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
                    AssignImportedId(Existing, Source, "tableDefinition", "create");
                    ApplyFormalFields(Existing, Source);
                    this.TargetDomain.TableDefinitions.Add(Existing);
                }
                this.Report.CountCreated("tableDefinition", this.IsPreview);
                TrackPlanned(this.PlannedTableDefinitionTechNames, Source.TechName);
            }
            else
            {
                var Changed = AssignImportedId(Existing, Source, "tableDefinition", MatchMethodFor(Existing, Source));
                Changed = ApplyFormalFields(Existing, Source) || Changed;
                if (Changed)
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
                    AssignImportedId(Existing, Source, "fieldDefinition", "create");
                    if (Source.Order != null)
                        Existing.StorageIndex = Source.Order.Value;
                    ApplyFormalFields(Existing, Source);
                    ApplyFieldFlags(Existing, Source, false);
                    Owner.FieldDefinitions.Add(Existing);
                }
                this.Report.CountCreated("fieldDefinition", this.IsPreview);
                return;
            }

            var SourceFieldType = String.IsNullOrWhiteSpace(Source.DataTypeTechName)
                                  ? null
                                  : this.Resolver.FindDataType(Source.DataTypeTechName);
            if (!String.IsNullOrWhiteSpace(Source.DataTypeTechName) && Existing.FieldType != null &&
                SourceFieldType == null)
            {
                Skip("fieldDefinition", "Skipped field '" + Existing.TechName + "' because dataType '" + Source.DataTypeTechName +
                                        "' was not resolved. Valid dataType techNames: " + KnownDataTypeTechNames());
                return;
            }

            var Changed = false;
            if (!String.IsNullOrWhiteSpace(Source.DataTypeTechName) && Existing.FieldType != null &&
                !String.Equals(Existing.FieldType.TechName, Source.DataTypeTechName, StringComparison.OrdinalIgnoreCase))
            {
                if (!this.PreserveSourceIds)
                {
                    this.Report.DangerousChangesSkipped++;
                    Skip("fieldDefinition", "Skipped incompatible field data type change for '" + Existing.TechName + "' from '" +
                                            Existing.FieldType.TechName + "' to '" + Source.DataTypeTechName + "'.");
                    return;
                }

                this.Report.LogFieldUpdate("fieldDefinition", "dataTypeTechName", Describe(Existing), MatchMethodFor(Existing, Source),
                                           Existing.FieldType.TechName, SourceFieldType.TechName, this.IsPreview);
                if (!this.IsPreview)
                    Existing.FieldType = SourceFieldType;
                Changed = true;
            }

            Changed = AssignImportedId(Existing, Source, "fieldDefinition", MatchMethodFor(Existing, Source)) || Changed;
            Changed = ApplyFormalFields(Existing, Source) || Changed;
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
                    var Ancestor = ResolveConceptAncestor(Source, true);
                    Existing = new ConceptDefinition(this.TargetDomain, Ancestor, Source.Name, Source.TechName,
                                                     Source.RepresentativeShape.NullDefault(Shapes.Rectangle),
                                                     Source.Summary.NullDefault(""));
                    AssignImportedId(Existing, Source, "conceptDefinition", "create");
                    ApplyIdeaDefinitionFields(Existing, Source);
                    this.TargetDomain.ConceptDefinitions.Add(Existing);
                }
                this.Report.CountCreated("conceptDefinition", this.IsPreview);
                TrackPlanned(this.PlannedConceptDefinitionTechNames, Source.TechName);
            }
            else
            {
                var Changed = AssignImportedId(Existing, Source, "conceptDefinition", MatchMethodFor(Existing, Source));
                Changed = ApplyIdeaDefinitionFields(Existing, Source) || Changed;
                if (Changed)
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
                    var Ancestor = ResolveRelationshipAncestor(Source, true);
                    Existing = new RelationshipDefinition(this.TargetDomain, Ancestor, Source.Name, Source.TechName,
                                                          Source.RepresentativeShape.NullDefault(Shapes.Ellipse),
                                                          Source.Summary.NullDefault(""), null, OriginRole, TargetRole);
                    AssignImportedId(Existing, Source, "relationshipDefinition", "create");
                    ApplyRelationshipDefinitionFields(Existing, Source);
                    this.TargetDomain.RelationshipDefinitions.Add(Existing);
                }
                this.Report.CountCreated("relationshipDefinition", this.IsPreview);
                TrackPlanned(this.PlannedRelationshipDefinitionTechNames, Source.TechName);
            }
            else
            {
                var Changed = AssignImportedId(Existing, Source, "relationshipDefinition", MatchMethodFor(Existing, Source));
                Changed = ApplyRelationshipDefinitionFields(Existing, Source) || Changed;
                if (Changed)
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
                    AssignImportedId(Existing, Source, "relationshipRole", "create");
                    ApplyRelationshipRoleFields(Existing, Source);
                    if (RoleType == ERoleType.Target)
                        Owner.TargetLinkRoleDef = Existing;
                    else
                        Owner.OriginOrParticipantLinkRoleDef = Existing;
                }
                this.Report.CountCreated("relationshipRole", this.IsPreview);
                return;
            }

            var RoleChanged = AssignImportedId(Existing, Source, "relationshipRole", MatchMethodFor(Existing, Source));
            RoleChanged = ApplyRelationshipRoleFields(Existing, Source) || RoleChanged;
            if (RoleChanged)
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
                if (Owner == null)
                    return null;

                Owner.DeclareOutputTemplatesCollection();
                return Owner.OutputTemplates;
            }

            if (String.Equals(OwnerScope, "relationshipDefinition", StringComparison.OrdinalIgnoreCase))
            {
                var Owner = this.Resolver.RelationshipDefinition(null, OwnerTechName);
                if (Owner == null)
                    return null;

                Owner.DeclareOutputTemplatesCollection();
                return Owner.OutputTemplates;
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

            Changed = ApplySymbolFormat(Target.DefaultSymbolFormat, GetSetDictionary(Source.Set, "visualSymbolFormat"), Source.Entity.NullDefault("ideaDefinition"), Target.TechName) || Changed;

            var ConceptTarget = Target as ConceptDefinition;
            if (ConceptTarget != null)
                Changed = ApplyConceptDefinitionFields(ConceptTarget, Source, Changed);

            return Changed;
        }

        private bool ApplyConceptDefinitionFields(ConceptDefinition Target, DomainJsonElement Source, bool Changed)
        {
            if (this.PreserveSourceIds || !String.IsNullOrWhiteSpace(Source.AncestorTechName))
            {
                var Ancestor = ResolveConceptAncestor(Source, false);
                if (Target.AncestorConceptDef != Ancestor)
                {
                    this.Report.LogFieldUpdate(Source.Entity.NullDefault("conceptDefinition"), "ancestorTechName",
                                               Describe(Target), MatchMethodFor(Target, Source),
                                               TechNameOf(Target.AncestorConceptDef), TechNameOf(Ancestor), this.IsPreview);
                    if (!this.IsPreview)
                        Target.AncestorConceptDef = Ancestor;
                    Changed = true;
                }
            }

            if (HasSetKey(Source.Set, "automaticCreationConceptDefinitionTechName"))
            {
                var TechName = GetSetString(Source.Set, "automaticCreationConceptDefinitionTechName");
                var ConceptDef = String.IsNullOrWhiteSpace(TechName)
                                 ? null
                                 : this.Resolver.ConceptDefinition(null, TechName);
                if (ConceptDef == null && !String.IsNullOrWhiteSpace(TechName) &&
                    String.Equals(Target.TechName, TechName, StringComparison.OrdinalIgnoreCase))
                    ConceptDef = Target;

                if (ConceptDef == null && !String.IsNullOrWhiteSpace(TechName))
                {
                    if (this.IsPreview && this.PlannedConceptDefinitionTechNames.Contains(TechName))
                        Changed = true;
                    else
                        Skip(Source.Entity.NullDefault("conceptDefinition"), "Automatic creation concept definition '" +
                                                                   TechName + "' was not found for definition '" + Target.TechName + "'.");
                }
                else if (Target.AutomaticCreationConceptDef != ConceptDef)
                {
                    this.Report.LogFieldUpdate(Source.Entity.NullDefault("conceptDefinition"), "automaticCreationConceptDefinitionTechName",
                                               Describe(Target), MatchMethodFor(Target, Source),
                                               TechNameOf(Target.AutomaticCreationConceptDef), TechNameOf(ConceptDef), this.IsPreview);
                    if (!this.IsPreview)
                        Target.AutomaticCreationConceptDef = ConceptDef;
                    Changed = true;
                }
            }

            if (HasSetKey(Source.Set, "automaticCreationRelationshipDefinitionTechName"))
            {
                var TechName = GetSetString(Source.Set, "automaticCreationRelationshipDefinitionTechName");
                var RelationshipDef = String.IsNullOrWhiteSpace(TechName)
                                      ? null
                                      : this.Resolver.RelationshipDefinition(null, TechName);

                if (RelationshipDef == null && !String.IsNullOrWhiteSpace(TechName))
                {
                    if (this.IsPreview && this.PlannedRelationshipDefinitionTechNames.Contains(TechName))
                        Changed = true;
                    else
                        Skip(Source.Entity.NullDefault("conceptDefinition"), "Automatic creation relationship definition '" +
                                                                   TechName + "' was not found for definition '" + Target.TechName + "'.");
                }
                else if (Target.AutomaticCreationRelationshipDef != RelationshipDef)
                {
                    this.Report.LogFieldUpdate(Source.Entity.NullDefault("conceptDefinition"), "automaticCreationRelationshipDefinitionTechName",
                                               Describe(Target), MatchMethodFor(Target, Source),
                                               TechNameOf(Target.AutomaticCreationRelationshipDef), TechNameOf(RelationshipDef), this.IsPreview);
                    if (!this.IsPreview)
                        Target.AutomaticCreationRelationshipDef = RelationshipDef;
                    Changed = true;
                }
            }

            var PositioningModeText = GetSetString(Source.Set, "automaticCreationPositioningMode");
            if (PositioningModeText != null)
            {
                EAutoPositioningMode PositioningMode;
                if (Enum.TryParse<EAutoPositioningMode>(PositioningModeText, out PositioningMode) &&
                    Target.AutomaticCreationPositioningMode != PositioningMode)
                {
                    this.Report.LogFieldUpdate(Source.Entity.NullDefault("conceptDefinition"), "automaticCreationPositioningMode",
                                               Describe(Target), MatchMethodFor(Target, Source),
                                               Target.AutomaticCreationPositioningMode, PositioningMode, this.IsPreview);
                    if (!this.IsPreview)
                        Target.AutomaticCreationPositioningMode = PositioningMode;
                    Changed = true;
                }
            }

            var IsRadialized = GetSetBool(Source.Set, "automaticCreationPositioningIsRadialized");
            if (IsRadialized != null && Target.AutomaticCreationPositioningIsRadialized != IsRadialized.Value)
            {
                this.Report.LogFieldUpdate(Source.Entity.NullDefault("conceptDefinition"), "automaticCreationPositioningIsRadialized",
                                           Describe(Target), MatchMethodFor(Target, Source),
                                           Target.AutomaticCreationPositioningIsRadialized, IsRadialized.Value, this.IsPreview);
                if (!this.IsPreview)
                    Target.AutomaticCreationPositioningIsRadialized = IsRadialized.Value;
                Changed = true;
            }

            return Changed;
        }

        private bool ApplyRelationshipDefinitionFields(RelationshipDefinition Target, DomainJsonElement Source)
        {
            var Changed = ApplyIdeaDefinitionFields(Target, Source);

            if (this.PreserveSourceIds || !String.IsNullOrWhiteSpace(Source.AncestorTechName))
            {
                var Ancestor = ResolveRelationshipAncestor(Source, false);
                if (Target.AncestorRelationshipDef != Ancestor)
                {
                    this.Report.LogFieldUpdate(Source.Entity.NullDefault("relationshipDefinition"), "ancestorTechName",
                                               Describe(Target), MatchMethodFor(Target, Source),
                                               TechNameOf(Target.AncestorRelationshipDef), TechNameOf(Ancestor), this.IsPreview);
                    if (!this.IsPreview)
                        Target.AncestorRelationshipDef = Ancestor;
                    Changed = true;
                }
            }

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

            Changed = ApplyConnectorsFormat(Target.DefaultConnectorsFormat, GetSetDictionary(Source.Set, "visualConnectorsFormat"), Source.Entity.NullDefault("relationshipDefinition"), Target.TechName) || Changed;

            return Changed;
        }

        private bool ApplySymbolFormat(VisualSymbolFormat Target, IDictionary<string, object> Source, string Entity, string OwnerTechName)
        {
            if (Target == null || Source == null || Source.Count < 1)
                return false;

            if (this.IsPreview)
                return true;

            ApplyElementFormat(Target, Source);
            ApplyDouble(Source, "initialWidth", delegate(double Value) { Target.InitialWidth = Value; });
            ApplyDouble(Source, "initialHeight", delegate(double Value) { Target.InitialHeight = Value; });
            ApplyBool(Source, "hasFixedWidth", delegate(bool Value) { Target.HasFixedWidth = Value; });
            ApplyBool(Source, "hasFixedHeight", delegate(bool Value) { Target.HasFixedHeight = Value; });
            ApplyBool(Source, "useNameAsMainTitle", delegate(bool Value) { Target.UseNameAsMainTitle = Value; });
            ApplyEnum<EVisualDispositionMonodimensional>(Source, "subtitleVisualDisposition", delegate(EVisualDispositionMonodimensional Value) { Target.SubtitleVisualDisposition = Value; });
            ApplyEnum<EVisualDispositionBidimensional>(Source, "pictogramVisualDisposition", delegate(EVisualDispositionBidimensional Value) { Target.PictogramVisualDisposition = Value; });
            ApplyBool(Source, "useDefinitorPictogramAsNullDefault", delegate(bool Value) { Target.UseDefinitorPictogramAsNullDefault = Value; });
            ApplyBool(Source, "usePictogramAsSymbol", delegate(bool Value) { Target.UsePictogramAsSymbol = Value; });
            ApplyBool(Source, "detailsPosterIsHanging", delegate(bool Value) { Target.DetailsPosterIsHanging = Value; });
            ApplyBool(Source, "includeDetailsSeparators", delegate(bool Value) { Target.IncludeDetailsSeparators = Value; });
            ApplyBool(Source, "initiallyFlippedHorizontally", delegate(bool Value) { Target.InitiallyFlippedHorizontally = Value; });
            ApplyBool(Source, "initiallyFlippedVertically", delegate(bool Value) { Target.InitiallyFlippedVertically = Value; });
            ApplyBool(Source, "initiallyTilted", delegate(bool Value) { Target.InitiallyTilted = Value; });
            ApplyBool(Source, "asMultiple", delegate(bool Value) { Target.AsMultiple = Value; });
            ApplyTextFormats(Target, GetSetDictionary(Source, "textFormats"));
            ApplyBrush(Source, "regionBackground", delegate(Brush Value) { Target.RegionBackground = Value; });
            ApplyBrush(Source, "regionForeground", delegate(Brush Value) { Target.RegionForeground = Value; });
            ApplyDash(Source, "regionDash", delegate(DashStyle Value) { Target.RegionDash = Value; });
            ApplyDouble(Source, "regionThickness", delegate(double Value) { Target.RegionThickness = Value; });
            ApplyEnum<EPlacementOnBorderHorizontal>(Source, "initialGroupRegionPlacementHorizontal", delegate(EPlacementOnBorderHorizontal Value) { Target.InitialGroupRegionPlacementHorizontal = Value; });
            this.Report.Log("Domain JSON applied visualSymbolFormat for " + Entity + " '" + OwnerTechName.ToStringAlways() + "'.");
            return true;
        }

        private bool ApplyConnectorsFormat(VisualConnectorsFormat Target, IDictionary<string, object> Source, string Entity, string OwnerTechName)
        {
            if (Target == null || Source == null || Source.Count < 1)
                return false;

            if (this.IsPreview)
                return true;

            ApplyElementFormat(Target, Source);
            ApplyEnum<EPathStyle>(Source, "pathStyle", delegate(EPathStyle Value) { Target.PathStyle = Value; });
            ApplyEnum<EPathCorner>(Source, "pathCorner", delegate(EPathCorner Value) { Target.PathCorner = Value; });
            ApplyBool(Source, "labelLinkVariant", delegate(bool Value) { Target.LabelLinkVariant = Value; });
            ApplyBool(Source, "labelLinkDefinitor", delegate(bool Value) { Target.LabelLinkDefinitor = Value; });
            ApplyBool(Source, "labelLinkDescriptor", delegate(bool Value) { Target.LabelLinkDescriptor = Value; });
            ApplyPlugMap(Target.HeadPlugs, GetSetDictionary(Source, "headPlugs"));
            ApplyPlugMap(Target.TailPlugs, GetSetDictionary(Source, "tailPlugs"));
            this.Report.Log("Domain JSON applied visualConnectorsFormat for " + Entity + " '" + OwnerTechName.ToStringAlways() + "'.");
            return true;
        }

        private void ApplyElementFormat(VisualElementFormat Target, IDictionary<string, object> Source)
        {
            ApplyBrush(Source, "mainBackground", delegate(Brush Value) { Target.MainBackground = Value; });
            ApplyBrush(Source, "lineBrush", delegate(Brush Value) { Target.LineBrush = Value; });
            ApplyDash(Source, "lineDash", delegate(DashStyle Value) { Target.LineDash = Value; });
            ApplyEnum<PenLineCap>(Source, "lineCap", delegate(PenLineCap Value) { Target.LineCap = Value; });
            ApplyEnum<PenLineJoin>(Source, "lineJoin", delegate(PenLineJoin Value) { Target.LineJoin = Value; });
            ApplyDouble(Source, "lineThickness", delegate(double Value) { Target.LineThickness = Value; });
            ApplyDouble(Source, "opacity", delegate(double Value) { Target.Opacity = Value; });
        }

        private static void ApplyTextFormats(VisualSymbolFormat Target, IDictionary<string, object> Source)
        {
            if (Target == null || Source == null)
                return;

            foreach (var Pair in Source)
            {
                ETextPurpose Purpose;
                if (!TryParseTextPurpose(Pair.Key, out Purpose))
                    continue;

                var Format = ImportTextFormat(Pair.Value);
                if (Format != null)
                    Target.SetTextFormat(Purpose, Format);
            }
        }

        private static bool TryParseTextPurpose(string Text, out ETextPurpose Purpose)
        {
            if (Enum.TryParse<ETextPurpose>(Text, true, out Purpose))
                return true;

            var Normalized = (Text ?? "").Replace(" ", "").Replace("-", "").Replace("_", "");
            foreach (ETextPurpose Candidate in Enum.GetValues(typeof(ETextPurpose)))
                if (String.Equals(Candidate.ToString(), Normalized, StringComparison.OrdinalIgnoreCase))
                {
                    Purpose = Candidate;
                    return true;
                }

            return false;
        }

        private void ApplyPlugMap(IDictionary<SimplePresentationElement, string> Target, IDictionary<string, object> Source)
        {
            if (Target == null || Source == null)
                return;

            foreach (var Pair in Source)
            {
                var Variant = this.Resolver.LinkRoleVariant(Pair.Key);
                var Plug = Pair.Value == null ? null : Convert.ToString(Pair.Value, CultureInfo.InvariantCulture);
                if (Variant != null && !String.IsNullOrWhiteSpace(Plug))
                    Target.AddOrReplace(Variant, Plug);
            }
        }

        private static void ApplyBrush(IDictionary<string, object> Source, string Key, Action<Brush> Setter)
        {
            if (Source == null || !Source.ContainsKey(Key))
                return;

            var Value = GetSetObject(Source, Key);
            if (Value == null)
            {
                Setter(null);
                return;
            }

            var Brush = ImportBrush(Value);
            if (Brush != null)
                Setter(Brush);
        }

        private static void ApplyDash(IDictionary<string, object> Source, string Key, Action<DashStyle> Setter)
        {
            var Dash = ImportDashStyle(GetSetObject(Source, Key));
            if (Dash != null)
                Setter(Dash);
        }

        private static void ApplyBool(IDictionary<string, object> Source, string Key, Action<bool> Setter)
        {
            var Value = GetSetBool(Source, Key);
            if (Value != null)
                Setter(Value.Value);
        }

        private static void ApplyDouble(IDictionary<string, object> Source, string Key, Action<double> Setter)
        {
            var Value = GetSetDouble(Source, Key);
            if (Value != null)
                Setter(Value.Value);
        }

        private static void ApplyEnum<TEnum>(IDictionary<string, object> Source, string Key, Action<TEnum> Setter)
            where TEnum : struct
        {
            var Text = GetSetString(Source, Key);
            TEnum Value;
            if (!String.IsNullOrWhiteSpace(Text) && Enum.TryParse<TEnum>(Text, true, out Value))
                Setter(Value);
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

        private bool ApplyDomainFields(DomainJsonElement Source)
        {
            var Changed = false;

            if (!String.IsNullOrWhiteSpace(Source.RepresentativeShape) &&
                this.TargetDomain.RepresentativeShape != Source.RepresentativeShape)
            {
                this.Report.LogFieldUpdate("domain", "representativeShape", Describe(this.TargetDomain), "active-domain",
                                           this.TargetDomain.RepresentativeShape, Source.RepresentativeShape, this.IsPreview);
                if (!this.IsPreview)
                    this.TargetDomain.RepresentativeShape = Source.RepresentativeShape;
                Changed = true;
            }

            if (Source.IsComposable != null && this.TargetDomain.IsComposable != Source.IsComposable.Value)
            {
                this.Report.LogFieldUpdate("domain", "isComposable", Describe(this.TargetDomain), "active-domain",
                                           this.TargetDomain.IsComposable.ToString(),
                                           Source.IsComposable.Value.ToString(), this.IsPreview);
                if (!this.IsPreview)
                    this.TargetDomain.IsComposable = Source.IsComposable.Value;
                Changed = true;
            }

            if (Source.IsVersionable != null && this.TargetDomain.IsVersionable != Source.IsVersionable.Value)
            {
                this.Report.LogFieldUpdate("domain", "isVersionable", Describe(this.TargetDomain), "active-domain",
                                           this.TargetDomain.IsVersionable.ToString(),
                                           Source.IsVersionable.Value.ToString(), this.IsPreview);
                if (!this.IsPreview)
                    this.TargetDomain.IsVersionable = Source.IsVersionable.Value;
                Changed = true;
            }

            var ModelRevision = GetSetInt(Source.Set, "modelRevision");
            if (ModelRevision != null && this.TargetDomain.ModelRevision != ModelRevision.Value)
            {
                this.Report.LogFieldUpdate("domain", "modelRevision", Describe(this.TargetDomain), "active-domain",
                                           this.TargetDomain.ModelRevision.ToString(CultureInfo.InvariantCulture),
                                           ModelRevision.Value.ToString(CultureInfo.InvariantCulture), this.IsPreview);
                if (!this.IsPreview)
                    this.TargetDomain.ModelRevision = ModelRevision.Value;
                Changed = true;
            }

            var ViewGridSize = GetSetDouble(Source.Set, "viewGridSize");
            if (ViewGridSize != null && this.TargetDomain.ViewGridSize != ViewGridSize.Value)
            {
                this.Report.LogFieldUpdate("domain", "viewGridSize", Describe(this.TargetDomain), "active-domain",
                                           this.TargetDomain.ViewGridSize.ToString(CultureInfo.InvariantCulture),
                                           ViewGridSize.Value.ToString(CultureInfo.InvariantCulture), this.IsPreview);
                if (!this.IsPreview)
                    this.TargetDomain.ViewGridSize = ViewGridSize.Value;
                Changed = true;
            }

            Changed = ApplyReportConfiguration(GetSetDictionary(Source.Set, "reportingConfiguration")) || Changed;

            return Changed;
        }

        private bool ApplyReportConfiguration(IDictionary<string, object> Source)
        {
            if (Source == null || Source.Count < 1)
                return false;

            var Target = this.TargetDomain.ReportingConfiguration ?? new ReportConfiguration();
            var Changed = false;

            Changed = ApplyReportString(Source, "documentTitle", Target.Document_Title, this.IsPreview, delegate(string Value) { Target.Document_Title = Value; }) || Changed;
            Changed = ApplyReportString(Source, "documentSubtitle", Target.Document_Subtitle, this.IsPreview, delegate(string Value) { Target.Document_Subtitle = Value; }) || Changed;
            Changed = ApplyReportString(Source, "pageHeaderLeft", Target.PageHeader_Left, this.IsPreview, delegate(string Value) { Target.PageHeader_Left = Value; }) || Changed;
            Changed = ApplyReportString(Source, "pageHeaderCenter", Target.PageHeader_Center, this.IsPreview, delegate(string Value) { Target.PageHeader_Center = Value; }) || Changed;
            Changed = ApplyReportString(Source, "pageHeaderRight", Target.PageHeader_Right, this.IsPreview, delegate(string Value) { Target.PageHeader_Right = Value; }) || Changed;
            Changed = ApplyReportString(Source, "pageFooterLeft", Target.PageFooter_Left, this.IsPreview, delegate(string Value) { Target.PageFooter_Left = Value; }) || Changed;
            Changed = ApplyReportString(Source, "pageFooterCenter", Target.PageFooter_Center, this.IsPreview, delegate(string Value) { Target.PageFooter_Center = Value; }) || Changed;
            Changed = ApplyReportString(Source, "pageFooterRight", Target.PageFooter_Right, this.IsPreview, delegate(string Value) { Target.PageFooter_Right = Value; }) || Changed;

            Changed = ApplyReportBool(Source, "docSectionTitlePage", Target.DocSection_TitlePage, this.IsPreview, delegate(bool Value) { Target.DocSection_TitlePage = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "docSectionTableOfContents", Target.DocSection_TableOfContents, this.IsPreview, delegate(bool Value) { Target.DocSection_TableOfContents = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "docSectionComposition", Target.DocSection_Composition, this.IsPreview, delegate(bool Value) { Target.DocSection_Composition = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "docSectionDomain", Target.DocSection_Domain, this.IsPreview, delegate(bool Value) { Target.DocSection_Domain = Value; }) || Changed;
            Changed = ApplyReportDisplayCard(GetSetDictionary(Source, "compositionCard"), Target.Composition_Card, this.IsPreview, delegate(DisplayCard Value) { Target.Composition_Card = Value; }) || Changed;
            Changed = ApplyReportDisplayCard(GetSetDictionary(Source, "compositeIdeaViewCard"), Target.CompositeIdea_View_Card, this.IsPreview, delegate(DisplayCard Value) { Target.CompositeIdea_View_Card = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "compositeIdeaViewDiagram", Target.CompositeIdea_View_Diagram, this.IsPreview, delegate(bool Value) { Target.CompositeIdea_View_Diagram = Value; }) || Changed;
            Changed = ApplyReportDisplayList(GetSetDictionary(Source, "compositeIdeaConceptsList"), Target.CompositeIdea_Concepts_List, this.IsPreview, delegate(DisplayList Value) { Target.CompositeIdea_Concepts_List = Value; }) || Changed;
            Changed = ApplyReportDisplayCard(GetSetDictionary(Source, "compositeIdeaConceptsCard"), Target.CompositeIdea_Concepts_Card, this.IsPreview, delegate(DisplayCard Value) { Target.CompositeIdea_Concepts_Card = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "compositeIdeaConceptsReportCompositeContent", Target.CompositeIdea_Concepts_ReportCompositeContent, this.IsPreview, delegate(bool Value) { Target.CompositeIdea_Concepts_ReportCompositeContent = Value; }) || Changed;
            Changed = ApplyReportDisplayList(GetSetDictionary(Source, "compositeIdeaRelationshipsList"), Target.CompositeIdea_Relationships_List, this.IsPreview, delegate(DisplayList Value) { Target.CompositeIdea_Relationships_List = Value; }) || Changed;
            Changed = ApplyReportDisplayCard(GetSetDictionary(Source, "compositeIdeaRelationshipsCard"), Target.CompositeIdea_Relationships_Card, this.IsPreview, delegate(DisplayCard Value) { Target.CompositeIdea_Relationships_Card = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "compositeIdeaRelationshipsReportCompositeContent", Target.CompositeIdea_Relationships_ReportCompositeContent, this.IsPreview, delegate(bool Value) { Target.CompositeIdea_Relationships_ReportCompositeContent = Value; }) || Changed;
            Changed = ApplyReportDisplayList(GetSetDictionary(Source, "compositeIdeaMarkersList"), Target.CompositeIdea_Markers_List, this.IsPreview, delegate(DisplayList Value) { Target.CompositeIdea_Markers_List = Value; }) || Changed;
            Changed = ApplyReportDisplayCard(GetSetDictionary(Source, "compositeIdeaMarkersCard"), Target.CompositeIdea_Markers_Card, this.IsPreview, delegate(DisplayCard Value) { Target.CompositeIdea_Markers_Card = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "compositeIdeaComplements", Target.CompositeIdea_Complements, this.IsPreview, delegate(bool Value) { Target.CompositeIdea_Complements = Value; }) || Changed;
            Changed = ApplyReportDisplayList(GetSetDictionary(Source, "compositeIdeaGroupedIdeasList"), Target.CompositeIdea_GroupedIdeas_List, this.IsPreview, delegate(DisplayList Value) { Target.CompositeIdea_GroupedIdeas_List = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "compositeIdeaDetails", Target.CompositeIdea_Details, this.IsPreview, delegate(bool Value) { Target.CompositeIdea_Details = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "compositeIdeaDetailsIncludeLinksTarget", Target.CompositeIdea_DetailsIncludeLinksTarget, this.IsPreview, delegate(bool Value) { Target.CompositeIdea_DetailsIncludeLinksTarget = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "compositeIdeaDetailsIncludeAttachmentsContent", Target.CompositeIdea_DetailsIncludeAttachmentsContent, this.IsPreview, delegate(bool Value) { Target.CompositeIdea_DetailsIncludeAttachmentsContent = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "compositeIdeaDetailsIncludeTablesData", Target.CompositeIdea_DetailsIncludeTablesData, this.IsPreview, delegate(bool Value) { Target.CompositeIdea_DetailsIncludeTablesData = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "compositeIdeaRelatedFromCollection", Target.CompositeIdea_RelatedFrom_Collection, this.IsPreview, delegate(bool Value) { Target.CompositeIdea_RelatedFrom_Collection = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "compositeIdeaIncludeTargetCompanions", Target.CompositeIdea_IncludeTargetCompanions, this.IsPreview, delegate(bool Value) { Target.CompositeIdea_IncludeTargetCompanions = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "compositeIdeaRelatedToCollection", Target.CompositeIdea_RelatedTo_Collection, this.IsPreview, delegate(bool Value) { Target.CompositeIdea_RelatedTo_Collection = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "compositeIdeaIncludeOriginCompanions", Target.CompositeIdea_IncludeOriginCompanions, this.IsPreview, delegate(bool Value) { Target.CompositeIdea_IncludeOriginCompanions = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "compositeRelationshipLinksCollection", Target.CompositeRelationship_Links_Collection, this.IsPreview, delegate(bool Value) { Target.CompositeRelationship_Links_Collection = Value; }) || Changed;
            Changed = ApplyReportDisplayCard(GetSetDictionary(Source, "compositeRelationshipLinksCard"), Target.CompositeRelationship_Links_Card, this.IsPreview, delegate(DisplayCard Value) { Target.CompositeRelationship_Links_Card = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "domainConceptDefs", Target.Domain_Concept_Defs, this.IsPreview, delegate(bool Value) { Target.Domain_Concept_Defs = Value; }) || Changed;
            Changed = ApplyReportDisplayList(GetSetDictionary(Source, "domainConceptDefsList"), Target.Domain_Concept_Defs_List, this.IsPreview, delegate(DisplayList Value) { Target.Domain_Concept_Defs_List = Value; }) || Changed;
            Changed = ApplyReportDisplayCard(GetSetDictionary(Source, "domainConceptDefsCard"), Target.Domain_Concept_Defs_Card, this.IsPreview, delegate(DisplayCard Value) { Target.Domain_Concept_Defs_Card = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "domainRelationshipDefs", Target.Domain_Relationship_Defs, this.IsPreview, delegate(bool Value) { Target.Domain_Relationship_Defs = Value; }) || Changed;
            Changed = ApplyReportDisplayList(GetSetDictionary(Source, "domainRelationshipDefsList"), Target.Domain_Relationship_Defs_List, this.IsPreview, delegate(DisplayList Value) { Target.Domain_Relationship_Defs_List = Value; }) || Changed;
            Changed = ApplyReportDisplayCard(GetSetDictionary(Source, "domainRelationshipDefsCard"), Target.Domain_Relationship_Defs_Card, this.IsPreview, delegate(DisplayCard Value) { Target.Domain_Relationship_Defs_Card = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "domainLinkRoleVariants", Target.Domain_LinkRole_Variants, this.IsPreview, delegate(bool Value) { Target.Domain_LinkRole_Variants = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "domainMarkerDefs", Target.Domain_Marker_Defs, this.IsPreview, delegate(bool Value) { Target.Domain_Marker_Defs = Value; }) || Changed;
            Changed = ApplyReportDisplayList(GetSetDictionary(Source, "domainMarkerDefsList"), Target.Domain_Marker_Defs_List, this.IsPreview, delegate(DisplayList Value) { Target.Domain_Marker_Defs_List = Value; }) || Changed;
            Changed = ApplyReportDisplayCard(GetSetDictionary(Source, "domainMarkerDefsCard"), Target.Domain_Marker_Defs_Card, this.IsPreview, delegate(DisplayCard Value) { Target.Domain_Marker_Defs_Card = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "domainTableStructDefs", Target.Domain_TableStruct_Defs, this.IsPreview, delegate(bool Value) { Target.Domain_TableStruct_Defs = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "domainBaseTables", Target.Domain_BaseTables, this.IsPreview, delegate(bool Value) { Target.Domain_BaseTables = Value; }) || Changed;

            if (!this.IsPreview && this.TargetDomain.ReportingConfiguration == null)
                this.TargetDomain.ReportingConfiguration = Target;

            if (Changed)
                this.Report.Log("Domain JSON " + (this.IsPreview ? "planned" : "applied") + " reportingConfiguration structural settings.");

            return Changed;
        }

        private static bool ApplyReportString(IDictionary<string, object> Source, string Key, string Current, bool IsPreview, Action<string> Setter)
        {
            var Value = GetSetString(Source, Key);
            if (Value == null || Current == Value)
                return false;

            if (!IsPreview)
                Setter(Value);
            return true;
        }

        private static bool ApplyReportBool(IDictionary<string, object> Source, string Key, bool Current, bool IsPreview, Action<bool> Setter)
        {
            var Value = GetSetBool(Source, Key);
            if (Value == null || Current == Value.Value)
                return false;

            if (!IsPreview)
                Setter(Value.Value);
            return true;
        }

        private static bool ApplyReportDisplayList(IDictionary<string, object> Source, DisplayList Target, bool IsPreview, Action<DisplayList> Setter)
        {
            if (Source == null || Source.Count < 1)
                return false;

            var Changed = false;
            if (Target == null)
            {
                Target = new DisplayList();
                if (!IsPreview)
                    Setter(Target);
                Changed = true;
            }

            Changed = ApplyReportBool(Source, "show", Target.Show, IsPreview, delegate(bool Value) { Target.Show = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "propName", Target.PropName, IsPreview, delegate(bool Value) { Target.PropName = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "propTechName", Target.PropTechName, IsPreview, delegate(bool Value) { Target.PropTechName = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "propSummary", Target.PropSummary, IsPreview, delegate(bool Value) { Target.PropSummary = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "propPictogram", Target.PropPictogram, IsPreview, delegate(bool Value) { Target.PropPictogram = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "definitor", Target.Definitor, IsPreview, delegate(bool Value) { Target.Definitor = Value; }) || Changed;
            return Changed;
        }

        private static bool ApplyReportDisplayCard(IDictionary<string, object> Source, DisplayCard Target, bool IsPreview, Action<DisplayCard> Setter)
        {
            if (Source == null || Source.Count < 1)
                return false;

            var Changed = false;
            if (Target == null)
            {
                Target = new DisplayCard();
                if (!IsPreview)
                    Setter(Target);
                Changed = true;
            }

            Changed = ApplyReportBool(Source, "show", Target.Show, IsPreview, delegate(bool Value) { Target.Show = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "route", Target.Route, IsPreview, delegate(bool Value) { Target.Route = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "definitor", Target.Definitor, IsPreview, delegate(bool Value) { Target.Definitor = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "propGlobalId", Target.PropGlobalId, IsPreview, delegate(bool Value) { Target.PropGlobalId = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "propName", Target.PropName, IsPreview, delegate(bool Value) { Target.PropName = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "propTechName", Target.PropTechName, IsPreview, delegate(bool Value) { Target.PropTechName = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "propSummary", Target.PropSummary, IsPreview, delegate(bool Value) { Target.PropSummary = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "propTechSpec", Target.PropTechSpec, IsPreview, delegate(bool Value) { Target.PropTechSpec = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "propPictogram", Target.PropPictogram, IsPreview, delegate(bool Value) { Target.PropPictogram = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "propDescription", Target.PropDescription, IsPreview, delegate(bool Value) { Target.PropDescription = Value; }) || Changed;
            Changed = ApplyReportBool(Source, "propVersioning", Target.PropVersioning, IsPreview, delegate(bool Value) { Target.PropVersioning = Value; }) || Changed;
            return Changed;
        }

        private ConceptDefinition ResolveConceptAncestor(DomainJsonElement Source, bool DefaultToBase)
        {
            if (!String.IsNullOrWhiteSpace(Source.AncestorTechName))
            {
                var Ancestor = this.Resolver.ConceptDefinition(null, Source.AncestorTechName);
                if (Ancestor != null || this.PreserveSourceIds)
                    return Ancestor;
            }

            if (!this.PreserveSourceIds && DefaultToBase)
                return this.TargetDomain.ConceptDefinitions.FirstOrDefault();

            return null;
        }

        private RelationshipDefinition ResolveRelationshipAncestor(DomainJsonElement Source, bool DefaultToBase)
        {
            if (!String.IsNullOrWhiteSpace(Source.AncestorTechName))
            {
                var Ancestor = this.Resolver.RelationshipDefinition(null, Source.AncestorTechName);
                if (Ancestor != null || this.PreserveSourceIds)
                    return Ancestor;
            }

            if (!this.PreserveSourceIds && DefaultToBase)
                return this.TargetDomain.RelationshipDefinitions.FirstOrDefault();

            return null;
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

            Changed = ApplyVersionFields(Target, Source, Entity, MatchMethod) || Changed;

            return Changed;
        }

        private bool ApplyVersionFields(FormalElement Target, DomainJsonElement Source, string Entity, string MatchMethod)
        {
            if (Target == null || Source == null || Source.Set == null)
                return false;

            var VersionNumber = GetSetString(Source.Set, "versionNumber");
            var VersionAnnotation = GetSetString(Source.Set, "versionAnnotation");
            var VersionSequence = GetSetInt(Source.Set, "versionSequence");
            var Creation = GetSetString(Source.Set, "creation");
            var Creator = GetSetString(Source.Set, "creator");
            var LastModification = GetSetString(Source.Set, "lastModification");
            var LastModifier = GetSetString(Source.Set, "lastModifier");

            var HasVersionData = VersionNumber != null ||
                                 VersionAnnotation != null ||
                                 VersionSequence != null ||
                                 Creation != null ||
                                 Creator != null ||
                                 LastModification != null ||
                                 LastModifier != null;

            if (!HasVersionData)
                return false;

            var Changed = false;
            if (Target.Version == null)
            {
                this.Report.LogFieldUpdate(Entity, "version", Describe(Target), MatchMethod, "<none>", "<present>", this.IsPreview);
                if (!this.IsPreview)
                    Target.Version = new VersionCard();
                Changed = true;
            }

            if (Target.Version == null)
                return Changed;

            if (VersionNumber != null && Target.Version.VersionNumber != VersionNumber)
            {
                this.Report.LogFieldUpdate(Entity, "versionNumber", Describe(Target), MatchMethod, Target.Version.VersionNumber, VersionNumber, this.IsPreview);
                if (!this.IsPreview)
                    Target.Version.VersionNumber = VersionNumber;
                Changed = true;
            }

            if (VersionAnnotation != null && Target.Version.Annotation != VersionAnnotation)
            {
                this.Report.LogFieldUpdate(Entity, "versionAnnotation", Describe(Target), MatchMethod, Target.Version.Annotation, VersionAnnotation, this.IsPreview);
                if (!this.IsPreview)
                    Target.Version.Annotation = VersionAnnotation;
                Changed = true;
            }

            if (VersionSequence != null && Target.Version.VersionSequence != VersionSequence.Value)
            {
                this.Report.LogFieldUpdate(Entity, "versionSequence", Describe(Target), MatchMethod,
                                           Target.Version.VersionSequence.ToString(CultureInfo.InvariantCulture),
                                           VersionSequence.Value.ToString(CultureInfo.InvariantCulture), this.IsPreview);
                if (!this.IsPreview)
                    Target.Version.VersionSequence = VersionSequence.Value;
                Changed = true;
            }

            if (Creator != null && Target.Version.Creator != Creator)
            {
                this.Report.LogFieldUpdate(Entity, "creator", Describe(Target), MatchMethod, Target.Version.Creator, Creator, this.IsPreview);
                if (!this.IsPreview)
                    Target.Version.Creator = Creator;
                Changed = true;
            }

            if (LastModifier != null && Target.Version.LastModifier != LastModifier)
            {
                this.Report.LogFieldUpdate(Entity, "lastModifier", Describe(Target), MatchMethod, Target.Version.LastModifier, LastModifier, this.IsPreview);
                if (!this.IsPreview)
                    Target.Version.LastModifier = LastModifier;
                Changed = true;
            }

            DateTime ParsedDate;
            if (Creation != null)
                if (TryParseJsonDate(Creation, out ParsedDate))
                {
                    if (Target.Version.Creation != ParsedDate)
                    {
                        this.Report.LogFieldUpdate(Entity, "creation", Describe(Target), MatchMethod,
                                                   Target.Version.Creation.ToString("o", CultureInfo.InvariantCulture),
                                                   Creation, this.IsPreview);
                        if (!this.IsPreview)
                            Target.Version.Creation = ParsedDate;
                        Changed = true;
                    }
                }
                else
                    this.Report.ImportWarning("Invalid version creation timestamp '" + Creation + "' for " + Describe(Target) + ".");

            if (LastModification != null)
                if (TryParseJsonDate(LastModification, out ParsedDate))
                {
                    if (Target.Version.LastModification != ParsedDate)
                    {
                        this.Report.LogFieldUpdate(Entity, "lastModification", Describe(Target), MatchMethod,
                                                   Target.Version.LastModification.ToString("o", CultureInfo.InvariantCulture),
                                                   LastModification, this.IsPreview);
                        if (!this.IsPreview)
                            Target.Version.LastModification = ParsedDate;
                        Changed = true;
                    }
                }
                else
                    this.Report.ImportWarning("Invalid version lastModification timestamp '" + LastModification + "' for " + Describe(Target) + ".");

            return Changed;
        }

        private static bool TryParseJsonDate(string Text, out DateTime Result)
        {
            return DateTime.TryParse(Text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out Result);
        }

        private bool AssignImportedId(FormalElement Target, DomainJsonElement Source, string Entity = null, string MatchMethod = null)
        {
            if (!this.PreserveSourceIds || this.IsPreview || Target == null || Source == null || String.IsNullOrWhiteSpace(Source.Id))
                return false;

            Guid Parsed;
            if (!Guid.TryParse(Source.Id, out Parsed))
            {
                this.Report.ImportWarning("Domain JSON id '" + Source.Id + "' for " + Entity.ToStringAlways("domainEntity") + " is not a valid GUID; preserving generated id.");
                return false;
            }

            if (Target.GlobalId == Parsed)
                return false;

            if (KnownDomainUniqueElements().Any(Element => Element != null &&
                                                           !Object.ReferenceEquals(Element, Target) &&
                                                           Element.GlobalId == Parsed))
            {
                this.Report.ImportWarning("Domain JSON id '" + Source.Id + "' for " + Entity.ToStringAlways("domainEntity") + " already exists in the target domain; preserving generated id.");
                return false;
            }

            this.Report.LogFieldUpdate(Entity.NullDefault(Source.Entity).NullDefault("domainEntity"),
                                       "id",
                                       Describe(Target),
                                       MatchMethod.NullDefault("preserveSourceIds"),
                                       Target.GlobalId,
                                       Parsed,
                                       this.IsPreview);
            Target.GlobalId = Parsed;
            return true;
        }

        private IEnumerable<FormalElement> KnownDomainUniqueElements()
        {
            yield return this.TargetDomain;

            foreach (var Language in this.TargetDomain.ExternalLanguages)
                yield return Language;

            foreach (var Category in this.TargetDomain.ConceptDefClusters)
                yield return Category;

            foreach (var Category in this.TargetDomain.RelationshipDefClusters)
                yield return Category;

            foreach (var Category in this.TargetDomain.TableDefCategories)
                yield return Category;

            foreach (var Category in this.TargetDomain.FieldDefCategories)
                yield return Category;

            foreach (var Table in this.TargetDomain.TableDefinitions)
            {
                yield return Table;

                foreach (var Field in Table.FieldDefinitions)
                    yield return Field;
            }

            foreach (var Definition in this.TargetDomain.ConceptDefinitions)
                yield return Definition;

            foreach (var Definition in this.TargetDomain.RelationshipDefinitions)
            {
                yield return Definition;

                if (Definition.OriginOrParticipantLinkRoleDef != null)
                    yield return Definition.OriginOrParticipantLinkRoleDef;

                if (Definition.TargetLinkRoleDef != null)
                    yield return Definition.TargetLinkRoleDef;
            }
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
                    AssignImportedId(Existing, Source, Entity, "create");
                    ApplyFormalFields(Existing, Source);
                    TargetList.Add(Existing);
                }
                this.Report.CountCreated(Entity, this.IsPreview);
                return;
            }

            var Changed = AssignImportedId(Existing, Source, Entity, MatchMethodFor(Existing, Source));
            Changed = ApplyFormalFields(Existing, Source) || Changed;
            if (Changed)
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

        private static string TechNameOf(FormalElement Element)
        {
            return Element == null ? null : Element.TechName;
        }

        private static bool HasSetKey(IDictionary<string, object> Set, string Key)
        {
            return Set != null && Set.ContainsKey(Key);
        }

        private static string GetSetString(IDictionary<string, object> Set, string Key)
        {
            if (Set == null || !Set.ContainsKey(Key) || Set[Key] == null)
                return null;
            return Convert.ToString(Set[Key], CultureInfo.InvariantCulture);
        }

        private static object GetSetObject(IDictionary<string, object> Set, string Key)
        {
            if (Set == null || !Set.ContainsKey(Key))
                return null;
            return Set[Key];
        }

        private static IDictionary<string, object> GetSetDictionary(IDictionary<string, object> Set, string Key)
        {
            var Value = GetSetObject(Set, Key);
            var Dictionary = Value as IDictionary<string, object>;
            if (Dictionary != null)
                return Dictionary;

            return new Dictionary<string, object>();
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

        private static double? GetSetDouble(IDictionary<string, object> Set, string Key)
        {
            if (Set == null || !Set.ContainsKey(Key) || Set[Key] == null)
                return null;

            double Result;
            return Double.TryParse(Convert.ToString(Set[Key], CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out Result)
                   ? (double?)Result : null;
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

        private static TextFormat ImportTextFormat(object Source)
        {
            var SourceDictionary = Source as IDictionary<string, object>;
            if (SourceDictionary == null)
                return null;

            var FontFamilyName = Convert.ToString(GetSetObject(SourceDictionary, "fontFamilyName")
                                                  ?? GetSetObject(SourceDictionary, "fontFamily")
                                                  ?? "Arial",
                                                  CultureInfo.InvariantCulture);

            var FontSize = GetSetDouble(SourceDictionary, "fontSize") ?? 12.0;
            var ForegroundBrush = ImportBrush(GetSetObject(SourceDictionary, "foregroundBrush")
                                              ?? GetSetObject(SourceDictionary, "foreground"));
            var IsBold = GetSetBool(SourceDictionary, "isBold") ?? false;
            var IsItalic = GetSetBool(SourceDictionary, "isItalic") ?? false;
            var IsUnderline = GetSetBool(SourceDictionary, "isUnderline") ?? false;
            var IsStrikethrough = GetSetBool(SourceDictionary, "isStrikethrough") ?? false;

            var Alignment = TextAlignment.Left;
            var AlignmentText = Convert.ToString(GetSetObject(SourceDictionary, "alignment"), CultureInfo.InvariantCulture);
            if (!String.IsNullOrWhiteSpace(AlignmentText))
                Enum.TryParse(AlignmentText, true, out Alignment);

            return new TextFormat(FontFamilyName, FontSize, ForegroundBrush, IsBold, IsItalic, IsUnderline, Alignment, IsStrikethrough);
        }

        private static Brush ImportBrush(object Source)
        {
            if (Source == null)
                return null;

            var Opacity = default(double?);
            var SourceDictionary = Source as IDictionary<string, object>;
            if (SourceDictionary != null)
            {
                var Xaml = GetSetObject(SourceDictionary, "xaml");
                if (Xaml != null)
                    Source = Xaml;
                else
                    Source = GetSetObject(SourceDictionary, "color")
                             ?? GetSetObject(SourceDictionary, "brush")
                             ?? GetSetObject(SourceDictionary, "value");
                Opacity = GetSetDouble(SourceDictionary, "opacity");
            }

            var Text = Convert.ToString(Source, CultureInfo.InvariantCulture);
            if (String.IsNullOrWhiteSpace(Text))
                return null;

            try
            {
                var Result = Text.TrimStart().StartsWith("<", StringComparison.Ordinal)
                             ? ImportBrushXaml(Text)
                             : (Brush)new BrushConverter().ConvertFromString(null, CultureInfo.InvariantCulture, Text);
                if (Result != null && Opacity != null)
                {
                    Result = Result.CloneCurrentValue();
                    Result.Opacity = Opacity.Value.EnforceRange(0.0, 1.0);
                }

                return Result;
            }
            catch
            {
                return null;
            }
        }

        private static Brush ImportBrushXaml(string Text)
        {
            if (!IsSupportedBrushXaml(Text))
                return null;

            return XamlReader.Parse(Text) as Brush;
        }

        private static bool IsSupportedBrushXaml(string Text)
        {
            try
            {
                var Settings = new XmlReaderSettings();
                Settings.DtdProcessing = DtdProcessing.Prohibit;
                Settings.XmlResolver = null;

                using (var Reader = XmlReader.Create(new StringReader(Text), Settings))
                {
                    while (Reader.Read())
                    {
                        if (Reader.NodeType != XmlNodeType.Element)
                            continue;

                        if (!IsSupportedBrushXamlElement(Reader.LocalName))
                            return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSupportedBrushXamlElement(string LocalName)
        {
            return LocalName == "SolidColorBrush" ||
                   LocalName == "LinearGradientBrush" ||
                   LocalName == "LinearGradientBrush.GradientStops" ||
                   LocalName == "RadialGradientBrush" ||
                   LocalName == "RadialGradientBrush.GradientStops" ||
                   LocalName == "GradientStop";
        }

        private static DashStyle ImportDashStyle(object Source)
        {
            if (Source == null)
                return null;

            var Text = Convert.ToString(Source, CultureInfo.InvariantCulture);
            if (String.IsNullOrWhiteSpace(Text))
                return null;

            var Declared = Display.DeclaredDashStyles.FirstOrDefault(Item => String.Equals(Item.Item2, Text, StringComparison.OrdinalIgnoreCase));
            return Declared == null ? null : Declared.Item1;
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
