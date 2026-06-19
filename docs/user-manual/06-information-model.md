# Appendix B: Composition Information Model

This appendix summarizes the information model exposed to Output Templates. Templates use this model to read composition, idea, domain, detail, table, and visual information.

![Composition information model associations](assets/manual/page-55-information-model-01.png)

![Composition information model inheritance](assets/manual/page-56-information-model-01.png)

## Special Access Patterns

| Content | Access | Example |
|---|---|---|
| Custom Fields | `_['<CustomFieldTechName>']` | `{{ _['Alias'] }}` |
| Details | `This['<DetailTechName>']` | `{{ This['MyTable'].Records.Size }}` |
| Table Records | `Record.FieldTechName` or `Record['FieldTechName']` | `{{ Person.Name }}; {{ Person['Age'] }}` |
| Domain Base Tables | `<Domain>.BaseContentRoot['<BaseTableTechName>']` | `{{ OwnerComposition.CompositionDefinitor.BaseContentRoot['States'].Records.Size }}` |

Example:

```liquid
The {{ This['People'].Records.Size }} persons are:
{% for Person in This['People'].Records %}
Name: {{ Person.Name }}; Age: {{ Person['Age'] }}
{% endfor %}
```

## Core Classes

| Class | Ancestor | Summary |
|---|---|---|
| `Attachment` | `ContainedDetail` | Embedded object from an external source, such as an image or file. |
| `AttachmentDetailDesignator` | `DetailDesignator` | Associates an attachment definition to an idea. |
| `Composition` | `Concept` | Semantic, informational, and visual set of ideas expressing knowledge about a subject. |
| `Concept` | `Idea` | Concrete object that can be associated to others through relationships. |
| `ConceptDefinition` | `IdeaDefinition` | Definition of a concept type. |
| `ConceptVisualRepresentation` | `VisualRepresentation` | Visual representation of a concept. |
| `ContainedDetail` | none | Object stored as detail for an idea. |
| `DetailDesignator` | `MetaDefinition` | Base ancestor for detailed-data designators. |
| `Domain` | `ConceptDefinition` | Metamodel for graph, visual, and information structures. |
| `FieldDefinition` | `MetaDefinition` | Defines a table field. |
| `FormalElement` | `UniqueElement` | Standard object with name, TechName, summary, TechSpec, rich description, and version. |
| `FormalPresentationElement` | `FormalElement` | Standard object with visual representation. |
| `Idea` | `FormalPresentationElement` | Base composition element from which concepts and relationships descend. |
| `IdeaDefinition` | `MetaDefinition` | Shared ancestor for concept and relationship definitions. |
| `InternalLink` | `Link` | References an internal property. |
| `Link` | `ContainedDetail` | References an external or internal object. |
| `LinkDetailDesignator` | `DetailDesignator` | Associates a link definition to an idea. |
| `LinkRoleDefinition` | `MetaDefinition` | Defines a relationship link role. |
| `MarkerAssignment` | none | Assignment of a marker to an idea, optionally with descriptor. |
| `MetaDefinition` | `FormalPresentationElement` | Definition-level object used to create schema objects. |
| `ModelDefinition` | none | Base classifier/member definition. |
| `Relationship` | `Idea` | Association between ideas through role-based links. |
| `RelationshipDefinition` | `IdeaDefinition` | Definition of a relationship type. |
| `RelationshipVisualRepresentation` | `VisualRepresentation` | Visual representation of a relationship. |
| `ResourceLink` | `Link` | References a file, folder, web address, or other external resource. |
| `RoleBasedLink` | `UniqueElement` | Links a relationship to a related idea through a role definition. |
| `SimpleElement` | none | Basic object with name, TechName, summary, and TechSpec. |
| `SimplePresentationElement` | `SimpleElement` | Simple object with a visual representation. |
| `Table` | `ContainedDetail` | Structured detail containing records. |
| `TableDefinition` | `MetaDefinition` | Defines a table structure. |
| `TableDetailDesignator` | `DetailDesignator` | Associates table structures to an idea, definition, or field. |
| `TableRecord` | none | One structured record inside a table. |
| `UniqueElement` | none | Object with a global unique identifier. |
| `VersionCard` | none | Version-control information for versionable objects. |
| `View` | `FormalElement` | Visual representation of the composite content of a composition or idea. |
| `VisualComplement` | `VisualObject` | Visual object exposing attached information such as notes, callouts, legends, and info-cards. |
| `VisualConnector` | `VisualElement` | Visual connection between symbols. |
| `VisualElement` | `VisualObject` | Base ancestor for visual representators such as symbols and connectors. |
| `VisualRepresentation` | `UniqueElement` | Groups visual elements that represent an idea in a view. |
| `VisualSymbol` | `VisualElement` | Base ancestor for visual symbols. |

