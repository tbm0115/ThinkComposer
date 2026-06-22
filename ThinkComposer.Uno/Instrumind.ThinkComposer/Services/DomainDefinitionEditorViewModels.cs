namespace Instrumind.ThinkComposer.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Instrumind.Common.Portable;

public sealed class RelationshipDefinitionEditorViewModel
{
    private readonly EditableDomainModel domain;
    private readonly EditableRelationshipDefinition original;

    public RelationshipDefinitionEditorViewModel(EditableDomainModel domain, EditableRelationshipDefinition relationship, bool isNew)
    {
        this.domain = domain ?? throw new ArgumentNullException(nameof(domain));
        original = relationship;
        IsNew = isNew;
        WorkingCopy = relationship == null ? new EditableRelationshipDefinition() : relationship.Clone();
        ValidationMessage = string.Empty;
    }

    public EditableRelationshipDefinition WorkingCopy { get; }

    public bool IsNew { get; }

    public string ValidationMessage { get; private set; }

    public bool TryApply()
    {
        if (!Validate())
            return false;

        var copy = WorkingCopy.Clone();
        copy.RepresentativeShape = ThinkComposerVisualCatalog.NormalizeShapeTechName(copy.RepresentativeShape, "Ellipse");
        copy.Symbol.Shape = copy.RepresentativeShape;

        if (IsNew)
        {
            domain.RelationshipDefinitions.Add(copy);
        }
        else
        {
            var index = domain.RelationshipDefinitions.FindIndex(item => item.Id == original.Id);
            if (index < 0)
            {
                ValidationMessage = "The Relationship being edited is no longer available.";
                return false;
            }

            domain.RelationshipDefinitions[index] = copy;
        }

        domain.IsDirty = true;
        return true;
    }

    public EditableDetailDesignator AddDetail()
    {
        var detail = EditableDetailDesignator.Create("Detail", "Link", true, true);
        WorkingCopy.Details.Add(detail);
        return detail;
    }

    public void RemoveDetail(EditableDetailDesignator detail)
    {
        if (detail != null)
            WorkingCopy.Details.Remove(detail);
    }

    public EditableOutputTemplate EnsureOutputTemplate()
    {
        if (WorkingCopy.OutputTemplates.Count == 0)
            WorkingCopy.OutputTemplates.Add(new EditableOutputTemplate());

        return WorkingCopy.OutputTemplates[0];
    }

