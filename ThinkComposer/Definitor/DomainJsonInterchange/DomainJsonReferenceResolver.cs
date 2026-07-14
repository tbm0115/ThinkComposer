// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Reference lookup helpers for Domain JSON merge.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Instrumind.Common;
using Instrumind.Common.Visualization;

using Instrumind.ThinkComposer.MetaModel;
using Instrumind.ThinkComposer.MetaModel.Configurations;
using Instrumind.ThinkComposer.MetaModel.GraphMetaModel;
using Instrumind.ThinkComposer.MetaModel.InformationMetaModel;

namespace Instrumind.ThinkComposer.Definitor.DomainJsonInterchange
{
    public class DomainJsonReferenceResolver
    {
        public DomainJsonReferenceResolver(Domain Domain)
        {
            this.Domain = Domain;
            this.ConceptDefinitions = new ReferenceIndex<ConceptDefinition>(() => this.Domain.ConceptDefinitions);
            this.RelationshipDefinitions = new ReferenceIndex<RelationshipDefinition>(() => this.Domain.RelationshipDefinitions);
            this.IdeaDefinitions = new ReferenceIndex<IdeaDefinition>(() => this.Domain.Definitions);
            this.TableDefinitions = new ReferenceIndex<TableDefinition>(() => this.Domain.TableDefinitions);
            this.ExternalLanguages = new ReferenceIndex<ExternalLanguageDeclaration>(() => this.Domain.ExternalLanguages);
            this.MarkerDefinitions = new ReferenceIndex<MarkerDefinition>(() => this.Domain.MarkerDefinitions);
            this.LinkRoleVariants = new ReferenceIndex<SimplePresentationElement>(() => this.Domain.LinkRoleVariants);
            this.MarkerClusters = new ReferenceIndex<SimplePresentationElement>(() => this.Domain.MarkerClusters);
            this.ConceptDefinitionClusters = new ReferenceIndex<FormalPresentationElement>(() => this.Domain.ConceptDefClusters);
            this.RelationshipDefinitionClusters = new ReferenceIndex<FormalPresentationElement>(() => this.Domain.RelationshipDefClusters);
            this.TableDefinitionCategories = new ReferenceIndex<MetaCategory<TableDefinition>>(() => this.Domain.TableDefCategories);
            this.FieldDefinitionCategories = new ReferenceIndex<MetaCategory<FieldDefinition>>(() => this.Domain.FieldDefCategories);
            this.AvailableDataTypes = new ReferenceIndex<DataType>(() => this.Domain.AvailableDataTypes);
        }

        public Domain Domain { get; private set; }

        private ReferenceIndex<ConceptDefinition> ConceptDefinitions { get; set; }
        private ReferenceIndex<RelationshipDefinition> RelationshipDefinitions { get; set; }
        private ReferenceIndex<IdeaDefinition> IdeaDefinitions { get; set; }
        private ReferenceIndex<TableDefinition> TableDefinitions { get; set; }
        private ReferenceIndex<ExternalLanguageDeclaration> ExternalLanguages { get; set; }
        private ReferenceIndex<MarkerDefinition> MarkerDefinitions { get; set; }
        private ReferenceIndex<SimplePresentationElement> LinkRoleVariants { get; set; }
        private ReferenceIndex<SimplePresentationElement> MarkerClusters { get; set; }
        private ReferenceIndex<FormalPresentationElement> ConceptDefinitionClusters { get; set; }
        private ReferenceIndex<FormalPresentationElement> RelationshipDefinitionClusters { get; set; }
        private ReferenceIndex<MetaCategory<TableDefinition>> TableDefinitionCategories { get; set; }
        private ReferenceIndex<MetaCategory<FieldDefinition>> FieldDefinitionCategories { get; set; }
        private ReferenceIndex<DataType> AvailableDataTypes { get; set; }
        private Dictionary<TableDefinition, ReferenceIndex<FieldDefinition>> FieldDefinitionsByOwner = new Dictionary<TableDefinition, ReferenceIndex<FieldDefinition>>();
        private Dictionary<RelationshipDefinition, ReferenceIndex<LinkRoleDefinition>> RelationshipRolesByOwner = new Dictionary<RelationshipDefinition, ReferenceIndex<LinkRoleDefinition>>();

