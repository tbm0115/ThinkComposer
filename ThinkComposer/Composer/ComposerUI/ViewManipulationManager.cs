// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Copyright (C) 2011-2015 Néstor Marcel Sánchez Ahumada.
// https://github.com/nmarcel/ThinkComposer
//
// This file is part of ThinkComposer, which is free software licensed under the GNU General Public License.
// It is provided without any warranty. You should find a copy of the license in the root directory of this software product.
// -------------------------------------------------------------------------------------------
//
// Project: Instrumind ThinkComposer v1.0
// File   : ResizingAssistant.cs
// Object : Instrumind.Common.Visualization.ResizingAssistant (Class)
//
// Date       Author             Changes
// ---------- ------------------ -------------------------------------------------------------
// 2009.10.05 Néstor Sánchez A.  Creation
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

using Instrumind.Common;
using Instrumind.Common.EntityBase;
using Instrumind.Common.Visualization;

using Instrumind.ThinkComposer.ApplicationProduct;
using Instrumind.ThinkComposer.Composer.Layout;
using Instrumind.ThinkComposer.Definitor;
using Instrumind.ThinkComposer.MetaModel;
using Instrumind.ThinkComposer.MetaModel.GraphMetaModel;
using Instrumind.ThinkComposer.MetaModel.VisualMetaModel;
using Instrumind.ThinkComposer.Model.GraphModel;
using Instrumind.ThinkComposer.Model.InformationModel;
using Instrumind.ThinkComposer.Model.VisualModel;

/// Provides the user-interface components for the Composition Composer.
namespace Instrumind.ThinkComposer.Composer.ComposerUI
{
    /// <summary>
    /// Helper for select, move and resize visual objects with mouse actions.
    /// </summary>
    public class ViewManipulationManager
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public ViewManipulationManager(View OwnerView)
        {
            this.OwnerView = OwnerView;
        }

        public System.Windows.Documents.AdornerLayer ExposedAdornerLayer
        {
            get
            {
                return this.OwnerView.PresenterLayer;
            }
        }

        public View OwnerView { get; protected set; }
        internal bool IsMultiselecting = false;
        internal bool IsManipulating = false;

        public ViewManipulationAdorner WorkingAdorner { get; internal set; }

        public VisualObject CurrentTargetedObject { get; protected set; }

        // -----------------------------------------------------------------------------------------------------------------------
        public bool PointObject(VisualObject TargetObject, bool ForcePointing = false)
        {
            if (TargetObject is VisualInert || (!ForcePointing && TargetObject == this.CurrentTargetedObject))
                return false;

            /*T if (TargetObject != this.CurrentTargetedObject)
                Console.WriteLine(":: Pointing: Target: {0}, Previous: {1}, Current: {2}", TargetObject, this.PreviousManipulatedObject, this.CurrentTargetedObject); */

            UnpointObject();

            this.CurrentTargetedObject = TargetObject;

            if (TargetObject== null)
                return false;

            this.WorkingAdorner = this.RetrieveAdorner(TargetObject);
            if (this.WorkingAdorner == null)
                return false;

            this.WorkingAdorner.Visualize();

            return true;
        }

        public void UnpointObject(VisualObject TargetObject = null)
        {
            if (TargetObject == null)
                TargetObject = this.CurrentTargetedObject;

            if (TargetObject == null)
                return;

            //- Console.WriteLine(":: UnPointing:" + TargetSymbol.ToString()); 

            /* if (this.TargetAdorner != null)
                this.TargetAdorner.CurrentPosition = new Point(-1000, -1000); */

            if (this.WorkingAdorner != null)
                this.WorkingAdorner.Visualize(false);

            ProductDirector.ShowPointingTo(this.OwnerView);
            ProductDirector.ShowAssistance();
            this.RemoveAdorners();
        }

        public void Continue()
        {
            if (this.WorkingAdorner == null)
                return;

            this.WorkingAdorner.Continue();
        }

        public void Finish()
        {
            if (this.WorkingAdorner == null)
                return;

            UnpointObject();
            this.WorkingAdorner.Stop();
        }

        public void Abort()
        {
            this.IsManipulating = false;
            UnpointObject();
        }

        protected readonly Capsule<VisualObject, ViewManipulationAdorner> CurrentAdorner = new Capsule<VisualObject,ViewManipulationAdorner>();
        public ViewManipulationAdorner RetrieveAdorner(VisualObject TargetObject)
        {
            this.RemoveAdorners();

            //-if (this.CurrentAdorner.Value0 != TargetObject)
            //-{
                this.CurrentAdorner.Value0 = TargetObject;

                if (TargetObject is VisualSymbol)
                    this.CurrentAdorner.Value1 = new ViewSymbolManipulationAdorner(this, TargetObject as VisualSymbol, this.ExposedAdornerLayer, Manipulate);
                else
                    if (TargetObject is VisualConnector)
                        this.CurrentAdorner.Value1 = new ViewConnectorManipulationAdorner(this, TargetObject as VisualConnector, this.ExposedAdornerLayer, Manipulate);
                    else
                        this.CurrentAdorner.Value1 = new ViewComplementManipulationAdorner(this, TargetObject as VisualComplement, this.ExposedAdornerLayer, Manipulate);

                this.ExposedAdornerLayer.Add(this.CurrentAdorner.Value1);

                //T Console.WriteLine("RetrievingAdorner(Create) for: " + this.CurrentAdorner.Value0.ToString());
            //-}
            /*T else
                Console.WriteLine("RetrievingAdorner(Read) for: " + this.CurrentAdorner.Value0.ToString()); */

            return this.CurrentAdorner.Value1;
        }

        public void RemoveAdorners()
        {
            var Adorns = this.ExposedAdornerLayer.GetAdorners(this.OwnerView.Presenter);
            if (Adorns == null)
                return;

            var ViewAdorns = Adorns.CastAs<ViewManipulationAdorner,System.Windows.Documents.Adorner>();

            foreach (var Adorn in ViewAdorns)
            {
                Adorn.ClearAllIndicators();
                this.ExposedAdornerLayer.Remove(Adorn);
            }
        } 

        // -----------------------------------------------------------------------------------------------------------------------
        private VisualObject PreviousManipulatedObject = null;

