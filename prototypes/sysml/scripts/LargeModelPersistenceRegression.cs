using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Instrumind.ThinkComposer.Composer;
using Instrumind.ThinkComposer.Headless;

class LargeModelPersistenceRegression
{
    static void Report(string stage, Stopwatch timer)
    {
        Console.WriteLine("STAGE " + stage + " elapsedSeconds=" + timer.Elapsed.TotalSeconds +
            " managedMB=" + GC.GetTotalMemory(false)/1048576 +
            " privateMB=" + Process.GetCurrentProcess().PrivateMemorySize64/1048576);
    }

    [STAThread]
    static int Main(string[] args)
    {
        AppDomain.CurrentDomain.FirstChanceException += (sender, evt) => {
            if (evt.Exception is OutOfMemoryException) Console.Error.WriteLine(evt.Exception + Environment.NewLine + Environment.StackTrace);
        };
        try
        {
            var input=Path.GetFullPath(args[0]); var output=Path.GetFullPath(args[1]);
            if (String.Equals(input,output,StringComparison.OrdinalIgnoreCase)) throw new Exception("Use a separate test output.");
            var timer=Stopwatch.StartNew();
            var context=HeadlessBootstrap.Initialize();
            CompositionEngine.CreateActiveCompositionEngine(context.Compositions,context.Visualizer,false);
            var loaded=CompositionEngine.Materialize(new Uri(input));
            if (loaded==null || loaded.Item1==null) throw new Exception("Native load failed.");
            var engine=loaded.Item1;
            Report("loaded",timer);
            Console.WriteLine("IDEAS="+engine.TargetComposition.DeclaredIdeas.Count());
            foreach (var owner in engine.TargetComposition.DeclaredIdeas.Where(i => i.CompositeViews.Count > 0))
            {
                var active=owner.CompositeActiveView;
                if (active==null) throw new Exception("Missing active drill-down View: "+owner.Name);
                var symbols=engine.TargetComposition.DeclaredIdeas.SelectMany(i => i.VisualRepresentators)
                    .Count(r => r.DisplayingView==active);
                if (symbols==0) throw new Exception("Empty drill-down View: "+owner.Name);
                Console.WriteLine("DRILLDOWN "+owner.GlobalId+" "+owner.Name+" symbols="+symbols);
            }
            var result=JsonPackagePersistence.StoreComposition(engine.TargetComposition,new Uri(output),false,true,null,true);
            if (!String.IsNullOrEmpty(result)) throw new Exception(result);
            Report("saved",timer);
            Console.WriteLine("PASS: full native save to separate output.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
