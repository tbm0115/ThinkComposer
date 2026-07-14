// ------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Copyright (C) 2011-2015 Néstor Marcel Sánchez Ahumada.
// https://github.com/nmarcel/ThinkComposer
//
// This file is part of ThinkComposer, which is free software licensed under the GNU General Public License.
// It is provided without any warranty. You should find a copy of the license in the root directory of this software product.
// -------------------------------------------------------------------------------------------
//
// Project: Instrumind ThinkComposer v1.0
// File   : ConsoleRollPublisher.cs
// Object : Instrumind.Common.Visualization.ConsoleRollPublisher (Class)
//
// Date       Author             Changes
// ---------- ------------------ -------------------------------------------------------------
// 2009.07.08 Néstor Sánchez A.  Creation
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using Instrumind.Common;

/// Specialized WPF components and features across Instrumind products.
namespace Instrumind.Common.Visualization
{
    /// <summary>
    /// Publishes text, to be continously supplied either directly or as a redirect of the standard console output, into a ListBox.
    /// </summary>
    public class ConsoleRollPublisher : CentralizedTextWriter
    {
        private ObservableCollection<TextLine> TextRollList = new ObservableCollection<TextLine>();
        private TextLine CurrentLine = null;
        private int CurrentIndex = -1;
        private bool LastCompletedLineWasWhitespaceOnly = false;

        private readonly object PendingWritesLock = new object();
        private readonly Queue<PendingWrite> PendingWrites = new Queue<PendingWrite>();
        private readonly DispatcherTimer PendingWritesDrainTimer;
        private bool PendingClear = false;
        private bool PendingWritesDrainActive = false;

        private static readonly TimeSpan PendingWritesDrainInterval = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// Default maximum lines for console output.
        /// </summary>
        public const int MAX_CONSOLE_OUTPUT_LINES = 1024;

        /// <summary>
        /// Listbox that presentes the published text roll.
        /// </summary>
        public ListBox TargetPresenter { get; protected set; }

