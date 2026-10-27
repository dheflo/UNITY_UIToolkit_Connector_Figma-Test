namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Maps a Figma "State" variant value, or a prototype trigger name, to a UITK pseudo-class:
    /// ":hover" etc, "" for a resting state, null for unrecognized (skipped + reported).
    /// Pure so it runs in the golden harness.
    /// </summary>
    static class FigmaPseudoState
    {
        public static string Map(string state)
        {
            if (string.IsNullOrEmpty(state))
                return "";
            switch (state.Trim().ToLowerInvariant())
            {
                case "hover":
                case "while_hovering":
                case "on_hover":
                    return ":hover";
                case "pressed":
                case "active":
                case "press":
                case "down":
                case "while_pressing":
                case "on_press":
                    return ":active";
                case "disabled":
                    return ":disabled";
                case "selected":
                case "checked":
                case "on":
                    return ":checked";
                case "focused":
                case "focus":
                    return ":focus";
                case "default":
                case "normal":
                case "rest":
                case "enabled":
                case "off":
                    return "";
                default:
                    return null; // unknown state - the variant is skipped and reported
            }
        }
    }
}
