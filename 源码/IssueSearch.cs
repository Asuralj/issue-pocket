using System;
using System.Collections.Generic;
using System.Linq;

internal static class IssueSearch
{
    internal static string[] Terms(string query)
    {
        return query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
    }

    internal static IEnumerable<Issue> Match(IEnumerable<Issue> issues, string[] terms, string category, bool? resolved)
    {
        return issues.Where(issue =>
            (category == null || CategoryCatalog.Comparer.Equals(issue.Category, category)) &&
            (!resolved.HasValue || issue.Resolved == resolved.Value) &&
            terms.All(term => Matches(issue, term)))
            .OrderByDescending(issue => issue.UpdatedUtc).ThenByDescending(issue => issue.CreatedUtc);
    }

    internal static string Snippet(Issue issue, string[] terms)
    {
        string resultMatch = terms.FirstOrDefault(term => Contains(issue.Result, term));
        if (resultMatch != null)
            return Excerpt(issue.Result, resultMatch, "处理结果 · ");
        if (terms.Length > 0)
        {
            foreach (var entry in issue.FollowUps.Reverse())
            {
                string match = terms.FirstOrDefault(term => Contains(entry.Text, term));
                if (match != null)
                    return Excerpt(entry.Text, match, "跟进 · ");
            }
        }
        if (issue.Body.Length > 0)
            return Excerpt(issue.Body, null, "");
        if (!String.IsNullOrWhiteSpace(issue.Result))
            return Excerpt(issue.Result, null, "处理结果 · ");
        return issue.Attachments.Count == 0 ? "" : issue.Attachments[0].Name;
    }

    private static bool Matches(Issue issue, string term)
    {
        return Contains(issue.Title, term) || Contains(issue.Body, term) || Contains(issue.Result, term) ||
            Contains(issue.Category, term) || issue.FollowUps.Any(entry => Contains(entry.Text, term)) ||
            issue.Attachments.Any(item => Contains(item.Name, term));
    }

    private static bool Contains(string text, string term)
    {
        return text != null && text.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    private static string Excerpt(string text, string match, string label)
    {
        int offset = match == null ? 0 : Math.Max(0, text.IndexOf(match, StringComparison.CurrentCultureIgnoreCase) - 9);
        if (offset > 0 && Char.IsLowSurrogate(text[offset]))
            offset--;
        int length = Math.Min(160, text.Length - offset);
        if (length > 0 && Char.IsHighSurrogate(text[offset + length - 1]))
            length--;
        return label + String.Join(" ", text.Substring(offset, length).Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
    }
}
