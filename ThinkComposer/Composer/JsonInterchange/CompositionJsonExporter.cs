// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Exports a Composition into a deterministic, editable JSON interchange document.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

using Instrumind.Common;
using Instrumind.Common.EntityBase;
using Instrumind.Common.EntityDefinition;
using Instrumind.Common.Visualization;
using Instrumind.ThinkComposer.MetaModel;
using Instrumind.ThinkComposer.MetaModel.GraphMetaModel;
using Instrumind.ThinkComposer.MetaModel.InformationMetaModel;
using Instrumind.ThinkComposer.MetaModel.VisualMetaModel;
using Instrumind.ThinkComposer.Model;
using Instrumind.ThinkComposer.Model.GraphModel;
using Instrumind.ThinkComposer.Model.InformationModel;
using Instrumind.ThinkComposer.Model.VisualModel;
using Instrumind.ThinkComposer.Definitor.DomainJsonInterchange;

namespace Instrumind.ThinkComposer.Composer.JsonInterchange
{
    public static class CompositionJsonExporter
    {
        public static CompositionJsonDocument Export(Composition Composition)
        {
            General.ContractRequiresNotNull(Composition);

            return Export(Composition,
                          DomainJsonCompatibility.ComputeSignature(Composition.CompositeContentDomain));
        }

        internal static CompositionJsonDocument Export(Composition Composition, string DomainCompatibilitySignature)
        {
            General.ContractRequiresNotNull(Composition);

            var Warnings = new List<string>();
            Warnings.Add("Custom visual formatting, store-box references, and native/binary-only content are exported only when represented by documented JSON fields; JSON persistence reconstructs documented visual formats and pictograms but does not reconstruct unsupported native-only payloads.");
            var Document = new CompositionJsonDocument();
            Document.ExportedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            Document.Composition = ExportComposition(Composition, DomainCompatibilitySignature);
            Document.TargetContext = ExportTargetContext(Composition, DomainCompatibilitySignature);

            var Ideas = Composition.DeclaredIdeas.ToList();
            var RepresentationsByView = Ideas.SelectMany(Idea => Idea.VisualRepresentators)
                                             .Where(Representation => Representation != null &&
                                                                      Representation.DisplayingView != null)
                                             .ToLookup(Representation => Representation.DisplayingView);

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
                             .Select(View => ExportView(View, RepresentationsByView[View], Warnings))
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

        private static CompositionJsonComposition ExportComposition(Composition Composition,
                                                                    string DomainCompatibilitySignature)
        {
            var Result = new CompositionJsonComposition();
            Result.Id = IdOf(Composition);
            Result.Name = Composition.Name;
            Result.TechName = Composition.TechName;
            Result.Summary = Composition.Summary;
            Result.Description = ExportDescription(Composition.Description);
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
                Result.Domain.Description = ExportDescription(Domain.Description);
                Result.Domain.TechSpec = Domain.TechSpec;
                Result.Domain.CompatibilitySignature = DomainCompatibilitySignature;
            }

            return Result;
        }

        private static CompositionJsonTargetContext ExportTargetContext(Composition Composition,
                                                                        string DomainCompatibilitySignature)
        {
            var Result = new CompositionJsonTargetContext();
            Result.Composition = ExportContextElement(Composition, null);
            Result.Domain = ExportContextElement(Composition == null ? null : Composition.CompositeContentDomain,
                                                 DomainCompatibilitySignature);
            return Result;
        }

