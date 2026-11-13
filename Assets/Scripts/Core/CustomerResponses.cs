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
    /// [5+]
    /// What is all this?
    /// </code>
    /// A header <c>[n]</c> starts the lines for orders with n extra toppings; its lines also cover every count up to
    /// the next header, so <c>[5+]</c> (the + is optional) covers five or more. A count below the first header uses
    /// the first header's lines. Blank lines and lines starting with # are ignored.
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
                    var number = line.Substring(1, line.Length - 2).Trim().TrimEnd('+').Trim();
                    if (!int.TryParse(number, out int n) || n < 0)
                        throw new FormatException($"Line {lineNumber}: \"{line}\" should be a header like [0], [1] or [5+].");
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
    }
}
