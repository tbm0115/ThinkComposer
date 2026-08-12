// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Shared relationship routing orchestration for UI layouts, JSON import and headless checks.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using Instrumind.ThinkComposer.Model;
using Instrumind.ThinkComposer.Model.VisualModel;

namespace Instrumind.ThinkComposer.Composer.Layout
{
    public sealed class RelationshipRoutingValidationResult
    {
        public RelationshipRoutingValidationResult()
        {
            this.Diagnostics = new List<RelationshipRouteDiagnostic>();
            this.Warnings = new List<string>();
        }

        public int Inspected { get; set; }
        public int Healthy { get; set; }
        public int Suspicious { get; set; }
        public int Invalid { get; set; }
        public int StaleEndpoints { get; set; }
        public int AmbiguousIdentities { get; set; }
        public int RelationshipCentersInspected { get; set; }
        public int DistantRelationshipCenters { get; set; }
        public IList<RelationshipRouteDiagnostic> Diagnostics { get; private set; }
        public IList<string> Warnings { get; private set; }

        public bool IsHealthy { get { return this.Invalid == 0 && this.Suspicious == 0; } }
    }

    /// <summary>
    /// Single orchestration facade used by every route-producing subsystem. The low-level
    /// planner remains pure; this class scopes model objects and applies profile defaults.
    /// </summary>
    public static class RelationshipRoutingCoordinator
    {
        public static LinkObstacleRoutingResult Route(LayoutSelectionContext Context, LinkObstacleRoutingOptions Options)
        {
            Options = Options ?? new LinkObstacleRoutingOptions();
            ApplyProfileDefaults(Options);
            return LinkObstacleRoutingService.RouteVisibleConnectors(Context, Options);
        }

        public static void ConfigureMandatoryCorridors(LinkObstacleRoutingOptions Options,
                                                       IEnumerable<RelationshipVisualRepresentation> Relationships)
        {
            if (Options == null)
                return;
            foreach (var Representation in (Relationships ?? Enumerable.Empty<RelationshipVisualRepresentation>())
                                            .Where(Representation => Representation != null))
                Options.MandatoryWaypointRelationships.Add(Representation);
        }

        public static LinkObstacleRoutingResult RouteGeneratedRelationships(CompositionEngine Engine, View View,
                                                                            IEnumerable<RelationshipVisualRepresentation> Relationships,
                                                                            string DirtyReason)
        {
            return RouteRelationships(Engine, View, Relationships, RelationshipRoutingProfile.JsonImport,
                                      RelationshipRouteIntent.Generated, DirtyReason, false);
        }

        public static LinkObstacleRoutingResult RouteAfterLayout(CompositionEngine Engine, View View,
                                                                 IEnumerable<RelationshipVisualRepresentation> Relationships,
                                                                 RelationshipRoutingProfile Profile,
                                                                 string DirtyReason)
        {
            return RouteRelationships(Engine, View, Relationships, Profile,
                                      RelationshipRouteIntent.Layout, DirtyReason, false);
        }

        public static LinkObstacleRoutingResult RouteRelationships(CompositionEngine Engine, View View,
                                                                   IEnumerable<RelationshipVisualRepresentation> Relationships,
                                                                   RelationshipRoutingProfile Profile,
                                                                   RelationshipRouteIntent Intent,
                                                                   string DirtyReason,
                                                                   bool PreserveExisting)
        {
            var Representations = (Relationships ?? Enumerable.Empty<RelationshipVisualRepresentation>())
                                  .Where(Representation => Representation != null)
                                  .Distinct()
                                  .ToList();
            var Selection = Representations.SelectMany(Representation => Representation.VisualConnectors ??
                                                                         Enumerable.Empty<VisualConnector>())
                                           .Where(Connector => Connector != null)
                                           .Cast<VisualObject>()
                                           .ToList();
            var Context = LayoutSelectionContext.FromViewSelection(Engine, View, Selection);
            var Options = new LinkObstacleRoutingOptions
            {
                Profile = Profile,
                RouteIntent = Intent,
                DirtyReason = DirtyReason,
                PreserveExistingValidRoutes = PreserveExisting,
                RouteSelectedConnectorsOnly = true,
                IncludeRelationshipCentralSymbolsAsObstacles = true,
                CorrectRelationshipCentersBeforeRouting = true
            };
            return Route(Context, Options);
        }

