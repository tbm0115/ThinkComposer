// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Copyright (C) 2011-2015 Nestor Marcel Sanchez Ahumada.
// https://github.com/nmarcel/ThinkComposer
//
// This file is part of ThinkComposer, which is free software licensed under the GNU General Public License.
// It is provided without any warranty. You should find a copy of the license in the root directory of this software product.
// -------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Instrumind.ThinkComposer.ApplicationShell
{
    /// <summary>
    /// Kinds of persistence work which can expose progress to an interactive host.
    /// </summary>
    internal enum PersistenceOperationKind
    {
        OpenComposition,
        OpenDomain,
        SaveComposition,
        SaveDomain,
        Other
    }

    /// <summary>
    /// Stable stage identifiers used by native JSON persistence.  A stage identifier is intended
    /// for diagnostics; the accompanying message is the user-facing text.
    /// </summary>
    internal static class PersistenceOperationStages
    {
        internal const int LoadStageCount = 9;

        internal const string OpenPackage = "open-package";
        internal const string ParseComposition = "parse-composition-json";
        internal const string ParseDomain = "parse-domain-json";
        internal const string RebuildDomain = "rebuild-domain";
        internal const string RebuildConcepts = "rebuild-concepts";
        internal const string RebuildRelationships = "rebuild-relationships";
        internal const string RebuildViews = "rebuild-views";
        internal const string FinalizeModel = "finalize-model";
        internal const string ActivateWorkspace = "activate-workspace";

        internal const string SaveExportDto = "save-export-dto";
        internal const string SaveJsonSerializationHash = "save-json-serialization-hash";
        internal const string SavePreviewCacheRead = "save-preview-cache-read";
        internal const string SavePreviewInputHash = "save-preview-input-hash";
        internal const string SavePreviewRender = "save-preview-render";
        internal const string SavePreviewReuse = "save-preview-reuse";
        internal const string SaveRequiredPackageWrite = "save-required-package-write";
        internal const string SavePackageClose = "save-package-close";
        internal const string SaveSafeReplacement = "save-safe-replacement";
        internal const string SaveOptionalSidecars = "save-optional-sidecars";
    }

    /// <summary>
    /// Immutable progress message.  It deliberately contains no DispatcherObject or other WPF
    /// object so it can safely cross from the blocked application dispatcher to a splash dispatcher.
    /// </summary>
    internal sealed class PersistenceProgressUpdate
    {
        internal PersistenceProgressUpdate(Guid OperationId,
                                           PersistenceOperationKind OperationKind,
                                           string StageId,
                                           int StageIndex,
                                           int StageCount,
                                           string Message,
                                           long? Current,
                                           long? Total,
                                           bool IsIndeterminate,
                                           TimeSpan Elapsed)
        {
            this.OperationId = OperationId;
            this.OperationKind = OperationKind;
            this.StageId = StageId ?? String.Empty;
            this.StageIndex = Math.Max(0, StageIndex);
            this.StageCount = Math.Max(0, StageCount);
            this.Message = Message ?? String.Empty;
            this.Current = Current;
            this.Total = Total;
            this.IsIndeterminate = IsIndeterminate;
            this.Elapsed = Elapsed;
        }

        internal Guid OperationId { get; private set; }
        internal PersistenceOperationKind OperationKind { get; private set; }
        internal string StageId { get; private set; }
        internal int StageIndex { get; private set; }
        internal int StageCount { get; private set; }
        internal string Message { get; private set; }
        internal long? Current { get; private set; }
        internal long? Total { get; private set; }
        internal bool IsIndeterminate { get; private set; }
        internal TimeSpan Elapsed { get; private set; }
    }

    /// <summary>
    /// Receives progress without assuming a SynchronizationContext.  Implementations are responsible
    /// for their own marshalling and throttling.
    /// </summary>
    internal interface IPersistenceProgressSink
    {
        void Report(PersistenceProgressUpdate Update);
    }

    /// <summary>
    /// Timing for one completed persistence stage.
    /// </summary>
    internal sealed class PersistenceStageTiming
    {
        internal PersistenceStageTiming(string StageId, TimeSpan Elapsed)
        {
            this.StageId = StageId ?? String.Empty;
            this.Elapsed = Elapsed;
        }

        internal string StageId { get; private set; }
        internal TimeSpan Elapsed { get; private set; }
    }

    /// <summary>
    /// Immutable responsiveness measurements produced by the dedicated persistence splash
    /// dispatcher.  The result deliberately carries only value types so benchmark/test hosts can
    /// observe it without retaining a Window, Dispatcher or any other thread-affine WPF object.
    /// </summary>
    internal sealed class PersistenceSplashResponsivenessResult
    {
        internal const int RequiredFirstPaintMilliseconds = 250;
        internal const int RequiredHeartbeatGapMilliseconds = 500;

        internal PersistenceSplashResponsivenessResult(Guid OperationId,
                                                        PersistenceOperationKind OperationKind,
                                                        bool DispatcherStarted,
                                                        bool FirstPaintObserved,
                                                        TimeSpan FirstPaintElapsed,
                                                        TimeSpan MaximumHeartbeatGap,
                                                        int HeartbeatCount,
                                                        TimeSpan OperationElapsed,
                                                        bool DispatcherStoppedCleanly)
        {
            this.OperationId = OperationId;
            this.OperationKind = OperationKind;
            this.DispatcherStarted = DispatcherStarted;
            this.FirstPaintObserved = FirstPaintObserved;
            this.FirstPaintElapsed = FirstPaintElapsed;
            this.MaximumHeartbeatGap = MaximumHeartbeatGap;
            this.HeartbeatCount = Math.Max(0, HeartbeatCount);
            this.OperationElapsed = OperationElapsed;
            this.DispatcherStoppedCleanly = DispatcherStoppedCleanly;

            var HeartbeatWasRequired = FirstPaintObserved &&
                                       OperationElapsed - FirstPaintElapsed >=
                                       TimeSpan.FromMilliseconds(RequiredHeartbeatGapMilliseconds);
            this.IsWithinRequiredThresholds = DispatcherStarted &&
                                              FirstPaintObserved &&
                                              DispatcherStoppedCleanly &&
                                              FirstPaintElapsed <= TimeSpan.FromMilliseconds(
                                                  RequiredFirstPaintMilliseconds) &&
                                              (!HeartbeatWasRequired || this.HeartbeatCount > 0) &&
                                              MaximumHeartbeatGap <= TimeSpan.FromMilliseconds(
                                                  RequiredHeartbeatGapMilliseconds);
        }

        internal Guid OperationId { get; private set; }
        internal PersistenceOperationKind OperationKind { get; private set; }
        internal bool DispatcherStarted { get; private set; }
        internal bool FirstPaintObserved { get; private set; }
        internal TimeSpan FirstPaintElapsed { get; private set; }
        internal TimeSpan MaximumHeartbeatGap { get; private set; }
        internal int HeartbeatCount { get; private set; }
        internal TimeSpan OperationElapsed { get; private set; }
        internal bool DispatcherStoppedCleanly { get; private set; }
        internal bool IsWithinRequiredThresholds { get; private set; }
    }

    /// <summary>
    /// UI-agnostic context shared by all parts of one persistence operation.
    /// </summary>
    internal sealed class PersistenceOperationContext
    {
        [ThreadStatic]
        private static PersistenceOperationContext CurrentOnThread;

        private readonly object SyncRoot = new object();
        private readonly Stopwatch OperationWatch = Stopwatch.StartNew();
        private readonly Stopwatch StageWatch = new Stopwatch();
        private readonly IPersistenceProgressSink Sink;
        private readonly List<PersistenceStageTiming> CompletedStageTimings = new List<PersistenceStageTiming>();
        private static readonly TimeSpan ProgressDeliveryInterval = TimeSpan.FromMilliseconds(250);
        private string ActiveStageId;
        private TimeSpan LastItemProgressDelivery;
        private PersistenceSplashResponsivenessResult SplashResponsivenessValue;
        private bool IsCompleted;

        internal PersistenceOperationContext(Guid OperationId,
                                             PersistenceOperationKind OperationKind,
                                             IPersistenceProgressSink Sink)
        {
            this.OperationId = OperationId;
            this.OperationKind = OperationKind;
            this.Sink = Sink ?? NullPersistenceProgressSink.Instance;
        }

        /// <summary>
        /// Context for the synchronous operation on the calling thread.  Native package readers,
        /// serializers, importers and repair passes can use this as an integration hook without
        /// changing their public APIs.
        /// </summary>
        internal static PersistenceOperationContext Current
        {
            get { return CurrentOnThread; }
        }

        internal Guid OperationId { get; private set; }
        internal PersistenceOperationKind OperationKind { get; private set; }

        internal IList<PersistenceStageTiming> StageTimings
        {
            get
            {
                lock (this.SyncRoot)
                    return new List<PersistenceStageTiming>(this.CompletedStageTimings).AsReadOnly();
            }
        }

        internal TimeSpan Elapsed
        {
            get
            {
                lock (this.SyncRoot)
                    return this.OperationWatch.Elapsed;
            }
        }

        internal PersistenceSplashResponsivenessResult SplashResponsiveness
        {
            get
            {
                lock (this.SyncRoot)
                    return this.SplashResponsivenessValue;
            }
        }

        internal void SetSplashResponsiveness(PersistenceSplashResponsivenessResult Result)
        {
            if (Result == null || Result.OperationId != this.OperationId)
                return;

            lock (this.SyncRoot)
                if (this.SplashResponsivenessValue == null)
                    this.SplashResponsivenessValue = Result;
        }

        internal IDisposable MakeCurrent()
        {
            var Previous = CurrentOnThread;
            CurrentOnThread = this;
            return new CurrentContextScope(this, Previous);
        }

        /// <summary>
        /// Starts a low-overhead diagnostic span. Unlike ReportStage, diagnostic spans may be
        /// nested, so a broad package-writer timing can contain export, serialization and preview
        /// sub-stages without disturbing user-facing progress delivery.
        /// </summary>
        internal static IDisposable MeasureCurrentStage(string StageId)
        {
            var Context = CurrentOnThread;
            if (Context == null)
                return NullStageMeasurement.Instance;

            return new StageMeasurement(Context, StageId);
        }

        internal void RecordStageTiming(string StageId, TimeSpan Elapsed)
        {
            if (String.IsNullOrWhiteSpace(StageId) || Elapsed < TimeSpan.Zero)
                return;

            lock (this.SyncRoot)
            {
                if (!this.IsCompleted)
                    this.CompletedStageTimings.Add(new PersistenceStageTiming(StageId, Elapsed));
            }
        }

        internal void ReportStage(string StageId,
                                  int StageIndex,
                                  int StageCount,
                                  string Message,
                                  bool IsIndeterminate = true)
        {
            this.Report(StageId, StageIndex, StageCount, Message, null, null, IsIndeterminate);
        }

        internal void ReportItems(string StageId,
                                  int StageIndex,
                                  int StageCount,
                                  string Message,
                                  long Current,
                                  long Total)
        {
            this.Report(StageId, StageIndex, StageCount, Message, Current, Total, Total <= 0);
        }

        internal void Report(string StageId,
                             int StageIndex,
                             int StageCount,
                             string Message,
                             long? Current,
                             long? Total,
                             bool IsIndeterminate)
        {
            PersistenceProgressUpdate Update;

            lock (this.SyncRoot)
            {
                if (this.IsCompleted)
                    return;

                var StageChanged = !String.Equals(this.ActiveStageId, StageId, StringComparison.Ordinal);
                if (StageChanged)
                {
                    this.CompleteActiveStage();
                    this.ActiveStageId = StageId ?? String.Empty;
                    this.StageWatch.Restart();
                }

                // Importers can account for tens of thousands of DTOs. Keep their loops cheap and
                // bound immutable cross-thread progress messages to four per second, while always
                // delivering stage transitions and the final item count.
                var IsIntermediateItemUpdate = Current.HasValue && Total.HasValue &&
                                               Total.Value > 0 && Current.Value < Total.Value;
                if (!StageChanged && IsIntermediateItemUpdate &&
                    this.OperationWatch.Elapsed - this.LastItemProgressDelivery < ProgressDeliveryInterval)
                    return;

                if (Current.HasValue)
                    this.LastItemProgressDelivery = this.OperationWatch.Elapsed;

                Update = new PersistenceProgressUpdate(this.OperationId, this.OperationKind,
                                                       StageId, StageIndex, StageCount, Message,
                                                       Current, Total, IsIndeterminate,
                                                       this.OperationWatch.Elapsed);
            }

            try
            {
                this.Sink.Report(Update);
            }
            catch
            {
                // Progress is strictly observational and must never make persistence fail.
            }
        }

        internal void Complete()
        {
            lock (this.SyncRoot)
            {
                if (this.IsCompleted)
                    return;

                this.CompleteActiveStage();
                this.IsCompleted = true;
                this.OperationWatch.Stop();
            }
        }

        private void CompleteActiveStage()
        {
            if (this.ActiveStageId == null || !this.StageWatch.IsRunning)
                return;

            this.StageWatch.Stop();
            this.CompletedStageTimings.Add(new PersistenceStageTiming(this.ActiveStageId,
                                                                      this.StageWatch.Elapsed));
            this.StageWatch.Reset();
        }

        private sealed class CurrentContextScope : IDisposable
        {
            private readonly PersistenceOperationContext Expected;
            private readonly PersistenceOperationContext Previous;
            private bool IsDisposed;

            internal CurrentContextScope(PersistenceOperationContext Expected,
                                         PersistenceOperationContext Previous)
            {
                this.Expected = Expected;
                this.Previous = Previous;
            }

            public void Dispose()
            {
                if (this.IsDisposed)
                    return;

                this.IsDisposed = true;
                if (Object.ReferenceEquals(CurrentOnThread, this.Expected))
                    CurrentOnThread = this.Previous;
            }
        }

        private sealed class StageMeasurement : IDisposable
        {
            private readonly PersistenceOperationContext Context;
            private readonly string StageId;
            private readonly Stopwatch Watch;
            private bool IsDisposed;

            internal StageMeasurement(PersistenceOperationContext Context, string StageId)
            {
                this.Context = Context;
                this.StageId = StageId;
                this.Watch = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                if (this.IsDisposed)
                    return;

                this.IsDisposed = true;
                this.Watch.Stop();
                this.Context.RecordStageTiming(this.StageId, this.Watch.Elapsed);
            }
        }

        private sealed class NullStageMeasurement : IDisposable
        {
            internal static readonly NullStageMeasurement Instance = new NullStageMeasurement();

            public void Dispose()
            {
            }
        }

        private sealed class NullPersistenceProgressSink : IPersistenceProgressSink
        {
            internal static readonly NullPersistenceProgressSink Instance = new NullPersistenceProgressSink();

            public void Report(PersistenceProgressUpdate Update)
            {
            }
        }
    }
}
