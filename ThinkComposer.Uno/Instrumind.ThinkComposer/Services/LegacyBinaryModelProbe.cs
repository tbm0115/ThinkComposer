using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Instrumind.ThinkComposer.Services;

public sealed class LegacyBinaryModelSummary
{
    public string EntryName { get; init; } = string.Empty;

    public string RootTypeName { get; init; } = string.Empty;

    public bool ParsedRecordStream { get; init; }

    public IReadOnlyDictionary<string, int> TypeCounts { get; init; } = new Dictionary<string, int>();

    public IReadOnlyList<string> CandidateNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CompositionContentNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CompositionViewNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CompositionConceptNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CompositionRelationshipNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> DomainConceptNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> DomainRelationshipNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> DomainMarkerNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> DomainComplementNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}

public sealed class LegacyStringFact
{
    public string DeclaringTypeName { get; init; } = string.Empty;

    public string MemberName { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;
}

public static class LegacyBinaryModelProbe
{
    public static LegacyBinaryModelSummary Analyze(string entryName, byte[] bytes)
    {
        if (bytes == null)
            throw new ArgumentNullException(nameof(bytes));

        var parser = new BinaryFormatterFactReader(entryName, bytes);
        return parser.Read();
    }

    private sealed class BinaryFormatterFactReader
    {
        private const int MaximumRecords = 500_000;
        private const int MaximumDepth = 512;

        private static readonly Regex FallbackStringPattern =
            new(@"[A-Za-z][A-Za-z0-9_ .:/()\-,'&]{2,100}", RegexOptions.Compiled);

        private readonly string entryName;
        private readonly byte[] bytes;
        private readonly BinaryReader reader;
        private readonly Dictionary<int, ClassMetadata> classMetadataById = new();
        private readonly Dictionary<string, int> typeCounts = new(StringComparer.Ordinal);
        private readonly HashSet<string> typeNames = new(StringComparer.Ordinal);
        private readonly HashSet<string> memberNames = new(StringComparer.Ordinal);
        private readonly HashSet<string> assemblyNames = new(StringComparer.Ordinal);
        private readonly List<string> strings = new();
        private readonly List<LegacyStringFact> stringFacts = new();
        private readonly Stack<StringContext> stringContexts = new();
        private readonly List<string> diagnostics = new();
        private int recordsRead;
        private int depth;
        private bool parsedRecordStream;
        private bool reachedEnd;

        public BinaryFormatterFactReader(string entryName, byte[] bytes)
        {
            this.entryName = entryName;
            this.bytes = bytes;
            reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
        }

