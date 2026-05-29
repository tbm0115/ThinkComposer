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
        }

        public Domain Domain { get; private set; }

        public ConceptDefinition ConceptDefinition(string Id, string TechName)
        {
            return Match(this.Domain.ConceptDefinitions, Id, TechName);
        }

        public RelationshipDefinition RelationshipDefinition(string Id, string TechName)
        {
            return Match(this.Domain.RelationshipDefinitions, Id, TechName);
        }

        public IdeaDefinition IdeaDefinition(string Id, string TechName)
        {
            return Match(this.Domain.Definitions, Id, TechName);
        }

        public TableDefinition TableDefinition(string Id, string TechName)
        {
            return Match(this.Domain.TableDefinitions, Id, TechName);
        }

        public FieldDefinition FieldDefinition(TableDefinition Owner, string Id, string TechName)
        {
            if (Owner == null)
                return null;

            return Match(Owner.FieldDefinitions, Id, TechName);
        }

        public ExternalLanguageDeclaration ExternalLanguage(string Id, string TechName)
        {
            return Match(this.Domain.ExternalLanguages, Id, TechName);
        }

        public DomainJsonReferenceMatch<ExternalLanguageDeclaration> ExternalLanguageMatch(string Id, string TechName)
        {
            var ExactId = MatchById(this.Domain.ExternalLanguages, Id);
            if (ExactId != null)
                return DomainJsonReferenceMatch<ExternalLanguageDeclaration>.Found(ExactId, "exact id");

            if (!String.IsNullOrWhiteSpace(TechName))
            {
                var ExactTechName = MatchSimple(this.Domain.ExternalLanguages, TechName);
                if (ExactTechName != null)
                    return DomainJsonReferenceMatch<ExternalLanguageDeclaration>.Found(ExactTechName, "exact techName");

                var Normalized = NormalizeReferenceKey(TechName);
                var Matches = this.Domain.ExternalLanguages
                    .Where(Language => NormalizeReferenceKey(Language.TechName) == Normalized)
                    .ToList();

                if (Matches.Count == 1)
                    return DomainJsonReferenceMatch<ExternalLanguageDeclaration>.Found(Matches[0], "normalized techName");

                if (Matches.Count > 1)
                    return DomainJsonReferenceMatch<ExternalLanguageDeclaration>.Ambiguous(Matches, "ambiguous normalized techName");
            }

            return DomainJsonReferenceMatch<ExternalLanguageDeclaration>.Unresolved();
        }

        public MarkerDefinition MarkerDefinition(string Id, string TechName)
        {
            return Match(this.Domain.MarkerDefinitions, Id, TechName);
        }

        public SimplePresentationElement LinkRoleVariant(string TechName)
        {
            return MatchSimple(this.Domain.LinkRoleVariants, TechName);
        }

        public SimplePresentationElement MarkerCluster(string TechName)
        {
            return MatchSimple(this.Domain.MarkerClusters, TechName);
        }

        public FormalPresentationElement ConceptDefinitionCluster(string Id, string TechName)
        {
            return Match(this.Domain.ConceptDefClusters, Id, TechName);
        }

        public FormalPresentationElement RelationshipDefinitionCluster(string Id, string TechName)
        {
            return Match(this.Domain.RelationshipDefClusters, Id, TechName);
        }

        public MetaCategory<TableDefinition> TableDefinitionCategory(string Id, string TechName)
        {
            return Match(this.Domain.TableDefCategories, Id, TechName);
        }

        public MetaCategory<FieldDefinition> FieldDefinitionCategory(string Id, string TechName)
        {
            return Match(this.Domain.FieldDefCategories, Id, TechName);
        }

        public LinkRoleDefinition RelationshipRole(RelationshipDefinition Owner, string Id, string TechName, string RoleType)
        {
            if (Owner == null)
                return null;

            var Roles = new[] { Owner.OriginOrParticipantLinkRoleDef, Owner.TargetLinkRoleDef }.Where(Role => Role != null);
            var Matched = Match(Roles, Id, TechName);
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

            var Exact = this.Domain.AvailableDataTypes.FirstOrDefault(Type => String.Equals(Type.TechName, TechName, StringComparison.OrdinalIgnoreCase));
            if (Exact != null)
                return Exact;

            var Normalized = NormalizeReferenceKey(TechName);
            var Matches = this.Domain.AvailableDataTypes.Where(Type => NormalizeReferenceKey(Type.TechName) == Normalized).ToList();
            return Matches.Count == 1 ? Matches[0] : null;
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
