using System;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// The single canonicalizer both write-back diffs compare through. Two spellings of one value must
    /// never read as an edit, or the connector pushes a phantom change into the designer's Figma file.
    /// </summary>
    static class FigmaCssValue
    {
        static readonly Dictionary<string, string> k_NamedColors = BuildNamedColors();

        /// <summary>
        /// Named / #hex / rgb() / rgba() -> "rgba(r,g,b,a)" with 0-255 channels and 0-1 alpha. False when
        /// the value is not a color.
        /// </summary>
        public static bool TryColor(string value, out string canonical)
        {
            canonical = null;
            if (string.IsNullOrEmpty(value))
                return false;

            var v = value.Trim();
            if (k_NamedColors.TryGetValue(v.ToLowerInvariant(), out var named))
                v = named;

            if (v.StartsWith("#", StringComparison.Ordinal))
                return TryHexColor(v, out canonical);
            if (v.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
                return TryFunctionColor(v, out canonical);
            return false;
        }

        /// <summary>
        /// A scalar with an optional px suffix -> a trimmed number, so 16, 16px and 16.00px all match.
        /// </summary>
        public static bool TryNumber(string value, out string canonical)
        {
            canonical = null;
            if (string.IsNullOrEmpty(value))
                return false;

            var body = value.Trim();
            if (body.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                body = body.Substring(0, body.Length - 2);
            if (body.Length == 0
                || !double.TryParse(body, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                return false;

            canonical = Math.Round(n, 4).ToString("0.####", CultureInfo.InvariantCulture);
            return true;
        }

        static bool TryHexColor(string v, out string canonical)
        {
            canonical = null;
            var h = v.Substring(1);
            if (h.Length == 3)
                h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
            if (h.Length != 6 && h.Length != 8)
                return false;

            if (!TryHexPair(h, 0, out var r) || !TryHexPair(h, 2, out var g) || !TryHexPair(h, 4, out var b))
                return false;
            double a = 1;
            if (h.Length == 8)
            {
                if (!TryHexPair(h, 6, out var alpha))
                    return false;
                a = alpha / 255.0;
            }

            canonical = Format(r, g, b, a);
            return true;
        }

        static bool TryFunctionColor(string v, out string canonical)
        {
            canonical = null;
            int open = v.IndexOf('('), close = v.LastIndexOf(')');
            if (open < 0 || close <= open)
                return false;

            var parts = v.Substring(open + 1, close - open - 1).Split(',');
            if (parts.Length < 3 || parts.Length > 4)
                return false;
            if (!TryChannel(parts[0], out var r) || !TryChannel(parts[1], out var g) || !TryChannel(parts[2], out var b))
                return false;

            double a = 1;
            if (parts.Length == 4)
            {
                var alpha = parts[3].Trim();
                var percent = alpha.EndsWith("%", StringComparison.Ordinal);
                if (percent)
                    alpha = alpha.Substring(0, alpha.Length - 1);
                if (!double.TryParse(alpha, NumberStyles.Float, CultureInfo.InvariantCulture, out a))
                    return false;
                if (percent)
                    a /= 100.0;
                else if (a > 1.0)
                    a /= 255.0; // Figma sometimes reports alpha on the 0-255 scale
            }

            canonical = Format(r, g, b, a);
            return true;
        }

        static string Format(double r, double g, double b, double a)
        {
            // Snap alpha to the 1/255 grid first: 0.5 and the 8-digit hex 80 are the same colour, and only
            // quantizing to 3 decimals would split them into a phantom edit.
            a = Math.Round(a * 255.0) / 255.0;
            return "rgba(" + (int)Math.Round(r) + "," + (int)Math.Round(g) + "," + (int)Math.Round(b) + ","
                + Math.Round(a, 3).ToString("0.###", CultureInfo.InvariantCulture) + ")";
        }

        // An rgb() channel: 0-255, or a percentage.
        static bool TryChannel(string text, out double value)
        {
            value = 0;
            if (text == null)
                return false;
            var t = text.Trim();
            var percent = t.EndsWith("%", StringComparison.Ordinal);
            if (percent)
                t = t.Substring(0, t.Length - 1);
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                return false;
            value = percent ? n * 2.55 : n;
            return true;
        }

        static bool TryHexPair(string h, int i, out int value)
        {
            return int.TryParse(h.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        static Dictionary<string, string> BuildNamedColors()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            map["transparent"] = "rgba(0,0,0,0)";
            map["black"] = "#000000";
            map["silver"] = "#C0C0C0";
            map["gray"] = "#808080";
            map["grey"] = "#808080";
            map["white"] = "#FFFFFF";
            map["maroon"] = "#800000";
            map["red"] = "#FF0000";
            map["purple"] = "#800080";
            map["fuchsia"] = "#FF00FF";
            map["magenta"] = "#FF00FF";
            map["green"] = "#008000";
            map["lime"] = "#00FF00";
            map["olive"] = "#808000";
            map["yellow"] = "#FFFF00";
            map["navy"] = "#000080";
            map["blue"] = "#0000FF";
            map["teal"] = "#008080";
            map["aqua"] = "#00FFFF";
            map["cyan"] = "#00FFFF";
            map["orange"] = "#FFA500";
            map["pink"] = "#FFC0CB";
            map["brown"] = "#A52A2A";
            map["gold"] = "#FFD700";
            map["indigo"] = "#4B0082";
            map["violet"] = "#EE82EE";
            map["beige"] = "#F5F5DC";
            map["ivory"] = "#FFFFF0";
            map["khaki"] = "#F0E68C";
            map["salmon"] = "#FA8072";
            map["turquoise"] = "#40E0D0";
            map["tan"] = "#D2B48C";
            map["plum"] = "#DDA0DD";
            map["orchid"] = "#DA70D6";
            map["crimson"] = "#DC143C";
            map["coral"] = "#FF7F50";
            return map;
        }
    }
}