        public static RelationshipRoutingValidationResult Validate(LayoutSelectionContext Context,
                                                                   LinkObstacleRoutingOptions Options)
        {
            var Result = new RelationshipRoutingValidationResult();
            Options = Options ?? new LinkObstacleRoutingOptions();
            ApplyProfileDefaults(Options);
            if (Context == null || Context.ActiveView == null)
            {
                Result.Invalid++;
                Result.Warnings.Add("No active view is available for relationship route validation.");
                return Result;
            }

            var Obstacles = LinkObstacleRoutingService.BuildObstacleRectangles(Context, Options);
            var Connectors = LinkObstacleRoutingService.GetConnectorsForScope(Context, Options)
                                                       .Where(Connector => Connector != null)
                                                       .ToList();
            var DuplicateConnectorIds = new HashSet<Guid>(Connectors
                .Where(Connector => Connector.GlobalId != Guid.Empty)
                .GroupBy(Connector => Connector.GlobalId)
                .Where(Group => Group.Count() > 1)
                .Select(Group => Group.Key));
            var AmbiguousLinkConnectors = GetAmbiguousLinkConnectorsForValidation(Connectors);

            foreach (var Connector in Connectors.OrderBy(GetValidationKey))
            {
                Result.Inspected++;
                var Reasons = new List<string>();
                var RawSource = Connector.OriginPosition;
                var RawTarget = Connector.TargetPosition;
                var SourceAnchorMissing = !IsUsablePoint(RawSource) ||
                                          RawSource == Instrumind.Common.Visualization.Display.NULL_POINT;
                var TargetAnchorMissing = !IsUsablePoint(RawTarget) ||
                                          RawTarget == Instrumind.Common.Visualization.Display.NULL_POINT;
                var Source = RawSource;
                if (SourceAnchorMissing)
                    Source = Connector.OriginSymbol == null ? new Point(Double.NaN, Double.NaN) : Connector.OriginSymbol.BaseCenter;
                var Target = RawTarget;
                if (TargetAnchorMissing)
                    Target = Connector.TargetSymbol == null ? new Point(Double.NaN, Double.NaN) : Connector.TargetSymbol.BaseCenter;
                var RoutePoints = Connector.RoutePoints == null ? new List<Point>() : Connector.RoutePoints.ToList();
                var LocalObstacles = Obstacles.Where(Rectangle =>
                    (Connector.OriginSymbol == null || !Rectangle.Contains(Connector.OriginSymbol.BaseCenter)) &&
                    (Connector.TargetSymbol == null || !Rectangle.Contains(Connector.TargetSymbol.BaseCenter))).ToList();
                string Reason;
                var Suspicious = OrthogonalRoutePlanner.IsRouteSuspicious(Source, RoutePoints, Target, LocalObstacles,
                                                                          Options.MaximumPreservedDetourRatio, out Reason);
                if (!String.IsNullOrWhiteSpace(Reason))
                    Reasons.Add(Reason);

                var IdentityAmbiguous = Connector.GlobalId == Guid.Empty ||
                                        DuplicateConnectorIds.Contains(Connector.GlobalId) ||
                                         Connector.RepresentedLink == null ||
                                         Connector.RepresentedLink.GlobalId == Guid.Empty ||
                                         AmbiguousLinkConnectors.Contains(Connector);
                if (IdentityAmbiguous)
                {
                    Result.AmbiguousIdentities++;
                    Reasons.Add("connector or represented-link identity is missing or duplicated");
                }

                var StaleEndpoint = SourceAnchorMissing || TargetAnchorMissing ||
                                    !IsAnchorLocalToSymbol(RawSource, Connector.OriginSymbol) ||
                                    !IsAnchorLocalToSymbol(RawTarget, Connector.TargetSymbol) ||
                                    !IsOptionalEdgeAnchorLocalToSymbol(Connector.OriginEdgePosition, Connector.OriginSymbol) ||
                                    !IsOptionalEdgeAnchorLocalToSymbol(Connector.TargetEdgePosition, Connector.TargetSymbol);
                if (StaleEndpoint)
                {
                    Result.StaleEndpoints++;
                    Reasons.Add("connector endpoint anchor is missing or no longer local to its attached symbol");
                }

                var Invalid = !IsUsablePoint(Source) || !IsUsablePoint(Target) ||
                              RoutePoints.Count > VisualConnector.MAX_ROUTE_POINTS ||
                              RoutePoints.Any(Point => !IsUsablePoint(Point)) ||
                              IdentityAmbiguous || StaleEndpoint;
                if (Invalid)
                    Result.Invalid++;
                else if (Suspicious)
                    Result.Suspicious++;
                else
                    Result.Healthy++;

                var Diagnostic = new RelationshipRouteDiagnostic
                {
                    RouteKey = GetValidationKey(Connector),
                    Intent = RelationshipRouteIntent.PreserveIfValid,
                    DirtyReason = null,
                    Status = Invalid ? RelationshipRouteStatus.Failed :
                             (Suspicious ? RelationshipRouteStatus.DegradedDirect : RelationshipRouteStatus.Preserved),
                    OldPointCount = RoutePoints.Count,
                    NewPointCount = RoutePoints.Count,
                    OldPoints = RoutePoints.ToList(),
                    NewPoints = RoutePoints.ToList(),
                    BendCount = RoutePoints.Count,
                    ObstacleCount = LocalObstacles.Count,
                    UsedFallback = !Invalid && Suspicious,
                    IsSuspicious = Suspicious,
                    IsSafe = !Invalid && !Suspicious,
                    Message = Reasons.Count == 0 ? null : String.Join("; ", Reasons.Distinct().ToArray())
                };
                Result.Diagnostics.Add(Diagnostic);
                if (Invalid || Suspicious)
                    Result.Warnings.Add(Diagnostic.RouteKey + ": " +
                                        (Diagnostic.Message ?? "invalid route geometry"));
            }

            var RelationshipScope = (Options.RouteSelectedConnectorsOnly
                                     ? Context.SelectedRelationshipRepresentations
                                     : Context.VisibleRelationshipRepresentations)
                .Where(Representation => Representation != null)
                .Distinct()
                .OrderBy(GetRelationshipValidationKey, StringComparer.Ordinal)
                .ToList();
            foreach (var Representation in RelationshipScope)
            {
                Point ReferenceCenter;
                Rect Corridor;
                int EndpointCount;
                double NormalizedDistance;
                string SuspiciousReason;
                if (!RelationshipVisualPlacementService.TryEvaluateRelationshipCenter(
                        Representation, Options.RelationshipVisualPlacementOptions,
                        out ReferenceCenter, out Corridor, out EndpointCount,
                        out NormalizedDistance, out SuspiciousReason))
                    continue;

                Result.Inspected++;
                Result.RelationshipCentersInspected++;
                var IsDistant = !String.IsNullOrWhiteSpace(SuspiciousReason);
                if (IsDistant)
                {
                    Result.Suspicious++;
                    Result.DistantRelationshipCenters++;
                }
                else
                    Result.Healthy++;

                var Center = Representation.MainSymbol.BaseCenter;
                var Diagnostic = new RelationshipRouteDiagnostic
                {
                    RouteKey = "relationship-center:" + GetRelationshipValidationKey(Representation),
                    Intent = RelationshipRouteIntent.PreserveIfValid,
                    Status = IsDistant ? RelationshipRouteStatus.DegradedDirect : RelationshipRouteStatus.Preserved,
                    OldPointCount = 1,
                    NewPointCount = 1,
                    OldPoints = new List<Point> { Center },
                    NewPoints = new List<Point> { Center },
                    ObstacleCount = EndpointCount,
                    DetourRatio = NormalizedDistance,
                    IsDistantRelationshipCenter = IsDistant,
                    IsSuspicious = IsDistant,
                    IsSafe = !IsDistant,
                    Message = IsDistant
                              ? SuspiciousReason + "; center=" + FormatPoint(Center) +
                                "; endpointReference=" + FormatPoint(ReferenceCenter) +
                                "; endpointCorridor=" + FormatRect(Corridor)
                              : null
                };
                Result.Diagnostics.Add(Diagnostic);
                if (IsDistant)
                    Result.Warnings.Add(Diagnostic.RouteKey + ": " + Diagnostic.Message);
            }
            return Result;
        }

