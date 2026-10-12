using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Instrumind.ThinkComposer.Composer;
using Instrumind.ThinkComposer.Headless;
using Instrumind.ThinkComposer.Model.GraphModel;

class ModelFixesRegression
{
    [STAThread]
    static int Main(string[] args)
    {
        var context=HeadlessBootstrap.Initialize();
        CompositionEngine.CreateActiveCompositionEngine(context.Compositions,context.Visualizer,false);
        var composition=CompositionEngine.Materialize(new Uri(Path.GetFullPath(args[0]))).Item1.TargetComposition;
        var definition=composition.CompositeContentDomain.ConceptDefinitions.First();
        var a=new Concept(composition,definition,"Duplicate A","DuplicateA"); a.AddToComposite(composition);
        var b=new Concept(composition,definition,"Duplicate B","DuplicateB"); b.AddToComposite(composition);
        var nested=new Concept(composition,definition,"Nested","Nested"); nested.AddToComposite(a);
        b.GlobalId=a.GlobalId;
        var first=a.GlobalId; var child=nested.GlobalId;
        var stable=composition.DeclaredIdeas.Where(i=>i!=b).ToDictionary(i=>i,i=>i.GlobalId);
        var fixes=typeof(CompositionEngine).Assembly.GetType("Instrumind.ThinkComposer.Composer.ModelFixes");
        var migrate=fixes.GetMethod("ModelRev8_FixDuplicatedGlobalIds",BindingFlags.Static|BindingFlags.NonPublic);
        migrate.Invoke(null,new object[]{composition,null,null,null});
        if (a.GlobalId!=first || b.GlobalId==first || nested.GlobalId!=child) throw new Exception("Legacy duplicate repair changed the wrong identities.");
        foreach (var pair in stable) if (pair.Key.GlobalId!=pair.Value) throw new Exception("Unique identity was changed.");
        var repaired=b.GlobalId;
        migrate.Invoke(null,new object[]{composition,null,null,null});
        if (b.GlobalId!=repaired) throw new Exception("Migration is not idempotent.");
        Console.WriteLine("PASS: legacy duplicates repaired, unrelated/nested identities preserved, repeated traversal idempotent.");
        return 0;
    }
}