        private static CompositionJsonContextElement ExportContextElement(FormalElement Element, string CompatibilitySignature)
        {
            if (Element == null)
                return null;

            var Result = new CompositionJsonContextElement();
            Result.Id = IdOf(Element);
            Result.Name = Element.Name;
            Result.TechName = Element.TechName;
            if (Element.Version != null)
            {
                Result.VersionNumber = Element.Version.VersionNumber == null ? null : Element.Version.VersionNumber.ToString();
                Result.VersionSequence = Element.Version.VersionSequence;
                Result.LastModification = Element.Version.LastModification.ToString("o", CultureInfo.InvariantCulture);
            }
            Result.CompatibilitySignature = CompatibilitySignature;
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
            Result.Description = ExportDescription(Definition.Description);
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
            Target.Description = ExportDescription(Source.Description);
            Target.TechSpec = Source.TechSpec;
            Target.Pictogram = ExportImageSource(Source.Pictogram);
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
            Target.Description = ExportDescription(Source.Description);
            Target.TechSpec = Source.TechSpec;
            Target.Pictogram = ExportImageSource(Source.Pictogram);
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
            Result.DescriptorName = Link.Descriptor == null ? null : Link.Descriptor.Name;
            Result.DescriptorTechName = Link.Descriptor == null ? null : Link.Descriptor.TechName;
            Result.DescriptorSummary = Link.Descriptor == null ? null : Link.Descriptor.Summary;
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
                            Warnings.Add("Detail '" + DetailSortKey(Detail) + "' on idea '" + Idea.TechName + "' was exported as text only; JSON persistence rehydrates only the exported text representation.");
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

            var FieldDefinitions = Table.Definition.FieldDefinitions
                                  .Where(Field => Field != null)
                                  .OrderBy(Field => Field.StorageIndex)
                                  .ToList();

            Target.Fields = FieldDefinitions
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
            var RecordIndex = 0;
            if (Table.Records != null)
                foreach (var Record in Table.Records)
                {
                    RecordIndex++;
                    var RecordObject = new Dictionary<string, object>();
                    foreach (var Field in FieldDefinitions)
                    {
                        var FieldKey = FieldExportKey(Field);
                        try
                        {
                            RecordObject[FieldKey] = Record == null ? "" : Record.GetFieldValueForExport(Field, false, true, true);
                        }
                        catch (Exception Problem)
                        {
                            RecordObject[FieldKey] = "";
                            Warnings.Add("Table detail '" + DetailSortKey(Table) +
                                         "' record " + RecordIndex.ToString(CultureInfo.InvariantCulture) +
                                         " field '" + FieldKey +
                                         "' could not be exported and was emitted as an empty string. " +
                                         Problem.GetType().Name + ": " + Problem.Message);
                        }
                    }

                    Target.Records.Add(RecordObject);
                }
        }

        private static string FieldExportKey(FieldDefinition Field)
        {
            if (Field == null)
                return "field";

            return Field.TechName.NullDefault(Field.Name)
                                .NullDefault(IdOf(Field))
                                .NullDefault("field");
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
            Warnings.Add("Attachment '" + Attachment.Source.ToStringAlways() + "' was exported as metadata only; binary content is not inlined in Composition JSON and is not reconstructed by JSON persistence.");
        }

        private static CompositionJsonView ExportView(View View,
                                                      IEnumerable<VisualRepresentation> ViewRepresentations,
                                                      List<string> Warnings)
        {
            var Result = new CompositionJsonView();
            Result.Id = IdOf(View);
            Result.Name = View.Name;
            Result.TechName = View.TechName;
            Result.Summary = View.Summary;
            Result.Description = ExportDescription(View.Description);
            Result.OwnerIdeaId = IdOf(View.OwnerCompositeContainer);
            Result.OwnerIdeaTechName = View.OwnerCompositeContainer == null ? null : View.OwnerCompositeContainer.TechName;
            var Representations = (ViewRepresentations ?? Enumerable.Empty<VisualRepresentation>())
                                  .OrderBy(Representation => Representation.RepresentedIdea == null ? "" : Representation.RepresentedIdea.TechName)
                                  .ThenBy(Representation => IdOf(Representation))
                                  .ToList();

            var FreeComplements = View.GetFreeComplements()
                                     .Where(Complement => IsExportableComplement(Complement, Warnings))
                                     .OrderBy(Complement => Complement.ZOrder)
                                     .ThenBy(Complement => Complement.GlobalId.ToString("D"))
                                     .ToList();
            var ZOrders = BuildExportedZOrderMap(View, Representations, FreeComplements);

            Result.Visuals = Representations
                                .Select(Representation => ExportVisual(Representation, Warnings, ZOrders))
                                .ToList();
            Result.Complements = FreeComplements
                                     .Select(Complement => ExportComplement(Complement, ZOrders))
                                     .Where(Complement => Complement != null)
                                     .ToList();

            var Children = View.ViewChildren == null ? Enumerable.Empty<ViewChild>() : View.ViewChildren;
            foreach (var Child in Children)
                if (Child != null && !(Child.Key is VisualObject))
                    Warnings.Add("View '" + View.TechName + "' contains a non-visual-object child that is not represented in JSON.");

            return Result;
        }

