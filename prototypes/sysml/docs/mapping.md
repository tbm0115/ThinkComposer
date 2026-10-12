# Model mappings and authoring rules

## Ownership and identity

A Domain defines metatypes; a Composition contains model elements. `Block` is a Concept Definition. `Device` is a concept using that definition. `Generalization` is a native relationship between model elements, independent of ThinkComposer definition inheritance. MTConnect Domains are self-contained generated extensions of the generic definitions.

`containerId` records semantic ownership. Ordering follows the persisted collection order and ordered Detail records; source order is additionally recorded in `ModelMetadata.SourceOrder`. Moving a visual does not reparent a model element. A shortcut is another visual representation of the same identity, never a duplicate model element.

New example identities are reproducible UUIDv5 values. Imported objects with an XMI ID use UUIDv5 of `MTConnect-source:` plus that ID; source objects without IDs use their positional source path. Renaming an existing object leaves its native identity and `SourceId` intact. Moving an anonymous XML node before reimport can change its generated identity; this importer is a snapshot importer, not an incremental reconciliation engine. Duplicate source IDs must be resolved before trusting references.

XMI export uses `SourceId` when supplied, otherwise `tc_` plus the native GlobalId. Text output uses the editable TechName for language names and native/source identities in comments. Prototype text names must be simple ASCII identifiers, unique within their owner. Qualified names, quoted names and overload resolution require a later mapping/validation pass. Single Typing/Generalization edges resolve the target's current TechName, so renaming the target updates emitted references. Multiple supertypes and cross-namespace relationships are outside that proven subset.

## Native data layout

Each definition has two editable Detail tables. `ModelMetadata` has one record containing export selectors and descriptive values. `Attributes` is an ordered Key/Value table retaining source XML attributes, tagged values and blocked modeling data. Values in markers or drawing labels alone never control export.

`XmlTag`, `UmlType`, `HasName`, `TextKeyword` and `KerMLKeyword` are explicit mapping selectors. They are not inferred type checking. The supplied examples initialize them. A newly created palette item may need its Detail record populated; the Domain cannot promise all required defaults through the tested JSON path. Copy a known example or use the reproducible helper, then check the output coverage report.

For v1 import, source attributes live authoritatively in `Attributes`. Convenience metadata such as Normative, Introduced or ImplementationXml is a searchable mirror. Edit the original attribute record to change exported source content; mirrors are not automatically synchronized. XML names and reference strings must be validated. Generalization is the exception: its `general` target comes from the native graph edge, not a stale attribute mirror.

No array is sorted merely to make export deterministic. Source ownership traversal and table rows retain order. The ModelingLab's connector ends and message order provide concrete examples of ordered data that is persisted but does not acquire formal SysML semantics.

## XMI subset

The templates emit a single `xmi:XMI` document using the supplied UML/XMI 20131001 namespaces and SysML 20181001 namespace. The example profile URI ends in `#SysML`, matching the reference input. Namespace declarations alone do not prove a profile is installed or correctly resolved by Cameo.

The generic Survey demonstrates Model/Package containment, Class plus Block stereotype, DataType plus ValueType stereotype, Enumeration/literal, owned Property with local type reference, Class generalization, and Class-based Requirement/ConstraintBlock applications. Activities, StateMachines, Interactions and UseCases in that Survey are container declarations only. Their executable contents and detailed notation are not implied by successful export.

The full importer retains every supported source-shaped node and its attributes, including stereotype applications, nested profile content, ownership, documentation and many relationship encodings. A local UML Generalization becomes a true native relationship. Other source relations are preserved as structured editable concepts unless explicitly mapped; textual endpoints do not count as native graph semantics. Unknown UML metaclasses have their original data preserved but their export selector disabled. Proprietary `xmi:Extension` subtrees are inventoried and left in the unchanged source file. No Cameo diagram encoding is generated.

