using System;
using System.Collections.Generic;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// One imported token, recorded in the manifest so a later tokens.uss edit diffs against the original.
    /// </summary>
    [Serializable]
    struct FigmaTokenRecord
    {
        public string name;  // sanitized "--token"
        public string id;    // Figma VariableID (write-back handle)
        public string type;  // COLOR | FLOAT
        public string value; // original USS value at import
    }

    /// <summary>
    /// A token whose value changed in Unity, to push back to its Figma Variable.
    /// </summary>
    [Serializable]
    struct FigmaTokenChange
    {
        public string id;
        public string name;
        public string type;
        public string value;   // the new USS value
        public string aliasId; // set when value is a var(--x) re-point: the target Variable's Figma id
        public string file;    // raw Figma file name (write-back scoping + ack key); stamped at scan, not by the pure diff
    }

    /// <summary>
    /// Pure token diff (no Unity, harness-testable): computes which tokens in an edited tokens.uss
    /// changed vs their imported originals.
    /// </summary>
    static class FigmaTokenDiff
    {
        public static List<FigmaTokenRecord> RecordsFrom(FigmaVariable[] variables)
        {
            var records = new List<FigmaTokenRecord>();
            if (variables == null)
                return records;

            var seen = new HashSet<string>();
            foreach (var v in variables)
            {
                var value = FigmaTokens.EmittedValue(v);
                if (string.IsNullOrEmpty(value))
                    continue;
                var name = FigmaTokens.SanitizeTokenName(v.name);
                if (!seen.Add(name))
                    continue;
                records.Add(new FigmaTokenRecord { name = name, id = v.id, type = v.type, value = value });
            }
            return records;
        }

        /// <summary>
        /// Parses the :root block into a name -> value map (tokens.uss is a single :root rule).
        /// </summary>
        public static Dictionary<string, string> ParseRoot(string uss)
        {
            var map = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(uss))
                return map;

            var open = uss.IndexOf('{');
            var close = open >= 0 ? uss.IndexOf('}', open + 1) : -1;
            if (open < 0 || close < 0)
                return map;

            foreach (var decl in uss.Substring(open + 1, close - open - 1).Split(';'))
            {
                var colon = decl.IndexOf(':');
                if (colon < 0)
                    continue;
                var name = decl.Substring(0, colon).Trim();
                var value = decl.Substring(colon + 1).Trim();
                if (name.StartsWith("--", StringComparison.Ordinal) && value.Length > 0)
                    map[name] = value;
            }
            return map;
        }

        /// <summary>
        /// Recorded tokens whose value in the edited tokens.uss differs from the imported original.
        /// </summary>
        public static List<FigmaTokenChange> Compute(List<FigmaTokenRecord> records, string currentUss)
        {
            var changes = new List<FigmaTokenChange>();
            if (records == null)
                return changes;

            var nameToId = new Dictionary<string, string>();
            foreach (var r in records)
                if (!string.IsNullOrEmpty(r.name) && !nameToId.ContainsKey(r.name))
                    nameToId[r.name] = r.id;

            var current = ParseRoot(currentUss);
            foreach (var record in records)
            {
                if (string.IsNullOrEmpty(record.name))
                    continue;
                if (!current.TryGetValue(record.name, out var value) || Normalize(value) == Normalize(record.value))
                    continue;

                var change = new FigmaTokenChange { id = record.id, name = record.name, type = record.type, value = value };
                // var(--target) is an alias re-point: resolve the target's Figma id so the plugin writes a
                // real VARIABLE_ALIAS; empty when the target isn't an imported token.
                if (TryVarTarget(value, out var target) && nameToId.TryGetValue(target, out var targetId))
                    change.aliasId = targetId;
                changes.Add(change);
            }
            return changes;
        }

        // "var(--target)" -> "--target"; false for a literal value.
        static bool TryVarTarget(string value, out string target)
        {
            target = null;
            if (string.IsNullOrEmpty(value))
                return false;
            var v = value.Trim();
            if (!v.StartsWith("var(", StringComparison.Ordinal))
                return false;
            var close = v.IndexOf(')', 4);
            if (close < 0)
                return false;
            target = v.Substring(4, close - 4).Trim();
            return target.Length > 0;
        }

        // Through FigmaCssValue, the same helper the property diff uses, so the two cannot drift:
        // "white" and "#FFFFFF" must never read as a designer edit.
        static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var bare = StripWhitespace(value);
            if (FigmaCssValue.TryColor(bare, out var color))
                return color;
            return FigmaCssValue.TryNumber(bare, out var number) ? number : bare;
        }

        static string StripWhitespace(string value)
        {
            var sb = new System.Text.StringBuilder(value.Length);
            foreach (var c in value)
                if (!char.IsWhiteSpace(c))
                    sb.Append(c);
            return sb.ToString();
        }
    }
}
