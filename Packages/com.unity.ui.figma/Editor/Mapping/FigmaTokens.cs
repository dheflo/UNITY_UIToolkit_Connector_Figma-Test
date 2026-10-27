using System.Globalization;
using System.Collections.Generic;
using System.Text;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Figma Variables -> tokens.uss (:root custom properties). Owns the token name sanitizer,
    /// shared with FigmaToUss's var() refs so definitions and references agree. Pure, harness-safe.
    /// </summary>
    static class FigmaTokens
    {
        /// <summary>
        /// Distinct Figma variable names that sanitize to the same USS custom property, grouped by that
        /// property. Only the first of each group is emitted, so the rest silently resolve to its value.
        /// </summary>
        public static Dictionary<string, List<string>> Collisions(FigmaVariable[] variables)
        {
            var byToken = new Dictionary<string, List<string>>();
            if (variables == null)
                return byToken;

            foreach (var variable in variables)
            {
                if (string.IsNullOrEmpty(EmittedValue(variable)))
                    continue;
                var name = SanitizeTokenName(variable.name);
                if (!byToken.TryGetValue(name, out var raws))
                    byToken[name] = raws = new List<string>();
                if (!raws.Contains(variable.name))
                    raws.Add(variable.name);
            }

            var collisions = new Dictionary<string, List<string>>();
            foreach (var kv in byToken)
                if (kv.Value.Count > 1)
                    collisions[kv.Key] = kv.Value;
            return collisions;
        }

        /// <summary>
        /// Raw Figma variable name ("color/primary") -> "--sanitized-token": lowercased, with
        /// non-alphanumeric runs collapsed to one dash.
        /// </summary>
        public static string SanitizeTokenName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName))
                return "--token";

            var sb = new StringBuilder("--");
            var lastDash = false;
            foreach (var ch in rawName.Trim())
            {
                var c = char.ToLowerInvariant(ch);
                var alnum = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
                if (alnum)
                {
                    sb.Append(c);
                    lastDash = false;
                }
                else if (!lastDash)
                {
                    sb.Append('-');
                    lastDash = true;
                }
            }

            var result = sb.ToString().TrimEnd('-');
            return result.Length <= 2 ? "--token" : result; // "--" alone -> fallback
        }

        static float Round(float value, float scale)
            => (float)System.Math.Floor(value * scale + 0.5f) / scale;

        static string Rgba(FigmaColor c)
            => "rgba(" + (int)System.Math.Floor(c.r * 255f + 0.5f)
             + ", " + (int)System.Math.Floor(c.g * 255f + 0.5f)
             + ", " + (int)System.Math.Floor(c.b * 255f + 0.5f)
             + ", " + Round(c.a, 1000f).ToString(CultureInfo.InvariantCulture) + ")";

        /// <summary>
        /// The USS value for a variable: var() when it aliases another, else the literal.
        /// BuildTokensUss and FigmaTokenDiff.RecordsFrom must agree on this or unedited tokens read as changes.
        /// </summary>
        public static string EmittedValue(FigmaVariable variable)
        {
            if (!string.IsNullOrEmpty(variable.aliasOf))
                return "var(" + SanitizeTokenName(variable.aliasOf) + ")";
            if (variable.hasColor)
                return Rgba(variable.color);
            // Figma floats are overwhelmingly px: spacing, size, radius. Unitless would not parse as a length.
            if (variable.hasNumber)
                return Round(variable.number, 1000f).ToString(CultureInfo.InvariantCulture) + "px";
            // The only path that reaches USS verbatim: the alias, color and number forms are all built here.
            var literal = variable.value?.Trim() ?? string.Empty;
            return FigmaToUss.IsSafeValue(literal) ? literal : string.Empty;
        }

        /// <summary>
        /// A :root rule defining every COLOR/FLOAT variable, or empty when there are none.
        /// </summary>
        public static string BuildTokensUss(FigmaVariable[] variables)
        {
            if (variables == null || variables.Length == 0)
                return string.Empty;

            var sb = new StringBuilder(":root {\n");
            var seen = new HashSet<string>();
            var count = 0;
            foreach (var variable in variables)
            {
                var value = EmittedValue(variable);
                if (string.IsNullOrEmpty(value))
                    continue;
                var name = SanitizeTokenName(variable.name);
                if (!seen.Add(name))
                    continue; // two Figma names collapsed to one token - keep the first
                sb.Append("    ").Append(name).Append(": ").Append(value).Append(";\n");
                count++;
            }
            sb.Append("}\n");

            return count == 0 ? string.Empty : sb.ToString();
        }

        /// <summary>
        /// One mode's :root rule (e.g. dark.uss), via BuildTokensUss so sanitizing/alias/dedup
        /// match tokens.uss. Empty when no variable has this mode.
        /// </summary>
        public static string BuildModeUss(FigmaModeVariable[] modeVars, string modeName)
        {
            if (modeVars == null || modeVars.Length == 0)
                return string.Empty;

            var projected = new List<FigmaVariable>();
            foreach (var mv in modeVars)
            {
                if (mv.values == null)
                    continue;
                foreach (var vm in mv.values)
                {
                    if (vm.mode != modeName)
                        continue;
                    projected.Add(new FigmaVariable
                    {
                        id = mv.id, name = mv.name, type = mv.type, value = vm.value, aliasOf = vm.aliasOf,
                        hasColor = vm.hasColor, color = vm.color, hasNumber = vm.hasNumber, number = vm.number
                    });
                    break;
                }
            }

            return BuildTokensUss(projected.ToArray());
        }

        /// <summary>
        /// Mode name -> stylesheet file name, e.g. "Dark" -> "dark.uss".
        /// </summary>
        public static string ModeFileName(string modeName)
        {
            var slug = SanitizeTokenName(modeName).TrimStart('-');
            return (string.IsNullOrEmpty(slug) ? "mode" : slug) + ".uss";
        }
    }
}
