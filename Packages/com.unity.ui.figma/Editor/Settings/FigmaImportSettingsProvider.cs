using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Project Settings page "Project/UI Toolkit/Connector for Figma design": pick or create the active FigmaImportSettings
    /// (stored in FigmaMappingState) and edit it inline via the asset's own editor.
    /// </summary>
    static class FigmaImportSettingsProvider
    {
        internal const string k_Path = "Project/UI Toolkit/Connector for Figma design";

        // Inline editor for the active mapping, held so it can be destroyed when the page closes.
        static UnityEditor.Editor s_Editor;

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider(k_Path, SettingsScope.Project)
            {
                label = FigmaStrings.Name,
                keywords = Keywords(),
                activateHandler = (searchContext, root) => Build(root),
                deactivateHandler = Cleanup,
            };
        }

        static void Build(VisualElement root)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            root.Add(scroll);

            var page = new VisualElement();
            page.style.paddingLeft = 10;
            page.style.paddingRight = 10;
            scroll.Add(page);

            var common = EditorGUIUtility.Load("StyleSheets/ProjectSettings/ProjectSettingsCommon.uss") as StyleSheet;
            if (common != null)
                page.styleSheets.Add(common);

            var title = new Label(FigmaStrings.Name);
            title.AddToClassList("project-settings-title-bar__label");
            title.style.flexGrow = 0;
            title.style.marginBottom = 8;
            page.Add(title);

            var mappingField = new ObjectField(FigmaStrings.ActiveMappingField)
            {
                objectType = typeof(FigmaImportSettings),
                allowSceneObjects = false,
                tooltip = FigmaStrings.ActiveMappingFieldTooltip
            };
            page.Add(mappingField);

            var body = new VisualElement();
            page.Add(body);

            void Rebuild()
            {
                Cleanup();
                body.Clear();

                var active = FigmaImportSettings.Find();
                if (mappingField.value != active)
                    mappingField.SetValueWithoutNotify(active);

                if (active == null)
                {
                    body.Add(new HelpBox(FigmaStrings.NoMappingSelected, HelpBoxMessageType.Info));
                    body.Add(new Button(() =>
                    {
                        var created = FigmaImportSettings.CreateActiveAsset();
                        EditorGUIUtility.PingObject(created);
                        Rebuild();
                    }) { text = FigmaStrings.CreateMapping });
                    return;
                }

                s_Editor = UnityEditor.Editor.CreateEditor(active);
                var inspector = new InspectorElement(s_Editor);
                inspector.style.paddingLeft = 0;
                inspector.style.paddingRight = 0;
                body.Add(inspector);
            }

            mappingField.RegisterValueChangedCallback(evt =>
            {
                // Clearing the field is a choice, not an absence: SetActiveMapping(null) records it so
                // Find() does not resurrect the first asset it happens to see.
                FigmaMappingState.SetActiveMapping(evt.newValue as FigmaImportSettings);
                Rebuild();
            });

            Rebuild();
        }

        static void Cleanup()
        {
            if (s_Editor != null)
            {
                Object.DestroyImmediate(s_Editor);
                s_Editor = null;
            }
        }

        static HashSet<string> Keywords()
        {
            var keywords = new HashSet<string>(FigmaStrings.SettingsKeywords);
            var active = FigmaImportSettings.Find();
            if (active != null)
                keywords.UnionWith(SettingsProvider.GetSearchKeywordsFromSerializedObject(new SerializedObject(active)));
            return keywords;
        }
    }
}
