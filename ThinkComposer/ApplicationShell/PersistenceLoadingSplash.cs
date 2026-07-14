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
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Instrumind.ThinkComposer.ApplicationShell
{
    /// <summary>
    /// Owns one responsive loading splash. Nested synchronous opens on the same application thread
    /// share the current context and splash and only the outermost scope closes it.
    /// </summary>
    internal static class PersistenceLoadingSplash
    {
        [ThreadStatic]
        private static LoadingSession CurrentSession;

        /// <summary>
        /// Raised after a splash dispatcher has stopped and its immutable telemetry is complete.
        /// This is an internal benchmark/test hook; persistence never depends on subscribers.
        /// </summary>
        internal static event Action<PersistenceSplashResponsivenessResult> ResponsivenessMeasured;

        internal static PersistenceLoadingScope Begin(PersistenceOperationKind OperationKind,
                                                      string InitialMessage,
                                                      Window OwnerWindow)
        {
            if (CurrentSession != null)
            {
                CurrentSession.Depth++;
                CurrentSession.Context.ReportStage(PersistenceOperationStages.OpenPackage, 1,
                                                   PersistenceOperationStages.LoadStageCount,
                                                   InitialMessage, true);
                return new PersistenceLoadingScope(CurrentSession);
            }

            var OperationId = Guid.NewGuid();
            LoadingSplashProgressSink Sink = null;

            // A missing Application indicates a headless host. Progress remains available through
            // the context, but no second WPF dispatcher is created.
            if (Application.Current != null)
                try
                {
                    Sink = new LoadingSplashProgressSink(OperationId,
                                                         OperationKind,
                                                         InitialMessage,
                                                         SplashPlacement.Capture(OwnerWindow));
                }
                catch
                {
                    // Thread creation, resource loading and first-paint synchronization are all
                    // best-effort. A wait cursor and the synchronous load remain available.
                    Sink = null;
                }

            var Context = new PersistenceOperationContext(OperationId, OperationKind, Sink);
            var ContextScope = Context.MakeCurrent();
            var Session = new LoadingSession(Context, ContextScope, Sink);
            CurrentSession = Session;

            Context.ReportStage(PersistenceOperationStages.OpenPackage, 1,
                                PersistenceOperationStages.LoadStageCount,
                                InitialMessage, true);

            return new PersistenceLoadingScope(Session);
        }

        private static PersistenceSplashResponsivenessResult End(LoadingSession Session)
        {
            if (Session == null || Session.Depth < 1)
                return null;

            Session.Depth--;
            if (Session.Depth > 0)
                return null;

            if (Object.ReferenceEquals(CurrentSession, Session))
                CurrentSession = null;

            Session.Context.Complete();

            PersistenceSplashResponsivenessResult Responsiveness = null;
            try
            {
                if (Session.Sink != null)
                    try
                    {
                        Responsiveness = Session.Sink.Close();
                        Session.Context.SetSplashResponsiveness(Responsiveness);
                        PublishResponsiveness(Responsiveness);
                    }
                    catch
                    {
                        // Closing/telemetry is observational and must never mask the load result.
                    }

                try
                {
                    var StageSummary = String.Join(", ", Session.Context.StageTimings
                        .Select(Timing => Timing.StageId + "=" +
                                          Timing.Elapsed.TotalMilliseconds.ToString("0.###",
                                              System.Globalization.CultureInfo.InvariantCulture) + "ms")
                        .ToArray());
                    Console.WriteLine("JSON persistence timing summary: operation={0}, total={1:0.###}ms, stages=[{2}].",
                                      Session.Context.OperationKind,
                                      Session.Context.Elapsed.TotalMilliseconds,
                                      StageSummary);
                }
                catch
                {
                    // Timing diagnostics are observational and never affect loading.
                }
            }
            finally
            {
                Session.ContextScope.Dispose();
            }

            return Responsiveness;
        }

        /// <summary>
        /// Exercises the real Window and dedicated STA dispatcher without requiring an Application
        /// instance. It is intentionally internal so a CLI/test host can enforce the responsiveness
        /// targets without extending the application API.
        /// </summary>
        internal static PersistenceSplashResponsivenessResult RunDedicatedStaSmokeTest(TimeSpan Duration)
        {
            if (Duration < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException("Duration");

            var OperationId = Guid.NewGuid();
            var Sink = new LoadingSplashProgressSink(OperationId,
                                                     PersistenceOperationKind.Other,
                                                     "Testing loading splash responsiveness...",
                                                     new SplashPlacement());
            Sink.Report(new PersistenceProgressUpdate(OperationId,
                                                      PersistenceOperationKind.Other,
                                                      PersistenceOperationStages.ParseComposition,
                                                      2,
                                                      PersistenceOperationStages.LoadStageCount,
                                                      "Testing loading splash responsiveness...",
                                                      null,
                                                      null,
                                                      true,
                                                      TimeSpan.Zero));

            // Deliberately leave the caller unable to pump a dispatcher. The splash must continue
            // to paint and heartbeat solely through its own STA thread.
            if (Duration > TimeSpan.Zero)
                Thread.Sleep(Duration);

            var Result = Sink.Close();
            PublishResponsiveness(Result);
            return Result;
        }

        private static void PublishResponsiveness(PersistenceSplashResponsivenessResult Result)
        {
            if (Result == null)
                return;

            try
            {
                Console.WriteLine(
                    "Persistence splash responsiveness: operationId={0}, operation={1}, " +
                    "firstPaintObserved={2}, firstPaintMs={3:0.###}, maxHeartbeatGapMs={4:0.###}, " +
                    "heartbeats={5}, durationMs={6:0.###}, dispatcherStopped={7}, thresholdsMet={8}.",
                    Result.OperationId,
                    Result.OperationKind,
                    Result.FirstPaintObserved,
                    Result.FirstPaintElapsed.TotalMilliseconds,
                    Result.MaximumHeartbeatGap.TotalMilliseconds,
                    Result.HeartbeatCount,
                    Result.OperationElapsed.TotalMilliseconds,
                    Result.DispatcherStoppedCleanly,
                    Result.IsWithinRequiredThresholds);
            }
            catch
            {
                // Telemetry is observational and must never make persistence fail.
            }

            var Handlers = ResponsivenessMeasured;
            if (Handlers == null)
                return;

            foreach (Action<PersistenceSplashResponsivenessResult> Handler in Handlers.GetInvocationList())
                try
                {
                    Handler(Result);
                }
                catch
                {
                    // One diagnostic subscriber must not prevent later subscribers or persistence.
                }
        }

        internal sealed class PersistenceLoadingScope : IDisposable
        {
            private LoadingSession Session;
            private readonly PersistenceOperationContext OperationContext;

            internal PersistenceLoadingScope(LoadingSession Session)
            {
                this.Session = Session;
                this.OperationContext = Session == null ? null : Session.Context;
            }

            internal PersistenceOperationContext Context
            {
                get { return this.OperationContext; }
            }

            internal PersistenceSplashResponsivenessResult ResponsivenessResult
            {
                get { return this.OperationContext == null ? null : this.OperationContext.SplashResponsiveness; }
            }

            public void Dispose()
            {
                var SessionToClose = this.Session;
                if (SessionToClose == null)
                    return;

                this.Session = null;
                End(SessionToClose);
            }
        }

        internal sealed class LoadingSession
        {
            internal LoadingSession(PersistenceOperationContext Context,
                                    IDisposable ContextScope,
                                    LoadingSplashProgressSink Sink)
            {
                this.Context = Context;
                this.ContextScope = ContextScope;
                this.Sink = Sink;
                this.Depth = 1;
            }

            internal readonly PersistenceOperationContext Context;
            internal readonly IDisposable ContextScope;
            internal readonly LoadingSplashProgressSink Sink;
            internal int Depth;
        }

        internal sealed class LoadingSplashProgressSink : IPersistenceProgressSink
        {
            private static readonly TimeSpan DeliveryInterval = TimeSpan.FromMilliseconds(250);

            private readonly object UpdateLock = new object();
            private readonly object TelemetryLock = new object();
            private readonly Guid OperationId;
            private readonly PersistenceOperationKind OperationKind;
            private readonly string InitialMessage;
            private readonly SplashPlacement Placement;
            private readonly ManualResetEventSlim FirstPaint = new ManualResetEventSlim(false);
            private readonly ManualResetEventSlim DispatcherStopped = new ManualResetEventSlim(false);
            private readonly Stopwatch ElapsedWatch = Stopwatch.StartNew();
            private readonly Thread SplashThread;

            private System.Threading.Timer DeliveryTimer;
            private Dispatcher SplashDispatcher;
            private LoadingSplashWindow SplashWindow;
            private PersistenceProgressUpdate LatestUpdate;
            private long LatestVersion;
            private long DeliveredVersion;
            private bool DeliveryScheduled;
            private DateTime LastDeliveryUtc = DateTime.MinValue;
            private int IsClosed;
            private bool DispatcherStarted;
            private bool FirstPaintObserved;
            private TimeSpan FirstPaintElapsed;
            private TimeSpan LastHeartbeatElapsed;
            private TimeSpan MaximumHeartbeatGap;
            private int HeartbeatCount;

            internal LoadingSplashProgressSink(Guid OperationId,
                                               PersistenceOperationKind OperationKind,
                                               string InitialMessage,
                                               SplashPlacement Placement)
            {
                this.OperationId = OperationId;
                this.OperationKind = OperationKind;
                this.InitialMessage = InitialMessage ?? "Opening document...";
                this.Placement = Placement;

                this.SplashThread = new Thread(this.RunSplashDispatcher);
                this.SplashThread.Name = "ThinkComposer persistence loading splash";
                this.SplashThread.IsBackground = true;
                this.SplashThread.SetApartmentState(ApartmentState.STA);
                this.SplashThread.Start();

                // Give the independent dispatcher a bounded opportunity to paint before the main
                // dispatcher enters a CPU-heavy JSON parse. Failure or timeout never blocks opening.
                this.FirstPaint.Wait(250);
            }

            internal TimeSpan Elapsed
            {
                get { return this.ElapsedWatch.Elapsed; }
            }

            public void Report(PersistenceProgressUpdate Update)
            {
                if (Update == null || Update.OperationId != this.OperationId ||
                    Volatile.Read(ref this.IsClosed) != 0)
                    return;

                lock (this.UpdateLock)
                {
                    this.LatestUpdate = Update;
                    this.LatestVersion++;
                    this.ScheduleDeliveryLocked();
                }
            }

            internal PersistenceSplashResponsivenessResult Close()
            {
                if (Interlocked.Exchange(ref this.IsClosed, 1) != 0)
                    return this.CreateResponsivenessResult(this.DispatcherStopped.IsSet);

                this.ElapsedWatch.Stop();
                this.FirstPaint.Set();

                lock (this.UpdateLock)
                {
                    if (this.DeliveryTimer != null)
                    {
                        this.DeliveryTimer.Dispose();
                        this.DeliveryTimer = null;
                    }

                    this.DeliveryScheduled = false;
                }

                var Dispatcher = this.SplashDispatcher;
                bool StoppedCleanly;
                if (Dispatcher == null || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                {
                    StoppedCleanly = this.DispatcherStopped.Wait(TimeSpan.FromSeconds(1));
                    return this.CreateResponsivenessResult(StoppedCleanly);
                }

                try
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (this.SplashWindow != null)
                            this.SplashWindow.Close();

                        Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    }), DispatcherPriority.Send);
                }
                catch
                {
                    // The loading result must not depend on a diagnostic window closing cleanly.
                }

                // Ensure the splash is gone before existing failure diagnostics or modal Domain
                // editing can open. This waits only on the independent splash dispatcher.
                StoppedCleanly = this.DispatcherStopped.Wait(TimeSpan.FromSeconds(2));
                return this.CreateResponsivenessResult(StoppedCleanly);
            }

            private void RecordFirstPaint()
            {
                lock (this.TelemetryLock)
                {
                    if (!this.FirstPaintObserved)
                    {
                        this.FirstPaintObserved = true;
                        this.FirstPaintElapsed = this.ElapsedWatch.Elapsed;
                        this.LastHeartbeatElapsed = this.FirstPaintElapsed;
                    }
                }

                this.FirstPaint.Set();
            }

            private void RecordHeartbeat()
            {
                if (Volatile.Read(ref this.IsClosed) != 0)
                    return;

                lock (this.TelemetryLock)
                {
                    var Now = this.ElapsedWatch.Elapsed;
                    if (this.FirstPaintObserved)
                    {
                        var Gap = Now - this.LastHeartbeatElapsed;
                        if (Gap > this.MaximumHeartbeatGap)
                            this.MaximumHeartbeatGap = Gap;
                    }

                    this.LastHeartbeatElapsed = Now;
                    this.HeartbeatCount++;
                }
            }

            private PersistenceSplashResponsivenessResult CreateResponsivenessResult(bool StoppedCleanly)
            {
                lock (this.TelemetryLock)
                {
                    var Duration = this.ElapsedWatch.Elapsed;

                    // Include the interval between the final heartbeat and operation completion.
                    // Otherwise a dispatcher which stalls just before Close could report a
                    // deceptively small maximum gap.
                    if (this.FirstPaintObserved)
                    {
                        var TailStart = this.HeartbeatCount > 0
                                      ? this.LastHeartbeatElapsed
                                      : this.FirstPaintElapsed;
                        var Tail = Duration - TailStart;
                        if (Tail > this.MaximumHeartbeatGap)
                            this.MaximumHeartbeatGap = Tail;
                    }

                    return new PersistenceSplashResponsivenessResult(
                        this.OperationId,
                        this.OperationKind,
                        this.DispatcherStarted,
                        this.FirstPaintObserved,
                        this.FirstPaintElapsed,
                        this.MaximumHeartbeatGap,
                        this.HeartbeatCount,
                        Duration,
                        StoppedCleanly);
                }
            }

            private void ScheduleDeliveryLocked()
            {
                if (this.DeliveryScheduled || Volatile.Read(ref this.IsClosed) != 0)
                    return;

                var Delay = TimeSpan.Zero;
                if (this.LastDeliveryUtc != DateTime.MinValue)
                {
                    Delay = DeliveryInterval - (DateTime.UtcNow - this.LastDeliveryUtc);
                    if (Delay < TimeSpan.Zero)
                        Delay = TimeSpan.Zero;
                }

                this.DeliveryScheduled = true;
                if (this.DeliveryTimer == null)
                    this.DeliveryTimer = new System.Threading.Timer(this.DeliveryTimerElapsed, null,
                                                                   Delay, Timeout.InfiniteTimeSpan);
                else
                    this.DeliveryTimer.Change(Delay, Timeout.InfiniteTimeSpan);
            }

            private void DeliveryTimerElapsed(object State)
            {
                PersistenceProgressUpdate Update;
                long Version;
                var Dispatcher = this.SplashDispatcher;

                lock (this.UpdateLock)
                {
                    if (Volatile.Read(ref this.IsClosed) != 0)
                        return;

                    if (Dispatcher == null || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                    {
                        this.DeliveryScheduled = false;
                        if (this.DeliveryTimer != null)
                            this.DeliveryTimer.Change(25, Timeout.Infinite);
                        this.DeliveryScheduled = true;
                        return;
                    }

                    Update = this.LatestUpdate;
                    Version = this.LatestVersion;
                }

                try
                {
                    Dispatcher.BeginInvoke(new Action(() => this.DeliverUpdate(Update, Version)),
                                           DispatcherPriority.Background);
                }
                catch
                {
                    lock (this.UpdateLock)
                        this.DeliveryScheduled = false;
                }
            }

            private void DeliverUpdate(PersistenceProgressUpdate Update, long Version)
            {
                if (Volatile.Read(ref this.IsClosed) != 0)
                    return;

                if (Update != null && Update.OperationId == this.OperationId && this.SplashWindow != null)
                    this.SplashWindow.Apply(Update);

                lock (this.UpdateLock)
                {
                    this.DeliveredVersion = Math.Max(this.DeliveredVersion, Version);
                    this.LastDeliveryUtc = DateTime.UtcNow;
                    this.DeliveryScheduled = false;

                    if (this.LatestVersion > this.DeliveredVersion)
                        this.ScheduleDeliveryLocked();
                }
            }

            private void RunSplashDispatcher()
            {
                try
                {
                    this.SplashDispatcher = Dispatcher.CurrentDispatcher;
                    lock (this.TelemetryLock)
                        this.DispatcherStarted = true;

                    if (Volatile.Read(ref this.IsClosed) != 0)
                    {
                        this.FirstPaint.Set();
                        return;
                    }

                    this.SplashWindow = new LoadingSplashWindow(this.InitialMessage, this.Placement,
                                                                () => this.Elapsed,
                                                                this.RecordHeartbeat);
                    this.SplashWindow.ContentRendered += (Sender, Args) => this.RecordFirstPaint();
                    this.SplashWindow.Closed += (Sender, Args) =>
                        Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    this.SplashWindow.Show();

                    lock (this.UpdateLock)
                        if (this.LatestUpdate != null)
                            this.ScheduleDeliveryLocked();

                    Dispatcher.Run();
                }
                catch
                {
                    // Splash creation (including image/resource loading) is best-effort.
                }
                finally
                {
                    this.FirstPaint.Set();
                    this.DispatcherStopped.Set();
                }
            }
        }

        private sealed class LoadingSplashWindow : Window
        {
            private readonly TextBlock StatusText;
            private readonly TextBlock StageText;
            private readonly TextBlock ElapsedText;
            private readonly Border ProgressTrack;
            private readonly Border ProgressFill;
            private readonly Func<TimeSpan> GetElapsed;
            private readonly Action RecordHeartbeat;
            private readonly DispatcherTimer HeartbeatTimer;
            private bool IsIndeterminate = true;
            private double ProgressRatio;
            private double IndeterminateRatio;

            internal LoadingSplashWindow(string InitialMessage,
                                         SplashPlacement Placement,
                                         Func<TimeSpan> GetElapsed,
                                         Action RecordHeartbeat)
            {
                this.GetElapsed = GetElapsed;
                this.RecordHeartbeat = RecordHeartbeat;

                this.Title = "ThinkComposer - Opening document";
                this.Width = 599;
                this.Height = 350;
                this.ResizeMode = ResizeMode.NoResize;
                this.WindowStyle = WindowStyle.None;
                this.ShowInTaskbar = false;
                this.ShowActivated = false;
                this.Background = new SolidColorBrush(Color.FromRgb(27, 31, 38));
                this.BorderBrush = new SolidColorBrush(Color.FromRgb(82, 91, 106));
                this.BorderThickness = new Thickness(1);
                this.SnapsToDevicePixels = true;
                this.Topmost = Placement.OwnerHandle == IntPtr.Zero;

                if (Placement.HasOwnerBounds)
                {
                    this.WindowStartupLocation = WindowStartupLocation.Manual;
                    this.Left = Placement.Left + Math.Max(0, (Placement.Width - this.Width) / 2.0);
                    this.Top = Placement.Top + Math.Max(0, (Placement.Height - this.Height) / 2.0);
                }
                else
                    this.WindowStartupLocation = WindowStartupLocation.CenterScreen;

                if (Placement.OwnerHandle != IntPtr.Zero)
                    this.SourceInitialized += (Sender, Args) =>
                        new WindowInteropHelper(this).Owner = Placement.OwnerHandle;

                var Root = new Grid();
                Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(245) });
                Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var Artwork = this.CreateArtwork();
                Grid.SetRow(Artwork, 0);
                Root.Children.Add(Artwork);

                var Details = new Grid { Margin = new Thickness(22, 14, 22, 14) };
                Details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                this.StatusText = new TextBlock
                {
                    Text = InitialMessage,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(242, 244, 247)),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetRow(this.StatusText, 0);
                Details.Children.Add(this.StatusText);

                var Secondary = new Grid { Margin = new Thickness(0, 5, 0, 9) };
                Secondary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Secondary.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                this.StageText = new TextBlock
                {
                    Text = "Preparing...",
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(181, 189, 201))
                };
                Secondary.Children.Add(this.StageText);

                this.ElapsedText = new TextBlock
                {
                    Text = "Elapsed 00:00",
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(181, 189, 201))
                };
                Grid.SetColumn(this.ElapsedText, 1);
                Secondary.Children.Add(this.ElapsedText);
                Grid.SetRow(Secondary, 1);
                Details.Children.Add(Secondary);

                var ProgressHost = new Grid { Height = 8, ClipToBounds = true };
                this.ProgressTrack = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(52, 59, 69)),
                    CornerRadius = new CornerRadius(4)
                };
                this.ProgressFill = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(74, 163, 255)),
                    CornerRadius = new CornerRadius(4),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                ProgressHost.Children.Add(this.ProgressTrack);
                ProgressHost.Children.Add(this.ProgressFill);
                Grid.SetRow(ProgressHost, 2);
                Details.Children.Add(ProgressHost);

                Grid.SetRow(Details, 1);
                Root.Children.Add(Details);
                this.Content = Root;

                this.HeartbeatTimer = new DispatcherTimer(DispatcherPriority.Background,
                                                          this.Dispatcher);
                this.HeartbeatTimer.Interval = TimeSpan.FromMilliseconds(250);
                this.HeartbeatTimer.Tick += this.HeartbeatTimer_Tick;
                this.HeartbeatTimer.Start();
            }

            internal void Apply(PersistenceProgressUpdate Update)
            {
                this.StatusText.Text = String.IsNullOrWhiteSpace(Update.Message)
                                       ? "Opening document..."
                                       : Update.Message;

                var StageDescription = Update.StageCount > 0
                                     ? String.Format("Stage {0} of {1}", Update.StageIndex, Update.StageCount)
                                     : Update.StageId;

                if (Update.Current.HasValue && Update.Total.HasValue && Update.Total.Value > 0)
                    StageDescription += String.Format("   {0:N0} of {1:N0}",
                                                      Math.Max(0, Update.Current.Value),
                                                      Update.Total.Value);

                this.StageText.Text = StageDescription;
                this.IsIndeterminate = Update.IsIndeterminate ||
                                       !Update.Current.HasValue ||
                                       !Update.Total.HasValue ||
                                       Update.Total.Value <= 0;

                if (!this.IsIndeterminate)
                    this.ProgressRatio = Math.Max(0.0, Math.Min(1.0,
                        (double)Update.Current.Value / (double)Update.Total.Value));

                this.RefreshProgressVisual();
            }

            private FrameworkElement CreateArtwork()
            {
                try
                {
                    var ImageSource = new BitmapImage();
                    ImageSource.BeginInit();
                    ImageSource.CacheOption = BitmapCacheOption.OnLoad;
                    ImageSource.UriSource = new Uri(
                        "pack://application:,,,/Instrumind.ThinkComposer;component/ApplicationShell/Images/Instrumind_ThinkComposer_Splash.png",
                        UriKind.Absolute);
                    ImageSource.EndInit();

                    return new Image
                    {
                        Source = ImageSource,
                        Stretch = Stretch.UniformToFill,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch
                    };
                }
                catch
                {
                    return new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(40, 47, 58)),
                        Child = new TextBlock
                        {
                            Text = "ThinkComposer",
                            FontFamily = new FontFamily("Segoe UI"),
                            FontSize = 34,
                            FontWeight = FontWeights.Light,
                            Foreground = new SolidColorBrush(Color.FromRgb(238, 241, 245)),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };
                }
            }

            private void HeartbeatTimer_Tick(object Sender, EventArgs Args)
            {
                if (this.RecordHeartbeat != null)
                    try
                    {
                        this.RecordHeartbeat();
                    }
                    catch
                    {
                        // Heartbeat telemetry must never interrupt splash animation.
                    }

                var Elapsed = this.GetElapsed == null ? TimeSpan.Zero : this.GetElapsed();
                this.ElapsedText.Text = Elapsed.TotalHours >= 1.0
                                      ? String.Format("Elapsed {0:00}:{1:00}:{2:00}",
                                                      (int)Elapsed.TotalHours, Elapsed.Minutes, Elapsed.Seconds)
                                      : String.Format("Elapsed {0:00}:{1:00}",
                                                      (int)Elapsed.TotalMinutes, Elapsed.Seconds);

                if (this.IsIndeterminate)
                {
                    this.IndeterminateRatio += 0.13;
                    if (this.IndeterminateRatio > 1.0)
                        this.IndeterminateRatio = -0.28;
                }

                this.RefreshProgressVisual();
            }

            private void RefreshProgressVisual()
            {
                var Width = this.ProgressTrack.ActualWidth;
                if (Width <= 0)
                    return;

                if (this.IsIndeterminate)
                {
                    this.ProgressFill.Width = Width * 0.28;
                    this.ProgressFill.Margin = new Thickness(Width * this.IndeterminateRatio, 0, 0, 0);
                }
                else
                {
                    this.ProgressFill.Width = Width * this.ProgressRatio;
                    this.ProgressFill.Margin = new Thickness(0);
                }
            }
        }

        internal struct SplashPlacement
        {
            internal IntPtr OwnerHandle;
            internal bool HasOwnerBounds;
            internal double Left;
            internal double Top;
            internal double Width;
            internal double Height;

            internal static SplashPlacement Capture(Window OwnerWindow)
            {
                var Result = new SplashPlacement();
                if (OwnerWindow == null)
                    return Result;

                try
                {
                    Result.OwnerHandle = new WindowInteropHelper(OwnerWindow).Handle;
                    Result.Left = OwnerWindow.Left;
                    Result.Top = OwnerWindow.Top;
                    Result.Width = OwnerWindow.ActualWidth > 0 ? OwnerWindow.ActualWidth : OwnerWindow.Width;
                    Result.Height = OwnerWindow.ActualHeight > 0 ? OwnerWindow.ActualHeight : OwnerWindow.Height;
                    Result.HasOwnerBounds = OwnerWindow.IsVisible &&
                                            !Double.IsNaN(Result.Left) && !Double.IsInfinity(Result.Left) &&
                                            !Double.IsNaN(Result.Top) && !Double.IsInfinity(Result.Top) &&
                                            Result.Width > 0 && Result.Height > 0;
                }
                catch
                {
                    Result = new SplashPlacement();
                }

                return Result;
            }
        }
    }
}
