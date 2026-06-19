// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Copyright (C) 2011-2016 Néstor Marcel Sánchez Ahumada.
// https://github.com/nmarcel/ThinkComposer
//
// This file is part of ThinkComposer, which is free software licensed under the GNU General Public License.
// It is provided without any warranty. You should find a copy of the license in the root directory of this software product.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Instrumind.Common;
using Instrumind.Common.Visualization;

using Instrumind.ThinkComposer.MetaModel.GraphMetaModel;
using Instrumind.ThinkComposer.Model.GraphModel;
using Instrumind.ThinkComposer.Model.VisualModel;

namespace Instrumind.ThinkComposer.Composer.ComposerUI
{
    /// <summary>
    /// Replaces a single concept visual with a shortcut visual to another concept, preserving the source concept.
    /// </summary>
    public static class ShortcutReplacementCommand
    {
        public static bool CanReplaceWithShortcut(VisualSymbol SourceSymbol)
        {
            if (SourceSymbol == null || SourceSymbol.OwnerRepresentation == null)
                return false;

            return SourceSymbol.OwnerRepresentation is ConceptVisualRepresentation
                   && !SourceSymbol.OwnerRepresentation.IsShortcut
                   && SourceSymbol.OwnerRepresentation.RepresentedIdea is Concept;
        }

        public static void ReplaceWithShortcut(VisualSymbol SourceSymbol)
        {
            if (!CanReplaceWithShortcut(SourceSymbol))
            {
                Display.DialogMessage("Replace with Shortcut", "Select a non-shortcut concept symbol to replace.", EMessageType.Information);
                return;
            }

            var SourceRepresentation = SourceSymbol.OwnerRepresentation as ConceptVisualRepresentation;
            var SourceConcept = SourceRepresentation.RepresentedConcept;
            var View = SourceRepresentation.DisplayingView;
            var Engine = View == null ? null : View.Engine;

            if (Engine == null || Engine.TargetComposition == null)
            {
                Display.DialogMessage("Replace with Shortcut", "No active composition/view was found.", EMessageType.Warning);
                return;
            }

            var TargetConcept = ShortcutTargetSelectorDialog.SelectTargetConcept(Engine, SourceConcept);
            if (TargetConcept == null)
                return;

            if (TargetConcept == SourceConcept)
            {
                Display.DialogMessage("Replace with Shortcut", "The shortcut target must be a different concept.", EMessageType.Warning);
                return;
            }

            var Relinks = CollectCurrentViewRelinks(SourceSymbol, SourceConcept, TargetConcept).ToList();
            var Problems = Relinks.Select(Relink => ValidateRelink(Relink, TargetConcept))
                                  .Where(Problem => !Problem.IsAbsent()).ToList();

            if (Problems.Count > 0)
            {
                var Message = "The selected concept cannot be replaced with a shortcut because one or more visible relationships would become invalid." +
                              Environment.NewLine + Environment.NewLine +
                              String.Join(Environment.NewLine, Problems.Take(8).ToArray());
                if (Problems.Count > 8)
                    Message += Environment.NewLine + "... " + (Problems.Count - 8).ToString() + " more.";

                Console.WriteLine("Replace with Shortcut blocked.");
                foreach (var Problem in Problems)
                    Console.WriteLine("  " + Problem);

                Display.DialogMessage("Replace with Shortcut", Message, EMessageType.Warning);
                return;
            }

            try
            {
                Engine.StartCommandVariation("Replace with Shortcut");

                var Center = SourceSymbol.BaseCenter;
                var Width = SourceSymbol.BaseWidth;
                var Height = SourceSymbol.BaseHeight;

                var ShortcutRepresentation = ConceptCreationCommand.CreateConceptVisualRepresentation(TargetConcept, View, Center, true, true, Width, Height);
                ShortcutRepresentation.MainSymbol.ResizeTo(Width, Height);
                ShortcutRepresentation.MainSymbol.MoveTo(Center.X, Center.Y, true);
                ShortcutRepresentation.MainSymbol.AreDetailsShown = SourceSymbol.AreDetailsShown;
                ShortcutRepresentation.MainSymbol.DetailsPosterHeight = SourceSymbol.DetailsPosterHeight;

                foreach (var Relink in Relinks)
                {
                    RelationshipCreationCommand.RelinkRelationship(Relink.Connector, ShortcutRepresentation.MainSymbol,
                                                                   ShortcutRepresentation.MainSymbol.BaseCenter, Relink.IsConnectingTarget);

                    if (Relink.Connector.RepresentedLink.AssociatedIdea != TargetConcept)
                        throw new InvalidOperationException("Relationship link was not reassigned: " + DescribeRelationship(Relink.Relationship));

                    Relink.Connector.RepresentedLink.UpdateVersion();
                    Relink.Relationship.UpdateVisualRepresentators();
                }

                SourceRepresentation.Clear();
                SourceConcept.VisualRepresentators.Remove(SourceRepresentation);

                View.SelectedObjects.Remove(SourceSymbol);
                View.SelectObject(ShortcutRepresentation.MainSymbol);
                View.UpdateVersion();
                SourceConcept.UpdateVisualRepresentators();
                TargetConcept.UpdateVisualRepresentators();

                Console.WriteLine("Replace with Shortcut completed.");
                Console.WriteLine("  source=" + DescribeConcept(SourceConcept));
                Console.WriteLine("  target=" + DescribeConcept(TargetConcept));
                Console.WriteLine("  shortcutVisual=" + ShortcutRepresentation.GlobalId.ToString("D"));
                Console.WriteLine("  currentViewLinksReassigned=" + Relinks.Count.ToString());
                Console.WriteLine("  sourceVisualsRemaining=" + SourceConcept.VisualRepresentators.Count.ToString());
                Console.WriteLine("  sourceLinksRemaining=" + SourceConcept.AssociatingLinks.Count.ToString());

                Engine.CompleteCommandVariation();
            }
            catch (Exception Problem)
            {
                Console.WriteLine("Replace with Shortcut failed: " + Problem.ToString());
                Engine.DiscardCommandVariation();
                Display.DialogMessage("Replace with Shortcut", "The shortcut replacement could not be completed." +
                                      Environment.NewLine + Environment.NewLine + Problem.Message, EMessageType.Error);
            }
        }

