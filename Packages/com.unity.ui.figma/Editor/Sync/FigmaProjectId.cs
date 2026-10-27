using System.Security.Cryptography;
using System.Text;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// A stable, non-reversible id for a project, so a plugin can tell two editors apart and refuse to
    /// send one editor's token to another.
    /// </summary>
    static class FigmaProjectId
    {
        public static string For(string projectRoot)
        {
            using (var sha = SHA1.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(projectRoot ?? string.Empty));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                    sb.Append(b.ToString("x2"));
                return sb.ToString(0, 16);
            }
        }
    }
}
