// Inspect actual native representations, not just the presence of JSON visual records.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Instrumind.ThinkComposer.ApplicationProduct.Widgets;
using Instrumind.ThinkComposer.Composer;
using Instrumind.ThinkComposer.Headless;
using Instrumind.ThinkComposer.Model.GraphModel;

class VisualLoadRegression
{
    static IEnumerable<Idea> Descendants(Idea owner)
    {
        foreach (var child in owner.CompositeIdeas)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            var context = HeadlessBootstrap.Initialize();
            CompositionEngine.CreateActiveCompositionEngine(context.Compositions, context.Visualizer, false);
            var loaded = CompositionEngine.Materialize(new Uri(Path.GetFullPath(args[0])));
            if (loaded == null || loaded.Item1 == null) throw new Exception("Native load failed.");
            var composition = loaded.Item1.TargetComposition;
            var view = composition.RootView;
            if (args.Length > 3)
            {
                view = Descendants(composition).SelectMany(i => i.CompositeViews)
                    .First(v => v.GlobalId.ToString() == args[3]);
                view.Initialize();
            }
            var representations = Descendants(composition).SelectMany(i => i.VisualRepresentators)
                .Where(r => r.DisplayingView == view).ToList();
            foreach (var representation in representations)
                Console.WriteLine("VISIBLE " + representation.RepresentedIdea.GlobalId + " " + representation.RepresentedIdea.Name);
            Console.WriteLine("NATIVE_VISUAL_COUNT=" + representations.Count);
            if (representations.Count != Int32.Parse(args[1])) throw new Exception("Unexpected native visual count.");
            Console.WriteLine("PASS: native overview visual count.");
            if (args.Length > 2)
            {
                context.Workspace.LoadDocument(composition);
                if (view.HostingScrollViewer != null) view.HostingScrollViewer.Content = null;
                var parent = view.TopCanvas.Parent as Panel;
                if (parent != null) parent.Children.Remove(view.TopCanvas);
                var widget = new WidgetDocumentVisualizer { Width = 900, Height = 640 };
                widget.PostViewActivation = delegate { };
                var scroll = widget.PutView(view);
                view.ShowAll();
                // An owned, non-visible HWND exercises Loaded/layout without opening a user window.
                var parameters = new HwndSourceParameters("Prototype viewport regression");
                parameters.Width = 900; parameters.Height = 640; parameters.WindowStyle = unchecked((int)0x80000000);
                using (var host = new HwndSource(parameters))
                {
                    host.RootVisual = widget;
                    for (var i = 0; i < 5; i++)
                    {
                        widget.Measure(new Size(900,640)); widget.Arrange(new Rect(0,0,900,640)); widget.UpdateLayout();
                        var frame = new DispatcherFrame();
                        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                            new Action(delegate { frame.Continue = false; }));
                        Dispatcher.PushFrame(frame);
                    }
                    Console.WriteLine("VIEWPORT loaded=" + scroll.IsLoaded + " offset=" + scroll.HorizontalOffset + "," + scroll.VerticalOffset +
                        " size=" + scroll.ViewportWidth + "," + scroll.ViewportHeight + " extent=" + scroll.ExtentWidth + "," + scroll.ExtentHeight);
                    var bitmap = new RenderTargetBitmap(900,640,96,96,PixelFormats.Pbgra32);
                    bitmap.Render(widget);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var file = File.Create(args[2])) encoder.Save(file);
                    var viewport = new Rect(scroll.HorizontalOffset,scroll.VerticalOffset,scroll.ViewportWidth,scroll.ViewportHeight);
                    if (!representations.Any(r => viewport.IntersectsWith(r.MainSymbol.BaseArea)))
                        throw new Exception("No native overview symbol intersects the initial desktop viewport.");
                    Console.WriteLine("PASS: native symbol intersects actual desktop startup viewport.");
                }
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