        public static void ApplyProfileDefaults(LinkObstacleRoutingOptions Options)
        {
            if (Options == null)
                return;

            switch (Options.Profile)
            {
                case RelationshipRoutingProfile.Spider:
                    // Spider maps have many radial links; relationship hubs must participate in
                    // the obstacle field to prevent lines from sweeping through adjacent hubs.
                    Options.IncludeRelationshipCentralSymbolsAsObstacles = true;
                    Options.CrossingCost = Math.Max(Options.CrossingCost, 250.0);
                    break;
                case RelationshipRoutingProfile.Hierarchy:
                case RelationshipRoutingProfile.Flowchart:
                case RelationshipRoutingProfile.SystemMap:
                case RelationshipRoutingProfile.JsonImport:
                case RelationshipRoutingProfile.Validation:
                    Options.IncludeRelationshipCentralSymbolsAsObstacles = true;
                    break;
            }
        }

        private static bool IsUsablePoint(Point Point)
        {
            return !Double.IsNaN(Point.X) && !Double.IsInfinity(Point.X) &&
                   !Double.IsNaN(Point.Y) && !Double.IsInfinity(Point.Y);
        }

        private static bool IsAnchorLocalToSymbol(Point Anchor, VisualSymbol Symbol)
        {
            if (Symbol == null || !IsUsablePoint(Anchor) ||
                Anchor == Instrumind.Common.Visualization.Display.NULL_POINT)
                return false;

            var Bounds = Symbol.TotalArea;
            if (Double.IsNaN(Bounds.X) || Double.IsInfinity(Bounds.X) ||
                Double.IsNaN(Bounds.Y) || Double.IsInfinity(Bounds.Y) ||
                Double.IsNaN(Bounds.Width) || Double.IsInfinity(Bounds.Width) ||
                Double.IsNaN(Bounds.Height) || Double.IsInfinity(Bounds.Height) ||
                Bounds.IsEmpty)
                return false;

            // Plugs and hit-test borders can extend a few pixels beyond TotalArea.  A fixed,
            // scale-independent allowance still rejects the distant anchors left by stale moves.
            Bounds.Inflate(24.0, 24.0);
            return Bounds.Contains(Anchor);
        }

