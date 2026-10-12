// Exercise the actual WPF tree click handler in an isolated headless process.
// No user document is saved and no window is shown.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Instrumind.ThinkComposer.ApplicationProduct;
using Instrumind.ThinkComposer.ApplicationProduct.Widgets;
using Instrumind.ThinkComposer.Composer;
using Instrumind.ThinkComposer.Composer.ComposerUI;
using Instrumind.ThinkComposer.Headless;
using Instrumind.ThinkComposer.Model.GraphModel;

class NavigationRegression
{
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    static void Click(WidgetNavTree widget, Idea target)
    {
        typeof(WidgetNavTree).GetField("TargetIdea", PrivateInstance).SetValue(widget, target);
        var item = new TreeViewItem { Header = target };
        item.Template = new ControlTemplate(typeof(TreeViewItem))
            { VisualTree = new FrameworkElementFactory(typeof(Border)) };
        item.ApplyTemplate();
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left);
        args.RoutedEvent = Mouse.MouseUpEvent;
        args.Source = VisualTreeHelper.GetChild(item, 0);
        typeof(WidgetNavTree).GetMethod("NavTree_MouseLeftButtonUp", PrivateInstance)
            .Invoke(widget, new object[] { item, args });
        if ((bool)typeof(WidgetNavTree).GetField("IsNavigating", PrivateInstance).GetValue(widget))
            throw new Exception("Navigation state remained active.");
        if (typeof(WidgetNavTree).GetField("TargetIdea", PrivateInstance).GetValue(widget) != null)
            throw new Exception("Navigation target was not cleared.");
        if (ProductDirector.EditorInterrelationsControl.WorkingEntity != target)
            throw new Exception("Model element was not selected for inspection.");
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            var context = HeadlessBootstrap.Initialize();
            CompositionEngine.CreateActiveCompositionEngine(context.Compositions, context.Visualizer, false);
            var loaded = CompositionEngine.Materialize(new Uri(Path.GetFullPath(args[0])));
            if (loaded == null || loaded.Item1 == null) throw new Exception("Fixture load failed.");
            var engine = loaded.Item1;
            var composition = engine.TargetComposition;
            context.Workspace.LoadDocument(composition);
            var view = composition.RootView;
            var target = new Concept(composition, composition.CompositeContentDomain.ConceptDefinitions.First(),
                                     "Model-only navigation regression", "ModelOnlyRegression");
            target.AddToComposite(composition);
            if (target.VisualRepresentators.Any()) throw new Exception("Fixture must begin without visuals.");
            ProductDirector.EditorInterrelationsControl = new WidgetInterrelationsPanel();
            var widget = new WidgetNavTree(context.Workspace);
            typeof(WidgetNavTree).GetField("CurrentView", PrivateInstance).SetValue(widget, view);

            Click(widget, target);
            if (target.VisualRepresentators.Any()) throw new Exception("Click unexpectedly created a visual.");
            Console.WriteLine("PASS: model-only click inspects the element without throwing or creating a symbol.");

            // A representation in another view must not be mistaken for a symbol in this one.
            var otherView = new Instrumind.ThinkComposer.Model.VisualModel.View(composition, "Other view", "Other_View");
            composition.CompositeViews.Add(otherView);
            otherView.Initialize();
            if (otherView == view) throw new Exception("Fixture needs a distinct second view.");
            ConceptCreationCommand.CreateConceptVisualRepresentation(target, otherView, new Point(7920, 5885));
            composition.RootView = view;
            context.Visualizer.PutView(view);
            if (target.VisualRepresentators.Any(rep => rep.DisplayingView == view))
                throw new Exception("Fixture must not have a symbol in the current view yet.");
            Click(widget, target);
            Console.WriteLine("PASS: symbol in another view does not require a current-view match.");

            ConceptCreationCommand.CreateConceptVisualRepresentation(target, view, new Point(7920, 5885));
            Click(widget, target);
            if (!target.IsSelected) throw new Exception("Visible-element selection was not preserved.");
            Console.WriteLine("PASS: visible-element navigation still completes.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally { if (Application.Current != null) Application.Current.Shutdown(); }
    }
}
