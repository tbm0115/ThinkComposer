// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Copyright (C) 2011-2015 Néstor Marcel Sánchez Ahumada.
// https://github.com/nmarcel/ThinkComposer
//
// This file is part of ThinkComposer, which is free software licensed under the GNU General Public License.
// It is provided without any warranty. You should find a copy of the license in the root directory of this software product.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

using Instrumind.Common;
using Instrumind.Common.Visualization;
using Instrumind.ThinkComposer.ApplicationProduct;
using Instrumind.ThinkComposer.MetaModel;
using Instrumind.ThinkComposer.MetaModel.VisualMetaModel;
using Instrumind.ThinkComposer.Model.GraphModel;
using Instrumind.ThinkComposer.Model.VisualModel;

namespace Instrumind.ThinkComposer.Composer.Layout
{
    /// <summary>
    /// Fits Concept visual-symbol widths to their visible title text.
    /// </summary>
    public static class ConceptAutoFitService
    {
        public const double MinWidth = 72.0;
        public const double MaxWidth = 420.0;
        public const double HorizontalPadding = 24.0;
        public const double ExtraSafetyPadding = 16.0;

        private const double MeasurementMaxWidth = 2000.0;
        private const double WidthChangeEpsilon = 0.5;

        public static bool CanFitSelection(CompositionEngine Engine)
        {
            var Context = LayoutSelectionContext.FromActiveView(Engine);
            return Context.SelectedConceptSymbols.Any();
        }

        public static ConceptAutoFitResult FitSelectedConceptWidths(CompositionEngine Engine)
        {
            var Context = LayoutSelectionContext.FromActiveView(Engine);
            return FitConceptSymbols(Engine, Context.SelectedConceptSymbols, "selection");
        }

        public static ConceptAutoFitResult FitSingleConceptWidth(CompositionEngine Engine, VisualSymbol Symbol, string Source)
        {
            return FitConceptSymbols(Engine, Symbol == null ? Enumerable.Empty<VisualSymbol>() : new[] { Symbol }, Source);
        }

        public static ConceptAutoFitResult FitConceptSymbols(CompositionEngine Engine, IEnumerable<VisualSymbol> Symbols, string Source)
        {
            var Result = new ConceptAutoFitResult();
            var SymbolList = (Symbols ?? Enumerable.Empty<VisualSymbol>()).Where(Symbol => Symbol != null).Distinct().ToList();

            Console.WriteLine("Appearance: Fit Concept Width to Text starting; source={0}; selected/target symbols={1}.",
                              Source ?? "unspecified", SymbolList.Count);

            if (Engine == null || Engine.CurrentView == null)
            {
                Result.AddWarning("No active composition view is available.");
                Result.SymbolsSkipped = SymbolList.Count;
                LogResult(Result);
                return Result;
            }

            var View = Engine.CurrentView;
            var LocalCommand = !View.EditEngine.IsVariating;
            var Changed = false;

            try
            {
                if (LocalCommand)
                    View.EditEngine.StartCommandVariation("Fit Concept Width to Text");

                foreach (var Symbol in SymbolList)
                    FitConceptSymbol(Symbol, Result, ref Changed);

                if (Changed)
                    View.UpdateVersion();

                if (LocalCommand)
                    View.EditEngine.CompleteCommandVariation();

                LogResult(Result);
                return Result;
            }
            catch (Exception Problem)
            {
                Console.WriteLine("Appearance: Fit Concept Width to Text failed. Problem: {0}", Problem.Message);
                Console.WriteLine(Problem.ToString());

                if (LocalCommand && View.EditEngine.IsVariating)
                {
                    try
                    {
                        View.EditEngine.DiscardCommandVariation();
                        Console.WriteLine("Appearance: Fit Concept Width to Text discarded its command variation after failure.");
                    }
                    catch (Exception DiscardProblem)
                    {
                        Console.WriteLine("Appearance: Could not discard failed auto-fit command variation. Problem: {0}", DiscardProblem.Message);
                        Console.WriteLine(DiscardProblem.ToString());
                    }
                }

                throw;
            }
        }

