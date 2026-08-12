// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Dependency-free regression checks for connector route storage and drawing.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Windows;
using System.Windows.Media;

using Instrumind.Common.EntityBase;
using Instrumind.Common.Visualization;
using Instrumind.ThinkComposer.Definitor.DefinitorUI;
using Instrumind.ThinkComposer.MetaModel.VisualMetaModel;

namespace Instrumind.ThinkComposer.Model.VisualModel
{
    public sealed class VisualConnectorRouteRegressionResult
    {
        public VisualConnectorRouteRegressionResult()
        {
            this.PassedScenarios = new List<string>();
            this.Failures = new List<string>();
        }

        public IList<string> PassedScenarios { get; private set; }
        public IList<string> Failures { get; private set; }
        public bool Passed { get { return this.Failures.Count == 0; } }
    }

    /// <summary>
    /// Small regression hook which can run from the CLI without constructing a Composition.
    /// </summary>
    public static class VisualConnectorRouteRegression
    {
        public static VisualConnectorRouteRegressionResult RunAll()
        {
            var Result = new VisualConnectorRouteRegressionResult();
            Run(Result, "route-apis", TestRouteApis);
            Run(Result, "undo-redo-route-edits", TestUndoRedoRouteEdits);
            Run(Result, "clone-independence", TestCloneIndependence);
            Run(Result, "legacy-binary-migration", TestLegacyBinaryMigration);
            Run(Result, "binary-singleton-facade", TestBinarySingletonFacade);
            Run(Result, "continuous-path-geometry", TestContinuousPathGeometry);
            return Result;
        }

        private static void Run(VisualConnectorRouteRegressionResult Result, string Name, Action Test)
        {
            try
            {
                Test();
                Result.PassedScenarios.Add(Name);
            }
            catch (Exception Problem)
            {
                Result.Failures.Add(Name + ": " + Problem);
            }
        }

        private static void TestRouteApis()
        {
            var Connector = CreateConnector();
            Connector.SetRoutePoints(new[] { new Point(20, 10), new Point(60, 10) });
            Require(Connector.RoutePoints.Count == 2, "set did not preserve both route points");
            Require(Connector.IntermediatePosition == Display.NULL_POINT,
                    "multi-point route leaked through the singleton facade");

            Connector.InsertRoutePoint(1, new Point(40, 20));
            Connector.UpdateRoutePoint(0, new Point(20, 20));
            Connector.RemoveRoutePoint(2);
            Connector.TranslateRoutePoints(5, -5);
            Require(SamePoint(Connector.RoutePoints[0], new Point(25, 15))
                    && SamePoint(Connector.RoutePoints[1], new Point(45, 15)),
                    "insert/update/remove/translate produced unexpected geometry");

            var TooMany = Enumerable.Range(0, VisualConnector.MAX_ROUTE_POINTS + 1)
                                    .Select(Index => new Point(Index, Index));
            RequireThrows<ArgumentOutOfRangeException>(() => Connector.SetRoutePoints(TooMany),
                                                       "oversized route was accepted");
            RequireThrows<ArgumentException>(() => Connector.SetRoutePoints(new[] { new Point(double.NaN, 1) }),
                                             "nonfinite route point was accepted");
            RequireThrows<ArgumentException>(() => Connector.TranslateRoutePoints(double.PositiveInfinity, 0),
                                             "nonfinite route translation was accepted");

            RequireThrows<NotSupportedException>(() => Connector.RoutePoints.Add(new Point(50, 50)),
                                                 "public route view allowed a caller to bypass validation");

            Connector.SetRoutePoints(new[] { new Point(20, 0), new Point(60, 0) });
            Connector.SimplifyRoutePoints();
            Require(Connector.RoutePoints.Count == 0,
                    "simplification retained continuing collinear points");

            Connector.SetRoutePoints(new[] { new Point(60, 0), new Point(20, 0) });
            Connector.SimplifyRoutePoints();
            Require(Connector.RoutePoints.Count == 2,
                    "simplification removed a collinear reversal and changed the visible route");

            Connector.ClearRoutePoints();
            Require(Connector.RoutePoints.Count == 0, "clear left route points behind");
        }

        private static void TestCloneIndependence()
        {
            var Source = CreateConnector();
            Source.SetRoutePoints(new[] { new Point(20, 10), new Point(60, 10) });

            var Clone = Source.CreateClone(ECloneOperationScope.Deep, null, false);
            Require(!Object.ReferenceEquals(Source.RoutePoints, Clone.RoutePoints), "clone shares its EditableList with the source");

            Clone.UpdateRoutePoint(0, new Point(999, 999));
            Require(SamePoint(Source.RoutePoints[0], new Point(20, 10)), "editing the clone changed its source");
        }

