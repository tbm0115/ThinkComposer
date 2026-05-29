// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Copyright (C) 2011-2015 Néstor Marcel Sánchez Ahumada.
// https://github.com/nmarcel/ThinkComposer
//
// This file is part of ThinkComposer, which is free software licensed under the GNU General Public License.
// It is provided without any warranty. You should find a copy of the license in the root directory of this software product.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using Instrumind.ThinkComposer.Model;
using Instrumind.ThinkComposer.Model.GraphModel;
using Instrumind.ThinkComposer.Model.VisualModel;

namespace Instrumind.ThinkComposer.Composer.Layout
{
    /// <summary>
    /// Read-only snapshot of the current diagram selection and visible graph.
    /// </summary>
    public class LayoutSelectionContext
    {
        private LayoutSelectionContext()
        {
            this.SelectedVisualSymbols = new List<VisualSymbol>();
            this.SelectedConceptSymbols = new List<VisualSymbol>();
            this.SelectedRelationshipRepresentations = new List<RelationshipVisualRepresentation>();
            this.SelectedRelationshipConnectors = new List<VisualConnector>();
            this.SelectedRouteableConnectors = new List<VisualConnector>();
            this.VisibleConceptSymbols = new List<VisualSymbol>();
            this.VisibleRelationshipRepresentations = new List<RelationshipVisualRepresentation>();
            this.VisibleRelationshipConnectors = new List<VisualConnector>();
            this.VisibleRelationships = new List<Relationship>();
            this.VisibleSymbolBounds = new Dictionary<VisualSymbol, Rect>();
        }

        public CompositionEngine Engine { get; private set; }

        public Composition Composition { get; private set; }

        public View ActiveView { get; private set; }

        public IList<VisualSymbol> SelectedVisualSymbols { get; private set; }

        public IList<VisualSymbol> SelectedConceptSymbols { get; private set; }

        public IList<RelationshipVisualRepresentation> SelectedRelationshipRepresentations { get; private set; }

        public IList<VisualConnector> SelectedRelationshipConnectors { get; private set; }

        public IList<VisualConnector> SelectedRouteableConnectors { get; private set; }

        public IList<VisualSymbol> VisibleConceptSymbols { get; private set; }

        public IList<RelationshipVisualRepresentation> VisibleRelationshipRepresentations { get; private set; }

        public IList<VisualConnector> VisibleRelationshipConnectors { get; private set; }

        public IList<Relationship> VisibleRelationships { get; private set; }

        public IDictionary<VisualSymbol, Rect> VisibleSymbolBounds { get; private set; }

        public Point CurrentViewportCenter { get; private set; }

        public static LayoutSelectionContext FromActiveView(CompositionEngine Engine)
        {
            if (Engine == null || Engine.CurrentView == null)
                return Empty(Engine);

            return FromSelection(Engine, Engine.CurrentView.SelectedObjects);
        }

        public static LayoutSelectionContext FromSelection(CompositionEngine Engine, IEnumerable<VisualObject> Selection)
        {
            return FromViewSelection(Engine, Engine == null ? null : Engine.CurrentView, Selection);
        }

        public static LayoutSelectionContext FromViewSelection(CompositionEngine Engine, View View, IEnumerable<VisualObject> Selection)
        {
            var Context = Empty(Engine);

            if (Engine == null || View == null)
                return Context;

            var SelectedObjects = (Selection ?? Enumerable.Empty<VisualObject>()).Where(Object => Object != null).ToList();
            var VisibleObjects = View.ViewChildren == null
                                 ? Enumerable.Empty<VisualObject>()
                                 : View.ViewChildren.Where(Child => Child != null && Child.Key is VisualObject)
                                                    .Select(Child => (VisualObject)Child.Key);

            Context.Engine = Engine;
            Context.Composition = Engine.TargetComposition;
            Context.ActiveView = View;

            Context.SelectedVisualSymbols = SelectedObjects.OfType<VisualSymbol>().ToList();
            Context.SelectedConceptSymbols = Context.SelectedVisualSymbols
                                                    .Where(Symbol => Symbol.OwnerRepresentation is ConceptVisualRepresentation)
                                                    .ToList();
            Context.SelectedRelationshipConnectors = SelectedObjects.OfType<VisualConnector>().ToList();
            Context.SelectedRelationshipRepresentations = SelectedObjects.OfType<VisualElement>()
                                                    .Select(Element => Element.OwnerRepresentation)
                                                    .OfType<RelationshipVisualRepresentation>()
                                                    .Concat(Context.SelectedRelationshipConnectors
                                                        .Select(Connector => Connector.OwnerRepresentation)
                                                        .OfType<RelationshipVisualRepresentation>())
                                                    .Distinct()
                                                    .ToList();
            Context.SelectedRouteableConnectors = Context.SelectedRelationshipConnectors
                                                    .Concat(Context.SelectedRelationshipRepresentations
                                                        .SelectMany(Representation => Representation.VisualConnectors))
                                                    .Where(Connector => Connector != null)
                                                    .Distinct()
                                                    .ToList();

            Context.VisibleConceptSymbols = VisibleObjects.OfType<VisualSymbol>()
                                                    .Where(Symbol => Symbol.OwnerRepresentation is ConceptVisualRepresentation)
                                                    .Distinct()
                                                    .ToList();
            Context.VisibleRelationshipRepresentations = VisibleObjects.OfType<VisualElement>()
                                                    .Select(Element => Element.OwnerRepresentation)
                                                    .OfType<RelationshipVisualRepresentation>()
                                                    .Distinct()
                                                    .ToList();
            Context.VisibleRelationshipConnectors = VisibleObjects.OfType<VisualConnector>()
                                                    .Concat(Context.VisibleRelationshipRepresentations
                                                        .SelectMany(Representation => Representation.VisualConnectors))
                                                    .Where(Connector => Connector != null)
                                                    .Distinct()
                                                    .ToList();
            Context.VisibleRelationships = Context.VisibleRelationshipRepresentations
                                                    .Select(Representation => Representation.RepresentedRelationship)
                                                    .Where(Relationship => Relationship != null)
                                                    .Distinct()
                                                    .ToList();
            Context.VisibleSymbolBounds = Context.VisibleConceptSymbols
                                                    .ToDictionary(Symbol => Symbol, Symbol => Symbol.TotalArea);
            Context.CurrentViewportCenter = GetViewportCenter(View);

            return Context;
        }

        private static LayoutSelectionContext Empty(CompositionEngine Engine)
        {
            var Context = new LayoutSelectionContext();
            Context.Engine = Engine;
            Context.Composition = Engine == null ? null : Engine.TargetComposition;
            Context.ActiveView = Engine == null ? null : Engine.CurrentView;
            Context.CurrentViewportCenter = Context.ActiveView == null ? new Point(0, 0) : GetViewportCenter(Context.ActiveView);
            return Context;
        }

        private static Point GetViewportCenter(View View)
        {
            if (View == null)
                return new Point(0, 0);

            if (View.HostingScrollViewer != null)
                return View.CurrentPresentationCenter;

            return View.ViewCenter;
        }
    }
}
