// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Safe merge importer for the JSON interchange document.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Instrumind.Common;
using Instrumind.Common.EntityBase;
using Instrumind.Common.EntityDefinition;
using Instrumind.Common.Visualization;
using Instrumind.ThinkComposer.MetaModel;
using Instrumind.ThinkComposer.MetaModel.GraphMetaModel;
using Instrumind.ThinkComposer.MetaModel.InformationMetaModel;
using Instrumind.ThinkComposer.Model;
using Instrumind.ThinkComposer.Model.GraphModel;
using Instrumind.ThinkComposer.Model.InformationModel;
using Instrumind.ThinkComposer.Model.VisualModel;

namespace Instrumind.ThinkComposer.Composer.JsonInterchange
{
    public class CompositionJsonImporter
    {
        private readonly Composition Composition;
        private readonly CompositionEngine Engine;
        private readonly bool IsPreview;
        private readonly CompositionJsonImportReport Report;

        private CompositionJsonImporter(Composition Composition, CompositionEngine Engine, bool IsPreview)
        {
            this.Composition = Composition;
            this.Engine = Engine ?? Composition.Engine;
            this.IsPreview = IsPreview;
            this.Report = new CompositionJsonImportReport();
        }

        public static CompositionJsonImportReport Preview(Composition Composition, CompositionJsonDocument Document)
        {
            General.ContractRequiresNotNull(Composition);
            CompositionJsonSerializer.Validate(Document);

            var Importer = new CompositionJsonImporter(Composition, Composition.Engine, true);
            Importer.ApplyDocument(Document);
            return Importer.Report;
        }

        public static CompositionJsonImportReport Import(CompositionEngine Engine, CompositionJsonDocument Document)
        {
            General.ContractRequiresNotNull(Engine, Engine.TargetComposition);
            CompositionJsonSerializer.Validate(Document);

            var Importer = new CompositionJsonImporter(Engine.TargetComposition, Engine, false);

            Engine.StartCommandVariation("Import JSON");
            try
            {
                Importer.ApplyDocument(Document);

                if (Engine.IsVariating)
                    Engine.CompleteCommandVariation();

                if (Importer.Report.Updated > 0 || Importer.Report.Created > 0 || Importer.Report.Deleted > 0)
                    Engine.ExistenceStatus = EExistenceStatus.Modified;

                Importer.RefreshAffectedViews();
            }
            catch
            {
                if (Engine.IsVariating)
                {
                    var Completed = Engine.CompleteCommandVariation();
                    if (Completed != null)
                        Engine.Undo(false, false);
                }

                throw;
            }

            return Importer.Report;
        }

        private void ApplyDocument(CompositionJsonDocument Document)
        {
            if (Document.Warnings != null)
                foreach (var Warning in Document.Warnings)
                    this.Report.Warn("Export warning preserved from JSON: " + Warning);

            if (Document.Composition != null)
                ApplyComposition(Document.Composition);

            if (Document.Ideas != null)
                foreach (var Idea in Document.Ideas)
                    if (StringEquals(Idea.Kind, "Relationship"))
                        this.Report.Warn("Relationship-like item appeared in ideas[] and was skipped. Put relationships in relationships[].");
                    else
                        ApplyConcept(Idea);

            if (Document.Relationships != null)
                foreach (var Relationship in Document.Relationships)
                    ApplyRelationship(Relationship);

            if (Document.Views != null)
                foreach (var View in Document.Views)
                    ApplyView(View);

            if (Document.Operations != null)
                foreach (var Operation in Document.Operations)
                    ApplyOperation(Operation);
        }

        private void ApplyComposition(CompositionJsonComposition Source)
        {
            var Changed = ApplyFormalSet(this.Composition, Source.Name, Source.TechName, Source.Summary, Source.Version);

            if (!String.IsNullOrEmpty(Source.ViewsPrefix) && this.Composition.ViewsPrefix != Source.ViewsPrefix)
            {
                if (!this.IsPreview)
                    this.Composition.ViewsPrefix = Source.ViewsPrefix;
                Changed = true;
            }

            CountUpdated(Changed);
        }

        private void ApplyConcept(CompositionJsonIdea Source)
        {
            var Existing = FindConcept(Source.Id, Source.TechName);

            if (Source.Delete)
            {
                DeleteIdea(Existing, "concept", Source.Id, Source.TechName);
                return;
            }

            if (Existing != null)
            {
                var Changed = ApplyFormalSet(Existing, Source.Name, Source.TechName, Source.Summary, null);
                CountUpdated(Changed);
                ApplyMarkers(Existing, Source.Markers);
                ApplyDetails(Existing, Source.Details);
                return;
            }

            if (CanCreateFromState(Source.Id, Source.IsNew))
                CreateConcept(Source);
            else
                Skip("Concept '" + Describe(Source.Id, Source.TechName) + "' was not found. Add isNew:true or omit id and provide a definition/container to create it.");
        }

