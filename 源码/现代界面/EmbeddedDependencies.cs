using System;
using System.IO;
using System.Reflection;

internal static class EmbeddedDependencies
{
    internal static void Register()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
    }

    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        if (new AssemblyName(args.Name).Name != "HtmlAgilityPack")
            return null;
        using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Pocket.HtmlAgilityPack.dll"))
        {
            if (resource == null)
                return null;
            using (var bytes = new MemoryStream())
            {
                resource.CopyTo(bytes);
                return Assembly.Load(bytes.ToArray());
            }
        }
    }
}