        private static bool IsOptionalEdgeAnchorLocalToSymbol(Point Anchor, VisualSymbol Symbol)
        {
            // Hidden relationship junctions do not render a symbol boundary; their cached edge
            // point may intentionally remain on the logical route corridor.  The actual endpoint
            // position is still validated against the hidden hub center above.
            if (Symbol != null && (Symbol.IsHidden || !Symbol.IsRelatedVisible))
                return true;
            return Anchor == Instrumind.Common.Visualization.Display.NULL_POINT ||
                   IsAnchorLocalToSymbol(Anchor, Symbol);
        }

        private static string GetValidationKey(VisualConnector Connector)
        {
            if (Connector == null)
                return "<null>";
            var Relationship = Connector.OwnerRelationshipRepresentation == null
                               ? null : Connector.OwnerRelationshipRepresentation.RepresentedRelationship;
            var Prefix = Relationship == null ? "relationship" : Relationship.GlobalId.ToString("D");
            return Prefix + ":" + Connector.GlobalId.ToString("D") + ":" +
                   DescribeSymbol(Connector.OriginSymbol) + "->" + DescribeSymbol(Connector.TargetSymbol);
        }

        private static string GetRelationshipValidationKey(RelationshipVisualRepresentation Representation)
        {
            var Relationship = Representation == null ? null : Representation.RepresentedRelationship;
            return (Relationship == null ? "?" : Relationship.GlobalId.ToString("D")) + ":" +
                   (Representation == null ? "?" : Representation.GlobalId.ToString("D"));
        }

        internal static HashSet<VisualConnector> GetAmbiguousLinkConnectorsForValidation(
            IEnumerable<VisualConnector> Source)
        {
            // A semantic Link is intentionally shared by every visual/shortcut representation.
            // It is ambiguous only when repeated inside the same representation.
            return new HashSet<VisualConnector>((Source ?? Enumerable.Empty<VisualConnector>())
                .Where(Connector => Connector != null && Connector.RepresentedLink != null)
                .GroupBy(Connector => new
                {
                    Representation = Connector.OwnerRelationshipRepresentation,
                    Link = Connector.RepresentedLink
                })
                .Where(Group => Group.Count() > 1)
                .SelectMany(Group => Group));
        }

        private static string FormatPoint(Point Point)
        {
            return Point.X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
                   Point.Y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FormatRect(Rect Rect)
        {
            return Rect.X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
                   Rect.Y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
                   Rect.Width.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "," +
                   Rect.Height.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string DescribeSymbol(VisualSymbol Symbol)
        {
            var Idea = Symbol == null || Symbol.OwnerRepresentation == null
                       ? null : Symbol.OwnerRepresentation.RepresentedIdea;
            return Idea == null ? "?" : Idea.GlobalId.ToString("D");
        }
    }
}
