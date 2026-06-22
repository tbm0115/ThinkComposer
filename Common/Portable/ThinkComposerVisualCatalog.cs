namespace Instrumind.Common.Portable
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public sealed class ThinkComposerShapeOption
    {
        public ThinkComposerShapeOption(string displayName, string techName)
        {
            DisplayName = displayName;
            TechName = techName;
        }

        public string DisplayName { get; }

        public string TechName { get; }
    }

    public sealed class ThinkComposerConceptStyle
    {
        public ThinkComposerConceptStyle(string name, string shape, string fillColorHex, string strokeColorHex)
        {
            Name = name;
            Shape = shape;
            FillColorHex = fillColorHex;
            StrokeColorHex = strokeColorHex;
        }

        public string Name { get; }

        public string Shape { get; }

        public string FillColorHex { get; }

        public string StrokeColorHex { get; }
    }

    public sealed class ThinkComposerGraphicStylePreset
    {
        public ThinkComposerGraphicStylePreset(string name, string fillColorHex, string strokeColorHex, double lineThickness, string lineDash)
        {
            Name = name;
            FillColorHex = fillColorHex;
            StrokeColorHex = strokeColorHex;
            LineThickness = lineThickness;
            LineDash = lineDash;
        }

        public string Name { get; }

        public string FillColorHex { get; }

        public string StrokeColorHex { get; }

        public double LineThickness { get; }

        public string LineDash { get; }
    }

    public static class ThinkComposerVisualCatalog
    {
        private static readonly ThinkComposerShapeOption[] Shapes =
        {
            new ThinkComposerShapeOption("<None>", "None"),
            new ThinkComposerShapeOption("Poster", "Poster"),
            new ThinkComposerShapeOption("Signboard", "Signboard"),
            new ThinkComposerShapeOption("Person", "Person"),
            new ThinkComposerShapeOption("Rectangle", "Rectangle"),
            new ThinkComposerShapeOption("Gears", "Gears"),
            new ThinkComposerShapeOption("Piece", "Piece"),
            new ThinkComposerShapeOption("Rect-Crossed-Horizontal", "RectCrossedHorizontal"),
            new ThinkComposerShapeOption("Rect-Crossed-Vertical", "RectCrossedVertical"),
            new ThinkComposerShapeOption("Ellipse", "Ellipse"),
            new ThinkComposerShapeOption("Envelope", "Envelope"),
            new ThinkComposerShapeOption("Rounded-Rectangle", "RoundedRectangle"),
            new ThinkComposerShapeOption("Rhomb", "Rhomb"),
            new ThinkComposerShapeOption("Hexagon-Horizontal", "HexagonHorizontal"),
            new ThinkComposerShapeOption("Hexagon-Vertical", "HexagonVertical"),
            new ThinkComposerShapeOption("Capsule", "Capsule"),
            new ThinkComposerShapeOption("Folder", "Folder"),
            new ThinkComposerShapeOption("File", "File"),
            new ThinkComposerShapeOption("Document", "Document"),
            new ThinkComposerShapeOption("Card", "Card"),
            new ThinkComposerShapeOption("Flag", "Flag"),
            new ThinkComposerShapeOption("Drum", "Drum"),
            new ThinkComposerShapeOption("Barrel", "Barrel"),
            new ThinkComposerShapeOption("Standard", "Standard"),
            new ThinkComposerShapeOption("Trapezium", "Trapezium"),
            new ThinkComposerShapeOption("Parallelogram", "Parallelogram"),
            new ThinkComposerShapeOption("Banner", "Banner"),
            new ThinkComposerShapeOption("Triangle", "Triangle"),
            new ThinkComposerShapeOption("Component", "Component"),
            new ThinkComposerShapeOption("Chevron-Vertical", "ChevronVertical"),
            new ThinkComposerShapeOption("Chevron-Horizontal", "ChevronHorizontal"),
            new ThinkComposerShapeOption("Octagon", "Octagon"),
            new ThinkComposerShapeOption("Block", "Block"),
            new ThinkComposerShapeOption("BowTie", "BowTie"),
            new ThinkComposerShapeOption("Rect-Distorted", "RectDistorted"),
            new ThinkComposerShapeOption("Rect-Curved", "RectCurved"),
            new ThinkComposerShapeOption("Rect-Crossed-Corner", "RectCrossedCorner"),
            new ThinkComposerShapeOption("Plate", "Plate"),
            new ThinkComposerShapeOption("Spin", "Spin"),
            new ThinkComposerShapeOption("Dome", "Dome"),
            new ThinkComposerShapeOption("Rect-Enclosed", "RectEnclosed"),
            new ThinkComposerShapeOption("Ellipse-Enclosed", "EllipseEnclosed"),
            new ThinkComposerShapeOption("Rect-Intercrossed", "RectIntercrossed"),
            new ThinkComposerShapeOption("Ellipse-Intercrossed", "EllipseIntercrossed"),
            new ThinkComposerShapeOption("Rect-Intercrossed-Diagonal", "RectIntercrossedDiagonal"),
            new ThinkComposerShapeOption("Ellipse-Intercrossed-Diagonal", "EllipseIntercrossedDiagonal"),
            new ThinkComposerShapeOption("Rhomb-Crossed", "RhombCrossed"),
            new ThinkComposerShapeOption("Opposite-Triangles", "OppositeTriangles"),
            new ThinkComposerShapeOption("Rect-Crossed", "RectCrossed"),
            new ThinkComposerShapeOption("Tape", "Tape"),
            new ThinkComposerShapeOption("X-Mark", "XMark"),
            new ThinkComposerShapeOption("Straight-Parallel-Lines", "StraightParallelLines"),
            new ThinkComposerShapeOption("Straight-Under-Line", "StraightUnderLine"),
            new ThinkComposerShapeOption("Brackets-Square", "BracketsSquare"),
            new ThinkComposerShapeOption("Brackets-Curved", "BracketsCurved"),
            new ThinkComposerShapeOption("Brackets-Curly", "BracketsCurly"),
            new ThinkComposerShapeOption("Pentagon", "Pentagon"),
            new ThinkComposerShapeOption("Bin", "Bin"),
            new ThinkComposerShapeOption("Rect-Diagonal", "RectDiagonal"),
            new ThinkComposerShapeOption("Button", "Button"),
            new ThinkComposerShapeOption("Rect-Crossed-Top", "RectCrossedTop"),
            new ThinkComposerShapeOption("Wrapper", "Wrapper"),
            new ThinkComposerShapeOption("Funnel", "Funnel"),
            new ThinkComposerShapeOption("Cloud", "Cloud"),
            new ThinkComposerShapeOption("Arrow", "Arrow"),
            new ThinkComposerShapeOption("ArrowDouble", "ArrowDouble"),
            new ThinkComposerShapeOption("ArrowRegular", "ArrowRegular"),
            new ThinkComposerShapeOption("ArrowRegularDouble", "ArrowRegularDouble")
        };

        private static readonly Dictionary<string, ThinkComposerShapeOption> ShapeByAnyName =
            Shapes
                .SelectMany(shape => new[]
                {
                    new KeyValuePair<string, ThinkComposerShapeOption>(shape.DisplayName, shape),
                    new KeyValuePair<string, ThinkComposerShapeOption>(shape.TechName, shape),
                    new KeyValuePair<string, ThinkComposerShapeOption>(RemoveSeparators(shape.DisplayName), shape),
                    new KeyValuePair<string, ThinkComposerShapeOption>(RemoveSeparators(shape.TechName), shape)
                })
                .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);

        private static readonly ThinkComposerConceptStyle[] ConceptStyles =
        {
            new ThinkComposerConceptStyle("Concept", "Capsule", "#FFFFFF66", "#FFFFCC00"),
            new ThinkComposerConceptStyle("Entity", "RoundedRectangle", "#FF2FC5BA", "#FF008A83"),
            new ThinkComposerConceptStyle("Process", "Gears", "#FF18A9F5", "#FF0284C7"),
            new ThinkComposerConceptStyle("Person", "Person", "#FF4FE0B0", "#FF0EA37E"),
            new ThinkComposerConceptStyle("Data", "EllipseEnclosed", "#FF486DFF", "#FF0000CC"),
            new ThinkComposerConceptStyle("File", "File", "#FFFFEEF2", "#FFF43F5E"),
            new ThinkComposerConceptStyle("Card", "Card", "#FFF8FBFF", "#FF94A3B8"),
            new ThinkComposerConceptStyle("Component", "Component", "#FF19A9F5", "#FF0E7490"),
            new ThinkComposerConceptStyle("Part", "Piece", "#FFE9E5FF", "#FF6D65B8"),
            new ThinkComposerConceptStyle("Document", "Document", "#FFFFFFFF", "#FF22C55E"),
            new ThinkComposerConceptStyle("Banner", "Banner", "#FFFFFFFF", "#FF7DD3FC"),
            new ThinkComposerConceptStyle("Screen", "HexagonHorizontal", "#FFFFC83D", "#FFFFA300"),
            new ThinkComposerConceptStyle("Filter", "Trapezium", "#FFEDE9FE", "#FF7C3AED"),
            new ThinkComposerConceptStyle("Flag", "Flag", "#FFFFF7ED", "#FFFF4D2E"),
            new ThinkComposerConceptStyle("Object", "Rectangle", "#FFE9FFE7", "#FF22C55E"),
            new ThinkComposerConceptStyle("Meta-Object", "RectCrossedHorizontal", "#FFFFE4E6", "#FFE11D48"),
            new ThinkComposerConceptStyle("Example", "RoundedRectangle", "#FFFFFFFF", "#FF1D4ED8"),
            new ThinkComposerConceptStyle("Container", "Wrapper", "#FFFFFBEB", "#FFA16207"),
            new ThinkComposerConceptStyle("Representation", "Standard", "#FFF8FAFC", "#FF64748B"),
            new ThinkComposerConceptStyle("Bifurcation", "Triangle", "#FFE6FFFB", "#FF0F766E"),
            new ThinkComposerConceptStyle("Specification", "HexagonHorizontal", "#FF14B8A6", "#FF0F766E"),
            new ThinkComposerConceptStyle("Abstraction", "HexagonVertical", "#FFE0E7FF", "#FF6366F1"),
            new ThinkComposerConceptStyle("Alternative", "Parallelogram", "#FFFFEDD5", "#FFFF4D2E"),
            new ThinkComposerConceptStyle("Anything", "Ellipse", "#FFF8FAFC", "#FF475569"),
            new ThinkComposerConceptStyle("Module", "Rectangle", "#FF38BDF8", "#FF0284C7"),
            new ThinkComposerConceptStyle("Stage", "ArrowRegular", "#FF84CC16", "#FF4D7C0F"),
            new ThinkComposerConceptStyle("Undefined", "RoundedRectangle", "#FFE2E8F0", "#FF334155"),
            new ThinkComposerConceptStyle("Error", "XMark", "#FFFFFFFF", "#FFEF4444"),
            new ThinkComposerConceptStyle("Area", "RectEnclosed", "#FFFFFFFF", "#FF22C55E"),
            new ThinkComposerConceptStyle("Product", "Capsule", "#FFFFE4D6", "#FFFF6B3A")
        };

        private static readonly ThinkComposerShapeOption[] ConnectorPlugs =
        {
            new ThinkComposerShapeOption("<None>", "None"),
            new ThinkComposerShapeOption("Filled-Arrow", "FilledArrow"),
            new ThinkComposerShapeOption("Double-Filled-Arrow", "DoubleFilledArrow"),
            new ThinkComposerShapeOption("Empty-Arrow", "EmptyArrow"),
            new ThinkComposerShapeOption("Double-Empty-Arrow", "DoubleEmptyArrow"),
            new ThinkComposerShapeOption("Simple-Arrow", "SimpleArrow"),
            new ThinkComposerShapeOption("Double-Simple-Arrow", "DoubleSimpleArrow"),
            new ThinkComposerShapeOption("Filled-Circle", "FilledCircle"),
            new ThinkComposerShapeOption("Empty-Circle", "EmptyCircle"),
            new ThinkComposerShapeOption("Filled-Rhomb", "FilledRhomb"),
            new ThinkComposerShapeOption("Empty-Rhomb", "EmptyRhomb"),
            new ThinkComposerShapeOption("Line-Dash", "LineDash"),
            new ThinkComposerShapeOption("Line-Double-Dash", "LineDoubleDash"),
            new ThinkComposerShapeOption("Triline-Circle", "TrilineCircle"),
            new ThinkComposerShapeOption("Triline-Dash", "TrilineDash"),
            new ThinkComposerShapeOption("Filled-Circle-Arrow", "FilledCircleArrow"),
            new ThinkComposerShapeOption("Empty-Circle-Arrow", "EmptyCircleArrow"),
            new ThinkComposerShapeOption("Empty-Circle-Simple-Arrow", "EmptyCircleSimpleArrow"),
            new ThinkComposerShapeOption("Line-X", "LineX"),
            new ThinkComposerShapeOption("Pointer-Arrow", "PointerArrow"),
            new ThinkComposerShapeOption("Chevron", "Chevron"),
            new ThinkComposerShapeOption("Plumb-Bob", "PlumbBob"),
            new ThinkComposerShapeOption("Circle-Dash", "CircleDash"),
            new ThinkComposerShapeOption("Circle-Plus", "CirclePlus"),
            new ThinkComposerShapeOption("Circle-Minus", "CircleMinus"),
            new ThinkComposerShapeOption("Circle-Asterisk", "CircleAsterisk")
        };

        private static readonly ThinkComposerShapeOption[] LinkRoleVariants =
        {
            new ThinkComposerShapeOption("Standard", "Standard"),
            new ThinkComposerShapeOption("1..1", "1..1"),
            new ThinkComposerShapeOption("0..1", "0..1"),
            new ThinkComposerShapeOption("1..*", "1..N"),
            new ThinkComposerShapeOption("0..*", "0..N")
        };

        private static readonly Dictionary<string, ThinkComposerConceptStyle> ConceptStyleByName =
            ConceptStyles.ToDictionary(style => style.Name, StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, ThinkComposerShapeOption> ConnectorPlugByAnyName =
            CreateOptionLookup(ConnectorPlugs);

        private static readonly Dictionary<string, ThinkComposerShapeOption> LinkRoleVariantByAnyName =
            CreateOptionLookup(LinkRoleVariants);

        public static IReadOnlyList<ThinkComposerShapeOption> ShapeOptions => Shapes;

        public static IReadOnlyList<string> ShapeDisplayNames => Shapes.Select(shape => shape.DisplayName).ToArray();

        public static IReadOnlyList<ThinkComposerConceptStyle> DefaultConceptStyles => ConceptStyles;

        public static IReadOnlyList<ThinkComposerShapeOption> ConnectorPlugOptions => ConnectorPlugs;

        public static IReadOnlyList<string> ConnectorPlugDisplayNames => ConnectorPlugs.Select(plug => plug.DisplayName).ToArray();

        public static IReadOnlyList<ThinkComposerShapeOption> LinkRoleVariantOptions => LinkRoleVariants;

        public static IReadOnlyList<string> LinkRoleVariantDisplayNames => LinkRoleVariants.Select(variant => variant.DisplayName).ToArray();

        public static IReadOnlyList<ThinkComposerGraphicStylePreset> GraphicStylePresets { get; } = CreateGraphicStylePresets();

        public static string NormalizeShapeTechName(string value, string fallback = "Rectangle")
        {
            var key = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            if (ShapeByAnyName.TryGetValue(key, out var shape))
                return shape.TechName;

            var compact = RemoveSeparators(key);
            return ShapeByAnyName.TryGetValue(compact, out shape) ? shape.TechName : NormalizeShapeTechName(fallback, "Rectangle");
        }

        public static string GetShapeDisplayName(string value)
        {
            var techName = NormalizeShapeTechName(value);
            return ShapeByAnyName.TryGetValue(techName, out var shape) ? shape.DisplayName : techName;
        }

        public static ThinkComposerConceptStyle GetDefaultConceptStyle(string name, int index)
        {
            if (TryGetDefaultConceptStyle(name, out var style))
                return style;

            var fallback = ConceptStyles[Math.Abs(index) % ConceptStyles.Length];
            return new ThinkComposerConceptStyle(name, fallback.Shape, fallback.FillColorHex, fallback.StrokeColorHex);
        }

        public static bool TryGetDefaultConceptStyle(string name, out ThinkComposerConceptStyle style)
        {
            if (!string.IsNullOrWhiteSpace(name) && ConceptStyleByName.TryGetValue(name.Trim(), out style))
                return true;

            style = null;
            return false;
        }

        public static bool IsEllipseShape(string shape)
        {
            var techName = NormalizeShapeTechName(shape);
            return techName == "Ellipse"
                || techName == "EllipseEnclosed"
                || techName == "EllipseIntercrossed"
                || techName == "EllipseIntercrossedDiagonal";
        }

        public static bool IsCapsuleShape(string shape)
        {
            return NormalizeShapeTechName(shape) == "Capsule";
        }

        public static bool IsRoundedRectangleShape(string shape)
        {
            return NormalizeShapeTechName(shape) == "RoundedRectangle";
        }

        public static string NormalizeConnectorPlugTechName(string value, string fallback = "None")
        {
            return NormalizeOptionTechName(ConnectorPlugByAnyName, value, fallback, "None");
        }

        public static string NormalizeLinkRoleVariantTechName(string value, string fallback = "Standard")
        {
            return NormalizeOptionTechName(LinkRoleVariantByAnyName, value, fallback, "Standard");
        }

        public static string GetConnectorPlugDisplayName(string value)
        {
            var techName = NormalizeConnectorPlugTechName(value);
            return ConnectorPlugByAnyName.TryGetValue(techName, out var option) ? option.DisplayName : techName;
        }

        public static string GetLinkRoleVariantDisplayName(string value)
        {
            var techName = NormalizeLinkRoleVariantTechName(value);
            return LinkRoleVariantByAnyName.TryGetValue(techName, out var option) ? option.DisplayName : techName;
        }

        private static IReadOnlyList<ThinkComposerGraphicStylePreset> CreateGraphicStylePresets()
        {
            var foregrounds = new[]
            {
                "#FF000000", "#FF808080", "#FF708090", "#FF4169E1", "#FF0000FF", "#FF4682B4",
                "#FF008080", "#FF008000", "#FF32CD32", "#FFA0522D", "#FF800080", "#FFFF0000",
                "#FFFF4500", "#FFFFA500", "#FFFFD700", "#FFFFFF00", "#FFDB7093", "#FF8B0000",
                "#FFDC143C", "#FF4682B4", "#FF1E90FF", "#FF48D1CC", "#FF4682B4", "#FF808000"
            };

            var backgrounds = new[]
            {
                "#FFA9A9A9", "#FFD3D3D3", "#FFB0C4DE", "#FF1E90FF", "#FF87CEEB", "#FFADD8E6",
                "#FF66CDAA", "#FF20B2AA", "#FF90EE90", "#FFD2B48C", "#FFDDA0DD", "#FFFFB6C1",
                "#FFFFDAB9", "#FFFFEFD5", "#FFFFF8DC", "#FFFFFFE0", "#FFFFF0F5", "#FFE9967A",
                "#FFF08080", "#FFDCDCDC", "#FFAFEEEE", "#FFF0FFF0", "#FF9ACD32", "#FFFAF0E6"
            };

            var styles = new List<ThinkComposerGraphicStylePreset>();
            var thicknesses = new[] { 1.0, 2.0, 0.0 };
            var dashes = new[] { "Solid", "Dashed" };

            foreach (var thickness in thicknesses)
            {
                foreach (var dash in thickness == 0.0 ? new[] { "Solid" } : dashes)
                {
                    for (var index = 0; index < foregrounds.Length; index++)
                    {
                        var stroke = thickness == 0.0 ? "#00FFFFFF" : InterpolateHex(foregrounds[index], "#FFFFFFFF", 0.25);
                        var fill = InterpolateHex(backgrounds[index], "#FFFFFFFF", 0.25);
                        styles.Add(new ThinkComposerGraphicStylePreset(
                            "Style " + (styles.Count + 1).ToString("000"),
                            fill,
                            stroke,
                            thickness,
                            dash));
                    }
                }
            }

            return styles;
        }

        private static string RemoveSeparators(string value)
        {
            return (value ?? string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty).Replace("<", string.Empty).Replace(">", string.Empty);
        }

        private static Dictionary<string, ThinkComposerShapeOption> CreateOptionLookup(IEnumerable<ThinkComposerShapeOption> options)
        {
            return options
                .SelectMany(option => new[]
                {
                    new KeyValuePair<string, ThinkComposerShapeOption>(option.DisplayName, option),
                    new KeyValuePair<string, ThinkComposerShapeOption>(option.TechName, option),
                    new KeyValuePair<string, ThinkComposerShapeOption>(RemoveSeparators(option.DisplayName), option),
                    new KeyValuePair<string, ThinkComposerShapeOption>(RemoveSeparators(option.TechName), option)
                })
                .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);
        }

        private static string NormalizeOptionTechName(IDictionary<string, ThinkComposerShapeOption> lookup, string value, string fallback, string finalFallback)
        {
            var key = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            if (lookup.TryGetValue(key, out var option))
                return option.TechName;

            var compact = RemoveSeparators(key);
            if (lookup.TryGetValue(compact, out option))
                return option.TechName;

            if (!string.Equals(key, finalFallback, StringComparison.OrdinalIgnoreCase))
                return NormalizeOptionTechName(lookup, fallback, finalFallback, finalFallback);

            return finalFallback;
        }

        private static string InterpolateHex(string sourceHex, string targetHex, double amount)
        {
            if (!TcColor.TryParseHex(sourceHex, out var source) || !TcColor.TryParseHex(targetHex, out var target))
                return sourceHex;

            return TcColor.FromArgb(
                255,
                Interpolate(source.R, target.R, amount),
                Interpolate(source.G, target.G, amount),
                Interpolate(source.B, target.B, amount)).ToHexArgb();
        }

        private static byte Interpolate(byte source, byte target, double amount)
        {
            return (byte)Math.Round(source + ((target - source) * amount));
        }
    }
}