        private static Dictionary<VisualObject, int> BuildExportedZOrderMap(View View,
                                                                            IEnumerable<VisualRepresentation> Representations,
                                                                            IEnumerable<VisualComplement> FreeComplements)
        {
            var ExportedObjects = new HashSet<VisualObject>();
            foreach (var Representation in Representations ?? Enumerable.Empty<VisualRepresentation>())
            {
                if (Representation == null)
                    continue;

                if (Representation.MainSymbol != null)
                    ExportedObjects.Add(Representation.MainSymbol);

                var RelationshipRepresentation = Representation as RelationshipVisualRepresentation;
                if (RelationshipRepresentation != null)
                    foreach (var Connector in RelationshipRepresentation.VisualConnectors.Where(Connector => Connector != null))
                        ExportedObjects.Add(Connector);

                if (Representation.MainSymbol != null)
                    foreach (var Complement in Representation.MainSymbol.AttachedComplements.Where(Complement => IsExportableComplement(Complement, null)))
                        ExportedObjects.Add(Complement);
            }

            foreach (var Complement in FreeComplements ?? Enumerable.Empty<VisualComplement>())
                if (Complement != null)
                    ExportedObjects.Add(Complement);

            var Result = new Dictionary<VisualObject, int>();
            if (View == null || View.ViewChildren == null || ExportedObjects.Count < 1)
                return Result;

            var Order = 0;
            foreach (var Child in View.ViewChildren)
            {
                var VisualObject = Child == null ? null : Child.Key as VisualObject;
                if (VisualObject != null && ExportedObjects.Contains(VisualObject))
                    Result[VisualObject] = Order++;
            }

            return Result;
        }

        private static string ExportDescription(string Description)
        {
            return Display.XamlRichTextToPlainTextOrSelf(Description);
        }

        private static CompositionJsonVisual ExportVisual(VisualRepresentation Representation, List<string> Warnings, IDictionary<VisualObject, int> ZOrders)
        {
            var Result = new CompositionJsonVisual();
            Result.IdeaId = IdOf(Representation.RepresentedIdea);
            Result.IdeaTechName = Representation.RepresentedIdea == null ? null : Representation.RepresentedIdea.TechName;
            Result.RepresentationId = IdOf(Representation);
            Result.IsShortcut = Representation.IsShortcut;

            var Symbol = Representation.MainSymbol;
            if (Symbol != null)
            {
                Result.ZOrder = ExportZOrder(Symbol, ZOrders);
                Result.X = Symbol.BaseLeft;
                Result.Y = Symbol.BaseTop;
                Result.Width = Symbol.BaseWidth;
                Result.Height = Symbol.BaseHeight;
                Result.AreDetailsShown = Symbol.AreDetailsShown;
                Result.ShowCompositeContentAsDetails = Symbol.ShowCompositeContentAsDetails;
                Result.DetailsPosterHeight = Symbol.DetailsPosterHeight;
                Result.ShowAsMultiple = Symbol.ShowAsMultiple;
                Result.IsHorizontallyFlipped = Symbol.IsHorizontallyFlipped;
                Result.IsVerticallyFlipped = Symbol.IsVerticallyFlipped;
                Result.IsTilted = Symbol.IsTilted;
                Result.Complements = Symbol.AttachedComplements
                                           .Where(Complement => IsExportableComplement(Complement, Warnings))
                                           .OrderBy(Complement => Complement.ZOrder)
                                           .ThenBy(Complement => Complement.GlobalId.ToString("D"))
                                           .Select(Complement => ExportComplement(Complement, ZOrders))
                                           .Where(Complement => Complement != null)
                                           .ToList();
            }

            var RelationshipRepresentation = Representation as RelationshipVisualRepresentation;
            if (RelationshipRepresentation != null)
                Result.Connectors = RelationshipRepresentation.VisualConnectors
                                      .Where(Connector => Connector != null)
                                      .OrderBy(Connector => Connector.ZOrder)
                                      .ThenBy(Connector => Connector.GlobalId.ToString("D"))
                                      .Select(Connector => ExportConnector(Connector, ZOrders))
                                      .Where(Connector => Connector != null)
                                      .ToList();

            Result.CustomFormatValues = ExportCustomFormatValues(Representation);
            return Result;
        }

