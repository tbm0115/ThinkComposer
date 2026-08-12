// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Dependency-light regressions for Domain JSON Detail Designator persistence.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

using Instrumind.Common;

using Instrumind.ThinkComposer.MetaModel;
using Instrumind.ThinkComposer.MetaModel.GraphMetaModel;
using Instrumind.ThinkComposer.MetaModel.InformationMetaModel;
using Instrumind.ThinkComposer.MetaModel.VisualMetaModel;
using Instrumind.ThinkComposer.Model.GraphModel;

namespace Instrumind.ThinkComposer.Definitor.DomainJsonInterchange
{
    public sealed class DomainJsonDetailPersistenceRegressionResult
    {
        public DomainJsonDetailPersistenceRegressionResult()
        {
            this.PassedScenarios = new List<string>();
            this.Failures = new List<string>();
        }

        public IList<string> PassedScenarios { get; private set; }
        public IList<string> Failures { get; private set; }
        public bool Passed { get { return this.Failures.Count == 0; } }
    }

    /// <summary>
    /// Exercises the Domain JSON contract that must exist before Composition table-detail values
    /// can resolve their definition-level designators.  These scenarios require no test framework,
    /// package files, or UI interaction; the headless Domain validators provide application bootstrap.
    /// </summary>
    public static class DomainJsonDetailPersistenceRegression
    {
        private const string ProbeTechName = "Persistence_Probe_Detail";

        public static DomainJsonDetailPersistenceRegressionResult RunAll()
        {
            var Result = new DomainJsonDetailPersistenceRegressionResult();
            Run(Result, "v2-dto-serialized-presence", TestV2DtoSerializedPresence);
            Run(Result, "v2-stable-id-shared-table-order-rehydrate", TestStableIdSharedTableAndOrderAfterRehydrate);
            Run(Result, "v2-relationship-owned-table-id-rehydrate", TestRelationshipOwnedTableIdAfterRehydrate);
            Run(Result, "v2-operation-preview-planned-table-parity", TestOperationPreviewWithPlannedTable);
            Run(Result, "v2-unresolved-contained-field-aborts-exact", TestUnresolvedContainedFieldAbortsExactReplacement);
            Run(Result, "v1-omission-preserves-constructor-defaults", TestV1OmissionPreservesDefaults);
            Run(Result, "v2-explicit-empty-is-exact", TestV2ExplicitEmptyIsExact);
            Run(Result, "v2-malformed-explicit-list-is-rejected", TestMalformedExplicitListIsRejected);
            return Result;
        }

        private static void Run(DomainJsonDetailPersistenceRegressionResult Result, string Name, Action Test)
        {
            try
            {
                Test();
                Result.PassedScenarios.Add(Name);
            }
            catch (Exception Problem)
            {
                Result.Failures.Add(Name + ": " + Problem);
            }
        }

        private static void TestV2DtoSerializedPresence()
        {
            var ExpectedId = Guid.NewGuid().ToString("D");
            var Definition = new DomainJsonElement
            {
                Entity = "conceptDefinition",
                Name = "Probe Concept Definition",
                TechName = "Probe_Concept_Definition",
                DetailDesignatorsSpecified = true
            };
            Definition.DetailDesignators.Add(new DomainJsonDetailDesignator
            {
                Id = ExpectedId,
                Kind = "table",
                Name = "Probe Detail",
                TechName = ProbeTechName,
                Summary = "Regression sentinel",
                TableDefinitionTechName = "Standard",
                TableDefinitionIsOwned = false,
                Order = 1,
                Appearance = new Dictionary<string, object>
                {
                    { "isDisplayed", true },
                    { "showTitle", false },
                    { "isMultiRecord", false },
                    { "layout", ETableLayoutStyle.Transposed.ToString() },
                    { "showFieldTitles", false }
                }
            });

            var Document = new DomainJsonDocument();
            Document.ConceptDefinitions.Add(Definition);

            var Json = DomainJsonSerializer.Serialize(Document);
            Require(Json.IndexOf("\"formatVersion\": 2", StringComparison.Ordinal) >= 0,
                    "Domain JSON did not serialize as formatVersion 2");
            Require(Json.IndexOf("\"detailDesignators\": [", StringComparison.Ordinal) >= 0,
                    "Domain JSON omitted the explicit detailDesignators collection");
            Require(Json.IndexOf(ProbeTechName, StringComparison.Ordinal) >= 0,
                    "Domain JSON omitted the Detail Designator payload");

            var Rehydrated = DomainJsonSerializer.Deserialize(Json);
            DomainJsonSerializer.Validate(Rehydrated);
            var ActualDefinition = Rehydrated.ConceptDefinitions.Single();
            var Actual = ActualDefinition.DetailDesignators.Single();
            Require(ActualDefinition.DetailDesignatorsSpecified,
                    "detailDesignators field presence was not retained");
            Require(Actual.Id == ExpectedId && Actual.Kind == "table" &&
                    Actual.TechName == ProbeTechName && Actual.TableDefinitionTechName == "Standard" &&
                    Actual.TableDefinitionIsOwned == false && Actual.Order == 1,
                    "Detail Designator identity/reference/order changed during DTO serialization");
            Require(Convert.ToString(Actual.Appearance["layout"]) == ETableLayoutStyle.Transposed.ToString(),
                    "Detail Designator appearance changed during DTO serialization");
        }

