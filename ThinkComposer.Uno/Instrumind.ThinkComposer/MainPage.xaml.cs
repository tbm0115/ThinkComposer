namespace Instrumind.ThinkComposer;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using Instrumind.Common.Platform;
using Instrumind.Common.Portable;
using Instrumind.ThinkComposer.Services;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

public sealed partial class MainPage : Page
{
    private static readonly string[] SupportedFileTypes =
    {
        ".tdom",
        ".tcom",
        ".tct",
        ".tdom.json",
        ".json",
        ".xml"
    };

    private readonly ITemplateRenderer templateRenderer;
    private readonly IPdfReportExporter pdfReportExporter;
    private readonly IEditableDomainStore editableDomainStore;
    private readonly PdfReportDocument reportDocument;

    private readonly ObservableCollection<DomainCatalogItemViewModel> domainCatalogItems = new();
    private readonly ObservableCollection<string> interrelationItems = new();
    private readonly ObservableCollection<string> documentDetails = new();
    private readonly ObservableCollection<string> documentItems = new();
    private readonly ObservableCollection<string> messageItems = new();
    private readonly ObservableCollection<PaletteItemViewModel> conceptPaletteItems = new();
    private readonly ObservableCollection<PaletteItemViewModel> relationshipPaletteItems = new();
    private readonly ObservableCollection<PaletteItemViewModel> markerPaletteItems = new();
    private readonly ObservableCollection<PaletteItemViewModel> complementPaletteItems = new();
    private readonly ObservableCollection<ConceptStylePresetViewModel> conceptStylePresetItems = new();
    private readonly ObservableCollection<ShapeOptionViewModel> shapeOptionItems = new();
    private IReadOnlyList<ContentTreeItemViewModel> contentTreeItems = Array.Empty<ContentTreeItemViewModel>();
    private ThinkComposerFileSummary? currentSummary;
    private EditableDomainModel? currentDomain;
    private ConceptDefinitionEditorViewModel? conceptEditor;
    private RelationshipDefinitionEditorViewModel? relationshipEditor;
    private MarkerDefinitionEditorViewModel? markerEditor;
    private ComplementDefinitionEditorViewModel? complementEditor;

    private static readonly IReadOnlyList<string> ConceptShapeNames = ThinkComposerVisualCatalog.ShapeDisplayNames;

    private static readonly IReadOnlyList<string> RelationshipShapeNames = ThinkComposerVisualCatalog.ShapeDisplayNames;

    private static readonly string[] DispositionNames =
    {
        "Hidden",
        "Left",
        "Right",
        "Top",
        "Bottom"
    };

    private static readonly string[] PositioningModes =
    {
        "Vertical Alternated",
        "Horizontal Alternated",
        "Radial",
        "Cascade"
    };

    private static readonly string[] TemplateLanguages =
    {
        "Text",
        "HTML",
        "JSON",
        "XML",
        "C#"
    };

    private static readonly string[] ConnectorDashNames =
    {
        "Solid",
        "Dashed",
        "Dotted"
    };

    private static readonly IReadOnlyList<string> ConnectorPlugNames = ThinkComposerVisualCatalog.ConnectorPlugDisplayNames;

    private static readonly IReadOnlyList<string> LinkRoleVariantNames = ThinkComposerVisualCatalog.LinkRoleVariantDisplayNames;

    private static readonly string[] LinkRoleTypeNames =
    {
        "Origin",
        "Participant",
        "Target"
    };

    private static readonly string[] ConnectorPathStyleNames =
    {
        "Straight",
        "Right Angle",
        "Curve"
    };

    private static readonly string[] ConnectorPathCornerNames =
    {
        "Sharp",
        "Round"
    };

    private static readonly string[] ComplementKindNames =
    {
        "Text",
        "Image",
        "Callout",
        "Quote",
        "Group Region",
        "Group Line",
        "Note",
        "Stamp",
        "Info-Card",
        "Legend"
    };

    private static readonly string[] ComplementOrientationNames =
    {
        "Horizontal",
        "Vertical"
    };

    private static readonly string[] ComplementQuadrantNames =
    {
        "TopRight",
        "TopLeft",
        "BottomRight",
        "BottomLeft"
    };

    public MainPage()
    {
        this.InitializeComponent();

        templateRenderer = new DotLiquidTemplateRenderer();
        pdfReportExporter = new PdfSharpCoreReportExporter();
        editableDomainStore = new EditableDomainJsonStore();
        reportDocument = CreateInitialReportDocument();

        DomainCatalogView.ItemsSource = domainCatalogItems;
        InterrelationsList.ItemsSource = interrelationItems;
        DocumentDetailsList.ItemsSource = documentDetails;
        DocumentItemsList.ItemsSource = documentItems;
        MessageList.ItemsSource = messageItems;
        ConceptPaletteList.ItemsSource = conceptPaletteItems;
        RelationshipPaletteList.ItemsSource = relationshipPaletteItems;
        MarkerPaletteList.ItemsSource = markerPaletteItems;
        ComplementPaletteList.ItemsSource = complementPaletteItems;
        ConceptStylePresetGrid.ItemsSource = conceptStylePresetItems;
        ConceptRepresentativeShapeGrid.ItemsSource = shapeOptionItems;
        ConceptSymbolShapeGrid.ItemsSource = shapeOptionItems;
        RelationshipRepresentativeShapeGrid.ItemsSource = shapeOptionItems;
        RelationshipSymbolShapeGrid.ItemsSource = shapeOptionItems;

        InitializeConceptEditorLists();
        PopulateConceptStylePresets();
        PopulateShapeOptions();

        ClearPaletteModels();
        ResetNavigationModels();
        ShowBlankWorkspace();
        LogMessage("ThinkComposer Uno shell initialized.");
        _ = LoadDomainCatalogAsync();
    }

    private static PdfReportDocument CreateInitialReportDocument()
    {
        var document = new PdfReportDocument
        {
            Title = "Untitled composition"
        };

        var section = new PdfReportSection
        {
            Heading = "Summary"
        };
        section.Blocks.Add(new PdfParagraphBlock
        {
            Text = "ThinkComposer portable report export is routed through IPdfReportExporter.",
            Format = new TcTextFormat
            {
                FontSize = 12,
                Foreground = TcColor.FromRgb(31, 41, 55)
            }
        });
        document.Sections.Add(section);

        return document;
    }

    private void InitializeConceptEditorLists()
    {
        ConceptRepresentativeShapeComboBox.ItemsSource = ConceptShapeNames;
        ConceptSymbolShapeComboBox.ItemsSource = ConceptShapeNames;
        ConceptSubtitleDispositionComboBox.ItemsSource = DispositionNames;
        ConceptPictogramDispositionComboBox.ItemsSource = DispositionNames;
        ConceptPositioningModeComboBox.ItemsSource = PositioningModes;
        ConceptTemplateLanguageComboBox.ItemsSource = TemplateLanguages;
        RelationshipRepresentativeShapeComboBox.ItemsSource = RelationshipShapeNames;
        RelationshipSymbolShapeComboBox.ItemsSource = RelationshipShapeNames;
        RelationshipConnectorDashComboBox.ItemsSource = ConnectorDashNames;
        RelationshipHeadPlugComboBox.ItemsSource = ConnectorPlugNames;
        RelationshipTailPlugComboBox.ItemsSource = ConnectorPlugNames;
        RelationshipHeadVariantComboBox.ItemsSource = LinkRoleVariantNames;
        RelationshipTailVariantComboBox.ItemsSource = LinkRoleVariantNames;
        RelationshipConnectorPathStyleComboBox.ItemsSource = ConnectorPathStyleNames;
        RelationshipConnectorPathCornerComboBox.ItemsSource = ConnectorPathCornerNames;
        RelationshipOriginRoleTypeComboBox.ItemsSource = LinkRoleTypeNames;
        RelationshipTargetRoleTypeComboBox.ItemsSource = LinkRoleTypeNames;
        RelationshipOriginAllowedVariantComboBox.ItemsSource = LinkRoleVariantNames;
        RelationshipTargetAllowedVariantComboBox.ItemsSource = LinkRoleVariantNames;
        RelationshipTemplateLanguageComboBox.ItemsSource = TemplateLanguages;
        MarkerClusterComboBox.ItemsSource = new[] { "UserDef", "Normal", "Validation", "Review" };
        ComplementKindComboBox.ItemsSource = ComplementKindNames;
        ComplementDashComboBox.ItemsSource = ConnectorDashNames;
        ComplementOrientationComboBox.ItemsSource = ComplementOrientationNames;
        ComplementQuadrantComboBox.ItemsSource = ComplementQuadrantNames;
    }

    private void PopulateConceptStylePresets()
    {
        conceptStylePresetItems.Clear();
        foreach (var preset in ThinkComposerVisualCatalog.GraphicStylePresets)
            conceptStylePresetItems.Add(new ConceptStylePresetViewModel(preset));
    }

    private void PopulateShapeOptions()
    {
        shapeOptionItems.Clear();
        foreach (var option in ThinkComposerVisualCatalog.ShapeOptions)
            shapeOptionItems.Add(new ShapeOptionViewModel(option));
    }

    private void PopulatePaletteModels(EditableDomainModel? domain)
    {
        if (domain == null)
        {
            ClearPaletteModels();
            return;
        }

        RefreshConceptPalette();
        RefreshRelationshipPalette();
        RefreshMarkerPalette();
        RefreshComplementPalette();
    }

