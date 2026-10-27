using System;
using System.IO;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Confines generated files to the import root with allowlisted extensions: a Figma node name must
    /// never escape the root (path traversal) or write an executable asset (e.g. a .cs).
    /// </summary>
    static class FigmaPathGuard
    {
        // Exactly what an import writes: documents, style sheets, rasterized fills, manifest sidecar.
        static readonly string[] k_AllowedExtensions = { ".uxml", ".uss", ".png", ".json" };

        /// <summary>
        /// True (with the absolute path) when relativePath resolves under importRoot with an allowed extension.
        /// </summary>
        public static bool TryResolve(string importRoot, string relativePath, out string fullPath)
        {
            fullPath = null;

            if (string.IsNullOrEmpty(importRoot) || string.IsNullOrEmpty(relativePath))
                return false;

            if (Path.IsPathRooted(relativePath))
                return false;

            var rootFull = Path.GetFullPath(importRoot);
            var candidate = Path.GetFullPath(Path.Combine(rootFull, relativePath));

            var rootWithSeparator = rootFull.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? rootFull
                : rootFull + Path.DirectorySeparatorChar;

            if (!candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal))
                return false;

            var extension = Path.GetExtension(candidate).ToLowerInvariant();
            if (Array.IndexOf(k_AllowedExtensions, extension) < 0)
                return false;

            if (CrossesLink(rootWithSeparator, candidate))
                return false;

            fullPath = candidate;
            return true;
        }

        // The containment check above is textual, so a link under the root would pass while pointing
        // outside it. The root itself is exempt: relocating it is the user's own choice.
        static bool CrossesLink(string rootWithSeparator, string candidate)
        {
            try
            {
                for (var current = Path.GetDirectoryName(candidate);
                     !string.IsNullOrEmpty(current) && current.Length >= rootWithSeparator.Length;
                     current = Path.GetDirectoryName(current))
                {
                    var info = new DirectoryInfo(current);
                    if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0)
                        return true;
                }

                var file = new FileInfo(candidate);
                return file.Exists && (file.Attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch (Exception)
            {
                return true; // unreadable attributes cannot be vouched for
            }
        }

        /// <summary>
        /// Reduces an arbitrary Figma node name to a safe file-name fragment.
        /// </summary>
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "node";

            var chars = name.ToCharArray();
            for (var i = 0; i < chars.Length; ++i)
            {
                var c = chars[i];
                var allowed = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!allowed)
                    chars[i] = '-';
            }

            var result = new string(chars).Trim('-');
            return result.Length == 0 ? "node" : result;
        }
    }
}
