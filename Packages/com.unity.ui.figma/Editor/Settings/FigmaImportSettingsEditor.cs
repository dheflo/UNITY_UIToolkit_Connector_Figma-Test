using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Inspector for FigmaImportSettings: a "Mapping" foldout (fonts, class prefix) plus Control Map
    /// rows that expand into ControlMappingDrawer. Also embedded by FigmaImportSettingsProvider.
    /// </summary>
    [CustomEditor(typeof(FigmaImportSettings))]
    internal class FigmaImportSettingsEditor : UnityEditor.Editor
    {
        const string k_Uss = "FigmaImportSettings.uss";

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var sheet = FigmaResources.LoadStyleSheet(k_Uss);
            if (sheet != null)
                root.styleSheets.Add(sheet);

            // The page title is drawn by the provider; keep just a subtitle here.
            var subtitle = new Label(FigmaStrings.SettingsSubtitle);
            subtitle.AddToClassList("figma-fs-subtitle");
            root.Add(subtitle);

            root.Add(new HelpBox(FigmaStrings.SettingsHelp, HelpBoxMessageType.Info));

            BuildMappingFoldout(root);
            BuildControlMap(root);
            return root;
        }

        void BuildMappingFoldout(VisualElement root)
        {
            var fold = new Foldout { text = FigmaStrings.MappingFoldout, value = true };
            fold.AddToClassList("figma-fs-fold");

            // A flex spacer pushes the "Global settings" hint to the far right of the foldout toggle.
            var hint = new Label(FigmaStrings.GlobalSettingsHint);
            hint.AddToClassList("figma-fold-hint");
            var toggle = fold.Q<Toggle>();
            var toggleText = toggle?.Q<Label>(null, "unity-toggle__text");
            if (toggleText?.parent != null)
            {
                var headerSpacer = new VisualElement();
                headerSpacer.AddToClassList("figma-spacer");
                toggleText.parent.Add(headerSpacer);
                toggleText.parent.Add(hint);
            }
            else
            {
                toggle?.Add(hint);
            }

            var fontProp = serializedObject.FindProperty("fontMap");
            var header = new VisualElement();
            header.AddToClassList("figma-headerrow");
            header.Add(new Label(FigmaStrings.FontMap) { tooltip = FigmaStrings.FontMapTooltip });
            var count = new Label();
            count.AddToClassList("figma-count");
            header.Add(count);
            var spacer = new VisualElement();
            spacer.AddToClassList("figma-spacer");
            header.Add(spacer);
            var addFont = new Button { text = FigmaStrings.AddFont };
            header.Add(addFont);

            var list = new VisualElement();
            list.AddToClassList("figma-inline-list");

            void RebuildFonts()
            {
                fontProp.serializedObject.Update();
                list.Clear();
                count.text = fontProp.arraySize == 0 ? string.Empty : FigmaStrings.FontCount(fontProp.arraySize);
                if (fontProp.arraySize == 0)
                {
                    var empty = new Label(FigmaStrings.ListIsEmpty);
                    empty.AddToClassList("figma-empty");
                    list.Add(empty);
                }
                for (var i = 0; i < fontProp.arraySize; ++i)
                {
                    var index = i;
                    var el = fontProp.GetArrayElementAtIndex(i);
                    var row = new VisualElement();
                    row.AddToClassList("figma-inline-row");

                    var family = new TextField { tooltip = FigmaStrings.FontFamilyTooltip };
                    family.BindProperty(el.FindPropertyRelative("family"));
                    row.Add(family);

                    var font = new ObjectField
                    {
                        objectType = typeof(UnityEngine.TextCore.Text.FontAsset),
                        allowSceneObjects = false,
                        tooltip = FigmaStrings.FontAssetTooltip
                    };
                    font.BindProperty(el.FindPropertyRelative("font"));
                    row.Add(font);

                    var remove = new Button(() => { DeleteElement(fontProp, index); RebuildFonts(); })
                        { text = "✕", tooltip = FigmaStrings.RemoveFont };
                    remove.AddToClassList("figma-icon-btn");
                    row.Add(remove);

                    list.Add(row);
                }
            }

            addFont.clicked += () =>
            {
                fontProp.serializedObject.Update();
                var n = fontProp.arraySize;
                fontProp.InsertArrayElementAtIndex(n);
                var el = fontProp.GetArrayElementAtIndex(n);
                el.FindPropertyRelative("family").stringValue = string.Empty;
                el.FindPropertyRelative("font").objectReferenceValue = null;
                fontProp.serializedObject.ApplyModifiedProperties();
                RebuildFonts();
            };

            var classPrefix = new TextField(FigmaStrings.ClassPrefix) { tooltip = FigmaStrings.ClassPrefixTooltip };
            classPrefix.BindProperty(serializedObject.FindProperty("classPrefix"));
            classPrefix.style.marginTop = 4;

            var body = new VisualElement();
            body.Add(header);
            body.Add(list);
            body.Add(classPrefix);
            fold.Add(body);
            root.Add(fold);

            RebuildFonts();

            // Bound fields refresh themselves on undo/redo, but the hand-built list structure doesn't.
            void OnUndoRedo() => RebuildFonts();
            fold.RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += OnUndoRedo);
            fold.RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= OnUndoRedo);
        }

        void BuildControlMap(VisualElement root)
        {
            var controlProp = serializedObject.FindProperty("controlMap");

            var header = new VisualElement();
            header.AddToClassList("figma-headerrow");
            header.style.marginTop = 4;
            var titleLabel = new Label(FigmaStrings.ControlMap);
            titleLabel.AddToClassList("figma-section-title");
            header.Add(titleLabel);
            var count = new Label();
            count.AddToClassList("figma-count");
            header.Add(count);
            var spacer = new VisualElement();
            spacer.AddToClassList("figma-spacer");
            header.Add(spacer);
            var expandAll = new Button { text = FigmaStrings.ExpandAll };
            var collapseAll = new Button { text = FigmaStrings.CollapseAll };
            var addMapping = new Button { text = FigmaStrings.AddMapping };
            header.Add(expandAll);
            header.Add(collapseAll);
            header.Add(addMapping);
            root.Add(header);

            var toolbar = new VisualElement();
            toolbar.AddToClassList("figma-toolbar");
            var search = new ToolbarSearchField { tooltip = FigmaStrings.SearchMappingsTooltip };
            toolbar.Add(search);
            var filter = new DropdownField(
                new List<string> { FigmaStrings.FilterAll, FigmaStrings.FilterControls, FigmaStrings.FilterTemplates }, 0)
                { tooltip = FigmaStrings.FilterMappingsTooltip };
            filter.style.width = 170;
            toolbar.Add(filter);
            root.Add(toolbar);

            var listEl = new VisualElement();
            listEl.AddToClassList("figma-cm-list");
            root.Add(listEl);

            var openRows = new HashSet<int>();

            // Summary badge text: the short type name, or "Template" when unmapped.
            string ControlLabel(SerializedProperty el)
            {
                var v = el.FindPropertyRelative("uitkControl").stringValue;
                if (string.IsNullOrEmpty(v) || string.Equals(v, "template", StringComparison.OrdinalIgnoreCase))
                    return FigmaStrings.Template;
                var dot = v.LastIndexOf('.');
                return dot >= 0 ? v.Substring(dot + 1) : v;
            }

            bool RowMatches(SerializedProperty el)
            {
                var ctrl = el.FindPropertyRelative("uitkControl").stringValue ?? string.Empty;
                var q = search.value?.Trim();
                if (!string.IsNullOrEmpty(q))
                {
                    var name = el.FindPropertyRelative("figmaName").stringValue ?? string.Empty;
                    if ((name + " " + ctrl).IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                        return false;
                }
                var template = string.IsNullOrEmpty(ctrl) || string.Equals(ctrl, "template", StringComparison.OrdinalIgnoreCase);
                switch (filter.index)
                {
                    case 1: return !template; // Controls
                    case 2: return template;  // Templates
                    default: return true;     // All
                }
            }

            void AddMapping()
            {
                serializedObject.Update();
                var n = controlProp.arraySize;
                controlProp.InsertArrayElementAtIndex(n);
                var el = controlProp.GetArrayElementAtIndex(n);
                el.FindPropertyRelative("figmaName").stringValue = string.Empty;
                el.FindPropertyRelative("uitkControl").stringValue = string.Empty;
                el.FindPropertyRelative("captionAttribute").stringValue = string.Empty;
                el.FindPropertyRelative("iconAttribute").stringValue = string.Empty;
                el.FindPropertyRelative("bindings").arraySize = 0;
                serializedObject.ApplyModifiedProperties();
                openRows.Add(n);
                BuildRows();
            }

            void DeleteMapping(int i)
            {
                serializedObject.Update();
                controlProp.DeleteArrayElementAtIndex(i);
                serializedObject.ApplyModifiedProperties();
                var shifted = new HashSet<int>();
                foreach (var o in openRows) { if (o != i) shifted.Add(o > i ? o - 1 : o); }
                openRows.Clear();
                openRows.UnionWith(shifted);
                BuildRows();
            }

            void BuildRows()
            {
                serializedObject.Update();
                listEl.Clear();
                var n = controlProp.arraySize;
                count.text = n == 0 ? string.Empty : FigmaStrings.MappingCount(n);

                var visible = 0;
                for (var i = 0; i < n; ++i)
                {
                    var index = i;
                    var el = controlProp.GetArrayElementAtIndex(i);
                    if (!RowMatches(el))
                        continue;
                    visible++;

                    var open = openRows.Contains(index);
                    var fold = new Foldout { value = open, text = " " };
                    fold.AddToClassList("figma-cm-row");

                    var toggle = fold.Q<Toggle>();
                    var nameLabel = toggle.Q<Label>(null, "unity-toggle__text");
                    nameLabel.AddToClassList("figma-cm-name");

                    var headerRow = nameLabel.parent;
                    var sSpacer = new VisualElement();
                    sSpacer.AddToClassList("figma-spacer");
                    headerRow.Add(sSpacer);

                    var badge = new Label();
                    badge.AddToClassList("figma-badge");
                    headerRow.Add(badge);

                    var propCount = new Label();
                    propCount.AddToClassList("figma-cm-count");
                    headerRow.Add(propCount);

                    // StopPropagation so clicking delete doesn't also toggle the foldout.
                    var remove = new Button(() => DeleteMapping(index)) { text = "✕", tooltip = FigmaStrings.Delete };
                    remove.AddToClassList("figma-icon-btn");
                    remove.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                    headerRow.Add(remove);

                    var built = false;
                    void EnsureBody()
                    {
                        if (built)
                            return;
                        built = true;
                        fold.Add(new PropertyField(controlProp.GetArrayElementAtIndex(index)));
                    }
                    if (open)
                        EnsureBody();

                    toggle.RegisterValueChangedCallback(evt =>
                    {
                        if (evt.newValue)
                        {
                            openRows.Add(index);
                            var wasBuilt = built;
                            EnsureBody();
                            if (!wasBuilt)
                                fold.Bind(serializedObject); // a PropertyField added after the initial bind needs its own Bind call
                        }
                        else
                        {
                            openRows.Remove(index);
                        }
                    });

                    void RefreshSummary()
                    {
                        var nm = el.FindPropertyRelative("figmaName").stringValue;
                        var inert = !string.IsNullOrEmpty(nm) && ControlMap.NormalizeName(nm) == null;
                        nameLabel.text = string.IsNullOrEmpty(nm) ? FigmaStrings.UntitledMapping
                            : inert ? FigmaStrings.MatchesNothing(nm) : nm;
                        nameLabel.tooltip = inert ? FigmaStrings.MatchesNothingTooltip : null;
                        badge.text = ControlLabel(el);
                        var nb = el.FindPropertyRelative("bindings").arraySize;
                        propCount.text = FigmaStrings.AttrCount(nb);
                    }
                    RefreshSummary();
                    // Live-update the summary labels as the expanded fields change; never rebuild rows here.
                    fold.TrackPropertyValue(el.FindPropertyRelative("figmaName"), _ => RefreshSummary());
                    fold.TrackPropertyValue(el.FindPropertyRelative("uitkControl"), _ => RefreshSummary());
                    fold.TrackPropertyValue(el.FindPropertyRelative("bindings"), _ => RefreshSummary());

                    listEl.Add(fold);
                }

                if (n == 0)
                {
                    var empty = new Label(FigmaStrings.NoMappingsYet);
                    empty.AddToClassList("figma-empty");
                    empty.style.whiteSpace = WhiteSpace.Normal;
                    listEl.Add(empty);
                }
                else if (visible == 0)
                {
                    var none = new Label(FigmaStrings.NoMappingsMatch);
                    none.AddToClassList("figma-empty");
                    listEl.Add(none);
                }

                // Activate the freshly-built trackers (and any open bodies) against the object.
                listEl.Bind(serializedObject);
            }

            expandAll.clicked += () =>
            {
                for (var i = 0; i < controlProp.arraySize; ++i) openRows.Add(i);
                BuildRows();
            };
            collapseAll.clicked += () => { openRows.Clear(); BuildRows(); };
            addMapping.clicked += AddMapping;
            search.RegisterValueChangedCallback(_ => BuildRows());
            filter.RegisterValueChangedCallback(_ => BuildRows());

            // Hand-built rows don't refresh on undo/redo; rebuild them from the reverted state.
            void OnUndoRedo() => BuildRows();
            listEl.RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += OnUndoRedo);
            listEl.RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= OnUndoRedo);

            BuildRows();
        }

        // FontMapping/ControlMapping are structs, so one delete removes the element (no object-reference double-delete).
        static void DeleteElement(SerializedProperty array, int index)
        {
            array.serializedObject.Update();
            if (index < 0 || index >= array.arraySize)
                return;
            array.DeleteArrayElementAtIndex(index);
            array.serializedObject.ApplyModifiedProperties();
        }
    }
}