        private void ApplyRelationship(CompositionJsonRelationship Source)
        {
            var Existing = FindRelationship(Source.Id, Source.TechName);

            if (Source.Delete)
            {
                DeleteIdea(Existing, "relationship", Source.Id, Source.TechName);
                return;
            }

            if (Existing != null)
            {
                var Changed = ApplyFormalSet(Existing, Source.Name, Source.TechName, Source.Summary, null);
                CountUpdated(Changed);
                ApplyRelationshipLinks(Existing, Source);
                ApplyMarkers(Existing, Source.Markers);
                ApplyDetails(Existing, Source.Details);
                return;
            }

            if (CanCreateFromState(Source.Id, Source.IsNew))
                CreateRelationship(Source);
            else
                Skip("Relationship '" + Describe(Source.Id, Source.TechName) + "' was not found. Add isNew:true or omit id and provide a definition/container to create it.");
        }

        private bool CanCreateFromState(string Id, bool IsNew)
        {
            return IsNew || String.IsNullOrEmpty(Id);
        }

        private void CreateConcept(CompositionJsonIdea Source)
        {
            var Definition = FindConceptDefinition(Source.DefinitionId, Source.DefinitionTechName, Source.DefinitionName);
            if (Definition == null)
            {
                Skip("Cannot create concept '" + Source.Name.ToStringAlways() + "' because definition '" + Source.DefinitionTechName.ToStringAlways() + "' was not found.");
                return;
            }

            var Container = ResolveContainer(Source.ContainerId, Source.ContainerTechName, Definition.OwnerDomain);
            if (Container == null)
            {
                Skip("Cannot create concept '" + Source.Name.ToStringAlways() + "' because its container was not found or is not safe.");
                return;
            }

            if (String.IsNullOrEmpty(Source.Name))
            {
                Skip("Cannot create concept because name is missing.");
                return;
            }

            if (this.IsPreview)
            {
                this.Report.Created++;
                return;
            }

            var Concept = new Concept(this.Composition, Definition, Source.Name, Source.TechName.NullDefault(Source.Name.TextToIdentifier()), Source.Summary.NullDefault(""));
            AssignImportedId(Concept, Source.Id);

            if (Definition.IsVersionable)
                Concept.Version = new VersionCard();

            Concept.AddToComposite(Container);
            this.Report.Created++;

            ApplyMarkers(Concept, Source.Markers);
            ApplyDetails(Concept, Source.Details);
        }

        private void CreateRelationship(CompositionJsonRelationship Source)
        {
            var Definition = FindRelationshipDefinition(Source.DefinitionId, Source.DefinitionTechName, Source.DefinitionName);
            if (Definition == null)
            {
                Skip("Cannot create relationship '" + Source.Name.ToStringAlways() + "' because definition '" + Source.DefinitionTechName.ToStringAlways() + "' was not found.");
                return;
            }

            var Container = ResolveContainer(Source.ContainerId, Source.ContainerTechName, Definition.OwnerDomain);
            if (Container == null)
            {
                Skip("Cannot create relationship '" + Source.Name.ToStringAlways() + "' because its container was not found or is not safe.");
                return;
            }

            var Name = Source.Name.NullDefault(Definition.Name);
            if (String.IsNullOrEmpty(Name))
            {
                Skip("Cannot create relationship because name is missing.");
                return;
            }

            if (this.IsPreview)
            {
                this.Report.Created++;
                return;
            }

            var Relationship = new Relationship(this.Composition, Definition, Name, Source.TechName.NullDefault(Name.TextToIdentifier()), Source.Summary.NullDefault(""));
            AssignImportedId(Relationship, Source.Id);

            if (Definition.IsVersionable)
                Relationship.Version = new VersionCard();

            Relationship.AddToComposite(Container);
            this.Report.Created++;

            ApplyRelationshipLinks(Relationship, Source);
            ApplyMarkers(Relationship, Source.Markers);
            ApplyDetails(Relationship, Source.Details);
        }

