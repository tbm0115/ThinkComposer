// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// In-memory document visualizer for command-line operations.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

using Instrumind.Common;
using Instrumind.Common.Visualization;

namespace Instrumind.ThinkComposer.Headless
{
    public sealed class HeadlessDocumentVisualizer : IDocumentVisualizer
    {
        private readonly Dictionary<Guid, IDocumentView> Views = new Dictionary<Guid, IDocumentView>();
        private IDocumentView ActiveViewValue = null;

        public IEnumerable<IDocumentView> GetAllViews(ISphereModel ParentDocument = null)
        {
            if (ParentDocument == null)
                return this.Views.Values.ToArray();

            return this.Views.Values.Where(View => View != null && View.ParentDocument == ParentDocument).ToArray();
        }

        public ScrollViewer PutView(IDocumentView DocView)
        {
            General.ContractRequiresNotNull(DocView);

            var ScrollPresenter = DocView.HostingScrollViewer;
            if (ScrollPresenter == null)
                ScrollPresenter = new ScrollViewer();

            var HostingGrid = DocView.PresenterHostingGrid;
            if (HostingGrid == null)
                HostingGrid = new Grid();

            if (ScrollPresenter.Parent == null && !HostingGrid.Children.Contains(ScrollPresenter))
                HostingGrid.Children.Add(ScrollPresenter);

            if (DocView.TopCanvas != null && DocView.TopCanvas.Parent == null && !HostingGrid.Children.Contains(DocView.TopCanvas))
                HostingGrid.Children.Add(DocView.TopCanvas);

            if (DocView.PresenterControl != null && DocView.PresenterControl.Parent == null)
                ScrollPresenter.Content = DocView.PresenterControl;

            DocView.PresenterHostingGrid = HostingGrid;
            DocView.HostingScrollViewer = ScrollPresenter;
            this.Views[DocView.GlobalId] = DocView;
            this.ActiveView = DocView;

            return ScrollPresenter;
        }

        public void DiscardView(Guid Key)
        {
            IDocumentView View = null;
            if (!this.Views.TryGetValue(Key, out View))
                return;

            if (this.CloseConfirmation != null && !this.CloseConfirmation(Key))
                return;

            if (this.ActiveViewValue == View)
                this.ActiveViewValue = null;

            if (View.HostingScrollViewer != null)
                View.HostingScrollViewer.Content = null;

            View.HostingScrollViewer = null;
            View.PresenterHostingGrid = null;
            this.Views.Remove(Key);
        }

        public void DiscardAllViews(ISphereModel ParentDocument = null)
        {
            var Targets = this.Views.Values
                .Where(View => ParentDocument == null || View.ParentDocument == ParentDocument)
                .Select(View => View.GlobalId)
                .ToArray();

            foreach (var Target in Targets)
                this.DiscardView(Target);
        }

        public IDocumentView ActiveView
        {
            get { return this.ActiveViewValue; }
            set
            {
                if (this.ActiveViewValue == value)
                    return;

                if (value != null && !this.Views.ContainsKey(value.GlobalId))
                    throw new UsageAnomaly("Cannot activate a non registered Document View.", value);

                this.ActiveViewValue = value;

                if (this.PostViewActivation != null && value != null)
                    this.PostViewActivation(value);
            }
        }

        public Action<IDocumentView> PostViewActivation { get; set; }

        public Func<Guid, bool> CloseConfirmation { get; set; }

        public void ScrollSegment(Orientation Direction, double Offset)
        {
        }
    }
}