        public ConceptDefinition ConceptDefinition(string Id, string TechName)
        {
            return this.ConceptDefinitions.Match(Id, TechName);
        }

        public RelationshipDefinition RelationshipDefinition(string Id, string TechName)
        {
            return this.RelationshipDefinitions.Match(Id, TechName);
        }

        public IdeaDefinition IdeaDefinition(string Id, string TechName)
        {
            return this.IdeaDefinitions.Match(Id, TechName);
        }

        public TableDefinition TableDefinition(string Id, string TechName)
        {
            return this.TableDefinitions.Match(Id, TechName);
        }

        public FieldDefinition FieldDefinition(TableDefinition Owner, string Id, string TechName)
        {
            if (Owner == null)
                return null;

            return this.GetFieldDefinitions(Owner).Match(Id, TechName);
        }

        public ExternalLanguageDeclaration ExternalLanguage(string Id, string TechName)
        {
            return this.ExternalLanguages.Match(Id, TechName);
        }

        public DomainJsonReferenceMatch<ExternalLanguageDeclaration> ExternalLanguageMatch(string Id, string TechName)
        {
            var ExactId = this.ExternalLanguages.MatchById(Id);
            if (ExactId != null)
                return DomainJsonReferenceMatch<ExternalLanguageDeclaration>.Found(ExactId, "exact id");

            if (!String.IsNullOrWhiteSpace(TechName))
            {
                var ExactTechName = this.ExternalLanguages.MatchSimple(TechName);
                if (ExactTechName != null)
                    return DomainJsonReferenceMatch<ExternalLanguageDeclaration>.Found(ExactTechName, "exact techName");

                var Normalized = NormalizeReferenceKey(TechName);
                var Matches = this.ExternalLanguages.NormalizedMatches(Normalized);

                if (Matches.Count == 1)
                    return DomainJsonReferenceMatch<ExternalLanguageDeclaration>.Found(Matches[0], "normalized techName");

                if (Matches.Count > 1)
                    return DomainJsonReferenceMatch<ExternalLanguageDeclaration>.Ambiguous(Matches, "ambiguous normalized techName");
            }

            return DomainJsonReferenceMatch<ExternalLanguageDeclaration>.Unresolved();
        }

        public MarkerDefinition MarkerDefinition(string Id, string TechName)
        {
            return this.MarkerDefinitions.Match(Id, TechName);
        }

        public SimplePresentationElement LinkRoleVariant(string TechName)
        {
            return this.LinkRoleVariants.MatchSimple(TechName);
        }

        public SimplePresentationElement MarkerCluster(string TechName)
        {
            return this.MarkerClusters.MatchSimple(TechName);
        }

        public FormalPresentationElement ConceptDefinitionCluster(string Id, string TechName)
        {
            return this.ConceptDefinitionClusters.Match(Id, TechName);
        }

        public FormalPresentationElement RelationshipDefinitionCluster(string Id, string TechName)
        {
            return this.RelationshipDefinitionClusters.Match(Id, TechName);
        }

        public MetaCategory<TableDefinition> TableDefinitionCategory(string Id, string TechName)
        {
            return this.TableDefinitionCategories.Match(Id, TechName);
        }

        public MetaCategory<FieldDefinition> FieldDefinitionCategory(string Id, string TechName)
        {
            return this.FieldDefinitionCategories.Match(Id, TechName);
        }

