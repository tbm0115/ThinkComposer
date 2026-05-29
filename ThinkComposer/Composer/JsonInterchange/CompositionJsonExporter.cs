// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Exports a Composition into a deterministic, editable JSON interchange document.
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
    public static class CompositionJsonExporter
    {
        public static CompositionJsonDocument Export(Composition Composition)
        {
            General.ContractRequiresNotNull(Composition);

            var Warnings = new List<string>();
            var Document = new CompositionJsonDocument();
            Document.ExportedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            Document.Composition = ExportComposition(Composition);

            var Ideas = Composition.DeclaredIdeas.ToList();

            Document.Ideas = Ideas.Where(Idea => Idea is Concept && !(Idea is Composition))
                                  .Cast<Concept>()
                                  .OrderBy(ContainmentSortKey)
                                  .ThenBy(Idea => Idea.Name ?? "")
                                  .ThenBy(Idea => Idea.TechName ?? "")
                                  .ThenBy(Idea => IdOf(Idea))
                                  .Select(Idea => ExportConcept(Idea, Warnings))
                                  .ToList();

            Document.Relationships = Ideas.Where(Idea => Idea is Relationship)
                                          .Cast<Relationship>()
                                          .OrderBy(Idea => Idea.Name ?? "")
                                          .ThenBy(Idea => Idea.TechName ?? "")
                                          .ThenBy(Idea => IdOf(Idea))
                                          .Select(Relationship => ExportRelationship(Relationship, Warnings))
                                          .ToList();

            Document.Views = GetAllViews(Composition)
                             .OrderBy(View => View.Name ?? "")
                             .ThenBy(View => View.TechName ?? "")
                             .ThenBy(View => IdOf(View))
                             .Select(View => ExportView(View, Composition, Warnings))
                             .ToList();

            if (Document.Composition.Domain != null)
                Document.Composition.Domain.Definitions =
                    Ideas.Select(Idea => Idea.IdeaDefinitor)
                         .Where(Definition => Definition != null)
                         .Distinct()
                         .OrderBy(Definition => Definition is RelationshipDefinition ? "Relationship" : "Concept")
                         .ThenBy(Definition => Definition.Name ?? "")
                         .ThenBy(Definition => Definition.TechName ?? "")
                         .ThenBy(Definition => IdOf(Definition))
                         .Select(ExportDefinition)
                         .ToList();

            Document.Warnings = Warnings.OrderBy(Warning => Warning).Distinct().ToList();
            return Document;
        }

        private static CompositionJsonComposition ExportComposition(Composition Composition)
        {
            var Result = new CompositionJsonComposition();
            Result.Id = IdOf(Composition);
            Result.Name = Composition.Name;
            Result.TechName = Composition.TechName;
            Result.Summary = Composition.Summary;
            Result.TechSpec = Composition.TechSpec;
            Result.ViewsPrefix = Composition.ViewsPrefix;
            Result.RootViewId = IdOf(Composition.RootView);
            Result.ActiveViewId = IdOf(Composition.ActiveView);
            Result.Version = ExportVersion(Composition.Version);

            var Domain = Composition.CompositeContentDomain;
            if (Domain != null)
            {
                Result.Domain = new CompositionJsonDomain();
                Result.Domain.Id = IdOf(Domain);
                Result.Domain.Name = Domain.Name;
                Result.Domain.TechName = Domain.TechName;
                Result.Domain.Summary = Domain.Summary;
                Result.Domain.TechSpec = Domain.TechSpec;
            }

            return Result;
        }

        private static CompositionJsonDefinition ExportDefinition(IdeaDefinition Definition)
        {
            var Result = new CompositionJsonDefinition();
            Result.Id = IdOf(Definition);
            Result.Kind = Definition is RelationshipDefinition ? "RelationshipDefinition" : "ConceptDefinition";
            Result.Name = Definition.Name;
            Result.TechName = Definition.TechName;
            Result.Summary = Definition.Summary;
            Result.TechSpec = Definition.TechSpec;
            return Result;
        }

        private static CompositionJsonIdea ExportConcept(Concept Concept, List<string> Warnings)
        {
            var Result = new CompositionJsonIdea();
            FillIdea(Result, Concept, Warnings);
            return Result;
        }

        private static CompositionJsonRelationship ExportRelationship(Relationship Relationship, List<string> Warnings)
        {
            var Result = new CompositionJsonRelationship();
            FillRelationship(Result, Relationship, Warnings);
            return Result;
        }

        private static void FillIdea(CompositionJsonIdea Target, Concept Source, List<string> Warnings)
        {
            Target.Id = IdOf(Source);
            Target.Kind = "Concept";
            Target.Name = Source.Name;
            Target.TechName = Source.TechName;
            Target.Summary = Source.Summary;
            Target.TechSpec = Source.TechSpec;
            FillDefinition(Target, Source.IdeaDefinitor);
            Target.ContainerId = IdOf(Source.OwnerContainer);
            Target.ContainerTechName = Source.OwnerContainer == null ? null : Source.OwnerContainer.TechName;
            Target.ChildIdeaIds = Source.CompositeIdeas.OrderBy(ContainmentSortKey).Select(IdOf).ToList();
            Target.CompositeViewIds = Source.CompositeViews.OrderBy(View => View.Name ?? "").ThenBy(View => View.TechName ?? "").Select(IdOf).ToList();
            Target.Details = ExportDetails(Source, Warnings);
            Target.Markers = ExportMarkers(Source);
        }

        private static void FillRelationship(CompositionJsonRelationship Target, Relationship Source, List<string> Warnings)
        {
            Target.Id = IdOf(Source);
            Target.Kind = "Relationship";
            Target.Name = Source.Name;
            Target.TechName = Source.TechName;
            Target.Summary = Source.Summary;
            Target.TechSpec = Source.TechSpec;
            Target.DefinitionId = IdOf(Source.IdeaDefinitor);
            Target.DefinitionTechName = Source.IdeaDefinitor == null ? null : Source.IdeaDefinitor.TechName;
            Target.DefinitionName = Source.IdeaDefinitor == null ? null : Source.IdeaDefinitor.Name;
            Target.ContainerId = IdOf(Source.OwnerContainer);
            Target.ContainerTechName = Source.OwnerContainer == null ? null : Source.OwnerContainer.TechName;
            Target.OriginIdeaIds = Source.OriginIdeas.OrderBy(ContainmentSortKey).Select(IdOf).ToList();
            Target.TargetIdeaIds = Source.TargetIdeas.OrderBy(ContainmentSortKey).Select(IdOf).ToList();
            Target.Links = Source.Links.OrderBy(Link => Link.RoleDefinitor == null ? "" : Link.RoleDefinitor.RoleType.ToString())
                                .ThenBy(Link => Link.RoleDefinitor == null ? "" : Link.RoleDefinitor.TechName)
                                .ThenBy(Link => Link.AssociatedIdea == null ? "" : Link.AssociatedIdea.TechName)
                                .Select(ExportRelationshipLink)
                                .ToList();
            Target.ChildIdeaIds = Source.CompositeIdeas.OrderBy(ContainmentSortKey).Select(IdOf).ToList();
            Target.CompositeViewIds = Source.CompositeViews.OrderBy(View => View.Name ?? "").ThenBy(View => View.TechName ?? "").Select(IdOf).ToList();
            Target.Details = ExportDetails(Source, Warnings);
            Target.Markers = ExportMarkers(Source);
        }

        private static void FillDefinition(CompositionJsonIdea Target, IdeaDefinition Definition)
        {
            Target.DefinitionId = IdOf(Definition);
            Target.DefinitionTechName = Definition == null ? null : Definition.TechName;
            Target.DefinitionName = Definition == null ? null : Definition.Name;
        }

        private static CompositionJsonRelationshipLink ExportRelationshipLink(RoleBasedLink Link)
        {
            var Result = new CompositionJsonRelationshipLink();
            Result.Id = IdOf(Link);
            Result.RoleType = Link.RoleDefinitor == null ? null : Link.RoleDefinitor.RoleType.ToString();
            Result.RoleDefinitionId = IdOf(Link.RoleDefinitor);
            Result.RoleDefinitionTechName = Link.RoleDefinitor == null ? null : Link.RoleDefinitor.TechName;
            Result.RoleDefinitionName = Link.RoleDefinitor == null ? null : Link.RoleDefinitor.Name;
            Result.RoleVariantTechName = Link.RoleVariant == null ? null : Link.RoleVariant.TechName;
            Result.RoleVariantName = Link.RoleVariant == null ? null : Link.RoleVariant.Name;
            Result.IdeaId = IdOf(Link.AssociatedIdea);
            Result.IdeaTechName = Link.AssociatedIdea == null ? null : Link.AssociatedIdea.TechName;
            return Result;
        }

        private static List<CompositionJsonMarker> ExportMarkers(Idea Idea)
        {
            return Idea.Markings.OrderBy(Marker => Marker.Definitor == null ? "" : Marker.Definitor.Name)
                        .ThenBy(Marker => Marker.Definitor == null ? "" : Marker.Definitor.TechName)
                        .Select(Marker =>
                        {
                            var Result = new CompositionJsonMarker();
                            Result.DefinitionId = null;
                            Result.DefinitionTechName = Marker.Definitor == null ? null : Marker.Definitor.TechName;
                            Result.DefinitionName = Marker.Definitor == null ? null : Marker.Definitor.Name;
                            Result.DescriptorName = Marker.Descriptor == null ? null : Marker.Descriptor.Name;
                            Result.DescriptorTechName = Marker.Descriptor == null ? null : Marker.Descriptor.TechName;
                            Result.DescriptorSummary = Marker.Descriptor == null ? null : Marker.Descriptor.Summary;
                            return Result;
                        }).ToList();
        }

        private static List<CompositionJsonDetail> ExportDetails(Idea Idea, List<string> Warnings)
        {
            var Result = new List<CompositionJsonDetail>();

            foreach (var Detail in Idea.Details.OrderBy(DetailSortKey))
            {
                var Exported = new CompositionJsonDetail();
                Exported.Kind = Detail.GetType().Name;

                if (Detail.Designation != null)
                {
                    Exported.DesignatorId = IdOf(Detail.Designation);
                    Exported.DesignatorTechName = Detail.Designation.TechName;
                    Exported.DesignatorName = Detail.Designation.Name;
                }

                var Table = Detail as Table;
                if (Table != null)
                    ExportTable(Table, Exported, Warnings);
                else
                {
                    var Link = Detail as Link;
                    if (Link != null)
                        ExportLink(Link, Exported, Warnings);
                    else
                    {
                        var Attachment = Detail as Attachment;
                        if (Attachment != null)
                            ExportAttachment(Attachment, Exported, Warnings);
                        else
                        {
                            Exported.Text = Detail.ToStringAlways();
                            Warnings.Add("Detail '" + DetailSortKey(Detail) + "' on idea '" + Idea.TechName + "' was exported as text only; import will preserve the native detail.");
                        }
                    }
                }

                Result.Add(Exported);
            }

            return Result;
        }

        private static void ExportTable(Table Table, CompositionJsonDetail Target, List<string> Warnings)
        {
            Target.Kind = "Table";

            if (Table.Definition == null)
            {
                Warnings.Add("Table detail '" + DetailSortKey(Table) + "' has no table definition and was exported without records.");
                return;
            }

            Target.Fields = Table.Definition.FieldDefinitions
                                 .OrderBy(Field => Field.StorageIndex)
                                 .Select(Field =>
                                 {
                                     var Result = new CompositionJsonField();
                                     Result.Id = IdOf(Field);
                                     Result.Name = Field.Name;
                                     Result.TechName = Field.TechName;
                                     Result.DataType = Field.FieldType == null ? null : Field.FieldType.TechName;
                                     return Result;
                                 }).ToList();

            Target.Records = new List<Dictionary<string, object>>();
            foreach (var Record in Table.Records)
            {
                var RecordObject = new Dictionary<string, object>();
                foreach (var Field in Table.Definition.FieldDefinitions.OrderBy(Field => Field.StorageIndex))
                    RecordObject[Field.TechName] = Record.GetFieldValueForExport(Field, false, true, true);

                Target.Records.Add(RecordObject);
            }
        }

        private static void ExportLink(Link Link, CompositionJsonDetail Target, List<string> Warnings)
        {
            Target.Kind = Link is InternalLink ? "InternalLink" : "ResourceLink";

            var Internal = Link as InternalLink;
            if (Internal != null && Internal.TargetProperty != null)
            {
                Target.TargetPropertyTechName = Internal.TargetProperty.TechName;
                Target.TargetAddress = Internal.TargetAddress;
                var Value = Internal.TargetProperty.Read(Internal.OwnerIdea);
                Target.Text = Value == null ? null : Value.ToString();
                return;
            }

            var Resource = Link as ResourceLink;
            if (Resource != null)
            {
                Target.TargetAddress = Resource.TargetLocation;
                return;
            }

            Warnings.Add("Link detail '" + DetailSortKey(Link) + "' was exported as metadata only.");
        }

        private static void ExportAttachment(Attachment Attachment, CompositionJsonDetail Target, List<string> Warnings)
        {
            Target.Kind = "Attachment";
            Target.Source = Attachment.Source;
            Target.MimeType = Attachment.MimeType;
            Target.Text = Attachment.ToString();
            Warnings.Add("Attachment '" + Attachment.Source.ToStringAlways() + "' was exported as metadata only; binary content is preserved only in the native .tcom file.");
        }

        private static CompositionJsonView ExportView(View View, Composition Composition, List<string> Warnings)
        {
            var Result = new CompositionJsonView();
            Result.Id = IdOf(View);
            Result.Name = View.Name;
            Result.TechName = View.TechName;
            Result.Summary = View.Summary;
            Result.OwnerIdeaId = IdOf(View.OwnerCompositeContainer);
            Result.OwnerIdeaTechName = View.OwnerCompositeContainer == null ? null : View.OwnerCompositeContainer.TechName;
            Result.Visuals = Composition.DeclaredIdeas
                                .SelectMany(Idea => Idea.VisualRepresentators)
                                .Where(Representation => Representation.DisplayingView == View)
                                .OrderBy(Representation => Representation.RepresentedIdea == null ? "" : Representation.RepresentedIdea.TechName)
                                .ThenBy(Representation => IdOf(Representation))
                                .Select(ExportVisual)
                                .ToList();

            var Children = View.ViewChildren == null ? Enumerable.Empty<ViewChild>() : View.ViewChildren;
            foreach (var Child in Children)
                if (Child != null && !(Child.Key is VisualObject))
                    Warnings.Add("View '" + View.TechName + "' contains a non-visual-object child that is not represented in JSON.");

            return Result;
        }

        private static CompositionJsonVisual ExportVisual(VisualRepresentation Representation)
        {
            var Result = new CompositionJsonVisual();
            Result.IdeaId = IdOf(Representation.RepresentedIdea);
            Result.IdeaTechName = Representation.RepresentedIdea == null ? null : Representation.RepresentedIdea.TechName;
            Result.RepresentationId = IdOf(Representation);
            Result.IsShortcut = Representation.IsShortcut;

            var Symbol = Representation.MainSymbol;
            if (Symbol != null)
            {
                Result.X = Symbol.BaseLeft;
                Result.Y = Symbol.BaseTop;
                Result.Width = Symbol.BaseWidth;
                Result.Height = Symbol.BaseHeight;
            }

            return Result;
        }

        private static CompositionJsonVersion ExportVersion(VersionCard Version)
        {
            if (Version == null)
                return null;

            var Result = new CompositionJsonVersion();
            Result.VersionSequence = Version.VersionSequence;
            Result.VersionNumber = Version.VersionNumber;
            Result.Annotation = Version.Annotation;
            Result.Creator = Version.Creator;
            Result.LastModifier = Version.LastModifier;
            Result.Creation = Version.Creation.ToString("o", CultureInfo.InvariantCulture);
            Result.LastModification = Version.LastModification.ToString("o", CultureInfo.InvariantCulture);
            return Result;
        }

        private static IEnumerable<View> GetAllViews(Composition Composition)
        {
            return Composition.GetSubgraphChildren()
                              .SelectMany(Idea => Idea.CompositeViews)
                              .Distinct();
        }

        private static string ContainmentSortKey(Idea Idea)
        {
            if (Idea == null)
                return "";

            return Idea.GetContainmentRoute(false, true, true, true, "/", true, true);
        }

        private static string DetailSortKey(ContainedDetail Detail)
        {
            if (Detail == null || Detail.Designation == null)
                return "";

            return Detail.Designation.TechName + "|" + Detail.Designation.Name + "|" + IdOf(Detail.Designation);
        }

        private static string IdOf(UniqueElement Element)
        {
            return Element == null ? null : Element.GlobalId.ToString("D");
        }

        private static string IdOf(MModelClassDefinitor Element)
        {
            return Element == null ? null : Element.TechName;
        }
    }
}
