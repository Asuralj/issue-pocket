using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal sealed class CategoryCatalog
{
    internal const string EntryName = "categories.json";
    internal const int MaxNameLength = 40;
    internal const int MaxBytes = 4 * 1024 * 1024;
    internal static readonly StringComparer Comparer = StringComparer.CurrentCultureIgnoreCase;
    private readonly string path;

    internal CategoryCatalog(string root)
    {
        path = Path.Combine(root, EntryName);
    }

    internal List<string> Load()
    {
        if (!File.Exists(path))
            return new List<string>();
        using (var input = File.OpenRead(path))
            return Read(input, input.Length);
    }

    internal List<string> Merge(IEnumerable<string> names)
    {
        var current = Load();
        var seen = new HashSet<string>(current, Comparer);
        bool changed = false;
        foreach (string name in names)
        {
            ValidateName(name);
            if (!Comparer.Equals(name, IssueStore.DefaultCategory) && seen.Add(name))
            {
                current.Add(name);
                changed = true;
            }
        }
        if (changed)
            LocalJson.Save(path, current, MaxBytes, "分类数据过大。");
        return current;
    }

    internal static List<string> Read(Stream input, long length)
    {
        if (length > MaxBytes)
            throw new InvalidDataException("分类数据过大。");
        var names = LocalJson.Read<List<string>>(input);
        if (names == null)
            throw new InvalidDataException("分类数据格式无效。");
        foreach (string name in names)
            ValidateName(name);
        return names.Where(name => !Comparer.Equals(name, IssueStore.DefaultCategory)).Distinct(Comparer).ToList();
    }

    private static void ValidateName(string name)
    {
        if (String.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength || name != name.Trim() || name.Any(Char.IsControl))
            throw new InvalidDataException("分类名称应为 1 至 40 个字符。");
    }
}
