// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Dependency-light regressions for Composition JSON connector route persistence.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

using Instrumind.ThinkComposer.Model.VisualModel;

namespace Instrumind.ThinkComposer.Composer.JsonInterchange
{
    public sealed class CompositionJsonRoutePersistenceRegressionResult
    {
        public CompositionJsonRoutePersistenceRegressionResult()
        {
            this.PassedScenarios = new List<string>();
            this.Failures = new List<string>();
        }

        public IList<string> PassedScenarios { get; private set; }
        public IList<string> Failures { get; private set; }
        public bool Passed { get { return this.Failures.Count == 0; } }
    }

    /// <summary>
    /// Exercises JSON route versioning and patch semantics without loading a Composition,
    /// package, Domain, View presenter, or routing service.
    /// </summary>
    public static class CompositionJsonRoutePersistenceRegression
    {
        public static CompositionJsonRoutePersistenceRegressionResult RunAll()
        {
            var Result = new CompositionJsonRoutePersistenceRegressionResult();
            Run(Result, "v1-intermediate-migration", TestV1IntermediateMigration);
            Run(Result, "v1-route-points-rejected", TestV1RoutePointsRejected);
            Run(Result, "v2-zero-one-many-round-trips", TestV2RoundTrips);
            Run(Result, "patch-omission-versus-empty", TestPatchOmissionVersusEmpty);
            Run(Result, "both-fields-route-points-win", TestBothFieldsRoutePointsWin);
            return Result;
        }

        private static void Run(CompositionJsonRoutePersistenceRegressionResult Result, string Name, Action Test)
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

        private static void TestV1IntermediateMigration()
        {
            var Document = CompositionJsonSerializer.Deserialize(BuildJson(1,
                "\"id\":\"legacy\",\"intermediatePosition\":{\"x\":24,\"y\":36}"));
            CompositionJsonSerializer.Validate(Document);
            var Source = GetOnlyConnector(Document);
            Require(!Source.RoutePointsSpecified && Source.IntermediatePositionSpecified,
                    "v1 connector field presence was not preserved");

            var Connector = CreateConnector(new Point(5, 5), new Point(10, 10));
            var Warnings = new List<string>();
            var Changed = CompositionJsonImporter.ApplyConnectorRoutePointsCore(Connector, Source, Warnings.Add);
            Require(Changed, "v1 intermediatePosition was not applied");
            RequireRoute(Connector.RoutePoints, new Point(24, 36));
            Require(Warnings.Count == 0, "v1 singleton migration produced an unexpected warning");
        }

        private static void TestV1RoutePointsRejected()
        {
            var Document = CompositionJsonSerializer.Deserialize(BuildJson(1,
                "\"id\":\"invalid-v1\",\"routePoints\":[{\"x\":20,\"y\":10}]"));
            Require(GetOnlyConnector(Document).RoutePointsSpecified,
                    "v1 routePoints presence was lost before validation");
            RequireThrows<InvalidDataException>(() => CompositionJsonSerializer.Validate(Document),
                                                "formatVersion 1 routePoints was accepted");
        }

        private static void TestV2RoundTrips()
        {
            var Cases = new[]
            {
                new Point[0],
                new[] { new Point(20, 10) },
                new[] { new Point(20, 10), new Point(20, 40), new Point(80, 40), new Point(80, 10) }
            };

            foreach (var Expected in Cases)
            {
                var Document = CreateDocument(Expected);
                var FirstJson = CompositionJsonSerializer.Serialize(Document);
                var FirstRead = CompositionJsonSerializer.Deserialize(FirstJson);
                CompositionJsonSerializer.Validate(FirstRead);
                RequireConnectorRoute(GetOnlyConnector(FirstRead), Expected,
                                      "first v2 round-trip for " + Expected.Length + " points");

                var SecondJson = CompositionJsonSerializer.Serialize(FirstRead);
                var SecondRead = CompositionJsonSerializer.Deserialize(SecondJson);
                CompositionJsonSerializer.Validate(SecondRead);
                RequireConnectorRoute(GetOnlyConnector(SecondRead), Expected,
                                      "second v2 round-trip for " + Expected.Length + " points");
            }
        }

        private static void TestPatchOmissionVersusEmpty()
        {
            var Original = new[] { new Point(20, 10), new Point(60, 10) };
            var Connector = CreateConnector(Original);
            var Warnings = new List<string>();

            var Omitted = GetOnlyConnector(CompositionJsonSerializer.Deserialize(BuildJson(2, "\"id\":\"omitted\"")));
            var OmittedChanged = CompositionJsonImporter.ApplyConnectorRoutePointsCore(Connector, Omitted, Warnings.Add);
            Require(!OmittedChanged, "omitted routePoints reported a mutation");
            RequireRoute(Connector.RoutePoints, Original);

            var Empty = GetOnlyConnector(CompositionJsonSerializer.Deserialize(BuildJson(2,
                "\"id\":\"clear\",\"routePoints\":[]")));
            var EmptyChanged = CompositionJsonImporter.ApplyConnectorRoutePointsCore(Connector, Empty, Warnings.Add);
            Require(EmptyChanged, "explicit empty routePoints did not report a mutation");
            Require(Connector.RoutePoints.Count == 0, "explicit empty routePoints did not clear the route");
            Require(Warnings.Count == 0, "omission/clear patch semantics produced an unexpected warning");
        }