        private static IEnumerable<RelinkPlan> CollectCurrentViewRelinks(VisualSymbol SourceSymbol, Concept SourceConcept, Concept TargetConcept)
        {
            var Connectors = SourceSymbol.TargetConnections.Concat(SourceSymbol.OriginConnections).Distinct().ToList();

            foreach (var Connector in Connectors)
            {
                if (Connector == null || Connector.RepresentedLink == null ||
                    Connector.RepresentedLink.AssociatedIdea != SourceConcept ||
                    Connector.OwnerRelationshipRepresentation == null)
                    continue;

                var Relationship = Connector.OwnerRelationshipRepresentation.RepresentedRelationship;
                if (Relationship == null)
                    continue;

                var IsConnectingTarget = Connector.TargetSymbol == SourceSymbol;
                var IsConnectingOrigin = Connector.OriginSymbol == SourceSymbol;
                if (!IsConnectingTarget && !IsConnectingOrigin)
                    continue;

                yield return new RelinkPlan(Connector, Relationship, IsConnectingTarget);
            }
        }

        private static string ValidateRelink(RelinkPlan Relink, Concept TargetConcept)
        {
            var Relationship = Relink.Relationship;
            var Link = Relink.Connector.RepresentedLink;

            if (Relationship == null || Link == null || Link.RoleDefinitor == null)
                return "Relationship link metadata is incomplete.";

            if (Relationship.Links.Any(Other => Other != Link && Other.RoleDefinitor == Link.RoleDefinitor && Other.AssociatedIdea == TargetConcept))
                return "Relationship '" + Relationship.TechName + "' already has a '" + Link.RoleDefinitor.TechName +
                       "' link to target concept '" + TargetConcept.TechName + "'.";

            var Definition = Relationship.RelationshipDefinitor == null ? null : Relationship.RelationshipDefinitor.Value;
            if (Definition == null)
                return "Relationship '" + Relationship.TechName + "' has no relationship definition.";

            if (Link.RoleDefinitor.RoleType == ERoleType.Target)
            {
                var Origins = Relationship.Links.Where(Other => Other != Link && Other.RoleDefinitor != null &&
                                                                Other.RoleDefinitor.RoleType != ERoleType.Target).ToList();
                foreach (var Origin in Origins)
                {
                    var CanLink = Definition.CanLink(Origin.AssociatedIdea.IdeaDefinitor, TargetConcept.IdeaDefinitor);
                    if (CanLink.Result)
                        return null;
                }

                if (Origins.Count < 1)
                    return null;

                return BuildCompatibilityMessage(Relationship, Definition.TechName, Origins.First().AssociatedIdea, TargetConcept);
            }

            var Targets = Relationship.Links.Where(Other => Other != Link && Other.RoleDefinitor != null &&
                                                            Other.RoleDefinitor.RoleType == ERoleType.Target).ToList();
            foreach (var Target in Targets)
            {
                var CanLink = Definition.CanLink(TargetConcept.IdeaDefinitor, Target.AssociatedIdea.IdeaDefinitor);
                if (CanLink.Result)
                    return null;
            }

            if (Targets.Count < 1)
                return null;

            return BuildCompatibilityMessage(Relationship, Definition.TechName, TargetConcept, Targets.First().AssociatedIdea);
        }