    private bool Validate()
    {
        WorkingCopy.Name = FirstText(WorkingCopy.Name);
        WorkingCopy.TechName = FirstText(WorkingCopy.TechName);

        if (string.IsNullOrWhiteSpace(WorkingCopy.Name))
        {
            ValidationMessage = "A Relationship name is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(WorkingCopy.TechName))
            WorkingCopy.TechName = EditableDomainNaming.ToTechName(WorkingCopy.Name);

        if (string.IsNullOrWhiteSpace(WorkingCopy.Id))
            WorkingCopy.Id = Guid.NewGuid().ToString("D");

        WorkingCopy.RepresentativeShape = ThinkComposerVisualCatalog.NormalizeShapeTechName(WorkingCopy.RepresentativeShape, "Ellipse");

        var duplicate = domain.RelationshipDefinitions.Any(item =>
            !string.Equals(item.Id, WorkingCopy.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.TechName, WorkingCopy.TechName, StringComparison.OrdinalIgnoreCase));

        if (duplicate)
        {
            ValidationMessage = "Another Relationship already uses Tech-Name '" + WorkingCopy.TechName + "'.";
            return false;
        }

        WorkingCopy.Symbol ??= EditableConceptSymbolFormat.CreateDefault();
        WorkingCopy.Connector ??= EditableConnectorFormat.CreateDefault();
        WorkingCopy.OriginRole ??= EditableLinkRoleDefinition.Create("Origin");
        WorkingCopy.TargetRole ??= EditableLinkRoleDefinition.Create("Target");
        WorkingCopy.Symbol.Shape = ThinkComposerVisualCatalog.NormalizeShapeTechName(WorkingCopy.Symbol.Shape, WorkingCopy.RepresentativeShape);
        WorkingCopy.Symbol.FillColorHex = NormalizeHex(WorkingCopy.Symbol.FillColorHex, "#FFE5E7EB");
        WorkingCopy.Symbol.StrokeColorHex = NormalizeHex(WorkingCopy.Symbol.StrokeColorHex, "#FF64748B");
        WorkingCopy.Symbol.LineThickness = PositiveOrDefault(WorkingCopy.Symbol.LineThickness, 1.5);
        WorkingCopy.Symbol.InitialWidth = PositiveOrDefault(WorkingCopy.Symbol.InitialWidth, 110);
        WorkingCopy.Symbol.InitialHeight = PositiveOrDefault(WorkingCopy.Symbol.InitialHeight, 38);
        WorkingCopy.Connector.LineColorHex = NormalizeHex(WorkingCopy.Connector.LineColorHex, "#FF111827");
        WorkingCopy.Connector.MainBackgroundColorHex = NormalizeHex(WorkingCopy.Connector.MainBackgroundColorHex, "#00FFFFFF");
        WorkingCopy.Connector.LineThickness = PositiveOrDefault(WorkingCopy.Connector.LineThickness, 1.5);
        WorkingCopy.Connector.HeadPlug = ThinkComposerVisualCatalog.NormalizeConnectorPlugTechName(WorkingCopy.Connector.HeadPlug, "SimpleArrow");
        WorkingCopy.Connector.TailPlug = ThinkComposerVisualCatalog.NormalizeConnectorPlugTechName(WorkingCopy.Connector.TailPlug, "None");
        WorkingCopy.Connector.HeadVariantTechName = ThinkComposerVisualCatalog.NormalizeLinkRoleVariantTechName(WorkingCopy.Connector.HeadVariantTechName, "Standard");
        WorkingCopy.Connector.TailVariantTechName = ThinkComposerVisualCatalog.NormalizeLinkRoleVariantTechName(WorkingCopy.Connector.TailVariantTechName, "Standard");
        WorkingCopy.Connector.LineDash = FirstText(WorkingCopy.Connector.LineDash);
        if (string.IsNullOrWhiteSpace(WorkingCopy.Connector.LineDash))
            WorkingCopy.Connector.LineDash = "Solid";

        NormalizeRole(WorkingCopy.OriginRole, "Origin");
        NormalizeRole(WorkingCopy.TargetRole, WorkingCopy.IsDirectional ? "Target" : "Participant");
        ValidationMessage = string.Empty;
        return true;
    }

    public static EditableRelationshipDefinition CreateNewRelationship(IEnumerable<EditableRelationshipDefinition> existing)
    {
        var existingList = (existing ?? Enumerable.Empty<EditableRelationshipDefinition>()).ToArray();
        var name = MakeUniqueName("Relationship", existingList.Select(item => item.Name));
        var relationship = EditableRelationshipDefinition.CreateDefault(name, "#FF64748B");
        relationship.TechName = EditableDomainNaming.MakeUniqueTechName(name, existingList.Select(item => item.TechName));
        return relationship;
    }

    public static EditableRelationshipDefinition DuplicateRelationship(EditableRelationshipDefinition source, IEnumerable<EditableRelationshipDefinition> existing)
    {
        var existingList = (existing ?? Enumerable.Empty<EditableRelationshipDefinition>()).ToArray();
        var copy = source.Clone();
        copy.Id = Guid.NewGuid().ToString("D");
        copy.Name = MakeUniqueName(source.Name + " Copy", existingList.Select(item => item.Name));
        copy.TechName = EditableDomainNaming.MakeUniqueTechName(copy.Name, existingList.Select(item => item.TechName));
        return copy;
    }

    private static string NormalizeHex(string value, string fallback)
    {
        return TcColor.TryParseHex(value, out var color) ? color.ToHexArgb() : fallback;
    }

    private static double PositiveOrDefault(double value, double fallback)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value) ? value : fallback;
    }

    private static void NormalizeRole(EditableLinkRoleDefinition role, string fallbackRoleType)
    {
        role.Name = string.IsNullOrWhiteSpace(role.Name) ? fallbackRoleType : role.Name.Trim();
        role.TechName = FirstText(role.TechName);
        if (string.IsNullOrWhiteSpace(role.TechName))
            role.TechName = EditableDomainNaming.ToTechName(role.Name);

        role.RoleType = FirstText(role.RoleType);
        if (string.IsNullOrWhiteSpace(role.RoleType))
            role.RoleType = fallbackRoleType;

        role.AllowedVariants = ThinkComposerVisualCatalog.NormalizeLinkRoleVariantTechName(role.AllowedVariants, "Standard");
        if (role.MaxConnections < 0)
            role.MaxConnections = 1;
    }

    private static string FirstText(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string MakeUniqueName(string preferredName, IEnumerable<string> existingNames)
    {
        var name = string.IsNullOrWhiteSpace(preferredName) ? "Relationship" : preferredName.Trim();
        var existing = new HashSet<string>(existingNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(name))
            return name;

        for (var index = 2; index < 10000; index++)
        {
            var candidate = name + " " + index.ToString(CultureInfo.InvariantCulture);
            if (!existing.Contains(candidate))
                return candidate;
        }

        return name + " " + Guid.NewGuid().ToString("N").Substring(0, 8);
    }
}

public sealed class MarkerDefinitionEditorViewModel
{
    private readonly EditableDomainModel domain;
    private readonly EditableMarkerDefinition original;