        private static void TestBothFieldsRoutePointsWin()
        {
            var Document = CompositionJsonSerializer.Deserialize(BuildJson(2,
                "\"id\":\"both\"," +
                "\"routePoints\":[{\"x\":20,\"y\":30},{\"x\":70,\"y\":30}]," +
                "\"intermediatePosition\":{\"x\":999,\"y\":999}"));
            CompositionJsonSerializer.Validate(Document);
            var Source = GetOnlyConnector(Document);
            Require(Source.RoutePointsSpecified && Source.IntermediatePositionSpecified,
                    "both-field presence was not retained by deserialization");

            var Connector = CreateConnector(new Point(1, 1));
            var Warnings = new List<string>();
            var Changed = CompositionJsonImporter.ApplyConnectorRoutePointsCore(Connector, Source, Warnings.Add);
            Require(Changed, "authoritative routePoints did not apply");
            RequireRoute(Connector.RoutePoints, new Point(20, 30), new Point(70, 30));
            Require(Warnings.Count == 1 &&
                    Warnings[0].IndexOf("routePoints is authoritative", StringComparison.OrdinalIgnoreCase) >= 0,
                    "both fields did not produce the authoritative-route warning");
        }

        private static CompositionJsonDocument CreateDocument(IEnumerable<Point> RoutePoints)
        {
            var Connector = new CompositionJsonConnector
            {
                Id = "round-trip",
                RoutePointsSpecified = true,
                RoutePoints = (RoutePoints ?? Enumerable.Empty<Point>())
                              .Select(Point => new CompositionJsonPoint { X = Point.X, Y = Point.Y })
                              .ToList()
            };
            var Visual = new CompositionJsonVisual { IdeaTechName = "Relationship" };
            Visual.Connectors.Add(Connector);
            var View = new CompositionJsonView { TechName = "Main_View" };
            View.Visuals.Add(Visual);
            var Document = new CompositionJsonDocument();
            Document.Views.Add(View);
            return Document;
        }

        private static string BuildJson(int FormatVersion, string ConnectorMembers)
        {
            var Builder = new StringBuilder();
            Builder.Append("{\"format\":\"").Append(CompositionJsonDocument.CurrentFormat)
                   .Append("\",\"formatVersion\":").Append(FormatVersion)
                   .Append(",\"application\":\"ThinkComposer\",\"views\":[{\"techName\":\"Main_View\",\"visuals\":[{")
                   .Append("\"ideaTechName\":\"Relationship\",\"connectors\":[{")
                   .Append(ConnectorMembers)
                   .Append("}]}]}]}");
            return Builder.ToString();
        }

        private static CompositionJsonConnector GetOnlyConnector(CompositionJsonDocument Document)
        {
            return Document.Views.Single().Visuals.Single().Connectors.Single();
        }

        private static VisualConnector CreateConnector(params Point[] RoutePoints)
        {
            var Connector = new VisualConnector();
            Connector.OriginPosition = new Point(0, 0);
            Connector.TargetPosition = new Point(100, 0);
            Connector.SetRoutePoints(RoutePoints ?? new Point[0]);
            return Connector;
        }

        private static void RequireConnectorRoute(CompositionJsonConnector Connector, IList<Point> Expected,
                                                  string Context)
        {
            Require(Connector.RoutePointsSpecified, Context + " omitted routePoints");
            Require(Connector.RoutePoints != null && Connector.RoutePoints.Count == Expected.Count,
                    Context + " changed the route-point count");
            for (var Index = 0; Index < Expected.Count; Index++)
            {
                var Actual = Connector.RoutePoints[Index];
                Require(Actual != null && Actual.X != null && Actual.Y != null &&
                        Same(Actual.X.Value, Expected[Index].X) && Same(Actual.Y.Value, Expected[Index].Y),
                        Context + " changed routePoints[" + Index + "]");
            }
        }

        private static void RequireRoute(IList<Point> Actual, params Point[] Expected)
        {
            Require(Actual != null && Actual.Count == Expected.Length, "route-point count differed");
            for (var Index = 0; Index < Expected.Length; Index++)
                Require(Same(Actual[Index].X, Expected[Index].X) && Same(Actual[Index].Y, Expected[Index].Y),
                        "routePoints[" + Index + "] differed");
        }

        private static bool Same(double First, double Second)
        {
            return Math.Abs(First - Second) <= 0.000001;
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