        private static void TestStableIdSharedTableAndOrderAfterRehydrate()
        {
            var Source = Domain.Create(null);
            var SourceOwner = FindGenericConceptDefinition(Source);
            var SharedTable = Source.DefaultTableDef;
            var ExpectedId = Guid.NewGuid();
            var ExpectedOrder = 1;

            var Probe = new TableDetailDesignator(Ownership.Create<IdeaDefinition, Idea>(SourceOwner),
                                                  SharedTable, false,
                                                  "Persistence Probe Detail", ProbeTechName,
                                                  "Regression sentinel");
            Probe.GlobalId = ExpectedId;
            Probe.Alterability = EAlterability.Definition;
            Probe.TableLook.IsDisplayed = true;
            Probe.TableLook.ShowTitle = false;
            Probe.TableLook.IsMultiRecord = false;
            Probe.TableLook.Layout = ETableLayoutStyle.Transposed;
            Probe.TableLook.ShowFieldTitles = false;
            SourceOwner.DetailDesignators.Insert(ExpectedOrder, Probe);

            var Exported = DomainJsonExporter.Export(Source);
            Require(Exported.FormatVersion == DomainJsonDocument.CurrentFormatVersion,
                    "native export did not use the current Domain JSON format");
            var ExportedOwner = FindDefinition(Exported, SourceOwner.TechName);
            var ExportedProbe = ExportedOwner.DetailDesignators.Single(Item => Item.TechName == ProbeTechName);
            Require(ExportedOwner.DetailDesignatorsSpecified,
                    "native export omitted detailDesignators field presence");
            Require(ExportedProbe.Id == ExpectedId.ToString("D") &&
                    ExportedProbe.TableDefinitionId == SharedTable.GlobalId.ToString("D") &&
                    ExportedProbe.TableDefinitionTechName == SharedTable.TechName &&
                    ExportedProbe.Order == ExpectedOrder,
                    "native export changed Detail Designator identity, table reference, or order");

            var Serialized = DomainJsonSerializer.Serialize(Exported);
            var Parsed = DomainJsonSerializer.Deserialize(Serialized);
            DomainJsonSerializer.Validate(Parsed);

            var Target = Domain.Create(null);
            var Report = DomainJsonImporter.ApplyPreservingIdsFromValidatedDocument(
                Target, Parsed, new DomainJsonImportReport { QuietLogging = true });
            Require(Report.Errors.Count == 0,
                    "native rehydration reported errors: " + String.Join(" | ", Report.Errors.ToArray()));

            var TargetOwner = Target.ConceptDefinitions.Single(Item => Item.TechName == SourceOwner.TechName);
            var TargetTable = Target.TableDefinitions.Single(Item => Item.TechName == SharedTable.TechName);
            var TargetProbe = TargetOwner.DetailDesignators.OfType<TableDetailDesignator>()
                                                          .Single(Item => Item.TechName == ProbeTechName);
            Require(TargetProbe.GlobalId == ExpectedId,
                    "native rehydration changed the stable Detail Designator id");
            Require(Object.ReferenceEquals(TargetProbe.DeclaringTableDefinition, TargetTable),
                    "native rehydration did not resolve the shared Domain TableDefinition by reference");
            Require(TargetOwner.DetailDesignators.IndexOf(TargetProbe) == ExpectedOrder,
                    "native rehydration changed Detail Designator order");
            Require(TargetProbe.Alterability == EAlterability.Definition &&
                    !TargetProbe.TableDefIsOwned && !TargetProbe.TableLook.ShowTitle &&
                    !TargetProbe.TableLook.IsMultiRecord &&
                    TargetProbe.TableLook.Layout == ETableLayoutStyle.Transposed &&
                    !TargetProbe.TableLook.ShowFieldTitles,
                    "native rehydration changed Detail Designator alterability, ownership, or appearance");
        }