        public LinkRoleDefinition RelationshipRole(RelationshipDefinition Owner, string Id, string TechName, string RoleType)
        {
            if (Owner == null)
                return null;

            var Roles = new[] { Owner.OriginOrParticipantLinkRoleDef, Owner.TargetLinkRoleDef }.Where(Role => Role != null);
            var Matched = this.GetRelationshipRoles(Owner).Match(Id, TechName);
            if (Matched != null)
                return Matched;

            if (!String.IsNullOrWhiteSpace(RoleType))
                return Roles.FirstOrDefault(Role => String.Equals(Role.RoleType.ToString(), RoleType, StringComparison.OrdinalIgnoreCase));

            return null;
        }

        public DataType DataType(string TechName)
        {
            if (String.IsNullOrWhiteSpace(TechName))
                return Instrumind.ThinkComposer.MetaModel.InformationMetaModel.DataType.DataTypeText;

            return FindDataType(TechName)
                   .NullDefault(Instrumind.ThinkComposer.MetaModel.InformationMetaModel.DataType.DataTypeText);
        }

        public DataType FindDataType(string TechName)
        {
            if (String.IsNullOrWhiteSpace(TechName))
                return null;

            var Exact = this.AvailableDataTypes.MatchSimple(TechName);
            if (Exact != null)
                return Exact;

            var Normalized = NormalizeReferenceKey(TechName);
            var Matches = this.AvailableDataTypes.NormalizedMatches(Normalized);
            return Matches.Count == 1 ? Matches[0] : null;
        }

        /// <summary>
        /// Keeps the operation-scoped lookup indexes synchronized as the importer
        /// creates entities or restores source IDs/techNames.
        /// </summary>
        public void Refresh(string Entity, IIdentifiableElement Element, Guid? PreviousId = null, string PreviousTechName = null)
        {
            if (Element == null || String.IsNullOrWhiteSpace(Entity))
                return;

            if (String.Equals(Entity, "domainEntity", StringComparison.OrdinalIgnoreCase))
            {
                if (Element is FieldDefinition)
                    Entity = "fieldDefinition";
                else if (Element is ConceptDefinition)
                    Entity = "conceptDefinition";
                else if (Element is RelationshipDefinition)
                    Entity = "relationshipDefinition";
                else if (Element is LinkRoleDefinition)
                    Entity = "relationshipRole";
                else if (Element is TableDefinition)
                    Entity = "tableDefinition";
                else if (Element is ExternalLanguageDeclaration)
                    Entity = "externalLanguage";
                else if (Element is MarkerDefinition)
                    Entity = "markerDefinition";
                else if (Element is MetaCategory<TableDefinition>)
                    Entity = "tableDefinitionCategory";
                else if (Element is MetaCategory<FieldDefinition>)
                    Entity = "fieldDefinitionCategory";
            }

            if (String.Equals(Entity, "externalLanguage", StringComparison.OrdinalIgnoreCase))
                this.ExternalLanguages.Refresh(Element as ExternalLanguageDeclaration, PreviousId, PreviousTechName);
            else if (String.Equals(Entity, "linkRoleVariant", StringComparison.OrdinalIgnoreCase))
                this.LinkRoleVariants.Refresh(Element as SimplePresentationElement, PreviousId, PreviousTechName);
            else if (String.Equals(Entity, "markerCluster", StringComparison.OrdinalIgnoreCase))
                this.MarkerClusters.Refresh(Element as SimplePresentationElement, PreviousId, PreviousTechName);
            else if (String.Equals(Entity, "conceptDefinitionCluster", StringComparison.OrdinalIgnoreCase))
                this.ConceptDefinitionClusters.Refresh(Element as FormalPresentationElement, PreviousId, PreviousTechName);
            else if (String.Equals(Entity, "relationshipDefinitionCluster", StringComparison.OrdinalIgnoreCase))
                this.RelationshipDefinitionClusters.Refresh(Element as FormalPresentationElement, PreviousId, PreviousTechName);
            else if (String.Equals(Entity, "tableDefinitionCategory", StringComparison.OrdinalIgnoreCase))
                this.TableDefinitionCategories.Refresh(Element as MetaCategory<TableDefinition>, PreviousId, PreviousTechName);
            else if (String.Equals(Entity, "fieldDefinitionCategory", StringComparison.OrdinalIgnoreCase))
                this.FieldDefinitionCategories.Refresh(Element as MetaCategory<FieldDefinition>, PreviousId, PreviousTechName);
            else if (String.Equals(Entity, "markerDefinition", StringComparison.OrdinalIgnoreCase))
                this.MarkerDefinitions.Refresh(Element as MarkerDefinition, PreviousId, PreviousTechName);
            else if (String.Equals(Entity, "tableDefinition", StringComparison.OrdinalIgnoreCase))
                this.TableDefinitions.Refresh(Element as TableDefinition, PreviousId, PreviousTechName);
            else if (String.Equals(Entity, "fieldDefinition", StringComparison.OrdinalIgnoreCase))
            {
                var Field = Element as FieldDefinition;
                if (Field != null && Field.OwnerTableDef != null)
                    this.GetFieldDefinitions(Field.OwnerTableDef).Refresh(Field, PreviousId, PreviousTechName);
            }
            else if (String.Equals(Entity, "conceptDefinition", StringComparison.OrdinalIgnoreCase))
            {
                var Definition = Element as ConceptDefinition;
                this.ConceptDefinitions.Refresh(Definition, PreviousId, PreviousTechName);
                this.IdeaDefinitions.Refresh(Definition, PreviousId, PreviousTechName);
            }
            else if (String.Equals(Entity, "relationshipDefinition", StringComparison.OrdinalIgnoreCase))
            {
                var Definition = Element as RelationshipDefinition;
                this.RelationshipDefinitions.Refresh(Definition, PreviousId, PreviousTechName);
                this.IdeaDefinitions.Refresh(Definition, PreviousId, PreviousTechName);
            }
            else if (String.Equals(Entity, "relationshipRole", StringComparison.OrdinalIgnoreCase))
            {
                var Role = Element as LinkRoleDefinition;
                if (Role != null && Role.OwnerRelationshipDef != null)
                    this.GetRelationshipRoles(Role.OwnerRelationshipDef).Refresh(Role, PreviousId, PreviousTechName);
            }
        }

