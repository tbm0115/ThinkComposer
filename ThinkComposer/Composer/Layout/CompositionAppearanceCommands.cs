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

using Instrumind.Common;
using Instrumind.Common.Visualization;
using Instrumind.ThinkComposer.Model.VisualModel;

namespace Instrumind.ThinkComposer.Composer.Layout
{
    /// <summary>
    /// UI command facade for appearance/layout services.
    /// </summary>
    public static class CompositionAppearanceCommands
    {
        public static bool CanFitConceptWidthToText(CompositionEngine Engine)
        {
            return ConceptAutoFitService.CanFitSelection(Engine);
        }

        public static void FitConceptWidthToText(CompositionEngine Engine)
        {
            try
            {
                Console.WriteLine("Appearance command: Fit Concept Width to Text requested from menu.");
                ConceptAutoFitService.FitSelectedConceptWidths(Engine);
            }
            catch (Exception Problem)
            {
                Console.WriteLine("Appearance command failed: Fit Concept Width to Text. Problem: {0}", Problem.Message);
                Console.WriteLine(Problem.ToString());
                Display.DialogMessage("Fit Concept Width to Text",
                                      "Cannot fit concept width to text.\n\nProblem: " + Problem.Message,
                                      EMessageType.Error);
            }
        }

        public static void FitConceptWidthToText(CompositionEngine Engine, VisualSymbol Symbol, string Source)
        {
            try
            {
                Console.WriteLine("Appearance command: Fit Concept Width to Text requested from {0}.", Source ?? "visual gesture");
                ConceptAutoFitService.FitSingleConceptWidth(Engine, Symbol, Source);
            }
            catch (Exception Problem)
            {
                Console.WriteLine("Appearance command failed: Fit Concept Width to Text from {0}. Problem: {1}",
                                  Source ?? "visual gesture", Problem.Message);
                Console.WriteLine(Problem.ToString());
                Display.DialogMessage("Fit Concept Width to Text",
                                      "Cannot fit concept width to text.\n\nProblem: " + Problem.Message,
                                      EMessageType.Error);
            }
        }

        public static bool IsFutureAppearanceToolEnabled(CompositionEngine Engine)
        {
            return false;
        }

        public static void ShowFutureAppearanceToolMessage(string CommandName)
        {
            Console.WriteLine("Appearance command planned but not implemented yet: {0}.", CommandName);
            Display.DialogMessage("Appearance", CommandName + " is planned, but not implemented yet.", EMessageType.Information);
        }
    }
}