## Common Properties

### `FormalElement`

| Property | Type | Summary |
|---|---|---|
| `Description` | `String` | Rich detailed text. |
| `Name` | `String` | User-facing title. |
| `NameCaption` | `String` | Single-line display name. |
| `Summary` | `String` | Short summary. |
| `TechName` | `String` | Stable technical identifier. |
| `TechSpec` | `String` | Technical text for scripts, templates, formulas, or generated output. |
| `Version` | `VersionCard` | Version metadata. |

### `Idea`

| Property | Type | Summary |
|---|---|---|
| `_` | `TableRecord` | Alias of the custom-fields record. |
| `AssociatingLinks` | `EditableList<RoleBasedLink>` | Links associating this idea to relationships. |
| `CompositeIdeas` | `EditableList<Idea>` | Child ideas when this idea is composite. |
| `CompositeViews` | `EditableList<View>` | Views for contained child ideas. |
| `Details` | `EditableList<ContainedDetail>` | Contained details. |
| `IncomingLinks` | `IEnumerable<RoleBasedLink>` | Links targeting this idea. |
| `OutgoingLinks` | `IEnumerable<RoleBasedLink>` | Links originating from this idea. |
| `OwnerComposition` | `Composition` | Owning composition. |
| `OwnerContainer` | `Idea` | Owning composite idea. |
| `RelatedFrom` | `IEnumerable<Idea>` | Ideas pointing to this idea. |
| `RelatingTo` | `IEnumerable<Idea>` | Ideas pointed to by this idea. |
| `This` | `Idea` | Self reference supporting detail indexer access. |
| `VisualRepresentators` | `EditableList<VisualRepresentation>` | Visual representations of this idea. |

### `Composition`

| Property | Type | Summary |
|---|---|---|
| `ActiveView` | `View` | Active view. |
| `CompositionDefinitor` | `Domain` | Domain definition used by the composition. |
| `RootView` | `View` | Initial central view. |
| `UsedDomains` | `EditableList<Domain>` | Domains used by the composition. |
| `ViewsPrefix` | `String` | Prefix for related view names. |

### `Domain`

| Property | Type | Summary |
|---|---|---|
| `BaseContentRoot` | `Concept` | Root for predefined base content such as base tables. |
| `DefaultTableDef` | `TableDefinition` | Default table structure. |
| `IdeaClusters` | `EditableList<SimplePresentationElement>` | Palette clusters for idea definitions. |
| `LinkRoleVariants` | `EditableList<SimplePresentationElement>` | Predefined role variants such as multiplicities. |
| `MarkerClusters` | `EditableList<SimplePresentationElement>` | Palette clusters for marker definitions. |
| `OwnerComposition` | `Composition` | Composition owning this domain instance. |
| `ViewBackgroundImage` | `ImageSource` | Initial background for views. |

### `IdeaDefinition`

| Property | Type | Summary |
|---|---|---|
| `CanAutomaticallyCreateGroupedConcepts` | `Boolean` | Allows automatic grouped concept creation. |
| `CanAutomaticallyCreateRelatedConcepts` | `Boolean` | Allows automatic related concept creation. |
| `CanGroupIntersectingObjects` | `Boolean` | Allows grouping objects intersecting a symbol or group region. |
| `CompositeContentDomain` | `Domain` | Domain governing composite content. |
| `ConceptDefinitions` | `EditableList<ConceptDefinition>` | Child concept definitions. |
| `CustomFieldsTableDef` | `TableDefinition` | Custom field structure. |
| `DetailDesignators` | `EditableList<DetailDesignator>` | Declared detail designators. |
| `HasGroupLine` | `Boolean` | Creates ideas with an appended Group Line. |
| `HasGroupRegion` | `Boolean` | Creates ideas with an appended Group Region. |
| `IsComposable` | `Boolean` | Allows ideas of this definition to contain others. |
| `IsVersionable` | `Boolean` | Enables version metadata. |
| `RelationshipDefinitions` | `EditableList<RelationshipDefinition>` | Child relationship definitions. |
| `RepresentativeShape` | `String` | Shape used for visual symbols. |
| `TableDefinitions` | `EditableList<TableDefinition>` | Declared table structures. |

### `Relationship`

