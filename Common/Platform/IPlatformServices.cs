using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Instrumind.Common.Platform
{
    public interface IPlatformServices
    {
        IPlatformDialogService Dialogs { get; }
        IPlatformFileService Files { get; }
        IPlatformClipboardService Clipboard { get; }
        IPlatformDispatcher Dispatcher { get; }
        IPlatformResourceService Resources { get; }
        IUserMessageService Messages { get; }
    }

    public interface IPlatformDialogService
    {
        Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken);
        Task ShowMessageAsync(string title, string message, PlatformMessageKind kind, CancellationToken cancellationToken);
    }

    public interface IPlatformFileService
    {
        Task<Stream> OpenReadAsync(IEnumerable<FileTypeFilter> filters, CancellationToken cancellationToken);
        Task<Stream> OpenWriteAsync(string suggestedFileName, IEnumerable<FileTypeFilter> filters, CancellationToken cancellationToken);
    }

    public interface IPlatformClipboardService
    {
        Task<string> GetTextAsync(CancellationToken cancellationToken);
        Task SetTextAsync(string text, CancellationToken cancellationToken);
    }

    public interface IPlatformDispatcher
    {
        bool HasThreadAccess { get; }
        Task RunAsync(Action action, CancellationToken cancellationToken);
    }

    public interface IPlatformResourceService
    {
        Stream OpenResource(string resourceKey);
        Uri GetResourceUri(string resourceKey);
    }

    public interface IUserMessageService
    {
        void Publish(string message, PlatformMessageKind kind);
    }

    public enum PlatformMessageKind
    {
        Information,
        Warning,
        Error
    }

    public sealed class FileTypeFilter
    {
        public FileTypeFilter(string displayName, params string[] extensions)
        {
            DisplayName = displayName;
            Extensions = extensions ?? new string[0];
        }

        public string DisplayName { get; }
        public IReadOnlyList<string> Extensions { get; }
    }
}
