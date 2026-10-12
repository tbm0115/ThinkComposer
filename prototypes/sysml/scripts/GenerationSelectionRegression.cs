// Render the real generation selector in a hidden HWND; never generate files or save the input.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Instrumind.Common;
using Instrumind.Common.Visualization;
using Instrumind.ThinkComposer.Composer;
using Instrumind.ThinkComposer.Composer.Generation.Widgets;
using Instrumind.ThinkComposer.Headless;
using Instrumind.ThinkComposer.MetaModel.Configurations;
using Instrumind.ThinkComposer.Model.GraphModel;

class GenerationSelectionRegression
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static int Containers(DependencyObject node)
    {
        int count = node is TreeViewItem ? 1 : 0;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            count += Containers(VisualTreeHelper.GetChild(node, i));
        return count;
    }
    static T Find<T>(DependencyObject node) where T : DependencyObject
    {
        var match = node as T; if (match != null) return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        { match = Find<T>(VisualTreeHelper.GetChild(node, i)); if (match != null) return match; }
        return null;
    }
    static void Layout(FrameworkElement editor)
    {
        for (int i = 0; i < 3; i++)
        {
            editor.Measure(new Size(700, 500)); editor.Arrange(new Rect(0, 0, 700, 500)); editor.UpdateLayout();
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }
    }
    static void Click(GeneratorSelectionEditor editor, string handler)
    {
        typeof(GeneratorSelectionEditor).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(editor, new object[] { editor, new RoutedEventArgs() });
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            var context = HeadlessBootstrap.Initialize();
            CompositionEngine.CreateActiveCompositionEngine(context.Compositions, context.Visualizer, false);
            var loaded = CompositionEngine.Materialize(new Uri(Path.GetFullPath(args[0])));
            Require(loaded != null && loaded.Item1 != null, "Model did not load.");
            var composition = loaded.Item1.TargetComposition;
            // Optional broad sibling set exercises viewport recycling without modifying the package.
            if (args.Length > 2 && args[2] == "wide")
                for (int i = 0; i < 2000; i++)
                {
                    var idea = new Concept(composition, composition.CompositeContentDomain.ConceptDefinitions.First(),
                        "Selection regression " + i, "SelectionRegression" + i);
                    idea.AddToComposite(composition);
                    if (i == 0)
                    {
                        var child = new Concept(composition, composition.CompositeContentDomain.ConceptDefinitions.First(),
                            "Nested selection regression", "NestedSelectionRegression");
                        child.AddToComposite(idea);
                    }
                }
            var bindingLog = new StringWriter();
            var listener = new TextWriterTraceListener(bindingLog);
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            var watch = Stopwatch.StartNew();
            var editor = new GeneratorSelectionEditor(composition) { Width = 700, Height = 500 };
            using (var host = new HwndSource(new HwndSourceParameters("Generation selector regression")
                { Width = 700, Height = 500, WindowStyle = unchecked((int)0x80000000) }))
            {
                host.RootVisual = editor;
                Layout(editor);
                watch.Stop();
                var tree = (TreeView)editor.FindName("TvSelection");
                var root = editor.SourceConfiguration.CurrentSelection;
                Require(editor.IsLoaded && root != null && tree.Items.Count == 1, "Selector did not load.");
                var selections = root.GetSelection();
                int initial = Containers(tree);
                Require(root.IsExpanded && selections.Skip(1).All(s => !s.IsExpanded), "Unexpected initial expansion.");
                Require(initial > 1 && initial < 200, "Selector eagerly realized too many rows: " + initial);
                Require(watch.Elapsed.TotalSeconds < 15, "Selector startup exceeded 15 seconds.");
                Console.WriteLine("PASS: selector loaded " + selections.Count + " selections in " + watch.Elapsed.TotalSeconds.ToString("F3")
                    + "s; realized rows=" + initial);

                Click(editor, "BtnUnselectAll_Click");
                Require(selections.All(s => !s.IsSelected), "Unselect all missed collapsed descendants.");
                Click(editor, "BtnSelectAllIdeas_Click");
                Require(selections.All(s => s.IsSelected == s.IsSelectable), "Select all missed collapsed descendants.");
                var branch = selections.First(s => s.HasChildren && s.Parent != null);
                branch.AreChildrenSelected = false;
                Require(branch.GetSelection().Skip(1).All(s => !s.IsSelected), "Subtree deselection failed.");
                branch.AreChildrenSelected = true;
                Require(branch.GetSelection().Skip(1).All(s => s.IsSelected == s.IsSelectable), "Subtree selection failed.");
                branch.IsSelected = false;
                Require(branch.GetSelection().Skip(1).All(s => s.IsSelected == s.IsSelectable), "Single selection propagated.");
                editor.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                Require(ReferenceEquals(root, editor.SourceConfiguration.CurrentSelection) && !branch.IsSelected,
                    "Reload reset pending selections.");
                Console.WriteLine("PASS: bulk, subtree and single selection include collapsed elements; reload preserves choices.");

                var exclusions = selections.Where((s, index) => index % 2 == 0).Select(s => s.SourceIdea.GlobalId.ToString()).ToList();
                var expected = new System.Collections.Generic.HashSet<string>(exclusions);
                watch.Restart();
                var restored = IdeaSelection.CreateSelectionTree(composition, exclusions,
                    idea => Tuple.Create(idea is Concept, idea is Concept, (IRecognizableElement)null));
                watch.Stop();
                Require(restored.GetSelection().All(s => s.IsSelected == (s.IsSelectable && !expected.Contains(s.SourceIdea.GlobalId.ToString()))),
                    "Saved exclusions were not restored.");
                Require(watch.Elapsed.TotalSeconds < 5, "Exclusion restoration exceeded 5 seconds.");
                Console.WriteLine("PASS: restored " + exclusions.Count + " exclusions in " + watch.Elapsed.TotalSeconds.ToString("F3") + "s.");

                // Expand a real branch, scroll to recycle rows, and verify state survives.
                branch.IsExpanded = true;
                Layout(editor);
                var scroll = Find<ScrollViewer>(tree);
                scroll.ScrollToEnd(); Layout(editor);
                Require(Containers(tree) < 200, "Scrolling realized the entire tree.");
                scroll.ScrollToHome(); Layout(editor);
                Require(branch.IsExpanded && !branch.IsSelected, "Recycling lost item state.");
                Require(!bindingLog.ToString().Contains("IdeaSelection"), "Selection binding errors: " + bindingLog);
                Console.WriteLine("PASS: expansion and scrolling preserve state; no IdeaSelection binding errors.");
                var bitmap = new RenderTargetBitmap(700, 500, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(editor);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var output = File.Create(args[1])) encoder.Save(output);
                Console.WriteLine("Other binding diagnostics: " + bindingLog);
            }
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (Application.Current != null) Application.Current.Shutdown(); }
    }
}
