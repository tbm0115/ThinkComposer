// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// No-window shell provider for command-line operations.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

using Instrumind.Common.Visualization;

namespace Instrumind.ThinkComposer.Headless
{
    public sealed class HeadlessShellProvider : IShellProvider
    {
        private readonly Dictionary<string, IShellVisualContent> Contents = new Dictionary<string, IShellVisualContent>(StringComparer.Ordinal);

        public HeadlessShellProvider()
        {
            this.MainSelector = new ListBox();
        }

        public Selector MainSelector { get; set; }

        public void RefreshSelection(object SelectedDocument = null, bool ReExposeDocuments = false)
        {
            if (this.MainSelector != null && SelectedDocument != null)
                this.MainSelector.SelectedItem = SelectedDocument;
        }

        public IEnumerable<KeyValuePair<string, IShellVisualContent>> GetAllVisualContents()
        {
            return this.Contents;
        }

        public IShellVisualContent GetVisualContent(string Key)
        {
            IShellVisualContent Result = null;
            return this.Contents.TryGetValue(Key, out Result) ? Result : null;
        }

        public void PutVisualContent(IShellVisualContent Content, int Group = 0)
        {
            if (Content == null || Content.Key == null)
                return;

            this.Contents[Content.Key] = Content;
        }

        public void PutVisualContent(EShellVisualContentType Kind, object Content, int Group = 0)
        {
        }

        public void DiscardVisualContent(string Key)
        {
            if (Key != null)
                this.Contents.Remove(Key);
        }

        public void DiscardAllVisualContents()
        {
            this.Contents.Clear();
        }

        public Func<bool> CloseConfirmation { get; set; }

        public event KeyEventHandler KeyActioned;

        internal void RaiseKeyActioned(KeyEventArgs Arguments)
        {
            var Handler = this.KeyActioned;
            if (Handler != null)
                Handler(this, Arguments);
        }
    }
}
