using System;
using System.Collections.Generic;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Per-folder memo of a computed diff, keyed by a file-mtime signature: keeps the window's 1s poll to
    /// stat() calls until a source file changes. Main-thread only.
    /// </summary>
    class FigmaScanCache<T>
    {
        readonly Dictionary<string, (string sig, List<T> diff)> m_Entries =
            new Dictionary<string, (string, List<T>)>();

        /// <summary>
        /// Cached diff when sig is unchanged; otherwise recomputes and caches.
        /// </summary>
        public List<T> GetOrCompute(string folder, string sig, Func<List<T>> recompute)
        {
            if (m_Entries.TryGetValue(folder, out var entry) && entry.sig == sig)
                return entry.diff;

            var diff = recompute();
            m_Entries[folder] = (sig, diff);
            return diff;
        }
    }
}