    private void RefreshConceptPalette()
    {
        if (currentDomain == null)
        {
            conceptPaletteItems.Clear();
            return;
        }

        var query = ConceptSearchTextBox?.Text?.Trim() ?? string.Empty;
        var concepts = currentDomain.ConceptDefinitions
            .Where(concept => string.IsNullOrWhiteSpace(query)
                || concept.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || concept.TechName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || concept.Summary.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(concept => concept.Name, StringComparer.OrdinalIgnoreCase)
            .Take(80)
            .Select(CreateConceptPaletteItem)
            .ToArray();

        Replace(conceptPaletteItems, concepts.Length == 0 && string.IsNullOrWhiteSpace(query)
            ? DefaultConceptPaletteItems()
            : concepts);
    }

    private void RefreshRelationshipPalette()
    {
        if (currentDomain == null)
        {
            relationshipPaletteItems.Clear();
            return;
        }

        var query = RelationshipSearchTextBox?.Text?.Trim() ?? string.Empty;
        var relationships = currentDomain.RelationshipDefinitions
            .Where(item => DomainDefinitionMatches(item.Name, item.TechName, item.Summary, query))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(48)
            .Select(CreateRelationshipPaletteItem)
            .ToArray();

        Replace(relationshipPaletteItems, relationships.Length == 0 && string.IsNullOrWhiteSpace(query)
            ? DefaultRelationshipPaletteItems()
            : relationships);
    }

    private void RefreshMarkerPalette()
    {
        if (currentDomain == null)
        {
            markerPaletteItems.Clear();
            return;
        }

        var query = MarkerSearchTextBox?.Text?.Trim() ?? string.Empty;
        var markers = currentDomain.MarkerDefinitions
            .Where(item => DomainDefinitionMatches(item.Name, item.TechName, item.Summary, query))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(48)
            .Select(CreateMarkerPaletteItem)
            .ToArray();

        Replace(markerPaletteItems, markers.Length == 0 && string.IsNullOrWhiteSpace(query)
            ? DefaultMarkerPaletteItems()
            : markers);
    }

    private void RefreshComplementPalette()
    {
        if (currentDomain == null)
        {
            complementPaletteItems.Clear();
            return;
        }

        var query = ComplementSearchTextBox?.Text?.Trim() ?? string.Empty;
        var complements = currentDomain.ComplementDefinitions
            .Where(item => DomainDefinitionMatches(item.Name, item.TechName, item.Summary, query))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(48)
            .Select(CreateComplementPaletteItem)
            .ToArray();

        Replace(complementPaletteItems, complements.Length == 0 && string.IsNullOrWhiteSpace(query)
            ? DefaultComplementPaletteItems()
            : complements);
    }

    private static bool DomainDefinitionMatches(string name, string techName, string summary, string query)
    {
        return string.IsNullOrWhiteSpace(query)
            || (name ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase)
            || (techName ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase)
            || (summary ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<PaletteItemViewModel> DefaultConceptPaletteItems()
    {
        return ThinkComposerVisualCatalog.DefaultConceptStyles
            .Select(style => Palette(style.Name, style.Shape, style.FillColorHex, style.StrokeColorHex))
            .ToArray();
    }

    private static IReadOnlyList<PaletteItemViewModel> DefaultRelationshipPaletteItems()
    {
        return new[]
        {
            Palette("Relationship", 31, 41, 55),
            Palette("Reference", 100, 116, 139),
            Palette("Flow", 79, 70, 229),
            Palette("Message", 139, 92, 246),
            Palette("Condition", 250, 204, 21),
            Palette("Is a", 244, 63, 94),
            Palette("Dependency", 148, 163, 184),
            Palette("Composed of", 45, 212, 191),
            Palette("Has", 20, 184, 166),
            Palette("Defined by", 244, 63, 94)
        };
    }

    private static IReadOnlyList<PaletteItemViewModel> DefaultMarkerPaletteItems()
    {
        return new[]
        {
            Palette("Normal", 251, 191, 36),
            Palette("Correct", 34, 197, 94),
            Palette("Incorrect", 239, 68, 68),
            Palette("Warning", 245, 158, 11),
            Palette("Dangerous", 220, 38, 38),
            Palette("Strange", 168, 85, 247),
            Palette("Sensitive", 236, 72, 153),
            Palette("Extinct", 100, 116, 139)
        };
    }

    private static IReadOnlyList<PaletteItemViewModel> DefaultComplementPaletteItems()
    {
        return new[]
        {
            Palette("Text", 59, 130, 246),
            Palette("Image", 34, 197, 94),
            Palette("Callout", 251, 191, 36),
            Palette("Group Region", 14, 165, 233),
            Palette("Group Line", 244, 63, 94),
            Palette("Quote", 148, 163, 184),
            Palette("Note", 253, 224, 71),
            Palette("Stamp", 168, 85, 247)
        };
    }

    private void ResetNavigationModels()
    {
        currentSummary = null;
        currentDomain = null;
        conceptEditor = null;
        contentTreeItems = Array.Empty<ContentTreeItemViewModel>();
        RefreshContentTree();

        Replace(interrelationItems, new[]
        {
            "Pointed by...",
            "Pointing to..."
        });

        Replace(documentDetails, Array.Empty<string>());
        Replace(documentItems, Array.Empty<string>());
        SnapshotImage.Source = null;
        SnapshotFrame.Visibility = Visibility.Collapsed;
    }

    private void ClearPaletteModels()
    {
        conceptPaletteItems.Clear();
        relationshipPaletteItems.Clear();
        markerPaletteItems.Clear();
        complementPaletteItems.Clear();
    }

    private async Task LoadDomainCatalogAsync()
    {
        var folder = FindPredefinedContentFolder();
        DomainFolderText.Text = folder ?? "(PredefinedContent folder not found)";

        if (folder == null)
        {
            UpdateStatus("Predefined domain folder was not found.");
            return;
        }

        var entries = await Task.Run(() => DomainCatalogService.Load(folder));
        domainCatalogItems.Clear();

        foreach (var entry in entries)
        {
            domainCatalogItems.Add(new DomainCatalogItemViewModel
            {
                FullPath = entry.FullPath,
                FileName = entry.FileName,
                Name = entry.Name,
                Summary = entry.Summary,
                Version = entry.Version,
                Snapshot = await CreateOptionalBitmapAsync(entry.SnapshotImageBytes),
                Pictogram = await CreateOptionalBitmapAsync(entry.PictogramImageBytes)
            });
        }

        LogMessage("Detected " + domainCatalogItems.Count + " predefined domains.");
        UpdateStatus("Ready. Click New to create a composition, or Open an existing .tcom file.");
    }

    private void ShowBlankWorkspace()
    {
        DomainDialogOverlay.Visibility = Visibility.Collapsed;
        BlankWorkspacePanel.Visibility = Visibility.Visible;
        DocumentWorkspacePanel.Visibility = Visibility.Collapsed;
        DocumentTabStrip.Visibility = Visibility.Collapsed;
    }

    private void ShowDocumentWorkspace()
    {
        DomainDialogOverlay.Visibility = Visibility.Collapsed;
        BlankWorkspacePanel.Visibility = Visibility.Collapsed;
        DocumentWorkspacePanel.Visibility = Visibility.Visible;
        DocumentTabStrip.Visibility = Visibility.Visible;
        MainTabText.Text = "Main View";
    }

    private void ShowDomainDialog()
    {
        DomainDialogOverlay.Visibility = Visibility.Visible;
        UpdateStatus("Select a domain file to create a composition.");
    }

    private void HideDomainDialog()
    {
        DomainDialogOverlay.Visibility = Visibility.Collapsed;
        DomainCatalogView.SelectedItem = null;
    }

    private void NewCompositionClicked(object sender, RoutedEventArgs e)
    {
        ShowDomainDialog();
        LogMessage("Creating new Composition of Domain...");
    }

    private async void DomainCatalogSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DomainCatalogView.SelectedItem is not DomainCatalogItemViewModel selected)
            return;

        FilePathTextBox.Text = selected.FullPath;
        LogMessage("Domain selected: " + selected.Name);
        HideDomainDialog();
        await LoadFileAsync(selected.FullPath);
    }

    private void DomainDialogCancelClicked(object sender, RoutedEventArgs e)
    {
        HideDomainDialog();
        UpdateStatus("Domain selection cancelled.");
    }

    private void DomainDialogBasicClicked(object sender, RoutedEventArgs e)
    {
        HideDomainDialog();
        ResetNavigationModels();
        currentDomain = CreateBasicEditableDomain();
        PopulatePaletteModels(currentDomain);
        ShowBlankWorkspace();
        LogMessage("Basic Domain selected. Portable composition creation is pending.");
        UpdateStatus("Basic Domain is editable in the Domain toolbox.");
    }

    private async void OpenClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            foreach (var fileType in SupportedFileTypes)
                picker.FileTypeFilter.Add(fileType);

            var file = await picker.PickSingleFileAsync();
            if (file == null)
            {
                UpdateStatus("Open cancelled.");
                return;
            }

            FilePathTextBox.Text = file.Path;
            await LoadFileAsync(file.Path);
        }
        catch (Exception exception)
        {
            RouteBar.Visibility = Visibility.Visible;
            UpdateStatus("Browse is unavailable in this host. Paste a file path and press Load. " + exception.Message);
        }
    }

    private async void LoadPathClicked(object sender, RoutedEventArgs e)
    {
        await LoadFileAsync(FilePathTextBox.Text);
    }

    private async void OpenSampleClicked(object sender, RoutedEventArgs e)
    {
        var sample = FindSampleDomain();
        if (sample == null)
        {
            UpdateStatus("Sample domain was not found under the repository PredefinedContent folder.");
            return;
        }

        FilePathTextBox.Text = sample;
        HideDomainDialog();
        await LoadFileAsync(sample);
    }

    private async void SaveClicked(object sender, RoutedEventArgs e)
    {
        await SaveEditableDomainAsync("Save");
    }

    private async void CommandPlaceholderClicked(object sender, RoutedEventArgs e)
    {
        var command = (sender as FrameworkElement)?.Tag as string ?? "Command";
        if (command.Equals("Export JSON", StringComparison.OrdinalIgnoreCase))
        {
            await SaveEditableDomainAsync("Export JSON");
            return;
        }

        UpdateStatus(command + " is in the parity shell; its migrated service is not connected yet.");
        LogMessage(command + " command reached the Uno shell.");
    }

    private void ExportPdfClicked(object sender, RoutedEventArgs e)
    {
        UpdateStatus("Direct PDF export is available through " + pdfReportExporter.GetType().Name + "; file save UI is next.");
        LogMessage("PDF report command reached IPdfReportExporter.");
    }

    private async Task LoadFileAsync(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            UpdateStatus("Enter a file path first.");
            return;
        }

        try
        {
            UpdateStatus("Loading " + route + " ...");
            var summary = await Task.Run(() => ThinkComposerFileProbe.Load(route));
            await ApplyFileSummaryAsync(summary);
        }
        catch (Exception exception)
        {
            UpdateStatus("Could not load file: " + exception.Message);
            LogMessage("Load failed: " + exception.Message);
        }
    }

    private async Task ApplyFileSummaryAsync(ThinkComposerFileSummary summary)
    {
        currentSummary = summary;
        currentDomain = await LoadEditableDomainAsync(summary);
        PopulatePaletteModels(currentDomain);
        ShowDocumentWorkspace();

        CompositionTitle.Text = Path.GetFileNameWithoutExtension(summary.FileName);
        CompositionStatus.Text = summary.Kind;
        reportDocument.Title = CompositionTitle.Text;

        var packagePictogram = await CreateOptionalBitmapAsync(summary.PictogramImageBytes);
        contentTreeItems = BuildContentTreeItems(summary, packagePictogram);
        RefreshContentTree();
        Replace(interrelationItems, BuildInterrelationItems(summary));
        Replace(documentDetails, summary.Details);
        Replace(documentItems, summary.Items.Concat(BuildModelDiagnosticItems(summary.LegacyModel)));
        await ShowSnapshotAsync(summary);

        UpdateStatus("Loaded " + summary.FullPath);
        LogMessage("Loaded " + summary.Kind + ": " + summary.FileName);
        RefreshDomainDirtyState();
    }

    private async Task<EditableDomainModel> LoadEditableDomainAsync(ThinkComposerFileSummary summary)
    {
        var storedDomain = await editableDomainStore.TryLoadAsync(summary.FullPath, CancellationToken.None);
        if (storedDomain != null)
        {
            LogMessage("Loaded Domain JSON sidecar: " + Path.GetFileName(storedDomain.SidecarPath));
            return storedDomain;
        }

        var projectedDomain = ProjectEditableDomain(summary);
        LogMessage("Projected editable Domain model from package facts.");
        return projectedDomain;
    }

    private EditableDomainModel ProjectEditableDomain(ThinkComposerFileSummary summary)
    {
        var name = Path.GetFileNameWithoutExtension(summary.FileName);
        var domain = new EditableDomainModel
        {
            Name = name,
            TechName = EditableDomainNaming.ToTechName(name),
            Summary = summary.Kind,
            SourcePath = summary.FullPath,
            SidecarPath = editableDomainStore.GetSidecarPath(summary.FullPath),
            IsProjectedFromLegacyPackage = summary.LegacyModel != null,
            IsDirty = false
        };

        var concepts = summary.LegacyModel?.DomainConceptNames;
        var relationships = summary.LegacyModel?.DomainRelationshipNames;
        var markers = summary.LegacyModel?.DomainMarkerNames;
        var complements = summary.LegacyModel?.DomainComplementNames;

        AddProjectedConcepts(domain, concepts != null && concepts.Count > 0
            ? concepts
            : DefaultConceptPaletteItems().Select(item => item.Name).ToArray());

        AddProjectedRelationships(domain.RelationshipDefinitions, relationships, DefaultRelationshipPaletteItems(), "#FF64748B");
        AddProjectedMarkers(domain.MarkerDefinitions, markers, DefaultMarkerPaletteItems(), "#FFFBBF24");
        AddProjectedComplements(domain.ComplementDefinitions, complements, DefaultComplementPaletteItems(), "#FF3B82F6");
        return domain;
    }

    private EditableDomainModel CreateBasicEditableDomain()
    {
        var domain = new EditableDomainModel
        {
            Name = "Basic Domain",
            TechName = "Basic_Domain",
            Summary = "Portable editable domain",
            SourcePath = currentSummary?.FullPath ?? string.Empty,
            SidecarPath = currentSummary == null ? string.Empty : editableDomainStore.GetSidecarPath(currentSummary.FullPath),
            IsDirty = true
        };

        AddProjectedConcepts(domain, DefaultConceptPaletteItems().Select(item => item.Name).ToArray());
        AddProjectedRelationships(domain.RelationshipDefinitions, null, DefaultRelationshipPaletteItems(), "#FF64748B");
        AddProjectedMarkers(domain.MarkerDefinitions, null, DefaultMarkerPaletteItems(), "#FFFBBF24");
        AddProjectedComplements(domain.ComplementDefinitions, null, DefaultComplementPaletteItems(), "#FF3B82F6");
        return domain;
    }

    private static void AddProjectedConcepts(EditableDomainModel domain, IReadOnlyList<string> names)
    {
        var defaults = DefaultConceptPaletteItems().ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var usedTechNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        foreach (var rawName in names)
        {
            var name = string.IsNullOrWhiteSpace(rawName) ? "Concept" : rawName.Trim();
            if (!usedTechNames.Add(EditableDomainNaming.ToTechName(name)))
                continue;

            defaults.TryGetValue(name, out var defaultItem);
            var projectedStyle = ThinkComposerVisualCatalog.GetDefaultConceptStyle(name, index);
            var concept = EditableConceptDefinition.CreateDefault(
                name,
                projectedStyle.Shape,
                defaultItem?.FillColorHex ?? projectedStyle.FillColorHex,
                defaultItem?.StrokeColorHex ?? projectedStyle.StrokeColorHex);

            concept.TechName = EditableDomainNaming.MakeUniqueTechName(name, domain.ConceptDefinitions.Select(item => item.TechName));
            concept.Summary = "Projected Concept definition";
            domain.ConceptDefinitions.Add(concept);
            index++;
        }
    }

    private static void AddProjectedRelationships(
        IList<EditableRelationshipDefinition> target,
        IReadOnlyList<string>? projectedNames,
        IReadOnlyList<PaletteItemViewModel> fallbackItems,
        string defaultColorHex)
    {
        var names = projectedNames != null && projectedNames.Count > 0
            ? projectedNames.Select(name => new PaletteItemViewModel { Name = name, FillColorHex = defaultColorHex, StrokeColorHex = defaultColorHex })
            : fallbackItems;

        foreach (var item in names)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                continue;

            var relationship = EditableRelationshipDefinition.CreateDefault(
                item.Name,
                string.IsNullOrWhiteSpace(item.FillColorHex) ? defaultColorHex : item.FillColorHex);
            relationship.Summary = "Projected Relationship definition";
            target.Add(relationship);
        }
    }

    private static void AddProjectedMarkers(
        IList<EditableMarkerDefinition> target,
        IReadOnlyList<string>? projectedNames,
        IReadOnlyList<PaletteItemViewModel> fallbackItems,
        string defaultColorHex)
    {
        var names = projectedNames != null && projectedNames.Count > 0
            ? projectedNames.Select(name => new PaletteItemViewModel { Name = name, FillColorHex = defaultColorHex, StrokeColorHex = defaultColorHex })
            : fallbackItems;

        foreach (var item in names)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                continue;

            var marker = EditableMarkerDefinition.CreateDefault(
                item.Name,
                string.IsNullOrWhiteSpace(item.FillColorHex) ? defaultColorHex : item.FillColorHex);
            marker.Summary = "Projected Marker definition";
            target.Add(marker);
        }
    }

    private static void AddProjectedComplements(
        IList<EditableComplementDefinition> target,
        IReadOnlyList<string>? projectedNames,
        IReadOnlyList<PaletteItemViewModel> fallbackItems,
        string defaultColorHex)
    {
        var names = projectedNames != null && projectedNames.Count > 0
            ? projectedNames.Select(name => new PaletteItemViewModel { Name = name, FillColorHex = defaultColorHex, StrokeColorHex = defaultColorHex })
            : fallbackItems;

        foreach (var item in names)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                continue;

            var complement = EditableComplementDefinition.CreateDefault(
                item.Name,
                string.IsNullOrWhiteSpace(item.FillColorHex) ? defaultColorHex : item.FillColorHex);
            target.Add(complement);
        }
    }

    private async Task SaveEditableDomainAsync(string commandName)
    {
        if (currentDomain == null)
        {
            UpdateStatus(commandName + " needs a loaded or selected Domain first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(currentDomain.SourcePath) && string.IsNullOrWhiteSpace(currentDomain.SidecarPath))
        {
            UpdateStatus("A file-backed Domain is required before saving JSON.");
            LogMessage(commandName + " skipped because the domain has no file route.");
            return;
        }

        try
        {
            await editableDomainStore.SaveAsync(currentDomain, CancellationToken.None);
            RefreshDomainDirtyState();
            UpdateStatus("Domain JSON saved: " + currentDomain.SidecarPath);
            LogMessage(commandName + " wrote " + Path.GetFileName(currentDomain.SidecarPath));
        }
        catch (Exception exception)
        {
            UpdateStatus("Could not save Domain JSON: " + exception.Message);
            LogMessage("Domain JSON save failed: " + exception.Message);
        }
    }

    private void ContentFindTextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshContentTree();
    }

    private void ContentSortToggleClicked(object sender, RoutedEventArgs e)
    {
        RefreshContentTree();
    }

    private void ConceptSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshConceptPalette();
    }

    private void ConceptAddClicked(object sender, RoutedEventArgs e)
    {
        EnsureEditableDomain();
        var concept = ConceptDefinitionEditorViewModel.CreateNewConcept(currentDomain!.ConceptDefinitions);
        OpenConceptEditor(concept, isNew: true);
    }

    private void ConceptEditClicked(object sender, RoutedEventArgs e)
    {
        var concept = GetSelectedConceptDefinition();
        if (concept == null)
        {
            UpdateStatus("Select a Concept definition to edit.");
            return;
        }

        OpenConceptEditor(concept, isNew: false);
    }

    private void ConceptDuplicateClicked(object sender, RoutedEventArgs e)
    {
        EnsureEditableDomain();
        var selected = GetSelectedConceptDefinition();
        if (selected == null)
        {
            UpdateStatus("Select a Concept definition to duplicate.");
            return;
        }

        OpenConceptEditor(
            ConceptDefinitionEditorViewModel.DuplicateConcept(selected, currentDomain!.ConceptDefinitions),
            isNew: true);
    }

    private void ConceptDeleteClicked(object sender, RoutedEventArgs e)
    {
        var concept = GetSelectedConceptDefinition();
        if (concept == null || currentDomain == null)
        {
            UpdateStatus("Select a Concept definition to delete.");
            return;
        }

        currentDomain.ConceptDefinitions.Remove(concept);
        currentDomain.IsDirty = true;
        RefreshConceptPalette();
        RefreshConceptEditorDefinitionLists();
        RefreshDomainDirtyState();
        UpdateStatus("Deleted Concept definition: " + concept.Name);
        LogMessage("Concept deleted from Domain toolbox: " + concept.Name);
    }

    private void ConceptPaletteDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var concept = GetSelectedConceptDefinition();
        if (concept != null)
            OpenConceptEditor(concept, isNew: false);
    }

    private EditableConceptDefinition? GetSelectedConceptDefinition()
    {
        return (ConceptPaletteList.SelectedItem as PaletteItemViewModel)?.ConceptDefinition;
    }

    private void RelationshipSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshRelationshipPalette();
    }

    private void RelationshipAddClicked(object sender, RoutedEventArgs e)
    {
        EnsureEditableDomain();
        var relationship = RelationshipDefinitionEditorViewModel.CreateNewRelationship(currentDomain!.RelationshipDefinitions);
        OpenRelationshipEditor(relationship, isNew: true);
    }

    private void RelationshipEditClicked(object sender, RoutedEventArgs e)
    {
        var relationship = GetSelectedRelationshipDefinition();
        if (relationship == null)
        {
            UpdateStatus("Select a Relationship definition to edit.");
            return;
        }

        OpenRelationshipEditor(relationship, isNew: false);
    }

    private void RelationshipDuplicateClicked(object sender, RoutedEventArgs e)
    {
        EnsureEditableDomain();
        var selected = GetSelectedRelationshipDefinition();
        if (selected == null)
        {
            UpdateStatus("Select a Relationship definition to duplicate.");
            return;
        }

        OpenRelationshipEditor(
            RelationshipDefinitionEditorViewModel.DuplicateRelationship(selected, currentDomain!.RelationshipDefinitions),
            isNew: true);
    }

    private void RelationshipDeleteClicked(object sender, RoutedEventArgs e)
    {
        var relationship = GetSelectedRelationshipDefinition();
        if (relationship == null || currentDomain == null)
        {
            UpdateStatus("Select a Relationship definition to delete.");
            return;
        }

        currentDomain.RelationshipDefinitions.Remove(relationship);
        currentDomain.IsDirty = true;
        RefreshRelationshipPalette();
        RefreshConceptEditorDefinitionLists();
        RefreshDomainDirtyState();
        UpdateStatus("Deleted Relationship definition: " + relationship.Name);
        LogMessage("Relationship deleted from Domain toolbox: " + relationship.Name);
    }

    private void RelationshipPaletteDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var relationship = GetSelectedRelationshipDefinition();
        if (relationship != null)
            OpenRelationshipEditor(relationship, isNew: false);
    }

    private EditableRelationshipDefinition? GetSelectedRelationshipDefinition()
    {
        return (RelationshipPaletteList.SelectedItem as PaletteItemViewModel)?.RelationshipDefinition;
    }

    private void MarkerSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshMarkerPalette();
    }

    private void MarkerAddClicked(object sender, RoutedEventArgs e)
    {
        EnsureEditableDomain();
        var marker = MarkerDefinitionEditorViewModel.CreateNewMarker(currentDomain!.MarkerDefinitions);
        OpenMarkerEditor(marker, isNew: true);
    }

    private void MarkerEditClicked(object sender, RoutedEventArgs e)
    {
        var marker = GetSelectedMarkerDefinition();
        if (marker == null)
        {
            UpdateStatus("Select a Marker definition to edit.");
            return;
        }

        OpenMarkerEditor(marker, isNew: false);
    }

    private void MarkerDuplicateClicked(object sender, RoutedEventArgs e)
    {
        EnsureEditableDomain();
        var selected = GetSelectedMarkerDefinition();
        if (selected == null)
        {
            UpdateStatus("Select a Marker definition to duplicate.");
            return;
        }

        OpenMarkerEditor(
            MarkerDefinitionEditorViewModel.DuplicateMarker(selected, currentDomain!.MarkerDefinitions),
            isNew: true);
    }

    private void MarkerDeleteClicked(object sender, RoutedEventArgs e)
    {
        var marker = GetSelectedMarkerDefinition();
        if (marker == null || currentDomain == null)
        {
            UpdateStatus("Select a Marker definition to delete.");
            return;
        }

        currentDomain.MarkerDefinitions.Remove(marker);
        currentDomain.IsDirty = true;
        RefreshMarkerPalette();
        RefreshDomainDirtyState();
        UpdateStatus("Deleted Marker definition: " + marker.Name);
        LogMessage("Marker deleted from Domain toolbox: " + marker.Name);
    }

    private void MarkerPaletteDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var marker = GetSelectedMarkerDefinition();
        if (marker != null)
            OpenMarkerEditor(marker, isNew: false);
    }

    private EditableMarkerDefinition? GetSelectedMarkerDefinition()
    {
        return (MarkerPaletteList.SelectedItem as PaletteItemViewModel)?.MarkerDefinition;
    }

    private void ComplementSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshComplementPalette();
    }

    private void ComplementAddClicked(object sender, RoutedEventArgs e)
    {
        EnsureEditableDomain();
        var complement = ComplementDefinitionEditorViewModel.CreateNewComplement(currentDomain!.ComplementDefinitions);
        OpenComplementEditor(complement, isNew: true);
    }

    private void ComplementEditClicked(object sender, RoutedEventArgs e)
    {
        var complement = GetSelectedComplementDefinition();
        if (complement == null)
        {
            UpdateStatus("Select a Complement definition to edit.");
            return;
        }

        OpenComplementEditor(complement, isNew: false);
    }

    private void ComplementDuplicateClicked(object sender, RoutedEventArgs e)
    {
        EnsureEditableDomain();
        var selected = GetSelectedComplementDefinition();
        if (selected == null)
        {
            UpdateStatus("Select a Complement definition to duplicate.");
            return;
        }

        OpenComplementEditor(
            ComplementDefinitionEditorViewModel.DuplicateComplement(selected, currentDomain!.ComplementDefinitions),
            isNew: true);
    }

    private void ComplementDeleteClicked(object sender, RoutedEventArgs e)
    {
        var complement = GetSelectedComplementDefinition();
        if (complement == null || currentDomain == null)
        {
            UpdateStatus("Select a Complement definition to delete.");
            return;
        }

        currentDomain.ComplementDefinitions.Remove(complement);
        currentDomain.IsDirty = true;
        RefreshComplementPalette();
        RefreshDomainDirtyState();
        UpdateStatus("Deleted Complement definition: " + complement.Name);
        LogMessage("Complement deleted from Domain toolbox: " + complement.Name);
    }

    private void ComplementPaletteDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var complement = GetSelectedComplementDefinition();
        if (complement != null)
            OpenComplementEditor(complement, isNew: false);
    }

    private EditableComplementDefinition? GetSelectedComplementDefinition()
    {
        return (ComplementPaletteList.SelectedItem as PaletteItemViewModel)?.ComplementDefinition;
    }

    private void PaletteSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (var item in e.RemovedItems.OfType<PaletteItemViewModel>())
            item.SetSelected(false);

        foreach (var item in e.AddedItems.OfType<PaletteItemViewModel>())
            item.SetSelected(true);
    }

    private void PaletteInlineEditClicked(object sender, RoutedEventArgs e)
    {
        var item = GetPaletteItemFromSender(sender);
        if (item == null)
            return;

        SelectPaletteItem(item);

        if (item.ConceptDefinition != null)
            ConceptEditClicked(sender, e);
        else if (item.RelationshipDefinition != null)
            RelationshipEditClicked(sender, e);
        else if (item.MarkerDefinition != null)
            MarkerEditClicked(sender, e);
        else if (item.ComplementDefinition != null)
            ComplementEditClicked(sender, e);
    }

    private void PaletteInlineDuplicateClicked(object sender, RoutedEventArgs e)
    {
        var item = GetPaletteItemFromSender(sender);
        if (item == null)
            return;

        SelectPaletteItem(item);

        if (item.ConceptDefinition != null)
            ConceptDuplicateClicked(sender, e);
        else if (item.RelationshipDefinition != null)
            RelationshipDuplicateClicked(sender, e);
        else if (item.MarkerDefinition != null)
            MarkerDuplicateClicked(sender, e);
        else if (item.ComplementDefinition != null)
            ComplementDuplicateClicked(sender, e);
    }

    private void PaletteInlineDeleteClicked(object sender, RoutedEventArgs e)
    {
        var item = GetPaletteItemFromSender(sender);
        if (item == null)
            return;

        SelectPaletteItem(item);

        if (item.ConceptDefinition != null)
            ConceptDeleteClicked(sender, e);
        else if (item.RelationshipDefinition != null)
            RelationshipDeleteClicked(sender, e);
        else if (item.MarkerDefinition != null)
            MarkerDeleteClicked(sender, e);
        else if (item.ComplementDefinition != null)
            ComplementDeleteClicked(sender, e);
    }

    private static PaletteItemViewModel? GetPaletteItemFromSender(object sender)
    {
        return (sender as FrameworkElement)?.DataContext as PaletteItemViewModel;
    }

    private void SelectPaletteItem(PaletteItemViewModel item)
    {
        if (item.ConceptDefinition != null)
            ConceptPaletteList.SelectedItem = item;
        else if (item.RelationshipDefinition != null)
            RelationshipPaletteList.SelectedItem = item;
        else if (item.MarkerDefinition != null)
            MarkerPaletteList.SelectedItem = item;
        else if (item.ComplementDefinition != null)
            ComplementPaletteList.SelectedItem = item;
    }

    private void EnsureEditableDomain()
    {
        if (currentDomain != null)
            return;

        currentDomain = currentSummary == null
            ? CreateBasicEditableDomain()
            : ProjectEditableDomain(currentSummary);

        PopulatePaletteModels(currentDomain);
    }

    private void OpenRelationshipEditor(EditableRelationshipDefinition relationship, bool isNew)
    {
        EnsureEditableDomain();
        relationshipEditor = new RelationshipDefinitionEditorViewModel(currentDomain!, relationship, isNew);
        RelationshipEditorTitleText.Text = (isNew ? "Add" : "Edit") + " Relationship Definition - " + relationshipEditor.WorkingCopy.Name;
        RelationshipEditorValidationText.Text = string.Empty;
        PopulateRelationshipEditor(relationshipEditor.WorkingCopy);
        SetRelationshipEditorTab("General");
        RelationshipEditorOverlay.Visibility = Visibility.Visible;
        UpdateStatus((isNew ? "Adding" : "Editing") + " Relationship definition.");
    }

    private void PopulateRelationshipEditor(EditableRelationshipDefinition relationship)
    {
        relationship.Symbol ??= EditableConceptSymbolFormat.CreateDefault();
        relationship.Connector ??= EditableConnectorFormat.CreateDefault();
        relationship.OriginRole ??= EditableLinkRoleDefinition.Create("Origin");
        relationship.TargetRole ??= EditableLinkRoleDefinition.Create("Target");

        RelationshipNameTextBox.Text = relationship.Name;
        RelationshipTechNameTextBox.Text = relationship.TechName;
        RelationshipSummaryTextBox.Text = relationship.Summary;
        RelationshipGlobalIdTextBox.Text = relationship.Id;
        RelationshipPictogramTextBox.Text = relationship.PictogramAsset;
        RelationshipClusterTextBox.Text = relationship.ClusterTechName;
        RelationshipIsComposableCheckBox.IsChecked = relationship.IsComposable;
        RelationshipIsVersionableCheckBox.IsChecked = relationship.IsVersionable;
        RelationshipPreciseConnectCheckBox.IsChecked = relationship.PreciseConnectByDefault;
        RelationshipIsDirectionalCheckBox.IsChecked = relationship.IsDirectional;
        RelationshipIsSimpleCheckBox.IsChecked = relationship.IsSimple;
        RelationshipHideCentralCheckBox.IsChecked = relationship.HideCentralSymbolWhenSimple;
        RelationshipShowNameIfHiddenCheckBox.IsChecked = relationship.ShowNameIfHidingCentralSymbol;
        RelationshipAncestorComboBox.ItemsSource = currentDomain?.RelationshipDefinitions
            .Where(item => !string.Equals(item.Id, relationship.Id, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.TechName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Prepend("<None>")
            .OrderBy(name => name == "<None>" ? string.Empty : name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SetComboBoxSelection(RelationshipAncestorComboBox, string.IsNullOrWhiteSpace(relationship.AncestorRelationshipTechName)
            ? "<None>"
            : relationship.AncestorRelationshipTechName);
        SetShapeComboBoxSelection(RelationshipRepresentativeShapeComboBox, relationship.RepresentativeShape, "Ellipse");
        SetShapeComboBoxSelection(RelationshipSymbolShapeComboBox, relationship.Symbol.Shape, relationship.RepresentativeShape);
        UpdateShapeSelectorVisuals();

        RelationshipHasGroupRegionCheckBox.IsChecked = relationship.HasGroupRegion;
        RelationshipHasGroupLineCheckBox.IsChecked = relationship.HasGroupLine;
        RelationshipAutoCreateRelatedCheckBox.IsChecked = relationship.CanAutomaticallyCreateRelatedConcepts;
        RelationshipCanGroupIntersectingCheckBox.IsChecked = relationship.CanGroupIntersectingObjects;
        RelationshipAutoCreateGroupedCheckBox.IsChecked = relationship.CanAutomaticallyCreateGroupedConcepts;
        RelationshipAutoGroupedConceptComboBox.ItemsSource = currentDomain?.ConceptDefinitions
            .Select(concept => concept.TechName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SetComboBoxSelection(RelationshipAutoGroupedConceptComboBox, relationship.AutomaticGroupedConceptTechName);

        RelationshipOriginNameTextBox.Text = relationship.OriginRole.Name;
        RelationshipOriginTechNameTextBox.Text = relationship.OriginRole.TechName;
        SetComboBoxSelection(RelationshipOriginRoleTypeComboBox, string.IsNullOrWhiteSpace(relationship.OriginRole.RoleType)
            ? "Origin"
            : relationship.OriginRole.RoleType);
        RelationshipOriginPictogramTextBox.Text = relationship.OriginRole.PictogramAsset;
        SetComboBoxSelection(RelationshipOriginAllowedVariantComboBox, ThinkComposerVisualCatalog.GetLinkRoleVariantDisplayName(relationship.OriginRole.AllowedVariants));
        RelationshipOriginSummaryTextBox.Text = relationship.OriginRole.Summary;
        RelationshipOriginMaxConnectionsTextBox.Text = relationship.OriginRole.MaxConnections.ToString(CultureInfo.InvariantCulture);
        RelationshipOriginOrderedCheckBox.IsChecked = relationship.OriginRole.RelatedIdeasAreOrdered;
        RelationshipOriginAllowedTextBox.Text = relationship.OriginRole.AllowedVariants;
        RelationshipOriginAssociableTextBox.Text = relationship.OriginRole.AssociableConcepts;

        RelationshipTargetNameTextBox.Text = relationship.TargetRole.Name;
        RelationshipTargetTechNameTextBox.Text = relationship.TargetRole.TechName;
        SetComboBoxSelection(RelationshipTargetRoleTypeComboBox, string.IsNullOrWhiteSpace(relationship.TargetRole.RoleType)
            ? "Target"
            : relationship.TargetRole.RoleType);
        RelationshipTargetPictogramTextBox.Text = relationship.TargetRole.PictogramAsset;
        SetComboBoxSelection(RelationshipTargetAllowedVariantComboBox, ThinkComposerVisualCatalog.GetLinkRoleVariantDisplayName(relationship.TargetRole.AllowedVariants));
        RelationshipTargetSummaryTextBox.Text = relationship.TargetRole.Summary;
        RelationshipTargetMaxConnectionsTextBox.Text = relationship.TargetRole.MaxConnections.ToString(CultureInfo.InvariantCulture);
        RelationshipTargetOrderedCheckBox.IsChecked = relationship.TargetRole.RelatedIdeasAreOrdered;
        RelationshipTargetAllowedTextBox.Text = relationship.TargetRole.AllowedVariants;
        RelationshipTargetAssociableTextBox.Text = relationship.TargetRole.AssociableConcepts;

        RelationshipFillColorTextBox.Text = relationship.Symbol.FillColorHex;
        RelationshipStrokeColorTextBox.Text = relationship.Symbol.StrokeColorHex;
        RelationshipLineThicknessTextBox.Text = relationship.Symbol.LineThickness.ToString(CultureInfo.InvariantCulture);
        RelationshipInitialWidthTextBox.Text = relationship.Symbol.InitialWidth.ToString(CultureInfo.InvariantCulture);
        RelationshipInitialHeightTextBox.Text = relationship.Symbol.InitialHeight.ToString(CultureInfo.InvariantCulture);
        RelationshipConnectorColorTextBox.Text = relationship.Connector.LineColorHex;
        RelationshipConnectorThicknessTextBox.Text = relationship.Connector.LineThickness.ToString(CultureInfo.InvariantCulture);
        RelationshipConnectorBackgroundTextBox.Text = relationship.Connector.MainBackgroundColorHex;
        SetComboBoxSelection(RelationshipConnectorDashComboBox, relationship.Connector.LineDash);
        SetComboBoxSelection(RelationshipHeadPlugComboBox, ThinkComposerVisualCatalog.GetConnectorPlugDisplayName(relationship.Connector.HeadPlug));
        SetComboBoxSelection(RelationshipTailPlugComboBox, ThinkComposerVisualCatalog.GetConnectorPlugDisplayName(relationship.Connector.TailPlug));
        SetComboBoxSelection(RelationshipHeadVariantComboBox, ThinkComposerVisualCatalog.GetLinkRoleVariantDisplayName(relationship.Connector.HeadVariantTechName));
        SetComboBoxSelection(RelationshipTailVariantComboBox, ThinkComposerVisualCatalog.GetLinkRoleVariantDisplayName(relationship.Connector.TailVariantTechName));
        SetComboBoxSelection(RelationshipConnectorPathStyleComboBox, relationship.Connector.PathStyle);
        SetComboBoxSelection(RelationshipConnectorPathCornerComboBox, relationship.Connector.PathCorner);
        RelationshipLabelDescriptorCheckBox.IsChecked = relationship.Connector.LabelLinkDescriptor;
        RelationshipLabelDefinitorCheckBox.IsChecked = relationship.Connector.LabelLinkDefinitor;
        RelationshipLabelVariantCheckBox.IsChecked = relationship.Connector.LabelLinkVariant;

        RelationshipDetailsList.ItemsSource = null;
        RelationshipDetailsList.ItemsSource = relationshipEditor?.WorkingCopy.Details;
        RelationshipDescriptionTextBox.Text = relationship.Description;
        RelationshipTechSpecTextBox.Text = relationship.TechSpec;
        var template = relationshipEditor?.EnsureOutputTemplate() ?? new EditableOutputTemplate();
        SetComboBoxSelection(RelationshipTemplateLanguageComboBox, template.Language);
        RelationshipTemplateTextBox.Text = template.TemplateText;
        RelationshipTemplateExtendsBaseCheckBox.IsChecked = template.ExtendsBaseTemplate;
        UpdateRelationshipEditorPreview();
    }

    private void RelationshipEditorTabClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tabKey)
            SetRelationshipEditorTab(tabKey);
    }

    private void SetRelationshipEditorTab(string tabKey)
    {
        RelationshipGeneralPanel.Visibility = tabKey == "General" ? Visibility.Visible : Visibility.Collapsed;
        RelationshipArrangePanel.Visibility = tabKey == "Arrange" ? Visibility.Visible : Visibility.Collapsed;
        RelationshipOriginPanel.Visibility = tabKey == "Origin" ? Visibility.Visible : Visibility.Collapsed;
        RelationshipTargetPanel.Visibility = tabKey == "Target" ? Visibility.Visible : Visibility.Collapsed;
        RelationshipSymbolPanel.Visibility = tabKey == "Symbol" ? Visibility.Visible : Visibility.Collapsed;
        RelationshipConnectorPanel.Visibility = tabKey == "Connector" ? Visibility.Visible : Visibility.Collapsed;
        RelationshipDetailsPanel.Visibility = tabKey == "Details" ? Visibility.Visible : Visibility.Collapsed;
        RelationshipDescriptionPanel.Visibility = tabKey == "Description" ? Visibility.Visible : Visibility.Collapsed;
        RelationshipTechSpecPanel.Visibility = tabKey == "TechSpec" ? Visibility.Visible : Visibility.Collapsed;
        RelationshipTemplatesPanel.Visibility = tabKey == "Templates" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RelationshipEditorFieldChanged(object sender, TextChangedEventArgs e)
    {
        UpdateRelationshipEditorPreview();
    }

    private void RelationshipEditorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, RelationshipRepresentativeShapeComboBox)
            && RelationshipRepresentativeShapeComboBox.SelectedItem is string shape)
            SetShapeComboBoxSelection(RelationshipSymbolShapeComboBox, shape, "Ellipse");

        if (ReferenceEquals(sender, RelationshipSymbolShapeComboBox)
            && RelationshipSymbolShapeComboBox.SelectedItem is string symbolShape)
            SetShapeComboBoxSelection(RelationshipRepresentativeShapeComboBox, symbolShape, "Ellipse");

        UpdateShapeSelectorVisuals();
        UpdateRelationshipEditorPreview();
    }

    private void RelationshipDetailAddClicked(object sender, RoutedEventArgs e)
    {
        if (relationshipEditor == null)
            return;

        relationshipEditor.AddDetail();
        RelationshipDetailsList.ItemsSource = null;
        RelationshipDetailsList.ItemsSource = relationshipEditor.WorkingCopy.Details;
    }

    private void RelationshipDetailDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (relationshipEditor == null || RelationshipDetailsList.SelectedItem is not EditableDetailDesignator detail)
            return;

        relationshipEditor.RemoveDetail(detail);
        RelationshipDetailsList.ItemsSource = null;
        RelationshipDetailsList.ItemsSource = relationshipEditor.WorkingCopy.Details;
    }

    private void RelationshipTemplateInsertClicked(object sender, RoutedEventArgs e)
    {
        RelationshipTemplateTextBox.Text += "{{ Name }}";
        UpdateStatus("Inserted a portable relationship template token.");
    }

    private void RelationshipTemplateTestClicked(object sender, RoutedEventArgs e)
    {
        UpdateStatus("Relationship template testing will use ITemplateRenderer after data binding is migrated.");
    }

    private void RelationshipEditorCancelClicked(object sender, RoutedEventArgs e)
    {
        RelationshipEditorOverlay.Visibility = Visibility.Collapsed;
        relationshipEditor = null;
        UpdateStatus("Relationship edit cancelled.");
    }

    private void RelationshipEditorOkClicked(object sender, RoutedEventArgs e)
    {
        if (relationshipEditor == null)
            return;

        UpdateWorkingRelationshipFromEditor();
        if (!relationshipEditor.TryApply())
        {
            RelationshipEditorValidationText.Text = relationshipEditor.ValidationMessage;
            UpdateStatus(relationshipEditor.ValidationMessage);
            return;
        }

        var name = relationshipEditor.WorkingCopy.Name;
        RelationshipEditorOverlay.Visibility = Visibility.Collapsed;
        relationshipEditor = null;
        RefreshRelationshipPalette();
        RefreshConceptEditorDefinitionLists();
        RefreshDomainDirtyState();
        UpdateStatus("Relationship definition applied: " + name);
        LogMessage("Relationship definition changed: " + name);
    }

    private void UpdateWorkingRelationshipFromEditor()
    {
        if (relationshipEditor == null)
            return;

        var relationship = relationshipEditor.WorkingCopy;
        var template = relationshipEditor.EnsureOutputTemplate();
        relationship.Name = RelationshipNameTextBox.Text;
        relationship.TechName = RelationshipTechNameTextBox.Text;
        relationship.Summary = RelationshipSummaryTextBox.Text;
        relationship.PictogramAsset = RelationshipPictogramTextBox.Text;
        relationship.ClusterTechName = RelationshipClusterTextBox.Text;
        relationship.IsComposable = RelationshipIsComposableCheckBox.IsChecked == true;
        relationship.IsVersionable = RelationshipIsVersionableCheckBox.IsChecked == true;
        relationship.PreciseConnectByDefault = RelationshipPreciseConnectCheckBox.IsChecked == true;
        relationship.IsDirectional = RelationshipIsDirectionalCheckBox.IsChecked == true;
        relationship.IsSimple = RelationshipIsSimpleCheckBox.IsChecked == true;
        relationship.HideCentralSymbolWhenSimple = RelationshipHideCentralCheckBox.IsChecked == true;
        relationship.ShowNameIfHidingCentralSymbol = RelationshipShowNameIfHiddenCheckBox.IsChecked == true;
        var ancestor = ComboText(RelationshipAncestorComboBox, string.Empty);
        relationship.AncestorRelationshipTechName = string.Equals(ancestor, "<None>", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : ancestor;
        relationship.RepresentativeShape = ShapeComboText(RelationshipRepresentativeShapeComboBox, "Ellipse");
        relationship.HasGroupRegion = RelationshipHasGroupRegionCheckBox.IsChecked == true;
        relationship.HasGroupLine = RelationshipHasGroupLineCheckBox.IsChecked == true;
        relationship.CanAutomaticallyCreateRelatedConcepts = RelationshipAutoCreateRelatedCheckBox.IsChecked == true;
        relationship.CanGroupIntersectingObjects = RelationshipCanGroupIntersectingCheckBox.IsChecked == true;
        relationship.CanAutomaticallyCreateGroupedConcepts = RelationshipAutoCreateGroupedCheckBox.IsChecked == true;
        relationship.AutomaticGroupedConceptTechName = ComboText(RelationshipAutoGroupedConceptComboBox, string.Empty);

        relationship.OriginRole ??= EditableLinkRoleDefinition.Create("Origin");
        relationship.OriginRole.Name = RelationshipOriginNameTextBox.Text;
        relationship.OriginRole.TechName = RelationshipOriginTechNameTextBox.Text;
        relationship.OriginRole.RoleType = ComboText(RelationshipOriginRoleTypeComboBox, "Origin");
        relationship.OriginRole.PictogramAsset = RelationshipOriginPictogramTextBox.Text;
        relationship.OriginRole.Summary = RelationshipOriginSummaryTextBox.Text;
        relationship.OriginRole.MaxConnections = (int)ParseDouble(RelationshipOriginMaxConnectionsTextBox.Text, 1);
        relationship.OriginRole.RelatedIdeasAreOrdered = RelationshipOriginOrderedCheckBox.IsChecked == true;
        relationship.OriginRole.AllowedVariants = ThinkComposerVisualCatalog.NormalizeLinkRoleVariantTechName(
            ComboText(RelationshipOriginAllowedVariantComboBox, RelationshipOriginAllowedTextBox.Text),
            "Standard");
        relationship.OriginRole.AssociableConcepts = RelationshipOriginAssociableTextBox.Text;

        relationship.TargetRole ??= EditableLinkRoleDefinition.Create("Target");
        relationship.TargetRole.Name = RelationshipTargetNameTextBox.Text;
        relationship.TargetRole.TechName = RelationshipTargetTechNameTextBox.Text;
        relationship.TargetRole.RoleType = ComboText(RelationshipTargetRoleTypeComboBox, "Target");
        relationship.TargetRole.PictogramAsset = RelationshipTargetPictogramTextBox.Text;
        relationship.TargetRole.Summary = RelationshipTargetSummaryTextBox.Text;
        relationship.TargetRole.MaxConnections = (int)ParseDouble(RelationshipTargetMaxConnectionsTextBox.Text, 1);
        relationship.TargetRole.RelatedIdeasAreOrdered = RelationshipTargetOrderedCheckBox.IsChecked == true;
        relationship.TargetRole.AllowedVariants = ThinkComposerVisualCatalog.NormalizeLinkRoleVariantTechName(
            ComboText(RelationshipTargetAllowedVariantComboBox, RelationshipTargetAllowedTextBox.Text),
            "Standard");
        relationship.TargetRole.AssociableConcepts = RelationshipTargetAssociableTextBox.Text;

        relationship.Symbol ??= EditableConceptSymbolFormat.CreateDefault();
        relationship.Symbol.Shape = ShapeComboText(RelationshipSymbolShapeComboBox, relationship.RepresentativeShape);
        relationship.RepresentativeShape = relationship.Symbol.Shape;
        relationship.Symbol.FillColorHex = RelationshipFillColorTextBox.Text;
        relationship.Symbol.StrokeColorHex = RelationshipStrokeColorTextBox.Text;
        relationship.Symbol.LineThickness = ParseDouble(RelationshipLineThicknessTextBox.Text, relationship.Symbol.LineThickness);
        relationship.Symbol.InitialWidth = ParseDouble(RelationshipInitialWidthTextBox.Text, relationship.Symbol.InitialWidth);
        relationship.Symbol.InitialHeight = ParseDouble(RelationshipInitialHeightTextBox.Text, relationship.Symbol.InitialHeight);

        relationship.Connector ??= EditableConnectorFormat.CreateDefault();
        relationship.Connector.LineColorHex = RelationshipConnectorColorTextBox.Text;
        relationship.Connector.MainBackgroundColorHex = RelationshipConnectorBackgroundTextBox.Text;
        relationship.Connector.LineThickness = ParseDouble(RelationshipConnectorThicknessTextBox.Text, relationship.Connector.LineThickness);
        relationship.Connector.LineDash = ComboText(RelationshipConnectorDashComboBox, "Solid");
        relationship.Connector.HeadPlug = ThinkComposerVisualCatalog.NormalizeConnectorPlugTechName(ComboText(RelationshipHeadPlugComboBox, "Simple-Arrow"), "SimpleArrow");
        relationship.Connector.TailPlug = ThinkComposerVisualCatalog.NormalizeConnectorPlugTechName(ComboText(RelationshipTailPlugComboBox, "<None>"), "None");
        relationship.Connector.HeadVariantTechName = ThinkComposerVisualCatalog.NormalizeLinkRoleVariantTechName(ComboText(RelationshipHeadVariantComboBox, "Standard"), "Standard");
        relationship.Connector.TailVariantTechName = ThinkComposerVisualCatalog.NormalizeLinkRoleVariantTechName(ComboText(RelationshipTailVariantComboBox, "Standard"), "Standard");
        relationship.Connector.PathStyle = ComboText(RelationshipConnectorPathStyleComboBox, "Straight");
        relationship.Connector.PathCorner = ComboText(RelationshipConnectorPathCornerComboBox, "Sharp");
        relationship.Connector.LabelLinkDescriptor = RelationshipLabelDescriptorCheckBox.IsChecked == true;
        relationship.Connector.LabelLinkDefinitor = RelationshipLabelDefinitorCheckBox.IsChecked == true;
        relationship.Connector.LabelLinkVariant = RelationshipLabelVariantCheckBox.IsChecked == true;

        relationship.Description = RelationshipDescriptionTextBox.Text;
        relationship.TechSpec = RelationshipTechSpecTextBox.Text;
        template.Language = ComboText(RelationshipTemplateLanguageComboBox, "Text");
        template.TemplateText = RelationshipTemplateTextBox.Text;
        template.ExtendsBaseTemplate = RelationshipTemplateExtendsBaseCheckBox.IsChecked == true;
    }

    private void UpdateRelationshipEditorPreview()
    {
        if (RelationshipPreviewNameText == null)
            return;

        var shape = ShapeComboText(RelationshipSymbolShapeComboBox, ShapeComboText(RelationshipRepresentativeShapeComboBox, "Ellipse"));
        var fill = ColorFromHex(RelationshipFillColorTextBox?.Text ?? string.Empty, Windows.UI.Color.FromArgb(255, 229, 231, 235));
        var stroke = ColorFromHex(RelationshipStrokeColorTextBox?.Text ?? string.Empty, Windows.UI.Color.FromArgb(255, 100, 116, 139));
        var connector = ColorFromHex(RelationshipConnectorColorTextBox?.Text ?? string.Empty, stroke);
        var thickness = ParseDouble(RelationshipLineThicknessTextBox?.Text ?? string.Empty, 2);
        var connectorThickness = ParseDouble(RelationshipConnectorThicknessTextBox?.Text ?? string.Empty, thickness);
        var connectorBrush = new SolidColorBrush(connector);
        var isDirectional = RelationshipIsDirectionalCheckBox?.IsChecked == true;
        var hideCentralSymbol = RelationshipIsSimpleCheckBox?.IsChecked == true
            && RelationshipHideCentralCheckBox?.IsChecked == true;
        var showNameWhenHidden = RelationshipShowNameIfHiddenCheckBox?.IsChecked == true;
        var headPlug = ThinkComposerVisualCatalog.NormalizeConnectorPlugTechName(ComboText(RelationshipHeadPlugComboBox, "Simple-Arrow"), "SimpleArrow");
        var tailPlug = ThinkComposerVisualCatalog.NormalizeConnectorPlugTechName(ComboText(RelationshipTailPlugComboBox, "<None>"), "None");

        RelationshipPreviewNameText.Text = string.IsNullOrWhiteSpace(RelationshipNameTextBox?.Text)
            ? "Relationship"
            : RelationshipNameTextBox.Text.Trim();
        RelationshipPreviewBorder.Visibility = Visibility.Collapsed;
        RelationshipPreviewEllipse.Visibility = Visibility.Collapsed;
        RelationshipPreviewPolygon.Visibility = Visibility.Collapsed;
        RelationshipPreviewGlyph.ShapeName = shape;
        RelationshipPreviewGlyph.SymbolFill = new SolidColorBrush(fill);
        RelationshipPreviewGlyph.SymbolStroke = new SolidColorBrush(stroke);
        RelationshipPreviewGlyph.SymbolThickness = thickness;
        RelationshipPreviewGlyph.Visibility = hideCentralSymbol ? Visibility.Collapsed : Visibility.Visible;
        RelationshipPreviewNameText.Visibility = !hideCentralSymbol || showNameWhenHidden ? Visibility.Visible : Visibility.Collapsed;
        RelationshipPreviewConnector.Stroke = connectorBrush;
        RelationshipPreviewConnector.StrokeThickness = connectorThickness;
        RelationshipPreviewHeadPlug.Stroke = connectorBrush;
        RelationshipPreviewHeadPlug.StrokeThickness = connectorThickness;
        RelationshipPreviewHeadPlug.Visibility = isDirectional && HasVisiblePlug(headPlug) ? Visibility.Visible : Visibility.Collapsed;
        RelationshipPreviewTailPlug.Stroke = connectorBrush;
        RelationshipPreviewTailPlug.StrokeThickness = connectorThickness;
        RelationshipPreviewTailPlug.Visibility = HasVisiblePlug(tailPlug) ? Visibility.Visible : Visibility.Collapsed;
        RelationshipPreviewOriginRoleText.Text = ComboText(RelationshipOriginRoleTypeComboBox, "Origin");
        RelationshipPreviewTargetRoleText.Text = ComboText(RelationshipTargetRoleTypeComboBox, "Target");
        RelationshipPreviewTargetRoleText.Visibility = isDirectional ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool HasVisiblePlug(string plugTechName)
    {
        return !string.Equals(ThinkComposerVisualCatalog.NormalizeConnectorPlugTechName(plugTechName), "None", StringComparison.OrdinalIgnoreCase);
    }

    private void OpenMarkerEditor(EditableMarkerDefinition marker, bool isNew)
    {
        EnsureEditableDomain();
        markerEditor = new MarkerDefinitionEditorViewModel(currentDomain!, marker, isNew);
        MarkerEditorTitleText.Text = (isNew ? "Add" : "Edit") + " Marker Definition - " + markerEditor.WorkingCopy.Name;
        MarkerEditorValidationText.Text = string.Empty;
        PopulateMarkerEditor(markerEditor.WorkingCopy);
        MarkerEditorOverlay.Visibility = Visibility.Visible;
        UpdateStatus((isNew ? "Adding" : "Editing") + " Marker definition.");
    }

    private void PopulateMarkerEditor(EditableMarkerDefinition marker)
    {
        MarkerNameTextBox.Text = marker.Name;
        MarkerTechNameTextBox.Text = marker.TechName;
        MarkerSummaryTextBox.Text = marker.Summary;
        MarkerGlobalIdTextBox.Text = marker.Id;
        MarkerPictogramTextBox.Text = marker.PictogramAsset;
        SetComboBoxSelection(MarkerClusterComboBox, marker.ClusterKey);
        MarkerBackgroundTextBox.Text = marker.BackgroundColorHex;
        MarkerForegroundTextBox.Text = marker.ForegroundColorHex;
        UpdateMarkerEditorPreview();
    }

    private void MarkerEditorFieldChanged(object sender, TextChangedEventArgs e)
    {
        UpdateMarkerEditorPreview();
    }

    private void MarkerEditorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateMarkerEditorPreview();
    }

    private void MarkerEditorCancelClicked(object sender, RoutedEventArgs e)
    {
        MarkerEditorOverlay.Visibility = Visibility.Collapsed;
        markerEditor = null;
        UpdateStatus("Marker edit cancelled.");
    }

    private void MarkerEditorOkClicked(object sender, RoutedEventArgs e)
    {
        if (markerEditor == null)
            return;

        var marker = markerEditor.WorkingCopy;
        marker.Name = MarkerNameTextBox.Text;
        marker.TechName = MarkerTechNameTextBox.Text;
        marker.Summary = MarkerSummaryTextBox.Text;
        marker.PictogramAsset = MarkerPictogramTextBox.Text;
        marker.ClusterKey = ComboText(MarkerClusterComboBox, "UserDef");
        marker.BackgroundColorHex = MarkerBackgroundTextBox.Text;
        marker.ForegroundColorHex = MarkerForegroundTextBox.Text;

        if (!markerEditor.TryApply())
        {
            MarkerEditorValidationText.Text = markerEditor.ValidationMessage;
            UpdateStatus(markerEditor.ValidationMessage);
            return;
        }

        var name = markerEditor.WorkingCopy.Name;
        MarkerEditorOverlay.Visibility = Visibility.Collapsed;
        markerEditor = null;
        RefreshMarkerPalette();
        RefreshDomainDirtyState();
        UpdateStatus("Marker definition applied: " + name);
        LogMessage("Marker definition changed: " + name);
    }

    private void UpdateMarkerEditorPreview()
    {
        if (MarkerPreviewNameText == null)
            return;

        var fill = ColorFromHex(MarkerBackgroundTextBox?.Text ?? string.Empty, Windows.UI.Color.FromArgb(255, 251, 191, 36));
        var foreground = ColorFromHex(MarkerForegroundTextBox?.Text ?? string.Empty, Windows.UI.Color.FromArgb(255, 17, 24, 39));
        MarkerPreviewSwatch.Background = new SolidColorBrush(fill);
        MarkerPreviewSwatch.BorderBrush = new SolidColorBrush(foreground);
        MarkerPreviewNameText.Text = string.IsNullOrWhiteSpace(MarkerNameTextBox?.Text)
            ? "Marker"
            : MarkerNameTextBox.Text.Trim();
        MarkerPreviewNameText.Foreground = new SolidColorBrush(foreground);
    }

    private void OpenComplementEditor(EditableComplementDefinition complement, bool isNew)
    {
        EnsureEditableDomain();
        complementEditor = new ComplementDefinitionEditorViewModel(currentDomain!, complement, isNew);
        ComplementEditorTitleText.Text = (isNew ? "Add" : "Edit") + " Complement Definition - " + complementEditor.WorkingCopy.Name;
        ComplementEditorValidationText.Text = string.Empty;
        PopulateComplementEditor(complementEditor.WorkingCopy);
        SetComplementEditorTab("General");
        ComplementEditorOverlay.Visibility = Visibility.Visible;
        UpdateStatus((isNew ? "Adding" : "Editing") + " Complement definition.");
    }

    private void PopulateComplementEditor(EditableComplementDefinition complement)
    {
        ComplementNameTextBox.Text = complement.Name;
        ComplementTechNameTextBox.Text = complement.TechName;
        ComplementSummaryTextBox.Text = complement.Summary;
        ComplementGlobalIdTextBox.Text = complement.Id;
        ComplementPictogramTextBox.Text = complement.PictogramAsset;
        SetComboBoxSelection(ComplementKindComboBox, complement.Kind);
        ComplementTextTextBox.Text = complement.Text;
        ComplementImageAssetTextBox.Text = complement.ImageAsset;
        ComplementForegroundTextBox.Text = complement.ForegroundColorHex;
        ComplementBackgroundTextBox.Text = complement.BackgroundColorHex;
        SetComboBoxSelection(ComplementDashComboBox, complement.LineDash);
        ComplementThicknessTextBox.Text = complement.LineThickness.ToString(CultureInfo.InvariantCulture);
        SetComboBoxSelection(ComplementOrientationComboBox, complement.Orientation);
        SetComboBoxSelection(ComplementQuadrantComboBox, complement.Quadrant);
        ComplementOffsetXTextBox.Text = complement.OffsetX.ToString(CultureInfo.InvariantCulture);
        ComplementOffsetYTextBox.Text = complement.OffsetY.ToString(CultureInfo.InvariantCulture);
        ComplementWidthTextBox.Text = complement.InitialWidth.ToString(CultureInfo.InvariantCulture);
        ComplementHeightTextBox.Text = complement.InitialHeight.ToString(CultureInfo.InvariantCulture);
        UpdateComplementEditorPreview();
    }

    private void ComplementEditorTabClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tabKey)
            SetComplementEditorTab(tabKey);
    }

    private void SetComplementEditorTab(string tabKey)
    {
        ComplementGeneralPanel.Visibility = tabKey == "General" ? Visibility.Visible : Visibility.Collapsed;
        ComplementVisualPanel.Visibility = tabKey == "Visual" ? Visibility.Visible : Visibility.Collapsed;
        ComplementContentPanel.Visibility = tabKey == "Content" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ComplementEditorFieldChanged(object sender, TextChangedEventArgs e)
    {
        UpdateComplementEditorPreview();
    }

    private void ComplementEditorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateComplementEditorPreview();
    }

    private void ComplementEditorCancelClicked(object sender, RoutedEventArgs e)
    {
        ComplementEditorOverlay.Visibility = Visibility.Collapsed;
        complementEditor = null;
        UpdateStatus("Complement edit cancelled.");
    }

    private void ComplementEditorOkClicked(object sender, RoutedEventArgs e)
    {
        if (complementEditor == null)
            return;

        var complement = complementEditor.WorkingCopy;
        complement.Name = ComplementNameTextBox.Text;
        complement.TechName = ComplementTechNameTextBox.Text;
        complement.Summary = ComplementSummaryTextBox.Text;
        complement.PictogramAsset = ComplementPictogramTextBox.Text;
        complement.Kind = ComboText(ComplementKindComboBox, "Text");
        complement.Text = ComplementTextTextBox.Text;
        complement.ImageAsset = ComplementImageAssetTextBox.Text;
        complement.ForegroundColorHex = ComplementForegroundTextBox.Text;
        complement.BackgroundColorHex = ComplementBackgroundTextBox.Text;
        complement.LineDash = ComboText(ComplementDashComboBox, "Solid");
        complement.LineThickness = ParseDouble(ComplementThicknessTextBox.Text, complement.LineThickness);
        complement.Orientation = ComboText(ComplementOrientationComboBox, "Horizontal");
        complement.Quadrant = ComboText(ComplementQuadrantComboBox, "TopRight");
        complement.OffsetX = ParseDouble(ComplementOffsetXTextBox.Text, 0);
        complement.OffsetY = ParseDouble(ComplementOffsetYTextBox.Text, 0);
        complement.InitialWidth = ParseDouble(ComplementWidthTextBox.Text, complement.InitialWidth);
        complement.InitialHeight = ParseDouble(ComplementHeightTextBox.Text, complement.InitialHeight);

        if (!complementEditor.TryApply())
        {
            ComplementEditorValidationText.Text = complementEditor.ValidationMessage;
            UpdateStatus(complementEditor.ValidationMessage);
            return;
        }

        var name = complementEditor.WorkingCopy.Name;
        ComplementEditorOverlay.Visibility = Visibility.Collapsed;
        complementEditor = null;
        RefreshComplementPalette();
        RefreshDomainDirtyState();
        UpdateStatus("Complement definition applied: " + name);
        LogMessage("Complement definition changed: " + name);
    }

    private void UpdateComplementEditorPreview()
    {
        if (ComplementPreviewNameText == null)
            return;

        var fill = ColorFromHex(ComplementBackgroundTextBox?.Text ?? string.Empty, Windows.UI.Color.FromArgb(255, 248, 250, 252));
        var stroke = ColorFromHex(ComplementForegroundTextBox?.Text ?? string.Empty, Windows.UI.Color.FromArgb(255, 29, 78, 216));
        ComplementPreviewBorder.Background = new SolidColorBrush(fill);
        ComplementPreviewBorder.BorderBrush = new SolidColorBrush(stroke);
        ComplementPreviewBorder.BorderThickness = new Thickness(ParseDouble(ComplementThicknessTextBox?.Text ?? string.Empty, 1.5));
        ComplementPreviewNameText.Text = string.IsNullOrWhiteSpace(ComplementNameTextBox?.Text)
            ? "Complement"
            : ComplementNameTextBox.Text.Trim();
        ComplementPreviewNameText.Foreground = new SolidColorBrush(stroke);
        ComplementPreviewKindText.Text = ComboText(ComplementKindComboBox, "Text");
    }

    private void OpenConceptEditor(EditableConceptDefinition concept, bool isNew)
    {
        EnsureEditableDomain();
        conceptEditor = new ConceptDefinitionEditorViewModel(currentDomain!, concept, isNew);
        ConceptEditorTitleText.Text = (isNew ? "Add" : "Edit") + " Concept Definition - " + conceptEditor.WorkingCopy.Name;
        ConceptEditorValidationText.Text = string.Empty;
        PopulateConceptEditor(conceptEditor.WorkingCopy);
        SetConceptEditorTab("General");
        ConceptEditorOverlay.Visibility = Visibility.Visible;
        UpdateStatus((isNew ? "Adding" : "Editing") + " Concept definition.");
    }

    private void PopulateConceptEditor(EditableConceptDefinition concept)
    {
        var symbol = concept.Symbol ?? EditableConceptSymbolFormat.CreateDefault();
        concept.Symbol = symbol;

        ConceptNameTextBox.Text = concept.Name;
        ConceptTechNameTextBox.Text = concept.TechName;
        ConceptSummaryTextBox.Text = concept.Summary;
        ConceptGlobalIdTextBox.Text = concept.Id;
        ConceptPictogramTextBox.Text = concept.PictogramAsset;
        ConceptIsComposableCheckBox.IsChecked = concept.IsComposable;
        ConceptIsVersionableCheckBox.IsChecked = concept.IsVersionable;
        ConceptPreciseConnectCheckBox.IsChecked = concept.PreciseConnectByDefault;
        ConceptClusterTextBox.Text = concept.ClusterTechName;
        SetShapeComboBoxSelection(ConceptRepresentativeShapeComboBox, concept.RepresentativeShape, "Capsule");
        SetShapeComboBoxSelection(ConceptSymbolShapeComboBox, symbol.Shape, concept.RepresentativeShape);
        UpdateShapeSelectorVisuals();

        ConceptHasGroupRegionCheckBox.IsChecked = concept.HasGroupRegion;
        ConceptHasGroupLineCheckBox.IsChecked = concept.HasGroupLine;
        ConceptAutoCreateRelatedCheckBox.IsChecked = concept.CanAutomaticallyCreateRelatedConcepts;
        ConceptPositioningRadialCheckBox.IsChecked = concept.AutomaticCreationPositioningIsRadialized;
        SetComboBoxSelection(ConceptPositioningModeComboBox, concept.AutomaticCreationPositioningMode);
        ConceptCanGroupIntersectingCheckBox.IsChecked = concept.CanGroupIntersectingObjects;
        ConceptAutoCreateGroupedCheckBox.IsChecked = concept.CanAutomaticallyCreateGroupedConcepts;

        ConceptShowGlobalDetailsFirstCheckBox.IsChecked = symbol.ShowGlobalDetailsFirst;
        ConceptUseNameAsTitleCheckBox.IsChecked = symbol.UseNameAsMainTitle;
        ConceptFlippedHorizontallyCheckBox.IsChecked = symbol.FlippedHorizontally;
        ConceptFlippedVerticallyCheckBox.IsChecked = symbol.FlippedVertically;
        ConceptTiltedCheckBox.IsChecked = symbol.Tilted;
        ConceptAsMultipleCheckBox.IsChecked = symbol.AsMultiple;
        SetComboBoxSelection(ConceptSubtitleDispositionComboBox, symbol.SubtitleVisualDisposition);
        SetComboBoxSelection(ConceptPictogramDispositionComboBox, symbol.PictogramVisualDisposition);
        ConceptInitialWidthTextBox.Text = symbol.InitialWidth.ToString(CultureInfo.InvariantCulture);
        ConceptInitialHeightTextBox.Text = symbol.InitialHeight.ToString(CultureInfo.InvariantCulture);
        ConceptStrokeColorTextBox.Text = symbol.StrokeColorHex;
        ConceptFillColorTextBox.Text = symbol.FillColorHex;
        ConceptLineThicknessTextBox.Text = symbol.LineThickness.ToString(CultureInfo.InvariantCulture);

        ConceptDescriptionTextBox.Text = concept.Description;
        ConceptTechSpecTextBox.Text = concept.TechSpec;
        RefreshConceptEditorDefinitionLists();
        RefreshConceptDetailsList();

        var template = conceptEditor?.EnsureOutputTemplate() ?? new EditableOutputTemplate();
        SetComboBoxSelection(ConceptTemplateLanguageComboBox, template.Language);
        ConceptTemplateTextBox.Text = template.TemplateText;
        ConceptTemplateExtendsBaseCheckBox.IsChecked = template.ExtendsBaseTemplate;

        UpdateConceptEditorPreview();
    }

    private void RefreshConceptEditorDefinitionLists()
    {
        if (currentDomain == null)
            return;

        var conceptNames = currentDomain.ConceptDefinitions
            .Select(concept => concept.TechName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var relationshipNames = currentDomain.RelationshipDefinitions
            .Select(reference => reference.TechName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ConceptAutoCreationConceptComboBox.ItemsSource = conceptNames;
        ConceptAutoGroupedConceptComboBox.ItemsSource = conceptNames;
        ConceptAutoCreationRelationshipComboBox.ItemsSource = relationshipNames;

        if (conceptEditor != null)
        {
            SetComboBoxSelection(ConceptAutoCreationConceptComboBox, conceptEditor.WorkingCopy.AutomaticCreationConceptTechName);
            SetComboBoxSelection(ConceptAutoGroupedConceptComboBox, conceptEditor.WorkingCopy.AutomaticGroupedConceptTechName);
            SetComboBoxSelection(ConceptAutoCreationRelationshipComboBox, conceptEditor.WorkingCopy.AutomaticCreationRelationshipTechName);
        }
    }

    private void ConceptEditorTabClicked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tabKey)
            SetConceptEditorTab(tabKey);
    }

    private void SetConceptEditorTab(string tabKey)
    {
        ConceptGeneralPanel.Visibility = tabKey == "General" ? Visibility.Visible : Visibility.Collapsed;
        ConceptArrangePanel.Visibility = tabKey == "Arrange" ? Visibility.Visible : Visibility.Collapsed;
        ConceptSymbolPanel.Visibility = tabKey == "Symbol" ? Visibility.Visible : Visibility.Collapsed;
        ConceptDetailsPanel.Visibility = tabKey == "Details" ? Visibility.Visible : Visibility.Collapsed;
        ConceptDescriptionPanel.Visibility = tabKey == "Description" ? Visibility.Visible : Visibility.Collapsed;
        ConceptTechSpecPanel.Visibility = tabKey == "TechSpec" ? Visibility.Visible : Visibility.Collapsed;
        ConceptTemplatesPanel.Visibility = tabKey == "Templates" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ConceptEditorFieldChanged(object sender, TextChangedEventArgs e)
    {
        UpdateConceptEditorPreview();
    }

    private void ConceptEditorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, ConceptRepresentativeShapeComboBox)
            && ConceptRepresentativeShapeComboBox.SelectedItem is string shape)
            SetShapeComboBoxSelection(ConceptSymbolShapeComboBox, shape, "Capsule");

        if (ReferenceEquals(sender, ConceptSymbolShapeComboBox)
            && ConceptSymbolShapeComboBox.SelectedItem is string symbolShape)
            SetShapeComboBoxSelection(ConceptRepresentativeShapeComboBox, symbolShape, "Capsule");

        UpdateShapeSelectorVisuals();
        UpdateConceptEditorPreview();
    }

    private void ShapeOptionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not GridView grid || grid.SelectedItem is not ShapeOptionViewModel option)
            return;

        var key = grid.Tag as string ?? string.Empty;
        switch (key)
        {
            case "ConceptRepresentative":
                SetShapeComboBoxSelection(ConceptRepresentativeShapeComboBox, option.ShapeName, "Capsule");
                ConceptRepresentativeShapeButton.Flyout?.Hide();
                break;
            case "ConceptSymbol":
                SetShapeComboBoxSelection(ConceptSymbolShapeComboBox, option.ShapeName, "Capsule");
                ConceptSymbolShapeButton.Flyout?.Hide();
                break;
            case "RelationshipRepresentative":
                SetShapeComboBoxSelection(RelationshipRepresentativeShapeComboBox, option.ShapeName, "Ellipse");
                RelationshipRepresentativeShapeButton.Flyout?.Hide();
                break;
            case "RelationshipSymbol":
                SetShapeComboBoxSelection(RelationshipSymbolShapeComboBox, option.ShapeName, "Ellipse");
                RelationshipSymbolShapeButton.Flyout?.Hide();
                break;
        }

        grid.SelectedItem = null;
        UpdateShapeSelectorVisuals();
    }

    private void UpdateShapeSelectorVisuals()
    {
        UpdateShapeSelectorVisual(
            ConceptRepresentativeShapeComboBox,
            ConceptRepresentativeShapeText,
            ConceptRepresentativeShapeGlyph,
            "Capsule");
        UpdateShapeSelectorVisual(
            ConceptSymbolShapeComboBox,
            ConceptSymbolShapeText,
            ConceptSymbolShapeGlyph,
            "Capsule");
        UpdateShapeSelectorVisual(
            RelationshipRepresentativeShapeComboBox,
            RelationshipRepresentativeShapeText,
            RelationshipRepresentativeShapeGlyph,
            "Ellipse");
        UpdateShapeSelectorVisual(
            RelationshipSymbolShapeComboBox,
            RelationshipSymbolShapeText,
            RelationshipSymbolShapeGlyph,
            "Ellipse");
    }

    private static void UpdateShapeSelectorVisual(ComboBox comboBox, TextBlock label, SymbolGlyph glyph, string fallback)
    {
        if (comboBox == null || label == null || glyph == null)
            return;

        var shape = ShapeComboText(comboBox, fallback);
        label.Text = ThinkComposerVisualCatalog.GetShapeDisplayName(shape);
        glyph.ShapeName = shape;
    }

    private void ConceptStylePresetClicked(object sender, RoutedEventArgs e)
    {
        UpdateStatus("Select a predefined Concept style sample.");
    }

    private void ConceptStylePresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConceptStylePresetGrid.SelectedItem is not ConceptStylePresetViewModel preset)
            return;

        ConceptFillColorTextBox.Text = preset.FillColorHex;
        ConceptStrokeColorTextBox.Text = preset.StrokeColorHex;
        ConceptLineThicknessTextBox.Text = preset.LineThickness.ToString(CultureInfo.InvariantCulture);
        UpdateConceptEditorPreview();
        UpdateStatus("Applied predefined Concept style: " + preset.Name);
        ConceptStylePresetGrid.SelectedItem = null;
    }

    private void ConceptDetailAddClicked(object sender, RoutedEventArgs e)
    {
        if (conceptEditor == null)
            return;

        conceptEditor.AddDetail();
        RefreshConceptDetailsList();
    }

    private void ConceptDetailDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (conceptEditor == null || ConceptDetailsList.SelectedItem is not EditableDetailDesignator detail)
            return;

        conceptEditor.RemoveDetail(detail);
        RefreshConceptDetailsList();
    }

    private void ConceptDetailDesignateClicked(object sender, RoutedEventArgs e)
    {
        UpdateStatus("Detail designation is represented; table/detail schema selection is pending.");
    }

    private void RefreshConceptDetailsList()
    {
        if (conceptEditor == null)
            return;

        ConceptDetailsList.ItemsSource = null;
        ConceptDetailsList.ItemsSource = conceptEditor.WorkingCopy.Details;
    }

    private void ConceptTemplateInsertClicked(object sender, RoutedEventArgs e)
    {
        ConceptTemplateTextBox.Text += "{{ Name }}";
        UpdateStatus("Inserted a portable template token.");
    }

    private void ConceptTemplateTestClicked(object sender, RoutedEventArgs e)
    {
        UpdateStatus("Template testing will use ITemplateRenderer after template data binding is migrated.");
    }

    private void ConceptEditorCancelClicked(object sender, RoutedEventArgs e)
    {
        ConceptEditorOverlay.Visibility = Visibility.Collapsed;
        conceptEditor = null;
        UpdateStatus("Concept edit cancelled.");
    }

    private void ConceptEditorOkClicked(object sender, RoutedEventArgs e)
    {
        if (conceptEditor == null)
            return;

        UpdateWorkingConceptFromEditor();
        if (!conceptEditor.TryApply())
        {
            ConceptEditorValidationText.Text = conceptEditor.ValidationMessage;
            UpdateStatus(conceptEditor.ValidationMessage);
            return;
        }

        var name = conceptEditor.WorkingCopy.Name;
        ConceptEditorOverlay.Visibility = Visibility.Collapsed;
        conceptEditor = null;
        RefreshConceptPalette();
        RefreshConceptEditorDefinitionLists();
        RefreshDomainDirtyState();
        UpdateStatus("Concept definition applied: " + name);
        LogMessage("Concept definition changed: " + name);
    }

    private void UpdateWorkingConceptFromEditor()
    {
        if (conceptEditor == null)
            return;

        var concept = conceptEditor.WorkingCopy;
        var template = conceptEditor.EnsureOutputTemplate();
        concept.Name = ConceptNameTextBox.Text;
        concept.TechName = ConceptTechNameTextBox.Text;
        concept.Summary = ConceptSummaryTextBox.Text;
        concept.PictogramAsset = ConceptPictogramTextBox.Text;
        concept.IsComposable = ConceptIsComposableCheckBox.IsChecked == true;
        concept.IsVersionable = ConceptIsVersionableCheckBox.IsChecked == true;
        concept.PreciseConnectByDefault = ConceptPreciseConnectCheckBox.IsChecked == true;
        concept.ClusterTechName = ConceptClusterTextBox.Text;
        concept.RepresentativeShape = ShapeComboText(ConceptRepresentativeShapeComboBox, "Capsule");

        concept.HasGroupRegion = ConceptHasGroupRegionCheckBox.IsChecked == true;
        concept.HasGroupLine = ConceptHasGroupLineCheckBox.IsChecked == true;
        concept.CanAutomaticallyCreateRelatedConcepts = ConceptAutoCreateRelatedCheckBox.IsChecked == true;
        concept.AutomaticCreationConceptTechName = ComboText(ConceptAutoCreationConceptComboBox, string.Empty);
        concept.AutomaticCreationRelationshipTechName = ComboText(ConceptAutoCreationRelationshipComboBox, string.Empty);
        concept.AutomaticCreationPositioningIsRadialized = ConceptPositioningRadialCheckBox.IsChecked == true;
        concept.AutomaticCreationPositioningMode = ComboText(ConceptPositioningModeComboBox, "Vertical Alternated");
        concept.CanGroupIntersectingObjects = ConceptCanGroupIntersectingCheckBox.IsChecked == true;
        concept.CanAutomaticallyCreateGroupedConcepts = ConceptAutoCreateGroupedCheckBox.IsChecked == true;
        concept.AutomaticGroupedConceptTechName = ComboText(ConceptAutoGroupedConceptComboBox, string.Empty);

        concept.Symbol ??= EditableConceptSymbolFormat.CreateDefault();
        concept.Symbol.Shape = ShapeComboText(ConceptSymbolShapeComboBox, concept.RepresentativeShape);
        concept.RepresentativeShape = concept.Symbol.Shape;
        concept.Symbol.ShowGlobalDetailsFirst = ConceptShowGlobalDetailsFirstCheckBox.IsChecked == true;
        concept.Symbol.UseNameAsMainTitle = ConceptUseNameAsTitleCheckBox.IsChecked == true;
        concept.Symbol.FlippedHorizontally = ConceptFlippedHorizontallyCheckBox.IsChecked == true;
        concept.Symbol.FlippedVertically = ConceptFlippedVerticallyCheckBox.IsChecked == true;
        concept.Symbol.Tilted = ConceptTiltedCheckBox.IsChecked == true;
        concept.Symbol.AsMultiple = ConceptAsMultipleCheckBox.IsChecked == true;
        concept.Symbol.SubtitleVisualDisposition = ComboText(ConceptSubtitleDispositionComboBox, "Hidden");
        concept.Symbol.PictogramVisualDisposition = ComboText(ConceptPictogramDispositionComboBox, "Right");
        concept.Symbol.InitialWidth = ParseDouble(ConceptInitialWidthTextBox.Text, concept.Symbol.InitialWidth);
        concept.Symbol.InitialHeight = ParseDouble(ConceptInitialHeightTextBox.Text, concept.Symbol.InitialHeight);
        concept.Symbol.StrokeColorHex = ConceptStrokeColorTextBox.Text;
        concept.Symbol.FillColorHex = ConceptFillColorTextBox.Text;
        concept.Symbol.LineThickness = ParseDouble(ConceptLineThicknessTextBox.Text, concept.Symbol.LineThickness);

        concept.Description = ConceptDescriptionTextBox.Text;
        concept.TechSpec = ConceptTechSpecTextBox.Text;
        template.Language = ComboText(ConceptTemplateLanguageComboBox, "Text");
        template.TemplateText = ConceptTemplateTextBox.Text;
        template.ExtendsBaseTemplate = ConceptTemplateExtendsBaseCheckBox.IsChecked == true;
    }

    private void UpdateConceptEditorPreview()
    {
        if (ConceptPreviewNameText == null)
            return;

        var shape = ShapeComboText(ConceptSymbolShapeComboBox, ShapeComboText(ConceptRepresentativeShapeComboBox, "Capsule"));
        var fill = ColorFromHex(ConceptFillColorTextBox?.Text ?? string.Empty, Windows.UI.Color.FromArgb(255, 255, 229, 64));
        var stroke = ColorFromHex(ConceptStrokeColorTextBox?.Text ?? string.Empty, Windows.UI.Color.FromArgb(255, 212, 169, 0));
        var thickness = ParseDouble(ConceptLineThicknessTextBox?.Text ?? string.Empty, 2);

        ConceptPreviewNameText.Text = string.IsNullOrWhiteSpace(ConceptNameTextBox?.Text)
            ? "Concept"
            : ConceptNameTextBox.Text.Trim();
        ConceptPreviewBorder.Visibility = Visibility.Collapsed;
        ConceptPreviewEllipse.Visibility = Visibility.Collapsed;
        ConceptPreviewPolygon.Visibility = Visibility.Collapsed;
        ConceptPreviewGlyph.ShapeName = shape;
        ConceptPreviewGlyph.SymbolFill = new SolidColorBrush(fill);
        ConceptPreviewGlyph.SymbolStroke = new SolidColorBrush(stroke);
        ConceptPreviewGlyph.SymbolThickness = thickness;
        ConceptPreviewPictogramMarker.Visibility = string.IsNullOrWhiteSpace(ConceptPictogramTextBox?.Text)
            ? Visibility.Visible
            : Visibility.Visible;
    }

    private static void SetComboBoxSelection(ComboBox comboBox, string value)
    {
        if (comboBox == null)
            return;

        if (!string.IsNullOrWhiteSpace(value))
        {
            foreach (var item in comboBox.Items)
            {
                if (item is string text && text.Equals(value, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
        }

        if (comboBox.Items.Count > 0)
            comboBox.SelectedIndex = 0;
    }

    private static void SetShapeComboBoxSelection(ComboBox comboBox, string value, string fallback)
    {
        if (comboBox == null)
            return;

        var normalized = ThinkComposerVisualCatalog.NormalizeShapeTechName(value, fallback);
        foreach (var item in comboBox.Items)
        {
            if (item is string text
                && ThinkComposerVisualCatalog.NormalizeShapeTechName(text, fallback).Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        var displayName = ThinkComposerVisualCatalog.GetShapeDisplayName(normalized);
        foreach (var item in comboBox.Items)
        {
            if (item is string text && text.Equals(displayName, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        if (comboBox.Items.Count > 0)
            comboBox.SelectedIndex = 0;
    }

    private static string ComboText(ComboBox comboBox, string fallback)
    {
        return comboBox?.SelectedItem as string
            ?? (comboBox?.SelectedValue as string)
            ?? fallback;
    }

    private static string ShapeComboText(ComboBox comboBox, string fallback)
    {
        return ThinkComposerVisualCatalog.NormalizeShapeTechName(ComboText(comboBox, fallback), fallback);
    }

    private static double ParseDouble(string value, double fallback)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : fallback;
    }

    private void RefreshContentTree()
    {
        if (ContentTree == null)
            return;

        ContentTree.RootNodes.Clear();

        foreach (var item in GetVisibleContentTreeItems())
            ContentTree.RootNodes.Add(CreateTreeNode(item));
    }

    private IReadOnlyList<ContentTreeItemViewModel> GetVisibleContentTreeItems()
    {
        var query = ContentFindTextBox?.Text?.Trim() ?? string.Empty;
        var filteredItems = string.IsNullOrWhiteSpace(query)
            ? contentTreeItems
            : contentTreeItems
                .Select(item => FilterContentTreeItem(item, query))
                .Where(item => item != null)
                .Cast<ContentTreeItemViewModel>()
                .ToArray();

        return ContentSortToggle?.IsChecked == true
            ? SortContentTreeItems(filteredItems)
            : filteredItems;
    }

    private static ContentTreeItemViewModel? FilterContentTreeItem(ContentTreeItemViewModel item, string query)
    {
        var filteredChildren = item.Children
            .Select(child => FilterContentTreeItem(child, query))
            .Where(child => child != null)
            .Cast<ContentTreeItemViewModel>()
            .ToArray();

        if (ContentTreeItemMatches(item, query) || filteredChildren.Length > 0)
            return item with { Children = filteredChildren };

        return null;
    }

    private static bool ContentTreeItemMatches(ContentTreeItemViewModel item, string query)
    {
        return item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || item.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ContentTreeItemViewModel> SortContentTreeItems(IEnumerable<ContentTreeItemViewModel> items)
    {
        return items
            .OrderBy(item => item.SortRank)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item with { Children = SortContentTreeItems(item.Children) })
            .ToArray();
    }

    private static TreeViewNode CreateTreeNode(ContentTreeItemViewModel item)
    {
        var node = new TreeViewNode
        {
            Content = item,
            IsExpanded = true
        };

        foreach (var child in item.Children)
            node.Children.Add(CreateTreeNode(child));

        return node;
    }

    private static IReadOnlyList<ContentTreeItemViewModel> BuildContentTreeItems(
        ThinkComposerFileSummary summary,
        BitmapImage? packagePictogram)
    {
        var model = summary.LegacyModel;
        var isDomainPackage = Path.GetExtension(summary.FileName).Equals(".tdom", StringComparison.OrdinalIgnoreCase);

        if (model == null)
            return new[]
            {
                ContentNode(Path.GetFileNameWithoutExtension(summary.FileName), ContentNodeKind.Document, summary.Kind, packagePictogram)
            };

        if (isDomainPackage)
        {
            return new[]
            {
                ContentNode(Path.GetFileNameWithoutExtension(summary.FileName), ContentNodeKind.Domain, summary.Kind, packagePictogram)
            };
        }

        var items = new List<ContentTreeItemViewModel>();
        var viewNames = model.CompositionViewNames.Count > 0
            ? model.CompositionViewNames
            : new[] { "Main View" };

        items.AddRange(viewNames
            .Select(name => ContentNode(name, ContentNodeKind.View, "Composite view"))
            .Take(24));

        items.AddRange(model.CompositionConceptNames
            .Where(name => !viewNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Select(name => ContentNode(name, ContentNodeKind.Concept))
            .Take(120));

        items.AddRange(model.CompositionRelationshipNames
            .Select(name => ContentNode(name, ContentNodeKind.Relationship, "Origin/target bridge pending"))
            .Take(120));

        if (items.Count == 0)
        {
            items.Add(ContentNode(
                Path.GetFileNameWithoutExtension(summary.FileName),
                ContentNodeKind.Document,
                "Composition content requires DTO materializer"));
        }

        return items
            .DistinctBy(item => item.Name + "|" + item.Kind, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ContentTreeItemViewModel ContentNode(
        string name,
        ContentNodeKind kind,
        string description = "",
        BitmapImage? pictographSource = null,
        IReadOnlyList<ContentTreeItemViewModel>? children = null)
    {
        var accent = GetContentNodeAccent(kind);

        return new ContentTreeItemViewModel
        {
            Name = name,
            Description = description,
            Kind = kind,
            SortRank = GetContentNodeSortRank(kind),
            SymbolFillBrush = new SolidColorBrush(GetContentNodeFill(kind)),
            SymbolStrokeBrush = new SolidColorBrush(accent),
            PictographSource = pictographSource,
            PictographVisibility = pictographSource == null ? Visibility.Collapsed : Visibility.Visible,
            ShapeSymbolVisibility = pictographSource == null && UsesShapeSymbol(kind) ? Visibility.Visible : Visibility.Collapsed,
            RelationshipSymbolVisibility = pictographSource == null && kind == ContentNodeKind.Relationship ? Visibility.Visible : Visibility.Collapsed,
            ViewSymbolVisibility = pictographSource == null && UsesViewSymbol(kind) ? Visibility.Visible : Visibility.Collapsed,
            Children = children ?? Array.Empty<ContentTreeItemViewModel>()
        };
    }

    private static bool UsesShapeSymbol(ContentNodeKind kind)
    {
        return kind is ContentNodeKind.Concept or ContentNodeKind.Domain or ContentNodeKind.Document;
    }

    private static bool UsesViewSymbol(ContentNodeKind kind)
    {
        return kind == ContentNodeKind.View;
    }

    private static Windows.UI.Color GetContentNodeAccent(ContentNodeKind kind)
    {
        return kind switch
        {
            ContentNodeKind.View => Windows.UI.Color.FromArgb(255, 14, 165, 233),
            ContentNodeKind.Concept => Windows.UI.Color.FromArgb(255, 245, 158, 11),
            ContentNodeKind.Relationship => Windows.UI.Color.FromArgb(255, 100, 116, 139),
            ContentNodeKind.Domain => Windows.UI.Color.FromArgb(255, 34, 197, 94),
            _ => Windows.UI.Color.FromArgb(255, 14, 165, 233)
        };
    }

    private static Windows.UI.Color GetContentNodeFill(ContentNodeKind kind)
    {
        return kind switch
        {
            ContentNodeKind.Concept => Windows.UI.Color.FromArgb(255, 255, 243, 128),
            ContentNodeKind.Domain => Windows.UI.Color.FromArgb(255, 187, 247, 208),
            ContentNodeKind.Document => Windows.UI.Color.FromArgb(255, 186, 230, 253),
            _ => Windows.UI.Color.FromArgb(255, 226, 232, 240)
        };
    }

    private static int GetContentNodeSortRank(ContentNodeKind kind)
    {
        return kind switch
        {
            ContentNodeKind.View => 0,
            ContentNodeKind.Concept => 1,
            ContentNodeKind.Relationship => 2,
            ContentNodeKind.Domain => 3,
            _ => 4
        };
    }

    private static IReadOnlyList<string> BuildInterrelationItems(ThinkComposerFileSummary summary)
    {
        var items = new List<string>
        {
            "Pointed by...",
            "Pointing to...",
            "Graph links require DTO materializer"
        };

        if (summary.LegacyModel != null)
        {
            var relationshipCount = CountTypes(summary.LegacyModel, "Relationship", "Link");
            var connectorCount = CountTypes(summary.LegacyModel, "Connector");

            if (relationshipCount > 0)
                items.Add("Relationship-like records detected: " + relationshipCount);
            if (connectorCount > 0)
                items.Add("Connector-like records detected: " + connectorCount);
        }

        return items;
    }

    private static IReadOnlyList<string> BuildModelDiagnosticItems(LegacyBinaryModelSummary? model)
    {
        if (model == null)
            return Array.Empty<string>();

        var items = new List<string>
        {
            "Legacy model projection",
            "  Entry: " + model.EntryName,
            "  Root type: " + (string.IsNullOrWhiteSpace(model.RootTypeName) ? "(unknown)" : model.RootTypeName),
            "  Record stream parsed: " + model.ParsedRecordStream,
            "  Candidate names: " + model.CandidateNames.Count
        };

        items.Add("  Composition content names: " + model.CompositionContentNames.Count);
        items.Add("  Domain concepts: " + model.DomainConceptNames.Count);
        items.Add("  Domain relationships: " + model.DomainRelationshipNames.Count);
        items.Add("  Domain markers: " + model.DomainMarkerNames.Count);
        items.Add("  Domain complements: " + model.DomainComplementNames.Count);
        items.AddRange(model.TypeCounts.Take(30).Select(pair => "  Type " + pair.Key + ": " + pair.Value));
        items.AddRange(model.Diagnostics.Take(10).Select(diagnostic => "  Diagnostic " + diagnostic));

        return items;
    }

    private static async Task<BitmapImage> CreateBitmapAsync(byte[] imageBytes)
    {
        var bitmap = new BitmapImage();
        using var stream = new InMemoryRandomAccessStream();
        var writer = new DataWriter(stream);
        writer.WriteBytes(imageBytes);
        await writer.StoreAsync();
        await writer.FlushAsync();

        stream.Seek(0);
        await bitmap.SetSourceAsync(stream);
        writer.Dispose();
        return bitmap;
    }

    private static async Task<BitmapImage?> CreateOptionalBitmapAsync(byte[]? imageBytes)
    {
        return imageBytes == null || imageBytes.Length == 0
            ? null
            : await CreateBitmapAsync(imageBytes);
    }

    private async Task ShowSnapshotAsync(ThinkComposerFileSummary summary)
    {
        if (summary.SnapshotImageBytes == null || summary.SnapshotImageBytes.Length == 0)
        {
            SnapshotImage.Source = null;
            SnapshotFrame.Visibility = Visibility.Collapsed;
            return;
        }

        SnapshotTitle.Text = "Snapshot preview: " + summary.SnapshotEntryName;
        SnapshotImage.Source = await CreateBitmapAsync(summary.SnapshotImageBytes);
        SnapshotFrame.Visibility = Visibility.Visible;
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }

    private static string? FindPredefinedContentFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "PredefinedContent");
            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }

    private static string? FindSampleDomain()
    {
        var folder = FindPredefinedContentFolder();
        if (folder == null)
            return null;

        var candidate = Path.Combine(folder, "All-Purpose.tdom");
        return File.Exists(candidate) ? candidate : null;
    }

    private static PaletteItemViewModel Palette(string name, byte red, byte green, byte blue)
    {
        return Palette(name, TcColor.FromRgb(red, green, blue).ToHexArgb());
    }

    private static PaletteItemViewModel Palette(string name, string fillColorHex)
    {
        return Palette(name, fillColorHex, "#FF64748B");
    }

    private static PaletteItemViewModel Palette(string name, string fillColorHex, string strokeColorHex)
    {
        return Palette(name, "Rectangle", fillColorHex, strokeColorHex);
    }

    private static PaletteItemViewModel Palette(string name, string shape, string fillColorHex, string strokeColorHex)
    {
        var fill = ColorFromHex(fillColorHex, Windows.UI.Color.FromArgb(255, 100, 116, 139));
        var stroke = ColorFromHex(strokeColorHex, Windows.UI.Color.FromArgb(255, 100, 116, 139));
        var techShape = ThinkComposerVisualCatalog.NormalizeShapeTechName(shape, "Rectangle");
        var ellipse = ThinkComposerVisualCatalog.IsEllipseShape(techShape);
        var points = CreateSymbolPoints(techShape, 32, 15);
        var hasPolygon = points.Count > 0;

        return new PaletteItemViewModel
        {
            Name = name,
            Subtitle = EditableDomainNaming.ToTechName(name),
            FillColorHex = TcColor.FromArgb(fill.A, fill.R, fill.G, fill.B).ToHexArgb(),
            StrokeColorHex = TcColor.FromArgb(stroke.A, stroke.R, stroke.G, stroke.B).ToHexArgb(),
            FillBrush = new SolidColorBrush(fill),
            StrokeBrush = new SolidColorBrush(stroke),
            AccentBrush = new SolidColorBrush(fill),
            ShapeName = techShape,
            SymbolCornerRadius = GetSymbolCornerRadius(techShape),
            SymbolPoints = points,
            BorderSymbolVisibility = !ellipse && !hasPolygon ? Visibility.Visible : Visibility.Collapsed,
            EllipseSymbolVisibility = ellipse ? Visibility.Visible : Visibility.Collapsed,
            PolygonSymbolVisibility = hasPolygon ? Visibility.Visible : Visibility.Collapsed,
            GlyphSymbolVisibility = Visibility.Visible,
            PictographVisibility = Visibility.Collapsed
        };
    }

    private static PaletteItemViewModel CreateHashedPaletteItem(string name)
    {
        var hash = Math.Abs(name.GetHashCode());
        return Palette(
            name,
            (byte)(80 + hash % 130),
            (byte)(110 + (hash / 17) % 120),
            (byte)(130 + (hash / 97) % 100));
    }

    private static PaletteItemViewModel CreateConceptPaletteItem(EditableConceptDefinition concept)
    {
        var symbol = concept.Symbol ?? EditableConceptSymbolFormat.CreateDefault();
        var shape = string.IsNullOrWhiteSpace(concept.RepresentativeShape)
            ? symbol.Shape
            : concept.RepresentativeShape;
        shape = ThinkComposerVisualCatalog.NormalizeShapeTechName(shape, "Capsule");
        var fill = ColorFromHex(symbol.FillColorHex, Windows.UI.Color.FromArgb(255, 255, 229, 64));
        var stroke = ColorFromHex(symbol.StrokeColorHex, Windows.UI.Color.FromArgb(255, 212, 169, 0));
        var pictogram = TryCreatePictogramSource(concept.PictogramAsset);
        var showPictogram = pictogram != null;
        var ellipse = IsEllipseShape(shape);
        var points = CreateSymbolPoints(shape, 32, 15);
        var hasPolygon = points.Count > 0;

        return new PaletteItemViewModel
        {
            Name = concept.Name,
            Subtitle = string.IsNullOrWhiteSpace(concept.Summary) ? concept.TechName : concept.Summary,
            ConceptDefinition = concept,
            FillColorHex = TcColor.FromArgb(fill.A, fill.R, fill.G, fill.B).ToHexArgb(),
            StrokeColorHex = TcColor.FromArgb(stroke.A, stroke.R, stroke.G, stroke.B).ToHexArgb(),
            FillBrush = new SolidColorBrush(fill),
            StrokeBrush = new SolidColorBrush(stroke),
            AccentBrush = new SolidColorBrush(fill),
            ShapeName = shape,
            SymbolCornerRadius = GetSymbolCornerRadius(shape),
            SymbolPoints = points,
            PictogramSource = pictogram,
            PictographVisibility = showPictogram ? Visibility.Visible : Visibility.Collapsed,
            BorderSymbolVisibility = !showPictogram && !ellipse && !hasPolygon ? Visibility.Visible : Visibility.Collapsed,
            EllipseSymbolVisibility = !showPictogram && ellipse ? Visibility.Visible : Visibility.Collapsed,
            PolygonSymbolVisibility = !showPictogram && hasPolygon ? Visibility.Visible : Visibility.Collapsed,
            GlyphSymbolVisibility = showPictogram ? Visibility.Collapsed : Visibility.Visible
        };
    }

    private static PaletteItemViewModel CreateRelationshipPaletteItem(EditableRelationshipDefinition relationship)
    {
        var symbol = relationship.Symbol ?? EditableConceptSymbolFormat.CreateDefault();
        var connector = relationship.Connector ?? EditableConnectorFormat.CreateDefault();
        var fill = ColorFromHex(symbol.FillColorHex, Windows.UI.Color.FromArgb(255, 229, 231, 235));
        var stroke = ColorFromHex(connector.LineColorHex ?? symbol.StrokeColorHex, Windows.UI.Color.FromArgb(255, 100, 116, 139));
        var pictogram = TryCreatePictogramSource(relationship.PictogramAsset);
        var showPictogram = pictogram != null;
        var shape = ThinkComposerVisualCatalog.NormalizeShapeTechName(relationship.RepresentativeShape, "Ellipse");
        var ellipse = IsEllipseShape(shape);
        var points = CreateSymbolPoints(shape, 32, 15);
        var hasPolygon = points.Count > 0;

        return new PaletteItemViewModel
        {
            Name = relationship.Name,
            Subtitle = string.IsNullOrWhiteSpace(relationship.Summary) ? relationship.TechName : relationship.Summary,
            RelationshipDefinition = relationship,
            FillColorHex = TcColor.FromArgb(fill.A, fill.R, fill.G, fill.B).ToHexArgb(),
            StrokeColorHex = TcColor.FromArgb(stroke.A, stroke.R, stroke.G, stroke.B).ToHexArgb(),
            FillBrush = new SolidColorBrush(fill),
            StrokeBrush = new SolidColorBrush(stroke),
            AccentBrush = new SolidColorBrush(stroke),
            ShapeName = shape,
            SymbolCornerRadius = GetSymbolCornerRadius(shape),
            SymbolPoints = points,
            PictogramSource = pictogram,
            PictographVisibility = showPictogram ? Visibility.Visible : Visibility.Collapsed,
            BorderSymbolVisibility = !showPictogram && !ellipse && !hasPolygon ? Visibility.Visible : Visibility.Collapsed,
            EllipseSymbolVisibility = !showPictogram && ellipse ? Visibility.Visible : Visibility.Collapsed,
            PolygonSymbolVisibility = !showPictogram && hasPolygon ? Visibility.Visible : Visibility.Collapsed,
            GlyphSymbolVisibility = showPictogram ? Visibility.Collapsed : Visibility.Visible
        };
    }

    private static PaletteItemViewModel CreateMarkerPaletteItem(EditableMarkerDefinition marker)
    {
        var fill = ColorFromHex(marker.BackgroundColorHex, Windows.UI.Color.FromArgb(255, 251, 191, 36));
        var stroke = ColorFromHex(marker.ForegroundColorHex, Windows.UI.Color.FromArgb(255, 17, 24, 39));
        var pictogram = TryCreatePictogramSource(marker.PictogramAsset);
        var showPictogram = pictogram != null;

        return new PaletteItemViewModel
        {
            Name = marker.Name,
            Subtitle = string.IsNullOrWhiteSpace(marker.Summary) ? marker.TechName : marker.Summary,
            MarkerDefinition = marker,
            FillColorHex = TcColor.FromArgb(fill.A, fill.R, fill.G, fill.B).ToHexArgb(),
            StrokeColorHex = TcColor.FromArgb(stroke.A, stroke.R, stroke.G, stroke.B).ToHexArgb(),
            FillBrush = new SolidColorBrush(fill),
            StrokeBrush = new SolidColorBrush(stroke),
            AccentBrush = new SolidColorBrush(fill),
            ShapeName = "Capsule",
            SymbolCornerRadius = new CornerRadius(8),
            PictogramSource = pictogram,
            PictographVisibility = showPictogram ? Visibility.Visible : Visibility.Collapsed,
            BorderSymbolVisibility = showPictogram ? Visibility.Collapsed : Visibility.Visible,
            EllipseSymbolVisibility = Visibility.Collapsed,
            GlyphSymbolVisibility = showPictogram ? Visibility.Collapsed : Visibility.Visible
        };
    }

    private static PaletteItemViewModel CreateComplementPaletteItem(EditableComplementDefinition complement)
    {
        var fill = ColorFromHex(complement.BackgroundColorHex, Windows.UI.Color.FromArgb(255, 248, 250, 252));
        var stroke = ColorFromHex(complement.ForegroundColorHex, Windows.UI.Color.FromArgb(255, 29, 78, 216));
        var pictogram = TryCreatePictogramSource(complement.PictogramAsset);
        var showPictogram = pictogram != null;

        return new PaletteItemViewModel
        {
            Name = complement.Name,
            Subtitle = string.IsNullOrWhiteSpace(complement.Summary) ? complement.Kind : complement.Summary,
            ComplementDefinition = complement,
            FillColorHex = TcColor.FromArgb(fill.A, fill.R, fill.G, fill.B).ToHexArgb(),
            StrokeColorHex = TcColor.FromArgb(stroke.A, stroke.R, stroke.G, stroke.B).ToHexArgb(),
            FillBrush = new SolidColorBrush(fill),
            StrokeBrush = new SolidColorBrush(stroke),
            AccentBrush = new SolidColorBrush(stroke),
            ShapeName = "Rectangle",
            SymbolCornerRadius = new CornerRadius(2),
            PictogramSource = pictogram,
            PictographVisibility = showPictogram ? Visibility.Visible : Visibility.Collapsed,
            BorderSymbolVisibility = showPictogram ? Visibility.Collapsed : Visibility.Visible,
            EllipseSymbolVisibility = Visibility.Collapsed,
            GlyphSymbolVisibility = showPictogram ? Visibility.Collapsed : Visibility.Visible
        };
    }

    private static BitmapImage? TryCreatePictogramSource(string pictogramAsset)
    {
        if (string.IsNullOrWhiteSpace(pictogramAsset))
            return null;

        var route = Environment.ExpandEnvironmentVariables(pictogramAsset.Trim().Trim('"'));
        if (!Path.IsPathRooted(route) || !File.Exists(route))
            return null;

        try
        {
            return new BitmapImage(new Uri(route));
        }
        catch
        {
            return null;
        }
    }

    private static Windows.UI.Color ColorFromHex(string value, Windows.UI.Color fallback)
    {
        return TcColor.TryParseHex(value, out var color)
            ? Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B)
            : fallback;
    }

    private static CornerRadius GetSymbolCornerRadius(string shape)
    {
        if (ThinkComposerVisualCatalog.IsCapsuleShape(shape))
            return new CornerRadius(7);

        if (ThinkComposerVisualCatalog.IsRoundedRectangleShape(shape))
            return new CornerRadius(5);

        return new CornerRadius(2);
    }

    private static bool IsEllipseShape(string shape)
    {
        return ThinkComposerVisualCatalog.IsEllipseShape(shape);
    }

    private static PointCollection CreateSymbolPoints(string shape, double width, double height)
    {
        var techName = ThinkComposerVisualCatalog.NormalizeShapeTechName(shape, "Rectangle");
        var points = new PointCollection();

        switch (techName)
        {
            case "HexagonVertical":
            case "HexagonHorizontal":
            case "Specification":
                Add(points, width * 0.18, 0, width * 0.82, 0, width, height * 0.5, width * 0.82, height, width * 0.18, height, 0, height * 0.5);
                break;
            case "Parallelogram":
                Add(points, width * 0.14, 0, width, 0, width * 0.86, height, 0, height);
                break;
            case "Trapezium":
            case "Funnel":
                Add(points, width * 0.14, 0, width * 0.86, 0, width, height, 0, height);
                break;
            case "Triangle":
                Add(points, width * 0.5, 0, width, height, 0, height);
                break;
            case "Rhomb":
            case "RhombCrossed":
                Add(points, width * 0.5, 0, width, height * 0.5, width * 0.5, height, 0, height * 0.5);
                break;
            case "BowTie":
            case "OppositeTriangles":
                Add(points, 0, 0, width * 0.5, height * 0.5, 0, height, width, height, width * 0.5, height * 0.5, width, 0);
                break;
            case "ChevronHorizontal":
                Add(points, 0, 0, width * 0.72, 0, width, height * 0.5, width * 0.72, height, 0, height, width * 0.25, height * 0.5);
                break;
            case "ChevronVertical":
                Add(points, width * 0.5, 0, width, height * 0.28, width, height, width * 0.5, height * 0.75, 0, height, 0, height * 0.28);
                break;
            case "Document":
            case "File":
                Add(points, 0, 0, width * 0.78, 0, width, height * 0.25, width, height, 0, height);
                break;
            case "Folder":
                Add(points, 0, height * 0.22, width * 0.32, height * 0.22, width * 0.4, 0, width * 0.72, 0, width * 0.78, height * 0.22, width, height * 0.22, width, height, 0, height);
                break;
            case "Flag":
                Add(points, 0, 0, width, 0, width * 0.8, height * 0.5, width, height, 0, height);
                break;
            case "Arrow":
            case "ArrowRegular":
                Add(points, 0, height * 0.25, width * 0.68, height * 0.25, width * 0.68, 0, width, height * 0.5, width * 0.68, height, width * 0.68, height * 0.75, 0, height * 0.75);
                break;
            case "ArrowDouble":
            case "ArrowRegularDouble":
                Add(points, 0, height * 0.5, width * 0.25, 0, width * 0.25, height * 0.25, width * 0.75, height * 0.25, width * 0.75, 0, width, height * 0.5, width * 0.75, height, width * 0.75, height * 0.75, width * 0.25, height * 0.75, width * 0.25, height);
                break;
            case "XMark":
                Add(points, width * 0.12, 0, width * 0.5, height * 0.35, width * 0.88, 0, width, height * 0.12, width * 0.65, height * 0.5, width, height * 0.88, width * 0.88, height, width * 0.5, height * 0.65, width * 0.12, height, 0, height * 0.88, width * 0.35, height * 0.5, 0, height * 0.12);
                break;
        }

        return points;
    }

    private static void Add(PointCollection points, params double[] coordinates)
    {
        for (var index = 0; index + 1 < coordinates.Length; index += 2)
            points.Add(new Windows.Foundation.Point(coordinates[index], coordinates[index + 1]));
    }

    private static string GetProjectedShape(string name, int index)
    {
        return ThinkComposerVisualCatalog.GetDefaultConceptStyle(name, index).Shape;
    }

    private static IReadOnlyList<PaletteItemViewModel> BuildDomainPaletteItems(
        IReadOnlyList<string> names,
        IReadOnlyList<PaletteItemViewModel> fallbackItems)
    {
        var items = names
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(48)
            .Select(CreateHashedPaletteItem)
            .ToArray();

        return items.Length == 0
            ? fallbackItems
            : items;
    }

    private static int CountTypes(LegacyBinaryModelSummary model, params string[] tokens)
    {
        return model.TypeCounts
            .Where(pair => tokens.Any(token => pair.Key.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .Sum(pair => pair.Value);
    }

    private void MainLeftSplitterDragDelta(object sender, ResizeDeltaEventArgs e)
    {
        ResizeColumns(
            MainWorkspaceGrid.ColumnDefinitions[0],
            MainWorkspaceGrid.ColumnDefinitions[2],
            e.HorizontalChange,
            minLeading: 210,
            minTrailing: 420);
    }

    private void MainRightSplitterDragDelta(object sender, ResizeDeltaEventArgs e)
    {
        ResizeColumns(
            MainWorkspaceGrid.ColumnDefinitions[2],
            MainWorkspaceGrid.ColumnDefinitions[4],
            e.HorizontalChange,
            minLeading: 420,
            minTrailing: 230);
    }

    private void LeftPaneSplitterDragDelta(object sender, ResizeDeltaEventArgs e)
    {
        ResizeRows(
            LeftPaneGrid.RowDefinitions[0],
            LeftPaneGrid.RowDefinitions[2],
            e.VerticalChange,
            minLeading: 96,
            minTrailing: 72,
            keepProportional: true);
    }

    private void RightPaneSplitterDragDelta(object sender, ResizeDeltaEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string tag)
            return;

        var parts = tag.Split(',');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var leadingIndex)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var trailingIndex))
            return;

        ResizeRows(
            RightPaneGrid.RowDefinitions[leadingIndex],
            RightPaneGrid.RowDefinitions[trailingIndex],
            e.VerticalChange,
            minLeading: 54,
            minTrailing: 54,
            keepProportional: true);
    }

    private void BottomPanelSplitterDragDelta(object sender, ResizeDeltaEventArgs e)
    {
        ResizeRows(
            MainShellGrid.RowDefinitions[1],
            MainShellGrid.RowDefinitions[3],
            e.VerticalChange,
            minLeading: 260,
            minTrailing: 48,
            keepProportional: false);
    }

    private static void ResizeColumns(
        ColumnDefinition leading,
        ColumnDefinition trailing,
        double delta,
        double minLeading,
        double minTrailing)
    {
        var leadingWidth = EffectiveColumnWidth(leading);
        var trailingWidth = EffectiveColumnWidth(trailing);
        var combined = leadingWidth + trailingWidth;

        if (combined <= minLeading + minTrailing)
            return;

        var newLeading = Math.Clamp(leadingWidth + delta, minLeading, combined - minTrailing);
        leading.Width = new GridLength(newLeading);
        trailing.Width = new GridLength(combined - newLeading);
    }

    private static void ResizeRows(
        RowDefinition leading,
        RowDefinition trailing,
        double delta,
        double minLeading,
        double minTrailing,
        bool keepProportional)
    {
        var leadingHeight = EffectiveRowHeight(leading);
        var trailingHeight = EffectiveRowHeight(trailing);
        var combined = leadingHeight + trailingHeight;

        if (combined <= minLeading + minTrailing)
            return;

        var newLeading = Math.Clamp(leadingHeight + delta, minLeading, combined - minTrailing);
        var newTrailing = combined - newLeading;

        if (keepProportional)
        {
            leading.Height = new GridLength(newLeading, GridUnitType.Star);
            trailing.Height = new GridLength(newTrailing, GridUnitType.Star);
        }
        else
        {
            leading.Height = new GridLength(newLeading, GridUnitType.Star);
            trailing.Height = new GridLength(newTrailing);
        }
    }

    private static double EffectiveColumnWidth(ColumnDefinition column)
    {
        if (column.ActualWidth > 0)
            return column.ActualWidth;

        return column.Width.IsAbsolute && column.Width.Value > 0
            ? column.Width.Value
            : 300;
    }

    private static double EffectiveRowHeight(RowDefinition row)
    {
        if (row.ActualHeight > 0)
            return row.ActualHeight;

        return row.Height.IsAbsolute && row.Height.Value > 0
            ? row.Height.Value
            : 100;
    }

    private void LogMessage(string message)
    {
        if (messageItems.Count >= 3)
            messageItems.RemoveAt(0);

        messageItems.Add(message);
    }

    private void UpdateStatus(string message)
    {
        StatusText.Text = message;
    }

    private void RefreshDomainDirtyState()
    {
        if (currentDomain == null || CompositionStatus == null)
            return;

        var baseStatus = currentSummary?.Kind ?? currentDomain.Summary ?? "Editable Domain";
        var projection = currentDomain.IsProjectedFromLegacyPackage ? " projected domain" : " Domain JSON";
        var dirty = currentDomain.IsDirty ? " *" : string.Empty;
        CompositionStatus.Text = baseStatus + " -" + projection + dirty;
    }
}

public sealed class DomainCatalogItemViewModel
{
    public string FullPath { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public BitmapImage? Snapshot { get; init; }

    public BitmapImage? Pictogram { get; init; }
}

public sealed class ResizeDeltaEventArgs : EventArgs
{
    public ResizeDeltaEventArgs(double horizontalChange, double verticalChange)
    {
        HorizontalChange = horizontalChange;
        VerticalChange = verticalChange;
    }

    public double HorizontalChange { get; }

    public double VerticalChange { get; }
}

public sealed class ResizeGrip : Control
{
    private bool isDragging;
    private double lastX;
    private double lastY;

    public event EventHandler<ResizeDeltaEventArgs>? DragDelta;

    public string CursorShape { get; set; } = nameof(InputSystemCursorShape.Arrow);

    public ResizeGrip()
    {
        Loaded += ResizeGripLoaded;
        PointerPressed += ResizeGripPointerPressed;
        PointerMoved += ResizeGripPointerMoved;
        PointerReleased += ResizeGripPointerReleased;
        PointerCanceled += ResizeGripPointerCanceled;
        PointerCaptureLost += ResizeGripPointerCaptureLost;
    }

    private void ResizeGripLoaded(object sender, RoutedEventArgs e)
    {
        if (Enum.TryParse<InputSystemCursorShape>(CursorShape, out var shape))
            ProtectedCursor = InputSystemCursor.Create(shape);
    }

    private void ResizeGripPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(this).Position;
        lastX = position.X;
        lastY = position.Y;
        isDragging = true;
        CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ResizeGripPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!isDragging)
            return;

        var position = e.GetCurrentPoint(this).Position;
        var horizontalChange = position.X - lastX;
        var verticalChange = position.Y - lastY;
        lastX = position.X;
        lastY = position.Y;

        if (Math.Abs(horizontalChange) > 0.01 || Math.Abs(verticalChange) > 0.01)
            DragDelta?.Invoke(this, new ResizeDeltaEventArgs(horizontalChange, verticalChange));

        e.Handled = true;
    }

    private void ResizeGripPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        isDragging = false;
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void ResizeGripPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        isDragging = false;
        e.Handled = true;
    }

    private void ResizeGripPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        isDragging = false;
    }
}

public enum ContentNodeKind
{
    View,
    Concept,
    Relationship,
    Domain,
    Document
}

public sealed record ContentTreeItemViewModel
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public ContentNodeKind Kind { get; init; }

    public int SortRank { get; init; }

    public SolidColorBrush SymbolFillBrush { get; init; } = new(Windows.UI.Color.FromArgb(255, 226, 232, 240));

    public SolidColorBrush SymbolStrokeBrush { get; init; } = new(Windows.UI.Color.FromArgb(255, 14, 165, 233));

    public BitmapImage? PictographSource { get; init; }

    public Visibility PictographVisibility { get; init; } = Visibility.Collapsed;

    public Visibility ShapeSymbolVisibility { get; init; } = Visibility.Visible;

    public Visibility RelationshipSymbolVisibility { get; init; } = Visibility.Collapsed;

    public Visibility ViewSymbolVisibility { get; init; } = Visibility.Collapsed;

    public IReadOnlyList<ContentTreeItemViewModel> Children { get; init; } = Array.Empty<ContentTreeItemViewModel>();
}

