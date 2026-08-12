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
// File   : ViewConnectorManipulationAdorner.cs
// Object : Instrumind.ThinkComposer.Composer.ComposerUI.ViewConnectorManipulationAdorner (Class)
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
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

using Instrumind.Common;
using Instrumind.Common.Visualization;

using Instrumind.ThinkComposer.ApplicationProduct;
using Instrumind.ThinkComposer.Definitor.DefinitorUI;
using Instrumind.ThinkComposer.MetaModel.VisualMetaModel;
using Instrumind.ThinkComposer.Model.VisualModel;

/// Provides the user-interface components for the Composition Composer.
namespace Instrumind.ThinkComposer.Composer.ComposerUI
{
    /// <summary>
    /// Presents visual cues for manipulating view connector elements.
    /// </summary>
    public class ViewConnectorManipulationAdorner : ViewManipulationAdorner
    {
        public const double INDICATOR_SIZE = 5;
        public const double ACTIONER_SIZE = 20;
        public const double MANIPULATING_CONNECTOR_WIDTH = 3.0; // 8.0 This should not be wider until resolve "alternate" pointing problem

        public VisualConnector ManipulatedConnector { get { return this.ManipulatedObject as VisualConnector; } }
        public Point ManipulationAlternatePosition;
        public Point ManipulConnDisplacingPos;
        public Point ManipulConnRelinkingPos;

        /// <summary>
        /// Index of the bend currently being dragged, or -1 when a segment handle is active.
        /// </summary>
        public int ManipulatedRoutePointIndex { get; internal set; }

        /// <summary>
        /// Origin-to-target segment index currently being dragged. Inserting on segment N
        /// creates route point N.
        /// </summary>
        public int ManipulatedSegmentIndex { get; internal set; }

        /// <summary>
        /// Connector which owns the active bend/segment. For a hidden simple Relationship this
        /// can be the opposite leg, while ManipulatedConnector remains the selected visual.
        /// </summary>
        public VisualConnector ManipulatedRouteConnector { get; internal set; }

        private sealed class RouteHandleBinding
        {
            public VisualConnector Connector;
            public int Index;
        }

        private readonly Dictionary<DrawingVisual, RouteHandleBinding> RoutePointIndicators =
            new Dictionary<DrawingVisual, RouteHandleBinding>();
        private readonly Dictionary<DrawingVisual, RouteHandleBinding> SegmentIndicators =
            new Dictionary<DrawingVisual, RouteHandleBinding>();
        private readonly Dictionary<DrawingVisual, VisualConnector> ConnectorPathIndicators =
            new Dictionary<DrawingVisual, VisualConnector>();

        public EConnectorManipulationAction IntendedAction { get { return (EConnectorManipulationAction)this.IntendedAction_; } set { this.IntendedAction_ = (byte)value; } }
        public EConnectorManipulationAction TentativeAction { get { return (EConnectorManipulationAction)this.TentativeAction_; } set { this.TentativeAction_ = (byte)value; } }

        Pen FrmPencil = new Pen(Brushes.LightCyan, 0);
        Brush FrmStroke = Brushes.Yellow.Clone();
        Brush FrmStrokeEdit = Brushes.Goldenrod;
        Brush FrmStrokeUnpointed = Brushes.LightGray;

        Pen ActPencil = new Pen(Brushes.Blue, 1);
        Brush ActStroke = Brushes.Yellow.Clone();

        Pen IndPencil = new Pen(Brushes.Blue, 1);
        Brush IndStroke = Brushes.White;

        internal DrawingVisual RelinkActionTargetIndicator = null;
        internal DrawingVisual RelinkActionOriginIndicator = null;