        private static string BuildCompatibilityMessage(Relationship Relationship, string DefinitionTechName, Idea Origin, Idea Target)
        {
            return "Relationship '" + Relationship.TechName + "' would violate definition '" + DefinitionTechName +
                   "' for " + DescribeIdeaDef(Origin) + " -> " + DescribeIdeaDef(Target) + ".";
        }

        private static string DescribeIdeaDef(Idea Idea)
        {
            if (Idea == null || Idea.IdeaDefinitor == null)
                return "<unknown>";

            return Idea.TechName + " [" + Idea.IdeaDefinitor.TechName + "]";
        }

        private static string DescribeRelationship(Relationship Relationship)
        {
            if (Relationship == null)
                return "<none>";

            return "name='" + Relationship.Name + "' techName='" + Relationship.TechName + "' id=" + Relationship.GlobalId.ToString("D");
        }

        private static string DescribeConcept(Concept Concept)
        {
            if (Concept == null)
                return "<none>";

            return "name='" + Concept.Name + "' techName='" + Concept.TechName + "' id=" + Concept.GlobalId.ToString("D");
        }

        private sealed class RelinkPlan
        {
            public RelinkPlan(VisualConnector Connector, Relationship Relationship, bool IsConnectingTarget)
            {
                this.Connector = Connector;
                this.Relationship = Relationship;
                this.IsConnectingTarget = IsConnectingTarget;
            }

            public VisualConnector Connector { get; private set; }
            public Relationship Relationship { get; private set; }
            public bool IsConnectingTarget { get; private set; }
        }

        private sealed class ShortcutTargetSelectorDialog : Window
        {
            private readonly List<ShortcutTargetCandidate> AllCandidates;
            private readonly TextBox SearchBox;
            private readonly ListBox CandidatesList;
            private readonly TextBlock DetailsText;
            private readonly Button OkButton;