        private void ApplyRelationshipLinks(Relationship Relationship, CompositionJsonRelationship Source)
        {
            if (Source.Links != null && Source.Links.Count > 0)
            {
                foreach (var Link in Source.Links)
                    AddRelationshipLink(Relationship, Link.RoleType, Link.IdeaId, Link.IdeaTechName);
                return;
            }

            if (Source.OriginIdeaIds != null)
                foreach (var IdeaId in Source.OriginIdeaIds)
                    AddRelationshipLink(Relationship, "Origin", IdeaId, null);

            if (Source.TargetIdeaIds != null)
                foreach (var IdeaId in Source.TargetIdeaIds)
                    AddRelationshipLink(Relationship, "Target", IdeaId, null);
        }

        private void AddRelationshipLink(Relationship Relationship, string RoleTypeName, string IdeaId, string IdeaTechName)
        {
            var Idea = FindIdea(IdeaId, IdeaTechName);
            if (Idea == null)
            {
                Skip("Cannot create relationship link for '" + Relationship.TechName + "' because idea '" + Describe(IdeaId, IdeaTechName) + "' was not found.");
                return;
            }

            ERoleType RoleType = ERoleType.Origin;
            if (StringEquals(RoleTypeName, "Target"))
                RoleType = ERoleType.Target;

            var Role = Relationship.RelationshipDefinitor.Value.GetLinkForRole(RoleType);
            if (Role == null)
            {
                Skip("Cannot create relationship link for '" + Relationship.TechName + "' because role '" + RoleTypeName.ToStringAlways() + "' was not found.");
                return;
            }

            if (Relationship.Links.Any(Link => Link.RoleDefinitor == Role && Link.AssociatedIdea == Idea))
                return;

            if (this.IsPreview)
            {
                this.Report.Updated++;
                return;
            }

            var Variant = Role.AllowedVariants.FirstOrDefault();
            if (Variant == null && this.Composition.CompositeContentDomain != null)
                Variant = this.Composition.CompositeContentDomain.LinkRoleVariants.FirstOrDefault();

            var NewLink = new RoleBasedLink(Relationship, Idea, Role, Variant);
            Relationship.AddLink(NewLink);
            this.Report.Updated++;
        }

        private void DeleteIdea(Idea Target, string Entity, string Id, string TechName)
        {
            if (Target == null)
            {
                Skip("Cannot delete " + Entity + " '" + Describe(Id, TechName) + "' because it was not found.");
                return;
            }

            if (Target == this.Composition)
            {
                Skip("Deleting the active composition root is not supported by JSON import.");
                return;
            }

            if (this.IsPreview)
            {
                this.Report.Deleted++;
                return;
            }

            Target.RemoveFromComposite(false, false);
            this.Report.Deleted++;
        }

        private void ApplyMarkers(Idea Idea, IList<CompositionJsonMarker> Markers)
        {
            if (Markers == null)
                return;

            foreach (var Marker in Markers)
            {
                var Definition = FindMarkerDefinition(Marker.DefinitionId, Marker.DefinitionTechName, Marker.DefinitionName);
                if (Definition == null)
                {
                    Skip("Marker '" + Marker.DefinitionTechName.ToStringAlways() + "' was not found for idea '" + Idea.TechName + "'.");
                    continue;
                }

                var Existing = Idea.Markings.FirstOrDefault(Assignment => Assignment.Definitor == Definition);

                if (Marker.Delete)
                {
                    if (Existing == null)
                    {
                        Skip("Marker '" + Definition.TechName + "' was requested for deletion but is not assigned to idea '" + Idea.TechName + "'.");
                        continue;
                    }

                    if (!this.IsPreview)
                        Idea.Markings.Remove(Existing);
                    this.Report.Deleted++;
                    continue;
                }

                var Descriptor = CreateDescriptor(Marker.DescriptorName, Marker.DescriptorTechName, Marker.DescriptorSummary);

                if (Existing == null)
                {
                    if (!this.IsPreview)
                        Idea.Markings.Add(new MarkerAssignment(this.Engine, Definition, Descriptor));
                    this.Report.Updated++;
                }
                else
                {
                    var Changed = !PresentationEquals(Existing.Descriptor, Descriptor);
                    if (Changed && !this.IsPreview)
                        Existing.Descriptor = Descriptor;
                    CountUpdated(Changed);
                }
            }
        }

        private void ApplyDetails(Idea Idea, IList<CompositionJsonDetail> Details)
        {
            if (Details == null)
                return;

            foreach (var Detail in Details)
            {
                if (StringEquals(Detail.Kind, "Table") || (Detail.Records != null && Detail.Records.Count > 0))
                    ApplyTableDetail(Idea, Detail);
                else
                    if (StringEquals(Detail.Kind, "ResourceLink"))
                        ApplyResourceLinkDetail(Idea, Detail);
                    else
                        if (StringEquals(Detail.Kind, "InternalLink"))
                            ApplyInternalLinkDetail(Idea, Detail);
                        else
                            if (StringEquals(Detail.Kind, "Attachment"))
                                this.Report.Warn("Attachment detail '" + Detail.DesignatorTechName.ToStringAlways() + "' is metadata-only in JSON; native binary content was preserved.");
                            else
                                if (!String.IsNullOrEmpty(Detail.Kind))
                                    this.Report.Warn("Detail kind '" + Detail.Kind + "' is not directly editable by JSON import and was preserved.");
            }
        }

