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
// File   : VisualConnector.cs
// Object : Instrumind.ThinkComposer.Model.VisualModel.VisualConnector (Class)
//
// Date       Author             Changes
// ---------- ------------------ -------------------------------------------------------------
// 2009.09.29 Néstor Sánchez A.  Creation
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Media;

using Instrumind.Common;
using Instrumind.Common.EntityBase;
using Instrumind.Common.EntityDefinition;
using Instrumind.Common.Visualization;

using Instrumind.ThinkComposer.ApplicationProduct;
using Instrumind.ThinkComposer.Definitor.DefinitorMaintenance;
using Instrumind.ThinkComposer.Definitor.DefinitorUI;
using Instrumind.ThinkComposer.MetaModel.GraphMetaModel;
using Instrumind.ThinkComposer.MetaModel.VisualMetaModel;
using Instrumind.ThinkComposer.Model.GraphModel;
using Instrumind.ThinkComposer.Composer.ComposerUI;

/// Base abstractions for the visual representation of Graph entities
namespace Instrumind.ThinkComposer.Model.VisualModel
{
    /// <summary>
    /// Makes a visual connection between two elements.
    /// </summary>
    [Serializable]
    public class VisualConnector : VisualElement, IModelEntity, IModelClass<VisualConnector>
    {
        /// <summary>
        /// Size adjustment factor for plug and lines respect its system definitions.
        /// </summary>
        public const double VISUAL_MAGNITUDE_ADJUSTMENT = 0.65;

        /// <summary>
        /// Maximum number of interior route points accepted by the visual model.
        /// </summary>
        public const int MAX_ROUTE_POINTS = 32;

