// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Helpers for validating composite/nested view visual integrity.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

using Instrumind.Common;
using Instrumind.ThinkComposer.Model;
using Instrumind.ThinkComposer.Model.GraphModel;

namespace Instrumind.ThinkComposer.Model.VisualModel
{
    internal static class CompositeViewIntegrity
    {
        [ThreadStatic]
        private static List<Guid> CompositeRenderStack;

        public static string CurrentRenderStack
        {
            get
            {
                var Stack = GetRenderStack();
                return Stack.Count < 1 ? "<empty>" : String.Join(" > ", Stack.Select(Id => Id.ToString("D")).ToArray());
            }
        }

        public static string GetToggleDiagnostic(VisualSymbol SourceSymbol)
        {
            if (SourceSymbol == null || SourceSymbol.OwnerRepresentation == null)
                return "Composite toggle diagnostic: source symbol is not available.";

            var SourceIdea = SourceSymbol.OwnerRepresentation.RepresentedIdea;
            var SourceView = SourceSymbol.GetDisplayingView();
            var CompositeView = SourceIdea == null ? null : SourceIdea.CompositeActiveView;
            var ContainsSelfVisual = SourceIdea != null && CompositeView != null && ContainsVisualOfIdea(CompositeView, SourceIdea);
            var ContainsSelfComplement = CompositeView != null && ContainsViewComplementTargetingSameView(CompositeView);

            return "Composite toggle diagnostic: source=" + DescribeIdea(SourceIdea) +
                   ", sourceView=" + DescribeView(SourceView) +
                   ", compositeView=" + DescribeView(CompositeView) +
                   ", compositeContainsSourceVisual=" + (ContainsSelfVisual ? "true" : "false") +
                   ", compositeContainsSelfViewComplement=" + (ContainsSelfComplement ? "true" : "false") +
                   ", renderStack=" + CurrentRenderStack + ".";
        }

        public static bool CanShowCompositeContentAsDetail(VisualSymbol SourceSymbol, out string Warning)
        {
            Warning = ValidateCompositeContentRender(SourceSymbol);
            return Warning == null;
        }

        public static bool TryEnterCompositeContentRender(VisualSymbol SourceSymbol, out string Warning)
        {
            Warning = ValidateCompositeContentRender(SourceSymbol);
            if (Warning != null)
                return false;

            var CompositeView = SourceSymbol.OwnerRepresentation.RepresentedIdea.CompositeActiveView;
            GetRenderStack().Add(CompositeView.GlobalId);
            return true;
        }

        public static void ExitCompositeContentRender(VisualSymbol SourceSymbol)
        {
            if (SourceSymbol == null || SourceSymbol.OwnerRepresentation == null ||
                SourceSymbol.OwnerRepresentation.RepresentedIdea == null ||
                SourceSymbol.OwnerRepresentation.RepresentedIdea.CompositeActiveView == null)
                return;

            var Stack = GetRenderStack();
            var ViewId = SourceSymbol.OwnerRepresentation.RepresentedIdea.CompositeActiveView.GlobalId;
            for (int Index = Stack.Count - 1; Index >= 0; Index--)
                if (Stack[Index] == ViewId)
                {
                    Stack.RemoveAt(Index);
                    return;
                }
        }

        public static bool IsSelfRecursiveConceptPlacement(Concept Concept, View TargetView, out string Warning)
        {
            Warning = null;
            if (Concept == null || TargetView == null)
                return false;

            if (TargetView.OwnerCompositeContainer == Concept)
            {
                Warning = "Cannot place concept '" + Concept.TechName.ToStringAlways() +
                          "' inside its own composite view '" + TargetView.TechName.ToStringAlways() +
                          "' because that can recursively render nested content.";
                return true;
            }

            return false;
        }

        public static bool IsSelfRecursiveRelationshipPlacement(Relationship Relationship, View TargetView, out string Warning)
        {
            Warning = null;
            if (Relationship == null || TargetView == null || TargetView.OwnerCompositeContainer == null)
                return false;

            var Owner = TargetView.OwnerCompositeContainer;
            if (Relationship.Links != null && Relationship.Links.Any(Link => Link.AssociatedIdea == Owner))
            {
                Warning = "Skipped visual placement for relationship '" + Relationship.TechName.ToStringAlways() +
                          "': an endpoint is the owner of target composite view '" + TargetView.TechName.ToStringAlways() +
                          "'. Auto-placing the owner inside its own view would create recursive nested content rendering.";
                return true;
            }

            return false;
        }