        private static void TestUndoRedoRouteEdits()
        {
            // EntityEditEngine's command finalization asks Display for the current WPF window.
            // The regression is otherwise headless, so provide an application host with no window.
            if (Application.Current == null)
                new Application();

            VerifyUndoRedo("replace",
                           new[] { new Point(10, 10) },
                           Connector => Connector.ReplaceRoutePoints(new[] { new Point(20, 20), new Point(40, 20) }),
                           new[] { new Point(20, 20), new Point(40, 20) });

            VerifyUndoRedo("insert",
                           new[] { new Point(20, 10), new Point(60, 10) },
                           Connector => Connector.InsertRoutePoint(1, new Point(40, 30)),
                           new[] { new Point(20, 10), new Point(40, 30), new Point(60, 10) });

            VerifyUndoRedo("update",
                           new[] { new Point(20, 10), new Point(60, 10) },
                           Connector => Connector.UpdateRoutePoint(0, new Point(25, 35)),
                           new[] { new Point(25, 35), new Point(60, 10) });

            VerifyUndoRedo("remove",
                           new[] { new Point(20, 10), new Point(40, 30), new Point(60, 10) },
                           Connector => Connector.RemoveRoutePoint(1),
                           new[] { new Point(20, 10), new Point(60, 10) });

            VerifyUndoRedo("clear",
                           new[] { new Point(20, 10), new Point(60, 10) },
                           Connector => Connector.ClearRoutePoints(),
                           new Point[0]);

            VerifyUndoRedo("translate",
                           new[] { new Point(20, 10), new Point(60, 10) },
                           Connector => Connector.TranslateRoutePoints(5, -3),
                           new[] { new Point(25, 7), new Point(65, 7) });
        }

        private static void VerifyUndoRedo(string Name, IEnumerable<Point> Initial,
                                           Action<VisualConnector> Mutation, IEnumerable<Point> Expected)
        {
            var PreviousEditor = EntityEditEngine.ActiveEntityEditor;
            var Engine = new RouteRegressionEditEngine();
            var Connector = CreateConnector();
            try
            {
                Engine.Start();
                EntityEditEngine.ActiveEntityEditor = Engine;
                Connector.EditEngine = Engine;
                Connector.SetRoutePoints(Initial);
                var InitialSnapshot = Connector.RoutePoints.ToList();
                var ExpectedSnapshot = Expected.ToList();

                Engine.StartCommandVariation("Regression route " + Name);
                Mutation(Connector);
                Engine.CompleteCommandVariation();
                Require(SameRoute(Connector.RoutePoints, ExpectedSnapshot), Name + " did not apply expected geometry");

                Engine.Undo(true, false);
                Require(SameRoute(Connector.RoutePoints, InitialSnapshot), Name + " undo did not restore the complete route");

                Engine.Redo(true, false);
                Require(SameRoute(Connector.RoutePoints, ExpectedSnapshot), Name + " redo did not restore the edited route");
            }
            finally
            {
                EntityEditEngine.ActiveEntityEditor = PreviousEditor;
                Engine.Stop();
            }
        }

        private static bool SameRoute(IList<Point> First, IList<Point> Second)
        {
            return First != null && Second != null && First.Count == Second.Count
                   && First.Zip(Second, SamePoint).All(Equal => Equal);
        }

        private sealed class RouteRegressionEditEngine : EntityEditEngine
        {
            public override ISphereModel TargetDocument { get { return null; } }

            public override event Action<EntityEditEngine> MainEditedEntityChanged
            {
                add { }
                remove { }
            }
        }

        private static void TestBinarySingletonFacade()
        {
            var Singleton = CreateConnector();
            Singleton.SetRoutePoints(new[] { new Point(40, 30) });
            var SingletonCopy = BinaryRoundTrip(Singleton);
            Require(SingletonCopy.RoutePoints.Count == 1
                    && SamePoint(SingletonCopy.IntermediatePosition, new Point(40, 30)),
                    "singleton route did not survive binary round-trip through the legacy facade");

            var Multiple = CreateConnector();
            Multiple.SetRoutePoints(new[] { new Point(20, 10), new Point(60, 10) });
            var MultipleCopy = BinaryRoundTrip(Multiple);
            Require(MultipleCopy.RoutePoints.Count == 2,
                    "multi-point route did not survive binary round-trip");
            Require(MultipleCopy.IntermediatePosition == Display.NULL_POINT,
                    "multi-point binary route exposed a lossy legacy singleton");
        }

