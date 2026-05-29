// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Safe merge importer for the JSON interchange document.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

using Instrumind.Common;
using Instrumind.Common.EntityBase;
using Instrumind.Common.EntityDefinition;
using Instrumind.Common.Visualization;
using Instrumind.ThinkComposer.ApplicationProduct;
using Instrumind.ThinkComposer.Composer.ComposerUI;
using Instrumind.ThinkComposer.Composer.Layout;
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
        private readonly Dictionary<View, int> AutoPlacementIndexes = new Dictionary<View, int>();
        private readonly List<View> AffectedViews = new List<View>();
        private readonly Dictionary<View, List<VisualObject>> ImportedVisualObjects = new Dictionary<View, List<VisualObject>>();
        private readonly Dictionary<View, List<RelationshipVisualRepresentation>> PendingAutoRouteRelationships = new Dictionary<View, List<RelationshipVisualRepresentation>>();
        private readonly HashSet<string> PendingAutoRouteKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> PlannedAutoRouteKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string LastOperationOutcome = null;
        private bool AutoPlaceNewItems = true;
        private bool AutoFitPlacedConcepts = true;
        private bool AutoRoutePlacedLinks = true;
        private bool UseActiveCompositionAsContainer = false;
        private bool PreventSelfRecursiveCompositeViews = true;
        private bool RepairRecursiveVisuals = true;
        private string LayoutMode = "gridNearViewport";
        private readonly Dictionary<string, int> MissingContainerSkipCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<View, Point> AutoPlacementOrigins = new Dictionary<View, Point>();
        private readonly Dictionary<View, int> AutoPlacementIgnoredOutliers = new Dictionary<View, int>();
        private readonly Dictionary<string, PlannedConceptReference> PlannedConceptsById = new Dictionary<string, PlannedConceptReference>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PlannedConceptReference> PlannedConceptsByTechName = new Dictionary<string, PlannedConceptReference>(StringComparer.OrdinalIgnoreCase);

        private class PlannedConceptReference
        {
            public string Id;
            public string TechName;
            public IdeaDefinition Definitor;
        }

        private class RelationshipLinkImportSpec
        {
            public string RoleTypeName;
            public string RoleDefinitionTechName;
            public string IdeaId;
            public string IdeaTechName;
            public Idea ResolvedIdea;
            public IdeaDefinition ResolvedIdeaDefinitor;
            public LinkRoleDefinition ResolvedRole;
            public bool ResolvedFromPreviewPlan;
        }

        private class RelationshipLinkImportPlan
        {
            public RelationshipLinkImportPlan()
            {
                this.Specs = new List<RelationshipLinkImportSpec>();
                this.Warnings = new List<string>();
            }

            public string SourceName;
            public List<RelationshipLinkImportSpec> Specs;
            public List<string> Warnings;
            public int ResolvedOriginCount;
            public int ResolvedTargetCount;
            public bool HasConnectivityInput;
        }

        private class RelationshipLinkApplyResult
        {
            public int Added;
            public int Duplicate;
            public int Unresolved;
        }

        private CompositionJsonImporter(Composition Composition, CompositionEngine Engine, bool IsPreview)
        {
            this.Composition = Composition;
            this.Engine = Engine ?? Composition.Engine;
            this.IsPreview = IsPreview;
            this.Report = new CompositionJsonImportReport();
            this.Report.IsPreview = IsPreview;
        }

        public static CompositionJsonImportReport Preview(Composition Composition, CompositionJsonDocument Document)
        {
            General.ContractRequiresNotNull(Composition);
            CompositionJsonSerializer.Validate(Document);

            var Importer = new CompositionJsonImporter(Composition, Composition.Engine, true);
            Importer.Report.Log("JSON import preview started for composition " + Importer.DescribeTarget(Composition) + ".");
            Importer.ApplyDocument(Document);
            Importer.Report.Log("JSON import preview completed: " + Importer.Report.ToDetailedCountsString() + ".");
            return Importer.Report;
        }

        public static CompositionJsonImportReport Import(CompositionEngine Engine, CompositionJsonDocument Document, CompositionJsonImportReport PlannedReport = null)
        {
            General.ContractRequiresNotNull(Engine, Engine.TargetComposition);
            CompositionJsonSerializer.Validate(Document);

            var Importer = new CompositionJsonImporter(Engine.TargetComposition, Engine, false);
            Importer.Report.CopyPlanFrom(PlannedReport);

            Importer.Report.Log("JSON import apply opening command variation for composition " + Importer.DescribeTarget(Engine.TargetComposition) + ".");
            Engine.StartCommandVariation("Import JSON");
            try
            {
                Importer.ApplyDocument(Document);

                Importer.Report.Log("JSON import refreshing affected views inside command variation.");
                Importer.RefreshAffectedViews();

                if (Engine.IsVariating)
                    Engine.CompleteCommandVariation();

                if (Importer.Report.HasAppliedChanges)
                {
                    Engine.ExistenceStatus = EExistenceStatus.Modified;
                    Importer.Report.Log("JSON import document marked modified.");
                }

                Importer.ExposeAffectedViewsAfterImport();
                Importer.Report.Log("JSON import apply completed: " + Importer.Report.ToDetailedCountsString() + ".");
            }
            catch (Exception Problem)
            {
                Importer.Report.Error("JSON import failed: " + Problem.Message);
                Importer.Report.Log("JSON import failure details: " + Problem.ToString());
                Importer.Report.Log("JSON import current operation: " + Importer.Report.CurrentOperationSummary.ToStringAlways());

                if (Engine.IsVariating)
                {
                    Importer.Report.Log("JSON import rollback attempt: complete open variation and undo it.");
                    try
                    {
                        var Completed = Engine.CompleteCommandVariation();
                        if (Completed != null)
                        {
                            Engine.Undo(false, false);
                            Importer.Report.Log("JSON import rollback succeeded via undo.");
                        }
                        else
                            Importer.Report.Log("JSON import rollback warning: command variation completed with no recorded changes.");
                    }
                    catch (Exception RollbackProblem)
                    {
                        Importer.Report.Log("JSON import rollback via undo failed: " + RollbackProblem.ToString());

                        if (Engine.IsVariating)
                        {
                            Importer.Report.Log("JSON import rollback fallback: discard open command variation.");
                            try
                            {
                                Engine.DiscardCommandVariation();
                                Importer.Report.Log("JSON import rollback fallback discard succeeded.");
                            }
                            catch (Exception DiscardProblem)
                            {
                                Importer.Report.Log("JSON import rollback fallback discard failed: " + DiscardProblem.ToString());
                            }
                        }
                    }
                }
                else
                    Importer.Report.Log("JSON import rollback skipped: no command variation was open.");

                throw;
            }

            return Importer.Report;
        }

        private void ApplyDocument(CompositionJsonDocument Document)
        {
            this.AutoPlaceNewItems = Document.ImportOptions == null ||
                                     Document.ImportOptions.AutoPlaceNewItems == null ||
                                     Document.ImportOptions.AutoPlaceNewItems.Value;
            this.AutoFitPlacedConcepts = Document.ImportOptions == null ||
                                         Document.ImportOptions.AutoFitPlacedConcepts == null ||
                                         Document.ImportOptions.AutoFitPlacedConcepts.Value;
            this.AutoRoutePlacedLinks = Document.ImportOptions == null ||
                                        Document.ImportOptions.AutoRoutePlacedLinks == null ||
                                        Document.ImportOptions.AutoRoutePlacedLinks.Value;
            this.UseActiveCompositionAsContainer = Document.ImportOptions != null &&
                                                   Document.ImportOptions.UseActiveCompositionAsContainer != null &&
                                                   Document.ImportOptions.UseActiveCompositionAsContainer.Value;
            this.PreventSelfRecursiveCompositeViews = Document.ImportOptions == null ||
                                                      Document.ImportOptions.PreventSelfRecursiveCompositeViews == null ||
                                                      Document.ImportOptions.PreventSelfRecursiveCompositeViews.Value;
            this.RepairRecursiveVisuals = Document.ImportOptions == null ||
                                          Document.ImportOptions.RepairRecursiveVisuals == null ||
                                          Document.ImportOptions.RepairRecursiveVisuals.Value;
            this.LayoutMode = NormalizeLayoutMode(Document.ImportOptions == null ? null : Document.ImportOptions.LayoutMode);
            this.Report.Log("JSON import options: autoPlaceNewItems=" + (this.AutoPlaceNewItems ? "true" : "false") +
                            ", autoFitPlacedConcepts=" + (this.AutoFitPlacedConcepts ? "true" : "false") +
                            ", autoRoutePlacedLinks=" + (this.AutoRoutePlacedLinks ? "true" : "false") +
                            ", useActiveCompositionAsContainer=" + (this.UseActiveCompositionAsContainer ? "true" : "false") +
                            ", layoutMode=" + this.LayoutMode +
                            ", preventSelfRecursiveCompositeViews=" + (this.PreventSelfRecursiveCompositeViews ? "true" : "false") +
                            ", repairRecursiveVisuals=" + (this.RepairRecursiveVisuals ? "true" : "false") + ".");

            if (this.RepairRecursiveVisuals)
                RepairRecursiveVisualsBeforeImport();

            if (Document.Warnings != null)
                foreach (var Warning in Document.Warnings)
                    this.Report.SourceWarning(Warning.ToStringAlways());

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
            {
                this.Report.CurrentOperationTotal = Document.Operations.Count;
                for (int Index = 0; Index < Document.Operations.Count; Index++)
                {
                    var Operation = Document.Operations[Index];
                    this.Report.CurrentOperationIndex = Index + 1;
                    ApplyOperation(Operation);
                }
            }

            if (this.RepairRecursiveVisuals && !this.IsPreview)
                RepairRecursiveVisualsAfterImport();

            if (!this.IsPreview)
                ApplyQueuedAutoRoutes();

            EmitMissingContainerSkipNotes();
        }

        private string NormalizeLayoutMode(string Mode)
        {
            if (String.IsNullOrWhiteSpace(Mode))
                return "gridNearViewport";

            if (StringEquals(Mode, "gridNearViewport"))
                return "gridNearViewport";

            if (StringEquals(Mode, "gridNearContainer"))
                return "gridNearContainer";

            if (StringEquals(Mode, "gridAfterExistingContent"))
                return "gridAfterExistingContent";

            if (StringEquals(Mode, "none"))
                return "none";

            this.Report.Warn("Unknown importOptions.layoutMode '" + Mode + "'; using gridNearViewport.");
            return "gridNearViewport";
        }

        private void RepairRecursiveVisualsBeforeImport()
        {
            this.Report.Log("JSON import recursive visual repair scan started.");
            var Repairs = CompositeViewIntegrity.RepairRecursiveVisuals(this.Composition,
                Message =>
                {
                    this.Report.Log("JSON import repair: " + Message);
                    this.Report.CountRepairedRecursiveVisual();
                },
                this.IsPreview);

            if (Repairs < 1)
                this.Report.Log("JSON import recursive visual repair scan found no repairs.");
        }

        private void RepairRecursiveVisualsAfterImport()
        {
            this.Report.Log("JSON import post-apply recursive visual repair scan started.");
            var Repairs = CompositeViewIntegrity.RepairRecursiveVisuals(this.Composition,
                Message =>
                {
                    this.Report.Log("JSON import post-apply repair: " + Message);
                    this.Report.CountRepairedRecursiveVisual();
                },
                false);

            if (Repairs < 1)
                this.Report.Log("JSON import post-apply recursive visual repair scan found no repairs.");
        }

        private void ApplyComposition(CompositionJsonComposition Source)
        {
            var Changed = ApplyFormalSet(this.Composition, Source.Name, Source.TechName, Source.Summary, Source.TechSpec, Source.Version);

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
                var Changed = ApplyFormalSet(Existing, Source.Name, Source.TechName, Source.Summary, Source.TechSpec, (CompositionJsonVersion)null);
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
                var Changed = ApplyFormalSet(Existing, Source.Name, Source.TechName, Source.Summary, Source.TechSpec, (CompositionJsonVersion)null);
                CountUpdated(Changed);
                var RepairResult = RepairRelationshipLinks(Existing, BuildRelationshipLinkPlan(Existing.RelationshipDefinitor.Value, Source, "top-level"));
                if (RepairResult.Added > 0)
                    PlanOrQueueAutoRouteForRelationship(Existing, null, true, "relationship links repaired from full-state import");
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

        private Concept CreateConcept(CompositionJsonIdea Source)
        {
            var Definition = FindConceptDefinition(Source.DefinitionId, Source.DefinitionTechName, Source.DefinitionName);
            if (Definition == null)
            {
                Skip("Cannot create concept '" + Source.Name.ToStringAlways() + "' because definition '" + Source.DefinitionTechName.ToStringAlways() + "' was not found.");
                return null;
            }

            var Container = ResolveContainer(Source.ContainerId, Source.ContainerTechName, Definition.OwnerDomain);
            if (Container == null)
            {
                RecordMissingContainerSkip(Source.ContainerId, Source.ContainerTechName);
                Skip("Cannot create concept '" + Source.Name.ToStringAlways() + "' because its container '" +
                     RequestedContainerDescription(Source.ContainerId, Source.ContainerTechName) + "' was not found or is not safe.");
                return null;
            }

            if (String.IsNullOrEmpty(Source.Name))
            {
                Skip("Cannot create concept because name is missing.");
                return null;
            }

            if (this.IsPreview)
            {
                RegisterPlannedConcept(Source, Definition);
                this.Report.CountCreated();
                return null;
            }

            var Concept = new Concept(this.Composition, Definition, Source.Name, Source.TechName.NullDefault(Source.Name.TextToIdentifier()), Source.Summary.NullDefault(""));
            AssignImportedId(Concept, Source.Id);

            if (Definition.IsVersionable)
                Concept.Version = new VersionCard();

            Concept.AddToComposite(Container);
            this.Report.CountCreated();

            ApplyMarkers(Concept, Source.Markers);
            ApplyDetails(Concept, Source.Details);

            return Concept;
        }

        private void RegisterPlannedConcept(CompositionJsonIdea Source, IdeaDefinition Definition)
        {
            if (!this.IsPreview || Source == null || Definition == null)
                return;

            var Planned = new PlannedConceptReference();
            Planned.Id = Source.Id;
            Planned.TechName = Source.TechName.NullDefault(Source.Name.NullDefault("Concept").TextToIdentifier());
            Planned.Definitor = Definition;

            if (!String.IsNullOrEmpty(Planned.Id))
                this.PlannedConceptsById[Planned.Id] = Planned;

            if (!String.IsNullOrEmpty(Planned.TechName))
                this.PlannedConceptsByTechName[Planned.TechName] = Planned;

            this.Report.Log("JSON import preview planned concept endpoint: techName=" +
                            Planned.TechName.ToStringAlways() +
                            ", id=" + Planned.Id.ToStringAlways() +
                            ", definition=" + Definition.TechName.ToStringAlways() + ".");
        }

        private PlannedConceptReference FindPlannedConcept(string Id, string TechName)
        {
            PlannedConceptReference Planned;

            if (!String.IsNullOrEmpty(Id) && this.PlannedConceptsById.TryGetValue(Id, out Planned))
                return Planned;

            if (!String.IsNullOrEmpty(TechName) && this.PlannedConceptsByTechName.TryGetValue(TechName, out Planned))
                return Planned;

            return null;
        }

        private Relationship CreateRelationship(CompositionJsonRelationship Source)
        {
            var Definition = FindRelationshipDefinition(Source.DefinitionId, Source.DefinitionTechName, Source.DefinitionName);
            if (Definition == null)
            {
                Skip("Cannot create relationship '" + Source.Name.ToStringAlways() + "' because definition '" + Source.DefinitionTechName.ToStringAlways() + "' was not found.");
                return null;
            }

            var Container = ResolveContainer(Source.ContainerId, Source.ContainerTechName, Definition.OwnerDomain);
            if (Container == null)
            {
                RecordMissingContainerSkip(Source.ContainerId, Source.ContainerTechName);
                Skip("Cannot create relationship '" + Source.Name.ToStringAlways() + "' because its container '" +
                     RequestedContainerDescription(Source.ContainerId, Source.ContainerTechName) + "' was not found or is not safe.");
                return null;
            }

            var Name = Source.Name.NullDefault(Definition.Name);
            if (String.IsNullOrEmpty(Name))
            {
                Skip("Cannot create relationship because name is missing.");
                return null;
            }

            var LinkPlan = BuildRelationshipLinkPlan(Definition, Source, "top-level");
            if (!ValidateRelationshipLinkPlan(Definition, Source.TechName.NullDefault(Name), LinkPlan))
                return null;

            if (this.IsPreview)
            {
                this.Report.CountCreated();
                return null;
            }

            var Relationship = new Relationship(this.Composition, Definition, Name, Source.TechName.NullDefault(Name.TextToIdentifier()), Source.Summary.NullDefault(""));
            AssignImportedId(Relationship, Source.Id);

            if (Definition.IsVersionable)
                Relationship.Version = new VersionCard();

            Relationship.AddToComposite(Container);
            this.Report.CountCreated();

            ApplyRelationshipLinks(Relationship, LinkPlan);
            ApplyMarkers(Relationship, Source.Markers);
            ApplyDetails(Relationship, Source.Details);

            return Relationship;
        }

        private RelationshipLinkApplyResult RepairRelationshipLinks(Relationship Relationship, RelationshipLinkImportPlan LinkPlan)
        {
            this.Report.Log(FormatOperationPrefix() + "existing relationship matched for repair " + DescribeTarget(Relationship) + ".");
            this.Report.Log(FormatOperationPrefix() + "relationship links source=" +
                            (LinkPlan == null ? "none" : LinkPlan.SourceName.NullDefault("none")) +
                            " for relationship techName=" + Relationship.TechName.ToStringAlways() + ".");
            this.Report.Log(FormatOperationPrefix() + "relationship links before repair: " + DescribeRelationshipLinks(Relationship) + ".");

            var Result = ApplyRelationshipLinks(Relationship, LinkPlan);

            this.Report.Log(FormatOperationPrefix() + "relationship links added=" + Result.Added.ToString(CultureInfo.InvariantCulture) +
                            ", duplicates=" + Result.Duplicate.ToString(CultureInfo.InvariantCulture) +
                            ", unresolved=" + Result.Unresolved.ToString(CultureInfo.InvariantCulture) + ".");
            this.Report.Log(FormatOperationPrefix() + "relationship links after repair: " + DescribeRelationshipLinks(Relationship) + ".");

            if (Result.Added > 0)
            {
                this.Report.CountRepairedRelationship();
                this.Report.CountUpdated();
            }

            return Result;
        }

        private RelationshipLinkApplyResult ApplyRelationshipLinks(Relationship Relationship, RelationshipLinkImportPlan LinkPlan)
        {
            var Result = new RelationshipLinkApplyResult();

            if (LinkPlan == null)
                return Result;

            foreach (var Warning in LinkPlan.Warnings)
                this.Report.Warn(Warning);

            foreach (var Spec in LinkPlan.Specs)
            {
                if ((Spec.ResolvedIdea == null && !Spec.ResolvedFromPreviewPlan) || Spec.ResolvedRole == null)
                {
                    Result.Unresolved++;
                    continue;
                }

                if (this.IsPreview && Spec.ResolvedIdea == null && Spec.ResolvedFromPreviewPlan)
                {
                    Result.Added++;
                    continue;
                }

                if (Relationship.Links.Any(Link => Link.RoleDefinitor == Spec.ResolvedRole && Link.AssociatedIdea == Spec.ResolvedIdea))
                {
                    Result.Duplicate++;
                    continue;
                }

                if (this.IsPreview)
                {
                    Result.Added++;
                    continue;
                }

                var Variant = Spec.ResolvedRole.AllowedVariants.FirstOrDefault();
                if (Variant == null && this.Composition.CompositeContentDomain != null)
                    Variant = this.Composition.CompositeContentDomain.LinkRoleVariants.FirstOrDefault();

                var NewLink = new RoleBasedLink(Relationship, Spec.ResolvedIdea, Spec.ResolvedRole, Variant);
                Relationship.AddLink(NewLink);
                Result.Added++;
            }

            return Result;
        }

        private RelationshipLinkImportPlan BuildRelationshipLinkPlan(RelationshipDefinition Definition, CompositionJsonRelationship Source, string SourceName)
        {
            var Plan = new RelationshipLinkImportPlan();
            Plan.SourceName = SourceName.NullDefault("none");

            if (Source == null)
                return Plan;

            if (Source.Links != null && Source.Links.Count > 0)
                foreach (var Link in Source.Links)
                    AddRelationshipLinkSpec(Plan, Link.RoleType, Link.RoleDefinitionTechName, Link.IdeaId, Link.IdeaTechName);

            AddRelationshipEndpointSpecs(Plan, "Origin", Source.OriginIdeaIds, Source.OriginIdeaTechNames);
            AddRelationshipEndpointSpecs(Plan, "Target", Source.TargetIdeaIds, Source.TargetIdeaTechNames);

            Plan.HasConnectivityInput = Plan.Specs.Count > 0;
            ResolveRelationshipLinkPlan(Definition, Plan);
            return Plan;
        }

        private void AddRelationshipEndpointSpecs(RelationshipLinkImportPlan Plan, string RoleTypeName, IList<string> IdeaIds, IList<string> IdeaTechNames)
        {
            var Count = Math.Max(IdeaIds == null ? 0 : IdeaIds.Count, IdeaTechNames == null ? 0 : IdeaTechNames.Count);
            for (int Index = 0; Index < Count; Index++)
            {
                var IdeaId = IdeaIds != null && Index < IdeaIds.Count ? IdeaIds[Index] : null;
                var IdeaTechName = IdeaTechNames != null && Index < IdeaTechNames.Count ? IdeaTechNames[Index] : null;
                AddRelationshipLinkSpec(Plan, RoleTypeName, null, IdeaId, IdeaTechName);
            }
        }

        private void AddRelationshipLinkSpec(RelationshipLinkImportPlan Plan, string RoleTypeName, string RoleDefinitionTechName, string IdeaId, string IdeaTechName)
        {
            if (String.IsNullOrEmpty(IdeaId) && String.IsNullOrEmpty(IdeaTechName))
                return;

            Plan.Specs.Add(new RelationshipLinkImportSpec
            {
                RoleTypeName = RoleTypeName,
                RoleDefinitionTechName = RoleDefinitionTechName,
                IdeaId = IdeaId,
                IdeaTechName = IdeaTechName
            });
        }

        private void ResolveRelationshipLinkPlan(RelationshipDefinition Definition, RelationshipLinkImportPlan Plan)
        {
            if (Definition == null || Plan == null)
                return;

            foreach (var Spec in Plan.Specs)
            {
                var RequestedRole = ResolveRoleType(Spec.RoleTypeName, Spec.RoleDefinitionTechName, Definition);
                var Role = Definition.GetLinkForRole(RequestedRole);
                if (Role == null)
                {
                    Plan.Warnings.Add("Cannot resolve relationship role '" + Spec.RoleTypeName.ToStringAlways() +
                                      "' for relationship definition '" + Definition.TechName + "'.");
                    continue;
                }

                var Idea = FindIdea(Spec.IdeaId, Spec.IdeaTechName);
                if (Idea == null)
                {
                    var Planned = this.IsPreview ? FindPlannedConcept(Spec.IdeaId, Spec.IdeaTechName) : null;
                    if (Planned == null || Planned.Definitor == null)
                    {
                        Plan.Warnings.Add("Cannot resolve relationship endpoint '" + Describe(Spec.IdeaId, Spec.IdeaTechName) + "'.");
                        continue;
                    }

                    Spec.ResolvedIdeaDefinitor = Planned.Definitor;
                    Spec.ResolvedFromPreviewPlan = true;
                    Spec.ResolvedRole = Role;

                    if (RequestedRole == ERoleType.Target)
                        Plan.ResolvedTargetCount++;
                    else
                        Plan.ResolvedOriginCount++;

                    this.Report.Log(FormatOperationPrefix() + "relationship endpoint '" +
                                    Describe(Spec.IdeaId, Spec.IdeaTechName) +
                                    "' resolved from planned concept map.");
                    continue;
                }

                Spec.ResolvedRole = Role;
                Spec.ResolvedIdea = Idea;
                Spec.ResolvedIdeaDefinitor = Idea.IdeaDefinitor;

                if (RequestedRole == ERoleType.Target)
                    Plan.ResolvedTargetCount++;
                else
                    Plan.ResolvedOriginCount++;
            }
        }

        private ERoleType ResolveRoleType(string RoleTypeName, string RoleDefinitionTechName, RelationshipDefinition Definition)
        {
            if (StringEquals(RoleTypeName, "Target"))
                return ERoleType.Target;

            if (StringEquals(RoleTypeName, "Origin"))
                return ERoleType.Origin;

            if (!String.IsNullOrEmpty(RoleDefinitionTechName) && Definition != null)
            {
                if (Definition.TargetLinkRoleDef != null &&
                    (StringEquals(Definition.TargetLinkRoleDef.TechName, RoleDefinitionTechName) ||
                     StringEquals(Definition.TargetLinkRoleDef.Name, RoleDefinitionTechName)))
                    return ERoleType.Target;

                if (Definition.OriginOrParticipantLinkRoleDef != null &&
                    (StringEquals(Definition.OriginOrParticipantLinkRoleDef.TechName, RoleDefinitionTechName) ||
                     StringEquals(Definition.OriginOrParticipantLinkRoleDef.Name, RoleDefinitionTechName)))
                    return ERoleType.Origin;
            }

            return ERoleType.Origin;
        }

        private bool ValidateRelationshipLinkPlan(RelationshipDefinition Definition, string RelationshipTechName, RelationshipLinkImportPlan LinkPlan)
        {
            this.Report.Log(FormatOperationPrefix() + "relationship links source=" +
                            (LinkPlan == null ? "none" : LinkPlan.SourceName.NullDefault("none")) +
                            " for relationship techName=" + RelationshipTechName.ToStringAlways() + ".");

            if (LinkPlan == null || !LinkPlan.HasConnectivityInput)
            {
                Skip("Skipped relationship '" + RelationshipTechName.ToStringAlways() + "': no valid origin/target links were provided.");
                return false;
            }

            if (LinkPlan.ResolvedOriginCount < 1 || LinkPlan.ResolvedTargetCount < 1)
            {
                foreach (var Warning in LinkPlan.Warnings)
                    this.Report.Warn(Warning);

                Skip("Skipped relationship '" + RelationshipTechName.ToStringAlways() + "': no valid origin/target links were provided.");
                return false;
            }

            var Origins = LinkPlan.Specs.Where(Spec => Spec.ResolvedIdeaDefinitor != null && ResolveRoleType(Spec.RoleTypeName, Spec.RoleDefinitionTechName, Definition) == ERoleType.Origin)
                                        .Select(Spec => Spec.ResolvedIdeaDefinitor)
                                        .ToList();
            var Targets = LinkPlan.Specs.Where(Spec => Spec.ResolvedIdeaDefinitor != null && ResolveRoleType(Spec.RoleTypeName, Spec.RoleDefinitionTechName, Definition) == ERoleType.Target)
                                        .Select(Spec => Spec.ResolvedIdeaDefinitor)
                                        .ToList();

            if (!Origins.SelectMany(Origin => Targets.Select(Target => Definition.CanLink(Origin, Target))).Any(Result => Result.Result))
            {
                Skip("Skipped relationship '" + RelationshipTechName.ToStringAlways() + "': resolved endpoints are not valid for definition '" + Definition.TechName + "'.");
                return false;
            }

            return true;
        }

        private string DescribeRelationshipLinks(Relationship Relationship)
        {
            if (Relationship == null || Relationship.Links == null)
                return "total=0, origins=0, targets=0";

            return "total=" + Relationship.Links.Count.ToString(CultureInfo.InvariantCulture) +
                   ", origins=" + Relationship.Links.Count(Link => Link.RoleDefinitor != null && Link.RoleDefinitor.RoleType == ERoleType.Origin).ToString(CultureInfo.InvariantCulture) +
                   ", targets=" + Relationship.Links.Count(Link => Link.RoleDefinitor != null && Link.RoleDefinitor.RoleType == ERoleType.Target).ToString(CultureInfo.InvariantCulture);
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
                this.Report.CountDeleted();
                return;
            }

            Target.RemoveFromComposite(false, false);
            this.Report.CountDeleted();
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
                    this.Report.CountDeleted();
                    continue;
                }

                var Descriptor = CreateDescriptor(Marker.DescriptorName, Marker.DescriptorTechName, Marker.DescriptorSummary);

                if (Existing == null)
                {
                    if (!this.IsPreview)
                        Idea.Markings.Add(new MarkerAssignment(this.Engine, Definition, Descriptor));
                    this.Report.CountUpdated();
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
                    this.Report.CountUpdated();
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
                this.Report.CountUpdated();
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

            this.Report.CountUpdated();
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
                this.Report.CountUpdated();
                return;
            }

            Existing.TargetLocation = Source.TargetAddress;
            this.Report.CountUpdated();
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
                this.Report.CountUpdated();
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
            this.Report.CountDeleted();
        }

        private void ApplyView(CompositionJsonView Source)
        {
            var Existing = FindView(Source.Id, Source.TechName);
            if (Existing == null)
            {
                Skip("View '" + Describe(Source.Id, Source.TechName) + "' was not found. Creating views from JSON is not supported yet.");
                return;
            }

            CountUpdated(ApplyFormalSet(Existing, Source.Name, Source.TechName, Source.Summary, null, (CompositionJsonVersion)null));

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
                this.Report.CountUpdated();
                return;
            }

            var Symbol = Representation.MainSymbol;
            var Width = Source.Width == null ? Symbol.BaseWidth : Source.Width.Value;
            var Height = Source.Height == null ? Symbol.BaseHeight : Source.Height.Value;
            Symbol.ResizeTo(Width, Height);

            var X = Source.X == null ? Symbol.BaseLeft : Source.X.Value;
            var Y = Source.Y == null ? Symbol.BaseTop : Source.Y.Value;
            Symbol.MoveTo(X + Symbol.BaseWidth / 2.0, Y + Symbol.BaseHeight / 2.0, true);

            this.Report.CountUpdated();
        }

        private void ApplyOperation(CompositionJsonOperation Operation)
        {
            this.LastOperationOutcome = null;
            var Summary = DescribeOperation(Operation);
            this.Report.CurrentOperationSummary = Summary;
            this.Report.Log(FormatOperationPrefix() + Summary + " -> " + (this.IsPreview ? "plan start" : "apply start"));

            var BeforeUpdated = this.Report.Updated;
            var BeforeCreated = this.Report.Created;
            var BeforeDeleted = this.Report.Deleted;
            var BeforeSkipped = this.Report.Skipped;
            var BeforeVisualsPlaced = this.IsPreview ? this.Report.PlannedVisualsPlaced : this.Report.AppliedVisualsPlaced;
            var BeforeVisualsSkipped = this.IsPreview ? this.Report.PlannedVisualsSkipped : this.Report.AppliedVisualsSkipped;
            var BeforeAutoFit = this.IsPreview ? this.Report.PlannedAutoFitConcepts : this.Report.AppliedAutoFitConcepts;
            var BeforeAutoFitSkipped = this.Report.SkippedAutoFitConcepts;
            var BeforeAutoRoute = this.IsPreview ? this.Report.PlannedAutoRouteLinks : this.Report.AppliedAutoRouteLinks;
            var BeforeAutoRouteSkipped = this.Report.SkippedAutoRouteLinks;

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
                        if (Op == "place")
                            ApplyPlaceOperation(Entity, Operation);
                        else
                            Skip("Unsupported operation op '" + Operation.Op.ToStringAlways() + "'.");

            if (this.LastOperationOutcome == null)
                this.LastOperationOutcome = InferOperationOutcome(BeforeUpdated, BeforeCreated, BeforeDeleted, BeforeSkipped,
                                                                  BeforeVisualsPlaced, BeforeVisualsSkipped,
                                                                  BeforeAutoFit, BeforeAutoFitSkipped,
                                                                  BeforeAutoRoute, BeforeAutoRouteSkipped);

            this.Report.Log(FormatOperationPrefix() + Summary + " -> " + this.LastOperationOutcome);
        }

        private void ApplyUpdateOperation(string Entity, CompositionJsonOperation Operation)
        {
            if (Entity == "composition")
            {
                var Changed = ApplySetToFormal(this.Composition, Operation.Set);
                CountUpdated(Changed);
                SetOperationOutcome((Changed ? Verb("update") : "no editable changes needed") + " matched " + DescribeTarget(this.Composition));
                return;
            }

            if (Entity == "concept")
            {
                var Concept = FindConcept(Operation.Id, Operation.TechName);
                if (Concept == null)
                {
                    SetOperationOutcome("skipped: no matching concept by id or techName");
                    Skip("Cannot update concept '" + Describe(Operation.Id, Operation.TechName) + "' because it was not found.");
                    return;
                }

                var Changed = ApplySetToFormal(Concept, Operation.Set);
                CountUpdated(Changed);
                var AutoFitChanged = AutoFitExistingConceptIfRequested(Concept, Operation, "operation autoFit=true");
                SetOperationOutcome((Changed ? Verb("update") : (AutoFitChanged ? Verb("concept auto-fit") : "no editable changes needed")) +
                                    " matched " + DescribeTarget(Concept));
                return;
            }

            if (Entity == "relationship")
            {
                var Relationship = FindRelationship(Operation.Id, Operation.TechName);
                if (Relationship == null)
                {
                    SetOperationOutcome("skipped: no matching relationship by id or techName");
                    Skip("Cannot update relationship '" + Describe(Operation.Id, Operation.TechName) + "' because it was not found.");
                    return;
                }

                var Changed = ApplySetToFormal(Relationship, Operation.Set);
                CountUpdated(Changed);
                var AutoRouteQueued = PlanOrQueueAutoRouteForRelationship(Relationship, Operation, false, "operation autoRoute=true update relationship");
                SetOperationOutcome((Changed ? Verb("update") : (AutoRouteQueued ? Verb("link auto-route") : "no editable changes needed")) +
                                    " matched " + DescribeTarget(Relationship));
                return;
            }

            if (Entity == "view")
            {
                var View = FindView(Operation.Id, Operation.TechName);
                if (View == null)
                {
                    SetOperationOutcome("skipped: no matching view by id or techName");
                    Skip("Cannot update view '" + Describe(Operation.Id, Operation.TechName) + "' because it was not found.");
                    return;
                }

                var Changed = ApplySetToFormal(View, Operation.Set);
                CountUpdated(Changed);
                SetOperationOutcome((Changed ? Verb("update") : "no editable changes needed") + " matched " + DescribeTarget(View));
                return;
            }

            Skip("Update operation for entity '" + Entity + "' is not supported.");
        }

        private void ApplyCreateOperation(string Entity, CompositionJsonOperation Operation)
        {
            if (Entity == "concept")
            {
                var SourceTechName = GetSetString(Operation.Set, "techName").NullDefault(Operation.TechName);
                var Existing = FindConcept(Operation.Id, SourceTechName);
                if (Existing != null)
                {
                    SetOperationOutcome("skipped: matching concept already exists " + DescribeTarget(Existing));
                    Skip("Cannot create concept '" + Describe(Operation.Id, SourceTechName) + "' because a matching concept already exists. Use op:update to edit it.");
                    if (ShouldPlaceCreatedItem(Operation))
                        PlaceIdeaVisual(Existing, Operation, false);
                    else
                        AutoFitExistingConceptIfRequested(Existing, Operation, "matching existing concept autoFit=true");
                    return;
                }

                var Source = new CompositionJsonIdea();
                Source.IsNew = true;
                Source.Id = Operation.Id;
                Source.TechName = SourceTechName;
                Source.Name = GetSetString(Operation.Set, "name");
                Source.Summary = GetSetString(Operation.Set, "summary");
                Source.DefinitionTechName = Operation.DefinitionTechName.NullDefault(GetSetString(Operation.Set, "definitionTechName"));
                Source.ContainerId = Operation.ContainerId.NullDefault(GetSetString(Operation.Set, "containerId"));
                Source.ContainerTechName = Operation.ContainerTechName.NullDefault(GetSetString(Operation.Set, "containerTechName"));
                var BeforeCreated = this.Report.Created;
                var Created = CreateConcept(Source);
                if (this.IsPreview && this.Report.Created > BeforeCreated)
                    PlanCreatedConceptVisual(Source, Operation);
                else
                    if (Created != null)
                    {
                        PlaceIdeaVisual(Created, Operation, false);
                        if (!ShouldPlaceCreatedItem(Operation))
                            SkipAutoFitForConcept(Created.TechName, null, "no visual representation was created for auto-fit", IsAutoFitExplicitlyEnabled(Operation));
                    }
                return;
            }

            if (Entity == "relationship")
            {
                var SourceTechName = GetSetString(Operation.Set, "techName").NullDefault(Operation.TechName);
                var Source = new CompositionJsonRelationship();
                Source.IsNew = true;
                Source.Id = Operation.Id;
                Source.TechName = SourceTechName;
                Source.Name = GetSetString(Operation.Set, "name");
                Source.Summary = GetSetString(Operation.Set, "summary");
                Source.DefinitionTechName = Operation.DefinitionTechName.NullDefault(GetSetString(Operation.Set, "definitionTechName"));
                Source.ContainerId = Operation.ContainerId.NullDefault(GetSetString(Operation.Set, "containerId"));
                Source.ContainerTechName = Operation.ContainerTechName.NullDefault(GetSetString(Operation.Set, "containerTechName"));
                var LinkSourceName = PopulateRelationshipConnectivityFromOperation(Source, Operation);

                var Existing = FindRelationship(Operation.Id, SourceTechName);
                if (Existing != null)
                {
                    this.Report.Log(FormatOperationPrefix() + "create relationship matched existing target; applying as repair/upsert.");
                    var Changed = ApplySetToFormal(Existing, Operation.Set);
                    CountUpdated(Changed);
                    var LinkPlan = BuildRelationshipLinkPlan(Existing.RelationshipDefinitor.Value, Source, LinkSourceName);
                    var RepairResult = RepairRelationshipLinks(Existing, LinkPlan);
                    if (RepairResult.Added > 0)
                        PlanOrQueueAutoRouteForRelationship(Existing, Operation, true, "relationship links repaired");
                    if (ShouldPlaceCreatedItem(Operation))
                        PlaceIdeaVisual(Existing, Operation, false);
                    SetOperationOutcome(Verb("repair") + " matched " + DescribeTarget(Existing));
                    return;
                }

                var BeforeCreated = this.Report.Created;
                var Created = CreateRelationship(Source);
                if (this.IsPreview && this.Report.Created > BeforeCreated)
                    PlanCreatedRelationshipVisual(Source, Operation);
                else
                    if (Created != null)
                        PlaceIdeaVisual(Created, Operation, false);
                return;
            }

            Skip("Create operation for entity '" + Entity + "' is not supported.");
        }

        private void ApplyDeleteOperation(string Entity, CompositionJsonOperation Operation)
        {
            if (Entity == "concept")
            {
                var Concept = FindConcept(Operation.Id, Operation.TechName);
                if (Concept != null)
                    SetOperationOutcome(Verb("delete") + " matched " + DescribeTarget(Concept));
                else
                    SetOperationOutcome("skipped: no matching concept by id or techName");
                DeleteIdea(Concept, "concept", Operation.Id, Operation.TechName);
                return;
            }

            if (Entity == "relationship")
            {
                var Relationship = FindRelationship(Operation.Id, Operation.TechName);
                if (Relationship != null)
                    SetOperationOutcome(Verb("delete") + " matched " + DescribeTarget(Relationship));
                else
                    SetOperationOutcome("skipped: no matching relationship by id or techName");
                DeleteIdea(Relationship, "relationship", Operation.Id, Operation.TechName);
                return;
            }

            Skip("Delete operation for entity '" + Entity + "' is not supported.");
        }

        private void ApplyPlaceOperation(string Entity, CompositionJsonOperation Operation)
        {
            if (Entity == "concept")
            {
                var Concept = FindConcept(Operation.Id, Operation.TechName.NullDefault(GetSetString(Operation.Set, "techName")));
                if (Concept == null)
                {
                    SkipPlaceOperation("Cannot place concept '" + Describe(Operation.Id, Operation.TechName) + "' because it was not found.");
                    return;
                }

                PlaceIdeaVisual(Concept, Operation, true);
                return;
            }

            if (Entity == "relationship")
            {
                var Relationship = FindRelationship(Operation.Id, Operation.TechName.NullDefault(GetSetString(Operation.Set, "techName")));
                if (Relationship == null)
                {
                    SkipPlaceOperation("Cannot place relationship '" + Describe(Operation.Id, Operation.TechName) + "' because it was not found.");
                    return;
                }

                PlaceIdeaVisual(Relationship, Operation, true);
                return;
            }

            Skip("Place operation for entity '" + Entity + "' is not supported.");
        }

        private string PopulateRelationshipConnectivityFromOperation(CompositionJsonRelationship Source, CompositionJsonOperation Operation)
        {
            var HasTopLevel = HasRelationshipConnectivity(Operation.OriginIdeaIds, Operation.OriginIdeaTechNames,
                                                          Operation.TargetIdeaIds, Operation.TargetIdeaTechNames,
                                                          Operation.Links);
            if (HasTopLevel)
            {
                Source.OriginIdeaIds = Operation.OriginIdeaIds ?? new List<string>();
                Source.OriginIdeaTechNames = Operation.OriginIdeaTechNames ?? new List<string>();
                Source.TargetIdeaIds = Operation.TargetIdeaIds ?? new List<string>();
                Source.TargetIdeaTechNames = Operation.TargetIdeaTechNames ?? new List<string>();
                Source.Links = Operation.Links ?? new List<CompositionJsonRelationshipLink>();
                return "top-level";
            }

            if (HasSetRelationshipConnectivity(Operation.Set))
            {
                Source.OriginIdeaIds = GetSetStringList(Operation.Set, "originIdeaIds");
                Source.OriginIdeaTechNames = GetSetStringList(Operation.Set, "originIdeaTechNames");
                Source.TargetIdeaIds = GetSetStringList(Operation.Set, "targetIdeaIds");
                Source.TargetIdeaTechNames = GetSetStringList(Operation.Set, "targetIdeaTechNames");
                Source.Links = GetSetRelationshipLinks(Operation.Set, "links");
                return "set";
            }

            Source.OriginIdeaIds = new List<string>();
            Source.OriginIdeaTechNames = new List<string>();
            Source.TargetIdeaIds = new List<string>();
            Source.TargetIdeaTechNames = new List<string>();
            Source.Links = new List<CompositionJsonRelationshipLink>();
            return "none";
        }

        private bool HasRelationshipConnectivity(IList<string> OriginIds, IList<string> OriginTechNames,
                                                 IList<string> TargetIds, IList<string> TargetTechNames,
                                                 IList<CompositionJsonRelationshipLink> Links)
        {
            return (OriginIds != null && OriginIds.Count > 0) ||
                   (OriginTechNames != null && OriginTechNames.Count > 0) ||
                   (TargetIds != null && TargetIds.Count > 0) ||
                   (TargetTechNames != null && TargetTechNames.Count > 0) ||
                   (Links != null && Links.Count > 0);
        }

        private bool HasSetRelationshipConnectivity(IDictionary<string, object> Set)
        {
            return GetSetStringList(Set, "originIdeaIds").Count > 0 ||
                   GetSetStringList(Set, "originIdeaTechNames").Count > 0 ||
                   GetSetStringList(Set, "targetIdeaIds").Count > 0 ||
                   GetSetStringList(Set, "targetIdeaTechNames").Count > 0 ||
                   GetSetRelationshipLinks(Set, "links").Count > 0;
        }

        private bool RelationshipSourceReferencesIdea(CompositionJsonRelationship Source, Idea Idea)
        {
            if (Source == null || Idea == null)
                return false;

            var IdeaId = Idea.GlobalId.ToString("D");
            var IdeaTechName = Idea.TechName;

            if ((Source.OriginIdeaIds != null && Source.OriginIdeaIds.Any(Id => StringEquals(Id, IdeaId))) ||
                (Source.TargetIdeaIds != null && Source.TargetIdeaIds.Any(Id => StringEquals(Id, IdeaId))) ||
                (Source.OriginIdeaTechNames != null && Source.OriginIdeaTechNames.Any(TechName => StringEquals(TechName, IdeaTechName))) ||
                (Source.TargetIdeaTechNames != null && Source.TargetIdeaTechNames.Any(TechName => StringEquals(TechName, IdeaTechName))))
                return true;

            return Source.Links != null &&
                   Source.Links.Any(Link => StringEquals(Link.IdeaId, IdeaId) ||
                                            StringEquals(Link.IdeaTechName, IdeaTechName));
        }

        private void PlanCreatedConceptVisual(CompositionJsonIdea Source, CompositionJsonOperation Operation)
        {
            if (!ShouldPlaceCreatedItem(Operation))
            {
                SkipAutoFitForConcept(Source.TechName, null, "no visual representation is planned for auto-fit", IsAutoFitExplicitlyEnabled(Operation));
                return;
            }

            var Definition = FindConceptDefinition(Source.DefinitionId, Source.DefinitionTechName, Source.DefinitionName);
            var Container = Definition == null ? null : ResolveContainer(Source.ContainerId, Source.ContainerTechName, Definition.OwnerDomain);
            if (Container == null)
            {
                SkipVisualPlacement("Cannot plan visual placement for concept '" + Source.TechName.ToStringAlways() + "' because its container could not be resolved.");
                return;
            }

            string Reason;
            var View = ResolvePlacementView(Container, Operation, true, out Reason);
            if (View == null)
            {
                SkipVisualPlacement(Reason);
                return;
            }

            var Width = GetOperationDouble(Operation, "width") ?? GetConceptDefaultWidth(Definition);
            var Height = GetOperationDouble(Operation, "height") ?? GetConceptDefaultHeight(Definition);
            var Center = ResolvePlacementCenter(View, Operation, Width, Height, null);
            CountAndLogVisualPlaced("planned", "concept", Source.TechName, View, Center, Width, Height);
            PlanAutoFitForConcept(Source.TechName, View, Operation, true, "new concept visual");
        }

        private void PlanCreatedRelationshipVisual(CompositionJsonRelationship Source, CompositionJsonOperation Operation)
        {
            if (!ShouldPlaceCreatedItem(Operation))
                return;

            var Definition = FindRelationshipDefinition(Source.DefinitionId, Source.DefinitionTechName, Source.DefinitionName);
            var Container = Definition == null ? null : ResolveContainer(Source.ContainerId, Source.ContainerTechName, Definition.OwnerDomain);
            if (Container == null)
            {
                SkipVisualPlacement("Cannot plan visual placement for relationship '" + Source.TechName.ToStringAlways() + "' because its container could not be resolved.");
                return;
            }

            string Reason;
            var View = ResolvePlacementView(Container, Operation, true, out Reason);
            if (View == null)
            {
                SkipVisualPlacement(Reason);
                return;
            }

            if (this.PreventSelfRecursiveCompositeViews && RelationshipSourceReferencesIdea(Source, View.OwnerCompositeContainer))
            {
                SkipVisualPlacement("Cannot plan visual placement for relationship '" + Source.TechName.ToStringAlways() +
                                    "' in " + DescribeView(View) +
                                    " because an endpoint is the owner of that composite view.");
                return;
            }

            var Width = GetOperationDouble(Operation, "width") ?? GetRelationshipDefaultWidth(Definition);
            var Height = GetOperationDouble(Operation, "height") ?? GetRelationshipDefaultHeight(Definition);
            var Center = ResolveRelationshipPlacementCenter(null, View, Operation, Width, Height, null);
            CountAndLogVisualPlaced("planned", "relationship", Source.TechName, View, Center, Width, Height);
            PlanOrQueueAutoRoute(Source.TechName, null, View, Operation, true, "planned new relationship visual");
        }

        private void PlaceIdeaVisual(Idea Idea, CompositionJsonOperation Operation, bool IsExplicitPlaceOperation)
        {
            if (Idea == null)
                return;

            if (!IsExplicitPlaceOperation && !ShouldPlaceCreatedItem(Operation))
                return;

            string Reason;
            var View = ResolvePlacementView(Idea.OwnerContainer, Operation, IsExplicitPlaceOperation || ShouldPlaceCreatedItem(Operation), out Reason);
            if (View == null)
            {
                if (IsExplicitPlaceOperation)
                    SkipPlaceOperation(Reason);
                else
                    SkipVisualPlacement(Reason);
                return;
            }

            var Concept = Idea as Concept;
            if (Concept != null)
            {
                PlaceConceptVisual(Concept, View, Operation, IsExplicitPlaceOperation);
                return;
            }

            var Relationship = Idea as Relationship;
            if (Relationship != null)
            {
                PlaceRelationshipVisual(Relationship, View, Operation, IsExplicitPlaceOperation);
                return;
            }

            SkipVisualPlacement("Cannot place idea '" + Idea.TechName.ToStringAlways() + "' because its type is not supported for JSON visual placement.");
        }

        private void PlaceConceptVisual(Concept Concept, View View, CompositionJsonOperation Operation, bool IsExplicitPlaceOperation)
        {
            string RecursiveWarning;
            if (this.PreventSelfRecursiveCompositeViews &&
                CompositeViewIntegrity.IsSelfRecursiveConceptPlacement(Concept, View, out RecursiveWarning))
            {
                if (IsExplicitPlaceOperation)
                    SkipPlaceOperation(RecursiveWarning);
                else
                    SkipVisualPlacement(RecursiveWarning);
                return;
            }

            var Existing = Concept.VisualRepresentators.OfType<ConceptVisualRepresentation>()
                                  .FirstOrDefault(Representation => Representation.DisplayingView == View);
            var ExistingSymbol = Existing == null ? null : Existing.MainSymbol;

            var Width = GetOperationDouble(Operation, "width") ?? (ExistingSymbol == null ? GetConceptDefaultWidth(Concept.ConceptDefinitor.Value) : ExistingSymbol.BaseWidth);
            var Height = GetOperationDouble(Operation, "height") ?? (ExistingSymbol == null ? GetConceptDefaultHeight(Concept.ConceptDefinitor.Value) : ExistingSymbol.BaseHeight);
            var Center = ResolvePlacementCenter(View, Operation, Width, Height, ExistingSymbol);

            if (this.IsPreview)
            {
                if (Existing == null || HasExplicitGeometry(Operation) || IsExplicitPlaceOperation)
                {
                    CountAndLogVisualPlaced("planned", "concept", Concept.TechName, View, Center, Width, Height);
                    PlanAutoFitForConcept(Concept.TechName, View, Operation, Existing == null, Existing == null ? "new concept visual" : "explicit concept placement/update");
                }
                else
                {
                    PlanAutoFitForConcept(Concept.TechName, View, Operation, false, "existing concept visual");
                    this.Report.Log(FormatOperationPrefix() + "concept '" + Concept.TechName + "' is already visible in " + DescribeView(View) + ".");
                }
                return;
            }

            var Changed = false;
            var CreatedVisualRepresentation = false;
            ConceptVisualRepresentation TargetRepresentation = Existing;
            if (TargetRepresentation == null)
            {
                var AsShortcut = Concept.OwnerContainer != View.OwnerCompositeContainer;
                TargetRepresentation = ConceptCreationCommand.CreateConceptVisualRepresentation(Concept, View, Center, AsShortcut, true, Width, Height);
                Changed = true;
                CreatedVisualRepresentation = true;
            }
            else
            {
                if (HasExplicitGeometry(Operation))
                {
                    TargetRepresentation.MainSymbol.ResizeTo(Width, Height);
                    TargetRepresentation.MainSymbol.MoveTo(Center.X, Center.Y, true);
                    Changed = true;
                }
            }

            if (Changed)
            {
                EnsureRepresentationViewChildren(TargetRepresentation);
                var Symbol = TargetRepresentation.MainSymbol;
                CountAndLogVisualPlaced("applied", "concept", Concept.TechName, View, Symbol.BaseCenter, Symbol.BaseWidth, Symbol.BaseHeight);
                AutoFitPlacedConceptIfNeeded(Concept, Symbol, Operation, CreatedVisualRepresentation, CreatedVisualRepresentation ? "new concept visual" : "explicit concept placement/update");
                View.UpdateVersion();
            }
            else
                if (EnsureRepresentationViewChildren(TargetRepresentation))
                {
                    var Symbol = TargetRepresentation.MainSymbol;
                    CountAndLogVisualPlaced("applied", "concept", Concept.TechName, View, Symbol.BaseCenter, Symbol.BaseWidth, Symbol.BaseHeight);
                    AutoFitPlacedConceptIfNeeded(Concept, Symbol, Operation, true, "restored concept view child");
                    View.UpdateVersion();
                }
            else
            {
                AutoFitPlacedConceptIfNeeded(Concept, TargetRepresentation == null ? null : TargetRepresentation.MainSymbol, Operation, false, "existing concept visual");
                this.Report.Log(FormatOperationPrefix() + "concept '" + Concept.TechName + "' is already visible in " + DescribeView(View) + ".");
            }
        }

        private void PlaceRelationshipVisual(Relationship Relationship, View View, CompositionJsonOperation Operation, bool IsExplicitPlaceOperation)
        {
            string RecursiveWarning;
            if (this.PreventSelfRecursiveCompositeViews &&
                CompositeViewIntegrity.IsSelfRecursiveRelationshipPlacement(Relationship, View, out RecursiveWarning))
            {
                if (IsExplicitPlaceOperation)
                    SkipPlaceOperation(RecursiveWarning);
                else
                    SkipVisualPlacement(RecursiveWarning);
                return;
            }

            var Existing = Relationship.VisualRepresentators.OfType<RelationshipVisualRepresentation>()
                                      .FirstOrDefault(Representation => Representation.DisplayingView == View);
            var ExistingSymbol = Existing == null ? null : Existing.MainSymbol;

            this.Report.Log(FormatOperationPrefix() + "relationship endpoints for '" + Relationship.TechName +
                            "' target view=" + DescribeView(View) + ": " + DescribeRelationshipEndpointStatus(Relationship, View) + ".");

            if (Relationship.Links == null || Relationship.Links.Count < 1)
            {
                var Reason = "Cannot place relationship '" + Relationship.TechName + "' in " + DescribeView(View) +
                             " because the relationship has no resolved links.";
                if (IsExplicitPlaceOperation)
                    SkipPlaceOperation(Reason);
                else
                    SkipVisualPlacement(Reason);
                return;
            }

            var MissingEndpoints = EnsureRelationshipEndpointSymbols(Relationship, View, Operation);
            if (MissingEndpoints.Count > 0)
            {
                var Reason = "Cannot place relationship '" + Relationship.TechName + "' in " + DescribeView(View) +
                             " because linked endpoints are not visible in that view: " + String.Join(", ", MissingEndpoints.ToArray()) + ".";
                if (IsExplicitPlaceOperation)
                    SkipPlaceOperation(Reason);
                else
                    SkipVisualPlacement(Reason);
                return;
            }

            var Width = GetOperationDouble(Operation, "width") ?? (ExistingSymbol == null ? GetRelationshipDefaultWidth(Relationship.RelationshipDefinitor.Value) : ExistingSymbol.BaseWidth);
            var Height = GetOperationDouble(Operation, "height") ?? (ExistingSymbol == null ? GetRelationshipDefaultHeight(Relationship.RelationshipDefinitor.Value) : ExistingSymbol.BaseHeight);
            var Center = ResolveRelationshipPlacementCenter(Relationship, View, Operation, Width, Height, ExistingSymbol);

            if (this.IsPreview)
            {
                if (Existing == null || HasExplicitGeometry(Operation) || IsExplicitPlaceOperation)
                {
                    CountAndLogVisualPlaced("planned", "relationship", Relationship.TechName, View, Center, Width, Height);
                    PlanOrQueueAutoRoute(Relationship.TechName, Existing, View, Operation, true,
                                         Existing == null ? "planned new relationship visual" : "planned explicit relationship placement/update");
                }
                else
                {
                    PlanOrQueueAutoRoute(Relationship.TechName, Existing, View, Operation, false, "existing relationship visual");
                    this.Report.Log(FormatOperationPrefix() + "relationship '" + Relationship.TechName + "' is already visible in " + DescribeView(View) + ".");
                }
                return;
            }

            var Changed = false;
            RelationshipVisualRepresentation TargetRepresentation = Existing;
            if (TargetRepresentation == null)
            {
                var AsShortcut = Relationship.OwnerContainer != View.OwnerCompositeContainer;
                TargetRepresentation = RelationshipCreationCommand.CreateRelationshipVisualRepresentation(Relationship, View, Center, AsShortcut);
                Changed = true;
            }

            if (HasExplicitGeometry(Operation) && TargetRepresentation.MainSymbol != null)
            {
                TargetRepresentation.MainSymbol.ResizeTo(Width, Height);
                TargetRepresentation.MainSymbol.MoveTo(Center.X, Center.Y, true);
                Changed = true;
            }

            var ConnectorsAdded = EnsureRelationshipVisualConnectors(TargetRepresentation, View);
            if (ConnectorsAdded > 0)
                Changed = true;

            if (Changed)
            {
                TargetRepresentation.Render();
                EnsureRepresentationViewChildren(TargetRepresentation);
                var Symbol = TargetRepresentation.MainSymbol;
                CountAndLogVisualPlaced("applied", "relationship", Relationship.TechName, View, Symbol.BaseCenter, Symbol.BaseWidth, Symbol.BaseHeight);
                PlanOrQueueAutoRoute(Relationship.TechName, TargetRepresentation, View, Operation, true,
                                     Existing == null ? "new relationship visual" :
                                     (ConnectorsAdded > 0 ? "relationship connectors added" : "explicit relationship placement/update"));
                View.UpdateVersion();
            }
            else
                if (EnsureRepresentationViewChildren(TargetRepresentation))
                {
                    TargetRepresentation.Render();
                    var Symbol = TargetRepresentation.MainSymbol;
                    CountAndLogVisualPlaced("applied", "relationship", Relationship.TechName, View, Symbol.BaseCenter, Symbol.BaseWidth, Symbol.BaseHeight);
                    PlanOrQueueAutoRoute(Relationship.TechName, TargetRepresentation, View, Operation, true, "restored relationship view child");
                    View.UpdateVersion();
                }
            else
            {
                PlanOrQueueAutoRoute(Relationship.TechName, TargetRepresentation, View, Operation, false, "existing relationship visual");
                this.Report.Log(FormatOperationPrefix() + "relationship '" + Relationship.TechName + "' is already visible in " + DescribeView(View) + ".");
            }
        }

        private int EnsureRelationshipVisualConnectors(RelationshipVisualRepresentation Representation, View View)
        {
            var Added = 0;
            foreach (var Link in Representation.RepresentedRelationship.Links)
            {
                if (Representation.VisualConnectors.Any(Connector => Connector.RepresentedLink == Link))
                    continue;

                var EndpointRepresentation = Link.AssociatedIdea.VisualRepresentators
                                                 .FirstOrDefault(Visual => Visual.DisplayingView == View && Visual.MainSymbol != null);
                if (EndpointRepresentation == null)
                {
                    this.Report.Warn("Relationship '" + Representation.RepresentedRelationship.TechName + "' connector for linked idea '" +
                                     Link.AssociatedIdea.TechName + "' was not shown because the linked idea is not visible in " + DescribeView(View) + ".");
                    continue;
                }

                VisualConnector NewConnector;
                if (Link.RoleDefinitor.RoleType == ERoleType.Target)
                    NewConnector = new VisualConnector(Representation, Link, Representation.MainSymbol, EndpointRepresentation.MainSymbol,
                                                       Representation.MainSymbol.BaseCenter, EndpointRepresentation.MainSymbol.BaseCenter);
                else
                    NewConnector = new VisualConnector(Representation, Link, EndpointRepresentation.MainSymbol, Representation.MainSymbol,
                                                       EndpointRepresentation.MainSymbol.BaseCenter, Representation.MainSymbol.BaseCenter);

                Representation.AddVisualPart(NewConnector);
                Added++;
            }

            return Added;
        }

        private List<string> EnsureRelationshipEndpointSymbols(Relationship Relationship, View View, CompositionJsonOperation Operation)
        {
            var Missing = new List<string>();

            foreach (var Link in Relationship.Links)
            {
                var Endpoint = Link.AssociatedIdea;
                if (Endpoint == null)
                    continue;

                var EndpointRepresentation = Endpoint.VisualRepresentators
                                                    .FirstOrDefault(Visual => Visual.DisplayingView == View && Visual.MainSymbol != null);
                if (EndpointRepresentation != null)
                    continue;

                if (this.AutoPlaceNewItems && Endpoint is Concept)
                {
                    string RecursiveWarning;
                    if (this.PreventSelfRecursiveCompositeViews &&
                        CompositeViewIntegrity.IsSelfRecursiveConceptPlacement((Concept)Endpoint, View, out RecursiveWarning))
                    {
                        this.Report.Warn(RecursiveWarning);
                        Missing.Add(Endpoint.TechName.ToStringAlways() + " (self-recursive endpoint)");
                        continue;
                    }

                    this.Report.Log(FormatOperationPrefix() + "auto-placing relationship endpoint '" + Endpoint.TechName +
                                    "' into " + DescribeView(View) + ".");
                    var EndpointOperation = new CompositionJsonOperation();
                    EndpointOperation.ViewId = View.GlobalId.ToString("D");
                    EndpointOperation.AutoPlace = true;
                    PlaceConceptVisual((Concept)Endpoint, View, EndpointOperation, false);

                    if (this.IsPreview)
                        continue;

                    EndpointRepresentation = Endpoint.VisualRepresentators
                                                    .FirstOrDefault(Visual => Visual.DisplayingView == View && Visual.MainSymbol != null);
                    if (EndpointRepresentation != null)
                        continue;
                }

                Missing.Add(Endpoint.TechName.ToStringAlways());
            }

            return Missing;
        }

        private string DescribeRelationshipEndpointStatus(Relationship Relationship, View View)
        {
            if (Relationship.Links == null || Relationship.Links.Count < 1)
                return "no resolved links";

            var Parts = new List<string>();
            foreach (var Link in Relationship.Links)
            {
                var Role = Link.RoleDefinitor == null ? "?" : Link.RoleDefinitor.RoleType.GetFieldName();
                var Idea = Link.AssociatedIdea;
                var Visible = Idea != null && Idea.VisualRepresentators.Any(Visual => Visual.DisplayingView == View && Visual.MainSymbol != null);
                Parts.Add(Role + ":" + (Idea == null ? "<none>" : Idea.TechName) + "=" + (Visible ? "visible" : "missing"));
            }

            return String.Join("; ", Parts.ToArray());
        }

        private bool EnsureRepresentationViewChildren(VisualRepresentation Representation)
        {
            if (Representation == null || Representation.DisplayingView == null)
                return false;

            var Changed = false;
            foreach (var Part in Representation.VisualParts.OrderBy(Part => !(Part is VisualSymbol)))
                if (!Representation.DisplayingView.ViewChildren.Any(Child => Child != null && Child.Key == Part))
                {
                    Representation.DisplayingView.ViewChildren.Add(ViewChild.Create(Part, Part.Graphic));
                    Changed = true;
                }

            if (Changed)
                MarkAffectedView(Representation.DisplayingView, Representation.MainSymbol);

            return Changed;
        }

        private bool ShouldPlaceCreatedItem(CompositionJsonOperation Operation)
        {
            if (Operation == null)
                return false;

            if (HasExplicitPlacement(Operation))
                return true;

            if (StringEquals(this.LayoutMode, "none"))
                return false;

            var AutoPlace = Operation.AutoPlace ?? GetSetBool(Operation.Set, "autoPlace");
            return AutoPlace == null ? this.AutoPlaceNewItems : AutoPlace.Value;
        }

        private bool HasExplicitPlacement(CompositionJsonOperation Operation)
        {
            return Operation != null &&
                   (!String.IsNullOrEmpty(Operation.ViewId) ||
                    !String.IsNullOrEmpty(Operation.ViewTechName) ||
                    !String.IsNullOrEmpty(GetSetString(Operation.Set, "viewId")) ||
                    !String.IsNullOrEmpty(GetSetString(Operation.Set, "viewTechName")) ||
                    HasExplicitGeometry(Operation));
        }

        private bool HasExplicitGeometry(CompositionJsonOperation Operation)
        {
            return Operation != null &&
                   (GetOperationDouble(Operation, "x") != null ||
                    GetOperationDouble(Operation, "y") != null ||
                    GetOperationDouble(Operation, "width") != null ||
                    GetOperationDouble(Operation, "height") != null);
        }

        private View ResolvePlacementView(Idea Container, CompositionJsonOperation Operation, bool AllowAuto, out string Reason)
        {
            Reason = null;
            var ViewId = Operation == null ? null : Operation.ViewId.NullDefault(GetSetString(Operation.Set, "viewId"));
            var ViewTechName = Operation == null ? null : Operation.ViewTechName.NullDefault(GetSetString(Operation.Set, "viewTechName"));

            if (!String.IsNullOrEmpty(ViewId) || !String.IsNullOrEmpty(ViewTechName))
            {
                var ExplicitView = FindView(ViewId, ViewTechName);
                if (ExplicitView == null)
                    Reason = "Cannot resolve requested placement view '" + Describe(ViewId, ViewTechName) + "'.";
                return ExplicitView;
            }

            if (!AllowAuto)
                return null;

            if (Container != null)
            {
                var ContainerView = Container.CompositeActiveView ?? Container.CompositeViews.FirstOrDefault();
                if (ContainerView != null)
                    return ContainerView;
            }

            if (this.Engine.CurrentView != null)
                return this.Engine.CurrentView;

            Reason = "Cannot place visual because no explicit view, container composite view, or active view is available.";
            return null;
        }

        private Point ResolvePlacementCenter(View View, CompositionJsonOperation Operation, double Width, double Height, VisualSymbol ExistingSymbol)
        {
            var X = GetOperationDouble(Operation, "x");
            var Y = GetOperationDouble(Operation, "y");
            if (X != null || Y != null)
            {
                var Area = ExistingSymbol == null ? GetNextAutoPlacementArea(View, Width, Height) : ExistingSymbol.BaseArea;
                var Left = X == null ? Area.Left : X.Value;
                var Top = Y == null ? Area.Top : Y.Value;
                return new Point(Left + Width / 2.0, Top + Height / 2.0);
            }

            if (ExistingSymbol != null)
                return ExistingSymbol.BaseCenter;

            var AutoArea = GetNextAutoPlacementArea(View, Width, Height);
            return new Point(AutoArea.Left + AutoArea.Width / 2.0, AutoArea.Top + AutoArea.Height / 2.0);
        }

        private Point ResolveRelationshipPlacementCenter(Relationship Relationship, View View, CompositionJsonOperation Operation, double Width, double Height, VisualSymbol ExistingSymbol)
        {
            if (GetOperationDouble(Operation, "x") != null || GetOperationDouble(Operation, "y") != null || ExistingSymbol != null)
                return ResolvePlacementCenter(View, Operation, Width, Height, ExistingSymbol);

            if (Relationship != null)
            {
                var EndpointSymbols = GetVisibleRelationshipEndpointSymbols(Relationship, View).ToList();
                if (EndpointSymbols.Count > 0)
                    return new Point(EndpointSymbols.Average(Symbol => Symbol.BaseCenter.X),
                                     EndpointSymbols.Average(Symbol => Symbol.BaseCenter.Y));
            }

            return ResolvePlacementCenter(View, Operation, Width, Height, ExistingSymbol);
        }

        private IEnumerable<VisualSymbol> GetVisibleRelationshipEndpointSymbols(Relationship Relationship, View View)
        {
            if (Relationship == null || View == null)
                yield break;

            foreach (var Link in Relationship.Links)
            {
                var Representation = Link.AssociatedIdea.VisualRepresentators
                                        .FirstOrDefault(Visual => Visual.DisplayingView == View && Visual.MainSymbol != null);
                if (Representation != null)
                    yield return Representation.MainSymbol;
            }
        }

        private Rect GetNextAutoPlacementArea(View View, double Width, double Height)
        {
            int Index;
            if (!this.AutoPlacementIndexes.TryGetValue(View, out Index))
                Index = 0;
            this.AutoPlacementIndexes[View] = Index + 1;

            var SpacingX = Math.Max(Width, 180.0) + 60.0;
            var SpacingY = Math.Max(Height, 80.0) + 50.0;
            Point Origin;
            if (!this.AutoPlacementOrigins.TryGetValue(View, out Origin))
            {
                int IgnoredOutliers;
                Origin = CalculateAutoPlacementOrigin(View, Width, Height, SpacingX, SpacingY, out IgnoredOutliers);
                this.AutoPlacementOrigins[View] = Origin;
                this.AutoPlacementIgnoredOutliers[View] = IgnoredOutliers;
            }

            var Column = Index % 4;
            var Row = Index / 4;

            this.Report.Log("JSON import layout: target view='" + View.TechName.ToStringAlways() +
                            "', mode='" + this.LayoutMode +
                            "', origin=(" + Origin.X.ToString("0.###", CultureInfo.InvariantCulture) +
                            "," + Origin.Y.ToString("0.###", CultureInfo.InvariantCulture) +
                            "), ignoredOutliers=" + this.AutoPlacementIgnoredOutliers[View] +
                            ", placed=" + (Index + 1).ToString(CultureInfo.InvariantCulture) + ".");

            return new Rect(Origin.X + Column * SpacingX, Origin.Y + Row * SpacingY, Width, Height);
        }

        private Point CalculateAutoPlacementOrigin(View View, double Width, double Height, double SpacingX, double SpacingY, out int IgnoredOutliers)
        {
            IgnoredOutliers = 0;

            Rect Bounds;
            if (StringEquals(this.LayoutMode, "gridAfterExistingContent"))
            {
                Bounds = GetExistingVisualBounds(View, out IgnoredOutliers);
                return Bounds.IsEmpty ? new Point(100.0, 100.0) : new Point(Bounds.Right + 80.0, Bounds.Top);
            }

            if (StringEquals(this.LayoutMode, "gridNearViewport") &&
                View != null && View.HostingScrollViewer != null &&
                View.HostingScrollViewer.IsLoaded &&
                !View.HostingScrollViewer.ViewportWidth.IsNan() &&
                !View.HostingScrollViewer.ViewportHeight.IsNan() &&
                View.HostingScrollViewer.ViewportWidth > 0 &&
                View.HostingScrollViewer.ViewportHeight > 0)
            {
                var Center = View.CurrentPresentationCenter;
                return new Point(Center.X - (SpacingX * 1.5), Center.Y - SpacingY);
            }

            Bounds = GetExistingVisualBounds(View, out IgnoredOutliers);
            if (Bounds.IsEmpty)
                return new Point(100.0, 100.0);

            if (StringEquals(this.LayoutMode, "gridNearContainer"))
                return new Point(Bounds.Left + 80.0, Bounds.Top + 80.0);

            return new Point(Bounds.Left, Bounds.Bottom + 80.0);
        }

        private Rect GetExistingVisualBounds(View View, out int IgnoredOutliers)
        {
            IgnoredOutliers = 0;
            var Symbols = this.Composition.DeclaredIdeas
                              .SelectMany(Idea => Idea.VisualRepresentators)
                              .Where(Representation => Representation.DisplayingView == View && Representation.MainSymbol != null)
                              .Select(Representation => Representation.MainSymbol.BaseArea)
                              .Where(Area => IsUsableLayoutArea(Area))
                              .ToList();

            if (Symbols.Count < 1)
                return Rect.Empty;

            var MedianX = Median(Symbols.Select(Area => Area.Left + Area.Width / 2.0).ToList());
            var MedianY = Median(Symbols.Select(Area => Area.Top + Area.Height / 2.0).ToList());
            var Cluster = Symbols.Where(Area =>
            {
                var CenterX = Area.Left + Area.Width / 2.0;
                var CenterY = Area.Top + Area.Height / 2.0;
                return Math.Abs(CenterX - MedianX) <= 2500.0 &&
                       Math.Abs(CenterY - MedianY) <= 1800.0 &&
                       Math.Abs(CenterX) <= 5000.0 &&
                       Math.Abs(CenterY) <= 4000.0;
            }).ToList();

            IgnoredOutliers = Symbols.Count - Cluster.Count;

            if (Cluster.Count < 1)
            {
                IgnoredOutliers = Symbols.Count;
                return Rect.Empty;
            }

            var Bounds = Cluster[0];
            foreach (var Symbol in Cluster.Skip(1))
                Bounds.Union(Symbol);

            return Bounds;
        }

        private bool IsUsableLayoutArea(Rect Area)
        {
            return !Area.IsEmpty &&
                   !Area.Left.IsNan() &&
                   !Area.Top.IsNan() &&
                   !Area.Width.IsNan() &&
                   !Area.Height.IsNan() &&
                   Area.Width > 0 &&
                   Area.Height > 0;
        }

        private double Median(List<double> Values)
        {
            if (Values == null || Values.Count < 1)
                return 0.0;

            Values.Sort();
            var Middle = Values.Count / 2;
            if (Values.Count % 2 == 1)
                return Values[Middle];

            return (Values[Middle - 1] + Values[Middle]) / 2.0;
        }

        private double GetConceptDefaultWidth(ConceptDefinition Definition)
        {
            return Definition.DefaultSymbolFormat.InitialWidth.SubstituteFor(0, ProductDirector.DefaultConceptBodySymbolSize.Width);
        }

        private double GetConceptDefaultHeight(ConceptDefinition Definition)
        {
            return Definition.DefaultSymbolFormat.InitialHeight.SubstituteFor(0, ProductDirector.DefaultConceptBodySymbolSize.Height);
        }

        private double GetRelationshipDefaultWidth(RelationshipDefinition Definition)
        {
            return Definition.DefaultSymbolFormat.InitialWidth.SubstituteFor(0, ProductDirector.DefaultRelationshipCentralSymbolSize.Width);
        }

        private double GetRelationshipDefaultHeight(RelationshipDefinition Definition)
        {
            return Definition.DefaultSymbolFormat.InitialHeight.SubstituteFor(0, ProductDirector.DefaultRelationshipCentralSymbolSize.Height);
        }

        private void CountAndLogVisualPlaced(string Phase, string Entity, string TechName, View View, Point Center, double Width, double Height)
        {
            this.Report.CountVisualPlaced();
            if (!this.IsPreview && StringEquals(Phase, "applied"))
                MarkAffectedView(View, null);
            this.Report.Log(FormatOperationPrefix() + Phase + " visual placement " + Entity +
                            " techName=" + TechName.ToStringAlways() +
                            " view=" + DescribeView(View) +
                            " x=" + (Center.X - Width / 2.0).ToString("0.###", CultureInfo.InvariantCulture) +
                            " y=" + (Center.Y - Height / 2.0).ToString("0.###", CultureInfo.InvariantCulture) +
                            " width=" + Width.ToString("0.###", CultureInfo.InvariantCulture) +
                            " height=" + Height.ToString("0.###", CultureInfo.InvariantCulture) + ".");
        }

        private bool AutoFitExistingConceptIfRequested(Concept Concept, CompositionJsonOperation Operation, string Reason)
        {
            if (!IsAutoFitExplicitlyEnabled(Operation))
                return false;

            var Symbols = Concept == null
                          ? new List<VisualSymbol>()
                          : Concept.VisualRepresentators.OfType<ConceptVisualRepresentation>()
                                   .Where(Representation => Representation.MainSymbol != null)
                                   .Select(Representation => Representation.MainSymbol)
                                   .ToList();

            if (Symbols.Count < 1)
            {
                SkipAutoFitForConcept(Concept == null ? null : Concept.TechName, null,
                                      "autoFit=true was requested, but the concept has no visual symbols", true);
                return false;
            }

            var Changed = false;
            foreach (var Symbol in Symbols)
            {
                if (this.IsPreview)
                {
                    PlanAutoFitForConcept(Concept.TechName, Symbol.GetDisplayingView(), Operation, false, Reason);
                    Changed = true;
                }
                else
                    Changed = AutoFitConceptSymbol(Concept, Symbol, Reason) || Changed;
            }

            return Changed;
        }

        private void PlanAutoFitForConcept(string TechName, View View, CompositionJsonOperation Operation, bool CreatedNewVisual, string Reason)
        {
            var AutoFit = GetOperationAutoFit(Operation);
            if (AutoFit != null && !AutoFit.Value)
            {
                SkipAutoFitForConcept(TechName, View, "operation autoFit=false", true);
                return;
            }

            if (!CreatedNewVisual && AutoFit != true)
                return;

            if (AutoFit == true || this.AutoFitPlacedConcepts)
            {
                this.Report.CountAutoFitConcept();
                this.Report.Log(FormatOperationPrefix() + "planned concept auto-fit techName=" + TechName.ToStringAlways() +
                                " view=" + DescribeView(View) +
                                " reason=" + Reason.ToStringAlways() + ".");
            }
            else
                SkipAutoFitForConcept(TechName, View, "importOptions.autoFitPlacedConcepts=false", true);
        }

        private void AutoFitPlacedConceptIfNeeded(Concept Concept, VisualSymbol Symbol, CompositionJsonOperation Operation, bool CreatedNewVisual, string Reason)
        {
            var AutoFit = GetOperationAutoFit(Operation);
            if (AutoFit != null && !AutoFit.Value)
            {
                SkipAutoFitForConcept(Concept == null ? null : Concept.TechName,
                                      Symbol == null ? null : Symbol.GetDisplayingView(),
                                      "operation autoFit=false", true);
                return;
            }

            if (!CreatedNewVisual && AutoFit != true)
                return;

            if (!(AutoFit == true || this.AutoFitPlacedConcepts))
            {
                SkipAutoFitForConcept(Concept == null ? null : Concept.TechName,
                                      Symbol == null ? null : Symbol.GetDisplayingView(),
                                      "importOptions.autoFitPlacedConcepts=false", true);
                return;
            }

            if (Symbol == null)
            {
                SkipAutoFitForConcept(Concept == null ? null : Concept.TechName, null,
                                      "no visual symbol was available for auto-fit", true);
                return;
            }

            AutoFitConceptSymbol(Concept, Symbol, Reason);
        }

        private bool AutoFitConceptSymbol(Concept Concept, VisualSymbol Symbol, string Reason)
        {
            if (Symbol == null)
            {
                SkipAutoFitForConcept(Concept == null ? null : Concept.TechName, null,
                                      "no visual symbol was available for auto-fit", true);
                return false;
            }

            var View = Symbol.GetDisplayingView();
            var OldWidth = Symbol.BaseWidth;
            this.Report.Log(FormatOperationPrefix() + "applying concept auto-fit techName=" +
                            (Concept == null ? "<none>" : Concept.TechName.ToStringAlways()) +
                            " view=" + DescribeView(View) +
                            " oldWidth=" + OldWidth.ToString("0.###", CultureInfo.InvariantCulture) +
                            " reason=" + Reason.ToStringAlways() + ".");

            var Result = ConceptAutoFitService.FitSingleConceptWidth(this.Engine, Symbol, "JSON import " + Reason.ToStringAlways());
            var NewWidth = Symbol.BaseWidth;

            if (Result.SymbolsFitted > 0)
            {
                this.Report.CountAutoFitConcept();
                MarkAffectedView(View, Symbol);
                this.Report.Log(FormatOperationPrefix() + "applied concept auto-fit techName=" +
                                (Concept == null ? "<none>" : Concept.TechName.ToStringAlways()) +
                                " view=" + DescribeView(View) +
                                " width " + OldWidth.ToString("0.###", CultureInfo.InvariantCulture) +
                                " -> " + NewWidth.ToString("0.###", CultureInfo.InvariantCulture) + ".");
                return true;
            }

            if (Result.SymbolsSkipped > 0)
            {
                this.Report.CountAutoFitConceptSkipped();
                this.Report.Log(FormatOperationPrefix() + "skipped concept auto-fit techName=" +
                                (Concept == null ? "<none>" : Concept.TechName.ToStringAlways()) +
                                " view=" + DescribeView(View) +
                                " width=" + OldWidth.ToString("0.###", CultureInfo.InvariantCulture) + ".");
            }

            foreach (var Warning in Result.Warnings)
                this.Report.Warn("Auto-fit warning for concept '" +
                                 (Concept == null ? "<none>" : Concept.TechName.ToStringAlways()) +
                                 "': " + Warning);

            return false;
        }

        private bool? GetOperationAutoFit(CompositionJsonOperation Operation)
        {
            if (Operation == null)
                return null;

            return Operation.AutoFit ?? GetSetBool(Operation.Set, "autoFit");
        }

        private bool IsAutoFitExplicitlyEnabled(CompositionJsonOperation Operation)
        {
            var AutoFit = GetOperationAutoFit(Operation);
            return AutoFit != null && AutoFit.Value;
        }

        private void SkipAutoFitForConcept(string TechName, View View, string Reason, bool Count)
        {
            if (Count)
                this.Report.CountAutoFitConceptSkipped();

            this.Report.Log(FormatOperationPrefix() + "skipped concept auto-fit techName=" + TechName.ToStringAlways() +
                            " view=" + DescribeView(View) +
                            " reason=" + Reason.ToStringAlways() + ".");
        }

        private bool? GetOperationAutoRoute(CompositionJsonOperation Operation)
        {
            if (Operation == null)
                return null;

            return Operation.AutoRoute ?? GetSetBool(Operation.Set, "autoRoute");
        }

        private bool IsAutoRouteExplicitlyEnabled(CompositionJsonOperation Operation)
        {
            var AutoRoute = GetOperationAutoRoute(Operation);
            return AutoRoute != null && AutoRoute.Value;
        }

        private bool PlanOrQueueAutoRouteForRelationship(Relationship Relationship, CompositionJsonOperation Operation,
                                                         bool TouchedByImport, string Reason)
        {
            var Representations = Relationship == null
                                  ? new List<RelationshipVisualRepresentation>()
                                  : Relationship.VisualRepresentators.OfType<RelationshipVisualRepresentation>()
                                                .Where(Representation => Representation.DisplayingView != null &&
                                                                         Representation.MainSymbol != null)
                                                .ToList();

            if (Representations.Count < 1)
            {
                if (IsAutoRouteExplicitlyEnabled(Operation))
                    SkipAutoRouteForRelationship(Relationship == null ? null : Relationship.TechName, null,
                                                 "autoRoute=true was requested, but the relationship has no visible relationship representation", true);
                return false;
            }

            var Queued = false;
            foreach (var Representation in Representations)
                Queued = PlanOrQueueAutoRoute(Relationship.TechName, Representation, Representation.DisplayingView,
                                              Operation, TouchedByImport, Reason) || Queued;

            return Queued;
        }

        private bool PlanOrQueueAutoRoute(string RelationshipTechName, RelationshipVisualRepresentation Representation, View View,
                                          CompositionJsonOperation Operation, bool TouchedByImport, string Reason)
        {
            var AutoRoute = GetOperationAutoRoute(Operation);
            this.Report.Log(FormatOperationPrefix() + "auto-route check relationship techName=" + RelationshipTechName.ToStringAlways() +
                            " view=" + DescribeView(View) +
                            " touchedByImport=" + (TouchedByImport ? "true" : "false") +
                            " operationAutoRoute=" + (AutoRoute == null ? "<default>" : (AutoRoute.Value ? "true" : "false")) +
                            " importOptions.autoRoutePlacedLinks=" + (this.AutoRoutePlacedLinks ? "true" : "false") +
                            " reason=" + Reason.ToStringAlways() + ".");

            if (AutoRoute != null && !AutoRoute.Value)
            {
                SkipAutoRouteForRelationship(RelationshipTechName, View, "operation autoRoute=false", true);
                return false;
            }

            if (!TouchedByImport && AutoRoute != true)
                return false;

            if (!(AutoRoute == true || this.AutoRoutePlacedLinks))
            {
                SkipAutoRouteForRelationship(RelationshipTechName, View, "importOptions.autoRoutePlacedLinks=false", true);
                return false;
            }

            if (View == null)
            {
                SkipAutoRouteForRelationship(RelationshipTechName, null, "no target view was available for auto-route", true);
                return false;
            }

            var Key = GetAutoRouteKey(RelationshipTechName, Representation, View);
            if (this.IsPreview)
            {
                if (!this.PlannedAutoRouteKeys.Add(Key))
                    return false;

                this.Report.CountAutoRouteLink();
                this.Report.Log(FormatOperationPrefix() + "planned link auto-route relationship techName=" +
                                RelationshipTechName.ToStringAlways() +
                                " view=" + DescribeView(View) +
                                " reason=" + Reason.ToStringAlways() + ".");
                return true;
            }

            if (Representation == null)
            {
                SkipAutoRouteForRelationship(RelationshipTechName, View, "no relationship visual representation was available for auto-route", true);
                return false;
            }

            if (!this.PendingAutoRouteKeys.Add(Key))
                return false;

            List<RelationshipVisualRepresentation> Representations;
            if (!this.PendingAutoRouteRelationships.TryGetValue(View, out Representations))
            {
                Representations = new List<RelationshipVisualRepresentation>();
                this.PendingAutoRouteRelationships[View] = Representations;
            }

            Representations.Add(Representation);
            this.Report.Log(FormatOperationPrefix() + "queued link auto-route relationship techName=" +
                            RelationshipTechName.ToStringAlways() +
                            " view=" + DescribeView(View) +
                            " reason=" + Reason.ToStringAlways() + ".");
            return true;
        }

        private void ApplyQueuedAutoRoutes()
        {
            if (this.PendingAutoRouteRelationships.Count < 1)
                return;

            this.Report.Log("JSON import auto-route applying queued relationship routes; views=" +
                            this.PendingAutoRouteRelationships.Count.ToString(CultureInfo.InvariantCulture) + ".");

            foreach (var Pair in this.PendingAutoRouteRelationships.ToList())
            {
                var View = Pair.Key;
                var Representations = Pair.Value.Where(Representation => Representation != null &&
                                                                         Representation.DisplayingView == View)
                                                .Distinct()
                                                .ToList();
                if (View == null || Representations.Count < 1)
                    continue;

                var Selection = Representations.SelectMany(Representation => Representation.VisualConnectors)
                                               .Where(Connector => Connector != null)
                                               .Cast<VisualObject>()
                                               .ToList();
                if (Selection.Count < 1)
                {
                    foreach (var Representation in Representations)
                        SkipAutoRouteForRelationship(Representation.RepresentedRelationship == null ? null : Representation.RepresentedRelationship.TechName,
                                                     View, "relationship representation has no visual connectors", true);
                    continue;
                }

                this.Report.Log("JSON import auto-route start view=" + DescribeView(View) +
                                " relationships=" + Representations.Count.ToString(CultureInfo.InvariantCulture) +
                                " connectors=" + Selection.Count.ToString(CultureInfo.InvariantCulture) + ".");

                var Context = LayoutSelectionContext.FromViewSelection(this.Engine, View, Selection);
                var Options = new LinkObstacleRoutingOptions();
                Options.RouteSelectedConnectorsOnly = true;
                var Result = LinkObstacleRoutingService.RouteVisibleConnectors(Context, Options);

                var Routed = Result.Routed + Result.Straightened + Result.DoglegRouted;
                for (int Index = 0; Index < Routed; Index++)
                    this.Report.CountAutoRouteLink();

                for (int Index = 0; Index < Result.Skipped; Index++)
                    this.Report.CountAutoRouteLinkSkipped();

                for (int Index = 0; Index < Result.DoglegRouted; Index++)
                    this.Report.CountDoglegRoutedLink();

                foreach (var Warning in Result.Warnings)
                    this.Report.Warn("Auto-route warning: " + Warning);

                if (Result.HasMutations)
                {
                    MarkAffectedView(View, null);
                    View.UpdateVersion();
                }

                this.Report.Log("JSON import auto-route completed view=" + DescribeView(View) +
                                "; routed=" + Result.Routed.ToString(CultureInfo.InvariantCulture) +
                                ", dogleg routed=" + Result.DoglegRouted.ToString(CultureInfo.InvariantCulture) +
                                ", straightened=" + Result.Straightened.ToString(CultureInfo.InvariantCulture) +
                                ", unchanged=" + Result.Unchanged.ToString(CultureInfo.InvariantCulture) +
                                ", skipped=" + Result.Skipped.ToString(CultureInfo.InvariantCulture) + ".");
            }
        }

        private void SkipAutoRouteForRelationship(string TechName, View View, string Reason, bool Count)
        {
            if (Count)
                this.Report.CountAutoRouteLinkSkipped();

            this.Report.Log(FormatOperationPrefix() + "skipped link auto-route relationship techName=" + TechName.ToStringAlways() +
                            " view=" + DescribeView(View) +
                            " reason=" + Reason.ToStringAlways() + ".");
        }

        private string GetAutoRouteKey(string RelationshipTechName, RelationshipVisualRepresentation Representation, View View)
        {
            var Relationship = Representation == null ? null : Representation.RepresentedRelationship;
            return (View == null ? "<no-view>" : View.GlobalId.ToString("D")) + "|" +
                   (Relationship == null ? RelationshipTechName.ToStringAlways() : Relationship.GlobalId.ToString("D"));
        }

        private void MarkAffectedView(View View, VisualObject ImportedObject)
        {
            if (View == null)
                return;

            if (!this.AffectedViews.Contains(View))
                this.AffectedViews.Add(View);

            this.Report.AddAffectedView(View.Name.ToStringAlways() + " (" + View.TechName.ToStringAlways() + ")");

            if (ImportedObject == null)
                return;

            List<VisualObject> Objects;
            if (!this.ImportedVisualObjects.TryGetValue(View, out Objects))
            {
                Objects = new List<VisualObject>();
                this.ImportedVisualObjects[View] = Objects;
            }

            if (!Objects.Contains(ImportedObject))
                Objects.Add(ImportedObject);
        }

        private void SkipVisualPlacement(string Warning)
        {
            this.Report.CountVisualSkipped();
            this.Report.Warn(Warning);
        }

        private void SkipPlaceOperation(string Warning)
        {
            this.Report.CountSkipped();
            this.Report.CountVisualSkipped();
            this.Report.SkippedMessage(Warning);
            SetOperationOutcome("skipped: " + Warning);
        }

        private string DescribeView(View View)
        {
            if (View == null)
                return "<none>";

            return "name='" + View.Name.ToStringAlways() +
                   "' techName='" + View.TechName.ToStringAlways() +
                   "' id=" + View.GlobalId.ToString("D");
        }

        private bool ApplySetToFormal(FormalElement Target, IDictionary<string, object> Set)
        {
            if (Target == null || Set == null || Set.Count < 1)
                return false;

            var Name = GetSetString(Set, "name");
            var TechName = GetSetString(Set, "techName");
            var Summary = GetSetString(Set, "summary");
            var TechSpec = GetSetString(Set, "techSpec");
            var VersionAnnotation = GetSetString(Set, "versionAnnotation");
            var VersionNumber = GetSetString(Set, "versionNumber");

            return ApplyFormalSet(Target, Name, TechName, Summary, TechSpec, VersionAnnotation, VersionNumber);
        }

        private bool ApplyFormalSet(FormalElement Target, string Name, string TechName, string Summary, string TechSpec, CompositionJsonVersion Version)
        {
            return ApplyFormalSet(Target, Name, TechName, Summary, TechSpec, Version == null ? null : Version.Annotation, Version == null ? null : Version.VersionNumber);
        }

        private bool ApplyFormalSet(FormalElement Target, string Name, string TechName, string Summary, string TechSpec, string VersionAnnotation, string VersionNumber = null)
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

            if (TechSpec != null && Target.TechSpec != TechSpec)
            {
                if (!this.IsPreview)
                    Target.TechSpec = TechSpec;

                this.Report.Log("JSON import " + (this.IsPreview ? "planned" : "applied") + " techSpec for " + DescribeTarget(Target));
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

            if (StringEquals(PropertyTechName, FormalElement.__TechSpec.TechName))
            {
                if (!this.IsPreview)
                    Idea.TechSpec = Value;
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

            if (Container == null && this.UseActiveCompositionAsContainer &&
                CanFallbackToActiveCompositionContainer(ContainerId, ContainerTechName))
            {
                Container = this.Composition;
                this.Report.Log("JSON import container fallback: requested='" +
                                RequestedContainerDescription(ContainerId, ContainerTechName) +
                                "' not found; using active composition '" +
                                this.Composition.TechName.ToStringAlways() + "'.");
            }

            if (Container == null)
                return null;

            if (Container.IdeaDefinitor == null || Container.IdeaDefinitor.CompositeContentDomain == null)
                return null;

            if (ExpectedDomain != null && Container.CompositeContentDomain != null &&
                Container.CompositeContentDomain.GlobalId != ExpectedDomain.GlobalId)
                return null;

            return Container;
        }

        private bool CanFallbackToActiveCompositionContainer(string ContainerId, string ContainerTechName)
        {
            if (!String.IsNullOrWhiteSpace(ContainerId) && IsUsableGuid(ContainerId))
                return false;

            if (String.IsNullOrWhiteSpace(ContainerTechName))
                return true;

            var Normalized = ContainerTechName.Trim().ToLowerInvariant();
            return Normalized.StartsWith("test__", StringComparison.OrdinalIgnoreCase) ||
                   Normalized.StartsWith("replace-with", StringComparison.OrdinalIgnoreCase) ||
                   Normalized.Contains("root-composition") ||
                   Normalized.Contains("active-composition") ||
                   Normalized.Contains("composition-root") ||
                   Normalized == "composition" ||
                   Normalized == "composition1";
        }

        private static bool IsUsableGuid(string Id)
        {
            Guid Parsed;
            return !String.IsNullOrWhiteSpace(Id) &&
                   Guid.TryParse(Id, out Parsed) &&
                   Parsed != Guid.Empty;
        }

        private void RecordMissingContainerSkip(string ContainerId, string ContainerTechName)
        {
            var Key = RequestedContainerDescription(ContainerId, ContainerTechName);
            if (!this.MissingContainerSkipCounts.ContainsKey(Key))
                this.MissingContainerSkipCounts[Key] = 0;
            this.MissingContainerSkipCounts[Key]++;
        }

        private void EmitMissingContainerSkipNotes()
        {
            foreach (var Pair in this.MissingContainerSkipCounts.OrderBy(Item => Item.Key))
            {
                if (Pair.Value < 2)
                    continue;

                this.Report.Note(Pair.Value.ToString(CultureInfo.InvariantCulture) +
                                 " operations skipped because container '" + Pair.Key +
                                 "' was not found. Create/open the matching test composition or enable importOptions.useActiveCompositionAsContainer for root-level fixture imports.");
            }
        }

        private static string RequestedContainerDescription(string ContainerId, string ContainerTechName)
        {
            if (!String.IsNullOrWhiteSpace(ContainerTechName))
                return ContainerTechName;

            if (!String.IsNullOrWhiteSpace(ContainerId))
                return ContainerId;

            return "<active composition>";
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

        private double? GetOperationDouble(CompositionJsonOperation Operation, string Key)
        {
            if (Operation == null)
                return null;

            double? Direct = null;
            if (Key == "x")
                Direct = Operation.X;
            else
                if (Key == "y")
                    Direct = Operation.Y;
                else
                    if (Key == "width")
                        Direct = Operation.Width;
                    else
                        if (Key == "height")
                            Direct = Operation.Height;

            return Direct ?? GetSetDouble(Operation.Set, Key);
        }

        private double? GetSetDouble(IDictionary<string, object> Set, string Key)
        {
            return CompositionJsonSerializer.GetNullableDouble(Set, Key);
        }

        private bool? GetSetBool(IDictionary<string, object> Set, string Key)
        {
            return CompositionJsonSerializer.GetNullableBool(Set, Key);
        }

        private List<string> GetSetStringList(IDictionary<string, object> Set, string Key)
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

        private List<CompositionJsonRelationshipLink> GetSetRelationshipLinks(IDictionary<string, object> Set, string Key)
        {
            var Result = new List<CompositionJsonRelationshipLink>();
            if (Set == null || !Set.ContainsKey(Key) || Set[Key] == null)
                return Result;

            var Single = Set[Key] as IDictionary<string, object>;
            if (Single != null)
            {
                Result.Add(ReadRelationshipLinkFromDictionary(Single));
                return Result;
            }

            var Items = Set[Key] as System.Collections.IEnumerable;
            if (Items == null || Set[Key] is string)
                return Result;

            foreach (var Item in Items)
            {
                var Dictionary = Item as IDictionary<string, object>;
                if (Dictionary != null)
                    Result.Add(ReadRelationshipLinkFromDictionary(Dictionary));
            }

            return Result;
        }

        private CompositionJsonRelationshipLink ReadRelationshipLinkFromDictionary(IDictionary<string, object> Source)
        {
            var Link = new CompositionJsonRelationshipLink();
            Link.Id = CompositionJsonSerializer.GetString(Source, "id");
            Link.RoleType = CompositionJsonSerializer.GetString(Source, "roleType");
            Link.RoleDefinitionId = CompositionJsonSerializer.GetString(Source, "roleDefinitionId");
            Link.RoleDefinitionTechName = CompositionJsonSerializer.GetString(Source, "roleDefinitionTechName");
            Link.RoleDefinitionName = CompositionJsonSerializer.GetString(Source, "roleDefinitionName");
            Link.RoleVariantTechName = CompositionJsonSerializer.GetString(Source, "roleVariantTechName");
            Link.RoleVariantName = CompositionJsonSerializer.GetString(Source, "roleVariantName");
            Link.IdeaId = CompositionJsonSerializer.GetString(Source, "ideaId");
            Link.IdeaTechName = CompositionJsonSerializer.GetString(Source, "ideaTechName");
            return Link;
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
                this.Report.CountUpdated();
        }

        private void Skip(string Warning)
        {
            if (this.LastOperationOutcome == null && this.Report.CurrentOperationIndex > 0)
                this.LastOperationOutcome = "skipped: " + Warning;

            this.Report.CountSkipped();
            this.Report.SkippedMessage(Warning);
        }

        private void SetOperationOutcome(string Outcome)
        {
            if (!String.IsNullOrEmpty(Outcome))
                this.LastOperationOutcome = Outcome;
        }

        private string InferOperationOutcome(int BeforeUpdated, int BeforeCreated, int BeforeDeleted, int BeforeSkipped,
                                             int BeforeVisualsPlaced, int BeforeVisualsSkipped,
                                             int BeforeAutoFit, int BeforeAutoFitSkipped,
                                             int BeforeAutoRoute, int BeforeAutoRouteSkipped)
        {
            if (this.Report.Skipped > BeforeSkipped)
                return "skipped";

            var VisualsPlaced = this.IsPreview ? this.Report.PlannedVisualsPlaced : this.Report.AppliedVisualsPlaced;
            if (VisualsPlaced > BeforeVisualsPlaced)
                return Verb("visual placement");

            var VisualsSkipped = this.IsPreview ? this.Report.PlannedVisualsSkipped : this.Report.AppliedVisualsSkipped;
            if (VisualsSkipped > BeforeVisualsSkipped)
                return "visual placement skipped";

            var AutoFits = this.IsPreview ? this.Report.PlannedAutoFitConcepts : this.Report.AppliedAutoFitConcepts;
            if (AutoFits > BeforeAutoFit)
                return Verb("concept auto-fit");

            if (this.Report.SkippedAutoFitConcepts > BeforeAutoFitSkipped)
                return "concept auto-fit skipped";

            var AutoRoutes = this.IsPreview ? this.Report.PlannedAutoRouteLinks : this.Report.AppliedAutoRouteLinks;
            if (AutoRoutes > BeforeAutoRoute)
                return Verb("link auto-route");

            if (this.Report.SkippedAutoRouteLinks > BeforeAutoRouteSkipped)
                return "link auto-route skipped";

            if (this.Report.Created > BeforeCreated)
                return Verb("create");

            if (this.Report.Deleted > BeforeDeleted)
                return Verb("delete");

            if (this.Report.Updated > BeforeUpdated)
                return Verb("update");

            return "no editable changes needed";
        }

        private string Verb(string Action)
        {
            return (this.IsPreview ? "planned " : "applied ") + Action;
        }

        private string FormatOperationPrefix()
        {
            if (this.Report.CurrentOperationTotal > 0)
                return "JSON import [" + this.Report.CurrentOperationIndex.ToString(CultureInfo.InvariantCulture) +
                       "/" + this.Report.CurrentOperationTotal.ToString(CultureInfo.InvariantCulture) + "] ";

            return "JSON import ";
        }

        private string DescribeOperation(CompositionJsonOperation Operation)
        {
            var Parts = new List<string>();
            Parts.Add(Operation.Op.ToStringAlways().NullDefault("?"));
            Parts.Add(Operation.Entity.ToStringAlways().NullDefault("?"));

            if (!String.IsNullOrEmpty(Operation.Id))
                Parts.Add("id=" + Operation.Id);

            if (!String.IsNullOrEmpty(Operation.TechName))
                Parts.Add("techName=" + Operation.TechName);

            var SetTechName = GetSetString(Operation.Set, "techName");
            if (String.IsNullOrEmpty(Operation.TechName) && !String.IsNullOrEmpty(SetTechName))
                Parts.Add("set.techName=" + SetTechName);

            if (!String.IsNullOrEmpty(Operation.DefinitionTechName))
                Parts.Add("definition=" + Operation.DefinitionTechName);

            if (!String.IsNullOrEmpty(Operation.ContainerId))
                Parts.Add("containerId=" + Operation.ContainerId);

            if (!String.IsNullOrEmpty(Operation.ContainerTechName))
                Parts.Add("container=" + Operation.ContainerTechName);

            if (!String.IsNullOrEmpty(Operation.ViewId))
                Parts.Add("viewId=" + Operation.ViewId);

            if (!String.IsNullOrEmpty(Operation.ViewTechName))
                Parts.Add("view=" + Operation.ViewTechName);

            var X = GetOperationDouble(Operation, "x");
            var Y = GetOperationDouble(Operation, "y");
            if (X != null || Y != null)
                Parts.Add("pos=" + (X == null ? "?" : X.Value.ToString("0.###", CultureInfo.InvariantCulture)) +
                          "," + (Y == null ? "?" : Y.Value.ToString("0.###", CultureInfo.InvariantCulture)));

            var Width = GetOperationDouble(Operation, "width");
            var Height = GetOperationDouble(Operation, "height");
            if (Width != null || Height != null)
                Parts.Add("size=" + (Width == null ? "?" : Width.Value.ToString("0.###", CultureInfo.InvariantCulture)) +
                          "x" + (Height == null ? "?" : Height.Value.ToString("0.###", CultureInfo.InvariantCulture)));

            var AutoPlace = Operation.AutoPlace ?? GetSetBool(Operation.Set, "autoPlace");
            if (AutoPlace != null)
                Parts.Add("autoPlace=" + (AutoPlace.Value ? "true" : "false"));

            var AutoFit = GetOperationAutoFit(Operation);
            if (AutoFit != null)
                Parts.Add("autoFit=" + (AutoFit.Value ? "true" : "false"));

            var AutoRoute = GetOperationAutoRoute(Operation);
            if (AutoRoute != null)
                Parts.Add("autoRoute=" + (AutoRoute.Value ? "true" : "false"));

            if (Operation.OriginIdeaIds != null && Operation.OriginIdeaIds.Count > 0)
                Parts.Add("origins=" + Operation.OriginIdeaIds.Count.ToString(CultureInfo.InvariantCulture));

            if (Operation.TargetIdeaIds != null && Operation.TargetIdeaIds.Count > 0)
                Parts.Add("targets=" + Operation.TargetIdeaIds.Count.ToString(CultureInfo.InvariantCulture));

            if (Operation.Links != null && Operation.Links.Count > 0)
                Parts.Add("links=" + Operation.Links.Count.ToString(CultureInfo.InvariantCulture));

            return String.Join(" ", Parts.ToArray());
        }

        private string DescribeTarget(FormalElement Target)
        {
            if (Target == null)
                return "<none>";

            return Target.GetType().Name + " name='" + Target.Name.ToStringAlways() +
                   "' techName='" + Target.TechName.ToStringAlways() +
                   "' id=" + Target.GlobalId.ToString("D");
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

        private void ExposeAffectedViewsAfterImport()
        {
            if (this.AffectedViews.Count < 1)
            {
                this.Report.Log("JSON import affected views: none.");
                return;
            }

            this.Report.Log("JSON import affected views: " +
                            String.Join(", ", this.AffectedViews.Select(View => View.Name + " (" + View.TechName + ")").ToArray()) + ".");

            var TargetView = this.AffectedViews.Contains(this.Engine.CurrentView)
                             ? this.Engine.CurrentView
                             : this.AffectedViews.FirstOrDefault();
            if (TargetView == null)
                return;

            var WasOpen = TargetView.Presenter != null && TargetView.HostingScrollViewer != null && TargetView.PresenterHostingGrid != null;
            var WasActive = this.Engine.CurrentView == TargetView;
            this.Report.Log("JSON import exposing first affected view: " + DescribeView(TargetView) +
                            ", wasOpen=" + (WasOpen ? "true" : "false") +
                            ", wasActive=" + (WasActive ? "true" : "false") + ".");

            try
            {
                if (!WasActive)
                {
                    this.Engine.ShowView(TargetView);
                    this.Report.Log("JSON import activated affected view: " + DescribeView(TargetView) + ".");
                }
                else
                    if (WasOpen)
                    {
                        this.Engine.StartCommandVariation("Refresh JSON import view");
                        TargetView.ShowAll();
                        this.Engine.CompleteCommandVariation();
                        this.Report.Log("JSON import refreshed active affected view via ShowAll: " + DescribeView(TargetView) + ".");
                    }

                if (TargetView.HostingScrollViewer != null)
                {
                    TargetView.FitContentIntoView();
                    this.Report.Log("JSON import fit affected view to content: " + DescribeView(TargetView) + ".");
                }

                SelectImportedVisuals(TargetView);
            }
            catch (Exception Problem)
            {
                this.Report.Warn("Affected view '" + TargetView.TechName + "' could not be activated/refreshed after import: " + Problem.Message);
            }
        }

        private void SelectImportedVisuals(View TargetView)
        {
            List<VisualObject> Objects;
            if (!this.ImportedVisualObjects.TryGetValue(TargetView, out Objects) || Objects.Count < 1)
                return;

            var First = Objects.FirstOrDefault(Object => Object != null && TargetView.ViewChildren.Any(Child => Child != null && Child.Key == Object));
            if (First == null)
                return;

            TargetView.UnselectAllObjects();
            TargetView.SelectObject(First, false);
            TargetView.Presenter.BringIntoView(First.BaseArea);
            this.Report.Log("JSON import selected imported visual in affected view: " + First.ToStringAlways() + ".");
        }

        private void RefreshAffectedViews()
        {
            foreach (var Idea in this.Composition.DeclaredIdeas)
                try
                {
                    Idea.UpdateVisualRepresentators();
                }
                catch (Exception Problem)
                {
                    this.Report.Warn("Idea '" + Idea.TechName + "' visual representators could not be refreshed after import: " + Problem.Message);
                }

            foreach (var View in this.Composition.GetSubgraphChildren().SelectMany(Idea => Idea.CompositeViews).Distinct())
                try
                {
                    var IsOpen = View.Presenter != null && View.HostingScrollViewer != null && View.PresenterHostingGrid != null;
                    var IsAffected = this.AffectedViews.Contains(View);
                    this.Report.Log("JSON import view refresh: " + DescribeView(View) +
                                    ", affected=" + (IsAffected ? "true" : "false") +
                                    ", open=" + (IsOpen ? "true" : "false") + ".");
                    if (View.Presenter != null && View.HostingScrollViewer != null && View.PresenterHostingGrid != null)
                    {
                        View.ShowAll();
                        this.Report.Log("JSON import view refresh called ShowAll: " + DescribeView(View) + ".");
                    }
                }
                catch (Exception Problem)
                {
                    this.Report.Warn("View '" + View.TechName + "' could not be refreshed after import: " + Problem.Message);
                }
        }
    }
}