        private static void TestV1OmissionPreservesDefaults()
        {
            var Source = Domain.Create(null);
            var Document = DomainJsonExporter.Export(Source);
            Document.FormatVersion = 1;
            foreach (var Definition in Document.ConceptDefinitions.Concat(Document.RelationshipDefinitions))
            {
                Definition.DetailDesignatorsSpecified = false;
                Definition.DetailDesignators.Clear();
            }

            var Json = DomainJsonSerializer.Serialize(Document);
            Require(Json.IndexOf("\"detailDesignators\"", StringComparison.Ordinal) < 0,
                    "v1 compatibility document unexpectedly serialized detailDesignators");
            var Parsed = DomainJsonSerializer.Deserialize(Json);
            DomainJsonSerializer.Validate(Parsed);

            var Target = Domain.Create(null);
            var TargetOwnerBefore = FindGenericConceptDefinition(Target);
            var ExpectedDefaults = TargetOwnerBefore.DetailDesignators
                                                    .Select(Item => Item.TechName)
                                                    .ToArray();
            Require(ExpectedDefaults.Length > 0, "test Domain had no constructor Detail Designators");

            var Report = DomainJsonImporter.ApplyPreservingIdsFromValidatedDocument(
                Target, Parsed, new DomainJsonImportReport { QuietLogging = true });
            Require(Report.Errors.Count == 0,
                    "v1 rehydration reported errors: " + String.Join(" | ", Report.Errors.ToArray()));

            var TargetOwnerAfter = FindGenericConceptDefinition(Target);
            Require(TargetOwnerAfter.DetailDesignators.Select(Item => Item.TechName).SequenceEqual(ExpectedDefaults),
                    "v1 omission cleared or reordered constructor Detail Designators");
        }