        private static bool IsExportableComplement(VisualComplement Complement, List<string> Warnings)
        {
            if (Complement == null || Complement.Kind == null)
                return false;

            if (Complement.IsComplementImage)
            {
                if (Warnings != null)
                    Warnings.Add("Image complement '" + Complement.GlobalId.ToString("D") + "' was not exported as native persistence JSON because image payloads remain binary-only.");

                return false;
            }

            return VisualComplement.ApplicablePropertiesByKind.ContainsKey(Complement.Kind.TechName);
        }

        private static CompositionJsonConnector ExportConnector(VisualConnector Connector, IDictionary<VisualObject, int> ZOrders)
        {
            var Result = new CompositionJsonConnector();
            Result.Id = IdOf(Connector);
            Result.LinkId = IdOf(Connector.RepresentedLink);
            Result.RoleType = Connector.RepresentedLink == null || Connector.RepresentedLink.RoleDefinitor == null
                              ? null : Connector.RepresentedLink.RoleDefinitor.RoleType.ToString();
            Result.RoleDefinitionTechName = Connector.RepresentedLink == null || Connector.RepresentedLink.RoleDefinitor == null
                                            ? null : Connector.RepresentedLink.RoleDefinitor.TechName;
            Result.RoleVariantTechName = Connector.RepresentedLink == null || Connector.RepresentedLink.RoleVariant == null
                                         ? null : Connector.RepresentedLink.RoleVariant.TechName;
            Result.AssociatedIdeaId = Connector.RepresentedLink == null ? null : IdOf(Connector.RepresentedLink.AssociatedIdea);
            Result.AssociatedIdeaTechName = Connector.RepresentedLink == null || Connector.RepresentedLink.AssociatedIdea == null
                                            ? null : Connector.RepresentedLink.AssociatedIdea.TechName;
            Result.ZOrder = ExportZOrder(Connector, ZOrders);
            FillConnectorEndpoint(Result, Connector.OriginSymbol, true);
            FillConnectorEndpoint(Result, Connector.TargetSymbol, false);
            Result.OriginPosition = ExportPoint(Connector.OriginPosition);
            Result.OriginEdgePosition = ExportPoint(Connector.OriginEdgePosition);
            Result.TargetPosition = ExportPoint(Connector.TargetPosition);
            Result.TargetEdgePosition = ExportPoint(Connector.TargetEdgePosition);
            Result.IntermediatePosition = ExportPoint(Connector.IntermediatePosition);
            return Result;
        }

        private static void FillConnectorEndpoint(CompositionJsonConnector Target, VisualSymbol Symbol, bool IsOrigin)
        {
            var Representation = Symbol == null ? null : Symbol.OwnerRepresentation;
            var Idea = Representation == null ? null : Representation.RepresentedIdea;

            if (IsOrigin)
            {
                Target.OriginRepresentationId = IdOf(Representation);
                Target.OriginIdeaId = IdOf(Idea);
                Target.OriginIdeaTechName = Idea == null ? null : Idea.TechName;
            }
            else
            {
                Target.TargetRepresentationId = IdOf(Representation);
                Target.TargetIdeaId = IdOf(Idea);
                Target.TargetIdeaTechName = Idea == null ? null : Idea.TechName;
            }
        }

