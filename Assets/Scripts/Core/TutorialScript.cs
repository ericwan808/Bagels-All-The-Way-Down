using System;
using System.Collections.Generic;

namespace Bagels.Core
{
    /// <summary>
    /// The text of the tutorial overlays, read from a plain text file:
    /// <code>
    /// # A comment
    /// [welcome]
    /// First page of the welcome overlay.
    /// Second page.
    /// [place-a]
    /// ...
    /// </code>
    /// A header <c>[id]</c> starts an overlay; each line under it is one page. Blank lines and lines starting with #
    /// are ignored. Which overlay shows when is decided by the game, by id.
    /// </summary>
    public sealed class TutorialScript
    {
        readonly Dictionary<string, List<string>> pages = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public static readonly TutorialScript None = new TutorialScript();

        /// <summary>The pages of overlay <paramref name="id"/>, in order; empty if the file has none.</summary>
        public IReadOnlyList<string> PagesOf(string id) =>
            id != null && pages.TryGetValue(id, out var list) ? list : (IReadOnlyList<string>)Array.Empty<string>();

        public static TutorialScript Parse(string text)
        {
            var result = new TutorialScript();
            List<string> current = null;
            int lineNumber = 0;
            foreach (var raw in (text ?? "").Split('\n'))
            {
                lineNumber++;
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    var id = line.Substring(1, line.Length - 2).Trim();
                    if (id.Length == 0) throw new FormatException($"Line {lineNumber}: an overlay header needs an id, like [welcome].");
                    if (!result.pages.TryGetValue(id, out current)) result.pages[id] = current = new List<string>();
                    continue;
                }
                if (current == null)
                    throw new FormatException($"Line {lineNumber}: \"{line}\" comes before any header like [welcome].");
                current.Add(line);
            }
            return result;
        }
    }
}
