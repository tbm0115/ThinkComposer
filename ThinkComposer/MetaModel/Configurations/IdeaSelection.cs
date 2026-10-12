using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

using Instrumind.Common;

using Instrumind.ThinkComposer.Model.GraphModel;
using Instrumind.Common.Visualization;

namespace Instrumind.ThinkComposer.MetaModel.Configurations
{
    /// <summary>
    /// References an Idea to be selected for later processing (such as file generation).
    /// </summary>
    public class IdeaSelection : INotifyPropertyChanged
    {
        public static IdeaSelection CreateSelectionTree(Idea Source, IEnumerable<string> PreExclusion = null,
                                                        Func<Idea, Tuple<bool,      // is-selectable
                                                                         bool,      // is-selected
                                                                         IRecognizableElement>    // action-info
                                                            > IdeaSelectionDeterminer = null,
                                                        IdeaSelection DirectParent = null)
        {
            // Build one lookup for the complete tree, including when most ideas are excluded.
            var ExcludedIds = new HashSet<string>(PreExclusion ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            return CreateSelectionTreeCore(Source, ExcludedIds, IdeaSelectionDeterminer, DirectParent);
        }

        private static IdeaSelection CreateSelectionTreeCore(Idea Source, HashSet<string> ExcludedIds,
            Func<Idea, Tuple<bool, bool, IRecognizableElement>> IdeaSelectionDeterminer, IdeaSelection DirectParent)
        {
            var Selection = (IdeaSelectionDeterminer == null
                             ? Tuple.Create(false, false, (IRecognizableElement)null)
                             : IdeaSelectionDeterminer(Source));

            var Result = new IdeaSelection
            {
                SourceIdea = Source,
                IsSelectable = Selection.Item1,
                IsSelected = (Selection.Item2
                              && !ExcludedIds.Contains(Source.GlobalId.ToString())),
                ActionInfo = Selection.Item3,
                Parent = DirectParent,
                IsExpanded = (DirectParent == null)
            };

            if (Source.CompositeIdeas.Count > 0)
            {
                Result.Children = new List<IdeaSelection>();
                foreach (var Composite in Source.CompositeIdeas)
                    if (Composite is Concept || Composite.CompositeIdeas.Count > 0)
                        Result.Children.Add(CreateSelectionTreeCore(Composite, ExcludedIds, IdeaSelectionDeterminer, Result));
            }

            return Result;
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------------------------
        public Idea SourceIdea { get; set; }

        public IdeaSelection Parent { get; set; }

        public bool IsSelectable { get; set; }

        // Keep expansion with the item when WPF recycles its visual container.
        public bool IsExpanded
        {
            get { return this.IsExpanded_; }
            set
            {
                if (this.IsExpanded_ == value) return;
                this.IsExpanded_ = value;
                this.NotifyPropertyChange("IsExpanded");
            }
        }
        private bool IsExpanded_;

        public bool IsSelected
        {
            get { return this.IsSelected_; }
            set
            {
                value = (this.IsSelectable && value);
                if (this.IsSelected_ == value)
                    return;

                this.IsSelected_ = value;
                this.NotifyPropertyChange("IsSelected");

                if (this.Parent != null)
                    this.Parent.NotifyPropertyChange("AreChildrenSelected");
            }
        }
        private bool IsSelected_ = false;

        public IRecognizableElement ActionInfo { get; set; }

        public bool HasChildren { get { return (this.Children != null && this.Children.Count > 0); } }

        public bool AreChildrenSelected
        {
            get
            {
                if (this.Children == null || this.Children.Count < 1)
                    return false;

                // Notice that only one unselected (selectable) child makes this flag false
                var Result = !this.Children.Any(chld => !chld.IsSelectable || !chld.IsSelected);
                return Result;
            }
            set
            {
                if (this.Children != null && this.Children.Count > 0)
                    foreach (var Child in this.Children)
                    {
                        Child.IsSelected = value;
                        Child.AreChildrenSelected = value;
                    }

                this.NotifyPropertyChange("AreChildrenSelected");
            }
        }

        public IList<IdeaSelection> Children { get; set; }

        public IList<IdeaSelection> GetSelection(Nullable<bool> FilterIsSelected = null)
        {
            var Result = new List<IdeaSelection>();

            this.GetCompleteTree(Result);

            if (FilterIsSelected != null)
                Result = Result.Where(ideasel => ideasel.IsSelected == FilterIsSelected.Value).ToList();

            return Result;
        }

        private void GetCompleteTree(IList<IdeaSelection> Result)
        {
            Result.Add(this);

            if (this.Children != null && this.Children.Count > 0)
                foreach (var Child in Children)
                    Child.GetCompleteTree(Result);
        }

        public void ApplySelector(Func<Idea, bool?> Selector)
        {
            var Selected = Selector(this.SourceIdea);
            if (Selected != null)
                this.IsSelected = Selected.Value;

            if (Children != null)
                foreach (var Child in Children)
                    Child.ApplySelector(Selector);
        }

        public void NotifyPropertyChange(string PropertyName)
        {
            var Handler = this.PropertyChanged;
            if (Handler == null)
                return;

            Handler(this, new PropertyChangedEventArgs(PropertyName));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