            private ShortcutTargetSelectorDialog(IEnumerable<ShortcutTargetCandidate> Candidates, ShortcutTargetCandidate Preferred)
            {
                this.AllCandidates = Candidates.ToList();
                this.Title = "Replace with Shortcut";
                this.Width = 680;
                this.Height = 520;
                this.MinWidth = 520;
                this.MinHeight = 380;
                this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                this.Owner = Application.Current == null ? null : Application.Current.MainWindow;

                var Root = new Grid();
                Root.Margin = new Thickness(12);
                Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var Intro = new TextBlock();
                Intro.Text = "Choose the existing concept that should be shown here as a shortcut.";
                Intro.TextWrapping = TextWrapping.Wrap;
                Intro.Margin = new Thickness(0, 0, 0, 8);
                Grid.SetRow(Intro, 0);
                Root.Children.Add(Intro);

                this.SearchBox = new TextBox();
                this.SearchBox.Margin = new Thickness(0, 0, 0, 8);
                this.SearchBox.ToolTip = "Search by name, techName, summary, definition, or id.";
                Grid.SetRow(this.SearchBox, 1);
                Root.Children.Add(this.SearchBox);

                this.CandidatesList = new ListBox();
                this.CandidatesList.MinHeight = 220;
                Grid.SetRow(this.CandidatesList, 2);
                Root.Children.Add(this.CandidatesList);

                this.DetailsText = new TextBlock();
                this.DetailsText.Margin = new Thickness(0, 8, 0, 8);
                this.DetailsText.TextWrapping = TextWrapping.Wrap;
                Grid.SetRow(this.DetailsText, 3);
                Root.Children.Add(this.DetailsText);

                var Buttons = new StackPanel();
                Buttons.Orientation = Orientation.Horizontal;
                Buttons.HorizontalAlignment = HorizontalAlignment.Right;

                this.OkButton = new Button();
                this.OkButton.Content = "OK";
                this.OkButton.MinWidth = 80;
                this.OkButton.Margin = new Thickness(0, 0, 8, 0);
                this.OkButton.IsDefault = true;
                this.OkButton.Click += delegate { AcceptSelection(); };
                Buttons.Children.Add(this.OkButton);

                var CancelButton = new Button();
                CancelButton.Content = "Cancel";
                CancelButton.MinWidth = 80;
                CancelButton.IsCancel = true;
                Buttons.Children.Add(CancelButton);

                Grid.SetRow(Buttons, 4);
                Root.Children.Add(Buttons);

                this.Content = Root;

                this.SearchBox.TextChanged += delegate { ApplyFilter(null); };
                this.CandidatesList.SelectionChanged += delegate { RefreshDetails(); };
                this.CandidatesList.MouseDoubleClick += delegate { AcceptSelection(); };
                this.Loaded += delegate
                {
                    ApplyFilter(Preferred);
                    this.SearchBox.Focus();
                };
            }

            public Concept SelectedConcept { get; private set; }

            public static Concept SelectTargetConcept(CompositionEngine Engine, Concept SourceConcept)
            {
                var Candidates = BuildCandidates(Engine, SourceConcept).ToList();
                if (Candidates.Count < 1)
                {
                    Display.DialogMessage("Replace with Shortcut", "No other concepts are available as shortcut targets.", EMessageType.Information);
                    return null;
                }

                var Preferred = Candidates.FirstOrDefault(Candidate => Candidate.IsCopiedConcept)
                                ?? Candidates.FirstOrDefault(Candidate => Candidate.HasSameTechName)
                                ?? Candidates.FirstOrDefault();

                var Dialog = new ShortcutTargetSelectorDialog(Candidates, Preferred);
                Dialog.ShowDialog();
                return Dialog.SelectedConcept;
            }

            private static IEnumerable<ShortcutTargetCandidate> BuildCandidates(CompositionEngine Engine, Concept SourceConcept)
            {
                var CopiedConcept = GetSingleCopiedConcept(SourceConcept);
                var Concepts = Engine.TargetComposition.GetNestedCompositeIdeas()
                                  .OfType<Concept>()
                                  .Where(Concept => Concept != SourceConcept)
                                  .Distinct()
                                  .Select(Concept => new ShortcutTargetCandidate(Concept, SourceConcept, CopiedConcept))
                                  .OrderByDescending(Candidate => Candidate.IsCopiedConcept)
                                  .ThenByDescending(Candidate => Candidate.HasSameTechName)
                                  .ThenBy(Candidate => Candidate.Name)
                                  .ThenBy(Candidate => Candidate.TechName)
                                  .ThenBy(Candidate => Candidate.Id);

                return Concepts;
            }

