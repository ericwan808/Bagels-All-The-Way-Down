using System;
using System.Collections.Generic;
using System.Linq;

namespace Bagels.Core
{
    /// <summary>
    /// What customers say when their order is completed, by how many extra toppings it had (design.md, "Orders").
    /// Read from a plain text file:
    /// <code>
    /// # A comment
    /// [0]
    /// Perfect, just what I asked for!
    /// [1]
    /// Hm, there's something extra in here.
    /// [3-6]
    /// This is getting crowded.
    /// [7+]
    /// What is all this?
    /// </code>
    /// A header <c>[n]</c> starts the lines for orders with n extra toppings; its lines also cover every count up to
    /// the next header, so <c>[7+]</c> (the + is optional) covers seven or more. A range like <c>[3-6]</c> starts at
    /// its first number; the end is there for the reader, and the next header still decides where it stops. A count
    /// below the first header uses the first header's lines. Blank lines and lines starting with # are ignored.
    /// </summary>
    public sealed class CustomerResponses
    {
        readonly SortedDictionary<int, List<string>> lines = new SortedDictionary<int, List<string>>();

        public static readonly CustomerResponses None = new CustomerResponses();

        /// <summary>
        /// The lines covering <paramref name="imperfections"/> extras: the last header at or below it, or the first
        /// header if none is. Empty if the file has no lines.
        /// </summary>
        public IReadOnlyList<string> LinesFor(int imperfections)
        {
            if (lines.Count == 0) return Array.Empty<string>();
            int key = lines.Keys.First();
            foreach (int k in lines.Keys)
                if (k <= imperfections) key = k;
            return lines[key];
        }

        /// <summary>A random line for an order with <paramref name="imperfections"/> extras, or null if there are none.</summary>
        public string Pick(int imperfections, Random random)
        {
            var options = LinesFor(imperfections);
            return options.Count == 0 ? null : options[random.Next(options.Count)];
        }

        public static CustomerResponses Parse(string text)
        {
            var result = new CustomerResponses();
            List<string> current = null;
            int lineNumber = 0;
            foreach (var raw in (text ?? "").Split('\n'))
            {
                lineNumber++;
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    if (!TryParseHeader(line.Substring(1, line.Length - 2), out int n))
                        throw new FormatException($"Line {lineNumber}: \"{line}\" should be a header like [0], [3-6] or [7+].");
                    if (!result.lines.TryGetValue(n, out current)) result.lines[n] = current = new List<string>();
                    continue;
                }
                if (current == null)
                    throw new FormatException($"Line {lineNumber}: \"{line}\" comes before any header like [0].");
                current.Add(line);
            }
            foreach (var key in result.lines.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList())
                result.lines.Remove(key);
            return result;
        }

        /// <summary>Reads a header's inside: "n", "n+" or "n-m" with m at least n. Gives the first number.</summary>
        static bool TryParseHeader(string body, out int first)
        {
            var parts = body.Trim().TrimEnd('+').Split('-');
            if (parts.Length > 2 || !int.TryParse(parts[0].Trim(), out first) || first < 0)
            {
                first = 0;
                return false;
            }
            return parts.Length == 1 || int.TryParse(parts[1].Trim(), out int last) && last >= first;
        }
    }
}