        private static CompositionJsonComplement ExportComplement(VisualComplement Complement, IDictionary<VisualObject, int> ZOrders)
        {
            if (Complement == null || Complement.Kind == null)
                return null;

            var Result = new CompositionJsonComplement();
            Result.Id = IdOf(Complement);
            Result.KindTechName = Complement.Kind.TechName;
            Result.KindName = Complement.Kind.Name;
            Result.ZOrder = ExportZOrder(Complement, ZOrders);
            Result.X = Complement.BaseLeft;
            Result.Y = Complement.BaseTop;
            Result.Width = Complement.BaseWidth;
            Result.Height = Complement.BaseHeight;
            Result.Set = ExportComplementFields(Complement);
            return Result;
        }

        private static int? ExportZOrder(VisualObject VisualObject, IDictionary<VisualObject, int> ZOrders)
        {
            if (VisualObject == null)
                return null;

            int NormalizedZOrder;
            if (ZOrders != null && ZOrders.TryGetValue(VisualObject, out NormalizedZOrder))
                return NormalizedZOrder;

            var ZOrder = VisualObject.ZOrder;
            return ZOrder < 0 ? (int?)null : ZOrder;
        }

        private static CompositionJsonPoint ExportPoint(Point Point)
        {
            if (Point == Display.NULL_POINT)
                return null;

            return new CompositionJsonPoint { X = Point.X, Y = Point.Y };
        }

        private static Dictionary<string, object> ExportComplementFields(VisualComplement Complement)
        {
            var Result = new Dictionary<string, object>();
            string[] Fields;
            if (Complement == null || Complement.Kind == null ||
                !VisualComplement.ApplicablePropertiesByKind.TryGetValue(Complement.Kind.TechName, out Fields))
                return Result;

            foreach (var Field in Fields)
            {
                var Value = ExportComplementField(Complement, Field);
                if (Value != null)
                    Result[Field] = Value;
            }

            return Result;
        }

        private static object ExportComplementField(VisualComplement Complement, string Field)
        {
            if (Field == VisualComplement.PROP_FIELD_FOREGROUND ||
                Field == VisualComplement.PROP_FIELD_BACKGROUND)
            {
                Brush Value;
                return TryGetComplementField(Complement, Field, out Value) ? ExportBrush(Value) : null;
            }

            if (Field == VisualComplement.PROP_FIELD_LINEDASH)
                return ExportDashStyle(Complement.GetPropertyField<DashStyle>(Field));

            if (Field == VisualComplement.PROP_FIELD_LINETHICK)
                return Complement.GetPropertyField<double>(Field);

            if (Field == VisualComplement.PROP_FIELD_OFFSETX ||
                Field == VisualComplement.PROP_FIELD_OFFSETY)
            {
                double Value;
                return TryGetComplementField(Complement, Field, out Value) ? (object)Value : null;
            }

            if (Field == VisualComplement.PROP_FIELD_TEXT)
            {
                string Value;
                return TryGetComplementField(Complement, Field, out Value) ? Value : null;
            }

            if (Field == VisualComplement.PROP_FIELD_TEXTFORMAT)
            {
                TextFormat Value;
                return TryGetComplementField(Complement, Field, out Value) ? ExportTextFormat(Value) : null;
            }

            if (Field == VisualComplement.PROP_FIELD_ORIENTATION)
            {
                System.Windows.Controls.Orientation Value;
                return TryGetComplementField(Complement, Field, out Value) ? Value.ToString() : null;
            }

            if (Field == VisualComplement.PROP_FIELD_QUADRANT)
            {
                EVecinityQuadrant Value;
                return TryGetComplementField(Complement, Field, out Value) ? Value.ToString() : null;
            }

            return null;
        }

        private static bool TryGetComplementField<TField>(VisualComplement Complement, string Field, out TField Value)
        {
            Value = default(TField);

            if (Complement == null)
                return false;

            try
            {
                Value = Complement.GetPropertyField<TField>(Field, false);
                return true;
            }
            catch (UsageAnomaly Problem)
            {
                if (Problem.Message != null &&
                    Problem.Message.StartsWith("Property field not assigned:", StringComparison.Ordinal))
                    return false;

                throw;
            }
        }

