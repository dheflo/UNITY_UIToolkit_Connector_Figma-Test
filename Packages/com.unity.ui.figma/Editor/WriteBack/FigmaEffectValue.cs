using System.Text;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Canonical form of the effect properties that sync back: unitless numbers, rgba colors,
    /// space-separated functions. The diff compares through it and the plugin parses only it.
    /// </summary>
    static class FigmaEffectValue
    {
        public static bool IsEffectProp(string prop)
        {
            return prop == "text-shadow" || prop == "filter" || prop == "backdrop-filter";
        }

        /// <summary>Null when the value has no lossless Figma effect equivalent.</summary>
        public static string Canonical(string prop, string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;
            switch (prop)
            {
                case "text-shadow":
                    return FigmaToUss.TryParseShadow(value, out var shadow) ? Shadow(shadow) : null;
                case "filter":
                    return Filter(value, true);
                case "backdrop-filter":
                    return Filter(value, false);
            }
            return null;
        }

        static string Shadow(FigmaToUss.ShadowParts s)
        {
            if (s.inset || s.spread != null || s.blur[0] == '-'
                || !FigmaCssValue.TryNumber(s.x, out var x) || !FigmaCssValue.TryNumber(s.y, out var y)
                || !FigmaCssValue.TryNumber(s.blur, out var blur) || !FigmaCssValue.TryColor(s.color, out var color))
                return null;
            return x + " " + y + " " + blur + " " + color;
        }

        static string Filter(string value, bool layer)
        {
            if (value.Trim() == "none")
                return "none";
            var functions = FigmaToUss.SplitFilterFunctions(value);
            if (functions == null || functions.Count == 0 || (!layer && functions.Count > 1))
                return null;

            var sb = new StringBuilder();
            foreach (var (name, args) in functions)
            {
                string part = null;
                if (name == "blur" && FigmaCssValue.TryNumber(args, out var sigma) && sigma[0] != '-')
                    part = "blur(" + sigma + ")";
                else if (name == "drop-shadow" && layer && FigmaToUss.TryParseShadow(args, out var s))
                {
                    var shadow = Shadow(s);
                    part = shadow != null ? "drop-shadow(" + shadow + ")" : null;
                }
                if (part == null)
                    return null;
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(part);
            }
            return sb.ToString();
        }
    }
}