        public LegacyBinaryModelSummary Read()
        {
            try
            {
                while (!reachedEnd && reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    ReadRecord();
                    recordsRead++;
                    if (recordsRead > MaximumRecords)
                    {
                        diagnostics.Add("Stopped after reaching the BinaryFormatter record safety limit.");
                        break;
                    }
                }

                parsedRecordStream = true;
            }
            catch (Exception exception)
            {
                diagnostics.Add("BinaryFormatter fact reader stopped early: " + exception.Message);
            }

            var candidateNames = strings
                .Concat(parsedRecordStream ? Array.Empty<string>() : ExtractFallbackStrings(bytes))
                .Select(Normalize)
                .Where(IsCandidateName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(200)
                .ToArray();

            var nameFacts = stringFacts
                .Where(fact => IsNameFact(fact) && IsCandidateName(fact.Value))
                .Select(fact => new LegacyStringFact
                {
                    DeclaringTypeName = SimplifyTypeName(fact.DeclaringTypeName),
                    MemberName = SimplifyMemberName(fact.MemberName),
                    Value = Normalize(fact.Value)
                })
                .DistinctBy(fact => fact.DeclaringTypeName + "|" + fact.MemberName + "|" + fact.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new LegacyBinaryModelSummary
            {
                EntryName = entryName,
                RootTypeName = typeCounts.Keys.FirstOrDefault() ?? string.Empty,
                ParsedRecordStream = parsedRecordStream,
                TypeCounts = typeCounts
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Take(120)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                CandidateNames = candidateNames,
                CompositionContentNames = BuildCompositionContentNames(nameFacts, candidateNames),
                CompositionViewNames = BuildCompositionNamesByExactType(nameFacts, "View"),
                CompositionConceptNames = BuildCompositionNamesByExactType(nameFacts, "Concept"),
                CompositionRelationshipNames = BuildCompositionNamesByExactType(nameFacts, "Relationship"),
                DomainConceptNames = BuildDomainNames(nameFacts, "ConceptDefinition"),
                DomainRelationshipNames = BuildDomainNames(nameFacts, "RelationshipDefinition"),
                DomainMarkerNames = BuildDomainNames(nameFacts, "MarkerDefinition"),
                DomainComplementNames = BuildDomainNames(nameFacts, "ComplementDefinition"),
                Diagnostics = diagnostics.ToArray()
            };
        }

        private int ReadRecord()
        {
            if (reader.BaseStream.Position >= reader.BaseStream.Length)
                return 0;

            var recordType = reader.ReadByte();

            return recordType switch
            {
                0 => ReadSerializedStreamHeader(),
                1 => ReadClassWithId(),
                2 => ReadSystemClassWithMembers(),
                3 => ReadClassWithMembers(hasLibraryId: true),
                4 => ReadSystemClassWithMembersAndTypes(),
                5 => ReadClassWithMembersAndTypes(hasLibraryId: true),
                6 => ReadBinaryObjectString(),
                7 => ReadBinaryArray(),
                8 => ReadMemberPrimitiveTyped(),
                9 => ReadMemberReference(),
                10 => 1,
                11 => ReadMessageEnd(),
                12 => ReadBinaryLibrary(),
                13 => ReadObjectNullMultiple256(),
                14 => ReadObjectNullMultiple(),
                15 => ReadArraySinglePrimitive(),
                16 => ReadArraySingleObject(),
                17 => ReadArraySingleObject(),
                21 => ReadMethodCall(),
                22 => ReadMethodReturn(),
                _ => throw new InvalidDataException("Unknown BinaryFormatter record type " + recordType + ".")
            };
        }

        private int ReadSerializedStreamHeader()
        {
            reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadInt32();
            return 0;
        }

        private int ReadClassWithId()
        {
            reader.ReadInt32();
            var metadataId = reader.ReadInt32();
            if (!classMetadataById.TryGetValue(metadataId, out var metadata))
            {
                diagnostics.Add("Missing class metadata id " + metadataId + ".");
                return 1;
            }

            CountType(metadata.Name);
            ReadMemberValues(metadata);
            return 1;
        }

        private int ReadSystemClassWithMembers()
        {
            var metadata = ReadClassInfo();
            metadata.MemberTypes.AddRange(metadata.MemberNames.Select(_ => MemberType.Object()));
            classMetadataById[metadata.ObjectId] = metadata;
            CountType(metadata.Name);
            ReadMemberValues(metadata);
            return 1;
        }

        private int ReadClassWithMembers(bool hasLibraryId)
        {
            var metadata = ReadClassInfo();
            metadata.MemberTypes.AddRange(metadata.MemberNames.Select(_ => MemberType.Object()));
            if (hasLibraryId)
                reader.ReadInt32();

            classMetadataById[metadata.ObjectId] = metadata;
            CountType(metadata.Name);
            ReadMemberValues(metadata);
            return 1;
        }

        private int ReadSystemClassWithMembersAndTypes()
        {
            return ReadClassWithMembersAndTypes(hasLibraryId: false);
        }

        private int ReadClassWithMembersAndTypes(bool hasLibraryId)
        {
            var metadata = ReadClassInfo();
            metadata.MemberTypes.AddRange(ReadMemberTypes(metadata.MemberNames.Count));
            if (hasLibraryId)
                reader.ReadInt32();

            classMetadataById[metadata.ObjectId] = metadata;
            CountType(metadata.Name);
            ReadMemberValues(metadata);
            return 1;
        }

        private int ReadBinaryObjectString()
        {
            reader.ReadInt32();
            AddString(reader.ReadString());
            return 1;
        }

        private int ReadBinaryLibrary()
        {
            reader.ReadInt32();
            assemblyNames.Add(reader.ReadString());
            return 0;
        }

        private int ReadMemberPrimitiveTyped()
        {
            var primitiveType = reader.ReadByte();
            SkipPrimitive(primitiveType);
            return 1;
        }

        private int ReadMemberReference()
        {
            reader.ReadInt32();
            return 1;
        }

        private int ReadMessageEnd()
        {
            reachedEnd = true;
            return 0;
        }

        private int ReadObjectNullMultiple256()
        {
            return reader.ReadByte();
        }

        private int ReadObjectNullMultiple()
        {
            return reader.ReadInt32();
        }

        private int ReadBinaryArray()
        {
            reader.ReadInt32();
            var arrayType = reader.ReadByte();
            var rank = reader.ReadInt32();
            var lengths = ReadInt32Values(rank);

            if (arrayType is 3 or 4 or 5)
                ReadInt32Values(rank);

            var binaryType = reader.ReadByte();
            var memberType = ReadMemberType(binaryType);
            var length = lengths.Aggregate(1, (current, value) => current * Math.Max(value, 0));

            if (memberType.BinaryType is 0 or 7)
                SkipPrimitiveArray(memberType.PrimitiveType, length);
            else
                ReadArrayItems(length);

            return 1;
        }

        private int ReadArraySinglePrimitive()
        {
            reader.ReadInt32();
            var length = reader.ReadInt32();
            var primitiveType = reader.ReadByte();
            SkipPrimitiveArray(primitiveType, length);
            return 1;
        }

        private int ReadArraySingleObject()
        {
            reader.ReadInt32();
            var length = reader.ReadInt32();
            ReadArrayItems(length);
            return 1;
        }

        private int ReadMethodCall()
        {
            diagnostics.Add("MethodCall record was skipped.");
            reachedEnd = true;
            return 0;
        }

        private int ReadMethodReturn()
        {
            diagnostics.Add("MethodReturn record was skipped.");
            reachedEnd = true;
            return 0;
        }

        private ClassMetadata ReadClassInfo()
        {
            var metadata = new ClassMetadata
            {
                ObjectId = reader.ReadInt32(),
                Name = reader.ReadString()
            };

            typeNames.Add(metadata.Name);

            var memberCount = reader.ReadInt32();
            for (var index = 0; index < memberCount; index++)
            {
                var memberName = reader.ReadString();
                metadata.MemberNames.Add(memberName);
                memberNames.Add(memberName);
            }

            return metadata;
        }

        private IReadOnlyList<MemberType> ReadMemberTypes(int memberCount)
        {
            var binaryTypes = new byte[memberCount];
            for (var index = 0; index < memberCount; index++)
                binaryTypes[index] = reader.ReadByte();

            var memberTypes = new List<MemberType>(memberCount);
            foreach (var binaryType in binaryTypes)
                memberTypes.Add(ReadMemberType(binaryType));

            return memberTypes;
        }

        private MemberType ReadMemberType(byte binaryType)
        {
            byte primitiveType = 0;
            string? typeName = null;

            switch (binaryType)
            {
                case 0:
                case 7:
                    primitiveType = reader.ReadByte();
                    break;
                case 3:
                    typeName = reader.ReadString();
                    typeNames.Add(typeName);
                    break;
                case 4:
                    typeName = reader.ReadString();
                    typeNames.Add(typeName);
                    reader.ReadInt32();
                    break;
            }

            return new MemberType(binaryType, primitiveType, typeName);
        }

        private void ReadMemberValues(ClassMetadata metadata)
        {
            depth++;
            if (depth > MaximumDepth)
                throw new InvalidDataException("Maximum object graph depth exceeded.");

            try
            {
                for (var index = 0; index < metadata.MemberTypes.Count; index++)
                {
                    var memberName = index < metadata.MemberNames.Count ? metadata.MemberNames[index] : string.Empty;
                    stringContexts.Push(new StringContext(metadata.Name, memberName));
                    try
                    {
                        ReadValue(metadata.MemberTypes[index]);
                    }
                    finally
                    {
                        stringContexts.Pop();
                    }
                }
            }
            finally
            {
                depth--;
            }
        }

        private void ReadValue(MemberType memberType)
        {
            if (memberType.BinaryType == 0)
            {
                SkipPrimitive(memberType.PrimitiveType);
                return;
            }

            ReadRecord();
            recordsRead++;
        }

        private void ReadArrayItems(int length)
        {
            var consumed = 0;
            while (consumed < length && !reachedEnd && reader.BaseStream.Position < reader.BaseStream.Length)
            {
                var recordSlots = ReadRecord();
                recordsRead++;
                consumed += Math.Max(recordSlots, 1);
            }
        }

        private void SkipPrimitive(byte primitiveType)
        {
            switch (primitiveType)
            {
                case 1:
                case 2:
                case 10:
                    reader.ReadByte();
                    break;
                case 3:
                case 7:
                case 14:
                    reader.ReadBytes(2);
                    break;
                case 8:
                case 11:
                case 15:
                    reader.ReadBytes(4);
                    break;
                case 5:
                    reader.ReadBytes(16);
                    break;
                case 6:
                case 9:
                case 12:
                case 13:
                case 16:
                    reader.ReadBytes(8);
                    break;
                case 18:
                    AddString(reader.ReadString());
                    break;
                default:
                    throw new InvalidDataException("Unknown primitive type " + primitiveType + ".");
            }
        }

        private void SkipPrimitiveArray(byte primitiveType, int length)
        {
            if (length < 0)
                throw new InvalidDataException("Negative primitive array length.");

            if (primitiveType == 18)
            {
                for (var index = 0; index < length; index++)
                    AddString(reader.ReadString());
                return;
            }

            var size = primitiveType switch
            {
                1 or 2 or 10 => 1,
                3 or 7 or 14 => 2,
                8 or 11 or 15 => 4,
                6 or 9 or 12 or 13 or 16 => 8,
                5 => 16,
                _ => throw new InvalidDataException("Unknown primitive array type " + primitiveType + ".")
            };

            reader.ReadBytes(checked(length * size));
        }

        private int[] ReadInt32Values(int count)
        {
            var values = new int[count];
            for (var index = 0; index < count; index++)
                values[index] = reader.ReadInt32();

            return values;
        }

        private void CountType(string typeName)
        {
            var normalized = SimplifyTypeName(typeName);
            if (normalized.Length == 0)
                return;

            typeCounts.TryGetValue(normalized, out var count);
            typeCounts[normalized] = count + 1;
        }

        private void AddString(string value)
        {
            strings.Add(value);

            var context = stringContexts.Count == 0
                ? new StringContext(string.Empty, string.Empty)
                : stringContexts.Peek();

            stringFacts.Add(new LegacyStringFact
            {
                DeclaringTypeName = context.DeclaringTypeName,
                MemberName = context.MemberName,
                Value = value
            });
        }

        private static IReadOnlyList<string> BuildCompositionContentNames(
            IEnumerable<LegacyStringFact> nameFacts,
            IEnumerable<string> fallbackNames)
        {
            var contentNames = nameFacts
                .Where(fact => IsCompositionContentType(fact.DeclaringTypeName))
                .Select(fact => fact.Value)
                .Where(value => !LooksDomainInfrastructureName(value))
                .Concat(fallbackNames.Where(value => LooksLikeUserContentName(value) && !LooksDomainInfrastructureName(value)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(160)
                .ToArray();

            return contentNames;
        }

        private static IReadOnlyList<string> BuildCompositionNamesByExactType(
            IEnumerable<LegacyStringFact> nameFacts,
            string exactTypeName)
        {
            var displayNames = BuildCompositionNamesByExactType(nameFacts, exactTypeName, requireDisplayName: true);
            return displayNames.Count > 0
                ? displayNames
                : BuildCompositionNamesByExactType(nameFacts, exactTypeName, requireDisplayName: false);
        }

        private static IReadOnlyList<string> BuildCompositionNamesByExactType(
            IEnumerable<LegacyStringFact> nameFacts,
            string exactTypeName,
            bool requireDisplayName)
        {
            return nameFacts
                .Where(fact => fact.DeclaringTypeName.Equals(exactTypeName, StringComparison.OrdinalIgnoreCase))
                .Where(fact => !requireDisplayName || IsDisplayNameFact(fact))
                .Select(fact => fact.Value)
                .Where(value => !LooksDomainInfrastructureName(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(160)
                .ToArray();
        }

        private static IReadOnlyList<string> BuildDomainNames(
            IEnumerable<LegacyStringFact> nameFacts,
            params string[] tokens)
        {
            return nameFacts
                .Where(fact => tokens.Any(token => fact.DeclaringTypeName.Contains(token, StringComparison.OrdinalIgnoreCase)))
                .Where(IsDisplayNameFact)
                .Select(fact => fact.Value)
                .Where(value => !LooksDomainInfrastructureName(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(160)
                .ToArray();
        }

        private static bool IsNameFact(LegacyStringFact fact)
        {
            var memberName = SimplifyMemberName(fact.MemberName);
            return memberName.Equals("Name", StringComparison.OrdinalIgnoreCase)
                || memberName.Equals("TechName", StringComparison.OrdinalIgnoreCase)
                || memberName.Equals("Title", StringComparison.OrdinalIgnoreCase)
                || memberName.Equals("NameCaption", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDisplayNameFact(LegacyStringFact fact)
        {
            var memberName = SimplifyMemberName(fact.MemberName);
            return memberName.Equals("Name", StringComparison.OrdinalIgnoreCase)
                || memberName.Equals("Title", StringComparison.OrdinalIgnoreCase)
                || memberName.Equals("NameCaption", StringComparison.OrdinalIgnoreCase);
        }

        private static string SimplifyMemberName(string memberName)
        {
            var name = memberName.Trim();

            if (name.EndsWith("_", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 1);

            return name;
        }

        private static bool IsCompositionContentType(string declaringTypeName)
        {
            return declaringTypeName.Contains("Composition", StringComparison.OrdinalIgnoreCase)
                || declaringTypeName.Contains("Idea", StringComparison.OrdinalIgnoreCase)
                || declaringTypeName.Contains("Concept", StringComparison.OrdinalIgnoreCase)
                || declaringTypeName.Contains("Relationship", StringComparison.OrdinalIgnoreCase)
                || declaringTypeName.Contains("View", StringComparison.OrdinalIgnoreCase)
                || declaringTypeName.Contains("Visual", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeUserContentName(string value)
        {
            if (value.Contains(" ", StringComparison.Ordinal) || value.Contains("_", StringComparison.Ordinal))
                return true;

            return value.Any(char.IsLower) && value.Any(char.IsUpper);
        }

        private static bool LooksDomainInfrastructureName(string value)
        {
            return value.Contains("DefCategories", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Definitions", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Clusters", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Variants", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Languages", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Table-Structure", StringComparison.OrdinalIgnoreCase)
                || value.Contains("base content", StringComparison.OrdinalIgnoreCase)
                || value.Contains("ModelRevision", StringComparison.OrdinalIgnoreCase)
                || value.Contains("GlobalId", StringComparison.OrdinalIgnoreCase);
        }

        private static string SimplifyTypeName(string typeName)
        {
            var name = typeName;
            var comma = name.IndexOf(',');
            if (comma >= 0)
                name = name.Substring(0, comma);

            var plus = name.LastIndexOf('+');
            if (plus >= 0)
                name = name.Substring(plus + 1);

            var dot = name.LastIndexOf('.');
            if (dot >= 0)
                name = name.Substring(dot + 1);

            return name.Trim();
        }

        private bool IsCandidateName(string value)
        {
            if (value.Length < 3 || value.Length > 120)
                return false;

            if (!value.Any(char.IsLetter))
                return false;

            if (value.EndsWith("_", StringComparison.Ordinal)
                || value.Contains("k__BackingField", StringComparison.Ordinal)
                || value.Contains("Version=", StringComparison.Ordinal)
                || value.Contains("PublicKeyToken", StringComparison.Ordinal)
                || value.StartsWith("Instrumind.", StringComparison.Ordinal)
                || value.StartsWith("System.", StringComparison.Ordinal)
                || value.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("Microsoft.", StringComparison.Ordinal))
                return false;

            if (Guid.TryParse(value, out _))
                return false;

            if (memberNames.Contains(value) || typeNames.Contains(value) || assemblyNames.Contains(value))
                return false;

            return true;
        }

        private static string Normalize(string value)
        {
            return Regex.Replace(value.Trim(), @"\s+", " ");
        }

        private static IEnumerable<string> ExtractFallbackStrings(byte[] bytes)
        {
            var text = Encoding.UTF8.GetString(bytes);
            return FallbackStringPattern.Matches(text).Select(match => match.Value);
        }
    }

    private sealed class ClassMetadata
    {
        public int ObjectId { get; init; }

        public string Name { get; init; } = string.Empty;

        public List<string> MemberNames { get; } = new();

        public List<MemberType> MemberTypes { get; } = new();
    }

    private readonly struct StringContext
    {
        public StringContext(string declaringTypeName, string memberName)
        {
            DeclaringTypeName = declaringTypeName;
            MemberName = memberName;
        }

        public string DeclaringTypeName { get; }

        public string MemberName { get; }
    }

    private readonly struct MemberType
    {
        public MemberType(byte binaryType, byte primitiveType, string? typeName)
        {
            BinaryType = binaryType;
            PrimitiveType = primitiveType;
            TypeName = typeName;
        }

        public byte BinaryType { get; }

        public byte PrimitiveType { get; }

        public string? TypeName { get; }

        public static MemberType Object()
        {
            return new MemberType(2, 0, null);
        }
    }
}