        public void Manipulate(ViewManipulationAdorner Modifier, bool IsMouseLeftButtonUp, bool IsMouseLeftButtonClicked, bool IsMouseRightButtonClicked,
                               bool IsKeyControlPressed, bool IsKeyShiftPressed, bool IsKeyAltLeftPressed, bool IsKeyAltRightPressed, bool IsMouseLeftDoubleClicked)
        {
            /*? var CurrentWindow = Display.GetCurrentWindow();
            var PreviousCursor = CurrentWindow.Cursor;
            CurrentWindow.ForceCursor = true;
            CurrentWindow.Cursor = Cursors.Wait; */

            //T Console.WriteLine("Manipulating {0}.", Modifier.ManipulatedObject.ToStringAlways());

            Modifier.IsUsingGridSettings = true;    // Start considering grid-settings

            var SymbolModifier = Modifier as ViewSymbolManipulationAdorner;
            var ConnectorModifier = Modifier as ViewConnectorManipulationAdorner;
            var ComplementModifier = Modifier as ViewComplementManipulationAdorner;
            bool JustSelected = false;

            if (Modifier.ManipulatedObject != PreviousManipulatedObject
                || !Modifier.ManipulatedObject.IsSelected)
            {
                ApplySelection(Modifier.ManipulatedObject, false, IsKeyControlPressed);
                PreviousManipulatedObject = Modifier.ManipulatedObject;
                JustSelected = true;
            }

            if (SymbolModifier != null)
            {
                if (IsMouseLeftButtonClicked)
                {
                    var ManipulatedSymbol = SymbolModifier.ManipulatedObject as VisualSymbol;

                    switch (SymbolModifier.IntendedAction)
                    {
                        case ESymbolManipulationAction.Move:    // This really means a 'touch'
                            if (!JustSelected)
                                ApplySelection(Modifier.ManipulatedObject, true, IsKeyControlPressed);
                            break;

                        case ESymbolManipulationAction.MarkerEdit:
                            ManipulatedSymbol.GetDisplayingView().EditPropertiesOfVisualRepresentation(ManipulatedSymbol.OwnerRepresentation, Display.TABKEY_MARKINGS,
                                                                                 SymbolModifier.LastPointedMarkerAssignment);
                            break;

                        case ESymbolManipulationAction.IndividualDetailAccess:
                            IndividualDetailAccess(ManipulatedSymbol.OwnerRepresentation.RepresentedIdea,
                                                   SymbolModifier.LastPointedDetailDesignator, ManipulatedSymbol);
                            break;

                        case ESymbolManipulationAction.IndividualDetailChange:
                            IndividualDetailChange(ManipulatedSymbol, SymbolModifier.LastPointedDetailDesignator);
                            break;

                        case ESymbolManipulationAction.IndividualDetailDesignation:
                            IndividualDetailDesignation(ManipulatedSymbol, SymbolModifier.LastPointedDetailDesignator);
                            break;

                        case ESymbolManipulationAction.SwitchIndividualDetail:
                            SwitchIndividualDetail(ManipulatedSymbol, SymbolModifier.LastPointedDetailDesignator);
                            break;

                        case ESymbolManipulationAction.ActionSwitchDetails:
                            SwitchDetails(ManipulatedSymbol);
                            break;

                        case ESymbolManipulationAction.ActionSwitchRelated:
                            SwitchRelated(ManipulatedSymbol, !IsKeyAltLeftPressed && !IsKeyAltRightPressed);
                            break;

                        case ESymbolManipulationAction.ActionShowCompositeAsView:
                            this.OwnerView.Engine.ShowCompositeAsView(ManipulatedSymbol);
                            break;

                        case ESymbolManipulationAction.ActionEditProperties:
                            if (ManipulatedSymbol != null)
                                this.OwnerView.EditPropertiesOfVisualRepresentation(ManipulatedSymbol.OwnerRepresentation);
                            break;

                        case ESymbolManipulationAction.ActionShowCompositeAsDetail:
                            ShowCompositeAsDetail(ManipulatedSymbol);
                            break;

                        case ESymbolManipulationAction.ActionAddDetail:
                            if (ManipulatedSymbol != null)
                                this.OwnerView.AppendDetailToVisualRepresentation(ManipulatedSymbol.OwnerRepresentation);
                            break;

                        case ESymbolManipulationAction.Resize:
                            if (IsMouseLeftDoubleClicked
                                && ManipulatedSymbol != null
                                && ManipulatedSymbol.OwnerRepresentation is ConceptVisualRepresentation
                                && (SymbolModifier.ResizingDirection == EManipulationDirection.Left
                                    || SymbolModifier.ResizingDirection == EManipulationDirection.Right))
                                CompositionAppearanceCommands.FitConceptWidthToText(this.OwnerView.Engine, ManipulatedSymbol,
                                                                                     "resize-handle double-click");
                            break;

                        case ESymbolManipulationAction.EditInPlace:
                            // Do not Edit In-Place if multiselecting...
                            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl) ||
                                Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                                break;

                            this.OwnerView.EditInPlace(ManipulatedSymbol);
                            break;

                        case ESymbolManipulationAction.GoToShortcutTarget:
                            this.OwnerView.Engine
                                .GoToShortcutTarget(ManipulatedSymbol);
                            break;
                    }
                }
                else
                    switch (SymbolModifier.IntendedAction)
                    {
                        case ESymbolManipulationAction.Move:
                            Move(SymbolModifier, IsMouseLeftButtonUp, IsKeyAltRightPressed, IsKeyControlPressed, IsKeyShiftPressed);
                            break;

                        case ESymbolManipulationAction.Resize:
                            Resize(SymbolModifier, IsMouseLeftButtonUp);
                            break;
                    }
            }
            else
                if (ConnectorModifier != null)
                {
                    var RelDef = ConnectorModifier.ManipulatedConnector.RepresentedLink.OwnerRelationship.RelationshipDefinitor.Value;
                    var IsSimpleDirectRelationship = (RelDef.IsSimple && RelDef.HideCentralSymbolWhenSimple);
                    var CompoEnginge = this.OwnerView.Engine;

                    switch (ConnectorModifier.IntendedAction)
                    {
                        case EConnectorManipulationAction.EditInPlace:
                            // CANCELLED... this.OwnerView.EditInPlace(ConnectorModifier.TargetObject as VisualConnector);
                            break;

                        case EConnectorManipulationAction.EditProperties:
                            if (IsMouseLeftButtonClicked)
                            {
                                var TargetedSymbol = ConnectorModifier.ManipulatedObject as VisualSymbol;
                                if (TargetedSymbol != null)
                                    this.OwnerView.EditPropertiesOfVisualRepresentation(TargetedSymbol.OwnerRepresentation, CompositionEngine.TABKEY_REL_LINKS);
                            }
                            break;

                        case EConnectorManipulationAction.Displace:
                            if (IsMouseLeftDoubleClicked)
                            {
                                var RouteConnector = ConnectorModifier.ManipulatedRouteConnector
                                                     ?? ConnectorModifier.ManipulatedConnector;
                                if (ConnectorModifier.ManipulatedRoutePointIndex >= 0)
                                    RouteConnector.DoRemoveRoutePoint(ConnectorModifier.ManipulatedRoutePointIndex);
                                else
                                    RouteConnector.DoEditDescriptor();
                            }
                            else
                                Displace(ConnectorModifier, IsMouseLeftButtonUp);
                            break;

                        case EConnectorManipulationAction.ReLink:
                            if (IsMouseLeftDoubleClicked)
                                ConnectorModifier.ManipulatedConnector.DoEditDescriptor();
                            else
                                Relink(ConnectorModifier, IsMouseLeftButtonUp);
                            break;

                        case EConnectorManipulationAction.CycleThroughVariants:
                            ConnectorModifier.ManipulatedConnector.DoCycleThroughVariants();
                            break;

                        case EConnectorManipulationAction.StraightenLine:
                            if (IsSimpleDirectRelationship)
                                ConnectorModifier.ManipulatedConnector.OwnerRelationshipRepresentation.DoStraighten();
                            else
                                ConnectorModifier.ManipulatedConnector.DoStraighten();

                            ConnectorModifier.Visualize(false);
                            this.RemoveAdorners();
                            this.OwnerView.UnselectAllObjects();

                            break;

                        case EConnectorManipulationAction.Remove:
                            if (IsSimpleDirectRelationship)
                                CompoEnginge.DeleteObjects(ConnectorModifier.ManipulatedConnector.IntoEnumerable());
                            else
                                CompoEnginge.DeleteRelationshipLink(ConnectorModifier.ManipulatedConnector.RepresentedLink);

                            this.RemoveAdorners();
                            break;
                    }
                }
                else
                    if (ComplementModifier != null)
                    {
                        if (IsMouseLeftButtonClicked)
                        {
                            var ManipulatedComplement = ComplementModifier.ManipulatedObject as VisualComplement;

                            switch (ComplementModifier.IntendedAction)
                            {
                                case EComplementManipulationAction.Move:    // This really means a 'touch'
                                    if (!JustSelected)
                                        ApplySelection(Modifier.ManipulatedObject, true, IsKeyControlPressed);
                                    break;

                                case EComplementManipulationAction.Edit:
                                    if (ManipulatedComplement != null)
                                        VisualComplement.Edit(ManipulatedComplement);
                                    break;
                            }
                        }
                        else
                            switch (ComplementModifier.IntendedAction)
                            {
                                case EComplementManipulationAction.Move:
                                    Move(ComplementModifier, IsMouseLeftButtonUp, true, IsKeyControlPressed, IsKeyShiftPressed);
                                    break;

                                case EComplementManipulationAction.Resize:
                                    Resize(ComplementModifier, IsMouseLeftButtonUp);
                                    break;
                            }
                    }

            //? CurrentWindow.ForceCursor = false;
            //? CurrentWindow.Cursor = PreviousCursor;

