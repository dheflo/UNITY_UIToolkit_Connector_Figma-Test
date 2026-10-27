using System.IO;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Stages plugin-exported PNGs under Library/UIToolkitConnector/images/, keyed by the name the plugin sends so
    /// the two POST phases (images, then tree) line up. That name hashes the bytes, so identical artwork is
    /// one file. Library is unimported, so staging is plain thread-safe file IO.
    /// </summary>
    static class FigmaImageStore
    {
        static string s_ImagesDir;

        // Nothing prunes this folder, and a caller varying the key is unbounded. Count matters as much
        // as size: every staged file costs the next import a copy, an import and a load.
        const long k_MaxStagedTotalBytes = 256L * 1024 * 1024;
        const int k_MaxStagedFiles = 2000;

        // Cached, not recounted per call: rescanning would make staging N files cost N scans.
        static long s_TotalBytes = -1;
        static int s_FileCount;
        static readonly object k_Lock = new object();

        /// <summary>
        /// Set once from the main thread so the accept thread can stage without touching Unity APIs.
        /// </summary>
        public static void EnsureInitialized(string dataPath)
        {
            if (string.IsNullOrEmpty(s_ImagesDir) && !string.IsNullOrEmpty(dataPath))
                s_ImagesDir = Path.GetFullPath(Path.Combine(dataPath, "..", "Library", "UIToolkitConnector", "images"));
        }

        public static bool Stage(string key, byte[] bytes)
        {
            if (string.IsNullOrEmpty(key) || bytes == null || bytes.Length == 0 || string.IsNullOrEmpty(s_ImagesDir))
                return false;

            try
            {
                Directory.CreateDirectory(s_ImagesDir);
                var path = Path.Combine(s_ImagesDir, FigmaPathGuard.SanitizeName(key) + ".png");

                lock (k_Lock)
                {
                    EnsureCounted();
                    // A rewrite reclaims what the existing file held, so only the delta counts.
                    var existing = File.Exists(path) ? new FileInfo(path).Length : -1;
                    if (existing < 0 && s_FileCount >= k_MaxStagedFiles)
                        return false;
                    if (s_TotalBytes - (existing > 0 ? existing : 0) + bytes.Length > k_MaxStagedTotalBytes)
                        return false;

                    File.WriteAllBytes(path, bytes);
                    s_TotalBytes += bytes.Length - (existing > 0 ? existing : 0);
                    if (existing < 0)
                        s_FileCount++;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        static void EnsureCounted()
        {
            if (s_TotalBytes >= 0)
                return;

            s_TotalBytes = 0;
            s_FileCount = 0;
            foreach (var file in Directory.EnumerateFiles(s_ImagesDir, "*.png"))
            {
                s_TotalBytes += new FileInfo(file).Length;
                s_FileCount++;
            }
        }

        /// <summary>
        /// Absolute path of the PNG staged under a key, or null if none was uploaded.
        /// </summary>
        public static string StagedPath(string key)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(s_ImagesDir))
                return null;

            var path = Path.Combine(s_ImagesDir, FigmaPathGuard.SanitizeName(key) + ".png");
            return File.Exists(path) ? path : null;
        }
    }
}