        public DrawingVisual IndOriginPoint { get; protected set; }
        public DrawingVisual IndTargetPoint { get; protected set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public ViewConnectorManipulationAdorner(ViewManipulationManager OwnerManager, VisualConnector WorkingConnector,
                                                AdornerLayer WorkingLayer, Action<ViewManipulationAdorner, bool, bool, bool, bool, bool, bool, bool, bool> ManipulationOperation)
             : base(OwnerManager, WorkingConnector, WorkingLayer)
        {
            this.TentativeAction_ = (byte)EConnectorManipulationAction.Displace;
            this.DefaultAction_ = (byte)EConnectorManipulationAction.Displace;

            this.ManipulationOperation = ManipulationOperation;

            this.ManipulatedRoutePointIndex = -1;
            this.ManipulatedSegmentIndex = -1;
            this.ManipulatedRouteConnector = WorkingConnector;

            var PathPoints = WorkingConnector.GetPathPoints();
            var OriginEdgePoint = PathPoints[0];
            var TargetEdgePoint = PathPoints[PathPoints.Count - 1];

            var ViewPosition = Mouse.GetPosition(this.OwnerManager.OwnerView.Presenter);
            var DistanceToOrigin = (ViewPosition - OriginEdgePoint).Length;
            var DistanceToTarget = (ViewPosition - TargetEdgePoint).Length;

            this.ManipulationAlternatePosition = (WorkingConnector.OriginSymbol == WorkingConnector.OwnerRelationshipRepresentation.MainSymbol
                                                  ? WorkingConnector.OriginPosition : WorkingConnector.TargetPosition);

            Point ClosestPoint;
            this.ManipulatedSegmentIndex = FindNearestSegment(PathPoints, ViewPosition, out ClosestPoint);
            this.ManipulConnDisplacingPos = ClosestPoint;

            if (IsHiddenSimpleRelationship(WorkingConnector))
            {
                var OppositeConnector = GetOppositeConnector(WorkingConnector);
                if (OppositeConnector != null)
                {
                    Point OppositeClosestPoint;
                    var OppositeSegment = FindNearestSegment(GetConnectorPathPoints(OppositeConnector), ViewPosition,
                                                             out OppositeClosestPoint);
                    if ((ViewPosition - OppositeClosestPoint).Length < (ViewPosition - ClosestPoint).Length)
                    {
                        this.ManipulatedRouteConnector = OppositeConnector;
                        this.ManipulatedSegmentIndex = OppositeSegment;
                        this.ManipulConnDisplacingPos = OppositeClosestPoint;
                    }
                }
            }
            //T Console.WriteLine("ManConnDisPos=" + this.ManipulConnDisplacingPos + "     at " + DateTime.Now.Ticks);

            if (DistanceToOrigin < DistanceToTarget)
            {
                this.WorkingOnOrigin = true;
                this.ManipulConnRelinkingPos = OriginEdgePoint;
            }
            else
            {
                this.WorkingOnOrigin = false;
                this.ManipulConnRelinkingPos = TargetEdgePoint;
            }

            this.ActStroke.Opacity = 0.1;
        }

        public bool WorkingOnOrigin { get; private set; }

        //------------------------------------------------------------------------------------------------------------------------
        public override void Visualize(bool Show = true, bool OnlyAdornAsSelected = false)
        {
            this.AlternateActions.Clear();
            this.RoutePointIndicators.Clear();
            this.SegmentIndicators.Clear();
            this.ConnectorPathIndicators.Clear();
            this.ClearAllIndicators();
            //T Console.WriteLine("Visualizing 111 ..." + DateTime.Now.Ticks);

            // Validate that the Adorner still points something.
            // Else, maybe an "Undo" was performed, so the Represented-Idea may not exist anymore.
            if (this.ManipulatedConnector == null || this.ManipulatedConnector.OwnerRepresentation == null)
            {
                if (this.ManipulatedConnector != null)
                    this.OwnerManager.RemoveAdorners();

                this.OwnerManager.OwnerView.UnselectAllObjects();

                return;
            } 
            
            if (!Show)
                return;

            //T Console.WriteLine("Visualizing 222 ..." + DateTime.Now.Ticks);
            //T Console.WriteLine("Showing connector manipulation adorner.");

            var PaintBrush = FrmStroke;
            PaintBrush.Opacity = 0.5;

            var RelDef = this.ManipulatedConnector.OwnerRelationshipRepresentation.RepresentedRelationship.RelationshipDefinitor.Value;

            if (this.IntendedAction == EConnectorManipulationAction.ReLink)
            {
                var ConnFormat = RelDef.DefaultConnectorsFormat;

                Point PosTarget, PosOrigin;

                var ViewPosition = this.MousePositionCurrent = Mouse.GetPosition(this.OwnerManager.OwnerView.Presenter);

                if (!this.WorkingOnOrigin)
                {
                    PosTarget = this.ManipulConnRelinkingPos;
                    PosOrigin = this.ManipulatedConnector.FinalOriginPoint;
                }
                else
                {
                    PosTarget = this.ManipulatedConnector.FinalTargetPoint;
                    PosOrigin = this.ManipulConnRelinkingPos;
                }

                var RelinkingConnector = MasterDrawer.CreateDrawingConnector(Plugs.None, Plugs.SimpleArrow,
                                                                             ConnFormat.LineBrush, ConnFormat.LineThickness,
                                                                             ConnFormat.LineDash, ConnFormat.LineJoin,
                                                                             ConnFormat.LineCap, ConnFormat.PathStyle,
                                                                             ConnFormat.PathCorner, ConnFormat.MainBackground,
                                                                             ConnFormat.Opacity,
                                                                             PosTarget,
                                                                             PosOrigin).RenderToDrawingVisual();

                this.Indicators.Insert(0, RelinkingConnector);
                //T Console.WriteLine("Visualizing 333 ..." + DateTime.Now.Ticks);
                this.RefreshAdorner();
                return;
            }

            //T Console.WriteLine("Visualizing 444 ..." + DateTime.Now.Ticks);
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                this.CurrentManipulationAction = EConnectorManipulationAction.Remove;
            else
                if ((Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                     && (this.ManipulatedConnector.RoutePoints.Count > 0
                         || (RelDef.IsSimple && RelDef.HideCentralSymbolWhenSimple)))
                    this.CurrentManipulationAction = EConnectorManipulationAction.StraightenLine;
                else
                    if ((Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
                        /*? && this.ManipulatedConnector.RepresentedLink.RoleDefinitor.AllowedVariants.Count > 1 */)
                        this.CurrentManipulationAction = EConnectorManipulationAction.CycleThroughVariants;
                    else
                        this.CurrentManipulationAction = EConnectorManipulationAction.Displace;

            /* Remove: problematic in autoref? */
            var PosX = this.ManipulatedConnector.OriginPosition.X - INDICATOR_SIZE / 2.0;
            var PosY = this.ManipulatedConnector.OriginPosition.Y - INDICATOR_SIZE / 2.0;
            this.IndOriginPoint = (new GeometryDrawing(IndStroke, IndPencil, new RectangleGeometry(new Rect(PosX, PosY, INDICATOR_SIZE, INDICATOR_SIZE)))).RenderToDrawingVisual();
            Indicators.Add(this.IndOriginPoint);    // Origin must be first

            PosX = this.ManipulatedConnector.TargetPosition.X - INDICATOR_SIZE / 2.0;
            PosY = this.ManipulatedConnector.TargetPosition.Y - INDICATOR_SIZE / 2.0;
            this.IndTargetPoint = (new GeometryDrawing(IndStroke, IndPencil, new RectangleGeometry(new Rect(PosX, PosY, INDICATOR_SIZE, INDICATOR_SIZE)))).RenderToDrawingVisual();
            Indicators.Add(this.IndTargetPoint);    // Origin must be last

            // Determine whether exposition of adorner for a hidden relationship's Central/Main-Symbol (which may be reccently deleted) is needed
            if (this.ManipulatedConnector.OwnerRelationshipRepresentation.MainSymbol != null
                && this.ManipulatedConnector.OwnerRelationshipRepresentation.MainSymbol.IsHidden)
            {
                PosX = this.ManipulationAlternatePosition.X - ACTIONER_SIZE / 2.0;
                PosY = this.ManipulationAlternatePosition.Y - ACTIONER_SIZE / 2.0;
                var AltActionIndicator = CreateActioner(PosX, PosY, this.CurrentManipulationAction, false, Brushes.Red);
                this.AlternateActions.Add(AltActionIndicator);
                this.ManipulatedAlternateObject = this.ManipulatedConnector.OwnerRelationshipRepresentation.MainSymbol;

                this.Indicators.Add(AltActionIndicator);
                this.ExclusivePointingIndicators.Add(AltActionIndicator);
            }

            // Draw lines before other visuals
            var PencilYellow = new Pen(PaintBrush, MANIPULATING_CONNECTOR_WIDTH);
            PencilYellow.DashCap = PenLineCap.Round;
            PencilYellow.StartLineCap = PenLineCap.Round;
            PencilYellow.EndLineCap = PenLineCap.Round;

            var CommonPoint = this.ManipulConnDisplacingPos;

            // PENDING: Solve ungly misplaced indicator at the previous intermediate-position
            if (RelDef.IsSimple && RelDef.HideCentralSymbolWhenSimple
                && this.ManipulatedRouteConnector.RoutePoints.Count == 0
                && this.IsWorkingOnAlternateTarget)
            {
                var RouteEndpoints = GetConnectorPathPoints(this.ManipulatedRouteConnector);
                CommonPoint = RouteEndpoints[0].DetermineCenterRespect(RouteEndpoints[RouteEndpoints.Count - 1]);
            }

            //T Console.WriteLine("Visualizing Compoint=" + CommonPoint.ToString() + ". NEW  At " + DateTime.Now.Ticks);

            if (this.CurrentManipulationAction != EConnectorManipulationAction.Displace)
            {
                this.DefaultActionIndicator = CreateActioner(CommonPoint.X - ACTIONER_SIZE / 2.0, CommonPoint.Y - ACTIONER_SIZE / 2.0,
                                                             this.CurrentManipulationAction, true, Brushes.Blue);
                this.Indicators.Add(this.DefaultActionIndicator);
                this.ExclusivePointingIndicators.Add(this.DefaultActionIndicator);
            }
            else
                this.DefaultActionIndicator = null;

            AddConnectorRouteVisualization(this.ManipulatedConnector, PencilYellow, PaintBrush);

            // A hidden simple Relationship is exposed as one logical editable path. Both legs
            // receive indexed bend and segment handles; each handle retains its owning connector
            // and local index so edits are persisted in the correct origin-to-target collection.
            if (RelDef.IsSimple && RelDef.HideCentralSymbolWhenSimple)
            {
                var OppositeConnector = GetOppositeConnector(this.ManipulatedConnector);
                if (OppositeConnector != null)
                    AddConnectorRouteVisualization(OppositeConnector, PencilYellow, PaintBrush);
            }

            // Indicators for re-linking
            if (this.CurrentManipulationAction != EConnectorManipulationAction.CycleThroughVariants
                && ((!(RelDef.IsSimple && RelDef.HideCentralSymbolWhenSimple)
                       && (RelDef.PreciseConnectByDefault || (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))))
                    || this.ManipulatedConnector.TargetSymbol != this.ManipulatedConnector.OwnerRelationshipRepresentation.MainSymbol))
            {
                this.RelinkActionTargetIndicator = CreateActioner(this.ManipulatedConnector.TargetEdgePosition.X - ACTIONER_SIZE / 2.0,
                                                                    this.ManipulatedConnector.TargetEdgePosition.Y - ACTIONER_SIZE / 2.0,
                                                                    EConnectorManipulationAction.ReLink,
                                                                    this.ManipulatedConnector.TargetSymbol == this.ManipulatedConnector.OwnerRelationshipRepresentation.MainSymbol,
                                                                    Brushes.Orange);
                this.Indicators.Add(this.RelinkActionTargetIndicator);
            }

            if (this.CurrentManipulationAction != EConnectorManipulationAction.CycleThroughVariants
                && ((!(RelDef.IsSimple && RelDef.HideCentralSymbolWhenSimple)
                       && (RelDef.PreciseConnectByDefault || (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))))
                    || this.ManipulatedConnector.OriginSymbol != this.ManipulatedConnector.OwnerRelationshipRepresentation.MainSymbol))
            {
                this.RelinkActionOriginIndicator = CreateActioner(this.ManipulatedConnector.OriginEdgePosition.X - ACTIONER_SIZE / 2.0,
                                                                    this.ManipulatedConnector.OriginEdgePosition.Y - ACTIONER_SIZE / 2.0,
                                                                    EConnectorManipulationAction.ReLink,
                                                                    this.ManipulatedConnector.OriginSymbol == this.ManipulatedConnector.OwnerRelationshipRepresentation.MainSymbol,
                                                                    Brushes.Green);
                this.Indicators.Add(this.RelinkActionOriginIndicator);
            }

            //T Console.WriteLine("Visualizing 555 ..." + DateTime.Now.Ticks);

            // Needed in order to show this adorner's indicators on top of a potentially selected visual element
            this.RefreshAdorner();
        }

        private void AddConnectorRouteVisualization(VisualConnector Connector, Pen Pencil, Brush PaintBrush)
        {
            if (Connector == null)
                return;

            var PreviewRoutePoints = Connector.RoutePoints.ToList();
            if (this.IsManipulating && this.IntendedAction == EConnectorManipulationAction.Displace
                && !this.IsWorkingOnAlternateTarget
                && Connector == this.ManipulatedRouteConnector
                && !this.MousePositionCurrent.IsNear(this.PointedLocationWhileClicking))
            {
                if (this.ManipulatedRoutePointIndex >= 0 && this.ManipulatedRoutePointIndex < PreviewRoutePoints.Count)
                    PreviewRoutePoints[this.ManipulatedRoutePointIndex] = this.ManipulConnDisplacingPos;
                else if (this.ManipulatedSegmentIndex >= 0 && PreviewRoutePoints.Count < VisualConnector.MAX_ROUTE_POINTS)
                    PreviewRoutePoints.Insert(Math.Min(this.ManipulatedSegmentIndex, PreviewRoutePoints.Count),
                                              this.ManipulConnDisplacingPos);
            }

            var CompletePreviewPath = GetConnectorPathPoints(Connector, PreviewRoutePoints);
            var PreviewPath = PathDrawer.CreatePath(EPathStyle.MultilineFreeAngled, EPathCorner.Sharp,
                                                    Pencil, PaintBrush,
                                                    CompletePreviewPath[CompletePreviewPath.Count - 1],
                                                    CompletePreviewPath[0], PreviewRoutePoints)
                                        .RenderToDrawingVisual();
            this.Indicators.Insert(0, PreviewPath);
            this.ConnectorPathIndicators[PreviewPath] = Connector;
            this.ExclusivePointingIndicators.Add(PreviewPath);

            // Bend handles are solid squares. Segment handles are smaller diamonds and
            // insert a new point when dragged. Indices always match the owning connector's
            // persisted origin-to-target RoutePoints collection.
            for (int Index = 0; Index < PreviewRoutePoints.Count; Index++)
            {
                var RouteHandle = CreateRouteHandle(PreviewRoutePoints[Index], false);
                this.RoutePointIndicators.Add(RouteHandle, new RouteHandleBinding
                {
                    Connector = Connector,
                    Index = Index
                });
                this.Indicators.Add(RouteHandle);
                this.ExclusivePointingIndicators.Add(RouteHandle);
            }

            for (int Index = 0; Index < CompletePreviewPath.Count - 1; Index++)
            {
                var SegmentCenter = CompletePreviewPath[Index].DetermineCenterRespect(CompletePreviewPath[Index + 1]);
                var SegmentHandle = CreateRouteHandle(SegmentCenter, true);
                this.SegmentIndicators.Add(SegmentHandle, new RouteHandleBinding
                {
                    Connector = Connector,
                    Index = Index
                });
                this.Indicators.Add(SegmentHandle);
                this.ExclusivePointingIndicators.Add(SegmentHandle);
            }
        }

        private IList<Point> GetConnectorPathPoints(VisualConnector Connector, IEnumerable<Point> RoutePoints = null)
        {
            var MainSymbol = Connector.OwnerRelationshipRepresentation.MainSymbol;
            var Origin = (Connector.OriginSymbol == MainSymbol
                          ? this.ManipulationAlternatePosition : Connector.OriginPosition);
            var Target = (Connector.TargetSymbol == MainSymbol
                          ? this.ManipulationAlternatePosition : Connector.TargetPosition);
            var Result = new List<Point> { Origin };
            Result.AddRange(RoutePoints ?? Connector.RoutePoints);
            Result.Add(Target);
            return Result;
        }

        private static bool IsHiddenSimpleRelationship(VisualConnector Connector)
        {
            if (Connector == null || Connector.OwnerRelationshipRepresentation == null
                || Connector.OwnerRelationshipRepresentation.MainSymbol == null
                || !Connector.OwnerRelationshipRepresentation.MainSymbol.IsHidden)
                return false;

            var Definition = Connector.OwnerRelationshipRepresentation.RepresentedRelationship.RelationshipDefinitor.Value;
            return Definition.IsSimple && Definition.HideCentralSymbolWhenSimple;
        }

        private static VisualConnector GetOppositeConnector(VisualConnector Connector)
        {
            if (!IsHiddenSimpleRelationship(Connector))
                return null;

            return Connector.OwnerRelationshipRepresentation.VisualConnectors
                            .FirstOrDefault(Candidate => Candidate != null && Candidate != Connector);
        }

        private DrawingVisual CreateRouteHandle(Point Center, bool IsSegmentHandle)
        {
            var Size = (IsSegmentHandle ? 8.0 : 11.0);
            var Half = Size / 2.0;
            Geometry Shape;

            if (IsSegmentHandle)
            {
                var Diamond = new StreamGeometry();
                using (var Context = Diamond.Open())
                {
                    Context.BeginFigure(new Point(Center.X, Center.Y - Half), true, true);
                    Context.LineTo(new Point(Center.X + Half, Center.Y), true, false);
                    Context.LineTo(new Point(Center.X, Center.Y + Half), true, false);
                    Context.LineTo(new Point(Center.X - Half, Center.Y), true, false);
                }
                Shape = Diamond;
            }
            else
                Shape = new RectangleGeometry(new Rect(Center.X - Half, Center.Y - Half, Size, Size), 2.0, 2.0);

            var Fill = (IsSegmentHandle ? Brushes.LightCyan : Brushes.White);
            return new GeometryDrawing(Fill, new Pen(Brushes.RoyalBlue, 1.0), Shape).RenderToDrawingVisual();
        }

        private static int FindNearestSegment(IList<Point> PathPoints, Point TestPoint, out Point ClosestPoint)
        {
            var BestIndex = 0;
            var BestDistance = double.MaxValue;
            ClosestPoint = (PathPoints != null && PathPoints.Count > 0 ? PathPoints[0] : TestPoint);

            if (PathPoints == null || PathPoints.Count < 2)
                return BestIndex;

            for (int Index = 0; Index < PathPoints.Count - 1; Index++)
            {
                var Start = PathPoints[Index];
                var End = PathPoints[Index + 1];
                var Segment = End - Start;
                var LengthSquared = Segment.X * Segment.X + Segment.Y * Segment.Y;
                var Position = Start;

                if (LengthSquared > double.Epsilon)
                {
                    var Offset = TestPoint - Start;
                    var Ratio = ((Offset.X * Segment.X + Offset.Y * Segment.Y) / LengthSquared).EnforceRange(0.0, 1.0);
                    Position = Start + Segment * Ratio;
                }

                var Distance = (TestPoint - Position).Length;
                if (Distance < BestDistance)
                {
                    BestDistance = Distance;
                    BestIndex = Index;
                    ClosestPoint = Position;
                }
            }

            return BestIndex;
        }

        public DrawingVisual CreateActioner(double PosX, double PosY, EConnectorManipulationAction Manipulation,
                                            bool ShowSimplified = false, Brush PenBrush = null)
        {
            ImageSource Source = null;

            if (Manipulation == EConnectorManipulationAction.ReLink)
                Source = Display.GetAppImage(ShowSimplified ? "actconn_repos.png" : "actconn_relink.png");
            else
                if (Manipulation == EConnectorManipulationAction.EditProperties)
                    Source = ImgSrcEditProperties ?? Display.GetAppImage("page_white_edit.png");
                else
                    if (Manipulation == EConnectorManipulationAction.Remove)
                        Source = Display.GetAppImage("actconn_delete.png");
                    else
                        if (Manipulation == EConnectorManipulationAction.StraightenLine)
                            Source = Display.GetAppImage("actconn_straighten.png");
                        else
                            if (Manipulation == EConnectorManipulationAction.CycleThroughVariants)
                                Source = Display.GetAppImage("actconn_cycle.png");
                            else
                                if (Manipulation == EConnectorManipulationAction.Displace)
                                    Source = Display.GetAppImage(ShowSimplified ? "actconn_displace_part.png" : "actconn_displace_main.png");

            if (Source == null)
                throw new InternalAnomaly("Actioner is not defined for manipulation-action.", Manipulation);

            var ContainerArea = new Rect(PosX, PosY, ACTIONER_SIZE, ACTIONER_SIZE);
            var ContentArea = new Rect(PosX + 2, PosY + 2, ACTIONER_SIZE - 4, ACTIONER_SIZE - 4);
            var Drawer = new DrawingGroup();

            /* T Drawer.Children.Add(new GeometryDrawing(ActStroke, (PenBrush == null ? ActPencil : new Pen(PenBrush, 2.0)),
                                                    new RectangleGeometry(ContainerArea, 2, 2))); */

            var Icon = new ImageDrawing(Source, ContentArea);
            var Pad = new GeometryDrawing(this.ActStroke, null,    // Helps to avoid selecting incorrect indicator
                                          new RectangleGeometry(Icon.Rect));
            Drawer.Children.Add(Pad);
            Drawer.Children.Add(Icon);
            Drawer.Opacity = 0.85;
            return Drawer.RenderToDrawingVisual();
        }

        public EConnectorManipulationAction CurrentManipulationAction { get; protected set; }

        //------------------------------------------------------------------------------------------------------------------------
        public override Visual DeterminePointedVisual(Point Position)
        {
            this.PreviousPosition = this.CurrentPosition;
            this.CurrentPosition = Position;

            if (this.CurrentPosition == this.PreviousPosition || this.IsManipulating)
                return this.CurrentPointedVisual;

            var NewPointed = GetPointedVisual(Position);

            if (NewPointed != this.CurrentPointedVisual)
            {
                this.CurrentPointedVisual = NewPointed;

                /* POSTPONED: Displace connecting points
                if (NewPointed.IsOneOf(IndOriginPoint, IndTargetPoint))
                {
                    this.TentativeAction = EConnectorManipulationAction.Displace;
                    this.Cursor = Cursors.Cross;
                } */

                if (NewPointed != null /* && !NewPointed.IsOneOf(IndOriginPoint, IndTargetPoint)*/ )
                {
                    RouteHandleBinding RouteBinding;
                    RouteHandleBinding SegmentBinding;
                    VisualConnector PathConnector;
                    if (NewPointed is DrawingVisual
                        && this.RoutePointIndicators.TryGetValue((DrawingVisual)NewPointed, out RouteBinding))
                    {
                        this.ManipulatedRouteConnector = RouteBinding.Connector;
                        this.ManipulatedRoutePointIndex = RouteBinding.Index;
                        this.ManipulatedSegmentIndex = -1;
                        this.ManipulConnDisplacingPos = RouteBinding.Connector.RoutePoints[RouteBinding.Index];
                        this.TentativeAction = EConnectorManipulationAction.Displace;
                    }
                    else if (NewPointed is DrawingVisual
                             && this.SegmentIndicators.TryGetValue((DrawingVisual)NewPointed, out SegmentBinding))
                    {
                        this.ManipulatedRouteConnector = SegmentBinding.Connector;
                        this.ManipulatedRoutePointIndex = -1;
                        this.ManipulatedSegmentIndex = SegmentBinding.Index;
                        var Path = GetConnectorPathPoints(SegmentBinding.Connector);
                        if (SegmentBinding.Index >= 0 && SegmentBinding.Index < Path.Count - 1)
                            this.ManipulConnDisplacingPos = Path[SegmentBinding.Index]
                                                            .DetermineCenterRespect(Path[SegmentBinding.Index + 1]);
                        this.TentativeAction = this.CurrentManipulationAction;
                    }
                    else if (NewPointed is DrawingVisual
                             && this.ConnectorPathIndicators.TryGetValue((DrawingVisual)NewPointed, out PathConnector))
                    {
                        this.ManipulatedRouteConnector = PathConnector;
                        this.ManipulatedRoutePointIndex = -1;
                        Point ClosestPoint;
                        this.ManipulatedSegmentIndex = FindNearestSegment(GetConnectorPathPoints(PathConnector), Position,
                                                                          out ClosestPoint);
                        this.ManipulConnDisplacingPos = ClosestPoint;
                        this.TentativeAction = this.CurrentManipulationAction;
                    }
                    else if (NewPointed == this.DefaultActionIndicator || NewPointed.IsIn(AlternateActions))
                        this.TentativeAction = this.CurrentManipulationAction;
                    else
                        if (NewPointed == this.RelinkActionTargetIndicator || NewPointed == this.RelinkActionOriginIndicator)
                        {
                            this.ManipulatedRouteConnector = this.ManipulatedConnector;
                            this.ManipulatedRoutePointIndex = -1;
                            this.ManipulatedSegmentIndex = -1;
                            this.TentativeAction = EConnectorManipulationAction.ReLink;
                        }

                    if (this.TentativeAction == EConnectorManipulationAction.Displace)
                        this.Cursor = Cursors.ScrollAll;
                    else
                        if (this.TentativeAction == EConnectorManipulationAction.ReLink)
                            this.Cursor = Cursors.Cross;
                        else
                            this.Cursor = Cursors.Hand;
                }

                var IndDescription = this.TentativeAction.GetDescription();

                if (this.TentativeAction == EConnectorManipulationAction.Displace)
                    IndDescription = IndDescription + (this.ManipulatedRoutePointIndex >= 0
                                      ? " Drag to move this bend; double-click to remove it. "
                                      : " Drag a diamond to insert a bend; double-click the connector for Edit. ") +
                    "Action icons: [Ctrl]=Straighten line, [Shift]=Delete connector, [Alt]=Cycle variant plugs.";

                ProductDirector.ShowAssistance(IndDescription);

                /* DANGER: This tooltip stops the adorner working
                var Tip = this.ToolTip as ToolTip;

                if (Tip == null || (Tip.Content as string).IsAbsent())
                {
                    Tip = (Tip == null ? new ToolTip() : Tip);
                    Tip.Content = IndDescription;
                    Tip.IsOpen = true;
                    Tip.StaysOpen = false;
                    this.ToolTip = Tip;
                } */
                //- }

                ProductDirector.ShowPointingTo(this.ManipulatedConnector);
            }

            return NewPointed;
        }

        //------------------------------------------------------------------------------------------------------------------------
        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);