public sealed class SymbolGlyph : Grid
{
    public static readonly DependencyProperty ShapeNameProperty =
        DependencyProperty.Register(nameof(ShapeName), typeof(string), typeof(SymbolGlyph), new PropertyMetadata("Rectangle", OnVisualPropertyChanged));

    public static readonly DependencyProperty SymbolFillProperty =
        DependencyProperty.Register(nameof(SymbolFill), typeof(Brush), typeof(SymbolGlyph), new PropertyMetadata(new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)), OnVisualPropertyChanged));

    public static readonly DependencyProperty SymbolStrokeProperty =
        DependencyProperty.Register(nameof(SymbolStroke), typeof(Brush), typeof(SymbolGlyph), new PropertyMetadata(new SolidColorBrush(Windows.UI.Color.FromArgb(255, 17, 24, 39)), OnVisualPropertyChanged));

    public static readonly DependencyProperty SymbolThicknessProperty =
        DependencyProperty.Register(nameof(SymbolThickness), typeof(double), typeof(SymbolGlyph), new PropertyMetadata(1.5, OnVisualPropertyChanged));

    public string ShapeName
    {
        get => (string)GetValue(ShapeNameProperty);
        set => SetValue(ShapeNameProperty, value);
    }

    public Brush SymbolFill
    {
        get => (Brush)GetValue(SymbolFillProperty);
        set => SetValue(SymbolFillProperty, value);
    }

    public Brush SymbolStroke
    {
        get => (Brush)GetValue(SymbolStrokeProperty);
        set => SetValue(SymbolStrokeProperty, value);
    }

    public double SymbolThickness
    {
        get => (double)GetValue(SymbolThicknessProperty);
        set => SetValue(SymbolThicknessProperty, value);
    }

    public SymbolGlyph()
    {
        Loaded += (_, _) => UpdateVisual();
        SizeChanged += (_, _) => UpdateVisual();
    }

    private static void OnVisualPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is SymbolGlyph glyph)
            glyph.UpdateVisual();
    }

    private void UpdateVisual()
    {
        Children.Clear();

        var width = ActualWidth > 0 ? ActualWidth : Width > 0 ? Width : 36;
        var height = ActualHeight > 0 ? ActualHeight : Height > 0 ? Height : 18;
        if (width <= 0 || height <= 0)
            return;

        var canvas = new Canvas
        {
            Width = width,
            Height = height,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Children.Add(canvas);

        var fill = SymbolFill ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
        var stroke = SymbolStroke ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 17, 24, 39));
        var thickness = SymbolThickness <= 0 ? 1.0 : SymbolThickness;
        var shape = ThinkComposerVisualCatalog.NormalizeShapeTechName(ShapeName, "Rectangle");

        switch (shape)
        {
            case "None":
                AddLine(canvas, 2, height * 0.5, width - 2, height * 0.5, stroke, thickness);
                break;
            case "Poster":
                AddBorder(canvas, width * 0.08, height * 0.12, width * 0.84, height * 0.62, fill, stroke, thickness, 1);
                AddBorder(canvas, width * 0.38, height * 0.72, width * 0.24, height * 0.18, fill, stroke, Math.Max(1, thickness * 0.75), 1);
                AddLine(canvas, width * 0.22, height * 0.90, width * 0.78, height * 0.90, stroke, Math.Max(1, thickness * 0.75));
                break;
            case "Signboard":
                AddBorder(canvas, width * 0.04, height * 0.08, width * 0.92, height * 0.50, fill, stroke, thickness, 1);
                AddLine(canvas, width * 0.50, height * 0.58, width * 0.50, height * 0.94, stroke, thickness);
                AddLine(canvas, width * 0.25, height * 0.94, width * 0.75, height * 0.94, stroke, thickness);
                break;
            case "Capsule":
            case "Button":
                AddBorder(canvas, 1, 1, width - 2, height - 2, fill, stroke, thickness, height / 2);
                break;
            case "RoundedRectangle":
                AddBorder(canvas, 1, 1, width - 2, height - 2, fill, stroke, thickness, 5);
                break;
            case "Ellipse":
            case "Anything":
                AddEllipse(canvas, 1, 1, width - 2, height - 2, fill, stroke, thickness);
                break;
            case "EllipseEnclosed":
            case "EllipseIntercrossed":
            case "EllipseIntercrossedDiagonal":
                AddEllipse(canvas, 1, 1, width - 2, height - 2, fill, stroke, thickness);
                AddEllipse(canvas, width * 0.12, height * 0.18, width * 0.76, height * 0.64, null, stroke, Math.Max(1, thickness * 0.75));
                if (shape == "EllipseIntercrossed" || shape == "EllipseIntercrossedDiagonal")
                {
                    AddLine(canvas, width * 0.18, height * 0.5, width * 0.82, height * 0.5, stroke, Math.Max(1, thickness * 0.75));
                    AddLine(canvas, width * 0.5, height * 0.18, width * 0.5, height * 0.82, stroke, Math.Max(1, thickness * 0.75));
                }

                if (shape == "EllipseIntercrossedDiagonal")
                    AddLine(canvas, width * 0.18, height * 0.8, width * 0.82, height * 0.2, stroke, Math.Max(1, thickness * 0.75));
                break;
            case "Gears":
                AddSourceRect(canvas, width, height, 24, 0, 101, 41, fill, stroke, thickness, 5);
                AddSourceGear(canvas, width, height, 0, -16, fill, stroke, thickness);
                AddSourceGear(canvas, width, height, 0, 6, fill, stroke, thickness);
                break;
            case "Person":
                AddSourceRect(canvas, width, height, 24, 0, 101, 41, fill, stroke, thickness, 5);
                AddSourceEllipse(canvas, width, height, 10, 5, 5, 5, null, stroke, thickness);
                AddSourceLine(canvas, width, height, 10, 10, 10, 24, stroke, thickness);
                AddSourceLine(canvas, width, height, 0, 16, 20, 16, stroke, thickness);
                AddSourceLine(canvas, width, height, 10, 24, 4, 41, stroke, thickness);
                AddSourceLine(canvas, width, height, 10, 24, 16, 41, stroke, thickness);
                break;
            case "Component":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 14, 5, 14, 0, 124, 0, 124, 40, 14, 40, 14, 34, 0, 34, 0, 24, 14, 24, 14, 15, 0, 15, 0, 5);
                AddSourceRect(canvas, width, height, 0, 5, 26, 10, fill, stroke, thickness);
                AddSourceRect(canvas, width, height, 0, 24, 26, 10, fill, stroke, thickness);
                break;
            case "Block":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 11, 0, 124, 0, 124, 31, 116, 40, 0, 40, 0, 8);
                AddSourceLine(canvas, width, height, 0, 8, 116, 8, stroke, Math.Max(1, thickness * 0.75));
                AddSourceLine(canvas, width, height, 116, 8, 124, 0, stroke, Math.Max(1, thickness * 0.75));
                AddSourceLine(canvas, width, height, 116, 8, 116, 40, stroke, Math.Max(1, thickness * 0.75));
                break;
            case "Piece":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 0, 5, 40, 5, 44, 0, 82, 0, 86, 5, 119, 5, 119, 14, 125, 18, 125, 28, 119, 32, 119, 41, 87, 41, 83, 36, 43, 36, 39, 41, 0, 41, 0, 33, 6, 29, 6, 17, 0, 13);
                break;
            case "Card":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 0, 12, 24, 0, 124, 0, 124, 40, 0, 40);
                break;
            case "Document":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 0, 0, 124, 0, 124, 34, 94, 33, 63, 29, 42, 41, 0, 38);
                break;
            case "File":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 0, 0, 104, 0, 124, 10, 124, 40, 0, 40);
                AddSourceLine(canvas, width, height, 104, 0, 104, 10, stroke, Math.Max(1, thickness * 0.75));
                AddSourceLine(canvas, width, height, 104, 10, 124, 10, stroke, Math.Max(1, thickness * 0.75));
                break;
            case "Folder":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 0, 7, 5, 0, 40, 0, 45, 7, 124, 7, 124, 40, 0, 40);
                AddSourceLine(canvas, width, height, 0, 7, 62, 7, stroke, Math.Max(1, thickness * 0.75));
                break;
            case "Bin":
                AddSourceLine(canvas, width, height, 0, 0, 0, 41, stroke, thickness);
                AddSourceLine(canvas, width, height, 125, 0, 125, 41, stroke, thickness);
                AddSourceLine(canvas, width, height, 0, 39, 125, 39, stroke, thickness);
                AddSourceLine(canvas, width, height, 0, 41, 125, 41, stroke, thickness);
                break;
            case "Flag":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 0, 0, 124, 0, 96, 20, 124, 40, 0, 40);
                break;
            case "Banner":
                AddPolygon(canvas, fill, stroke, thickness, 1, height * 0.22, width * 0.25, height * 0.12, width * 0.48, height * 0.22, width * 0.74, height * 0.12, width - 1, height * 0.22, width - 1, height * 0.78, width * 0.74, height * 0.88, width * 0.48, height * 0.78, width * 0.25, height * 0.88, 1, height * 0.78);
                break;
            case "HexagonVertical":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 0, 8, 63, 0, 124, 8, 124, 32, 63, 40, 0, 32);
                break;
            case "HexagonHorizontal":
            case "Specification":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 0, 20, 20, 0, 104, 0, 124, 20, 104, 40, 20, 40);
                break;
            case "Octagon":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 0, 10, 31, 0, 93, 0, 124, 10, 124, 30, 93, 40, 31, 40, 0, 30);
                break;
            case "Pentagon":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 0, 17, 63.5, 0, 127, 17, 100, 41, 27, 41);
                break;
            case "Trapezium":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 0, 0, 124, 0, 104, 40, 20, 40);
                break;
            case "Funnel":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 0, 0, 125, 0, 125, 5, 80, 36, 80, 41, 45, 41, 45, 36, 0, 5);
                break;
            case "Parallelogram":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 14, 0, 124, 0, 110, 40, 0, 40);
                break;
            case "RectDistorted":
                AddPolygon(canvas, fill, stroke, thickness, width * 0.12, height * 0.04, width * 0.92, height * 0.18, width * 0.84, height * 0.92, width * 0.04, height * 0.78);
                break;
            case "RectCurved":
                AddPolygon(canvas, fill, stroke, thickness, width * 0.08, height * 0.08, width * 0.92, height * 0.08, width * 0.82, height * 0.50, width * 0.92, height * 0.92, width * 0.08, height * 0.92, width * 0.18, height * 0.50);
                break;
            case "Plate":
                AddPolygon(canvas, fill, stroke, thickness, width * 0.08, height * 0.16, width * 0.92, height * 0.16, width * 0.84, height * 0.84, width * 0.16, height * 0.84);
                break;
            case "Dome":
                AddPolygon(canvas, fill, stroke, thickness, width * 0.08, height * 0.92, width * 0.92, height * 0.92, width * 0.82, height * 0.30, width * 0.66, height * 0.10, width * 0.34, height * 0.10, width * 0.18, height * 0.30);
                break;
            case "Spin":
                AddEllipse(canvas, width * 0.08, height * 0.16, width * 0.84, height * 0.68, fill, stroke, thickness);
                AddLine(canvas, width * 0.18, height * 0.72, width * 0.82, height * 0.28, stroke, Math.Max(1, thickness * 0.75));
                break;
            case "Triangle":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 63, 0, 124, 40, 0, 40);
                break;
            case "Rhomb":
            case "RhombCrossed":
                AddPolygon(canvas, fill, stroke, thickness, width * 0.5, 1, width - 1, height * 0.5, width * 0.5, height - 1, 1, height * 0.5);
                if (shape == "RhombCrossed")
                    AddLine(canvas, width * 0.18, height * 0.5, width * 0.82, height * 0.5, stroke, Math.Max(1, thickness * 0.75));
                break;
            case "ChevronHorizontal":
                AddPolygon(canvas, fill, stroke, thickness, 1, 1, width * 0.72, 1, width - 1, height * 0.5, width * 0.72, height - 1, 1, height - 1, width * 0.24, height * 0.5);
                break;
            case "ChevronVertical":
                AddPolygon(canvas, fill, stroke, thickness, width * 0.5, 1, width - 1, height * 0.28, width - 1, height - 1, width * 0.5, height * 0.75, 1, height - 1, 1, height * 0.28);
                break;
            case "BowTie":
            case "OppositeTriangles":
                AddPolygon(canvas, fill, stroke, thickness, 1, 1, width * 0.5, height * 0.5, 1, height - 1, width - 1, height - 1, width * 0.5, height * 0.5, width - 1, 1);
                break;
            case "Arrow":
            case "Stage":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 0, 6, 104, 6, 104, 0, 124, 20, 104, 40, 104, 34, 0, 34);
                break;
            case "ArrowRegular":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 0, 20, 0, 6, 98, 6, 92, 0, 104, 0, 124, 20, 104, 40, 92, 40, 98, 34, 0, 34);
                break;
            case "ArrowDouble":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 0, 20, 20, 0, 20, 6, 104, 6, 104, 0, 124, 20, 104, 40, 104, 34, 20, 34, 20, 40);
                break;
            case "ArrowRegularDouble":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 0, 20, 20, 0, 32, 0, 26, 6, 98, 6, 92, 0, 104, 0, 124, 20, 104, 40, 92, 40, 98, 34, 26, 34, 32, 40, 20, 40);
                break;
            case "Tape":
                AddPolygon(canvas, fill, stroke, thickness, width * 0.04, height * 0.30, width * 0.92, height * 0.18, width * 0.96, height * 0.70, width * 0.08, height * 0.82);
                break;
            case "Envelope":
                AddBorder(canvas, 1, 1, width - 2, height - 2, fill, stroke, thickness, 1);
                AddLine(canvas, 1, 1, width * 0.5, height * 0.58, stroke, Math.Max(1, thickness * 0.75));
                AddLine(canvas, width - 1, 1, width * 0.5, height * 0.58, stroke, Math.Max(1, thickness * 0.75));
                AddLine(canvas, 1, height - 1, width * 0.38, height * 0.45, stroke, Math.Max(1, thickness * 0.75));
                AddLine(canvas, width - 1, height - 1, width * 0.62, height * 0.45, stroke, Math.Max(1, thickness * 0.75));
                break;
            case "Drum":
                AddSourceRect(canvas, width, height, 0, 6, 125, 29, fill, stroke, thickness, 0);
                AddSourceEllipse(canvas, width, height, 62.5, 6, 62.5, 6, fill, stroke, thickness);
                AddSourceEllipse(canvas, width, height, 62.5, 35, 62.5, 6, null, stroke, Math.Max(1, thickness * 0.75));
                break;
            case "Barrel":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 0, 8, 31, 0, 94, 0, 124, 8, 124, 32, 62.5, 41, 0, 32);
                break;
            case "Cloud":
                AddEllipse(canvas, width * 0.06, height * 0.38, width * 0.34, height * 0.42, fill, stroke, thickness);
                AddEllipse(canvas, width * 0.24, height * 0.16, width * 0.38, height * 0.54, fill, stroke, thickness);
                AddEllipse(canvas, width * 0.50, height * 0.30, width * 0.42, height * 0.48, fill, stroke, thickness);
                AddBorder(canvas, width * 0.16, height * 0.50, width * 0.68, height * 0.30, fill, null, 0, 0);
                break;
            case "RectCrossedHorizontal":
            case "RectCrossedVertical":
            case "RectIntercrossed":
            case "RectIntercrossedDiagonal":
            case "RectCrossed":
            case "RectCrossedTop":
            case "RectCrossedCorner":
            case "RectEnclosed":
            case "RectDiagonal":
            case "MetaObject":
                AddBorder(canvas, 1, 1, width - 2, height - 2, fill, stroke, thickness, 1);
                if (shape.Contains("Horizontal", StringComparison.Ordinal) || shape == "RectCrossed" || shape == "RectCrossedTop" || shape == "RectIntercrossed")
                    AddLine(canvas, 1, height * 0.5, width - 1, height * 0.5, stroke, Math.Max(1, thickness * 0.75));
                if (shape.Contains("Vertical", StringComparison.Ordinal) || shape == "RectCrossed" || shape == "RectIntercrossed")
                    AddLine(canvas, width * 0.5, 1, width * 0.5, height - 1, stroke, Math.Max(1, thickness * 0.75));
                if (shape.EndsWith("Diagonal", StringComparison.Ordinal) || shape == "RectDiagonal")
                    AddLine(canvas, 1, height - 1, width - 1, 1, stroke, Math.Max(1, thickness * 0.75));
                if (shape == "RectEnclosed")
                    AddBorder(canvas, width * 0.14, height * 0.18, width * 0.72, height * 0.64, null, stroke, Math.Max(1, thickness * 0.75), 1);
                break;
            case "Standard":
                AddSourcePolygon(canvas, width, height, fill, stroke, thickness, 0, 0, 124, 0, 124, 32, 63, 40, 0, 32);
                break;
            case "Wrapper":
                AddLine(canvas, width * 0.12, height * 0.10, width * 0.12, height * 0.90, stroke, thickness);
                AddLine(canvas, width * 0.12, height * 0.10, width * 0.92, height * 0.10, stroke, thickness);
                AddLine(canvas, width * 0.12, height * 0.90, width * 0.92, height * 0.90, stroke, thickness);
                AddLine(canvas, width * 0.92, height * 0.22, width * 0.92, height * 0.78, stroke, Math.Max(1, thickness * 0.75));
                break;
            case "XMark":
                AddLine(canvas, 2, 2, width - 2, height - 2, stroke, thickness);
                AddLine(canvas, width - 2, 2, 2, height - 2, stroke, thickness);
                break;
            case "StraightParallelLines":
                AddLine(canvas, 2, height * 0.36, width - 2, height * 0.36, stroke, thickness);
                AddLine(canvas, 2, height * 0.64, width - 2, height * 0.64, stroke, thickness);
                break;
            case "StraightUnderLine":
                AddLine(canvas, 2, height * 0.42, width - 2, height * 0.42, stroke, thickness);
                AddLine(canvas, 2, height * 0.72, width - 2, height * 0.72, stroke, Math.Max(1, thickness * 0.75));
                break;
            case "BracketsSquare":
            case "BracketsCurved":
            case "BracketsCurly":
                AddLine(canvas, 4, 2, 4, height - 2, stroke, thickness);
                AddLine(canvas, width - 4, 2, width - 4, height - 2, stroke, thickness);
                AddLine(canvas, 4, 2, width * 0.22, 2, stroke, thickness);
                AddLine(canvas, 4, height - 2, width * 0.22, height - 2, stroke, thickness);
                AddLine(canvas, width * 0.78, 2, width - 4, 2, stroke, thickness);
                AddLine(canvas, width * 0.78, height - 2, width - 4, height - 2, stroke, thickness);
                break;
            case "Module":
                AddBorder(canvas, 1, height * 0.18, width * 0.78, height * 0.64, fill, stroke, thickness, 1);
                AddPolygon(canvas, Darken(fill), stroke, thickness, width * 0.78, height * 0.18, width - 1, height * 0.08, width - 1, height * 0.72, width * 0.78, height * 0.82);
                break;
            default:
                AddBorder(canvas, 1, 1, width - 2, height - 2, fill, stroke, thickness, 1);
                break;
        }
    }

    private static double SourceX(double value, double width) => value * width / 125.0;

    private static double SourceY(double value, double height) => value * height / 41.0;

    private static void AddSourceRect(Canvas canvas, double symbolWidth, double symbolHeight, double x, double y, double width, double height, Brush fill, Brush stroke, double thickness, double radius = 0)
    {
        AddBorder(
            canvas,
            SourceX(x, symbolWidth),
            SourceY(y, symbolHeight),
            SourceX(width, symbolWidth),
            SourceY(height, symbolHeight),
            fill,
            stroke,
            thickness,
            Math.Min(SourceX(radius, symbolWidth), SourceY(radius, symbolHeight)));
    }

    private static void AddSourceEllipse(Canvas canvas, double symbolWidth, double symbolHeight, double centerX, double centerY, double radiusX, double radiusY, Brush fill, Brush stroke, double thickness)
    {
        AddEllipse(
            canvas,
            SourceX(centerX - radiusX, symbolWidth),
            SourceY(centerY - radiusY, symbolHeight),
            SourceX(radiusX * 2, symbolWidth),
            SourceY(radiusY * 2, symbolHeight),
            fill,
            stroke,
            thickness);
    }

    private static void AddSourceLine(Canvas canvas, double symbolWidth, double symbolHeight, double x1, double y1, double x2, double y2, Brush stroke, double thickness)
    {
        AddLine(
            canvas,
            SourceX(x1, symbolWidth),
            SourceY(y1, symbolHeight),
            SourceX(x2, symbolWidth),
            SourceY(y2, symbolHeight),
            stroke,
            thickness);
    }

    private static void AddSourcePolygon(Canvas canvas, double symbolWidth, double symbolHeight, Brush fill, Brush stroke, double thickness, params double[] coordinates)
    {
        var scaled = new double[coordinates.Length];
        for (var index = 0; index + 1 < coordinates.Length; index += 2)
        {
            scaled[index] = SourceX(coordinates[index], symbolWidth);
            scaled[index + 1] = SourceY(coordinates[index + 1], symbolHeight);
        }

        AddPolygon(canvas, fill, stroke, thickness, scaled);
    }

    private static void AddSourceGear(Canvas canvas, double symbolWidth, double symbolHeight, double offsetX, double offsetY, Brush fill, Brush stroke, double thickness)
    {
        AddSourcePolygon(
            canvas,
            symbolWidth,
            symbolHeight,
            fill,
            stroke,
            thickness,
            8 + offsetX, 20 + offsetY,
            8 + offsetX, 16 + offsetY,
            12 + offsetX, 16 + offsetY,
            12 + offsetX, 20 + offsetY,
            15 + offsetX, 17 + offsetY,
            18 + offsetX, 20 + offsetY,
            15 + offsetX, 23 + offsetY,
            19 + offsetX, 23 + offsetY,
            19 + offsetX, 27 + offsetY,
            15 + offsetX, 27 + offsetY,
            18 + offsetX, 30 + offsetY,
            15 + offsetX, 33 + offsetY,
            12 + offsetX, 30 + offsetY,
            12 + offsetX, 34 + offsetY,
            8 + offsetX, 34 + offsetY,
            8 + offsetX, 30 + offsetY,
            5 + offsetX, 33 + offsetY,
            2 + offsetX, 30 + offsetY,
            5 + offsetX, 27 + offsetY,
            1 + offsetX, 27 + offsetY,
            1 + offsetX, 23 + offsetY,
            5 + offsetX, 23 + offsetY,
            2 + offsetX, 20 + offsetY,
            5 + offsetX, 17 + offsetY);
        AddSourceEllipse(canvas, symbolWidth, symbolHeight, 10 + offsetX, 25 + offsetY, 6, 6, null, stroke, Math.Max(1, thickness * 0.75));
        AddSourceEllipse(canvas, symbolWidth, symbolHeight, 10 + offsetX, 25 + offsetY, 3, 3, fill, stroke, Math.Max(1, thickness * 0.75));
    }

    private static Brush Darken(Brush brush)
    {
        if (brush is SolidColorBrush solid)
        {
            var color = solid.Color;
            return new SolidColorBrush(Windows.UI.Color.FromArgb(color.A, (byte)(color.R * 0.75), (byte)(color.G * 0.75), (byte)(color.B * 0.75)));
        }

        return brush;
    }

    private static void AddGearDot(Canvas canvas, double x, double y, double size, Brush fill, Brush stroke, double thickness)
    {
        AddEllipse(canvas, x, y, size, size, fill, stroke, thickness);
        AddEllipse(canvas, x + size * 0.28, y + size * 0.28, size * 0.44, size * 0.44, null, stroke, Math.Max(1, thickness * 0.6));
    }

    private static void AddBorder(Canvas canvas, double x, double y, double width, double height, Brush fill, Brush stroke, double thickness, double radius)
    {
        var border = new Border
        {
            Width = Math.Max(0, width),
            Height = Math.Max(0, height),
            Background = fill,
            BorderBrush = stroke,
            BorderThickness = stroke == null || thickness <= 0 ? new Thickness(0) : new Thickness(thickness),
            CornerRadius = new CornerRadius(radius)
        };
        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y);
        canvas.Children.Add(border);
    }

    private static void AddEllipse(Canvas canvas, double x, double y, double width, double height, Brush fill, Brush stroke, double thickness)
    {
        var ellipse = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = Math.Max(0, width),
            Height = Math.Max(0, height),
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = stroke == null || thickness <= 0 ? 0 : thickness
        };
        Canvas.SetLeft(ellipse, x);
        Canvas.SetTop(ellipse, y);
        canvas.Children.Add(ellipse);
    }

    private static void AddLine(Canvas canvas, double x1, double y1, double x2, double y2, Brush stroke, double thickness)
    {
        canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = stroke,
            StrokeThickness = stroke == null || thickness <= 0 ? 0 : thickness
        });
    }

    private static void AddPolygon(Canvas canvas, Brush fill, Brush stroke, double thickness, params double[] coordinates)
    {
        var polygon = new Microsoft.UI.Xaml.Shapes.Polygon
        {
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = stroke == null || thickness <= 0 ? 0 : thickness
        };

        for (var index = 0; index + 1 < coordinates.Length; index += 2)
            polygon.Points.Add(new Windows.Foundation.Point(coordinates[index], coordinates[index + 1]));

        canvas.Children.Add(polygon);
    }
}