        private ReferenceIndex<FieldDefinition> GetFieldDefinitions(TableDefinition Owner)
        {
            ReferenceIndex<FieldDefinition> Result;
            if (!this.FieldDefinitionsByOwner.TryGetValue(Owner, out Result))
            {
                Result = new ReferenceIndex<FieldDefinition>(() => Owner.FieldDefinitions);
                this.FieldDefinitionsByOwner.Add(Owner, Result);
            }
            return Result;
        }

        private ReferenceIndex<LinkRoleDefinition> GetRelationshipRoles(RelationshipDefinition Owner)
        {
            ReferenceIndex<LinkRoleDefinition> Result;
            if (!this.RelationshipRolesByOwner.TryGetValue(Owner, out Result))
            {
                Result = new ReferenceIndex<LinkRoleDefinition>(() =>
                    new[] { Owner.OriginOrParticipantLinkRoleDef, Owner.TargetLinkRoleDef }.Where(Role => Role != null));
                this.RelationshipRolesByOwner.Add(Owner, Result);
            }
            return Result;
        }

        private sealed class ReferenceIndex<T>
            where T : class, IIdentifiableElement
        {
            private readonly Func<IEnumerable<T>> Source;
            private readonly Dictionary<Guid, T> ById = new Dictionary<Guid, T>();
            private readonly Dictionary<string, T> ByTechName = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<T>> ByNormalizedTechName = new Dictionary<string, List<T>>(StringComparer.OrdinalIgnoreCase);
            private bool IsInitialized;

            public ReferenceIndex(Func<IEnumerable<T>> Source)
            {
                this.Source = Source;
            }

            public T Match(string Id, string TechName)
            {
                var Result = this.MatchById(Id);
                return Result ?? this.MatchSimple(TechName);
            }

            public T MatchById(string Id)
            {
                this.EnsureInitialized();
                Guid Parsed;
                T Result;
                return !String.IsNullOrWhiteSpace(Id) && Guid.TryParse(Id, out Parsed) && this.ById.TryGetValue(Parsed, out Result)
                       ? Result : null;
            }

            public T MatchSimple(string TechName)
            {
                this.EnsureInitialized();
                T Result;
                return !String.IsNullOrWhiteSpace(TechName) && this.ByTechName.TryGetValue(TechName, out Result)
                       ? Result : null;
            }

            public List<T> NormalizedMatches(string NormalizedTechName)
            {
                this.EnsureInitialized();
                List<T> Result;
                return !String.IsNullOrWhiteSpace(NormalizedTechName) && this.ByNormalizedTechName.TryGetValue(NormalizedTechName, out Result)
                       ? Result.ToList() : new List<T>();
            }

            public void Refresh(T Item, Guid? PreviousId, string PreviousTechName)
            {
                if (Item == null)
                    return;

                this.EnsureInitialized();

                T Existing;
                if (PreviousId != null && this.ById.TryGetValue(PreviousId.Value, out Existing) && Object.ReferenceEquals(Existing, Item))
                    this.ById.Remove(PreviousId.Value);

                var Unique = Item as UniqueElement;
                if (Unique != null && !this.ById.ContainsKey(Unique.GlobalId))
                    this.ById.Add(Unique.GlobalId, Item);

                if (!String.IsNullOrWhiteSpace(PreviousTechName) &&
                    this.ByTechName.TryGetValue(PreviousTechName, out Existing) && Object.ReferenceEquals(Existing, Item))
                {
                    this.ByTechName.Remove(PreviousTechName);
                    var Replacement = this.Items().FirstOrDefault(Candidate => !Object.ReferenceEquals(Candidate, Item) &&
                                                                               String.Equals(Candidate.TechName, PreviousTechName, StringComparison.OrdinalIgnoreCase));
                    if (Replacement != null)
                        this.ByTechName.Add(PreviousTechName, Replacement);
                }

                if (!String.IsNullOrWhiteSpace(Item.TechName))
                {
                    if (!this.ByTechName.TryGetValue(Item.TechName, out Existing))
                        this.ByTechName.Add(Item.TechName, Item);
                    else if (!Object.ReferenceEquals(Existing, Item))
                    {
                        var First = this.Items().FirstOrDefault(Candidate => String.Equals(Candidate.TechName, Item.TechName, StringComparison.OrdinalIgnoreCase));
                        if (First != null)
                            this.ByTechName[Item.TechName] = First;
                    }
                }

                if (!String.IsNullOrWhiteSpace(PreviousTechName) &&
                    !String.Equals(PreviousTechName, Item.TechName, StringComparison.OrdinalIgnoreCase))
                    foreach (var Pair in this.ByNormalizedTechName.ToList())
                    {
                        Pair.Value.RemoveAll(Candidate => Object.ReferenceEquals(Candidate, Item));
                        if (Pair.Value.Count < 1)
                            this.ByNormalizedTechName.Remove(Pair.Key);
                    }

                this.AddNormalized(Item);
            }

            private void EnsureInitialized()
            {
                if (this.IsInitialized)
                    return;

                foreach (var Item in this.Items())
                    this.Add(Item);
                this.IsInitialized = true;
            }

            private IEnumerable<T> Items()
            {
                return this.Source == null ? Enumerable.Empty<T>() : (this.Source() ?? Enumerable.Empty<T>());
            }

            private void Add(T Item)
            {
                if (Item == null)
                    return;

                var Unique = Item as UniqueElement;
                if (Unique != null && !this.ById.ContainsKey(Unique.GlobalId))
                    this.ById.Add(Unique.GlobalId, Item);
                if (!String.IsNullOrWhiteSpace(Item.TechName) && !this.ByTechName.ContainsKey(Item.TechName))
                    this.ByTechName.Add(Item.TechName, Item);
                this.AddNormalized(Item);
            }

            private void AddNormalized(T Item)
            {
                if (Item == null || String.IsNullOrWhiteSpace(Item.TechName))
                    return;

                var Key = NormalizeReferenceKey(Item.TechName);
                List<T> Items;
                if (!this.ByNormalizedTechName.TryGetValue(Key, out Items))
                {
                    Items = new List<T>();
                    this.ByNormalizedTechName.Add(Key, Items);
                }
                if (!Items.Any(Candidate => Object.ReferenceEquals(Candidate, Item)))
                    Items.Add(Item);
            }
        }