        private void ApplyTableDetail(Idea Idea, CompositionJsonDetail Source)
        {
            var Existing = FindDetail<Table>(Idea, Source.DesignatorId, Source.DesignatorTechName);
            if (Source.Delete)
            {
                DeleteDetail(Idea, Existing, Source);
                return;
            }

            if (Existing == null)
            {
                var Designator = FindDetailDesignator<TableDetailDesignator>(Idea, Source.DesignatorId, Source.DesignatorTechName);
                if (Designator == null)
                {
                    Skip("Table detail '" + Source.DesignatorTechName.ToStringAlways() + "' was not found on idea '" + Idea.TechName + "'.");
                    return;
                }

                if (this.IsPreview)
                {
                    this.Report.Updated++;
                    return;
                }

                Existing = new Table(Idea, Designator.Assign<DetailDesignator>(true));
                Idea.Details.Add(Existing);
            }

            if (Source.Records == null)
                return;

            if (Existing.Definition == null)
            {
                Skip("Table detail '" + Source.DesignatorTechName.ToStringAlways() + "' has no table definition and cannot import records.");
                return;
            }

            if (this.IsPreview)
            {
                this.Report.Updated++;
                return;
            }

            Existing.Clear();
            foreach (var SourceRecord in Source.Records)
            {
                var Record = new TableRecord(Existing);
                foreach (var Field in Existing.Definition.FieldDefinitions.OrderBy(Field => Field.StorageIndex))
                {
                    object Value = null;
                    if (SourceRecord.ContainsKey(Field.TechName))
                        Value = SourceRecord[Field.TechName];
                    else
                        if (SourceRecord.ContainsKey(Field.Name))
                            Value = SourceRecord[Field.Name];
                        else
                            continue;

                    if (!Record.SetStoredValue(Field, Value))
                        this.Report.Warn("Field '" + Field.TechName + "' in table detail '" + Source.DesignatorTechName.ToStringAlways() + "' rejected imported value '" + Value.ToStringAlways() + "'.");
                }

                Existing.Add(Record);
            }

            this.Report.Updated++;
        }

        private void ApplyResourceLinkDetail(Idea Idea, CompositionJsonDetail Source)
        {
            var Existing = FindDetail<ResourceLink>(Idea, Source.DesignatorId, Source.DesignatorTechName);
            if (Source.Delete)
            {
                DeleteDetail(Idea, Existing, Source);
                return;
            }

            if (String.IsNullOrEmpty(Source.TargetAddress))
                return;

            if (Existing == null)
            {
                var Designator = FindDetailDesignator<LinkDetailDesignator>(Idea, Source.DesignatorId, Source.DesignatorTechName);
                if (Designator == null)
                {
                    Skip("Resource link detail '" + Source.DesignatorTechName.ToStringAlways() + "' was not found on idea '" + Idea.TechName + "'.");
                    return;
                }

                if (!this.IsPreview)
                {
                    Existing = new ResourceLink(Idea, Designator.Assign<DetailDesignator>(true));
                    Idea.Details.Add(Existing);
                }
            }

            if (this.IsPreview)
            {
                this.Report.Updated++;
                return;
            }

            Existing.TargetLocation = Source.TargetAddress;
            this.Report.Updated++;
        }

        private void ApplyInternalLinkDetail(Idea Idea, CompositionJsonDetail Source)
        {
            var Existing = FindDetail<InternalLink>(Idea, Source.DesignatorId, Source.DesignatorTechName);
            if (Source.Delete)
            {
                DeleteDetail(Idea, Existing, Source);
                return;
            }

            if (String.IsNullOrEmpty(Source.Text))
                return;

            if (SetKnownIdeaField(Idea, Source.TargetPropertyTechName, Source.Text))
            {
                this.Report.Updated++;
                return;
            }

            this.Report.Warn("Internal link detail '" + Source.DesignatorTechName.ToStringAlways() + "' was not directly editable and was preserved.");
        }