        private static void FitConceptSymbol(VisualSymbol Symbol, ConceptAutoFitResult Result, ref bool Changed)
        {
            Result.SymbolsInspected++;

            var Representation = Symbol.OwnerRepresentation as ConceptVisualRepresentation;
            if (Representation == null)
            {
                Result.SymbolsSkipped++;
                Result.AddWarning("Skipped a non-concept visual symbol.");
                return;
            }

            var Concept = Representation.RepresentedConcept;
            if (Concept == null)
            {
                Result.SymbolsSkipped++;
                Result.AddWarning("Skipped a concept symbol without a represented concept.");
                return;
            }

            if (VisualSymbolFormat.GetHasFixedWidth(Symbol))
            {
                Result.SymbolsSkipped++;
                Result.AddWarning(String.Format(CultureInfo.InvariantCulture,
                                                "Skipped concept '{0}' ({1}) because its visual format has fixed width.",
                                                Concept.Name, Concept.TechName));
                return;
            }

            var OldWidth = Symbol.BaseWidth;
            var NewWidth = DetermineRequiredWidth(Symbol, Concept);

            if (Double.IsNaN(NewWidth) || Double.IsInfinity(NewWidth) || NewWidth <= 0)
            {
                Result.SymbolsSkipped++;
                Result.AddWarning(String.Format(CultureInfo.InvariantCulture,
                                                "Skipped concept '{0}' ({1}) because its text width could not be measured.",
                                                Concept.Name, Concept.TechName));
                return;
            }

            if (Math.Abs(NewWidth - OldWidth) <= WidthChangeEpsilon)
            {
                Result.SymbolsSkipped++;
                Console.WriteLine("Appearance: concept '{0}' ({1}, id={2}) already fits; width={3:0.##}.",
                                  Concept.Name, Concept.TechName, Concept.GlobalId, OldWidth);
                return;
            }

            var PreviousCenter = Symbol.BaseCenter;
            if (!Symbol.ResizeTo(NewWidth, Symbol.BaseHeight))
            {
                Result.SymbolsSkipped++;
                Result.AddWarning(String.Format(CultureInfo.InvariantCulture,
                                                "Skipped concept '{0}' ({1}) because the visual symbol rejected width {2:0.##}.",
                                                Concept.Name, Concept.TechName, NewWidth));
                return;
            }

            Symbol.MoveTo(PreviousCenter.X, PreviousCenter.Y, !Symbol.IsAutoPositionable, true);
            Symbol.RenderElement();

            Changed = true;
            Result.SymbolsFitted++;
            Result.RecordAppliedWidth(NewWidth);

            Console.WriteLine("Appearance: fit concept '{0}' ({1}, id={2}) width {3:0.##} -> {4:0.##}.",
                              Concept.Name, Concept.TechName, Concept.GlobalId, OldWidth, NewWidth);
        }

        private static double DetermineRequiredWidth(VisualSymbol Symbol, Concept Concept)
        {
            var UseNameAsMainTitle = VisualSymbolFormat.GetUseNameAsMainTitle(Symbol);
            var Title = UseNameAsMainTitle ? Concept.Name : Concept.TechName;
            var Subtitle = UseNameAsMainTitle ? Concept.TechName : Concept.Name;
            var TitleFormat = VisualSymbolFormat.GetTextFormat(Symbol, ETextPurpose.Title);
            var SubtitleFormat = VisualSymbolFormat.GetTextFormat(Symbol, ETextPurpose.Subtitle);
            var TextWidth = MeasureTextWidth(TitleFormat, Title, Symbol.BaseHeight);

            if (VisualSymbolFormat.GetSubtitleVisualDisposition(Symbol) != EVisualDispositionMonodimensional.Hidden)
                TextWidth = Math.Max(TextWidth, MeasureTextWidth(SubtitleFormat, Subtitle, Symbol.BaseHeight));

            var ExistingGeometryAllowance = Symbol.BaseContentArea.IsEmpty
                                            ? HorizontalPadding
                                            : Math.Max(0.0, Symbol.BaseWidth - Symbol.BaseContentArea.Width);
            var RequiredWidth = TextWidth + ExistingGeometryAllowance + HorizontalPadding + ExtraSafetyPadding;

            var Minimum = DetermineMinimumWidth(Symbol, Concept);
            return RequiredWidth.EnforceRange(Minimum, MaxWidth);
        }

