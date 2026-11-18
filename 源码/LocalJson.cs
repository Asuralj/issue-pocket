using System;
using System.IO;
using System.Runtime.Serialization.Json;

internal static class LocalJson
{
    internal static T Read<T>(Stream stream)
    {
        return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
    }

    internal static void Write<T>(Stream stream, T value)
    {
        new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
    }

    internal static void Save<T>(string path, T value, long limit, string limitMessage)
    {
        using (var contents = new MemoryStream())
        {
            Write(contents, value);
            if (contents.Length > limit)
                throw new InvalidDataException(limitMessage);
            string pending = Path.Combine(Path.GetDirectoryName(path), "." + Guid.NewGuid().ToString("N") + ".pending");
            using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                contents.WriteTo(stream);
                stream.Flush(true);
            }
            if (File.Exists(path))
                File.Replace(pending, path, path + ".bak", true);
            else
                File.Move(pending, path);
        }
    }
}