        private void DeleteDetail<TDetail>(Idea Idea, TDetail Existing, CompositionJsonDetail Source)
            where TDetail : ContainedDetail
        {
            if (Existing == null)
            {
                Skip("Detail '" + Source.DesignatorTechName.ToStringAlways() + "' was requested for deletion but was not found on idea '" + Idea.TechName + "'.");
                return;
            }

            if (!this.IsPreview)
                Idea.Details.Remove(Existing);
            this.Report.Deleted++;
        }

        private void ApplyView(CompositionJsonView Source)
        {
            var Existing = FindView(Source.Id, Source.TechName);
            if (Existing == null)
            {
                Skip("View '" + Describe(Source.Id, Source.TechName) + "' was not found. Creating views from JSON is not supported yet.");
                return;
            }

            CountUpdated(ApplyFormalSet(Existing, Source.Name, Source.TechName, Source.Summary, null));

            if (Source.Visuals == null)
                return;

            foreach (var Visual in Source.Visuals)
                ApplyVisual(Existing, Visual);
        }

        private void ApplyVisual(View View, CompositionJsonVisual Source)
        {
            var Representation = FindVisualRepresentation(View, Source.RepresentationId, Source.IdeaId, Source.IdeaTechName);
            if (Representation == null || Representation.MainSymbol == null)
            {
                Skip("Visual representation '" + Source.RepresentationId.ToStringAlways() + "' was not found in view '" + View.TechName + "'.");
                return;
            }

            if (Source.X == null && Source.Y == null && Source.Width == null && Source.Height == null)
                return;

            if (this.IsPreview)
            {
                this.Report.Updated++;
                return;
            }

            var Symbol = Representation.MainSymbol;
            var Width = Source.Width == null ? Symbol.BaseWidth : Source.Width.Value;
            var Height = Source.Height == null ? Symbol.BaseHeight : Source.Height.Value;
            Symbol.ResizeTo(Width, Height);

            var X = Source.X == null ? Symbol.BaseLeft : Source.X.Value;
            var Y = Source.Y == null ? Symbol.BaseTop : Source.Y.Value;
            Symbol.MoveTo(X + Symbol.BaseWidth / 2.0, Y + Symbol.BaseHeight / 2.0, true);

            this.Report.Updated++;
        }

        private void ApplyOperation(CompositionJsonOperation Operation)
        {
            var Op = Operation.Op.NullDefault("").ToLowerInvariant();
            var Entity = Operation.Entity.NullDefault("").ToLowerInvariant();

            if (Op == "update")
                ApplyUpdateOperation(Entity, Operation);
            else
                if (Op == "create")
                    ApplyCreateOperation(Entity, Operation);
                else
                    if (Op == "delete")
                        ApplyDeleteOperation(Entity, Operation);
                    else
                        Skip("Unsupported operation op '" + Operation.Op.ToStringAlways() + "'.");
        }

        private void ApplyUpdateOperation(string Entity, CompositionJsonOperation Operation)
        {
            if (Entity == "composition")
            {
                CountUpdated(ApplySetToFormal(this.Composition, Operation.Set));
                return;
            }

            if (Entity == "concept")
            {
                var Concept = FindConcept(Operation.Id, Operation.TechName);
                if (Concept == null)
                {
                    Skip("Cannot update concept '" + Describe(Operation.Id, Operation.TechName) + "' because it was not found.");
                    return;
                }

                CountUpdated(ApplySetToFormal(Concept, Operation.Set));
                return;
            }

            if (Entity == "relationship")
            {
                var Relationship = FindRelationship(Operation.Id, Operation.TechName);
                if (Relationship == null)
                {
                    Skip("Cannot update relationship '" + Describe(Operation.Id, Operation.TechName) + "' because it was not found.");
                    return;
                }

                CountUpdated(ApplySetToFormal(Relationship, Operation.Set));
                return;
            }

            if (Entity == "view")
            {
                var View = FindView(Operation.Id, Operation.TechName);
                if (View == null)
                {
                    Skip("Cannot update view '" + Describe(Operation.Id, Operation.TechName) + "' because it was not found.");
                    return;
                }

                CountUpdated(ApplySetToFormal(View, Operation.Set));
                return;
            }

            Skip("Update operation for entity '" + Entity + "' is not supported.");
        }

