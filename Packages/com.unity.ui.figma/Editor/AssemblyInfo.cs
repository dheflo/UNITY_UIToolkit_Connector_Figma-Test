using System.Runtime.CompilerServices;

// The package's types are internal by default; the EditMode suite needs to see them.
[assembly: InternalsVisibleTo("Unity.UIToolkit.Figma.Editor.Tests")]

// Registers this assembly's .po files under its name, the group FigmaStrings translates against.
[assembly: UnityEditor.Localization]