        /// <summary>
        /// Static Constructor.
        /// </summary>
        static VisualConnector()
        {
            __ClassDefinitor = new ModelClassDefinitor<VisualConnector>("VisualConnector", VisualElement.__ClassDefinitor, "Visual Connector",
                                                                        "Makes a visual connection between two elements.");
            __ClassDefinitor.DeclareProperty(__OwnerRelationshipRepresentation);
            __ClassDefinitor.DeclareProperty(__RepresentedLink);
            __ClassDefinitor.DeclareProperty(__OriginPosition);
            __ClassDefinitor.DeclareProperty(__OriginEdgePosition);
            __ClassDefinitor.DeclareProperty(__OriginSymbol);
            __ClassDefinitor.DeclareProperty(__TargetPosition);
            __ClassDefinitor.DeclareProperty(__TargetEdgePosition);
            __ClassDefinitor.DeclareProperty(__TargetSymbol);
            __ClassDefinitor.DeclareProperty(__IntermediatePosition);
            __ClassDefinitor.DeclareCollection(__RoutePoints);
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public VisualConnector(RelationshipVisualRepresentation OwnerRelationshipRepresentation, RoleBasedLink RepresentedLink,
                               VisualSymbol OriginSymbol, VisualSymbol TargetSymbol, Point OriginPosition, Point TargetPosition)
             : base(EVisualRepresentationPart.RelationshipLinkConnector)
        {
            this.SetRoutePointsStorage(new EditableList<Point>(__RoutePoints.TechName, this, 4));
            this.OwnerRelationshipRepresentation = OwnerRelationshipRepresentation;
            this.RepresentedLink = RepresentedLink;
            this.OriginSymbol = OriginSymbol;
            this.TargetSymbol = TargetSymbol;
            this.OriginPosition = OriginPosition;
            this.TargetPosition = TargetPosition;

            this.OriginSymbol.TargetConnections.Add(this);
            this.TargetSymbol.OriginConnections.Add(this);
        }

        /// <summary>
        /// Minimal constructor used by dependency-free model regression checks.
        /// </summary>
        internal VisualConnector()
             : base(EVisualRepresentationPart.RelationshipLinkConnector)
        {
            this.SetRoutePointsStorage(new EditableList<Point>(__RoutePoints.TechName, this, 4));
        }

        /// <summary>
        /// Initializes route storage after loading an old binary Composition or cloning by serialization.
        /// </summary>
        [OnDeserialized]
        private void InitializeRoutePoints(StreamingContext context = default(StreamingContext))
        {
            this.ContextRoutePointIndex = -1;

            try
            {
                if (this.RoutePoints_ == null)
                {
                    this.SetRoutePointsStorage(new EditableList<Point>(__RoutePoints.TechName, this, 1));
                    if (IsUsableRoutePoint(this.IntermediatePosition_))
                        this.RoutePoints_.Add(this.IntermediatePosition_);
                }
                else
                    this.SetRoutePointsStorage(this.RoutePoints_);

                ValidateRoutePoints(this.RoutePoints_);
            }
            catch (ArgumentException Problem)
            {
                throw new SerializationException("The serialized connector route is invalid.", Problem);
            }

            SynchronizeLegacyIntermediatePosition();
        }

        /// <summary>
        /// Keeps the old singleton field populated for readers of the legacy binary shape.
        /// </summary>
        [OnSerializing]
        private void PrepareLegacyRoutePoint(StreamingContext context = default(StreamingContext))
        {
            ValidateRoutePoints(this.RoutePoints_);
            SynchronizeLegacyIntermediatePosition();
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------------------------
        /// <summary>
        /// Disconnects the bounds created on construction with the Origin and Target symbols.
        /// </summary>
        public void Disconnect()
        {
            // Remove semantic Link

            // IMPORTANT: If the future, and if the link can be represented by more than one connector,
            //            then only remove the link when only one representative connector remains.
            this.OwnerRelationshipRepresentation.RepresentedRelationship.RemoveLink(this.RepresentedLink);

            this.OriginSymbol.TargetConnections.Remove(this);
            this.TargetSymbol.OriginConnections.Remove(this);
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------------------------
        /// <summary>
        /// Creates and returns a new draw implementing this visual connector for an optional presentation context.
        /// </summary>
        public override DrawingGroup CreateDraw(UIElement PresentationContext, bool ShowManipulationAdorners)
        {
            EnsureRoutePointsCollection();

            // Calculate the Edge Positions
            if (this.OriginSymbol.Graphic == null)
                this.OriginSymbol.GenerateGraphic(PresentationContext, ShowManipulationAdorners);

            if (this.TargetSymbol.Graphic == null)
                this.TargetSymbol.GenerateGraphic(PresentationContext, ShowManipulationAdorners);

            // IMPORTANT: This validation allows to calculate edge-positions ONLY when not present.
            //            The last calculated edge-positions are used for when presentation-context is not supplied,
            //            which is used for generating the graphics of a composite-content view inside a symbol's details poster.
            if (PresentationContext == null
                && (this.OriginEdgePosition == Display.NULL_POINT || this.TargetEdgePosition == Display.NULL_POINT))
                PresentationContext = this.OwnerRepresentation.DisplayingView.Presenter;

            var FirstRoutePoint = FindNextDistinctPoint(this.OriginPosition, this.RoutePoints, this.TargetPosition);
            var LastRoutePoint = FindPreviousDistinctPoint(this.TargetPosition, this.RoutePoints, this.OriginPosition);

            if (PresentationContext != null)
            {
                if (this.OriginSymbol.IsRelatedVisible)
                {
                    var EdgePosition = (this.OriginSymbol.IsHidden ? this.OriginPosition :
                                        this.OriginPosition.DetermineNearestIntersectingPoint(FirstRoutePoint, PresentationContext,
                                                                                              this.OriginSymbol.Graphic,
                                                                                              this.OwnerRepresentation.DisplayingView.VisualHitTestFilter));
                    if (EdgePosition != FirstRoutePoint)
                        this.OriginEdgePosition = EdgePosition;
                }

                if (this.TargetSymbol.IsRelatedVisible)
                {
                    var EdgePosition = (this.TargetSymbol.IsHidden ? this.TargetPosition :
                                        this.TargetPosition.DetermineNearestIntersectingPoint(LastRoutePoint, PresentationContext,
                                                                                              this.TargetSymbol.Graphic,
                                                                                              this.OwnerRepresentation.DisplayingView.VisualHitTestFilter));
                    if (EdgePosition != LastRoutePoint)
                        this.TargetEdgePosition = EdgePosition;
                }

                // Compensate a border hit-test result that points to the opposite symbol center.
                if (this.OriginEdgePosition == this.TargetSymbol.BaseCenter)
                {
                    this.OriginPosition = this.OriginSymbol.BaseCenter.FindBoundary(this.OriginPosition, PresentationContext,
                                                                                    this.OriginSymbol.Graphic, true)
                                                                      .SubstituteFor(default(Point), this.OriginSymbol.BaseCenter);
                    this.OriginEdgePosition = this.OriginPosition;
                }

                if (this.TargetEdgePosition == this.OriginSymbol.BaseCenter)
                {
                    this.TargetPosition = this.TargetSymbol.BaseCenter.FindBoundary(this.TargetPosition, PresentationContext,
                                                                                    this.TargetSymbol.Graphic, true)
                                                                      .SubstituteFor(default(Point), this.TargetSymbol.BaseCenter);
                    this.TargetEdgePosition = this.TargetPosition;
                }
            }

            // Draw one continuous geometry so joins, dashes, hit bounds and rounded corners
            // remain coherent across every interior route point.
            var Result = MasterDrawer.CreateDrawingConnector(this.OriginPlug, this.TargetPlug,
                                                             VisualConnectorsFormat.GetLineBrush(this),
                                                             VisualConnectorsFormat.GetLineThickness(this),
                                                             VisualConnectorsFormat.GetLineDash(this),
                                                             VisualConnectorsFormat.GetLineJoin(this),
                                                             VisualConnectorsFormat.GetLineCap(this),
                                                             VisualConnectorsFormat.GetPathStyle(this),
                                                             VisualConnectorsFormat.GetPathCorner(this),
                                                             VisualConnectorsFormat.GetMainBackground(this),
                                                             VisualConnectorsFormat.GetOpacity(this),
                                                             this.TargetEdgePosition, this.OriginEdgePosition,
                                                             this.RoutePoints, VISUAL_MAGNITUDE_ADJUSTMENT);

            /*T Console.WriteLine("OriginEdgePosition X={0}, Y={1}. TargetEdgePosition X={2}, Y={3}. IP={4}",
                              OriginEdgePosition.X, OriginEdgePosition.Y, TargetEdgePosition.X, TargetEdgePosition.Y, this.IntermediatePosition); */

            // PENDING: Register periferic decorators for drawing (such as callouts, notes, etc. Not to be confused with Text decorations)

            // Show main-symbol name if required
            var RelDef = this.RepresentedLink.OwnerRelationship.RelationshipDefinitor.Value;

            /* ?
            if (RelDef.IsSimple && RelDef.HideCentralSymbolWhenSimple && RelDef.ShowNameIfHidingCentralSymbol)
            {
                var LabelingBrushes = this.OwnerRelationshipRepresentation.MainSymbol.PutNameOnTop(Result);
                this.OwnerRelationshipRepresentation.MainSymbol.PutDefinitionOnTop(Result, LabelingBrushes.Item2, LabelingBrushes.Item1, 4);
            }
            else
                if (this.OwnerRelationshipRepresentation.MainSymbol.IsHidden)
                {
                    var LabelingBrushes = this.OwnerRelationshipRepresentation.MainSymbol.GetDefaultLabelBrushes();
                    this.OwnerRelationshipRepresentation.MainSymbol.PutDefinitionOnTop(Result, LabelingBrushes.Item2, LabelingBrushes.Item1);
                } */

            this.LabelArea = null;

            // Show link-role name decorator if required
            if (RelDef.DefaultConnectorsFormat.LabelLinkVariant
                || RelDef.DefaultConnectorsFormat.LabelLinkDefinitor
                || RelDef.DefaultConnectorsFormat.LabelLinkDescriptor
                || this.OwnerRepresentation.DisplayingView.ShowLinkRoleVariantLabels
                || this.OwnerRepresentation.DisplayingView.ShowLinkRoleDefNameLabels
                || this.OwnerRepresentation.DisplayingView.ShowLinkRoleDescNameLabels)
                using (var Context = Result.Append())
                {
                    var DecoratorCenter = DetermineLabelPosition(this.GetPathPoints());

                    var LinkDescriptorLabel = (((RelDef.DefaultConnectorsFormat.LabelLinkDescriptor
                                               || this.OwnerRepresentation.DisplayingView.ShowLinkRoleDescNameLabels)
                                               && this.RepresentedLink.Descriptor != null)
                                               ? this.RepresentedLink.Descriptor.Name : null);

                    var LinkDefinitorLabel = (RelDef.DefaultConnectorsFormat.LabelLinkDefinitor
                                              || this.OwnerRepresentation.DisplayingView.ShowLinkRoleDefNameLabels
                                              ? this.RepresentedLink.RoleDefinitor.Name : null);

                    var LinkRoleVariantLabel = (RelDef.DefaultConnectorsFormat.LabelLinkVariant
                                                || this.OwnerRepresentation.DisplayingView.ShowLinkRoleVariantLabels
                                                ? this.RepresentedLink.RoleVariant.ToString() : null);

                    this.LabelArea = MasterDrawer.PutConnectorLabeling(Context, RelDef, DecoratorCenter,
                                                                       VisualSymbolFormat.GetTextFormat(this.OwnerRelationshipRepresentation.MainSymbol,
                                                                                                        ETextPurpose.Extra),
                                                                       VisualConnectorsFormat.GetMainBackground(this),
                                                                       VisualConnectorsFormat.GetLineBrush(this),
                                                                       LinkDescriptorLabel, LinkDefinitorLabel, LinkRoleVariantLabel);
                }

            // Register Selection Indicators for drawing
            // NOTE: (selection indicators at the symbol's center interfere with in-place editing)
            if (ShowManipulationAdorners)
                if (this.OwnerRepresentation.IsSelected)
                {
                    var SizeFactor = (this.GetDisplayingView().SelectedObjects.Contains(this)
                                      ? 1.5 : 0.5);
                    this.OwnerRepresentation.DisplayingView.AttachAdorner(this, GenerateSelectionIndicators(INDICATOR_SIZE * SizeFactor,
                                                                                                            SelectionIndicatorBackground,
                                                                                                            SelectionIndicatorForeground).Select(tup => tup.Item1));
                }
                else
                    this.OwnerRepresentation.DisplayingView.DetachAdorner(this);

            if (this.OwnerRepresentation.IsVanished)
                Result.Opacity = VisualRepresentation.SELECTION_VANISHING_OPACITY;

            return Result;
        }

        /// <summary>
        /// Contains area of a possible label.
        /// </summary>
        [NonSerialized]
        public Rect? LabelArea = null;

        /// <summary>
        /// Bend index exposed temporarily while a connector context menu is open.
        /// </summary>
        [NonSerialized]
        public int ContextRoutePointIndex = -1;

        /// <summary>
        /// Recreates and returns the Graphic of this visual connector.
        /// </summary>
        public override ContainerVisual GenerateGraphic(UIElement PresentationContext, bool ShowManipulationAdorners)
        {
            this.Graphic = this.CreateDraw(PresentationContext, ShowManipulationAdorners).RenderToDrawingVisual();
            return this.Graphic;
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------------------------
        /// <summary>
        /// Updates the connector intermediate point to the specified position.
        /// </summary>
        [Obsolete("Use SetRoutePoints or UpdateRoutePoint for multi-point routes.")]
        public void UpdateIntermediatePoint(Point NewPosition)
        {
            this.IntermediatePosition = NewPosition;
            this.RenderElement();
        }

        /// <summary>
        /// Atomically replaces all interior route points after validating the complete input.
        /// </summary>
        public void SetRoutePoints(IEnumerable<Point> NewRoutePoints)
        {
            var ValidatedPoints = ValidateRoutePoints(NewRoutePoints);
            EnsureRoutePointsCollection();

            this.RoutePoints_.Clear();
            this.RoutePoints_.AddRange(ValidatedPoints);
            SynchronizeLegacyIntermediatePosition();
        }

        /// <summary>
        /// Alias which emphasizes that the complete route is replaced.
        /// </summary>
        public void ReplaceRoutePoints(IEnumerable<Point> NewRoutePoints)
        {
            SetRoutePoints(NewRoutePoints);
        }

        /// <summary>
        /// Clears all interior points, producing a straight connector.
        /// </summary>
        public void ClearRoutePoints()
        {
            EnsureRoutePointsCollection();
            if (this.RoutePoints_.Count > 0)
                this.RoutePoints_.Clear();

            SynchronizeLegacyIntermediatePosition();
        }

        /// <summary>
        /// Inserts an interior point at the supplied route index.
        /// </summary>
        public void InsertRoutePoint(int Index, Point NewPoint)
        {
            EnsureRoutePointsCollection();
            if (Index < 0 || Index > this.RoutePoints_.Count)
                throw new ArgumentOutOfRangeException("Index");
            if (this.RoutePoints_.Count >= MAX_ROUTE_POINTS)
                throw new InvalidOperationException("A connector route cannot contain more than " + MAX_ROUTE_POINTS + " interior points.");

            ValidateRoutePoint(NewPoint, "NewPoint");
            this.RoutePoints_.Insert(Index, NewPoint);
            SynchronizeLegacyIntermediatePosition();
        }

        /// <summary>
        /// Replaces one interior point.
        /// </summary>
        public void UpdateRoutePoint(int Index, Point NewPoint)
        {
            EnsureRoutePointsCollection();
            if (Index < 0 || Index >= this.RoutePoints_.Count)
                throw new ArgumentOutOfRangeException("Index");

            ValidateRoutePoint(NewPoint, "NewPoint");
            this.RoutePoints_[Index] = NewPoint;
            SynchronizeLegacyIntermediatePosition();
        }

        /// <summary>
        /// Removes one interior point.
        /// </summary>
        public void RemoveRoutePoint(int Index)
        {
            EnsureRoutePointsCollection();
            if (Index < 0 || Index >= this.RoutePoints_.Count)
                throw new ArgumentOutOfRangeException("Index");

            this.RoutePoints_.RemoveAt(Index);
            SynchronizeLegacyIntermediatePosition();
        }

        /// <summary>
        /// Translates the complete route by the supplied delta.
        /// </summary>
        public void TranslateRoutePoints(double DeltaX, double DeltaY)
        {
            EnsureRoutePointsCollection();
            if (double.IsNaN(DeltaX) || double.IsInfinity(DeltaX)
                || double.IsNaN(DeltaY) || double.IsInfinity(DeltaY))
                throw new ArgumentException("Connector route translation deltas must be finite.");

            if (DeltaX == 0.0 && DeltaY == 0.0)
                return;

            // Validate the entire translated route before mutating any undo-aware item.
            var Translated = ValidateRoutePoints(this.RoutePoints_.Select(Point =>
                                        new Point(Point.X + DeltaX, Point.Y + DeltaY)));
            for (int Index = 0; Index < this.RoutePoints_.Count; Index++)
                this.RoutePoints_[Index] = Translated[Index];

            SynchronizeLegacyIntermediatePosition();
        }

        /// <summary>
        /// Returns the complete path in origin-to-target order, including endpoint anchors.
        /// </summary>
        public IList<Point> GetPathPoints(bool UseEdgePositions = true)
        {
            EnsureRoutePointsCollection();

            var Origin = (UseEdgePositions && IsUsableRoutePoint(this.OriginEdgePosition)
                          ? this.OriginEdgePosition : this.OriginPosition);
            var Target = (UseEdgePositions && IsUsableRoutePoint(this.TargetEdgePosition)
                          ? this.TargetEdgePosition : this.TargetPosition);

            var Result = new List<Point>(this.RoutePoints.Count + 2) { Origin };
            Result.AddRange(this.RoutePoints);
            Result.Add(Target);
            return Result;
        }

        /// <summary>
        /// Removes duplicate and collinear interior points without changing the visible path.
        /// </summary>
        public void SimplifyRoutePoints(double Tolerance = 0.001)
        {
            var Points = GetPathPoints(false);
            if (Points.Count <= 2)
                return;

            var Simplified = new List<Point>();
            for (int Index = 1; Index < Points.Count - 1; Index++)
            {
                var Previous = (Simplified.Count == 0 ? Points[0] : Simplified[Simplified.Count - 1]);
                var Current = Points[Index];
                var Next = Points[Index + 1];

                if ((Current - Previous).Length <= Tolerance)
                    continue;

                var CrossProduct = ((Current.X - Previous.X) * (Next.Y - Current.Y)
                                    - (Current.Y - Previous.Y) * (Next.X - Current.X));
                var IncomingX = Current.X - Previous.X;
                var IncomingY = Current.Y - Previous.Y;
                var OutgoingX = Next.X - Current.X;
                var OutgoingY = Next.Y - Current.Y;
                var DirectionDotProduct = IncomingX * OutgoingX + IncomingY * OutgoingY;
                if (Math.Abs(CrossProduct) <= Tolerance && DirectionDotProduct >= 0.0)
                    continue;

                Simplified.Add(Current);
            }

            SetRoutePoints(Simplified);
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------------------------
        /// <summary>
        /// Clears the connector intermediate point.
        /// </summary>
        public void DoStraighten()
        {
            if (IsPartOfHiddenSimpleRelationship())
            {
                this.OwnerRelationshipRepresentation.DoStraighten();
                return;
            }

            this.EditEngine.StartCommandVariation("Straighten Connector");

            this.ClearRoutePoints();
            this.OwnerRelationshipRepresentation.Render();
            this.GetDisplayingView().UpdateVersion();

            this.EditEngine.CompleteCommandVariation();
        }

        /// <summary>
        /// Removes one bend as a single undoable command.
        /// </summary>
        public void DoRemoveRoutePoint(int Index)
        {
            if (this.RoutePoints == null || Index < 0 || Index >= this.RoutePoints.Count)
                return;

            this.EditEngine.StartCommandVariation("Remove Connector Bend");
            this.RemoveRoutePoint(Index);
            this.OwnerRelationshipRepresentation.Render();
            this.GetDisplayingView().UpdateVersion();
            this.EditEngine.CompleteCommandVariation();
        }

        /// <summary>
        /// Simplifies the route as a single undoable command.
        /// </summary>
        public void DoSimplifyRoute()
        {
            var LogicalConnectors = (IsPartOfHiddenSimpleRelationship()
                                     ? this.OwnerRelationshipRepresentation.VisualConnectors.Where(Connector => Connector != null).ToList()
                                     : this.IntoEnumerable().ToList());
            if (!LogicalConnectors.Any(Connector => Connector.RoutePoints.Count > 0))
                return;

            this.EditEngine.StartCommandVariation("Simplify Connector Route");
            foreach (var Connector in LogicalConnectors)
                Connector.SimplifyRoutePoints();
            this.OwnerRelationshipRepresentation.Render();
            this.GetDisplayingView().UpdateVersion();
            this.EditEngine.CompleteCommandVariation();
        }

        private bool IsPartOfHiddenSimpleRelationship()
        {
            if (this.OwnerRelationshipRepresentation == null
                || this.OwnerRelationshipRepresentation.MainSymbol == null
                || !this.OwnerRelationshipRepresentation.MainSymbol.IsHidden
                || this.OwnerRelationshipRepresentation.RepresentedRelationship == null)
                return false;

            var Definition = this.OwnerRelationshipRepresentation.RepresentedRelationship.RelationshipDefinitor.Value;
            return Definition.IsSimple && Definition.HideCentralSymbolWhenSimple;
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------------------------
        /// <summary>
        /// Cycle through the available link-role variants.
        /// </summary>
        public void DoCycleThroughVariants()
        {
            /* Old validation, which not allowed modification (derivated from Link-Role Variants definitions change)
            if (this.RepresentedLink.RoleDefinitor.AllowedVariants.Count <= 1)
            {
                Console.WriteLine("Cannot cycle through variants because only one is allowed for that link-role type.");
                // return;
            } */

            var OriginalIndex = this.RepresentedLink.RoleDefinitor.AllowedVariants.IndexOf(this.RepresentedLink.RoleVariant);
            var VariantIndex = (OriginalIndex < this.RepresentedLink.RoleDefinitor.AllowedVariants.Count - 1
                                ? OriginalIndex + 1 : 0);

            if (VariantIndex == OriginalIndex)
            {
                Console.WriteLine("Cannot cycle through variants because only one is allowed for that link-role type.");
                return;
            }

            this.EditEngine.StartCommandVariation("Cycle Through Variants of Connector");

            this.RepresentedLink.RoleVariant = this.RepresentedLink.RoleDefinitor.AllowedVariants[VariantIndex];

            this.RepresentedLink.UpdateVersion();
            this.RenderElement();

            this.EditEngine.CompleteCommandVariation();
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------------------------
        /// <summary>
        /// Edit the represented Link descriptor.
        /// </summary>
        public void DoEditDescriptor()
        {
            this.RepresentedLink.DoEditDescriptor(lnk => this.RenderElement());
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------------------------
        /// <summary>
        /// References the owning visual representator.
        /// </summary>
        public override VisualRepresentation OwnerRepresentation { get { return this.OwnerRelationshipRepresentation; } set { this.OwnerRelationshipRepresentation = (RelationshipVisualRepresentation)value; } }

        /// <summary>
        /// References the owning relationship visual representator.
        /// </summary>
        public RelationshipVisualRepresentation OwnerRelationshipRepresentation { get { return __OwnerRelationshipRepresentation.Get(this); } set { __OwnerRelationshipRepresentation.Set(this, value); } }
        protected RelationshipVisualRepresentation OwnerRelationshipRepresentation_;
        public static readonly ModelPropertyDefinitor<VisualConnector, RelationshipVisualRepresentation> __OwnerRelationshipRepresentation =
                   new ModelPropertyDefinitor<VisualConnector, RelationshipVisualRepresentation>("OwnerRelationshipRepresentation", EEntityMembership.External, true, EPropertyKind.Common, ins => ins.OwnerRelationshipRepresentation_, (ins, val) => ins.OwnerRelationshipRepresentation_ = val, false, false,
                                                                                                 "Owner Relationship Representation", "References the owning relationship visual representator.");

        // ---------------------------------------------------------------------------------------------------------------------------------------------------------
        /// <summary>
        /// References the represented Role Based Link.
        /// </summary>
        public RoleBasedLink RepresentedLink { get { return __RepresentedLink.Get(this); } internal set { __RepresentedLink.Set(this, value); } }
        protected RoleBasedLink RepresentedLink_ = null;
        public static readonly ModelPropertyDefinitor<VisualConnector, RoleBasedLink> __RepresentedLink =
                   new ModelPropertyDefinitor<VisualConnector, RoleBasedLink>("RepresentedLink", EEntityMembership.External, null, EPropertyKind.Common, ins => ins.RepresentedLink_, (ins, val) => ins.RepresentedLink_ = val, false, false,
                                                                              "Represented Link", "References the represented Role Based Link.");

        /// <summary>
        /// Source position of the connector.
        /// </summary>
        // IMPORTANT: Notice the return of symbol.BaseCenter when not populated.
        public Point OriginPosition { get { return __OriginPosition.Get(this)/*?.SubstituteFor(default(Point), (this.OriginSymbol == null ? Display.NULL_POINT : this.OriginSymbol.BaseCenter))*/; }
                             internal set { __OriginPosition.Set(this, value); } }
        protected Point OriginPosition_ = Display.NULL_POINT;
        public static readonly ModelPropertyDefinitor<VisualConnector, Point> __OriginPosition =
                   new ModelPropertyDefinitor<VisualConnector, Point>("OriginPosition", EEntityMembership.InternalCoreExclusive, null, EPropertyKind.Common, ins => ins.OriginPosition_, (ins, val) => ins.OriginPosition_ = val, false, false,
                                                                      "Origin Position", "Source position of the connector.");

        /// <summary>
        /// Source edge-position of the connector respect the source symbol.
        /// </summary>
        public Point OriginEdgePosition { get { return __OriginEdgePosition.Get(this); } internal set { __OriginEdgePosition.Set(this, value); } }
        protected Point OriginEdgePosition_ = Display.NULL_POINT;
        public static readonly ModelPropertyDefinitor<VisualConnector, Point> __OriginEdgePosition =
                   new ModelPropertyDefinitor<VisualConnector, Point>("OriginEdgePosition", EEntityMembership.InternalCoreExclusive, null, EPropertyKind.Common, ins => ins.OriginEdgePosition_, (ins, val) => ins.OriginEdgePosition_ = val, false, false,
                                                                      "Origin Edge-Position", "Source edge-position of the connector respect the source symbol.");

        /// <summary>
        /// Destination position of the connector.
        /// </summary>
        // IMPORTANT: Notice the return of symbol.BaseCenter when not populated.
        public Point TargetPosition { get { return __TargetPosition.Get(this)/*?.SubstituteFor(default(Point), (this.TargetSymbol == null ? Display.NULL_POINT : this.TargetSymbol.BaseCenter))*/; }
                             internal set { __TargetPosition.Set(this, value); } }
        protected Point TargetPosition_ = Display.NULL_POINT;
        public static readonly ModelPropertyDefinitor<VisualConnector, Point> __TargetPosition =
                   new ModelPropertyDefinitor<VisualConnector, Point>("TargetPosition", EEntityMembership.InternalCoreExclusive, null, EPropertyKind.Common, ins => ins.TargetPosition_, (ins, val) => ins.TargetPosition_ = val, false, false,
                                                                      "Target Position", "Destination position of the connector.");

        /// <summary>
        /// Destination edge-position of the connector respect the target symbol.
        /// </summary>
        public Point TargetEdgePosition { get { return __TargetEdgePosition.Get(this); } internal set { __TargetEdgePosition.Set(this, value); } }
        protected Point TargetEdgePosition_ = Display.NULL_POINT;
        public static readonly ModelPropertyDefinitor<VisualConnector, Point> __TargetEdgePosition =
                   new ModelPropertyDefinitor<VisualConnector, Point>("TargetEdgePosition", EEntityMembership.InternalCoreExclusive, null, EPropertyKind.Common, ins => ins.TargetEdgePosition_, (ins, val) => ins.TargetEdgePosition_ = val, false, false,
                                                                      "Target Edge-Position", "Destination edge-position of the connector respect the target symbol.");

        /// <summary>
        /// Symbol pointed by this Connector.
        /// </summary>
        public VisualSymbol TargetSymbol { get { return __TargetSymbol.Get(this); } internal set { __TargetSymbol.Set(this, value); } }
        protected VisualSymbol TargetSymbol_ = null;
        public static readonly ModelPropertyDefinitor<VisualConnector, VisualSymbol> __TargetSymbol =
                   new ModelPropertyDefinitor<VisualConnector, VisualSymbol>("TargetSymbol", EEntityMembership.External, null, EPropertyKind.Common, ins => ins.TargetSymbol_, (ins, val) => ins.TargetSymbol_ = val, false, false,
                                                                             "Target Symbol", "Symbol pointed by this Connector.");

        /// <summary>
        /// Symbol where this Connector originates.
        /// </summary>
        public VisualSymbol OriginSymbol { get { return __OriginSymbol.Get(this); } internal set { __OriginSymbol.Set(this, value); } }
        protected VisualSymbol OriginSymbol_ = null;
        public static readonly ModelPropertyDefinitor<VisualConnector, VisualSymbol> __OriginSymbol =
                   new ModelPropertyDefinitor<VisualConnector, VisualSymbol>("OriginSymbol", EEntityMembership.External, null, EPropertyKind.Common, ins => ins.OriginSymbol_, (ins, val) => ins.OriginSymbol_ = val, false, false,
                                                                             "Origin Symbol", "Symbol where this Connector originates.");

        /// <summary>
        /// Gets the connector intermediate point (if not empty/null) or the final origin position inside symbol.
        /// </summary>
        public Point OriginIntermediateOrFinalPosition
        { get{ return (this.RoutePoints != null && this.RoutePoints.Count > 0 ? this.RoutePoints[0] : this.OriginPosition); } }

        /// <summary>
        /// Gets the connector intermediate point (if not empty/null) or the final target position inside symbol.
        /// </summary>
        public Point TargetIntermediateOrFinalPosition
        { get{ return (this.RoutePoints != null && this.RoutePoints.Count > 0 ? this.RoutePoints[this.RoutePoints.Count - 1] : this.TargetPosition); } }

        /// <summary>
        /// Gets the connector Origin point, considering if Relationship is "Simple" and hidden, plus intermediate points of both connectors in that case.
        /// </summary>
        public Point FinalOriginPoint
        {
            get
            {
                var RelDef = this.OwnerRelationshipRepresentation.RepresentedRelationship.RelationshipDefinitor.Value;

                if (RelDef.IsSimple)
                    if (this.RoutePoints != null && this.RoutePoints.Count > 0)
                        return this.RoutePoints[0];
                    else
                        if (!this.OriginSymbol.IsAutoPositionable)
                            return this.OriginPosition;
                        else
                            if (this.OwnerRelationshipRepresentation.VisualConnectorsCount > 1)
                            {
                                var OppositeConnector = this.OwnerRelationshipRepresentation.VisualConnectors.FirstOrDefault(connector => connector != this);
                                if (OppositeConnector != null)
                                {
                                    if (OppositeConnector.RoutePoints != null && OppositeConnector.RoutePoints.Count > 0)
                                        return OppositeConnector.RoutePoints[OppositeConnector.RoutePoints.Count - 1];
                                    else
                                        return OppositeConnector.OriginPosition;
                                }

                                Console.WriteLine("JSON import warning: relationship connector '{0}' expected an opposite connector but none was available.", this.RepresentedLink);
                            }

                return this.OriginIntermediateOrFinalPosition;
            }
        }

        /// <summary>
        /// Gets the connector Target point, considering if Relationship is "Simple" and hidden, plus intermediate points of both connectors in that case.
        /// </summary>
        public Point FinalTargetPoint
        {
            get
            {
                var RelDef = this.OwnerRelationshipRepresentation.RepresentedRelationship.RelationshipDefinitor.Value;

                if (RelDef.IsSimple)
                    if (this.RoutePoints != null && this.RoutePoints.Count > 0)
                        return this.RoutePoints[this.RoutePoints.Count - 1];
                    else
                        if (!this.TargetSymbol.IsAutoPositionable)
                            return this.TargetPosition;
                        else
                            if (this.OwnerRelationshipRepresentation.VisualConnectorsCount > 1)
                            {
                                var OppositeConnector = this.OwnerRelationshipRepresentation.VisualConnectors.FirstOrDefault(connector => connector != this);
                                if (OppositeConnector != null)
                                {
                                    if (OppositeConnector.RoutePoints != null && OppositeConnector.RoutePoints.Count > 0)
                                        return OppositeConnector.RoutePoints[0];
                                    else
                                        return OppositeConnector.TargetPosition;
                                }

                                Console.WriteLine("JSON import warning: relationship connector '{0}' expected an opposite connector but none was available.", this.RepresentedLink);
                            }

                return this.TargetIntermediateOrFinalPosition;
            }
        }

        /// <summary>
        /// Gets the plug type code for the origin side.
        /// </summary>
        [Description("Gets the plug type code for the origin side.")]
        public string OriginPlug
        {
            get
            {
                var Result = Plugs.None;

                if (this.OriginSymbol == this.OwnerRelationshipRepresentation.MainSymbol)
                    return Result;

                var PlugsSource = this.OwnerRelationshipRepresentation.RepresentedRelationship.RelationshipDefinitor.Value.DefaultConnectorsFormat.TailPlugs;
                Result = PlugsSource.GetValueOrFirst(this.RepresentedLink.RoleVariant);

                return Result;
            }
        }

        /// <summary>
        /// Gets the plug type code for the target side.
        /// </summary>
        [Description("Gets the plug type code for the target side.")]
        public string TargetPlug
        {
            get
            {
                var Result = Plugs.None;

                if (this.TargetSymbol == this.OwnerRelationshipRepresentation.MainSymbol)
                    return Result;

                if (this.RepresentedLink.RoleDefinitor.OwnerRelationshipDef.IsDirectional)
                {
                    var PlugsSource = this.OwnerRelationshipRepresentation.RepresentedRelationship.RelationshipDefinitor.Value.DefaultConnectorsFormat.HeadPlugs;
                    Result = PlugsSource.GetValueOrFirst(this.RepresentedLink.RoleVariant);
                }
                else
                {
                    var PlugsSource = this.OwnerRelationshipRepresentation.RepresentedRelationship.RelationshipDefinitor.Value.DefaultConnectorsFormat.TailPlugs;
                    Result = PlugsSource.GetValueOrFirst(this.RepresentedLink.RoleVariant);
                }

                return Result;
            }
        }

        /// <summary>
        /// When targeting to a Relationship Main Symbol, returns the first target Symbol from that Relationship or null if none is targeted,
        /// else, when targeting to a Concept Symbol, returns that symbol. Returns null if there is no targeted symbol.
        /// </summary>
        public VisualSymbol PrimaryRelatedTargetSymbol
        {
            get
            {
                if (this.TargetSymbol == null)
                    return null;

                if (this.TargetSymbol.OwnerRepresentation is ConceptVisualRepresentation)
                    return this.TargetSymbol;

                var FirstConnection = this.TargetSymbol.TargetConnections.FirstOrDefault();
                if (FirstConnection == null)
                    return null;

                return FirstConnection.TargetSymbol;
            }
        }

        /// <summary>
        /// When originated from a Relationship Main Symbol, returns the first Origin Symbol from that Relationship or null if none is originated,
        /// else, when originating from a Concept Symbol, returns that Origin symbol. Returns null if there is no originated symbol.
        /// </summary>
        public VisualSymbol PrimaryRelatedOriginSymbol
        {
            get
            {
                if (this.OriginSymbol == null)
                    return null;

                if (this.OriginSymbol.OwnerRepresentation is ConceptVisualRepresentation)
                    return this.OriginSymbol;

                var FirstConnection = this.OriginSymbol.OriginConnections.FirstOrDefault();
                if (FirstConnection == null)
                    return null;

                return FirstConnection.OriginSymbol;
            }
        }

        /// <summary>
        /// Draws and returns a set of indicator adorners (drawing, is-main and manipulation-direction), based on supplied Indicator Size, Stroke, Pencil and optional Geometry-Creator, for mark the selection of this visual element.
        /// </summary>
        public override List<Tuple<Drawing, bool, EManipulationDirection>> GenerateSelectionIndicators(double IndicatorSize, Brush IndStroke, Pen IndPencil, Func<Rect, Geometry> GeometryCreator = null)
        {
            /*T if (GeometryCreator == null)
            {
                IndicatorSize = IndicatorSize * 3;
                IndStroke = Brushes.Transparent;
            } */

            GeometryCreator = GeometryCreator.NullDefault((rect) => new EllipseGeometry(rect));

            var StandardIndicators = new List<Tuple<Drawing, bool, EManipulationDirection>>();
            double PosX, PosY;
            Drawing IndOrigin = null, IndTarget = null;

            PosX = this.OriginEdgePosition.X - (IndicatorSize / 2.0);
            PosY = this.OriginEdgePosition.Y - (IndicatorSize / 2.0);
            IndOrigin = (new GeometryDrawing(IndStroke, IndPencil, GeometryCreator(new Rect(PosX, PosY, IndicatorSize, IndicatorSize))));
            StandardIndicators.Add(Tuple.Create(IndOrigin, true, EManipulationDirection.TopLeft)); // Note: The Tuple Items 1 and 2 (is-main and manipulation-direction) are not currently used in the Connectors context

            PosX = this.TargetEdgePosition.X - (IndicatorSize / 2.0);
            PosY = this.TargetEdgePosition.Y - (IndicatorSize / 2.0);
            IndTarget = (new GeometryDrawing(IndStroke, IndPencil, GeometryCreator(new Rect(PosX, PosY, IndicatorSize, IndicatorSize))));
            StandardIndicators.Add(Tuple.Create(IndTarget, true, EManipulationDirection.BottomRight)); // Note: The Tuple Items 1 and 2 (is-main and manipulation-direction) are not currently used in the Connectors context

            if (this.RoutePoints != null)
                foreach (var RoutePoint in this.RoutePoints)
                {
                    PosX = RoutePoint.X - (IndicatorSize / 2.0);
                    PosY = RoutePoint.Y - (IndicatorSize / 2.0);
                    var RouteIndicator = (new GeometryDrawing(IndStroke, IndPencil, GeometryCreator(new Rect(PosX, PosY, IndicatorSize, IndicatorSize))));
                    StandardIndicators.Add(Tuple.Create((Drawing)RouteIndicator, false, EManipulationDirection.Top));
                }

            return StandardIndicators;
        }

        /// <summary>
        /// Ordered interior points of the connector, from origin to target. Callers receive a
        /// read-only view so every mutation goes through the validating, undo-aware route APIs.
        /// </summary>
        public IList<Point> RoutePoints
        {
            get
            {
                EnsureRoutePointsCollection();
                return this.RoutePointsView_;
            }
        }

        [OptionalField(VersionAdded = 2)]
        private EditableList<Point> RoutePoints_;

        [NonSerialized]
        private ReadOnlyCollection<Point> RoutePointsView_;

        public static ModelListDefinitor<VisualConnector, Point> __RoutePoints =
                   new ModelListDefinitor<VisualConnector, Point>("RoutePoints", EEntityMembership.InternalCoreExclusive,
                                                                  ins => ins.RoutePoints_, (ins, coll) => ins.SetRoutePointsStorage(coll),
                                                                  "Route Points", "Ordered interior route points of the connector.");

        /// <summary>
        /// Legacy singleton intermediate position facade. A multi-point route has no
        /// lossless singleton representation and therefore returns Display.NULL_POINT.
        /// </summary>
        [Obsolete("Use RoutePoints and the route editing APIs.")]
        public Point IntermediatePosition
        {
            get
            {
                if (this.RoutePoints != null)
                    return (this.RoutePoints.Count == 1 ? this.RoutePoints[0] : Display.NULL_POINT);

                return this.IntermediatePosition_;
            }
            internal set
            {
                if (value == Display.NULL_POINT)
                    ClearRoutePoints();
                else
                    SetRoutePoints(value.IntoEnumerable());
            }
        }
        protected Point IntermediatePosition_ = Display.NULL_POINT;
        public static readonly ModelPropertyDefinitor<VisualConnector, Point> __IntermediatePosition =
                   new ModelPropertyDefinitor<VisualConnector, Point>("IntermediatePosition", EEntityMembership.External, null, EPropertyKind.Common, ins => ins.IntermediatePosition_, (ins, val) => ins.IntermediatePosition_ = val, false, false,
                                                                      "Intermediate Position", "Intermediate optional position of the connector.");

        private void EnsureRoutePointsCollection()
        {
            if (this.RoutePoints_ == null)
                SetRoutePointsStorage(new EditableList<Point>(__RoutePoints.TechName, this, 4));
            else if (this.RoutePointsView_ == null)
                this.RoutePointsView_ = new ReadOnlyCollection<Point>(this.RoutePoints_);
        }

        private void SetRoutePointsStorage(EditableList<Point> Storage)
        {
            if (Storage == null)
                Storage = new EditableList<Point>(__RoutePoints.TechName, this, 4);

            ValidateRoutePoints(Storage);
            this.RoutePoints_ = Storage;
            this.RoutePointsView_ = new ReadOnlyCollection<Point>(this.RoutePoints_);
        }

        private void SynchronizeLegacyIntermediatePosition()
        {
            this.IntermediatePosition_ = (this.RoutePoints_ != null && this.RoutePoints_.Count == 1
                                          ? this.RoutePoints_[0] : Display.NULL_POINT);
        }

        private static List<Point> ValidateRoutePoints(IEnumerable<Point> Points)
        {
            var Result = (Points == null ? new List<Point>() : Points.ToList());
            if (Result.Count > MAX_ROUTE_POINTS)
                throw new ArgumentOutOfRangeException("Points", "A connector route cannot contain more than " + MAX_ROUTE_POINTS + " interior points.");

            for (int Index = 0; Index < Result.Count; Index++)
                ValidateRoutePoint(Result[Index], "Points[" + Index + "]");

            return Result;
        }

        private static void ValidateRoutePoint(Point Point, string ParameterName)
        {
            if (!IsUsableRoutePoint(Point))
                throw new ArgumentException("A connector route point must contain finite coordinates and cannot be Display.NULL_POINT.", ParameterName);
        }

        private static bool IsUsableRoutePoint(Point Point)
        {
            return (Point != Display.NULL_POINT
                    && !double.IsNaN(Point.X) && !double.IsInfinity(Point.X)
                    && !double.IsNaN(Point.Y) && !double.IsInfinity(Point.Y));
        }

        private static Point FindNextDistinctPoint(Point Endpoint, IEnumerable<Point> RoutePoints, Point OppositeEndpoint)
        {
            foreach (var RoutePoint in RoutePoints ?? Enumerable.Empty<Point>())
                if (RoutePoint != Endpoint)
                    return RoutePoint;

            return OppositeEndpoint;
        }

        private static Point FindPreviousDistinctPoint(Point Endpoint, IEnumerable<Point> RoutePoints, Point OppositeEndpoint)
        {
            var Points = (RoutePoints ?? Enumerable.Empty<Point>()).ToList();
            for (int Index = Points.Count - 1; Index >= 0; Index--)
                if (Points[Index] != Endpoint)
                    return Points[Index];

            return OppositeEndpoint;
        }

        private static Point DetermineLabelPosition(IList<Point> PathPoints)
        {
            if (PathPoints == null || PathPoints.Count < 2)
                return default(Point);

            var Lengths = new double[PathPoints.Count - 1];
            var TotalLength = 0.0;
            for (int Index = 0; Index < Lengths.Length; Index++)
            {
                Lengths[Index] = (PathPoints[Index + 1] - PathPoints[Index]).Length;
                TotalLength += Lengths[Index];
            }

            var BestIndex = 0;
            var BestLength = -1.0;
            var BestMidpointDistance = double.MaxValue;
            var Traversed = 0.0;
            var PathMidpoint = TotalLength / 2.0;

            for (int Index = 0; Index < Lengths.Length; Index++)
            {
                var SegmentMidpoint = Traversed + Lengths[Index] / 2.0;
                var MidpointDistance = Math.Abs(SegmentMidpoint - PathMidpoint);
                if (Lengths[Index] > BestLength + 0.001
                    || (Math.Abs(Lengths[Index] - BestLength) <= 0.001 && MidpointDistance < BestMidpointDistance))
                {
                    BestIndex = Index;
                    BestLength = Lengths[Index];
                    BestMidpointDistance = MidpointDistance;
                }

                Traversed += Lengths[Index];
            }

            return new Point((PathPoints[BestIndex].X + PathPoints[BestIndex + 1].X) / 2.0,
                             (PathPoints[BestIndex].Y + PathPoints[BestIndex + 1].Y) / 2.0);
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------------------------
        /// <summary>
        /// Gets the current content area, considering the origin, target and intermediate points.
        /// </summary>
        public override Rect TotalArea
        {
            get
            {
                double LeftLimit = this.OriginPosition.X;
                double RightLimit = this.OriginPosition.X;
                double TopLimit = this.OriginPosition.Y;
                double BottomLimit = this.OriginPosition.Y;
                
                LeftLimit = Math.Min(LeftLimit, this.TargetPosition.X);
                RightLimit = Math.Max(RightLimit, this.TargetPosition.X);
                TopLimit = Math.Min(TopLimit, this.TargetPosition.Y);
                BottomLimit = Math.Max(BottomLimit, this.TargetPosition.Y);

                if (this.RoutePoints != null)
                    foreach (var RoutePoint in this.RoutePoints)
                    {
                        LeftLimit = Math.Min(LeftLimit, RoutePoint.X);
                        RightLimit = Math.Max(RightLimit, RoutePoint.X);
                        TopLimit = Math.Min(TopLimit, RoutePoint.Y);
                        BottomLimit = Math.Max(BottomLimit, RoutePoint.Y);
                    }

                var Result = new Rect(LeftLimit, TopLimit, (RightLimit - LeftLimit) + 1.0, (BottomLimit - TopLimit) + 1.0);
                return Result;
            }
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------------------------
        /// <summary>
        /// Indicates whether this object can be moved.
        /// </summary>
        public override bool CanMove { get { return false; } }  // Move is not provided as by rectangle basis.

        /// <summary>
        /// Indicates whether this object can be resized.
        /// </summary>
        public override bool CanResize { get { return false; } }    // Resize is not provided as by rectangle basis.

        /// Center point of the object.
        /// </summary>
        public override Point BaseCenter { get { return default(Point); } set { } }

        /// <summary>
        /// Top X-coordinate of the object.
        /// </summary>
        public override double BaseTop { get { return 0; } set { } }

        /// <summary>
        /// Left Y-coordinate of the object.
        /// </summary>
        public override double BaseLeft { get { return 0; } set { } }

        /// <summary>
        /// Width of the object.
        /// </summary>
        public override double BaseWidth { get { return 0; } set { } }

        /// <summary>
        /// Height of the object.
        /// </summary>
        public override double BaseHeight { get { return 0; } set { } }

        /// <summary>
        /// Area of the figure.
        /// </summary>
        public override Rect BaseArea { get { return this.TotalArea; } }

        /// <summary>
        /// Gets the movable pieces which this visual-object considers as visually united, plus indication of being contained within a region.
        /// </summary>
        // MUST RETURN NOTHING FOR CONNECTORS, BECAUSE THEY ARE INDIRECTLY MOVED.
        public override IEnumerable<Tuple<VisualObject,bool>> GetMovableMembers(bool IncludeRelatedOrigins, bool IncludeRelatedTargets, bool IsForVisualization)
        {
            return Enumerable.Empty<Tuple<VisualObject,bool>>();
        }

        /// <summary>
        /// Moves the object to the specified coordinates.
        /// </summary>
        // MUST DO NOTHING, BECAUSE CONNECTORS ARE INDIRECTLY MOVED.
        public override void MoveTo(double PosX, double PosY, bool LockNewPosition = false, bool IsResizing = false) { }

        /// <summary>
        /// Resizes the object to the specified dimensions.
        /// Returns indication of valid resizing respect the minimum allowed.
        /// </summary>
        public override bool ResizeTo(double Width, double Height) { return false; }
        
        // ---------------------------------------------------------------------------------------------------------------------------------------------------------
        #region IModelClass<VisualConnector> Members

        public new MModelClassDefinitor ClassDefinition { get { return __ClassDefinitor; } }
        public new ModelClassDefinitor<VisualConnector> ClassDefinitor { get { return __ClassDefinitor; } }
        public static readonly new ModelClassDefinitor<VisualConnector> __ClassDefinitor = null;

        public override object CreateCopy(ECloneOperationScope CloningScope, IMModelClass DirectOwner) { return this.CreateClone(CloningScope, DirectOwner); }
        public new VisualConnector CreateClone(ECloneOperationScope CloningScope, IMModelClass DirectOwner, bool AsActive = true) { return this.ClassDefinitor.PopulateInstance((VisualConnector)this.MemberwiseClone(), this, DirectOwner, CloningScope, true, AsActive); }
        public VisualConnector PopulateFrom(VisualConnector SourceElement, IMModelClass DirectOwner = null, ECloneOperationScope CloningScope = ECloneOperationScope.Slight, params string[] MemberNames) { return this.ClassDefinitor.PopulateInstance(this, SourceElement, DirectOwner, CloningScope, false, true, MemberNames); }

        #endregion

        public override string ToString()
        {
            var Origin = (this.OriginSymbol == null ? "<Empty>" : this.OriginSymbol.OwnerRepresentation.RepresentedIdea.ToString());
            var Target = (this.TargetSymbol == null ? "<Empty>" : this.TargetSymbol.OwnerRepresentation.RepresentedIdea.ToString());
            var OwnerRep = (this.OwnerRepresentation == null ? "<Empty>" : this.OwnerRepresentation.RepresentedIdea.Name);

            return "Connector of '" + OwnerRep + "', from [" + Origin + "] to [" + Target + "]"; //T ", Id=" + this.GlobalId.ToString() + ".";
        }

    }
}