            RouteHandleBinding BendBinding;
            RouteHandleBinding SegmentBinding;
            VisualConnector PathConnector;
            var ContextConnector = this.ManipulatedRouteConnector ?? this.ManipulatedConnector;
            var BendIndex = -1;
            if (this.CurrentPointedVisual is DrawingVisual
                && this.RoutePointIndicators.TryGetValue((DrawingVisual)this.CurrentPointedVisual, out BendBinding))
            {
                ContextConnector = BendBinding.Connector;
                BendIndex = BendBinding.Index;
            }
            else if (this.CurrentPointedVisual is DrawingVisual
                     && this.SegmentIndicators.TryGetValue((DrawingVisual)this.CurrentPointedVisual, out SegmentBinding))
                ContextConnector = SegmentBinding.Connector;
            else if (this.CurrentPointedVisual is DrawingVisual
                     && this.ConnectorPathIndicators.TryGetValue((DrawingVisual)this.CurrentPointedVisual, out PathConnector))
                ContextConnector = PathConnector;

            ContextConnector.ContextRoutePointIndex = BendIndex;

            this.OwnerManager.OwnerView.Engine.ShowContextMenu(this.OwnerManager.OwnerView.Presenter,
                                                               ContextConnector,
                                                               this.OwnerManager.OwnerView,
                                                               () => ContextConnector.ContextRoutePointIndex = -1);
        }

        //------------------------------------------------------------------------------------------------------------------------
    }
}