        public static int CountRecursiveVisualRepairs(Composition Composition)
        {
            return RepairRecursiveVisuals(Composition, null, true);
        }

        public static int RepairRecursiveVisuals(Composition Composition, Action<string> Log, bool PreviewOnly)
        {
            if (Composition == null)
                return 0;

            var Repairs = 0;
            foreach (var View in Composition.GetSubgraphChildren().SelectMany(Idea => Idea.CompositeViews).Distinct().ToList())
            {
                if (View == null || View.OwnerCompositeContainer == null)
                    continue;

                var Owner = View.OwnerCompositeContainer;

                var RecursiveRelationshipRepresentations = Composition.GetSubgraphChildren().OfType<Relationship>()
                    .SelectMany(Rel => Rel.VisualRepresentators.OfType<RelationshipVisualRepresentation>())
                    .Where(Rep => Rep.DisplayingView == View && Rep.RepresentedRelationship != null &&
                                  Rep.RepresentedRelationship.Links != null &&
                                  Rep.RepresentedRelationship.Links.Any(Link => Link.AssociatedIdea == Owner))
                    .ToList();

                foreach (var Representation in RecursiveRelationshipRepresentations)
                {
                    Repairs++;
                    var Message = "Removed self-recursive relationship visual for relationship '" +
                                  Representation.RepresentedRelationship.TechName.ToStringAlways() +
                                  "' from composite view '" + View.TechName.ToStringAlways() + "'.";
                    if (Log != null)
                        Log(Message);
                    if (!PreviewOnly)
                        RemoveVisualRepresentation(Representation);
                }

                var RecursiveConceptRepresentations = Owner.VisualRepresentators.OfType<ConceptVisualRepresentation>()
                    .Where(Rep => Rep.DisplayingView == View)
                    .ToList();

                foreach (var Representation in RecursiveConceptRepresentations)
                {
                    Repairs++;
                    var Message = "Removed self-recursive visual for concept '" + Owner.TechName.ToStringAlways() +
                                  "' from its own composite view '" + View.TechName.ToStringAlways() + "'.";
                    if (Log != null)
                        Log(Message);
                    if (!PreviewOnly)
                        RemoveVisualRepresentation(Representation);
                }
            }

            return Repairs;
        }

        public static int RepairInvalidVisualRepresentations(Composition Composition, Action<string> Log, bool PreviewOnly)
        {
            if (Composition == null)
                return 0;

            var Repairs = 0;
            var InvalidRepresentations = new HashSet<VisualRepresentation>();
            var Ideas = Composition.GetSubgraphChildren().Where(Idea => Idea != null).Distinct().ToList();

            foreach (var Idea in Ideas)
            {
                if (Idea.VisualRepresentators == null)
                    continue;

                var Representations = Idea.VisualRepresentators.ToList();
                foreach (var Representation in Representations)
                    if (IsInvalidVisualRepresentation(Representation, Idea))
                    {
                        Repairs++;
                        if (Representation != null)
                            InvalidRepresentations.Add(Representation);

                        if (Log != null)
                            Log("Removed invalid visual representation for idea '" +
                                Idea.TechName.ToStringAlways() + "' from view '" +
                                (Representation == null || Representation.DisplayingView == null
                                 ? "<none>"
                                 : Representation.DisplayingView.TechName.ToStringAlways()) +
                                "': " + GetInvalidVisualRepresentationReason(Representation, Idea) + ".");

                        if (!PreviewOnly)
                            RemoveVisualRepresentation(Representation, Idea);
                    }
            }

            var Views = Ideas.SelectMany(Idea => Idea.CompositeViews ?? Enumerable.Empty<View>()).Where(View => View != null).Distinct().ToList();
            foreach (var View in Views)
            {
                if (View.ViewChildren == null)
                    continue;

                var OrphanChildren = View.ViewChildren
                                         .Where(Child => IsInvalidViewChild(View, Child, InvalidRepresentations))
                                         .ToList();

                foreach (var Child in OrphanChildren)
                {
                    Repairs++;
                    if (Log != null)
                        Log("Removed invalid visual child from view '" + View.TechName.ToStringAlways() +
                            "': " + GetInvalidViewChildReason(View, Child) + ".");

                    if (!PreviewOnly)
                        RemoveViewChild(View, Child);
                }
            }

            return Repairs;
        }

