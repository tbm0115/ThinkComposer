// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Portable Git synchronization metadata stored in package manifests.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;

namespace Instrumind.ThinkComposer.Composer.GitSync
{
    public sealed class GitPackageLink
    {
        public const int CurrentVersion = 1;
        public const string KindComposition = "composition";
        public const string KindDomain = "domain";
        public const string RoleSelf = "self";
        public const string RoleEmbeddedDomainSource = "embeddedDomainSource";

        public int Version = CurrentVersion;
        public GitPackageRemote Remote = new GitPackageRemote();
        public List<GitPackageBaseline> Baselines = new List<GitPackageBaseline>();

        public static GitPackageLink Create(string RemoteUrl, string Branch, string SelfPath, string PackageKind, string EmbeddedDomainPath)
        {
            var Result = new GitPackageLink();
            Result.Remote.Url = RemoteUrl;
            Result.Remote.Branch = Branch;

            Result.Baselines.Add(new GitPackageBaseline
            {
                Kind = PackageKind,
                Role = RoleSelf,
                Path = SelfPath
            });

            if (!String.IsNullOrWhiteSpace(EmbeddedDomainPath))
                Result.Baselines.Add(new GitPackageBaseline
                {
                    Kind = KindDomain,
                    Role = RoleEmbeddedDomainSource,
                    Path = EmbeddedDomainPath
                });

            Result.Validate();
            return Result;
        }

        public GitPackageBaseline FindBaseline(string Kind, string Role)
        {
            return this.Baselines.FirstOrDefault(Baseline =>
                Baseline != null &&
                String.Equals(Baseline.Kind, Kind, StringComparison.OrdinalIgnoreCase) &&
                String.Equals(Baseline.Role, Role, StringComparison.OrdinalIgnoreCase));
        }

        public void Validate()
        {
            if (this.Version != CurrentVersion)
                throw new InvalidDataException("Unsupported gitSync version: " + this.Version + ".");

            if (this.Remote == null)
                throw new InvalidDataException("gitSync remote is missing.");

            if (String.IsNullOrWhiteSpace(this.Remote.Url))
                throw new InvalidDataException("gitSync remote.url is required.");

            if (ContainsCredentialUserInfo(this.Remote.Url))
                throw new InvalidDataException("gitSync remote.url must not include embedded credentials.");

            if (String.IsNullOrWhiteSpace(this.Remote.Branch))
                throw new InvalidDataException("gitSync remote.branch is required.");

            if (this.Remote.Branch.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0)
                throw new InvalidDataException("gitSync remote.branch contains invalid whitespace.");

            if (this.Baselines == null || this.Baselines.Count < 1)
                throw new InvalidDataException("gitSync baselines must include at least one entry.");

            foreach (var Baseline in this.Baselines)
                ValidateBaseline(Baseline);
        }

        public OrderedDictionary ToGraph()
        {
            var Obj = NewObject();
            Add(Obj, "version", this.Version);
            Add(Obj, "remote", this.Remote == null ? null : this.Remote.ToGraph());
            Add(Obj, "baselines", this.Baselines.Select(Baseline => Baseline.ToGraph()).ToList());
            return Obj;
        }

        public static GitPackageLink FromGraph(object Graph)
        {
            var Root = Graph as IDictionary<string, object>;
            if (Root == null)
                return null;

            var Result = new GitPackageLink();
            Result.Version = GetInt(Root, "version") ?? CurrentVersion;

            var Remote = GetDictionary(Root, "remote");
            if (Remote != null)
            {
                Result.Remote = new GitPackageRemote();
                Result.Remote.Url = GetString(Remote, "url");
                Result.Remote.Branch = GetString(Remote, "branch");
            }

            Result.Baselines = new List<GitPackageBaseline>();
            var Baselines = GetEnumerable(Root, "baselines");
            if (Baselines != null)
                foreach (var Item in Baselines)
                {
                    var BaselineData = Item as IDictionary<string, object>;
                    if (BaselineData == null)
                        continue;

                    Result.Baselines.Add(new GitPackageBaseline
                    {
                        Kind = GetString(BaselineData, "kind"),
                        Role = GetString(BaselineData, "role"),
                        Path = GetString(BaselineData, "path")
                    });
                }

            Result.Validate();
            return Result;
        }