        private static void TestLegacyBinaryMigration()
        {
            var Connector = CreateConnector();
            var RouteField = typeof(VisualConnector).GetField("RoutePoints_", BindingFlags.Instance | BindingFlags.NonPublic);
            var LegacyField = typeof(VisualConnector).GetField("IntermediatePosition_", BindingFlags.Instance | BindingFlags.NonPublic);
            var Callback = typeof(VisualConnector).GetMethod("InitializeRoutePoints", BindingFlags.Instance | BindingFlags.NonPublic);

            Require(RouteField != null && RouteField.IsDefined(typeof(OptionalFieldAttribute), false),
                    "RoutePoints binary field is not optional for old files");
            Require(LegacyField != null && Callback != null, "legacy migration members were not found");

            RouteField.SetValue(Connector, null);
            LegacyField.SetValue(Connector, new Point(45, 35));
            Callback.Invoke(Connector, new object[] { default(StreamingContext) });

            Require(Connector.RoutePoints.Count == 1 && SamePoint(Connector.RoutePoints[0], new Point(45, 35)),
                    "old IntermediatePosition was not migrated to RoutePoints");
        }

        private static void TestContinuousPathGeometry()
        {
            var SharpDrawing = PathDrawer.CreatePath(EPathStyle.SinglelineStraight, EPathCorner.Sharp,
                                                     new Pen(Brushes.Black, 1.0), null,
                                                     new Point(100, 0), new Point(0, 0),
                                                     new[] { new Point(20, 10), new Point(60, 10) }) as GeometryDrawing;
            var SharpGeometry = (SharpDrawing == null ? null : SharpDrawing.Geometry as PathGeometry);
            Require(SharpGeometry != null && SharpGeometry.Figures.Count == 1,
                    "connector was not rendered as one PathGeometry");
            Require(SharpGeometry.Figures[0].Segments.Count == 3,
                    "continuous path did not include every leg");

            var StraightDrawing = PathDrawer.CreatePath(EPathStyle.SinglelineStraight, EPathCorner.Sharp,
                                                        new Pen(Brushes.Black, 1.0), null,
                                                        new Point(100, 0), new Point(0, 0), null) as GeometryDrawing;
            var StraightGeometry = (StraightDrawing == null ? null : StraightDrawing.Geometry as PathGeometry);
            Require(StraightGeometry != null && StraightGeometry.Figures.Count == 1
                    && StraightGeometry.Figures[0].Segments.Count == 1,
                    "zero-point route was not rendered as one continuous segment");

            var SingletonDrawing = PathDrawer.CreatePath(EPathStyle.SinglelineStraight, EPathCorner.Sharp,
                                                         new Pen(Brushes.Black, 1.0), null,
                                                         new Point(100, 0), new Point(0, 0),
                                                         new[] { new Point(50, 20) }) as GeometryDrawing;
            var SingletonGeometry = (SingletonDrawing == null ? null : SingletonDrawing.Geometry as PathGeometry);
            Require(SingletonGeometry != null && SingletonGeometry.Figures.Count == 1
                    && SingletonGeometry.Figures[0].Segments.Count == 2,
                    "singleton route was not rendered as one continuous polyline");

            var RoundedDrawing = PathDrawer.CreatePath(EPathStyle.MultilineRightAngled, EPathCorner.Rounded,
                                                       new Pen(Brushes.Black, 1.0), null,
                                                       new Point(100, 100), new Point(0, 0),
                                                       new[] { new Point(100, 0) }) as GeometryDrawing;
            var RoundedGeometry = (RoundedDrawing == null ? null : RoundedDrawing.Geometry as PathGeometry);
            Require(RoundedGeometry != null
                    && RoundedGeometry.Figures[0].Segments.OfType<QuadraticBezierSegment>().Any(),
                    "rounded multiline path did not create a rounded corner segment");
        }

        private static VisualConnector CreateConnector()
        {
            var Result = new VisualConnector();
            Result.OriginPosition = new Point(0, 0);
            Result.TargetPosition = new Point(100, 0);
            return Result;
        }

        private static VisualConnector BinaryRoundTrip(VisualConnector Connector)
        {
            using (var Buffer = new MemoryStream())
            {
                var Formatter = new BinaryFormatter();
                Formatter.Serialize(Buffer, Connector);
                Buffer.Position = 0;
                return (VisualConnector)Formatter.Deserialize(Buffer);
            }
        }

        private static bool SamePoint(Point First, Point Second)
        {
            return Math.Abs(First.X - Second.X) <= 0.001 && Math.Abs(First.Y - Second.Y) <= 0.001;
        }

        private static void RequireThrows<TException>(Action Operation, string Message) where TException : Exception
        {
            try
            {
                Operation();
            }
            catch (TException)
            {
                return;
            }

            throw new InvalidOperationException(Message);
        }

        private static void Require(bool Condition, string Message)
        {
            if (!Condition)
                throw new InvalidOperationException(Message);
        }
    }
}