        private static string ValidateCompositeContentRender(VisualSymbol SourceSymbol)
        {
            if (SourceSymbol == null || SourceSymbol.OwnerRepresentation == null)
                return "Cannot show nested content because the source symbol is not available.";

            var SourceIdea = SourceSymbol.OwnerRepresentation.RepresentedIdea;
            if (SourceIdea == null)
                return "Cannot show nested content because the source concept is not available.";

            var CompositeView = SourceIdea.CompositeActiveView;
            if (CompositeView == null)
                return null;

            var SourceView = SourceSymbol.GetDisplayingView();
            if (SourceView == CompositeView)
                return "Cannot show nested content for concept '" + SourceIdea.TechName.ToStringAlways() +
                       "' because it would render view '" + CompositeView.TechName.ToStringAlways() + "' inside itself.";

            if (GetRenderStack().Contains(CompositeView.GlobalId))
                return "Cannot show nested content for concept '" + SourceIdea.TechName.ToStringAlways() +
                       "' because composite view '" + CompositeView.TechName.ToStringAlways() +
                       "' is already in the nested render stack: " + CurrentRenderStack + ".";

            if (ContainsVisualOfIdea(CompositeView, SourceIdea))
                return "Cannot show nested content for concept '" + SourceIdea.TechName.ToStringAlways() +
                       "' because its composite view '" + CompositeView.TechName.ToStringAlways() +
                       "' contains a visual representation of the same concept.";

            if (ContainsViewComplementTargetingSameView(CompositeView))
                return "Cannot show nested content for concept '" + SourceIdea.TechName.ToStringAlways() +
                       "' because composite view '" + CompositeView.TechName.ToStringAlways() +
                       "' contains a view-level complement targeting the same nested view.";

            return null;
        }

        private static bool ContainsVisualOfIdea(View View, Idea Idea)
        {
            if (View == null || Idea == null)
                return false;

            if (Idea.VisualRepresentators.Any(Rep => Rep.DisplayingView == View && Rep.MainSymbol != null))
                return true;

            return View.ViewChildren.Any(Child => Child != null &&
                                                  Child.Key is VisualSymbol &&
                                                  ((VisualSymbol)Child.Key).OwnerRepresentation != null &&
                                                  ((VisualSymbol)Child.Key).OwnerRepresentation.RepresentedIdea == Idea);
        }

        private static bool ContainsViewComplementTargetingSameView(View View)
        {
            if (View == null)
                return false;

            return View.ViewChildren.Any(Child => Child != null &&
                                                  Child.Key is VisualComplement &&
                                                  ((VisualComplement)Child.Key).Target != null &&
                                                  ((VisualComplement)Child.Key).Target.IsGlobal &&
                                                  ((VisualComplement)Child.Key).Target.OwnerGlobal == View);
        }

        private static bool IsInvalidVisualRepresentation(VisualRepresentation Representation, Idea ExpectedIdea)
        {
            return !String.IsNullOrEmpty(GetInvalidVisualRepresentationReason(Representation, ExpectedIdea));
        }

        private static string GetInvalidVisualRepresentationReason(VisualRepresentation Representation, Idea ExpectedIdea)
        {
            if (Representation == null)
                return "visual representation entry is null";

            if (Representation.DisplayingView == null)
                return "displaying view is null";

            if (Representation.RepresentedIdea == null)
                return "represented idea is null";

            if (ExpectedIdea != null && Representation.RepresentedIdea != ExpectedIdea)
                return "represented idea does not match the owning idea's visual list";

            if (Representation.VisualParts == null)
                return "visual parts collection is null";

            var MainSymbol = Representation.MainSymbol;
            if (MainSymbol == null)
                return "main symbol is null";

            if (MainSymbol.OwnerRepresentation != Representation)
                return "main symbol points to a different owner representation";

            if (!Representation.VisualParts.Contains(MainSymbol))
                return "main symbol is not present in the representation visual parts";

            return null;
        }