| Property | Type | Summary |
|---|---|---|
| `DescriptiveCaption` | `String` | Short text describing relationship links. |
| `IsAutoReference` | `Boolean` | Indicates a relationship can link an idea to itself. |
| `IsAutoReferenceExclusive` | `Boolean` | Indicates all links point from/to the same idea. |
| `Links` | `EditableList<RoleBasedLink>` | Implemented links. |
| `OriginIdeas` | `IEnumerable<Idea>` | Source/participant ideas. |
| `OriginLinks` | `IEnumerable<RoleBasedLink>` | Source/participant links. |
| `RelationshipDefinitor` | `Assignment<RelationshipDefinition>` | Relationship definition assignment. |
| `TargetIdeas` | `IEnumerable<Idea>` | Target ideas. |
| `TargetLinks` | `IEnumerable<RoleBasedLink>` | Target links. |

### `RelationshipDefinition`

| Property | Type | Summary |
|---|---|---|
| `AncestorRelationshipDef` | `RelationshipDefinition` | Ancestor relationship definition. |
| `HideCentralSymbolWhenSimple` | `Boolean` | Hides the central symbol for simple relationships. |
| `IsDirectional` | `Boolean` | Indicates source-to-target semantics. |
| `IsSimple` | `Boolean` | Allows one source and one target link. |
| `OriginOrParticipantLinkRoleDef` | `LinkRoleDefinition` | Origin or participant role definition. |
| `ShowNameIfHidingCentralSymbol` | `Boolean` | Shows the name when central symbol is hidden. |
| `TargetLinkRoleDef` | `LinkRoleDefinition` | Target role definition. |

### `Table`

| Property | Type | Summary |
|---|---|---|
| `Count` | `Int32` | Number of records. |
| `Definition` | `TableDefinition` | Table structure definition. |
| `Records` | `EditableList<TableRecord>` | Records in the table. |
| `RecordsLabel` | `String` | Text representation of the first records. |

### `View`

| Property | Type | Summary |
|---|---|---|
| `BackgroundImage` | `ImageSource` | View background image. |
| `GridSize` | `Double` | Grid size from 2 to 20 pixels. |
| `GridUsesLines` | `Boolean` | Uses line grid instead of points. |
| `OwnerCompositeContainer` | `Idea` | Composite idea owning this view. |
| `PageDisplayScale` | `Int32` | View page scale percentage. |
| `ShowConceptDefinitionLabels` | `Boolean` | Shows concept definition labels. |
| `ShowContextGrid` | `Boolean` | Shows the grid. |
| `ShowIndicators` | `Boolean` | Shows indicators over ideas. |
| `ShowMarkers` | `Boolean` | Shows markers. |
| `SnapToGrid` | `Boolean` | Aligns object positioning to grid points. |
| `ViewSize` | `Size` | View dimensions. |

### `VisualRepresentation`

| Property | Type | Summary |
|---|---|---|
| `DisplayingView` | `View` | View showing this representation. |
| `IsShortcut` | `Boolean` | Indicates the visual points to an idea outside the current container. |
| `MainSymbol` | `VisualSymbol` | Primary symbol of the representation. |
| `RepresentedIdea` | `Idea` | Represented idea. |

### `VisualSymbol`

| Property | Type | Summary |
|---|---|---|
| `AreDetailsShown` | `Boolean` | Indicates whether details are visible. |
| `BaseArea` | `Rect` | Symbol heading rectangle. |
| `BaseCenter` | `Point` | Symbol center. |
| `BaseHeight` | `Double` | Symbol body height. |
| `BaseLeft` | `Double` | Symbol body left position. |
| `BaseTop` | `Double` | Symbol body top position. |
| `BaseWidth` | `Double` | Symbol body width. |
| `Complements` | `EditableList<VisualComplement>` | Attached complements such as callouts. |
| `DetailsArea` | `Rect` | Details poster area. |
| `ShowCompositeContentAsDetails` | `Boolean` | Shows composite content in the details area. |
| `TotalArea` | `Rect` | Symbol plus details poster area. |

## Generic Collection Types

| Type | Summary |
|---|---|
| `EditableList<ItemType>` | Ordered collection. Relevant property: `Count`. |
| `EditableDictionary<KeyType, ValueType>` | Key-value collection. Relevant property: `Count`. |
| `Assignment<KeyType, ValueType>` | References a local writable object or an external read-only object. Relevant properties: `IsLocal`, `Value`. |
| `Ownership<GlobalType, LocalType>` | References global/shared or local/exclusive ownership. Relevant properties: `IsGlobal`, `Owner`. |
| `StoreBox<ContentType>` | Stores content that requires conversion to and from disk format. Relevant property: `Value`. |

`TableRecord` also exposes dynamic properties based on the Field Definitions created for its Table Definition. Use the special access patterns above when field names are easier to resolve by TechName.
