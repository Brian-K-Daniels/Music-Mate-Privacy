using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace musicmate.Services
{
    /// <summary>
    /// Logical source-line numbering for About HTML. Line 1 is the source line
    /// whose visible text begins with "Welcome". Counts file newlines, not
    /// visual wrap from screen width.
    /// </summary>
    public static class AboutLogicalLineMap
    {
        public const string WelcomeLinePrefix = "Welcome";

        public readonly record struct Result(int TotalLines, int[] MatchLines);

        public static string NormalizeNewlines(string html)
            => string.IsNullOrEmpty(html)
                ? string.Empty
                : html.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        public static string[] SplitSourceLines(string html)
        {
            var normalized = NormalizeNewlines(html);
            return normalized.Length == 0 ? Array.Empty<string>() : normalized.Split('\n');
        }

        /// <summary>0-based index of the Welcome source line, or 0 if missing.</summary>
        public static int FindWelcomeLineIndex(IReadOnlyList<string> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var visible = StripTags(lines[i]).TrimStart();
                if (visible.StartsWith(WelcomeLinePrefix, StringComparison.Ordinal))
                    return i;
            }

            return 0;
        }

        public static int CountLinesFromWelcome(string html)
        {
            var lines = SplitSourceLines(html);
            if (lines.Length == 0)
                return 0;

            return lines.Length - FindWelcomeLineIndex(lines);
        }

        /// <summary>
        /// <paramref name="MatchLines"/> is aligned with in-document search hits
        /// (body text-node order, same idea as About highlight spans), 1-based from Welcome.
        /// </summary>
        public static Result Analyze(string html, string? query)
        {
            html = NormalizeNewlines(html);
            int total = CountLinesFromWelcome(html);
            if (total <= 0)
                return new Result(0, Array.Empty<int>());

            var lines = SplitSourceLines(html);
            int welcomeFileLine = FindWelcomeLineIndex(lines) + 1;

            if (string.IsNullOrEmpty(query))
                return new Result(total, Array.Empty<int>());

            var matchLines = new List<int>();
            var expression = new Regex(
                Regex.Escape(query),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            foreach (var (text, startFileLine) in ExtractBodyTextNodes(html))
            {
                foreach (Match match in expression.Matches(text))
                {
                    if (match.Length == 0)
                        continue;

                    int newlinesBefore = 0;
                    for (int i = 0; i < match.Index; i++)
                    {
                        if (text[i] == '\n')
                            newlinesBefore++;
                    }

                    int fileLine = startFileLine + newlinesBefore;
                    int logical = fileLine - welcomeFileLine + 1;
                    if (logical < 1)
                        logical = 1;
                    else if (logical > total)
                        logical = total;
                    matchLines.Add(logical);
                }
            }

            return new Result(total, matchLines.ToArray());
        }

        public static string FormatIndicator(
            bool hasMatches,
            int currentIndex,
            IReadOnlyList<int> matchLines,
            int totalLines)
        {
            if (!hasMatches || totalLines <= 0 || currentIndex < 0 || matchLines == null)
                return string.Empty;
            if (currentIndex >= matchLines.Count)
                return string.Empty;

            int line = matchLines[currentIndex];
            if (line <= 0)
                return string.Empty;

            return $"{line}/{totalLines}";
        }

        internal static IEnumerable<(string Text, int StartFileLine)> ExtractBodyTextNodes(string html)
        {
            html = NormalizeNewlines(html);
            if (html.Length == 0)
                yield break;

            int i = IndexOfBodyContent(html);
            int fileLine = 1;
            for (int c = 0; c < i; c++)
            {
                if (html[c] == '\n')
                    fileLine++;
            }

            var text = new StringBuilder();
            int textStartLine = fileLine;
            bool inTag = false;
            bool skipContent = false;
            string? skipUntil = null;

            while (i < html.Length)
            {
                char ch = html[i];

                if (!inTag && ch == '<')
                {
                    if (!skipContent && text.Length > 0)
                    {
                        yield return (Decode(text.ToString()), textStartLine);
                        text.Clear();
                    }

                    if (StartsWithAt(html, i, "<!--"))
                    {
                        int end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                        if (end < 0)
                            yield break;
                        for (int c = i; c < end + 3; c++)
                        {
                            if (html[c] == '\n')
                                fileLine++;
                        }
                        i = end + 3;
                        continue;
                    }

                    inTag = true;
                    var (tagName, isClose) = ReadTagName(html, i + 1);
                    if (!isClose && IsSkippedTag(tagName))
                    {
                        skipContent = true;
                        skipUntil = tagName;
                    }
                    else if (isClose && skipUntil != null
                             && string.Equals(tagName, skipUntil, StringComparison.OrdinalIgnoreCase))
                    {
                        skipContent = false;
                        skipUntil = null;
                    }

                    i++;
                    continue;
                }

                if (inTag)
                {
                    if (ch == '\n')
                        fileLine++;
                    if (ch == '>')
                        inTag = false;
                    i++;
                    continue;
                }

                if (skipContent)
                {
                    if (ch == '\n')
                        fileLine++;
                    i++;
                    continue;
                }

                if (text.Length == 0)
                    textStartLine = fileLine;
                text.Append(ch);
                if (ch == '\n')
                    fileLine++;
                i++;
            }

            if (text.Length > 0)
                yield return (Decode(text.ToString()), textStartLine);
        }

        private static int IndexOfBodyContent(string html)
        {
            var match = Regex.Match(html, @"<body\b[^>]*>", RegexOptions.IgnoreCase);
            return match.Success ? match.Index + match.Length : 0;
        }

        private static bool StartsWithAt(string html, int index, string value)
            => index + value.Length <= html.Length
               && string.Compare(html, index, value, 0, value.Length, StringComparison.Ordinal) == 0;

        private static (string Name, bool IsClose) ReadTagName(string html, int start)
        {
            int i = start;
            while (i < html.Length && char.IsWhiteSpace(html[i]))
                i++;
            bool isClose = i < html.Length && html[i] == '/';
            if (isClose)
                i++;
            while (i < html.Length && char.IsWhiteSpace(html[i]))
                i++;
            int from = i;
            while (i < html.Length && (char.IsLetterOrDigit(html[i]) || html[i] == '-' || html[i] == ':'))
                i++;
            return (html[from..i], isClose);
        }

        private static bool IsSkippedTag(string tagName)
            => tagName.Equals("script", StringComparison.OrdinalIgnoreCase)
               || tagName.Equals("style", StringComparison.OrdinalIgnoreCase)
               || tagName.Equals("noscript", StringComparison.OrdinalIgnoreCase);

        private static string Decode(string value)
            => WebUtility.HtmlDecode(value) ?? value;

        internal static string StripTags(string line)
        {
            if (string.IsNullOrEmpty(line))
                return string.Empty;

            var stripped = Regex.Replace(line, "<[^>]+>", string.Empty);
            return Decode(stripped);
        }
    }
}