        private static void TestOperationPreviewWithPlannedTable()
        {
            var Target = Domain.Create(null);
            var Owner = FindGenericConceptDefinition(Target);
            var PlannedFieldId = Guid.NewGuid().ToString("D");
            var Document = new DomainJsonDocument();
            Document.Operations.Add(new DomainJsonOperation
            {
                Op = "create",
                Entity = "tableDefinition",
                TechName = "Patch_Planned_Table",
                Set = new Dictionary<string, object>
                {
                    { "name", "Patch Planned Table" }
                }
            });
            Document.Operations.Add(new DomainJsonOperation
            {
                Op = "create",
                Entity = "fieldDefinition",
                Id = PlannedFieldId,
                TechName = "Patch_Planned_Field",
                OwnerTechName = "Patch_Planned_Table",
                Set = new Dictionary<string, object>
                {
                    { "name", "Patch Planned Field" },
                    { "dataTypeTechName", "Text" }
                }
            });
            Document.Operations.Add(new DomainJsonOperation
            {
                Op = "create",
                Entity = "detailDesignator",
                TechName = "Patch_Planned_Detail",
                OwnerTechName = Owner.TechName,
                OwnerScope = "conceptDefinition",
                Set = new Dictionary<string, object>
                {
                    { "kind", "table" },
                    { "name", "Patch Planned Detail" },
                    { "tableDefinitionTechName", "Patch_Planned_Table" },
                    { "tableDefinitionIsOwned", false },
                    { "fieldDefinitionId", PlannedFieldId }
                }
            });

            var Preview = DomainJsonImporter.Preview(Target, Document);
            Require(Preview.Errors.Count == 0 && Preview.PlannedSkipped == 0 && Preview.PlannedCreated == 3,
                    "preview rejected a Detail that references a table/field created earlier in the same patch: " +
                    String.Join(" | ", Preview.SkippedMessages.ToArray()));

            var Apply = DomainJsonImporter.Apply(Target, Document);
            Require(Apply.Errors.Count == 0 && Apply.AppliedSkipped == 0 && Apply.AppliedCreated == 3,
                    "apply did not match the successful same-patch preview: " +
                    String.Join(" | ", Apply.SkippedMessages.ToArray()));
            var Table = Target.TableDefinitions.Single(Item => Item.TechName == "Patch_Planned_Table");
            var Field = Table.FieldDefinitions.Single(Item => Item.TechName == "Patch_Planned_Field");
            var Detail = Owner.DetailDesignators.OfType<TableDetailDesignator>()
                              .Single(Item => Item.TechName == "Patch_Planned_Detail");
            Require(Object.ReferenceEquals(Detail.DeclaringTableDefinition, Table) &&
                    Object.ReferenceEquals(Detail.ContainedTableSubOwner, Field),
                    "same-patch Detail did not resolve the newly created field by its requested patch ID during apply");

            var InvalidKindDocument = new DomainJsonDocument();
            InvalidKindDocument.Operations.Add(new DomainJsonOperation
            {
                Op = "create",
                Entity = "detailDesignator",
                TechName = "Unsupported_Patch_Detail",
                OwnerTechName = Owner.TechName,
                OwnerScope = "conceptDefinition",
                Set = new Dictionary<string, object>
                {
                    { "kind", "unsupported" },
                    { "name", "Unsupported Patch Detail" }
                }
            });
            var InvalidPreview = DomainJsonImporter.Preview(Target, InvalidKindDocument);
            var InvalidApply = DomainJsonImporter.Apply(Target, InvalidKindDocument);
            Require(InvalidPreview.PlannedCreated == 0 && InvalidPreview.PlannedSkipped == 1 &&
                    InvalidApply.AppliedCreated == 0 && InvalidApply.AppliedSkipped == 1,
                    "unsupported Detail kind did not produce matching preview/apply skips");

            var MissingTableDocument = new DomainJsonDocument();
            MissingTableDocument.Operations.Add(new DomainJsonOperation
            {
                Op = "create",
                Entity = "conceptDefinition",
                TechName = "Patch_Planned_Owner",
                Set = new Dictionary<string, object> { { "name", "Patch Planned Owner" } }
            });
            MissingTableDocument.Operations.Add(new DomainJsonOperation
            {
                Op = "create",
                Entity = "detailDesignator",
                TechName = "Missing_Table_Detail",
                OwnerTechName = "Patch_Planned_Owner",
                OwnerScope = "conceptDefinition",
                Set = new Dictionary<string, object>
                {
                    { "kind", "table" },
                    { "name", "Missing Table Detail" },
                    { "tableDefinitionTechName", "Missing_Table" }
                }
            });
            MissingTableDocument.Operations.Add(new DomainJsonOperation
            {
                Op = "create",
                Entity = "detailDesignator",
                TechName = "Missing_Field_Detail",
                OwnerTechName = "Patch_Planned_Owner",
                OwnerScope = "conceptDefinition",
                Set = new Dictionary<string, object>
                {
                    { "kind", "table" },
                    { "name", "Missing Field Detail" },
                    { "tableDefinitionTechName", Target.DefaultTableDef.TechName },
                    { "fieldDefinitionTechName", "Missing_Field" }
                }
            });
            var MissingTablePreview = DomainJsonImporter.Preview(Domain.Create(null), MissingTableDocument);
            Require(MissingTablePreview.PlannedCreated == 1 && MissingTablePreview.PlannedSkipped == 2,
                    "planned-owner preview accepted a Detail whose referenced table or field does not exist");

            var MissingFieldDocument = new DomainJsonDocument();
            MissingFieldDocument.Operations.Add(new DomainJsonOperation
            {
                Op = "create",
                Entity = "detailDesignator",
                TechName = "Existing_Owner_Missing_Field",
                OwnerTechName = Owner.TechName,
                OwnerScope = "conceptDefinition",
                Set = new Dictionary<string, object>
                {
                    { "kind", "table" },
                    { "name", "Existing Owner Missing Field" },
                    { "tableDefinitionTechName", Target.DefaultTableDef.TechName },
                    { "fieldDefinitionTechName", "Missing_Field" }
                }
            });
            var MissingFieldPreview = DomainJsonImporter.Preview(Target, MissingFieldDocument);
            var MissingFieldApply = DomainJsonImporter.Apply(Target, MissingFieldDocument);
            Require(MissingFieldPreview.PlannedCreated == 0 && MissingFieldPreview.PlannedSkipped == 1 &&
                    MissingFieldApply.AppliedCreated == 0 && MissingFieldApply.AppliedSkipped == 1,
                    "missing contained-field reference did not produce matching preview/apply skips");
        }

