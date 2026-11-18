using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;

[DataContract]
internal sealed class Attachment
{
    [DataMember] public string Name;
    [DataMember] public string Hash;
    [DataMember] public long Size;

    public override string ToString()
    {
        return Name;
    }
}

[DataContract]
internal sealed class IssueFollowUp
{
    internal const int MaxLength = 8000;
    [DataMember] public DateTime CreatedUtc { get; private set; }
    [DataMember] public string Text { get; private set; }

    internal IssueFollowUp(DateTime createdUtc, string text)
    {
        CreatedUtc = createdUtc;
        Text = text;
    }
}

[DataContract]
internal sealed class Issue
{
    [DataMember] public string Id;
    [DataMember] public string Title;
    [DataMember] public string Body;
    [DataMember(EmitDefaultValue = false)] public string Result;
    [DataMember] public string Category;
    [DataMember] public DateTime CreatedUtc;
    [DataMember] public DateTime UpdatedUtc;
    [DataMember] public bool Resolved;
    [DataMember] public List<Attachment> Attachments = new List<Attachment>();
    [DataMember(Name = "FollowUps", EmitDefaultValue = false)] private List<IssueFollowUp> followUps;

    internal IEnumerable<IssueFollowUp> FollowUps
    {
        get { return followUps ?? Enumerable.Empty<IssueFollowUp>(); }
    }

    public Issue Copy()
    {
        var copy = (Issue)MemberwiseClone();
        copy.Attachments = new List<Attachment>(Attachments);
        if (followUps != null)
            copy.followUps = new List<IssueFollowUp>(followUps);
        return copy;
    }

    internal void AddFollowUp(string text, DateTime now)
    {
        if (followUps == null)
            followUps = new List<IssueFollowUp>();
        followUps.Add(new IssueFollowUp(now, text));
        Touch(now);
    }

    internal bool EditFollowUp(IssueFollowUp entry, string text, DateTime now)
    {
        int index = followUps == null ? -1 : followUps.IndexOf(entry);
        if (index < 0)
            return false;
        followUps[index] = new IssueFollowUp(entry.CreatedUtc, text);
        Touch(now);
        return true;
    }

    internal void Touch(DateTime now)
    {
        if (now < CreatedUtc)
            now = CreatedUtc;
        if (now > UpdatedUtc)
            UpdatedUtc = now;
    }

    public override string ToString()
    {
        return Title;
    }
}

internal sealed class IssueStore
{
    public const string DefaultCategory = "未分类";
    private const int MaxRecordBytes = 4 * 1024 * 1024;
    private const string LegacyBackupMarker = "问题随手记备份.v1";
    private const string BackupMarker = "问题随手记备份.v2";
    private readonly string records;
    private readonly string files;
    public readonly string Root;
    internal readonly CategoryCatalog Categories;

    public IssueStore(string root)
    {
        Root = root;
        Categories = new CategoryCatalog(root);
        records = Path.Combine(root, "records");
        files = Path.Combine(root, "files");
        Directory.CreateDirectory(records);
        Directory.CreateDirectory(files);
    }