        private static double DetermineMinimumWidth(VisualSymbol Symbol, Idea Idea)
        {
            var Minimum = Math.Max(MinWidth, ProductDirector.DefaultMinBaseFigureSize.Width);

            if (Idea != null && Idea.IdeaDefinitor != null && Idea.IdeaDefinitor.DefaultSymbolFormat != null)
                Minimum = Math.Max(Minimum, Idea.IdeaDefinitor.DefaultSymbolFormat.InitialWidth);

            if (Idea != null && Idea.Markings != null && Idea.Markings.Count > 0)
            {
                var MarkerSlots = Math.Min(Idea.Markings.Count, 4);
                Minimum = Math.Max(Minimum, MarkerSlots * MarkerDefinition.StandardMarkerIconSize.Width + HorizontalPadding);
            }

            if (Idea != null && (Idea.IsComposite || Idea.HasDetailedContent))
                Minimum = Math.Max(Minimum, 88.0);

            if (Symbol != null && Symbol.AreDetailsShown)
                Minimum = Math.Max(Minimum, 120.0);

            return Minimum;
        }

        private static double MeasureTextWidth(TextFormat Format, string Text, double BaseHeight)
        {
            if (Format == null)
                return Double.NaN;

            Text = Text ?? String.Empty;
            var Lines = Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var MaxLineWidth = 0.0;
            var MaxHeight = Math.Max(1000.0, BaseHeight);

            foreach (var Line in Lines)
            {
                var MeasuredLine = Format.GenerateFormattedText(Line.Length == 0 ? " " : Line,
                                                                MeasurementMaxWidth, MaxHeight);
                MaxLineWidth = Math.Max(MaxLineWidth, MeasuredLine.WidthIncludingTrailingWhitespace);
            }

            return MaxLineWidth;
        }

        private static void LogResult(ConceptAutoFitResult Result)
        {
            Console.WriteLine("Appearance: Fit Concept Width to Text completed; inspected={0}; fitted={1}; skipped={2}; minWidth={3}; maxWidth={4}; warnings={5}.",
                              Result.SymbolsInspected, Result.SymbolsFitted, Result.SymbolsSkipped,
                              Result.MinWidthApplied.HasValue ? Result.MinWidthApplied.Value.ToString("0.##", CultureInfo.InvariantCulture) : "n/a",
                              Result.MaxWidthApplied.HasValue ? Result.MaxWidthApplied.Value.ToString("0.##", CultureInfo.InvariantCulture) : "n/a",
                              Result.Warnings.Count);

            foreach (var Warning in Result.Warnings)
                Console.WriteLine("Appearance warning: {0}", Warning);
        }
    }

    public class ConceptAutoFitResult
    {
        public ConceptAutoFitResult()
        {
            this.Warnings = new List<string>();
        }

        public int SymbolsInspected { get; set; }

        public int SymbolsFitted { get; set; }

        public int SymbolsSkipped { get; set; }

        public double? MinWidthApplied { get; private set; }

        public double? MaxWidthApplied { get; private set; }

        public IList<string> Warnings { get; private set; }

        public void AddWarning(string Warning)
        {
            if (!String.IsNullOrWhiteSpace(Warning))
                this.Warnings.Add(Warning);
        }

        public void RecordAppliedWidth(double Width)
        {
            this.MinWidthApplied = this.MinWidthApplied.HasValue ? Math.Min(this.MinWidthApplied.Value, Width) : Width;
            this.MaxWidthApplied = this.MaxWidthApplied.HasValue ? Math.Max(this.MaxWidthApplied.Value, Width) : Width;
        }
    }
}