        public static string RedactRemoteUrl(string RemoteUrl)
        {
            if (String.IsNullOrWhiteSpace(RemoteUrl))
                return RemoteUrl;

            Uri Parsed;
            if (Uri.TryCreate(RemoteUrl, UriKind.Absolute, out Parsed) &&
                !String.IsNullOrEmpty(Parsed.UserInfo))
            {
                var Builder = new UriBuilder(Parsed);
                Builder.UserName = "<redacted>";
                Builder.Password = "";
                return Builder.Uri.ToString();
            }

            return RemoteUrl;
        }

        public static bool ContainsCredentialUserInfo(string RemoteUrl)
        {
            Uri Parsed;
            if (Uri.TryCreate(RemoteUrl, UriKind.Absolute, out Parsed) &&
                !String.IsNullOrEmpty(Parsed.UserInfo))
            {
                if (String.Equals(Parsed.Scheme, "ssh", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(Parsed.Scheme, "git+ssh", StringComparison.OrdinalIgnoreCase))
                    return Parsed.UserInfo.Contains(":");

                return String.Equals(Parsed.Scheme, "http", StringComparison.OrdinalIgnoreCase) ||
                       String.Equals(Parsed.Scheme, "https", StringComparison.OrdinalIgnoreCase) ||
                       Parsed.UserInfo.Contains(":");
            }

            var AtIndex = RemoteUrl == null ? -1 : RemoteUrl.IndexOf('@');
            if (AtIndex <= 0)
                return false;

            var Prefix = RemoteUrl.Substring(0, AtIndex);
            return Prefix.IndexOf('/') < 0 && Prefix.Contains(":");
        }

        private static void ValidateBaseline(GitPackageBaseline Baseline)
        {
            if (Baseline == null)
                throw new InvalidDataException("gitSync baseline entry is missing.");

            if (!String.Equals(Baseline.Kind, KindComposition, StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(Baseline.Kind, KindDomain, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("gitSync baseline kind must be composition or domain.");

            if (!String.Equals(Baseline.Role, RoleSelf, StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(Baseline.Role, RoleEmbeddedDomainSource, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("gitSync baseline role must be self or embeddedDomainSource.");

            ValidateRepositoryPath(Baseline.Path);
        }

        public static void ValidateRepositoryPath(string PathText)
        {
            if (String.IsNullOrWhiteSpace(PathText))
                throw new InvalidDataException("gitSync baseline path is required.");

            if (PathText.Contains("\\") || PathText.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(PathText) || PathText.Contains(":"))
                throw new InvalidDataException("gitSync baseline path must be a repository-relative path using forward slashes.");

            var Segments = PathText.Split('/');
            if (Segments.Any(Segment => String.IsNullOrWhiteSpace(Segment) ||
                                        Segment == "." ||
                                        Segment == ".."))
                throw new InvalidDataException("gitSync baseline path must not contain empty, '.', or '..' segments.");
        }

        private static OrderedDictionary NewObject()
        {
            return new OrderedDictionary();
        }

        private static void Add(OrderedDictionary Obj, string Key, object Value)
        {
            Obj.Add(Key, Value);
        }

        private static string GetString(IDictionary<string, object> Source, string Key)
        {
            object Value;
            return Source != null && Source.TryGetValue(Key, out Value) && Value != null ? Value.ToString() : null;
        }

        private static int? GetInt(IDictionary<string, object> Source, string Key)
        {
            object Value;
            if (Source == null || !Source.TryGetValue(Key, out Value) || Value == null)
                return null;

            if (Value is int)
                return (int)Value;

            int Parsed;
            if (Int32.TryParse(Value.ToString(), out Parsed))
                return Parsed;

            return null;
        }

        private static IDictionary<string, object> GetDictionary(IDictionary<string, object> Source, string Key)
        {
            object Value;
            return Source != null && Source.TryGetValue(Key, out Value) ? Value as IDictionary<string, object> : null;
        }

        private static IEnumerable GetEnumerable(IDictionary<string, object> Source, string Key)
        {
            object Value;
            if (Source == null || !Source.TryGetValue(Key, out Value))
                return null;

            return Value as IEnumerable;
        }
    }

    public sealed class GitPackageRemote
    {
        public string Url;
        public string Branch;

        public OrderedDictionary ToGraph()
        {
            var Obj = new OrderedDictionary();
            Obj.Add("url", this.Url);
            Obj.Add("branch", this.Branch);
            return Obj;
        }
    }

    public sealed class GitPackageBaseline
    {
        public string Kind;
        public string Role;
        public string Path;

        public OrderedDictionary ToGraph()
        {
            var Obj = new OrderedDictionary();
            Obj.Add("kind", this.Kind);
            Obj.Add("role", this.Role);
            Obj.Add("path", this.Path);
            return Obj;
        }
    }
}