public sealed class PaletteItemViewModel : INotifyPropertyChanged
{
    private bool isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; init; } = string.Empty;

    public string Subtitle { get; init; } = string.Empty;

    public string FillColorHex { get; init; } = "#FF64748B";

    public string StrokeColorHex { get; init; } = "#FF64748B";

    public SolidColorBrush AccentBrush { get; init; } = new(Windows.UI.Color.FromArgb(255, 100, 116, 139));

    public SolidColorBrush FillBrush { get; init; } = new(Windows.UI.Color.FromArgb(255, 100, 116, 139));

    public SolidColorBrush StrokeBrush { get; init; } = new(Windows.UI.Color.FromArgb(255, 100, 116, 139));

    public string ShapeName { get; init; } = "Rectangle";

    public CornerRadius SymbolCornerRadius { get; init; } = new(2);

    public PointCollection SymbolPoints { get; init; } = new();

    public BitmapImage? PictogramSource { get; init; }

    public Visibility PictographVisibility { get; init; } = Visibility.Collapsed;

    public Visibility BorderSymbolVisibility { get; init; } = Visibility.Visible;

    public Visibility EllipseSymbolVisibility { get; init; } = Visibility.Collapsed;

    public Visibility PolygonSymbolVisibility { get; init; } = Visibility.Collapsed;

    public Visibility GlyphSymbolVisibility { get; init; } = Visibility.Visible;

    public Visibility InlineActionsVisibility => isSelected && HasEditableDefinition
        ? Visibility.Visible
        : Visibility.Collapsed;

    public void SetSelected(bool value)
    {
        if (isSelected == value)
            return;

        isSelected = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InlineActionsVisibility)));
    }

    public EditableConceptDefinition? ConceptDefinition { get; init; }

    public EditableRelationshipDefinition? RelationshipDefinition { get; init; }

    public EditableMarkerDefinition? MarkerDefinition { get; init; }

    public EditableComplementDefinition? ComplementDefinition { get; init; }

    private bool HasEditableDefinition =>
        ConceptDefinition != null
        || RelationshipDefinition != null
        || MarkerDefinition != null
        || ComplementDefinition != null;
}

