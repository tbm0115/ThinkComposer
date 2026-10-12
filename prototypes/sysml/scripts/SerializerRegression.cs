using System;
using System.IO;
using System.Reflection;
using System.Text;
using Instrumind.ThinkComposer.Composer.JsonInterchange;

class SerializerRegression
{
    static int Main(string[] args)
    {
        var doc=CompositionJsonSerializer.Load(args[0]);
        CompositionJsonSerializer.Save(doc,args[1]);
        var serialized=CompositionJsonSerializer.Serialize(doc);
        var textLoaded=CompositionJsonSerializer.Deserialize(File.ReadAllText(args[0],Encoding.UTF8));
        if (CompositionJsonSerializer.Serialize(textLoaded)!=serialized) throw new Exception("Stream and text readers differ.");
        if (File.ReadAllText(args[1],Encoding.UTF8)!=serialized) throw new Exception("Save and Serialize differ.");
        var write=typeof(CompositionJsonSerializer).GetMethod("Write",BindingFlags.Public|BindingFlags.Static);
        if (write!=null)
            using (var writer=new StringWriter())
            {
                write.Invoke(null,new object[]{doc,writer});
                if (writer.ToString()!=serialized) throw new Exception("Streaming writer differs.");
            }
        Console.WriteLine("PASS: serializer APIs agree.");
        return 0;
    }
}