            Modifier.IsUsingGridSettings = false;   // End considering grid-settings
        }

        // -----------------------------------------------------------------------------------------------------------------------
        /// <summary>
        /// Selects the supplied object, also considering switching selection, plus indication of continue-selection (typically by holding "Ctrl").
        /// </summary>
        public void ApplySelection(VisualObject TargetObject, bool SwitchSelection = false,
                                   bool ContinueSelection = false, bool? EnforcedSelection = null)
        {
            var SelectionFlag = (!SwitchSelection || !TargetObject.IsSelected);

            //T Console.WriteLine("Select Start!");
            this.OwnerView.EditEngine.StartCommandVariation("Apply Selection", false);

            if (EnforcedSelection != null)
                SelectionFlag = EnforcedSelection.Value;
            else
                if (TargetObject.IsSelected == SelectionFlag)
                    if (SelectionFlag && TargetObject is VisualConnector)
                    {
                        ((VisualConnector)TargetObject).OwnerRepresentation.IsSelected = false;
                        ((VisualConnector)TargetObject).OwnerRepresentation.Render();
                    }
                    else
                    {
                        this.OwnerView.EditEngine.DiscardCommandVariation();
                        return;
                    }

            if (!ContinueSelection)
                this.OwnerView.UnselectAllObjects(TargetObject);

            if (SelectionFlag)
                this.OwnerView.SelectObject(TargetObject);
            else
                this.OwnerView.UnselectObject(TargetObject);

            TargetObject.RenderRepresentatedObject();

            this.OwnerView.EditEngine.CompleteCommandVariation(true);

            //T Console.WriteLine("Select end.");
        }

        // -----------------------------------------------------------------------------------------------------------------------
        private sealed class RouteGeometryChangeBatch
        {
            public readonly HashSet<VisualSymbol> MovingSymbols = new HashSet<VisualSymbol>();
            public readonly HashSet<VisualConnector> Connectors = new HashSet<VisualConnector>();
            public readonly Dictionary<RelationshipVisualRepresentation, Point> RelationshipCenters =
                new Dictionary<RelationshipVisualRepresentation, Point>();
        }

        private RouteGeometryChangeBatch CaptureRouteGeometryChanges(IEnumerable<VisualSymbol> Symbols)
        {
            var Result = new RouteGeometryChangeBatch();
            foreach (var Symbol in (Symbols ?? Enumerable.Empty<VisualSymbol>()).Where(Symbol => Symbol != null).Distinct())
                Result.MovingSymbols.Add(Symbol);

            var IncidentConnectors = Result.MovingSymbols
                                           .SelectMany(Symbol => Symbol.TargetConnections.Concat(Symbol.OriginConnections))
                                           .Where(Connector => Connector != null)
                                           .Distinct()
                                           .ToList();
            foreach (var Connector in IncidentConnectors)
            {
                // A connector whose complete endpoint pair moves by the same delta retains its
                // hand-authored route. All other incident connectors are dirty and must route.
                if (!(Result.MovingSymbols.Contains(Connector.OriginSymbol)
                      && Result.MovingSymbols.Contains(Connector.TargetSymbol)))
                    Result.Connectors.Add(Connector);

                var Representation = Connector.OwnerRelationshipRepresentation;
                if (Representation != null && Representation.MainSymbol != null
                    && !Result.RelationshipCenters.ContainsKey(Representation))
                    Result.RelationshipCenters.Add(Representation, Representation.MainSymbol.BaseCenter);
            }

            return Result;
        }

        private void RouteGeometryChanges(RouteGeometryChangeBatch Batch,
                                          RelationshipRouteIntent Intent, string DirtyReason)
        {
            if (Batch == null)
                return;

            // A simple hidden hub can be auto-positioned as a consequence of moving/resizing one
            // endpoint. In that case every non-co-moving leg incident to the hub is dirty.
            foreach (var Entry in Batch.RelationshipCenters)
                if (Entry.Key != null && Entry.Key.MainSymbol != null
                    && Entry.Key.MainSymbol.BaseCenter != Entry.Value)
                    foreach (var Connector in Entry.Key.VisualConnectors.Where(Connector => Connector != null))
                        if (!(Batch.MovingSymbols.Contains(Connector.OriginSymbol)
                              && Batch.MovingSymbols.Contains(Connector.TargetSymbol)))
                            Batch.Connectors.Add(Connector);

            if (Batch.Connectors.Count == 0)
                return;

            var Context = LayoutSelectionContext.FromViewSelection(this.OwnerView.Engine, this.OwnerView,
                              Batch.Connectors.Cast<VisualObject>());
            var Options = new LinkObstacleRoutingOptions
            {
                Profile = RelationshipRoutingProfile.Manual,
                RouteIntent = Intent,
                DirtyReason = DirtyReason,
                PreserveExistingValidRoutes = false,
                RouteSelectedConnectorsOnly = true,
                IncludeRelationshipCentralSymbolsAsObstacles = true,
                // A direct manipulation of a hub is an explicit user position. Automatic hub
                // placement is not allowed to undo that gesture while repairing its legs.
                CorrectRelationshipCentersBeforeRouting = false
            };
            RelationshipRoutingCoordinator.Route(Context, Options);
        }

        private RouteGeometryChangeBatch ApplyMove(VisualObject ManipulatedObject, bool LockPosition,
                                                    bool IncludeRelatedTargets, bool IncludeRelatedOrigins,
                                                    double OffsetX, double OffsetY)
        {
            // See also ViewSymbolManipulationAdorner.Visualize()
            var AllAffectedObjects = this.OwnerView.GetCurrentManipulableObjects(IncludeRelatedOrigins, IncludeRelatedTargets);
            var RegionContainedObjects = AllAffectedObjects.Where(obj => obj.Item2).Select(obj => obj.Item1).Distinct().ToList();

            var AffectedObjects = AllAffectedObjects.Select(obj => obj.Item1).Distinct().ToList();
            var AffectedSymbols = AffectedObjects.OfType<VisualSymbol>().Distinct().ToList();

            // A hidden simple Relationship's junction cannot normally be selected. When every
            // visible endpoint of that logical relationship is in the move, carry its hidden hub
            // by the same delta so the whole route qualifies for intact translation.
            var InitiallyMovingSymbols = new HashSet<VisualSymbol>(AffectedSymbols);
            var HiddenRelationshipHubs = AffectedSymbols
                .SelectMany(Symbol => Symbol.TargetConnections.Concat(Symbol.OriginConnections))
                .Where(Connector => Connector != null && Connector.OwnerRelationshipRepresentation != null)
                .Select(Connector => Connector.OwnerRelationshipRepresentation)
                .Distinct()
                .Where(Representation => Representation.MainSymbol != null
                                         && Representation.MainSymbol.IsHidden
                                         && Representation.RepresentedRelationship.RelationshipDefinitor.Value.IsSimple
                                         && Representation.RepresentedRelationship.RelationshipDefinitor.Value.HideCentralSymbolWhenSimple)
                .Where(Representation => Representation.VisualConnectors
                    .Where(Connector => Connector != null)
                    .All(Connector => InitiallyMovingSymbols.Contains(
                         Connector.OriginSymbol == Representation.MainSymbol
                         ? Connector.TargetSymbol : Connector.OriginSymbol)))
                .Select(Representation => Representation.MainSymbol)
                .Distinct()
                .ToList();

            // Auto/self-reference hubs are also a logical part of their endpoint symbol. The
            // legacy VisualSymbol parallel-move path translates them independently; this batch
            // owns the movement instead so the hub and every connector route move exactly once.
            var AutoReferenceHubs = InitiallyMovingSymbols
                .SelectMany(Symbol => Symbol.TargetConnections.Concat(Symbol.OriginConnections))
                .Where(Connector => Connector != null
                                    && Connector.OwnerRelationshipRepresentation != null
                                    && Connector.OwnerRelationshipRepresentation.RepresentedRelationship.IsAutoReference)
                .Select(Connector => Connector.OwnerRelationshipRepresentation)
                .Distinct()
                .Where(Representation => Representation.MainSymbol != null
                                         && Representation.VisualConnectors.Any(Connector =>
                                                (Connector.OriginSymbol != Representation.MainSymbol
                                                 && InitiallyMovingSymbols.Contains(Connector.OriginSymbol))
                                                || (Connector.TargetSymbol != Representation.MainSymbol
                                                    && InitiallyMovingSymbols.Contains(Connector.TargetSymbol))))
                .Select(Representation => Representation.MainSymbol)
                .Distinct()
                .ToList();

            foreach (var CoMovingHub in HiddenRelationshipHubs.Concat(AutoReferenceHubs).Distinct())
            {
                if (!AffectedSymbols.Contains(CoMovingHub))
                    AffectedSymbols.Add(CoMovingHub);
                if (!AffectedObjects.Contains(CoMovingHub))
                    AffectedObjects.Add(CoMovingHub);
            }

            var RouteChanges = CaptureRouteGeometryChanges(AffectedSymbols);

            // Translate every fully connected route exactly once, regardless of whether the
            // Relationship representation itself is selected. The endpoint MoveTo calls below
            // update anchors, while this preserves all interior geometry and cached edge anchors.
            var FullyMovedConnectors = AffectedSymbols
                                      .SelectMany(Symbol => Symbol.TargetConnections.Concat(Symbol.OriginConnections))
                                      .Where(Connector => Connector != null
                                                          && RouteChanges.MovingSymbols.Contains(Connector.OriginSymbol)
                                                          && RouteChanges.MovingSymbols.Contains(Connector.TargetSymbol))
                                      .Distinct()
                                      .ToList();
            foreach (var Connector in FullyMovedConnectors)
            {
                Connector.TranslateRoutePoints(OffsetX, OffsetY);
                if (IsFinitePoint(Connector.OriginEdgePosition))
                    Connector.OriginEdgePosition = new Point(Connector.OriginEdgePosition.X + OffsetX,
                                                             Connector.OriginEdgePosition.Y + OffsetY);
                if (IsFinitePoint(Connector.TargetEdgePosition))
                    Connector.TargetEdgePosition = new Point(Connector.TargetEdgePosition.X + OffsetX,
                                                             Connector.TargetEdgePosition.Y + OffsetY);
            }

            foreach (var AffectedObject in AffectedObjects)
            {
                var NewCenterX = OffsetX + AffectedObject.BaseLeft + (AffectedObject.BaseWidth / 2.0);
                var NewCenterY = OffsetY + AffectedObject.BaseTop + (AffectedObject.BaseHeight / 2.0);

                var AffectedSymbol = AffectedObject as VisualSymbol;

                if (AffectedSymbol != null)
                    AffectedSymbol.MoveTo(NewCenterX, NewCenterY,
                                          (LockPosition && AffectedObject == ManipulatedObject),
                                          // ApplyMove explicitly includes and translates logical
                                          // auto-reference hubs, so suppress the legacy nested
                                          // DoParallelMove path which would move them twice.
                                          null,
                                          AffectedSymbols,
                                          RegionContainedObjects);
                else
                    AffectedObject.MoveTo(NewCenterX, NewCenterY, (LockPosition && AffectedObject == ManipulatedObject));

                /*T else
                {
                    Console.WriteLine("Manipulation target was not included in Selection.");
                    // throw new InternalAnomaly("Manipulation target was not included in Selection.");
                }*/
            }

            return RouteChanges;
        }

        private static bool IsFinitePoint(Point Point)
        {
            return !(double.IsNaN(Point.X) || double.IsInfinity(Point.X)
                     || double.IsNaN(Point.Y) || double.IsInfinity(Point.Y));
        }

        public void Move(ViewSymbolManipulationAdorner Modifier, bool IsDefinitive, bool LockPosition,
                         bool IncludeRelatedTargets, bool IncludeRelatedOrigins)
        {
            this.IsManipulating = true;

            double DeltaX = Modifier.CurrentPosition.X - Modifier.PreviousPosition.X;
            double DeltaY = Modifier.CurrentPosition.Y - Modifier.PreviousPosition.Y;

            DeltaX = DeltaX.ApplyScalePercent(this.OwnerView.PageDisplayScale, true);
            DeltaY = DeltaY.ApplyScalePercent(this.OwnerView.PageDisplayScale, true);

            double NewX = Modifier.ManipulatingHeadingRectangle.X + DeltaX;
            double NewY = Modifier.ManipulatingHeadingRectangle.Y + DeltaY;

            Modifier.ManipulatingHeadingRectangle.X = NewX;
            Modifier.ManipulatingDetailsRectangle.X = NewX;

            Modifier.ManipulatingHeadingRectangle.Y = NewY;
            Modifier.ManipulatingDetailsRectangle.Y = Modifier.ManipulatingDetailsRectangle.Y + DeltaY; ;

            // Note: Label is just moved, never resized
            Modifier.ManipulatingHeadingLabel.X = Modifier.ManipulatingHeadingLabel.X + DeltaX;
            Modifier.ManipulatingHeadingLabel.Y = Modifier.ManipulatingHeadingLabel.Y + DeltaY;

            //T Console.WriteLine("MOVING... CurrentCenter={0}, GivePosition={1}", TargetSymbol.SymbolCenter, CurrentPosition);
            if (IsDefinitive)
            {
                //T Console.WriteLine("Move Start!");
                Cursor PreviousTargetAdornerCursor = null;

                if (this.WorkingAdorner != null
                    && (this.OwnerView.SelectedObjects.Count > 1
                        || Modifier.ManipulatedSymbol.TargetConnections.Count > 0
                        || Modifier.ManipulatedSymbol.OriginConnections.Count > 0))
                {
                    PreviousTargetAdornerCursor = this.WorkingAdorner.Cursor;
                    this.WorkingAdorner.Cursor = Cursors.Wait;
                }

                this.OwnerView.EditEngine.StartCommandVariation("Move Symbol");

                var Match = this.OwnerView.SelectedObjects
                                .Where(selobj => selobj == Modifier.ManipulatedSymbol).FirstOrDefault();
                if (Match == null)
                    ApplySelection(Modifier.ManipulatedSymbol, true, false, true);

                var OffsetX = NewX - Modifier.ManipulatedSymbol.BaseLeft;
                var OffsetY = NewY - Modifier.ManipulatedSymbol.BaseTop;

                var RouteChanges = ApplyMove(Modifier.ManipulatedSymbol, LockPosition, IncludeRelatedTargets,
                                             IncludeRelatedOrigins, OffsetX, OffsetY);
                RouteGeometryChanges(RouteChanges,
                                     Modifier.ManipulatedSymbol.OwnerRepresentation is RelationshipVisualRepresentation
                                     ? RelationshipRouteIntent.RelationshipCenterMoved
                                     : RelationshipRouteIntent.EndpointMoved,
                                     "direct manipulation moved selected symbol geometry");

                this.OwnerView.UpdateVersion();

                this.OwnerView.EditEngine.CompleteCommandVariation();

                this.IsManipulating = false;

                if (PreviousTargetAdornerCursor != null)
                    this.WorkingAdorner.Cursor = PreviousTargetAdornerCursor;

                //T Console.WriteLine("Move end.");
            }
        }

        public void Move(ViewComplementManipulationAdorner Modifier, bool IsDefinitive, bool LockPosition,
                         bool IncludeRelatedTargets, bool IncludeRelatedOrigins)
        {
            this.IsManipulating = true;

            double DeltaX = Modifier.CurrentPosition.X - Modifier.PreviousPosition.X;
            double DeltaY = Modifier.CurrentPosition.Y - Modifier.PreviousPosition.Y;

            DeltaX = DeltaX.ApplyScalePercent(this.OwnerView.PageDisplayScale, true);
            DeltaY = DeltaY.ApplyScalePercent(this.OwnerView.PageDisplayScale, true);

            double NewX = Modifier.ManipulatingHeadingRectangle.X + DeltaX;
            double NewY = Modifier.ManipulatingHeadingRectangle.Y + DeltaY;

            Modifier.ManipulatingHeadingRectangle.X = NewX;

            Modifier.ManipulatingHeadingRectangle.Y = NewY;

            //T Console.WriteLine("MOVING... CurrentCenter={0}, GivePosition={1}", TargetSymbol.SymbolCenter, CurrentPosition);
            if (IsDefinitive)
            {
                //T Console.WriteLine("Move Start!");
                Cursor PreviousTargetAdornerCursor = null;

                if (this.WorkingAdorner != null
                    && (this.OwnerView.SelectedObjects.Count > 1))
                {
                    PreviousTargetAdornerCursor = this.WorkingAdorner.Cursor;
                    this.WorkingAdorner.Cursor = Cursors.Wait;
                }

                this.OwnerView.EditEngine.StartCommandVariation("Move Complement");

                var Match = this.OwnerView.SelectedObjects
                                .Where(selobj => selobj == Modifier.ManipulatedComplement).FirstOrDefault();
                if (Match == null)
                    ApplySelection(Modifier.ManipulatedComplement, true);

                var OffsetX = NewX - Modifier.ManipulatedComplement.BaseLeft;
                var OffsetY = NewY - Modifier.ManipulatedComplement.BaseTop;

                var RouteChanges = ApplyMove(Modifier.ManipulatedComplement, LockPosition, IncludeRelatedTargets,
                                             IncludeRelatedOrigins, OffsetX, OffsetY);
                RouteGeometryChanges(RouteChanges, RelationshipRouteIntent.EndpointMoved,
                                     "direct manipulation moved related symbol geometry");

                this.OwnerView.UpdateVersion();

                this.OwnerView.EditEngine.CompleteCommandVariation();

                this.IsManipulating = false;

                if (PreviousTargetAdornerCursor != null)
                    this.WorkingAdorner.Cursor = PreviousTargetAdornerCursor;

                //T Console.WriteLine("Move end.");
            }
        }

        public void Resize(ViewSymbolManipulationAdorner Modifier, bool IsDefinitive)
        {
            // Console.WriteLine("RESIZING... CurrentCenter={0}, GivePosition={1}, Direction={2}", TargetSymbol.SymbolCenter, CurrentPosition, ResizingDirection);
            double DeltaX = 0, DeltaY = 0;
            bool GoingRight = false, GoingBottom = false;
            bool FixedX = false, FixedY = false;

            this.IsManipulating = true;

            FixedX = (Modifier.ResizingDirection == EManipulationDirection.Top || Modifier.ResizingDirection == EManipulationDirection.Bottom
                      || VisualSymbolFormat.GetHasFixedWidth(Modifier.ManipulatedSymbol));
            FixedY = (Modifier.ResizingDirection == EManipulationDirection.Left || Modifier.ResizingDirection == EManipulationDirection.Right
                      || VisualSymbolFormat.GetHasFixedHeight(Modifier.ManipulatedSymbol));

            if (!FixedX)
            {
                DeltaX = Modifier.CurrentPosition.X - Modifier.PreviousPosition.X;
                GoingRight = (Modifier.ResizingDirection == EManipulationDirection.Right || 
                              Modifier.ResizingDirection == EManipulationDirection.TopRight || 
                              Modifier.ResizingDirection == EManipulationDirection.BottomRight);
            }

            if (!FixedY)
            {
                DeltaY = Modifier.CurrentPosition.Y - Modifier.PreviousPosition.Y;
                GoingBottom = (Modifier.ResizingDirection == EManipulationDirection.Bottom || 
                               Modifier.ResizingDirection == EManipulationDirection.BottomLeft || 
                               Modifier.ResizingDirection == EManipulationDirection.BottomRight);
            }

            DeltaX = DeltaX.ApplyScalePercent(this.OwnerView.PageDisplayScale, true);
            DeltaY = DeltaY.ApplyScalePercent(this.OwnerView.PageDisplayScale, true);

            double NewWidth = 0;
            double NewHeight = 0;
            double NewX = (Modifier.IsManipulatingHeading ? Modifier.ManipulatingHeadingRectangle.X : Modifier.ManipulatingDetailsRectangle.X);
            double NewY = (Modifier.IsManipulatingHeading ? Modifier.ManipulatingHeadingRectangle.Y : Modifier.ManipulatingDetailsRectangle.Y);
            bool IsSymmetrical = Keyboard.IsKeyDown(Key.LeftAlt);    // Do not use [Alt-Right]

            if (IsSymmetrical)
            {
                NewWidth = Modifier.ManipulatingHeadingRectangle.Width + DeltaX * (GoingRight ? 2.0 : -2.0);
                NewHeight = (Modifier.IsManipulatingHeading ? Modifier.ManipulatingHeadingRectangle.Height : Modifier.ManipulatingDetailsRectangle.Height)
                            + DeltaY * (GoingBottom ? 2.0 : -2.0);
            }
            else
            {
                NewWidth = Modifier.ManipulatingHeadingRectangle.Width + DeltaX * (GoingRight ? 1.0 : -1.0);
                NewHeight = (Modifier.IsManipulatingHeading ? Modifier.ManipulatingHeadingRectangle.Height : Modifier.ManipulatingDetailsRectangle.Height)
                            + DeltaY * (GoingBottom ? 1.0 : -1.0);
            }

            if (NewWidth < ApplicationProduct.ProductDirector.DefaultMinBaseFigureSize.Width)
                NewWidth = ApplicationProduct.ProductDirector.DefaultMinBaseFigureSize.Width;

            if (Modifier.IsManipulatingHeading)
            {
                if (NewHeight < ApplicationProduct.ProductDirector.DefaultMinBaseFigureSize.Height)
                    NewHeight = ApplicationProduct.ProductDirector.DefaultMinBaseFigureSize.Height;
            }
            else
                if (NewHeight < ApplicationProduct.ProductDirector.DefaultMinDetailsPosterHeight)
                    NewHeight = ApplicationProduct.ProductDirector.DefaultMinDetailsPosterHeight;

            Modifier.ManipulatingHeadingRectangle.Width = NewWidth;
            Modifier.ManipulatingDetailsRectangle.Width = NewWidth;

            if (Modifier.IsManipulatingHeading)
                Modifier.ManipulatingHeadingRectangle.Height = NewHeight;
            else
                Modifier.ManipulatingDetailsRectangle.Height = NewHeight;

            if (IsSymmetrical)
            {
                NewX = Modifier.ManipulatingHeadingRectangle.X + DeltaX * (GoingRight ? -1.0 : 1.0);
                NewY = (Modifier.IsManipulatingHeading ? Modifier.ManipulatingHeadingRectangle.Y : Modifier.ManipulatingDetailsRectangle.Y)
                       + DeltaY * (GoingBottom ? -1.0 : 1.0);
            }
            else
            {
                NewX = Modifier.ManipulatingHeadingRectangle.X + DeltaX * (GoingRight ? 0 : 1.0);
                NewY = (Modifier.IsManipulatingHeading ? Modifier.ManipulatingHeadingRectangle.Y : Modifier.ManipulatingDetailsRectangle.Y)
                       + DeltaY * (GoingBottom ? 0 : 1.0);
            }

            Modifier.ManipulatingHeadingRectangle.X = NewX;
            Modifier.ManipulatingDetailsRectangle.X = NewX;

            if (Modifier.IsManipulatingHeading)
                Modifier.ManipulatingHeadingRectangle.Y = NewY;
            else
                Modifier.ManipulatingDetailsRectangle.Y = NewY;

            if (IsDefinitive)
            {
                this.OwnerView.EditEngine.StartCommandVariation("Resize Symbol");
                var RouteChanges = CaptureRouteGeometryChanges(Modifier.ManipulatedSymbol.IntoEnumerable());

                if (this.OwnerView.SnapToGrid)
                {
                    NewWidth = NewWidth.EnforceMinimum(this.OwnerView.GridSize);
                    NewHeight = NewHeight.EnforceMinimum(this.OwnerView.GridSize);
                }
                else
                {
                    NewWidth = NewWidth.EnforceMinimum(VisualSymbolFormat.SYMBOL_MIN_INI_SIZE);
                    NewHeight = NewHeight.EnforceMinimum(VisualSymbolFormat.SYMBOL_MIN_INI_SIZE);
                }

                if (Modifier.IsManipulatingHeading)
                {
                    if (Modifier.ManipulatedSymbol.ResizeTo(NewWidth, NewHeight))
                        Modifier.ManipulatedSymbol.MoveTo(NewX + NewWidth / 2.0, NewY + NewHeight / 2.0, false, true);

                    Modifier.ManipulatingDetailsRectangle = new Rect(Modifier.ManipulatingHeadingRectangle.X, Modifier.ManipulatingHeadingRectangle.Y + Modifier.ManipulatingHeadingRectangle.Height,
                                                                     Modifier.ManipulatingHeadingRectangle.Width, Modifier.ManipulatedSymbol.DetailsPosterHeight);
                }
                else
                {
                    Modifier.ManipulatedSymbol.DetailsPosterHeight = NewHeight;

                    // FIX 2012.02.13: Do not remove this!, propagates poster resizing to the symbol head.
                    if (Modifier.ManipulatedSymbol.ResizeTo(NewWidth, Modifier.ManipulatedSymbol.BaseHeight))
                        Modifier.ManipulatedSymbol.MoveTo(NewX + NewWidth / 2.0, Modifier.ManipulatedSymbol.BaseCenter.Y, false, true);

                    Modifier.ManipulatedSymbol.ResizingArrange(VisualSymbolFormat.GetHasFixedWidth(Modifier.ManipulatedSymbol) ? 0.0 : NewWidth - Modifier.ManipulatedSymbol.BaseWidth,
                                                               NewHeight, true);
                    Modifier.ManipulatedSymbol.RenderElement();
                }

                RouteGeometryChanges(RouteChanges,
                                     Modifier.ManipulatedSymbol.OwnerRepresentation is RelationshipVisualRepresentation
                                     ? RelationshipRouteIntent.RelationshipCenterMoved
                                     : RelationshipRouteIntent.EndpointMoved,
                                     "direct manipulation resized symbol geometry");

                this.OwnerView.UpdateVersion();

                this.OwnerView.EditEngine.CompleteCommandVariation();

                this.IsManipulating = false;
            }
        }

        public void Resize(ViewComplementManipulationAdorner Modifier, bool IsDefinitive)
        {
            // Console.WriteLine("RESIZING... CurrentCenter={0}, GivePosition={1}, Direction={2}", TargetSymbol.SymbolCenter, CurrentPosition, ResizingDirection);
            double DeltaX = 0, DeltaY = 0;
            bool GoingRight = false, GoingBottom = false;
            bool FixedX = false, FixedY = false;

            this.IsManipulating = true;

            FixedX = (Modifier.ResizingDirection == EManipulationDirection.Top || Modifier.ResizingDirection == EManipulationDirection.Bottom);
            FixedY = (Modifier.ResizingDirection == EManipulationDirection.Left || Modifier.ResizingDirection == EManipulationDirection.Right);

            if (!FixedX)
            {
                DeltaX = Modifier.CurrentPosition.X - Modifier.PreviousPosition.X;
                GoingRight = (Modifier.ResizingDirection == EManipulationDirection.Right ||
                              Modifier.ResizingDirection == EManipulationDirection.TopRight ||
                              Modifier.ResizingDirection == EManipulationDirection.BottomRight);
            }

            if (!FixedY)
            {
                DeltaY = Modifier.CurrentPosition.Y - Modifier.PreviousPosition.Y;
                GoingBottom = (Modifier.ResizingDirection == EManipulationDirection.Bottom ||
                               Modifier.ResizingDirection == EManipulationDirection.BottomLeft ||
                               Modifier.ResizingDirection == EManipulationDirection.BottomRight);
            }

            DeltaX = DeltaX.ApplyScalePercent(this.OwnerView.PageDisplayScale, true);
            DeltaY = DeltaY.ApplyScalePercent(this.OwnerView.PageDisplayScale, true);

            double NewWidth = 0;
            double NewHeight = 0;
            double NewX = Modifier.ManipulatingHeadingRectangle.X;
            double NewY = Modifier.ManipulatingHeadingRectangle.Y;
            bool IsSymmetrical = Keyboard.IsKeyDown(Key.LeftAlt);    // Do not use [Alt-Right]

            if (IsSymmetrical)
            {
                NewWidth = Modifier.ManipulatingHeadingRectangle.Width + DeltaX * (GoingRight ? 2.0 : -2.0);
                NewHeight = Modifier.ManipulatingHeadingRectangle.Height + DeltaY * (GoingBottom ? 2.0 : -2.0);
            }
            else
            {
                NewWidth = Modifier.ManipulatingHeadingRectangle.Width + DeltaX * (GoingRight ? 1.0 : -1.0);
                NewHeight = Modifier.ManipulatingHeadingRectangle.Height + DeltaY * (GoingBottom ? 1.0 : -1.0);
            }

            if (Modifier.ManipulatedComplement.IsComplementGroupLine)
            {
                var Direction = Modifier.ManipulatedComplement.GetPropertyField<Orientation>(VisualComplement.PROP_FIELD_ORIENTATION);

                if (Direction == Orientation.Vertical)
                {
                    if (NewWidth < VisualComplement.DefaultGroupLineThicknessRange.Item1)
                        NewWidth = VisualComplement.DefaultGroupLineThicknessRange.Item1;

                    if (NewWidth > VisualComplement.DefaultGroupLineThicknessRange.Item2)
                        NewWidth = VisualComplement.DefaultGroupLineThicknessRange.Item2;

                    if (NewHeight < VisualComplement.DefaultMinBaseFigureSize.Height)
                        NewHeight = VisualComplement.DefaultMinBaseFigureSize.Height;
                }
                else
                {
                    if (NewHeight < VisualComplement.DefaultGroupLineThicknessRange.Item1)
                        NewHeight = VisualComplement.DefaultGroupLineThicknessRange.Item1;

                    if (NewHeight > VisualComplement.DefaultGroupLineThicknessRange.Item2)
                        NewHeight = VisualComplement.DefaultGroupLineThicknessRange.Item2;

                    if (NewWidth < VisualComplement.DefaultMinBaseFigureSize.Width)
                        NewWidth = VisualComplement.DefaultMinBaseFigureSize.Width;
                }
            }
            else
            {
                if (NewWidth < VisualComplement.DefaultMinBaseFigureSize.Width)
                    NewWidth = VisualComplement.DefaultMinBaseFigureSize.Width;

                if (NewHeight < VisualComplement.DefaultMinBaseFigureSize.Height)
                    NewHeight = VisualComplement.DefaultMinBaseFigureSize.Height;
            }

            if (IsSymmetrical)
            {
                NewX = Modifier.ManipulatingHeadingRectangle.X + DeltaX * (GoingRight ? -1.0 : 1.0);
                NewY = Modifier.ManipulatingHeadingRectangle.Y + DeltaY * (GoingBottom ? -1.0 : 1.0);
            }
            else
            {
                NewX = Modifier.ManipulatingHeadingRectangle.X + DeltaX * (GoingRight ? 0 : 1.0);
                NewY = Modifier.ManipulatingHeadingRectangle.Y + DeltaY * (GoingBottom ? 0 : 1.0);
            }

            Modifier.ManipulatingHeadingRectangle.Width = NewWidth;
            Modifier.ManipulatingHeadingRectangle.Height = NewHeight;

            Modifier.ManipulatingHeadingRectangle.X = NewX;
            Modifier.ManipulatingHeadingRectangle.Y = NewY;

            if (IsDefinitive)
            {
                this.OwnerView.EditEngine.StartCommandVariation("Resize Complement");

                if (Modifier.ManipulatedComplement.ResizeTo(NewWidth, NewHeight))
                    Modifier.ManipulatedComplement.MoveTo(NewX + NewWidth / 2.0, NewY + NewHeight / 2.0, false, true);

                this.OwnerView.UpdateVersion();

                this.OwnerView.EditEngine.CompleteCommandVariation();

                this.IsManipulating = false;
            }
        }

        // -----------------------------------------------------------------------------------------------------------------------
        public void SwitchIndividualDetail(VisualSymbol TargetSymbol, DetailDesignator Designator)
        {
            TargetSymbol.OwnerRepresentation.RepresentedIdea.OwnerComposition.Engine.StartCommandVariation("Switch individual Detail");

            var TargetIdea = TargetSymbol.OwnerRepresentation.RepresentedIdea;
            var Look = TargetIdea.GetDetailLook(Designator, TargetSymbol).Clone();

            Look.IsDisplayed = !Look.IsDisplayed;
            TargetIdea.UpdateCustomLookFor(Designator, TargetSymbol, Look);

            TargetSymbol.RenderElement();
            TargetSymbol.GetDisplayingView().UpdateVersion();

            TargetSymbol.OwnerRepresentation.RepresentedIdea.OwnerComposition.Engine.CompleteCommandVariation();
        }

        // -----------------------------------------------------------------------------------------------------------------------
        public void IndividualDetailDesignation(VisualSymbol Symbol, DetailDesignator Designator)
        {
            this.OwnerView.EditEngine.StartCommandVariation("Individual Detail Designation");
            var EditResult = DomainServices.EditDetailDesignator(Designator, false, this.OwnerView.EditEngine, false);
            if (EditResult.Item1.IsTrue())
            {
                var TargetIdeaDef = Symbol.OwnerRepresentation.RepresentedIdea.IdeaDefinitor;
                if (TargetIdeaDef.DetailDesignators.Contains(Designator))
                {
                    DomainServices.UpdateDomainDependants(TargetIdeaDef.OwnerDomain);
                    TargetIdeaDef.OwnerDomain.UpdateVersion();
                }
                else
                {
                    Symbol.OwnerRepresentation.RepresentedIdea.UpdateVersion();
                    Symbol.RenderElement();
                }

                this.OwnerView.EditEngine.CompleteCommandVariation();
            }
            else
                this.OwnerView.EditEngine.DiscardCommandVariation();
        }

        public void IndividualDetailChange(VisualSymbol Symbol, DetailDesignator Designator)
        {
            var TargetIdea = Symbol.OwnerRepresentation.RepresentedIdea;
            var Detail = TargetIdea.Details.FirstOrDefault(det => det.Designation.IsEqual(Designator));
            var Engine = this.OwnerView.EditEngine as CompositionEngine;

            Engine.StartCommandVariation("Change Individual Detail");

            var Designation = (Detail == null ? Designator.Assign(!Designator.Owner.IsGlobal)
                                              : Detail.ContentDesignator as Assignment<DetailDesignator>);
            var Look = TargetIdea.GetDetailLook(Designation.Value, Symbol);

            var Result = Engine.EditIdeaDetail(Designation, TargetIdea, Detail, Look);
            if (Result.Item1)
            {
                var TargetIdeaDef = Symbol.OwnerRepresentation.RepresentedIdea.IdeaDefinitor;
                // Only update Designator Id for local details definitions
                if (!TargetIdeaDef.DetailDesignators.Contains(Designator) && !Designator.Owner.IsGlobal)
                    Result.Item2.UpdateDesignatorIdentification();

                var PrevDsn = TargetIdea.Details.FirstOrDefault(cdet => cdet.Designation == Result.Item2.Designation);
                if (PrevDsn == null)
                    TargetIdea.Details.Add(Result.Item2);
                else
                    TargetIdea.Details.Replace(PrevDsn, Result.Item2);

                Symbol.RenderElement();
                TargetIdea.UpdateVersion();
                Engine.CompleteCommandVariation();
            }
            else
                Engine.DiscardCommandVariation();
        }

        public void IndividualDetailAccess(Idea TargetIdea, DetailDesignator Designator, VisualSymbol Symbol = null)
        {
            if (Symbol != null && Symbol.OwnerRepresentation.RepresentedIdea != TargetIdea)
                throw new UsageAnomaly("Symbol's represented Idea must be the same as the target, while accessing Detail.");

            var Detail = TargetIdea.Details.FirstOrDefault(det => det.Designation.IsEqual(Designator));
            var Engine = this.OwnerView.EditEngine as CompositionEngine;

            TargetIdea.OwnerComposition.Engine.StartCommandVariation("Access Individual Detail");

            var Designation = (Detail == null ? Designator.Assign(!Designator.Owner.IsGlobal)
                                              : Detail.ContentDesignator as Assignment<DetailDesignator>);
            var Look = (Symbol != null ? TargetIdea.GetDetailLook(Designation.Value, Symbol) : null);

            if (Detail is InternalLink || (Detail == null && Designator is LinkDetailDesignator && Designator.Initializer != null))
            {
                var LinkDetDsn = (Detail != null ? Detail.Designation : Designator) as LinkDetailDesignator;
                var PropDef = (Detail != null ? ((InternalLink)Detail).TargetProperty : LinkDetDsn.Initializer as MModelPropertyDefinitor);
                Engine.GoToInternalLink(LinkDetDsn, PropDef, TargetIdea);

                if (Symbol != null)
                    Symbol.RenderElement();

                Engine.CompleteCommandVariation();
                return;
            }

            if (Detail is Link)
            {
                Engine.GoToLink((Link)Detail, TargetIdea);
                return;
            }

            if (Detail is Attachment || (Detail == null && Designator is AttachmentDetailDesignator))
            {
                var ExtResult = Engine.ExternalEditDetailAttachment(Designation, TargetIdea, (Attachment)Detail, (AttachmentAppearance)Look);
                if (ExtResult.Item1)
                {
                    // Only update Designator Id for local details definitions
                    if (!TargetIdea.IdeaDefinitor.DetailDesignators.Contains(Designator) && !Designator.Owner.IsGlobal)
                        ExtResult.Item2.UpdateDesignatorIdentification();

                    if (!TargetIdea.Details.Any(cdet => cdet.Designation == ExtResult.Item2.Designation))
                        TargetIdea.Details.Add(ExtResult.Item2);

                    if (Symbol != null)
                        Symbol.RenderElement();

                    Engine.CompleteCommandVariation();
                }
                else
                    Engine.DiscardCommandVariation();

                return;
            }

            // For Tables...
            var Result = Engine.EditIdeaDetail(Designation, TargetIdea, Detail, Look);
            if (Result.Item1)
            {
                // Only update Designator Id for local details definitions
                if (!TargetIdea.IdeaDefinitor.DetailDesignators.Contains(Designator) && !Designator.Owner.IsGlobal)
                    Result.Item2.UpdateDesignatorIdentification();

                if (!TargetIdea.Details.Any(cdet => cdet.Designation == Result.Item2.Designation))
                    TargetIdea.Details.Add(Result.Item2);

                if (Symbol != null)
                    Symbol.RenderElement();

                Engine.CompleteCommandVariation();
            }
            else
                Engine.DiscardCommandVariation();
        }

        // -----------------------------------------------------------------------------------------------------------------------
        public void ShowCompositeAsDetail(VisualSymbol TargetSymbol)
        {
            Console.WriteLine(CompositeViewIntegrity.GetToggleDiagnostic(TargetSymbol));

            string Warning;
            if (!CompositeViewIntegrity.CanShowCompositeContentAsDetail(TargetSymbol, out Warning))
            {
                Console.WriteLine(Warning);
                Display.DialogMessage("Nested Content", Warning, EMessageType.Warning);
                return;
            }

            TargetSymbol.OwnerRepresentation.RepresentedIdea.OwnerComposition.Engine.StartCommandVariation("Show Composite-Content as Detail");
            var PrevHeight = TargetSymbol.TotalHeight;

            if (!TargetSymbol.AreDetailsShown)
                TargetSymbol.AreDetailsShown = true;

            TargetSymbol.ShowCompositeContentAsDetails = !TargetSymbol.ShowCompositeContentAsDetails;

            TargetSymbol.OwnerRepresentation.Render();
            TargetSymbol.ResizingArrange(0, TargetSymbol.TotalHeight - PrevHeight, true);

            TargetSymbol.GetDisplayingView().UpdateVersion();

            TargetSymbol.OwnerRepresentation.RepresentedIdea.OwnerComposition.Engine.CompleteCommandVariation();
        }

        public void SwitchDetails(VisualSymbol TargetSymbol, bool? ExplicitShow = null, bool Refresh = true)
        {
            TargetSymbol.OwnerRepresentation.DisplayingView.EditEngine.StartCommandVariation("Switch Details");
            var PrevHeight = TargetSymbol.TotalHeight;

            TargetSymbol.AreDetailsShown = (ExplicitShow == null ? !TargetSymbol.AreDetailsShown : ExplicitShow.Value);

            TargetSymbol.OwnerRepresentation.Render();
            TargetSymbol.ResizingArrange(0, TargetSymbol.TotalHeight - PrevHeight, true);

            if (Refresh)
                TargetSymbol.OwnerRepresentation.DisplayingView.UpdateVersion();

            TargetSymbol.OwnerRepresentation.DisplayingView.EditEngine.CompleteCommandVariation();
        }

        // -----------------------------------------------------------------------------------------------------------------------
        public void SwitchRelated(VisualSymbol SelectedSymbol, bool ToTargets)
        {
            this.OwnerView.EditEngine.StartCommandVariation("Switch Related Visual Representations");

            var RootRepresentation = SelectedSymbol.OwnerRepresentation;

            if (ToTargets)
                RootRepresentation.AreRelatedTargetsShown = !RootRepresentation.AreRelatedTargetsShown;
            else
                RootRepresentation.AreRelatedOriginsShown = !RootRepresentation.AreRelatedOriginsShown;

            var Trace = RootRepresentation.IntoList();

            RootRepresentation.Render();

            var BaseRepresentations = (ToTargets ? RootRepresentation.TargetRepresentations
                                                 : RootRepresentation.OriginRepresentations);
            var ExposeRelated = (ToTargets ? RootRepresentation.AreRelatedTargetsShown : RootRepresentation.AreRelatedOriginsShown);
            foreach (var Reference in BaseRepresentations)
                VisualizeRelatedRepresentations(Reference, RootRepresentation, ExposeRelated, Trace, ToTargets);

            // Needed in case of movement between show/hide the related connectors
            SelectedSymbol.UpdateDependents();

            this.OwnerView.UpdateVersion();
            this.OwnerView.EditEngine.CompleteCommandVariation();
        }

        public void VisualizeRelatedRepresentations(VisualRepresentation CurrentRepresentator, VisualRepresentation PreviousRepresentator,
                                                    bool ExposeRelated, IList<VisualRepresentation> Trace, bool ToTargets)
        {
            // Visit representators only once
            if (CurrentRepresentator.IsIn(Trace))
                return;

            Trace.Add(CurrentRepresentator);

            // Propagate to related connectors pointed to/from the current-representator
            var SourceRepDirectedConnectors = (ToTargets ? PreviousRepresentator.MainSymbol.TargetConnections
                                                                .Where(conn => conn.TargetSymbol == CurrentRepresentator.MainSymbol)
                                                         : PreviousRepresentator.MainSymbol.OriginConnections
                                                                .Where(conn => conn.OriginSymbol == CurrentRepresentator.MainSymbol));

            foreach (var Connector in SourceRepDirectedConnectors)
                if (Connector.IsRelatedVisible != ExposeRelated)
                {
                    Connector.IsRelatedVisible = ExposeRelated;
                    Connector.RenderElement();
                }

            // Stop if contra-pointed to/from other visible sources
            var ContraPointedByOtherSources = (ToTargets ? CurrentRepresentator.MainSymbol.OriginConnections : CurrentRepresentator.MainSymbol.TargetConnections)
                                                    .Any(conn => !conn.OwnerRelationshipRepresentation.RepresentedRelationship.IsAutoReferenceExclusive
                                                                    && (!conn.OwnerRepresentation.IsOneOf(PreviousRepresentator, CurrentRepresentator)
                                                                        && conn.IsRelatedVisible));

            if (ContraPointedByOtherSources)
                return;

            // Propagate to related objects
            foreach (var Part in CurrentRepresentator.VisualParts.OrderBy(part => !(part is VisualSymbol)))
                if (Part.IsRelatedVisible != ExposeRelated)
                {
                    Part.IsRelatedVisible = ExposeRelated;
                    Part.RenderElement();

                    var SymbolPart = Part as VisualSymbol;
                    if (SymbolPart != null && SymbolPart.AttachedComplements != null)
                        foreach (var Complement in SymbolPart.AttachedComplements)
                        {
                            Complement.IsRelatedVisible = ExposeRelated;
                            Complement.Render();
                        }
                }

            // Propagate to related connectors pointing to the current-representator
            var OtherSourceRepsDirectedConnectors = (ToTargets ? CurrentRepresentator.MainSymbol.OriginConnections : CurrentRepresentator.MainSymbol.TargetConnections)
                                                        .Where(conn => conn.IsRelatedVisible != ExposeRelated
                                                                       && !conn.IsIn(SourceRepDirectedConnectors));

            foreach (var Connector in OtherSourceRepsDirectedConnectors)
                if (!ExposeRelated || (Connector.OriginSymbol.IsRelatedVisible
                                        && Connector.TargetSymbol.IsRelatedVisible))
                {
                    Connector.IsRelatedVisible = ExposeRelated;
                    Connector.RenderElement();
                }

            // Propagate to related representators if shown
            if ((ToTargets && CurrentRepresentator.AreRelatedTargetsShown)
                || (!ToTargets && CurrentRepresentator.AreRelatedOriginsShown))
            {
                var PropagationRepresentations = (ToTargets ? CurrentRepresentator.TargetRepresentations : CurrentRepresentator.OriginRepresentations);
                foreach (var Reference in PropagationRepresentations)
                    VisualizeRelatedRepresentations(Reference, CurrentRepresentator, ExposeRelated, Trace, ToTargets);
            }
        }

        // -----------------------------------------------------------------------------------------------------------------------
        public void Displace(ViewConnectorManipulationAdorner Modifier, bool IsDefinitive)
        {
            //T Console.WriteLine("DISPLACING..." + DateTime.Now.Ticks);

            var DeltaX = Modifier.CurrentPosition.X - Modifier.PreviousPosition.X;
            var DeltaY = Modifier.CurrentPosition.Y - Modifier.PreviousPosition.Y;

            DeltaX = DeltaX.ApplyScalePercent(this.OwnerView.PageDisplayScale, true);
            DeltaY = DeltaY.ApplyScalePercent(this.OwnerView.PageDisplayScale, true);

            if (Modifier.IsWorkingOnAlternateTarget)
            {
                //T Console.WriteLine("On Alternate.");
                Modifier.ManipulationAlternatePosition = new Point(Modifier.ManipulationAlternatePosition.X + DeltaX,
                                                                   Modifier.ManipulationAlternatePosition.Y + DeltaY);

                if (Modifier.ManipulatedConnector.RoutePoints.Count == 0)
                {
                    //T Console.WriteLine("Intermediate is empty.");
                    double CalcSumX = 0, CalcSumY = 0;

                    if (Modifier.ManipulatedConnector.OriginSymbol == (Modifier.ManipulatedAlternateObject is VisualElement
                                                                       ? ((VisualElement)Modifier.ManipulatedAlternateObject).OwnerRepresentation.MainSymbol
                                                                       : null))
                    {
                        var TargetEdgePoint = Modifier.ManipulatedConnector.TargetPosition
                                                .DetermineNearestIntersectingPoint(Modifier.ManipulatedConnector.OriginPosition,
                                                                                   this.OwnerView.Presenter,
                                                                                   Modifier.ManipulatedConnector.TargetSymbol.Graphic, this.OwnerView.VisualHitTestFilter);

                        var AltEdge = Modifier.ManipulatedConnector.OriginPosition
                                                .DetermineNearestIntersectingPoint(Modifier.ManipulatedConnector.TargetPosition, this.OwnerView.Presenter,
                                                                                   Modifier.ManipulatedConnector.OwnerRepresentation.MainSymbol.Graphic, this.OwnerView.VisualHitTestFilter);
                        CalcSumX = AltEdge.X + TargetEdgePoint.X;
                        CalcSumY = AltEdge.Y + TargetEdgePoint.Y;
                    }
                    else
                    {
                        var OriginEdgePoint = Modifier.ManipulatedConnector.OriginPosition
                                                .DetermineNearestIntersectingPoint(Modifier.ManipulatedConnector.TargetPosition,
                                                                                   this.OwnerView.Presenter,
                                                                                   Modifier.ManipulatedConnector.OriginSymbol.Graphic, this.OwnerView.VisualHitTestFilter);

                        var AltEdge = Modifier.ManipulatedConnector.TargetPosition
                                                .DetermineNearestIntersectingPoint(Modifier.ManipulatedConnector.OriginPosition, this.OwnerView.Presenter,
                                                                                   Modifier.ManipulatedConnector.OwnerRepresentation.MainSymbol.Graphic, this.OwnerView.VisualHitTestFilter);
                        CalcSumX = AltEdge.X + OriginEdgePoint.X;
                        CalcSumY = AltEdge.Y + OriginEdgePoint.Y;
                    }

                    var PosX = CalcSumX / 2.0;
                    var PosY = CalcSumY / 2.0;
                    Modifier.ManipulConnDisplacingPos = new Point(PosX, PosY);
                }
            }
            else
            {
                //T Console.WriteLine("Intermediate is NOT empty.");
                Modifier.ManipulConnDisplacingPos = new Point(Modifier.ManipulConnDisplacingPos.X + DeltaX,
                                                              Modifier.ManipulConnDisplacingPos.Y + DeltaY);
                Modifier.ManipulConnRelinkingPos = new Point(Modifier.ManipulConnRelinkingPos.X + DeltaX,
                                                             Modifier.ManipulConnRelinkingPos.Y + DeltaY);
            }

            //T Console.WriteLine("Displacing to " + Modifier.ManipulConnDisplacingPos.ToString());

            if (IsDefinitive)
            {
                this.OwnerView.EditEngine.StartCommandVariation("Displace Connector");

                if (Modifier.IsWorkingOnAlternateTarget)
                {
                    var RelationshipSymbol = Modifier.ManipulatedConnector.OwnerRelationshipRepresentation.MainSymbol;
                    var RouteChanges = CaptureRouteGeometryChanges(RelationshipSymbol.IntoEnumerable());
                    RelationshipSymbol.IsAutoPositionable = false;
                    RelationshipSymbol
                        .MoveTo(this.OwnerView.GetGridSnappedCoordinate(Modifier.ManipulationAlternatePosition.X, false),
                                this.OwnerView.GetGridSnappedCoordinate(Modifier.ManipulationAlternatePosition.Y, false), true);
                    RouteGeometryChanges(RouteChanges, RelationshipRouteIntent.RelationshipCenterMoved,
                                         "direct manipulation moved relationship hub geometry");
                }
                else
                    if (!Modifier.MousePositionCurrent.IsNear(Modifier.PointedLocationWhileClicking))
                    {
                        var RouteConnector = Modifier.ManipulatedRouteConnector ?? Modifier.ManipulatedConnector;
                        var RoutePosition = (this.OwnerView.SnapToGrid
                                             ? this.OwnerView.GetGridSnappedPosition(Modifier.ManipulConnDisplacingPos, false)
                                             : Modifier.ManipulConnDisplacingPos);

                        if (Modifier.ManipulatedRoutePointIndex >= 0)
                            RouteConnector.UpdateRoutePoint(Modifier.ManipulatedRoutePointIndex, RoutePosition);
                        else if (Modifier.ManipulatedSegmentIndex >= 0)
                        {
                            if (RouteConnector.RoutePoints.Count < VisualConnector.MAX_ROUTE_POINTS)
                                RouteConnector.InsertRoutePoint(Math.Min(Modifier.ManipulatedSegmentIndex,
                                                                         RouteConnector.RoutePoints.Count),
                                                                RoutePosition);
                        }
                        else
                            RouteConnector.SetRoutePoints(RoutePosition.IntoEnumerable());

                        VisualConnectorsFormat.SetPathStyle(RouteConnector, EPathStyle.MultilineFreeAngled);
                        VisualConnectorsFormat.SetPathCorner(RouteConnector, EPathCorner.Sharp);
                        // Connector format overrides are stored on the Relationship visual
                        // representation. Re-render the owner once so every sibling leg observes
                        // the new path style/corner while the edited leg shows its new geometry.
                        RouteConnector.OwnerRelationshipRepresentation.Render();
                    }

                this.OwnerView.UpdateVersion();

                this.OwnerView.EditEngine.CompleteCommandVariation();
            }

            // This forces the adorners to be later repainted correctly.
            this.WorkingAdorner.Visualize(false);
        }

        // -----------------------------------------------------------------------------------------------------------------------
        public void Relink(ViewConnectorManipulationAdorner Modifier, bool IsDefinitive)
        {
            //T Console.WriteLine("RELINKING...");
            var DeltaX = Modifier.CurrentPosition.X - Modifier.PreviousPosition.X;
            var DeltaY = Modifier.CurrentPosition.Y - Modifier.PreviousPosition.Y;

            DeltaX = DeltaX.ApplyScalePercent(this.OwnerView.PageDisplayScale, true);
            DeltaY = DeltaY.ApplyScalePercent(this.OwnerView.PageDisplayScale, true);

            //T Console.WriteLine("Relinking DeltaX=[{0}], DeltaY=[{1}]", DeltaX, DeltaY);

            if (this.OwnerView.SnapToGrid)
                Modifier.ManipulConnRelinkingPos = new Point(this.OwnerView.GetGridSnappedCoordinate(Modifier.ManipulConnRelinkingPos.X, false) + DeltaX,
                                                             this.OwnerView.GetGridSnappedCoordinate(Modifier.ManipulConnRelinkingPos.Y, false) + DeltaY);
            else
                Modifier.ManipulConnRelinkingPos = new Point(Modifier.ManipulConnRelinkingPos.X + DeltaX,
                                                             Modifier.ManipulConnRelinkingPos.Y + DeltaY);
            
            if (IsDefinitive)
            {
                var PointedRepresentation = this.OwnerView.Engine.GetPointedRepresentation(Modifier.ManipulConnRelinkingPos, true);
                if (PointedRepresentation == null)
                    return;

                if (!(PointedRepresentation.RepresentedIdea.IdeaDefinitor.PreciseConnectByDefault
                      || (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))))
                    Modifier.ManipulConnRelinkingPos = PointedRepresentation.MainSymbol.BaseCenter;

                // Console.WriteLine("RELINK to {0}", TargetRepresentation);
                this.OwnerView.EditEngine.StartCommandVariation("Relink Connector");

                RelationshipCreationCommand.RelinkRelationship(Modifier.ManipulatedConnector, PointedRepresentation.MainSymbol,
                                                               Modifier.ManipulConnRelinkingPos, !Modifier.WorkingOnOrigin);
                this.OwnerView.UpdateVersion();

                this.OwnerView.EditEngine.CompleteCommandVariation();

                Modifier.IntendedAction = EConnectorManipulationAction.Displace;
            }

            this.WorkingAdorner.Visualize();
        }

        // -----------------------------------------------------------------------------------------------------------------------
    }
}