public sealed class ConceptStylePresetViewModel
{
    public ConceptStylePresetViewModel(ThinkComposerGraphicStylePreset preset)
    {
        Name = preset.Name;
        FillColorHex = preset.FillColorHex;
        StrokeColorHex = preset.StrokeColorHex;
        LineThickness = preset.LineThickness;
        LineDash = preset.LineDash;
        FillBrush = new SolidColorBrush(MainPageColorFromHex(FillColorHex, Windows.UI.Color.FromArgb(255, 255, 255, 255)));
        StrokeBrush = new SolidColorBrush(MainPageColorFromHex(StrokeColorHex, Windows.UI.Color.FromArgb(255, 31, 41, 55)));
        BorderThickness = new Thickness(Math.Max(1, LineThickness));
    }

    public string Name { get; }

    public string FillColorHex { get; }

    public string StrokeColorHex { get; }

    public double LineThickness { get; }

    public string LineDash { get; }

    public SolidColorBrush FillBrush { get; }

    public SolidColorBrush StrokeBrush { get; }

    public Thickness BorderThickness { get; }

    private static Windows.UI.Color MainPageColorFromHex(string value, Windows.UI.Color fallback)
    {
        return TcColor.TryParseHex(value, out var color)
            ? Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B)
            : fallback;
    }
}

public sealed class ShapeOptionViewModel
{
    public ShapeOptionViewModel(ThinkComposerShapeOption option)
    {
        DisplayName = option.DisplayName;
        ShapeName = option.TechName;
        FillBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
        StrokeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 17, 24, 39));
    }

    public string DisplayName { get; }

    public string ShapeName { get; }

    public SolidColorBrush FillBrush { get; }

    public SolidColorBrush StrokeBrush { get; }
}
