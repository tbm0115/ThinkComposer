// -------------------------------------------------------------------------------------------
// Instrumind ThinkComposer
//
// Headless bootstrap services for command-line operations.
// -------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

using Instrumind.Common;
using Instrumind.Common.Visualization;

using Instrumind.ThinkComposer.ApplicationProduct;
using Instrumind.ThinkComposer.ApplicationProduct.Widgets;
using Instrumind.ThinkComposer.Composer;
using Instrumind.ThinkComposer.Definitor;

namespace Instrumind.ThinkComposer.Headless
{
    public sealed class HeadlessContext
    {
        internal HeadlessContext(HeadlessShellProvider ShellProvider,
                                 WorkspaceManager Workspace,
                                 HeadlessDocumentVisualizer Visualizer,
                                 CompositionsManager Compositions,
                                 DomainsManager Domains)
        {
            this.ShellProvider = ShellProvider;
            this.Workspace = Workspace;
            this.Visualizer = Visualizer;
            this.Compositions = Compositions;
            this.Domains = Domains;
        }

        public HeadlessShellProvider ShellProvider { get; private set; }
        public WorkspaceManager Workspace { get; private set; }
        public HeadlessDocumentVisualizer Visualizer { get; private set; }
        public CompositionsManager Compositions { get; private set; }
        public DomainsManager Domains { get; private set; }
    }

    public static class HeadlessBootstrap
    {
        private static readonly object SyncRoot = new object();
        private static HeadlessContext CurrentContext = null;

        public static HeadlessContext Initialize()
        {
            lock (SyncRoot)
            {
                if (CurrentContext != null)
                    return CurrentContext;

                EnsureApplication();
                InitializeCommonServices();

                // ProductDirector's static constructor depends on AppExec paths.
                RuntimeHelpers.RunClassConstructor(typeof(ProductDirector).TypeHandle);

                var Shell = new HeadlessShellProvider();
                ProductDirector.Initialize(Shell);

                var Workspace = new WorkspaceManager(Shell);
                var Visualizer = new HeadlessDocumentVisualizer();
                Visualizer.PostViewActivation =
                    delegate(IDocumentView DocView)
                    {
                        if (DocView == null || DocView.ParentDocument == null || DocView.ParentDocument.DocumentEditEngine == null)
                            return;

                        DocView.ParentDocument.DocumentEditEngine.ReactToViewChanged(DocView);
                    };

                var ConceptPalette = new WidgetItemsPaletteGroup();
                var RelationshipPalette = new WidgetItemsPaletteGroup();
                var MarkerPalette = new WidgetItemsPaletteGroup();
                var ComplementPalette = new WidgetItemsPaletteGroup();

                var Compositions = new CompositionsManager("Composition Manager", "CompositionManager",
                                                           "Manager for the Composition work-sphere.",
                                                           Display.GetAppImage("page_white_edit.png"),
                                                           Workspace, Visualizer,
                                                           ConceptPalette, RelationshipPalette,
                                                           MarkerPalette, ComplementPalette);

                var Domains = new DomainsManager("Domain Manager", "DomainManager",
                                                 "Manager for the Domain work-sphere.",
                                                 Display.GetAppImage("book_edit.png"),
                                                 Workspace, Visualizer,
                                                 ConceptPalette, RelationshipPalette);

                ProductDirector.WorkspaceDirector = Workspace;
                ProductDirector.DocumentVisualizerControl = null;
                ProductDirector.CompositionDirector = Compositions;
                ProductDirector.DomainDirector = Domains;

                CurrentContext = new HeadlessContext(Shell, Workspace, Visualizer, Compositions, Domains);
                return CurrentContext;
            }
        }

        private static void EnsureApplication()
        {
            if (Application.Current == null)
                new Application();

            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            EnsureResourceDictionaries();

            try
            {
                FrameworkElement.LanguageProperty.OverrideMetadata(
                    typeof(FrameworkElement),
                    new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.Name)));
            }
            catch (InvalidOperationException)
            {
                // Metadata can only be overridden once per AppDomain.
            }
        }

        private static void EnsureResourceDictionaries()
        {
            AddResourceDictionary("pack://application:,,,/Instrumind.Common;component/Themes/Generic.xaml");
            AddResourceDictionary("pack://application:,,,/Instrumind.ThinkComposer;component/Themes/Generic.xaml");
            AddResourceDictionary("pack://application:,,,/Instrumind.ThinkComposer;component/ApplicationProduct/Cursors/AppCursors.xaml");
            AddResourceDictionary("pack://application:,,,/Instrumind.ThinkComposer;component/MetaModel/VisualMetaModel/DecoratorGeometries.xaml");
            AddResourceDictionary("pack://application:,,,/Instrumind.ThinkComposer;component/MetaModel/VisualMetaModel/SymbolGeometries.xaml");
            AddResourceDictionary("pack://application:,,,/Instrumind.ThinkComposer;component/MetaModel/VisualMetaModel/PlugGeometries.xaml");
        }

        private static void AddResourceDictionary(string Source)
        {
            var DictionaryUri = new Uri(Source, UriKind.Absolute);
            if (Application.Current.Resources.MergedDictionaries.Any(Dictionary => Dictionary.Source == DictionaryUri))
                return;

            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = DictionaryUri });
        }

        private static void InitializeCommonServices()
        {
            AppExec.Initialize(ProductDirector.APPLICATION_NAME,
                               ProductDirector.APPLICATION_VERSION,
                               ProductDirector.APPLICATION_DEFINITIONS_NAME,
                               ProductDirector.USER_DOCUMENTS_NAME);

            if (!String.IsNullOrEmpty(AppExec.ConfigurationFilePath) && File.Exists(AppExec.ConfigurationFilePath))
                AppExec.LoadConfigurationFrom();

            AppExec.LogRegistrationPolicy = AppExec.GetConfiguration("Application", "LoggingPolicy", AppExec.LogRegistrationPolicy);
        }
    }
}