        private static Dictionary<string, object> ExportCustomFormatValues(VisualRepresentation Representation)
        {
            var Result = new Dictionary<string, object>();
            if (Representation == null || Representation.CustomFormatValues == null)
                return Result;

            foreach (var Pair in Representation.CustomFormatValues.OrderBy(Pair => Pair.Key))
            {
                var Value = ExportFormatValue(Pair.Value);
                if (Value != null)
                    Result[Pair.Key] = Value;
            }

            return Result;
        }

        private static object ExportFormatValue(object Value)
        {
            if (Value == null)
                return null;

            var StoreBox = Value as StoreBoxBase;
            if (StoreBox != null)
                Value = StoreBox.StoredObject;

            var Brush = Value as Brush;
            if (Brush != null)
                return ExportBrush(Brush);

            var Dash = Value as DashStyle;
            if (Dash != null)
                return ExportDashStyle(Dash);

            var TextFormat = Value as TextFormat;
            if (TextFormat != null)
                return ExportTextFormat(TextFormat);

            if (Value is bool || Value is int || Value is double || Value is string)
                return Value;

            if (Value.GetType().IsEnum)
                return Value.ToString();

            return null;
        }

        private static Dictionary<string, object> ExportTextFormat(TextFormat Format)
        {
            if (Format == null)
                return null;

            var Result = new Dictionary<string, object>();
            Result["type"] = "textFormat";
            Result["fontFamilyName"] = Format.FontFamilyName;
            Result["fontSize"] = Format.FontSize;
            Result["foregroundBrush"] = ExportBrush(Format.ForegroundBrush);
            Result["isBold"] = Format.IsBold;
            Result["isItalic"] = Format.IsItalic;
            Result["isUnderline"] = Format.IsUnderline;
            Result["isStrikethrough"] = Format.IsStrikethrough;
            Result["alignment"] = Format.Alignment.ToString();
            return Result;
        }

        private static Dictionary<string, object> ExportImageSource(ImageSource Image)
        {
            if (Image == null)
                return null;

            try
            {
                var Bytes = Image.ToBytes(true);
                if (Bytes == null || Bytes.Length < 1)
                    return null;

                var Result = new Dictionary<string, object>();
                Result["type"] = "imageSource";
                Result["encoding"] = "base64";
                Result["format"] = "thinkComposerImageBytes";
                Result["data"] = Convert.ToBase64String(Bytes);
                return Result;
            }
            catch
            {
                return null;
            }
        }

        private static object ExportBrush(Brush Brush)
        {
            if (Brush == null)
                return null;

            var Text = default(string);
            try
            {
                var Converter = new BrushConverter();
                if (Converter.CanConvertTo(typeof(string)))
                    Text = (string)Converter.ConvertTo(null, CultureInfo.InvariantCulture, Brush, typeof(string));
            }
            catch
            {
            }

            if (!CanImportBrushText(Text))
            {
                var Xaml = ExportBrushXaml(Brush);
                if (!String.IsNullOrWhiteSpace(Xaml))
                    return new Dictionary<string, object>
                    {
                        { "type", "brush" },
                        { "xaml", Xaml }
                    };

                if (String.IsNullOrWhiteSpace(Text))
                    return null;
            }

            if (Math.Abs(Brush.Opacity - 1.0) < 0.0001)
                return Text;

            return new Dictionary<string, object>
            {
                { "color", Text },
                { "opacity", Brush.Opacity }
            };
        }

        private static bool CanImportBrushText(string Text)
        {
            if (String.IsNullOrWhiteSpace(Text))
                return false;

            try
            {
                return new BrushConverter().ConvertFromString(null, CultureInfo.InvariantCulture, Text) is Brush;
            }
            catch
            {
                return false;
            }
        }

        private static string ExportBrushXaml(Brush Brush)
        {
            if (Brush == null)
                return null;

            try
            {
                return XamlWriter.Save(Brush);
            }
            catch
            {
                return null;
            }
        }

        private static string ExportDashStyle(DashStyle Dash)
        {
            if (Dash == null)
                return null;

            var Declared = Display.DeclaredDashStyles.FirstOrDefault(Item => Item.Item1.IsEqual(Dash));
            return Declared == null ? null : Declared.Item2;
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