        /// <summary>
        /// Maximum number of console output lines to be shown.
        /// </summary>
        public int MaxLines { get; protected set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="TargetPresenter"></param>
        public ConsoleRollPublisher(ListBox TargetPresenter, int MaxLines = MAX_CONSOLE_OUTPUT_LINES)
        {
            General.ContractRequiresNotNull(TargetPresenter);

            this.TargetPresenter = TargetPresenter;
            this.MaxLines = Math.Min(MAX_CONSOLE_OUTPUT_LINES, Math.Max(1, MaxLines));

            this.TargetPresenter.Items.Clear();
            this.TargetPresenter.ItemsSource = TextRollList;

            this.PendingWritesDrainTimer = new DispatcherTimer(DispatcherPriority.Background,
                                                               this.TargetPresenter.Dispatcher);
            this.PendingWritesDrainTimer.Interval = PendingWritesDrainInterval;
            this.PendingWritesDrainTimer.Tick += this.PendingWritesDrainTimer_Tick;
        }

        public void Clear()
        {
            bool ActivateDrain;

            lock (this.PendingWritesLock)
            {
                this.PendingWrites.Clear();
                this.PendingClear = true;
                ActivateDrain = !this.PendingWritesDrainActive;
                this.PendingWritesDrainActive = true;
            }

            if (ActivateDrain)
                this.ActivatePendingWritesDrain();
        }

        protected override void ApplyWrite(string Value, bool AddNewLine = false)
        {
            bool ActivateDrain;

            lock (this.PendingWritesLock)
            {
                var PendingCapacity = Math.Max(1, this.MaxLines);

                // Console output is intentionally lossy once it exceeds the visible roll.  Keeping the
                // newest writes prevents a verbose import from growing memory without bound while the
                // UI dispatcher is busy materializing a document.
                while (this.PendingWrites.Count >= PendingCapacity)
                    this.PendingWrites.Dequeue();

                this.PendingWrites.Enqueue(new PendingWrite(Value, AddNewLine));
                ActivateDrain = !this.PendingWritesDrainActive;
                this.PendingWritesDrainActive = true;
            }

            if (ActivateDrain)
                this.ActivatePendingWritesDrain();
        }

        private void ActivatePendingWritesDrain()
        {
            try
            {
                if (this.TargetPresenter.Dispatcher.HasShutdownStarted ||
                    this.TargetPresenter.Dispatcher.HasShutdownFinished)
                    return;

                this.TargetPresenter.Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        if (!this.PendingWritesDrainTimer.IsEnabled)
                            this.PendingWritesDrainTimer.Start();
                    }), DispatcherPriority.Background);
            }
            catch
            {
                lock (this.PendingWritesLock)
                    this.PendingWritesDrainActive = false;
            }
        }

        private void PendingWritesDrainTimer_Tick(object Sender, EventArgs Args)
        {
            PendingWrite[] Writes;
            bool ClearRequested;

            lock (this.PendingWritesLock)
            {
                Writes = this.PendingWrites.ToArray();
                this.PendingWrites.Clear();
                ClearRequested = this.PendingClear;
                this.PendingClear = false;

                if (this.PendingWrites.Count < 1 && !this.PendingClear)
                {
                    this.PendingWritesDrainActive = false;
                    this.PendingWritesDrainTimer.Stop();
                }
            }

            try
            {
                if (ClearRequested)
                    this.ClearOnDispatcher();

                foreach (var Write in Writes)
                    this.ApplyWriteOnDispatcher(Write.Value, Write.AddNewLine);

                // Scrolling once per drained batch avoids repeated measure/layout work for verbose
                // persistence diagnostics.
                if (this.TextRollList.Count > 0)
                    this.TargetPresenter.ScrollIntoView(this.TextRollList[this.TextRollList.Count - 1]);
            }
            catch (Exception Problem)
            {
                AppExec.LogException(Problem, "ConsoleRollPublisher");
            }
        }

        private void ClearOnDispatcher()
        {
            this.TextRollList.Clear();
            this.CurrentLine = null;
            this.CurrentIndex = -1;
            this.LastCompletedLineWasWhitespaceOnly = false;
        }

        private void ApplyWriteOnDispatcher(string Value, bool AddNewLine)
        {
            if (this.CurrentLine == null && AddNewLine && String.IsNullOrWhiteSpace(Value))
            {
                if (this.LastCompletedLineWasWhitespaceOnly)
                    return;

                Value = String.Empty;
            }

            if (this.CurrentLine == null)
            {
                /* Needed only for Server type software
                var FormattedDateTime = DateTime.Now.AsCommonDateTime(false, true);
                Value = FormattedDateTime + ". " + Value; */
                this.CurrentLine = new TextLine(Value);
                this.AppendLine();
            }
            else
            {
                this.CurrentLine.Extend(Value);

                // Enforces the update and show of the collection item.
                this.TextRollList[CurrentIndex] = DummyTextLine;
                this.TextRollList[CurrentIndex] = this.CurrentLine;
            }

            if (AddNewLine)
            {
                this.LastCompletedLineWasWhitespaceOnly = String.IsNullOrWhiteSpace(this.CurrentLine.ToString());
                this.CurrentLine = null;
            }
        }
        private static TextLine DummyTextLine = new TextLine();

        private void AppendLine()
        {
            var VisibleCapacity = Math.Max(1, this.MaxLines);
            if (this.TextRollList.Count >= VisibleCapacity)
                this.TextRollList.RemoveAt(0);

            this.TextRollList.Add(this.CurrentLine);
            this.CurrentIndex = this.TextRollList.Count - 1;
        }

        private sealed class PendingWrite
        {
            public PendingWrite(string Value, bool AddNewLine)
            {
                this.Value = Value;
                this.AddNewLine = AddNewLine;
            }

            public string Value { get; private set; }
            public bool AddNewLine { get; private set; }
        }

        /// <summary>
        /// Wrapping string container which allow extensions.
        /// </summary>
        public class TextLine
        {
            private string Text = null;

            public TextLine()
            {
            }

            public TextLine(string Text)
            {
                this.Text = Text;
            }

            public override string ToString()
            {
                return (this.Text == null ? String.Empty : this.Text);
            }

            public void Extend(string Text)
            {
                if (this.Text == null)
                    this.Text = Text;
                else
                    this.Text = this.Text + Text;
            }
        }
    }
}