        private void ApplyCreateOperation(string Entity, CompositionJsonOperation Operation)
        {
            if (Entity == "concept")
            {
                var Source = new CompositionJsonIdea();
                Source.IsNew = true;
                Source.Id = Operation.Id;
                Source.TechName = GetSetString(Operation.Set, "techName").NullDefault(Operation.TechName);
                Source.Name = GetSetString(Operation.Set, "name");
                Source.Summary = GetSetString(Operation.Set, "summary");
                Source.DefinitionTechName = Operation.DefinitionTechName;
                Source.ContainerId = Operation.ContainerId;
                Source.ContainerTechName = Operation.ContainerTechName;
                CreateConcept(Source);
                return;
            }

            if (Entity == "relationship")
            {
                var Source = new CompositionJsonRelationship();
                Source.IsNew = true;
                Source.Id = Operation.Id;
                Source.TechName = GetSetString(Operation.Set, "techName").NullDefault(Operation.TechName);
                Source.Name = GetSetString(Operation.Set, "name");
                Source.Summary = GetSetString(Operation.Set, "summary");
                Source.DefinitionTechName = Operation.DefinitionTechName;
                Source.ContainerId = Operation.ContainerId;
                Source.ContainerTechName = Operation.ContainerTechName;
                Source.OriginIdeaIds = Operation.OriginIdeaIds;
                Source.TargetIdeaIds = Operation.TargetIdeaIds;
                CreateRelationship(Source);
                return;
            }

            Skip("Create operation for entity '" + Entity + "' is not supported.");
        }

        private void ApplyDeleteOperation(string Entity, CompositionJsonOperation Operation)
        {
            if (Entity == "concept")
            {
                DeleteIdea(FindConcept(Operation.Id, Operation.TechName), "concept", Operation.Id, Operation.TechName);
                return;
            }

            if (Entity == "relationship")
            {
                DeleteIdea(FindRelationship(Operation.Id, Operation.TechName), "relationship", Operation.Id, Operation.TechName);
                return;
            }

            Skip("Delete operation for entity '" + Entity + "' is not supported.");
        }

        private bool ApplySetToFormal(FormalElement Target, IDictionary<string, object> Set)
        {
            if (Target == null || Set == null || Set.Count < 1)
                return false;

            var Name = GetSetString(Set, "name");
            var TechName = GetSetString(Set, "techName");
            var Summary = GetSetString(Set, "summary");
            var VersionAnnotation = GetSetString(Set, "versionAnnotation");
            var VersionNumber = GetSetString(Set, "versionNumber");

            return ApplyFormalSet(Target, Name, TechName, Summary, VersionAnnotation, VersionNumber);
        }

        private bool ApplyFormalSet(FormalElement Target, string Name, string TechName, string Summary, CompositionJsonVersion Version)
        {
            return ApplyFormalSet(Target, Name, TechName, Summary, Version == null ? null : Version.Annotation, Version == null ? null : Version.VersionNumber);
        }

        private bool ApplyFormalSet(FormalElement Target, string Name, string TechName, string Summary, string VersionAnnotation, string VersionNumber = null)
        {
            var Changed = false;

            if (Name != null && Target.Name != Name)
            {
                if (!this.IsPreview)
                    Target.Name = Name;
                Changed = true;
            }

            if (TechName != null && Target.TechName != TechName)
            {
                if (!this.IsPreview)
                    Target.TechName = TechName;
                Changed = true;
            }

            if (Summary != null && Target.Summary != Summary)
            {
                if (!this.IsPreview)
                    Target.Summary = Summary;
                Changed = true;
            }

            if (VersionAnnotation != null || VersionNumber != null)
            {
                if (Target.Version == null && !this.IsPreview)
                    Target.Version = new VersionCard();

                if (Target.Version == null)
                    Changed = true;
                else
                {
                    if (VersionAnnotation != null && Target.Version.Annotation != VersionAnnotation)
                    {
                        if (!this.IsPreview)
                            Target.Version.Annotation = VersionAnnotation;
                        Changed = true;
                    }

                    if (VersionNumber != null && Target.Version.VersionNumber != VersionNumber)
                    {
                        if (!this.IsPreview)
                            Target.Version.VersionNumber = VersionNumber;
                        Changed = true;
                    }
                }
            }

            return Changed;
        }

        private bool SetKnownIdeaField(Idea Idea, string PropertyTechName, string Value)
        {
            if (StringEquals(PropertyTechName, FormalElement.__Name.TechName))
            {
                if (!this.IsPreview)
                    Idea.Name = Value;
                return true;
            }

            if (StringEquals(PropertyTechName, FormalElement.__TechName.TechName))
            {
                if (!this.IsPreview)
                    Idea.TechName = Value;
                return true;
            }

            if (StringEquals(PropertyTechName, FormalElement.__Summary.TechName))
            {
                if (!this.IsPreview)
                    Idea.Summary = Value;
                return true;
            }

            if (StringEquals(PropertyTechName, FormalElement.__Description.TechName))
            {
                if (!this.IsPreview)
                    Idea.Description = Value;
                return true;
            }

            return false;
        }

