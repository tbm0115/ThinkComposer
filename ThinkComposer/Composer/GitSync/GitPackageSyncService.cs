using System;
namespace Instrumind.ThinkComposer.Composer.GitSync
{
    // Compatibility facade: legacy metadata remains readable; private checkout mutation is retired.
    public static class GitPackageSyncService
    {
        private static Exception Retired() { return new GitWorkflowException("LEGACY_WORKFLOW", "Background package synchronization is retired. Migrate to a visible working repository and use the Repository panel."); }
        public static string LinkPackage(string Input, string Output, bool InPlace, string RemoteUrl, string Branch, string RepositoryPath, string EmbeddedDomainPath) { throw Retired(); }
        public static string UnlinkPackage(string Input, string Output, bool InPlace) { throw Retired(); }
        public static string StatusPackage(string Input) { return GitWorkflowCli.Serialize(RepositoryGit.Status(Input)); }
        public static GitPackageRemoteStatus GetRemoteStatus(string Input) { throw Retired(); }
        public static GitPackageRemoteStatus GetEmbeddedDomainRemoteStatus(string CompositionPackagePath) { throw Retired(); }
        public static GitPackagePullResult PullPackage(string Input, string Output, bool InPlace, string BackupDirectory) { throw Retired(); }
        public static string PushComposition(string Input, string Message) { throw Retired(); }
        public static string PullEmbeddedDomainBaseline(string CompositionPackagePath, string OutputDirectory) { throw Retired(); }
        public static string PullDomainPackageToTemporaryFile(string DomainPackagePath, string OutputDirectory) { throw Retired(); }
        public static string DescribeLink(GitPackageLink Link) { return "Legacy remote: " + GitPackageLink.RedactRemoteUrl(Link.Remote.Url) + " [" + Link.Remote.Branch + "]"; }
    }
    public sealed class GitPackagePullResult
    {
        public string OutputPath;
        public string BackupPath;
        public string RemoteHead;
        public string Message;
    }

    public sealed class GitPackageRemoteStatus
    {
        public string PackagePath;
        public string PackageKind;
        public string RemoteDisplayUrl;
        public string Branch;
        public string BaselinePath;
        public string RemoteHead;
        public string PreviousSeenHead;
        public bool BaselineExists;
        public string LocalAuthoritativeJsonHash;
        public string RemoteAuthoritativeJsonHash;
        public bool PackageDiffersFromRemote;
        public bool RemoteBranchAdvanced;
        public bool HasRemoteUpdate;
        public bool HasLocalChangesToPush;
    }
}