        private static void TestRelationshipOwnedTableIdAfterRehydrate()
        {
            var Source = Domain.Create(null);
            var SourceOwner = FindGenericRelationshipDefinition(Source);
            var OwnedTable = new TableDefinition(Source, "Owned Probe Table", "Owned_Probe_Table",
                                                 "Regression sentinel");
            var OwnedField = new FieldDefinition(OwnedTable, "Owned Probe Field", "Owned_Probe_Field",
                                                 DataType.DataTypeText, "Regression sentinel");
            OwnedTable.FieldDefinitions.Add(OwnedField);
            OwnedTable.AlterStructure();
            Source.TableDefinitions.Add(OwnedTable);
            var ExpectedDesignatorId = Guid.NewGuid();

            var Probe = new TableDetailDesignator(Ownership.Create<IdeaDefinition, Idea>(SourceOwner),
                                                  OwnedTable, true,
                                                  "Owned Relationship Probe", "Owned_Relationship_Probe",
                                                  "Regression sentinel");
            Probe.GlobalId = ExpectedDesignatorId;
            Probe.ContainedTableSubOwner = OwnedField;
            SourceOwner.DetailDesignators.Add(Probe);

            var Exported = DomainJsonExporter.Export(Source);
            var ExportedOwner = Exported.RelationshipDefinitions.Single(Item => Item.TechName == SourceOwner.TechName);
            var ExportedProbe = ExportedOwner.DetailDesignators.Single(Item => Item.TechName == Probe.TechName);
            Require(ExportedProbe.TableDefinitionIsOwned == true &&
                    ExportedProbe.TableDefinitionId == OwnedTable.GlobalId.ToString("D") &&
                    ExportedProbe.FieldDefinitionId == OwnedField.GlobalId.ToString("D"),
                    "top-level table or contained-field identity was discarded because the Detail marked it as owned");

            var Target = Domain.Create(null);
            var Parsed = DomainJsonSerializer.Deserialize(DomainJsonSerializer.Serialize(Exported));
            var Report = DomainJsonImporter.ApplyPreservingIdsFromValidatedDocument(
                Target, Parsed, new DomainJsonImportReport { QuietLogging = true });
            Require(Report.Errors.Count == 0,
                    "relationship Detail rehydration reported errors: " +
                    String.Join(" | ", Report.Errors.ToArray()));

            var TargetOwner = FindGenericRelationshipDefinition(Target);
            var TargetTable = Target.TableDefinitions.Single(Item => Item.TechName == OwnedTable.TechName);
            var TargetProbe = TargetOwner.DetailDesignators.OfType<TableDetailDesignator>()
                                         .Single(Item => Item.TechName == Probe.TechName);
            var TargetField = TargetTable.FieldDefinitions.Single(Item => Item.TechName == OwnedField.TechName);
            Require(TargetProbe.GlobalId == ExpectedDesignatorId && TargetProbe.TableDefIsOwned &&
                    Object.ReferenceEquals(TargetProbe.DeclaringTableDefinition, TargetTable) &&
                    Object.ReferenceEquals(TargetProbe.ContainedTableSubOwner, TargetField),
                    "relationship Detail did not retain its stable id, owned flag, table reference, or contained field");
        }

        private static void TestUnresolvedContainedFieldAbortsExactReplacement()
        {
            var Source = Domain.Create(null);
            var Document = DomainJsonExporter.Export(Source);
            var SourceOwner = FindGenericConceptDefinition(Source);
            var SourceDefinition = FindDefinition(Document, SourceOwner.TechName);
            var TableDetail = SourceDefinition.DetailDesignators.Single(Item => Item.Kind == "table");
            TableDetail.FieldDefinitionId = null;
            TableDetail.FieldDefinitionTechName = "Missing_Contained_Field";

            var Target = Domain.Create(null);
            var TargetOwner = FindGenericConceptDefinition(Target);
            var ExpectedDetails = TargetOwner.DetailDesignators.Select(Item => Item.TechName).ToArray();
            var Report = DomainJsonImporter.ApplyPreservingIds(
                Target, Document, new DomainJsonImportReport { QuietLogging = true });

            Require(Report.Errors.Count > 0,
                    "authoritative replacement accepted an unresolved contained-field reference");
            Require(TargetOwner.DetailDesignators.Select(Item => Item.TechName).SequenceEqual(ExpectedDetails),
                    "failed authoritative Detail preflight changed or cleared the existing collection");
        }