        private void AssignImportedId(UniqueElement Target, string Id)
        {
            if (String.IsNullOrEmpty(Id))
                return;

            Guid Parsed;
            if (!Guid.TryParse(Id, out Parsed))
            {
                this.Report.Warn("Imported id '" + Id + "' is not a valid GUID; a new id was assigned.");
                return;
            }

            if (this.Composition.DeclaredIdeas.Any(Idea => Idea.GlobalId == Parsed) || this.Composition.GlobalId == Parsed)
            {
                this.Report.Warn("Imported id '" + Id + "' already exists in the composition; a new id was assigned.");
                return;
            }

            Target.GlobalId = Parsed;
        }

        private Idea ResolveContainer(string ContainerId, string ContainerTechName, Domain ExpectedDomain)
        {
            var Container = FindIdea(ContainerId, ContainerTechName);
            if (Container == null && String.IsNullOrEmpty(ContainerId) && String.IsNullOrEmpty(ContainerTechName))
                Container = this.Composition;

            if (Container == null)
                return null;

            if (Container.IdeaDefinitor == null || Container.IdeaDefinitor.CompositeContentDomain == null)
                return null;

            if (ExpectedDomain != null && Container.CompositeContentDomain != null &&
                Container.CompositeContentDomain.GlobalId != ExpectedDomain.GlobalId)
                return null;

            return Container;
        }

        private Concept FindConcept(string Id, string TechName)
        {
            return FindIdea(Id, TechName) as Concept;
        }

        private Relationship FindRelationship(string Id, string TechName)
        {
            return FindIdea(Id, TechName) as Relationship;
        }

        private Idea FindIdea(string Id, string TechName)
        {
            var Ideas = (new Idea[] { this.Composition }).Concat(this.Composition.DeclaredIdeas);
            var Match = FindById<Idea>(Ideas, Id);
            if (Match != null)
                return Match;

            if (String.IsNullOrEmpty(Id) && !String.IsNullOrEmpty(TechName))
                return Ideas.FirstOrDefault(Idea => StringEquals(Idea.TechName, TechName));

            return null;
        }

        private View FindView(string Id, string TechName)
        {
            var Views = this.Composition.GetSubgraphChildren().SelectMany(Idea => Idea.CompositeViews).Distinct();
            var Match = FindById<View>(Views, Id);
            if (Match != null)
                return Match;

            if (String.IsNullOrEmpty(Id) && !String.IsNullOrEmpty(TechName))
                return Views.FirstOrDefault(View => StringEquals(View.TechName, TechName));

            return null;
        }

        private VisualRepresentation FindVisualRepresentation(View View, string RepresentationId, string IdeaId, string IdeaTechName)
        {
            var Representations = this.Composition.DeclaredIdeas
                                      .SelectMany(DeclaredIdea => DeclaredIdea.VisualRepresentators)
                                      .Where(Representation => Representation.DisplayingView == View);

            var Match = FindById<VisualRepresentation>(Representations, RepresentationId);
            if (Match != null)
                return Match;

            var Idea = FindIdea(IdeaId, IdeaTechName);
            if (Idea == null)
                return null;

            return Idea.VisualRepresentators.FirstOrDefault(Representation => Representation.DisplayingView == View);
        }

        private TElement FindById<TElement>(IEnumerable<TElement> Source, string Id)
            where TElement : UniqueElement
        {
            if (String.IsNullOrEmpty(Id))
                return null;

            Guid Parsed;
            if (!Guid.TryParse(Id, out Parsed))
                return null;

            return Source.FirstOrDefault(Element => Element != null && Element.GlobalId == Parsed);
        }

        private ConceptDefinition FindConceptDefinition(string Id, string TechName, string Name)
        {
            return FindDefinition<ConceptDefinition>(Id, TechName, Name);
        }

        private RelationshipDefinition FindRelationshipDefinition(string Id, string TechName, string Name)
        {
            return FindDefinition<RelationshipDefinition>(Id, TechName, Name);
        }

        private TDefinition FindDefinition<TDefinition>(string Id, string TechName, string Name)
            where TDefinition : IdeaDefinition
        {
            var Definitions = GetAllDefinitions(this.Composition.CompositeContentDomain).OfType<TDefinition>();
            var Match = FindById<TDefinition>(Definitions, Id);
            if (Match != null)
                return Match;

            if (!String.IsNullOrEmpty(TechName))
                return Definitions.FirstOrDefault(Definition => StringEquals(Definition.TechName, TechName));

            if (!String.IsNullOrEmpty(Name))
                return Definitions.FirstOrDefault(Definition => StringEquals(Definition.Name, Name));

            return null;
        }