    public List<Issue> Load(out int unreadable)
    {
        unreadable = 0;
        var issues = new List<Issue>();
        foreach (string path in Directory.EnumerateFiles(records, "*.json"))
        {
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    issues.Add(ReadIssue(stream, stream.Length, Path.GetFileNameWithoutExtension(path)));
                }
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is InvalidDataException || ex is SerializationException ||
                    ex is UnauthorizedAccessException || ex is FormatException ||
                    ex is ArgumentException || ex is System.Xml.XmlException))
                    throw;
                unreadable++;
            }
        }
        return issues;
    }

    public void Save(Issue issue)
    {
        Validate(issue, issue.Id);
        LocalJson.Save(RecordPath(issue.Id), issue, MaxRecordBytes, "记录文件过大。");
    }

    public Attachment AddFile(string path)
    {
        using (var stream = File.OpenRead(path))
        {
            return AddAttachment(Path.GetFileName(path), stream);
        }
    }

    public Attachment AddAttachment(string name, Stream stream)
    {
        ValidateName(name);
        long start = stream.Position;
        long size = stream.Length - stream.Position;
        string hash = HashStream(stream);
        stream.Position = start;
        WriteObject(stream, hash, size);
        return new Attachment { Name = name, Size = size, Hash = hash };
    }

    public void ExportAttachment(Attachment attachment, string path)
    {
        using (var input = OpenAttachment(attachment))
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            input.CopyTo(output);
            output.Flush(true);
        }
    }

    public Stream OpenAttachment(Attachment attachment)
    {
        if (!IsHex(attachment.Hash, 64))
            throw new InvalidDataException("附件编号无效。");
        var stream = File.OpenRead(Path.Combine(files, attachment.Hash));
        if (stream.Length == attachment.Size)
            return stream;
        stream.Dispose();
        throw new InvalidDataException("附件大小与记录不符。");
    }

    public void ExportBackup(string path)
    {
        int unreadable;
        var issues = Load(out unreadable);
        if (unreadable > 0)
            throw new InvalidDataException("有记录无法读取，无法生成完整备份。");
        var categoryNames = Categories.Load();
        string pending = PendingPath(path);
        using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                zip.CreateEntry(BackupMarker);
                if (categoryNames.Count > 0)
                {
                    using (var output = zip.CreateEntry(CategoryCatalog.EntryName).Open())
                        LocalJson.Write(output, categoryNames);
                }
                var saved = new HashSet<string>(StringComparer.Ordinal);
                foreach (var issue in issues)
                {
                    using (var output = zip.CreateEntry("records/" + issue.Id + ".json").Open())
                        LocalJson.Write(output, issue);
                    foreach (var attachment in issue.Attachments)
                    {
                        if (!saved.Add(attachment.Hash))
                            continue;
                        using (var input = OpenAttachment(attachment))
                        using (var output = zip.CreateEntry("files/" + attachment.Hash, CompressionLevel.Fastest).Open())
                            input.CopyTo(output);
                    }
                }
            }
            stream.Flush(true);
        }
        File.Move(pending, path);
    }

    public int ImportBackup(string path, out int skipped)
    {
        skipped = 0;
        using (var stream = File.OpenRead(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            if (zip.GetEntry(BackupMarker) == null && zip.GetEntry(LegacyBackupMarker) == null)
                throw new InvalidDataException("请选择由问题随手记生成的备份文件。");

            var categoryEntry = zip.GetEntry(CategoryCatalog.EntryName);
            List<string> importedCategories = null;
            if (categoryEntry != null)
            {
                using (var input = categoryEntry.Open())
                    importedCategories = CategoryCatalog.Read(input, categoryEntry.Length);
            }
            var issues = new List<Issue>();
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith("records/", StringComparison.Ordinal))
                    continue;
                string name = entry.FullName.Substring(8);
                if (name.Length != 37 || !name.EndsWith(".json", StringComparison.Ordinal))
                    throw new InvalidDataException("备份包含无效记录。");
                string id = name.Substring(0, 32);
                if (!identifiers.Add(id))
                    throw new InvalidDataException("备份包含重复编号。");
                using (var input = entry.Open())
                {
                    var issue = ReadIssue(input, entry.Length, id);
                    if (File.Exists(RecordPath(id)))
                        skipped++;
                    else
                        issues.Add(issue);
                }
            }

            foreach (var group in issues.SelectMany(issue => issue.Attachments).GroupBy(attachment => attachment.Hash))
            {
                var attachment = group.First();
                var entry = zip.GetEntry("files/" + attachment.Hash);
                if (entry == null || entry.Length != attachment.Size ||
                    group.Any(other => other.Size != attachment.Size))
                    throw new InvalidDataException("备份缺少完整附件：" + attachment.Name);
                using (var input = entry.Open())
                    WriteObject(input, attachment.Hash, attachment.Size);
            }
            foreach (var issue in issues)
                Save(issue);
            if (importedCategories != null && importedCategories.Count > 0)
                Categories.Merge(importedCategories);
            return issues.Count;
        }
    }

    private void WriteObject(Stream input, string expectedHash, long expectedSize)
    {
        string path = Path.Combine(files, expectedHash);
        if (File.Exists(path))
        {
            bool intact;
            using (var existing = File.OpenRead(path))
                intact = existing.Length == expectedSize && HashStream(existing) == expectedHash;
            if (intact)
            {
                if (HashStream(input) != expectedHash)
                    throw new InvalidDataException("附件校验失败。");
                return;
            }
        }
        string pending = Path.Combine(files, Guid.NewGuid().ToString("N") + ".pending");
        using (var sha = SHA256.Create())
        using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            long size = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                size += read;
                if (size > expectedSize)
                    throw new InvalidDataException("附件大小发生变化。");
                sha.TransformBlock(buffer, 0, read, buffer, 0);
                output.Write(buffer, 0, read);
            }
            sha.TransformFinalBlock(new byte[0], 0, 0);
            if (size != expectedSize || HexDigest(sha.Hash) != expectedHash)
                throw new InvalidDataException("附件校验失败。");
            output.Flush(true);
        }
        if (File.Exists(path))
            File.Replace(pending, path, PendingPath(path) + ".bak", true);
        else
            File.Move(pending, path);
    }

    private static string HashStream(Stream stream)
    {
        using (var sha = SHA256.Create())
            return HexDigest(sha.ComputeHash(stream));
    }

    private static string HexDigest(byte[] digest)
    {
        return BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();
    }

    private static Issue ReadIssue(Stream stream, long length, string id)
    {
        if (length > MaxRecordBytes)
            throw new InvalidDataException("记录文件过大。");
        var issue = LocalJson.Read<Issue>(stream);
        Validate(issue, id);
        return issue;
    }

    private static void Validate(Issue issue, string id)
    {
        if (issue == null || issue.Id != id || !IsHex(id, 32) ||
            String.IsNullOrWhiteSpace(issue.Title) || issue.Title.Length > 160 ||
            issue.Body == null || issue.Body.Length > 120000 ||
            (issue.Result != null && issue.Result.Length > 120000) ||
            String.IsNullOrWhiteSpace(issue.Category) || issue.Category.Length > CategoryCatalog.MaxNameLength ||
            issue.CreatedUtc == default(DateTime) || issue.UpdatedUtc < issue.CreatedUtc ||
            issue.Attachments == null || issue.Attachments.Count > 100)
            throw new InvalidDataException("问题记录格式无效。");
        if (issue.FollowUps.Any(entry => entry == null || entry.CreatedUtc == default(DateTime) ||
            String.IsNullOrWhiteSpace(entry.Text) || entry.Text.Length > IssueFollowUp.MaxLength))
            throw new InvalidDataException("跟进记录格式无效。");
        foreach (var attachment in issue.Attachments)
        {
            if (attachment == null || !IsHex(attachment.Hash, 64) || attachment.Size < 0)
                throw new InvalidDataException("附件信息无效。");
            ValidateName(attachment.Name);
        }
    }

    private static void ValidateName(string name)
    {
        if (String.IsNullOrWhiteSpace(name) || name.Length > 255 || name == "." || name == ".." ||
            name.EndsWith(".", StringComparison.Ordinal) || name.EndsWith(" ", StringComparison.Ordinal) ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("附件文件名无效。");
        string stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
        if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) &&
                "123456789¹²³".Contains(stem[3])))
            throw new InvalidDataException("附件文件名使用了 Windows 保留名称。");
    }

    private static bool IsHex(string value, int length)
    {
        return value != null && value.Length == length &&
            value.All(ch => (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f'));
    }

    private string RecordPath(string id)
    {
        return Path.Combine(records, id + ".json");
    }

    private static string PendingPath(string path)
    {
        return Path.Combine(Path.GetDirectoryName(path), "." + Guid.NewGuid().ToString("N") + ".pending");
    }
}