        public static T Match<T>(IEnumerable<T> Source, string Id, string TechName)
            where T : class, IIdentifiableElement
        {
            if (Source == null)
                return null;

            var UniqueMatch = MatchById(Source, Id);
            if (UniqueMatch != null)
                return UniqueMatch;

            return MatchSimple(Source, TechName);
        }

        private static T MatchById<T>(IEnumerable<T> Source, string Id)
            where T : class, IIdentifiableElement
        {
            Guid Parsed;
            if (Source == null || String.IsNullOrWhiteSpace(Id) || !Guid.TryParse(Id, out Parsed))
                return null;

            return Source.OfType<UniqueElement>().FirstOrDefault(Item => Item.GlobalId == Parsed) as T;
        }

        public static T MatchSimple<T>(IEnumerable<T> Source, string TechName)
            where T : class, IIdentifiableElement
        {
            if (Source == null || String.IsNullOrWhiteSpace(TechName))
                return null;

            return Source.FirstOrDefault(Item => String.Equals(Item.TechName, TechName, StringComparison.OrdinalIgnoreCase));
        }

        public static string NormalizeReferenceKey(string Source)
        {
            if (String.IsNullOrWhiteSpace(Source))
                return "";

            var Result = new StringBuilder();
            var PreviousWasSeparator = false;
            foreach (var Character in Source.Trim())
            {
                if (Char.IsLetterOrDigit(Character))
                {
                    Result.Append(Char.ToUpperInvariant(Character));
                    PreviousWasSeparator = false;
                }
                else
                {
                    if (!PreviousWasSeparator)
                    {
                        Result.Append('_');
                        PreviousWasSeparator = true;
                    }
                }
            }

            return Result.ToString().Trim('_');
        }
    }

    public class DomainJsonReferenceMatch<T>
        where T : class, IIdentifiableElement
    {
        public T Item { get; private set; }
        public string MatchMethod { get; private set; }
        public List<T> AmbiguousCandidates { get; private set; }
        public bool IsAmbiguous { get { return this.AmbiguousCandidates != null && this.AmbiguousCandidates.Count > 1; } }

        private DomainJsonReferenceMatch()
        {
            this.AmbiguousCandidates = new List<T>();
        }

        public static DomainJsonReferenceMatch<T> Found(T Item, string MatchMethod)
        {
            return new DomainJsonReferenceMatch<T> { Item = Item, MatchMethod = MatchMethod };
        }

        public static DomainJsonReferenceMatch<T> Ambiguous(IEnumerable<T> Candidates, string MatchMethod)
        {
            return new DomainJsonReferenceMatch<T> { AmbiguousCandidates = Candidates.ToList(), MatchMethod = MatchMethod };
        }

        public static DomainJsonReferenceMatch<T> Unresolved()
        {
            return new DomainJsonReferenceMatch<T> { MatchMethod = "unresolved" };
        }
    }
}