        private IEnumerable<IdeaDefinition> GetAllDefinitions(IdeaDefinition Root)
        {
            if (Root == null)
                yield break;

            foreach (var Definition in Root.Definitions)
            {
                yield return Definition;

                foreach (var Child in GetAllDefinitions(Definition))
                    yield return Child;
            }
        }

        private MarkerDefinition FindMarkerDefinition(string Id, string TechName, string Name)
        {
            if (this.Composition.CompositeContentDomain == null || this.Composition.CompositeContentDomain.MarkerDefinitions == null)
                return null;

            var Definitions = this.Composition.CompositeContentDomain.MarkerDefinitions;

            if (!String.IsNullOrEmpty(TechName))
                return Definitions.FirstOrDefault(Definition => StringEquals(Definition.TechName, TechName));

            if (!String.IsNullOrEmpty(Name))
                return Definitions.FirstOrDefault(Definition => StringEquals(Definition.Name, Name));

            return null;
        }

        private TDetail FindDetail<TDetail>(Idea Idea, string DesignatorId, string DesignatorTechName)
            where TDetail : ContainedDetail
        {
            return Idea.Details.OfType<TDetail>()
                       .FirstOrDefault(Detail => Matches(Detail.Designation, DesignatorId, DesignatorTechName));
        }

        private TDesignator FindDetailDesignator<TDesignator>(Idea Idea, string DesignatorId, string DesignatorTechName)
            where TDesignator : DetailDesignator
        {
            var Existing = Idea.Details.Select(Detail => Detail.Designation).OfType<TDesignator>()
                               .FirstOrDefault(Designator => Matches(Designator, DesignatorId, DesignatorTechName));
            if (Existing != null)
                return Existing;

            if (Idea.IdeaDefinitor == null || Idea.IdeaDefinitor.DetailDesignators == null)
                return null;

            return Idea.IdeaDefinitor.DetailDesignators.OfType<TDesignator>()
                       .FirstOrDefault(Designator => Matches(Designator, DesignatorId, DesignatorTechName));
        }

        private bool Matches(UniqueElement Element, string Id, string TechName)
        {
            if (Element == null)
                return false;

            if (!String.IsNullOrEmpty(Id))
            {
                Guid Parsed;
                return Guid.TryParse(Id, out Parsed) && Element.GlobalId == Parsed;
            }

            var Formal = Element as FormalElement;
            return Formal != null && !String.IsNullOrEmpty(TechName) && StringEquals(Formal.TechName, TechName);
        }

        private SimplePresentationElement CreateDescriptor(string Name, string TechName, string Summary)
        {
            if (String.IsNullOrEmpty(Name) && String.IsNullOrEmpty(TechName) && String.IsNullOrEmpty(Summary))
                return null;

            return new SimplePresentationElement(Name.NullDefault(""), TechName.NullDefault(Name.NullDefault("").TextToIdentifier()), Summary.NullDefault(""));
        }

        private bool PresentationEquals(SimplePresentationElement Current, SimplePresentationElement Desired)
        {
            if (Current == null && Desired == null)
                return true;

            if (Current == null || Desired == null)
                return false;

            return Current.Name == Desired.Name && Current.TechName == Desired.TechName && Current.Summary == Desired.Summary;
        }

        private string GetSetString(IDictionary<string, object> Set, string Key)
        {
            if (Set == null || !Set.ContainsKey(Key) || Set[Key] == null)
                return null;

            return Convert.ToString(Set[Key], CultureInfo.InvariantCulture);
        }

        private void CountUpdated(bool Changed)
        {
            if (Changed)
                this.Report.Updated++;
        }

        private void Skip(string Warning)
        {
            this.Report.Skipped++;
            this.Report.Warn(Warning);
        }

        private string Describe(string Id, string TechName)
        {
            if (!String.IsNullOrEmpty(Id))
                return Id;

            return TechName.ToStringAlways();
        }

        private static bool StringEquals(string One, string Two)
        {
            return String.Equals(One, Two, StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshAffectedViews()
        {
            foreach (var Idea in this.Composition.DeclaredIdeas)
                Idea.UpdateVisualRepresentators();

            foreach (var View in this.Composition.GetSubgraphChildren().SelectMany(Idea => Idea.CompositeViews).Distinct())
                try
                {
                    if (View.Presenter != null && View.HostingScrollViewer != null && View.PresenterHostingGrid != null)
                        View.ShowAll();
                }
                catch (Exception Problem)
                {
                    this.Report.Warn("View '" + View.TechName + "' could not be refreshed after import: " + Problem.Message);
                }
        }
    }
}