The XML checker verifies identities, recognized local references, emitted-content coverage and selected stereotype base mappings. It does not implement the entire UML metamodel, XMI schema or profile constraints. Unresolved external profiles/libraries remain unresolved. In particular, `Profile:normative` in the small MTConnect Survey demonstrates application attributes but does not package a complete portable MTConnect profile definition. That export is not certified importable by Cameo.

## SysML and KerML text subsets

SysML text supports nested packages, basic part/attribute/item definitions and usages, one locally resolvable typing edge, one specialization edge, documentation and identity comments. The Survey also emits action, state, requirement, constraint, calculation and metadata definition/usage skeletons. Bodies without implemented behavior or expressions are explicitly recorded as reduced support in coverage; they are not execution models.

KerML has a separate template and mapping: Package becomes `package`; PartDefinition/ItemDefinition becomes `class`; AttributeDefinition becomes `datatype`; corresponding usages become `feature`. Local typing and specialization remain. This loses SysML-specific occurrence/part semantics. Ports, actions, states, requirements, constraints and metadata semantics are excluded from this KerML subset; their missing IDs appear in its companion report.

`TextContent` becomes a documentation comment. A literal comment terminator is rendered as `* /`; the original stays in model data. Concept Modeler tokens such as `{{term(Component)}}` remain literal documentation, not resolved hyperlinks or ontology expressions. Domain templates perform the actual generation. Python only authors input packages, runs the installed CLI and checks its output.

The text templates traverse at most 12 levels below ModelDocument. They emit a depth-limit diagnostic when exceeded. A companion report must fail such output. This bounded expansion avoids relying on unproven recursive template behavior. The renderer's `DetailValue` filter works for metadata; chained table indexing failed the feasibility experiment and is not used.

## MTConnect extensions

The extension recipe is `MT_STEREOTYPES` plus `make_domain(..., mt=True)` in `scripts/prototype.py`. It creates dedicated definitions for normative, informative, deprecated, extensible, observes, implementation format, MTConnect requirement, custom value type, array and organizer applications. It also creates ConceptRestriction, LiteralAnnotation and ConceptModelerAnnotation separately. Both generated variants contain these definitions without referencing another Domain file.

The source report inventories each applied stereotype and all its attribute names. Dedicated fields include Normative, Informative, Deprecated, Extensible, Introduced, DeprecatedVersion, Observes, ImplementationXml/Json, SimpleRequirement, SemanticRequirement, IsArray, ArraySize and resource/version IRIs. Original profile values remain in ordered Attributes records, including unknown fields. In v2 these are prototype metadata carriers; formal metadata application export is blocked, not silently equated with v1 stereotype semantics.

Concept Modeler Restriction, Literal_Annotation, Equivalent_Class, Disjoint_With, Concept_Model, Resource, Transitive, Functional, Equivalent_Property and Anything applications remain separate carriers. OWL reasoning, cardinality restrictions and ontology equivalence are not implemented. Their original base references and attributes are retained; editable carrier data is only partial mapping support.

## Full reference accounting

`source-ledger.jsonl.gz` contains one record for each XML element, with source path, ID, type, mapping status, reason and native target when available. `references.jsonl.gz` covers recognized semantic reference attributes in mapped content and native Generalization endpoints. It is not a proprietary diagram-reference validator. The report's external dependency inventory also includes URIs found inside unsupported source subtrees.

Statuses mean: **mapped** = native identity/ownership/data mapping, not full language semantics; **partial** = editable carrier with reduced semantics; **unresolved** = external reference kept without fetching its definition; **preserved-only** = recoverable source or unsupported metaclass carrier; **unsupported** = source subtree inventoried but not imported as editable model content. Original bytes and source paths are the recovery mechanism for unsupported XML. No arbitrary dependency is fetched and no missing definition is invented.

The v1/v2 slices are curated examples, with exact selection and losses in `slice-provenance.json`. Same-named classes in different source packages are not conflated. They are an aid to discussing a future migration, not a complete MTConnect v2 conversion.