    public MarkerDefinitionEditorViewModel(EditableDomainModel domain, EditableMarkerDefinition marker, bool isNew)
    {
        this.domain = domain ?? throw new ArgumentNullException(nameof(domain));
        original = marker;
        IsNew = isNew;
        WorkingCopy = marker == null ? new EditableMarkerDefinition() : marker.Clone();
        ValidationMessage = string.Empty;
    }

    public EditableMarkerDefinition WorkingCopy { get; }

    public bool IsNew { get; }

    public string ValidationMessage { get; private set; }

    public bool TryApply()
    {
        if (!Validate())
            return false;

        var copy = WorkingCopy.Clone();
        if (IsNew)
        {
            domain.MarkerDefinitions.Add(copy);
        }
        else
        {
            var index = domain.MarkerDefinitions.FindIndex(item => item.Id == original.Id);
            if (index < 0)
            {
                ValidationMessage = "The Marker being edited is no longer available.";
                return false;
            }

            domain.MarkerDefinitions[index] = copy;
        }

        domain.IsDirty = true;
        return true;
    }

    private bool Validate()
    {
        WorkingCopy.Name = FirstText(WorkingCopy.Name);
        WorkingCopy.TechName = FirstText(WorkingCopy.TechName);

        if (string.IsNullOrWhiteSpace(WorkingCopy.Name))
        {
            ValidationMessage = "A Marker name is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(WorkingCopy.TechName))
            WorkingCopy.TechName = EditableDomainNaming.ToTechName(WorkingCopy.Name);

        if (string.IsNullOrWhiteSpace(WorkingCopy.Id))
            WorkingCopy.Id = Guid.NewGuid().ToString("D");

        var duplicate = domain.MarkerDefinitions.Any(item =>
            !string.Equals(item.Id, WorkingCopy.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.TechName, WorkingCopy.TechName, StringComparison.OrdinalIgnoreCase));

        if (duplicate)
        {
            ValidationMessage = "Another Marker already uses Tech-Name '" + WorkingCopy.TechName + "'.";
            return false;
        }

        WorkingCopy.BackgroundColorHex = NormalizeHex(WorkingCopy.BackgroundColorHex, "#FFFBBF24");
        WorkingCopy.ForegroundColorHex = NormalizeHex(WorkingCopy.ForegroundColorHex, "#FF111827");
        if (string.IsNullOrWhiteSpace(WorkingCopy.ClusterKey))
            WorkingCopy.ClusterKey = "UserDef";

        ValidationMessage = string.Empty;
        return true;
    }

    public static EditableMarkerDefinition CreateNewMarker(IEnumerable<EditableMarkerDefinition> existing)
    {
        var existingList = (existing ?? Enumerable.Empty<EditableMarkerDefinition>()).ToArray();
        var name = MakeUniqueName("Marker", existingList.Select(item => item.Name), "Marker");
        var marker = EditableMarkerDefinition.CreateDefault(name, "#FFFBBF24");
        marker.TechName = EditableDomainNaming.MakeUniqueTechName(name, existingList.Select(item => item.TechName));
        return marker;
    }

    public static EditableMarkerDefinition DuplicateMarker(EditableMarkerDefinition source, IEnumerable<EditableMarkerDefinition> existing)
    {
        var existingList = (existing ?? Enumerable.Empty<EditableMarkerDefinition>()).ToArray();
        var copy = source.Clone();
        copy.Id = Guid.NewGuid().ToString("D");
        copy.Name = MakeUniqueName(source.Name + " Copy", existingList.Select(item => item.Name), "Marker");
        copy.TechName = EditableDomainNaming.MakeUniqueTechName(copy.Name, existingList.Select(item => item.TechName));
        return copy;
    }

    private static string NormalizeHex(string value, string fallback)
    {
        return TcColor.TryParseHex(value, out var color) ? color.ToHexArgb() : fallback;
    }

    private static string FirstText(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string MakeUniqueName(string preferredName, IEnumerable<string> existingNames, string fallback)
    {
        var name = string.IsNullOrWhiteSpace(preferredName) ? fallback : preferredName.Trim();
        var existing = new HashSet<string>(existingNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(name))
            return name;

        for (var index = 2; index < 10000; index++)
        {
            var candidate = name + " " + index.ToString(CultureInfo.InvariantCulture);
            if (!existing.Contains(candidate))
                return candidate;
        }

        return name + " " + Guid.NewGuid().ToString("N").Substring(0, 8);
    }
}

public sealed class ComplementDefinitionEditorViewModel
{
    private readonly EditableDomainModel domain;
    private readonly EditableComplementDefinition original;

    public ComplementDefinitionEditorViewModel(EditableDomainModel domain, EditableComplementDefinition complement, bool isNew)
    {
        this.domain = domain ?? throw new ArgumentNullException(nameof(domain));
        original = complement;
        IsNew = isNew;
        WorkingCopy = complement == null ? new EditableComplementDefinition() : complement.Clone();
        ValidationMessage = string.Empty;
    }

    public EditableComplementDefinition WorkingCopy { get; }

    public bool IsNew { get; }

    public string ValidationMessage { get; private set; }

    public bool TryApply()
    {
        if (!Validate())
            return false;

        var copy = WorkingCopy.Clone();
        if (IsNew)
        {
            domain.ComplementDefinitions.Add(copy);
        }
        else
        {
            var index = domain.ComplementDefinitions.FindIndex(item => item.Id == original.Id);
            if (index < 0)
            {
                ValidationMessage = "The Complement being edited is no longer available.";
                return false;
            }

            domain.ComplementDefinitions[index] = copy;
        }

        domain.IsDirty = true;
        return true;
    }

    private bool Validate()
    {
        WorkingCopy.Name = FirstText(WorkingCopy.Name);
        WorkingCopy.TechName = FirstText(WorkingCopy.TechName);

        if (string.IsNullOrWhiteSpace(WorkingCopy.Name))
        {
            ValidationMessage = "A Complement name is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(WorkingCopy.TechName))
            WorkingCopy.TechName = EditableDomainNaming.ToTechName(WorkingCopy.Name);

        if (string.IsNullOrWhiteSpace(WorkingCopy.Id))
            WorkingCopy.Id = Guid.NewGuid().ToString("D");

        if (string.IsNullOrWhiteSpace(WorkingCopy.Kind))
            WorkingCopy.Kind = WorkingCopy.Name;

        var duplicate = domain.ComplementDefinitions.Any(item =>
            !string.Equals(item.Id, WorkingCopy.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.TechName, WorkingCopy.TechName, StringComparison.OrdinalIgnoreCase));

        if (duplicate)
        {
            ValidationMessage = "Another Complement already uses Tech-Name '" + WorkingCopy.TechName + "'.";
            return false;
        }

        WorkingCopy.ForegroundColorHex = NormalizeHex(WorkingCopy.ForegroundColorHex, "#FF1D4ED8");
        WorkingCopy.BackgroundColorHex = NormalizeHex(WorkingCopy.BackgroundColorHex, "#FFF8FAFC");
        WorkingCopy.LineThickness = PositiveOrDefault(WorkingCopy.LineThickness, 1.5);
        WorkingCopy.InitialWidth = PositiveOrDefault(WorkingCopy.InitialWidth, 180);
        WorkingCopy.InitialHeight = PositiveOrDefault(WorkingCopy.InitialHeight, 80);
        ValidationMessage = string.Empty;
        return true;
    }

    public static EditableComplementDefinition CreateNewComplement(IEnumerable<EditableComplementDefinition> existing)
    {
        var existingList = (existing ?? Enumerable.Empty<EditableComplementDefinition>()).ToArray();
        var name = MakeUniqueName("Complement", existingList.Select(item => item.Name), "Complement");
        var complement = EditableComplementDefinition.CreateDefault(name, "#FFF8FAFC");
        complement.TechName = EditableDomainNaming.MakeUniqueTechName(name, existingList.Select(item => item.TechName));
        return complement;
    }

    public static EditableComplementDefinition DuplicateComplement(EditableComplementDefinition source, IEnumerable<EditableComplementDefinition> existing)
    {
        var existingList = (existing ?? Enumerable.Empty<EditableComplementDefinition>()).ToArray();
        var copy = source.Clone();
        copy.Id = Guid.NewGuid().ToString("D");
        copy.Name = MakeUniqueName(source.Name + " Copy", existingList.Select(item => item.Name), "Complement");
        copy.TechName = EditableDomainNaming.MakeUniqueTechName(copy.Name, existingList.Select(item => item.TechName));
        return copy;
    }

    private static string NormalizeHex(string value, string fallback)
    {
        return TcColor.TryParseHex(value, out var color) ? color.ToHexArgb() : fallback;
    }

    private static double PositiveOrDefault(double value, double fallback)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value) ? value : fallback;
    }

    private static string FirstText(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string MakeUniqueName(string preferredName, IEnumerable<string> existingNames, string fallback)
    {
        var name = string.IsNullOrWhiteSpace(preferredName) ? fallback : preferredName.Trim();
        var existing = new HashSet<string>(existingNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(name))
            return name;

        for (var index = 2; index < 10000; index++)
        {
            var candidate = name + " " + index.ToString(CultureInfo.InvariantCulture);
            if (!existing.Contains(candidate))
                return candidate;
        }

        return name + " " + Guid.NewGuid().ToString("N").Substring(0, 8);
    }
}
