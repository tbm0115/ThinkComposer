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

namespace Instrumind.ThinkComposer.Composer.Layout
{
    /// <summary>
    /// Summary of visible relationship central-symbol decluttering.
    /// </summary>
    public class RelationshipNodeDeclutterResult
    {
        public RelationshipNodeDeclutterResult()
        {
            this.Warnings = new List<string>();
        }

        public int RelationshipSymbolsInspected { get; set; }

        public int RelationshipSymbolsMoved { get; set; }

        public int RelationshipSymbolsSkipped { get; set; }

        public int OverlapGroupsDetected { get; set; }

        public int InitialOverlapCount { get; set; }

        public int GlobalDeclutterPasses { get; set; }

        public int GlobalDeclutterMoves { get; set; }

        public int FinalOverlapCount { get; set; }

        public int FinalConceptOverlapCount { get; set; }

        public int CorridorViolations { get; set; }

        public int CorridorCorrections { get; set; }

        public IList<string> Warnings { get; private set; }

        public bool HasMutations
        {
            get { return this.RelationshipSymbolsMoved > 0; }
        }

        public void AddWarning(string Warning)
        {
            if (!String.IsNullOrWhiteSpace(Warning))
                this.Warnings.Add(Warning);
        }
    }
}