        private static void TestV2ExplicitEmptyIsExact()
        {
            var Source = Domain.Create(null);
            var Document = DomainJsonExporter.Export(Source);
            var SourceOwner = FindGenericConceptDefinition(Source);
            var SourceDefinition = FindDefinition(Document, SourceOwner.TechName);
            SourceDefinition.DetailDesignatorsSpecified = true;
            SourceDefinition.DetailDesignators.Clear();

            var Json = DomainJsonSerializer.Serialize(Document);
            var Parsed = DomainJsonSerializer.Deserialize(Json);
            DomainJsonSerializer.Validate(Parsed);
            var ParsedDefinition = FindDefinition(Parsed, SourceOwner.TechName);
            Require(ParsedDefinition.DetailDesignatorsSpecified &&
                    ParsedDefinition.DetailDesignators.Count == 0,
                    "v2 explicit-empty detailDesignators presence was not retained");

            var Target = Domain.Create(null);
            Require(FindGenericConceptDefinition(Target).DetailDesignators.Count > 0,
                    "test Domain had no constructor Detail Designators to clear");
            var Report = DomainJsonImporter.ApplyPreservingIdsFromValidatedDocument(
                Target, Parsed, new DomainJsonImportReport { QuietLogging = true });
            Require(Report.Errors.Count == 0,
                    "v2 explicit-empty rehydration reported errors: " + String.Join(" | ", Report.Errors.ToArray()));
            Require(FindGenericConceptDefinition(Target).DetailDesignators.Count == 0,
                    "v2 explicit-empty detailDesignators did not replace constructor defaults exactly");

            var Reexported = DomainJsonExporter.Export(Target);
            var ReexportedDefinition = FindDefinition(Reexported, SourceOwner.TechName);
            Require(ReexportedDefinition.DetailDesignatorsSpecified &&
                    ReexportedDefinition.DetailDesignators.Count == 0,
                    "v2 explicit-empty detailDesignators did not remain exact after re-export");
        }

        private static void TestMalformedExplicitListIsRejected()
        {
            var InvalidValues = new[]
            {
                "null",
                "{}",
                "\"not-an-array\"",
                "[null]",
                "[{}]",
                "[{\"kind\":\"unknown\",\"name\":\"Probe\",\"techName\":\"Probe\"}]"
            };
            foreach (var InvalidValue in InvalidValues)
            {
                var Json = "{\"format\":\"" + DomainJsonDocument.CurrentFormat +
                           "\",\"formatVersion\":2,\"conceptDefinitions\":[{" +
                           "\"name\":\"Probe\",\"techName\":\"Probe\"," +
                           "\"detailDesignators\":" + InvalidValue + "}]}";
                var WasRejected = false;
                try
                {
                    DomainJsonSerializer.Deserialize(Json);
                }
                catch (System.IO.InvalidDataException)
                {
                    WasRejected = true;
                }

                Require(WasRejected,
                        "malformed explicit detailDesignators value was accepted as an authoritative empty list: " +
                        InvalidValue);
            }
        }

        private static ConceptDefinition FindGenericConceptDefinition(Domain Domain)
        {
            return Domain.ConceptDefinitions.Single(Item => Item.TechName == Concept.__ClassDefinitor.TechName);
        }

        private static RelationshipDefinition FindGenericRelationshipDefinition(Domain Domain)
        {
            return Domain.RelationshipDefinitions.Single(Item => Item.TechName == Relationship.__ClassDefinitor.TechName);
        }

        private static DomainJsonElement FindDefinition(DomainJsonDocument Document, string TechName)
        {
            return Document.ConceptDefinitions.Single(Item => Item.TechName == TechName);
        }

        private static void Require(bool Condition, string Message)
        {
            if (!Condition)
                throw new InvalidOperationException(Message);
        }
    }
}