        private static bool IsInvalidViewChild(View View, ViewChild Child, ISet<VisualRepresentation> InvalidRepresentations)
        {
            var Reason = GetInvalidViewChildReason(View, Child);
            if (String.IsNullOrEmpty(Reason))
                return false;

            var Element = Child == null ? null : Child.Key as VisualElement;
            if (Element != null &&
                Element.OwnerRepresentation != null &&
                InvalidRepresentations != null &&
                InvalidRepresentations.Contains(Element.OwnerRepresentation))
                return false;

            return true;
        }

        private static string GetInvalidViewChildReason(View View, ViewChild Child)
        {
            if (Child == null)
                return "view child entry is null";

            if (Child.Key == null)
                return "view child key is null";

            var Element = Child.Key as VisualElement;
            if (Element == null)
                return null;

            var Representation = Element.OwnerRepresentation;
            if (Representation == null)
                return "visual element has no owner representation";

            if (Representation.DisplayingView != null && View != null && Representation.DisplayingView != View)
                return "visual element owner representation belongs to a different view";

            if (Representation.VisualParts == null || !Representation.VisualParts.Contains(Element))
                return "visual element is not present in its owner representation visual parts";

            var Symbol = Element as VisualSymbol;
            if (Symbol != null)
            {
                if (Representation.MainSymbol == null)
                    return "visual symbol owner representation has no main symbol";

                if (Representation.MainSymbol != Symbol)
                    return "visual symbol is not the owner representation main symbol";
            }

            var Connector = Element as VisualConnector;
            if (Connector != null)
            {
                if (!(Representation is RelationshipVisualRepresentation))
                    return "visual connector owner representation is not a relationship visual representation";

                if (Representation.MainSymbol == null)
                    return "visual connector owner representation has no main symbol";

                if (Connector.OriginSymbol == null || Connector.TargetSymbol == null)
                    return "visual connector has a null endpoint symbol";

                if (Connector.RepresentedLink == null)
                    return "visual connector has no represented relationship link";
            }

            return null;
        }

        private static void RemoveVisualRepresentation(VisualRepresentation Representation)
        {
            RemoveVisualRepresentation(Representation, Representation == null ? null : Representation.RepresentedIdea);
        }

        private static void RemoveVisualRepresentation(VisualRepresentation Representation, Idea OwningIdea)
        {
            if (Representation == null)
                return;

            var View = Representation.DisplayingView;
            var Parts = Representation.VisualParts.ToList();

            try
            {
                Representation.Clear();
            }
            catch (Exception Problem)
            {
                Console.WriteLine("Warning: could not clear recursive visual representation '{0}'. Problem: {1}",
                                  Representation.ToStringAlways(), Problem.Message);
            }

            if (View != null && View.ViewChildren != null)
                foreach (var Part in Parts)
                    while (View.ViewChildren.Any(Child => Child != null && Child.Key == Part))
                    {
                        var Index = View.ViewChildren.IndexOfMatch(Child => Child != null && Child.Key == Part);
                        if (Index < 0)
                            break;
                        View.ViewChildren.RemoveAt(Index);
                    }

            if (Representation.RepresentedIdea != null && Representation.RepresentedIdea.VisualRepresentators.Contains(Representation))
                Representation.RepresentedIdea.VisualRepresentators.Remove(Representation);

            if (OwningIdea != null && OwningIdea != Representation.RepresentedIdea && OwningIdea.VisualRepresentators.Contains(Representation))
                OwningIdea.VisualRepresentators.Remove(Representation);
        }

        private static void RemoveViewChild(View View, ViewChild Child)
        {
            if (View == null || View.ViewChildren == null || Child == null)
                return;

            while (View.ViewChildren.Contains(Child))
                View.ViewChildren.Remove(Child);
        }

        private static List<Guid> GetRenderStack()
        {
            if (CompositeRenderStack == null)
                CompositeRenderStack = new List<Guid>();

            return CompositeRenderStack;
        }

        private static string DescribeIdea(Idea Idea)
        {
            if (Idea == null)
                return "<none>";

            return "name='" + Idea.Name.ToStringAlways() + "' techName='" + Idea.TechName.ToStringAlways() +
                   "' id=" + Idea.GlobalId.ToString("D");
        }

        private static string DescribeView(View View)
        {
            if (View == null)
                return "<none>";

            return "name='" + View.Name.ToStringAlways() + "' techName='" + View.TechName.ToStringAlways() +
                   "' id=" + View.GlobalId.ToString("D");
        }
    }
}
