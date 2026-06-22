namespace Instrumind.ThinkComposer.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Instrumind.Common.Portable;

public sealed class ConceptDefinitionEditorViewModel
{
    private readonly EditableDomainModel domain;
    private readonly EditableConceptDefinition original;

    public ConceptDefinitionEditorViewModel(EditableDomainModel domain, EditableConceptDefinition concept, bool isNew)
    {
        this.domain = domain ?? throw new ArgumentNullException(nameof(domain));
        original = concept;
        IsNew = isNew;
        WorkingCopy = concept == null ? new EditableConceptDefinition() : concept.Clone();
        ValidationMessage = string.Empty;
    }

    public EditableConceptDefinition WorkingCopy { get; }

    public bool IsNew { get; }

    public string ValidationMessage { get; private set; }

    public bool TryApply()
    {
        if (!Validate())
            return false;

        var copy = WorkingCopy.Clone();
        if (copy.Symbol == null)
            copy.Symbol = EditableConceptSymbolFormat.CreateDefault();

        copy.RepresentativeShape = ThinkComposerVisualCatalog.NormalizeShapeTechName(copy.RepresentativeShape, "Capsule");
        copy.Symbol.Shape = copy.RepresentativeShape;

        if (IsNew)
        {
            domain.ConceptDefinitions.Add(copy);
        }
        else
        {
            var index = domain.ConceptDefinitions.FindIndex(concept => concept.Id == original.Id);
            if (index < 0)
            {
                ValidationMessage = "The Concept being edited is no longer available.";
                return false;
            }

            domain.ConceptDefinitions[index] = copy;
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
            ValidationMessage = "A Concept name is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(WorkingCopy.TechName))
            WorkingCopy.TechName = EditableDomainNaming.ToTechName(WorkingCopy.Name);

        if (string.IsNullOrWhiteSpace(WorkingCopy.Id))
            WorkingCopy.Id = Guid.NewGuid().ToString("D");

        WorkingCopy.RepresentativeShape = ThinkComposerVisualCatalog.NormalizeShapeTechName(WorkingCopy.RepresentativeShape, "Capsule");

        var duplicate = domain.ConceptDefinitions.Any(concept =>
            !string.Equals(concept.Id, WorkingCopy.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(concept.TechName, WorkingCopy.TechName, StringComparison.OrdinalIgnoreCase));

        if (duplicate)
        {
            ValidationMessage = "Another Concept already uses Tech-Name '" + WorkingCopy.TechName + "'.";
            return false;
        }

        if (WorkingCopy.Symbol == null)
            WorkingCopy.Symbol = EditableConceptSymbolFormat.CreateDefault();

        WorkingCopy.Symbol.FillColorHex = NormalizeHex(WorkingCopy.Symbol.FillColorHex, "#FFFFE540");
        WorkingCopy.Symbol.StrokeColorHex = NormalizeHex(WorkingCopy.Symbol.StrokeColorHex, "#FFD4A900");
        WorkingCopy.Symbol.LineThickness = PositiveOrDefault(WorkingCopy.Symbol.LineThickness, 1.5);
        WorkingCopy.Symbol.InitialWidth = PositiveOrDefault(WorkingCopy.Symbol.InitialWidth, 110);
        WorkingCopy.Symbol.InitialHeight = PositiveOrDefault(WorkingCopy.Symbol.InitialHeight, 38);
        WorkingCopy.Symbol.Shape = ThinkComposerVisualCatalog.NormalizeShapeTechName(WorkingCopy.Symbol.Shape, WorkingCopy.RepresentativeShape);
        WorkingCopy.RepresentativeShape = WorkingCopy.Symbol.Shape;
        ValidationMessage = string.Empty;
        return true;
    }

    private static string NormalizeHex(string value, string fallback)
    {
        return TcColor.TryParseHex(value, out var color)
            ? color.ToHexArgb()
            : fallback;
    }

    private static double PositiveOrDefault(double value, double fallback)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value)
            ? value
            : fallback;
    }

    private static string FirstText(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    public static EditableConceptDefinition CreateNewConcept(IEnumerable<EditableConceptDefinition> existing)
    {
        var existingList = (existing ?? Enumerable.Empty<EditableConceptDefinition>()).ToArray();
        var name = MakeUniqueName("Concept", existingList.Select(concept => concept.Name));
        var concept = EditableConceptDefinition.CreateDefault(name, "Capsule", "#FFFFE540", "#FFD4A900");
        concept.TechName = EditableDomainNaming.MakeUniqueTechName(name, existingList.Select(item => item.TechName));
        return concept;
    }

    public static EditableConceptDefinition DuplicateConcept(EditableConceptDefinition source, IEnumerable<EditableConceptDefinition> existing)
    {
        var existingList = (existing ?? Enumerable.Empty<EditableConceptDefinition>()).ToArray();
        var copy = source.Clone();
        copy.Id = Guid.NewGuid().ToString("D");
        copy.Name = MakeUniqueName(source.Name + " Copy", existingList.Select(concept => concept.Name));
        copy.TechName = EditableDomainNaming.MakeUniqueTechName(copy.Name, existingList.Select(item => item.TechName));
        return copy;
    }

    private static string MakeUniqueName(string preferredName, IEnumerable<string> existingNames)
    {
        var name = string.IsNullOrWhiteSpace(preferredName) ? "Concept" : preferredName.Trim();
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