            private static Concept GetSingleCopiedConcept(Concept SourceConcept)
            {
                var Concepts = CompositionEngine.ClipboardTransferSelectedObjects
                               .OfType<VisualElement>()
                               .Where(Element => Element.OwnerRepresentation != null)
                               .Select(Element => Element.OwnerRepresentation.RepresentedIdea as Concept)
                               .Where(Concept => Concept != null && Concept != SourceConcept)
                               .Distinct()
                               .ToList();

                return Concepts.Count == 1 ? Concepts[0] : null;
            }

            private void ApplyFilter(ShortcutTargetCandidate Preferred)
            {
                var Text = this.SearchBox.Text;
                var Filtered = this.AllCandidates.Where(Candidate => Candidate.Matches(Text)).ToList();
                this.CandidatesList.ItemsSource = Filtered;

                if (Preferred != null && Filtered.Contains(Preferred))
                    this.CandidatesList.SelectedItem = Preferred;
                else
                    this.CandidatesList.SelectedItem = Filtered.FirstOrDefault();

                this.OkButton.IsEnabled = this.CandidatesList.SelectedItem != null;
                RefreshDetails();
            }

            private void RefreshDetails()
            {
                var Candidate = this.CandidatesList.SelectedItem as ShortcutTargetCandidate;
                this.OkButton.IsEnabled = Candidate != null;
                this.DetailsText.Text = Candidate == null ? "No matching concept." : Candidate.Description;
            }

            private void AcceptSelection()
            {
                var Candidate = this.CandidatesList.SelectedItem as ShortcutTargetCandidate;
                if (Candidate == null)
                    return;

                this.SelectedConcept = Candidate.Concept;
                this.DialogResult = true;
                this.Close();
            }
        }

        private sealed class ShortcutTargetCandidate
        {
            private readonly string SearchText;

            public ShortcutTargetCandidate(Concept Concept, Concept SourceConcept, Concept CopiedConcept)
            {
                this.Concept = Concept;
                this.Name = Concept.Name ?? "";
                this.TechName = Concept.TechName ?? "";
                this.Id = Concept.GlobalId.ToString("D");
                this.DefinitionTechName = Concept.ConceptDefinitor == null || Concept.ConceptDefinitor.Value == null
                                          ? ""
                                          : Concept.ConceptDefinitor.Value.TechName;
                this.Summary = Concept.Summary ?? "";
                this.HasSameTechName = !this.TechName.IsAbsent() &&
                                       String.Equals(this.TechName, SourceConcept.TechName, StringComparison.OrdinalIgnoreCase);
                this.IsCopiedConcept = CopiedConcept != null && CopiedConcept == Concept;
                this.Description = "name: " + this.Name + Environment.NewLine +
                                   "techName: " + this.TechName + Environment.NewLine +
                                   "definition: " + this.DefinitionTechName + Environment.NewLine +
                                   "id: " + this.Id + Environment.NewLine +
                                   "summary: " + this.Summary;
                this.SearchText = (this.Name + " " + this.TechName + " " + this.DefinitionTechName + " " +
                                   this.Id + " " + this.Summary).ToUpperInvariant();
            }

            public Concept Concept { get; private set; }
            public string Name { get; private set; }
            public string TechName { get; private set; }
            public string Id { get; private set; }
            public string DefinitionTechName { get; private set; }
            public string Summary { get; private set; }
            public bool HasSameTechName { get; private set; }
            public bool IsCopiedConcept { get; private set; }
            public string Description { get; private set; }

            public bool Matches(string Text)
            {
                if (Text.IsAbsent())
                    return true;

                return this.SearchText.Contains(Text.ToUpperInvariant());
            }

            public override string ToString()
            {
                var Prefix = this.IsCopiedConcept ? "[copied] " : (this.HasSameTechName ? "[same techName] " : "");
                return Prefix + this.Name + "  (" + this.TechName + ", " + this.DefinitionTechName + ")";
            }
        }
    }
}
