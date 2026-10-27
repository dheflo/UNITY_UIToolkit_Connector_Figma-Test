using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// Drawer for one Control Map entry: pick the target control (discovered via ControlCatalog)
    /// and its attribute bindings.
    /// </summary>
    [CustomPropertyDrawer(typeof(ControlMapping))]
    class ControlMappingDrawer : PropertyDrawer
    {
        static readonly string k_None = FigmaStrings.None;
        static readonly string k_Custom = FigmaStrings.CustomType;
        static readonly string k_SrcFixed = FigmaStrings.SourceFixedValue;
        static readonly string k_SrcProperty = FigmaStrings.SourceComponentProperty;
        static readonly string k_SrcDesignText = FigmaStrings.SourceDesignText;
        static readonly string k_SrcDesignIcon = FigmaStrings.SourceDesignIcon;
        static readonly string k_OtherName = FigmaStrings.OtherName;

        // Discovered-control catalog, built once per domain reload; see EnsureCatalog.
        static List<string> s_Choices;
        static Dictionary<string, string> s_ChoiceToStored;
        static Dictionary<string, string> s_StoredToChoice;
        static Dictionary<string, string> s_ByBareTag;
        static bool s_CatalogCached;

        // Inherited VisualElement attributes, hidden from the properties preview.
        static readonly HashSet<string> k_BaseAttrs = new HashSet<string>(new[]
        {
            "name", "tooltip", "enabled", "focusable", "picking-mode", "tabindex", "usage-hints",
            "view-data-key", "binding-path", "data-source", "data-source-path", "data-source-type",
            "language-direction"
        });

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();

            var control = property.FindPropertyRelative("uitkControl");
            EnsureCatalog();

            var nameField = new TextField();
            nameField.BindProperty(property.FindPropertyRelative("figmaName"));

            var dropdown = new DropdownField(s_Choices, 0) { tooltip = FigmaStrings.ControlTooltip };
            dropdown.formatSelectedValueCallback = c =>
            {
                if (c == k_None || c == k_Custom) return c;
                var slash = c.LastIndexOf('/');
                return slash >= 0 ? c.Substring(slash + 1) : c;
            };

            var fieldsRow = new VisualElement();
            fieldsRow.style.flexDirection = FlexDirection.Row;
            fieldsRow.style.flexWrap = Wrap.Wrap;
            fieldsRow.style.alignItems = Align.FlexStart;
            fieldsRow.style.marginBottom = 10;
            fieldsRow.Add(FieldCell(FigmaStrings.FigmaName, 2f, nameField, 0));
            var controlCell = FieldCell(FigmaStrings.Control, 0f, dropdown, 170);
            fieldsRow.Add(controlCell);

            var raw = new TextField(FigmaStrings.CustomTypeField)
            {
                tooltip = FigmaStrings.CustomTypeTooltip,
                isDelayed = true
            };
            var rawWarn = new Label(FigmaStrings.GlobalNamespaceWarning);
            rawWarn.AddToClassList("figma-empty");
            rawWarn.style.display = DisplayStyle.None;
            rawWarn.style.whiteSpace = WhiteSpace.Normal;
            var bindingsProp = property.FindPropertyRelative("bindings");
            var propLabel = new Label(FigmaStrings.Attributes);
            propLabel.AddToClassList("figma-prop-title");
            var bindingsBox = new VisualElement();
            var addButton = new Button { text = FigmaStrings.AddAttribute };
            addButton.style.alignSelf = Align.FlexStart;
            addButton.style.marginTop = 6;

            string FullNameOf(string stored)
            {
                if (string.IsNullOrEmpty(stored))
                    return string.Empty;
                if (stored.Contains("."))
                    return stored;
                // A bare catalog name is an engine control; any other bare name is a global-namespace
                // type, whose discovered full name is the bare name itself.
                return ControlMap.CanonicalCatalogTag(stored) != null
                    ? "UnityEngine.UIElements." + stored
                    : stored;
            }

            void Write(string value)
            {
                control.stringValue = value;
                control.serializedObject.ApplyModifiedProperties();
            }

            void Sync()
            {
                var v = control.stringValue;
                var template = string.IsNullOrEmpty(v) || string.Equals(v, "template", StringComparison.OrdinalIgnoreCase);
                if (template)
                {
                    dropdown.SetValueWithoutNotify(k_None);
                    raw.style.display = DisplayStyle.None;
                    return;
                }
                if (s_StoredToChoice.TryGetValue(v, out var choice))
                {
                    dropdown.SetValueWithoutNotify(choice);
                    raw.style.display = DisplayStyle.None;
                    rawWarn.style.display = DisplayStyle.None;
                }
                else
                {
                    // Not discovered (assembly not loaded, or hand-entered) - fall back to Custom.
                    dropdown.SetValueWithoutNotify(k_Custom);
                    raw.style.display = DisplayStyle.Flex;
                    raw.SetValueWithoutNotify(v);
                    // A bare non-catalog name imports as a global-namespace element; say so here.
                    rawWarn.style.display = !v.Contains(".") && CanonicalizeTypedControl(v) == null
                        ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }

            // Deferred: a pick clears the very box that fired the callback.
            void QueueRebuild() => bindingsBox.schedule.Execute(() => RebuildBindings());

            void WriteAttr(int index, string value)
            {
                bindingsProp.serializedObject.Update();
                bindingsProp.GetArrayElementAtIndex(index).FindPropertyRelative("uxmlAttribute").stringValue = value;
                bindingsProp.serializedObject.ApplyModifiedProperties();
            }

            // Clears a row's value so it re-seeds to the new attribute's type (a stale old-type value would emit).
            void ResetValue(int index)
            {
                bindingsProp.serializedObject.Update();
                bindingsProp.GetArrayElementAtIndex(index).FindPropertyRelative("value").stringValue = string.Empty;
                bindingsProp.serializedObject.ApplyModifiedProperties();
            }

            bool HasDesignRow(FigmaBindingSource source, int exceptIndex)
            {
                for (var i = 0; i < bindingsProp.arraySize; ++i)
                {
                    if (i == exceptIndex)
                        continue;
                    if (bindingsProp.GetArrayElementAtIndex(i).FindPropertyRelative("source").intValue == (int)source)
                        return true;
                }
                return false;
            }

            void RebuildBindings()
            {
                bindingsProp.serializedObject.Update();
                bindingsBox.Clear();

                var all = MappableAttrs(FullNameOf(control.stringValue));

                var taken = new HashSet<string>();
                for (var i = 0; i < bindingsProp.arraySize; ++i)
                {
                    var a = bindingsProp.GetArrayElementAtIndex(i).FindPropertyRelative("uxmlAttribute").stringValue;
                    if (!string.IsNullOrEmpty(a))
                        taken.Add(a);
                }

                // A template emits no attributes, so hide the section; keep it visible while rows exist
                // so they can still be removed.
                var target = control.stringValue;
                var isTemplate = string.IsNullOrEmpty(target)
                    || string.Equals(target, "template", StringComparison.OrdinalIgnoreCase);
                var showProps = !isTemplate || bindingsProp.arraySize > 0;
                propLabel.style.display = showProps ? DisplayStyle.Flex : DisplayStyle.None;
                bindingsBox.style.display = showProps ? DisplayStyle.Flex : DisplayStyle.None;
                addButton.style.display = isTemplate ? DisplayStyle.None : DisplayStyle.Flex;

                if (isTemplate && bindingsProp.arraySize > 0)
                {
                    var warn = new Label(FigmaStrings.TemplateEmitsNoAttributes);
                    warn.AddToClassList("figma-empty");
                    bindingsBox.Add(warn);
                }

                if (bindingsProp.arraySize == 0)
                {
                    var none = new Label(FigmaStrings.NoAttributesYet);
                    none.AddToClassList("figma-empty");
                    bindingsBox.Add(none);
                }
                else
                {
                    bindingsBox.Add(PropHeader());
                    for (var i = 0; i < bindingsProp.arraySize; ++i)
                        bindingsBox.Add(BuildRow(i, all, taken));
                }

                propLabel.text = bindingsProp.arraySize > 0
                    ? FigmaStrings.AttributesCount(bindingsProp.arraySize) : FigmaStrings.Attributes;

                // Lock the control while properties exist - switching it would strand the mapped attributes.
                var hasProps = bindingsProp.arraySize > 0;
                dropdown.SetEnabled(!hasProps);
                raw.SetEnabled(!hasProps);
                var lockTip = hasProps ? FigmaStrings.ControlLockedTooltip : null;
                dropdown.tooltip = lockTip ?? FigmaStrings.ControlTooltip;
                controlCell.tooltip = lockTip;
            }

            VisualElement BuildRow(int index, List<ControlAttrInfo> all, HashSet<string> taken)
            {
                var el = bindingsProp.GetArrayElementAtIndex(index);
                var attrP = el.FindPropertyRelative("uxmlAttribute");
                var srcP = el.FindPropertyRelative("source");
                var valP = el.FindPropertyRelative("value");
                var current = attrP.stringValue ?? string.Empty;

                // Choices: schema attrs not taken by other rows, plus this row's own and "Custom".
                var rowChoices = new List<string>();
                var hasSchema = false;
                Type attrType = null;
                foreach (var a in all)
                {
                    hasSchema = true;
                    if (a.name == current)
                        attrType = a.type;
                    if (a.name == current || !taken.Contains(a.name))
                        rowChoices.Add(a.name);
                }
                var currentIsSchema = rowChoices.Contains(current);
                rowChoices.Add(k_Custom);

                // Sources this attribute can take. Design rows are one per mapping; no Figma property
                // type carries an image, so an asset-typed attribute never offers Component Property.
                var assetTyped = IsAssetType(attrType);
                var stringLike = attrType == null || attrType == typeof(string);
                var sourceChoices = new List<string> { k_SrcFixed };
                if (!assetTyped)
                    sourceChoices.Add(k_SrcProperty);
                var source = (FigmaBindingSource)srcP.intValue;
                if (stringLike && !assetTyped
                    && (source == FigmaBindingSource.DesignText || !HasDesignRow(FigmaBindingSource.DesignText, index)))
                    sourceChoices.Add(k_SrcDesignText);
                if ((assetTyped || attrType == null)
                    && (source == FigmaBindingSource.DesignIcon || !HasDesignRow(FigmaBindingSource.DesignIcon, index)))
                    sourceChoices.Add(k_SrcDesignIcon);

                if (!sourceChoices.Contains(SourceLabel(source)))
                {
                    srcP.intValue = (int)FigmaBindingSource.FixedValue;
                    srcP.serializedObject.ApplyModifiedProperties();
                    source = FigmaBindingSource.FixedValue;
                }
                var fromProperty = source == FigmaBindingSource.ComponentProperty;
                var fromDesign = source == FigmaBindingSource.DesignText || source == FigmaBindingSource.DesignIcon;

                var row = new VisualElement();
                row.AddToClassList("figma-prop-row");
                if ((index & 1) == 1)
                    row.AddToClassList("figma-prop-row--alt");

                var attrCell = PropCell(1.5f);
                var attrDrop = new DropdownField(rowChoices, 0);
                var attrRaw = new TextField { tooltip = FigmaStrings.AttributeNameTooltip, isDelayed = true };
                attrRaw.style.display = DisplayStyle.None;
                attrRaw.style.marginTop = 3;
                if (!hasSchema || !currentIsSchema)
                {
                    attrDrop.SetValueWithoutNotify(k_Custom);
                    attrRaw.style.display = DisplayStyle.Flex;
                    attrRaw.SetValueWithoutNotify(current);
                }
                else
                {
                    attrDrop.SetValueWithoutNotify(current);
                }
                attrCell.Add(attrDrop);
                attrCell.Add(attrRaw);

                // A property row's value is the Figma property name; a Design row has no value of its
                // own; a constant row gets a typed editor.
                VisualElement valField;
                if (fromProperty)
                {
                    valField = PropertyNameField(valP, attrType,
                        property.FindPropertyRelative("figmaName").stringValue);
                }
                else if (fromDesign)
                {
                    var hint = new Label(source == FigmaBindingSource.DesignText
                        ? FigmaStrings.AbsorbedTextLayer : FigmaStrings.ExportedIconLayer);
                    hint.AddToClassList("figma-empty");
                    valField = hint;
                }
                else
                {
                    valField = BuildValueField(valP, attrType);
                }

                var srcField = new DropdownField(sourceChoices, 0);
                srcField.SetValueWithoutNotify(SourceLabel(source));
                if (sourceChoices.Count == 1)
                {
                    srcField.tooltip = FigmaStrings.SourceLockedTooltip;
                    srcField.SetEnabled(false);
                }
                else
                {
                    srcField.RegisterValueChangedCallback(evt =>
                    {
                        srcP.intValue = (int)SourceFromLabel(evt.newValue);
                        srcP.serializedObject.ApplyModifiedProperties();
                        ResetValue(index);
                        QueueRebuild(); // the Value editor's shape depends on the source
                    });
                }

                attrDrop.RegisterValueChangedCallback(evt =>
                {
                    attrRaw.style.display = evt.newValue == k_Custom ? DisplayStyle.Flex : DisplayStyle.None;
                    WriteAttr(index, evt.newValue == k_Custom ? attrRaw.value : evt.newValue);
                    ResetValue(index);
                    QueueRebuild(); // the attribute-list dedup and the Value field's type depend on the picked attribute
                });
                attrRaw.RegisterValueChangedCallback(evt =>
                {
                    WriteAttr(index, evt.newValue);
                    ResetValue(index);
                    QueueRebuild();
                });

                var srcCell = PropCell(1f);
                srcCell.Add(srcField);

                // Center the short bool checkbox.
                var valCell = PropCell(1.3f);
                if (!fromProperty && attrType == typeof(bool))
                {
                    valCell.style.alignSelf = Align.Stretch;
                    valCell.style.justifyContent = Justify.Center;
                }
                valCell.Add(valField);

                var removeCell = new VisualElement();
                removeCell.AddToClassList("figma-cell-fixed");
                removeCell.style.alignItems = Align.Center;
                var remove = new Button(() => RemoveBinding(index)) { text = "✕", tooltip = FigmaStrings.RemoveAttribute };
                remove.AddToClassList("figma-icon-btn");
                remove.style.marginLeft = 0;
                remove.style.width = 18;
                remove.style.height = 18;
                remove.style.fontSize = 12;
                removeCell.Add(remove);

                row.Add(attrCell);
                row.Add(srcCell);
                row.Add(valCell);
                row.Add(removeCell);
                return row;
            }

            // Value editor matched to the attribute's type, read/written as valP.stringValue in UXML format.
            // A fresh typed row is seeded with the shown default so it emits without needing a touch.
            VisualElement BuildValueField(SerializedProperty valP, Type attrType)
            {
                var stored = valP.stringValue ?? string.Empty;
                void Set(string v) { valP.stringValue = v; valP.serializedObject.ApplyModifiedProperties(); }

                if (IsAssetType(attrType))
                {
                    // Background is a struct fed by image assets; an Object-derived attribute filters to
                    // its own type, so a package control's asset attributes take exactly what they declare.
                    var background = attrType == typeof(Background);
                    var f = new ObjectField
                    {
                        objectType = background ? typeof(UnityEngine.Object) : attrType,
                        allowSceneObjects = false,
                        tooltip = background ? FigmaStrings.ImageAssetTooltip : FigmaStrings.AssetTooltip(attrType.Name)
                    };
                    f.SetValueWithoutNotify(AssetFromUrl(stored));
                    f.RegisterValueChangedCallback(e =>
                    {
                        if (background && e.newValue != null && !(e.newValue is UnityEngine.Texture
                            || e.newValue is UnityEngine.Sprite || e.newValue is VectorImage))
                        {
                            f.SetValueWithoutNotify(e.previousValue);
                            return;
                        }
                        Set(UrlFromAsset(e.newValue));
                    });
                    return f;
                }
                if (attrType == typeof(bool))
                {
                    bool.TryParse(stored, out var b);
                    if (string.IsNullOrEmpty(stored)) Set(b ? "true" : "false");
                    var f = new Toggle();
                    f.SetValueWithoutNotify(b);
                    f.RegisterValueChangedCallback(e => Set(e.newValue ? "true" : "false"));
                    return f;
                }
                if (attrType == typeof(UnityEngine.Color))
                {
                    UnityEngine.ColorUtility.TryParseHtmlString(stored, out var c);
                    if (string.IsNullOrEmpty(stored))
                    {
                        c = UnityEngine.Color.white;
                        Set("#" + UnityEngine.ColorUtility.ToHtmlStringRGBA(c));
                    }
                    var f = new ColorField();
                    f.SetValueWithoutNotify(c);
                    f.RegisterValueChangedCallback(e => Set("#" + UnityEngine.ColorUtility.ToHtmlStringRGBA(e.newValue)));
                    return f;
                }
                if (attrType == typeof(int) || attrType == typeof(uint) || attrType == typeof(long)
                    || attrType == typeof(short) || attrType == typeof(byte))
                {
                    int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i);
                    if (string.IsNullOrEmpty(stored)) Set(i.ToString(CultureInfo.InvariantCulture));
                    var f = new IntegerField();
                    f.SetValueWithoutNotify(i);
                    f.RegisterValueChangedCallback(e => Set(e.newValue.ToString(CultureInfo.InvariantCulture)));
                    return f;
                }
                if (attrType == typeof(float) || attrType == typeof(double))
                {
                    float.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out var fl);
                    if (string.IsNullOrEmpty(stored)) Set(fl.ToString(CultureInfo.InvariantCulture));
                    var f = new FloatField();
                    f.SetValueWithoutNotify(fl);
                    f.RegisterValueChangedCallback(e => Set(e.newValue.ToString(CultureInfo.InvariantCulture)));
                    return f;
                }
                if (attrType != null && attrType.IsEnum)
                {
                    Enum def;
                    try { def = (Enum)Enum.Parse(attrType, stored, true); }
                    catch { def = (Enum)Enum.GetValues(attrType).GetValue(0); }
                    if (string.IsNullOrEmpty(stored)) Set(def.ToString());
                    var f = new EnumField(def);
                    f.RegisterValueChangedCallback(e => Set(e.newValue.ToString()));
                    return f;
                }
                var t = new TextField();
                t.SetValueWithoutNotify(stored);
                t.RegisterValueChangedCallback(e => Set(e.newValue));
                return t;
            }

            void AddBinding()
            {
                bindingsProp.serializedObject.Update();
                var n = bindingsProp.arraySize;
                bindingsProp.InsertArrayElementAtIndex(n);
                var el = bindingsProp.GetArrayElementAtIndex(n);
                // InsertArrayElementAtIndex copies the previous element - clear so the new row starts blank.
                el.FindPropertyRelative("uxmlAttribute").stringValue = string.Empty;
                el.FindPropertyRelative("source").intValue = (int)FigmaBindingSource.FixedValue;
                el.FindPropertyRelative("value").stringValue = string.Empty;

                // Prefill the first still-available attr so the new row opens on a valid pick.
                var taken = new HashSet<string>();
                for (var i = 0; i < n; ++i)
                {
                    var a = bindingsProp.GetArrayElementAtIndex(i).FindPropertyRelative("uxmlAttribute").stringValue;
                    if (!string.IsNullOrEmpty(a))
                        taken.Add(a);
                }
                foreach (var a in MappableAttrs(FullNameOf(control.stringValue)))
                    if (!taken.Contains(a.name))
                    {
                        el.FindPropertyRelative("uxmlAttribute").stringValue = a.name;
                        break;
                    }

                bindingsProp.serializedObject.ApplyModifiedProperties();
                RebuildBindings();
            }

            void RemoveBinding(int index)
            {
                bindingsProp.serializedObject.Update();
                if (index < 0 || index >= bindingsProp.arraySize)
                    return;
                bindingsProp.DeleteArrayElementAtIndex(index);
                bindingsProp.serializedObject.ApplyModifiedProperties();
                QueueRebuild();
            }

            addButton.clicked += AddBinding;

            dropdown.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue == k_Custom)
                {
                    raw.style.display = DisplayStyle.Flex;
                    Write(raw.value);
                    // An empty value reads as a template in Sync, which would snap the pick back to
                    // None; keep the row on Custom until something is typed.
                    if (string.IsNullOrEmpty(raw.value))
                    {
                        rawWarn.style.display = DisplayStyle.None;
                    }
                    else
                    {
                        Sync();
                    }
                    RebuildBindings();
                    return;
                }
                raw.style.display = DisplayStyle.None;
                rawWarn.style.display = DisplayStyle.None;
                var stored = s_ChoiceToStored.TryGetValue(evt.newValue, out var s) ? s : string.Empty;
                Write(stored);
                RebuildBindings();
            });
            raw.RegisterValueChangedCallback(evt =>
            {
                var typed = evt.newValue?.Trim() ?? string.Empty;
                // Typing a known control's name is picking it: store the spelling the resolver uses,
                // and Sync flips the row to the dropdown pick.
                if (!typed.Contains("."))
                {
                    var stored = CanonicalizeTypedControl(typed);
                    if (stored != null)
                        typed = stored;
                }
                Write(typed);
                if (string.IsNullOrEmpty(typed))
                {
                    rawWarn.style.display = DisplayStyle.None;
                }
                else
                {
                    Sync();
                }
                RebuildBindings();
            });

            root.Add(fieldsRow);
            root.Add(raw);
            root.Add(rawWarn);
            root.Add(propLabel);
            root.Add(bindingsBox);
            root.Add(addButton);

            // Property-name suggestions key on the Figma name, so a rename refreshes the rows.
            root.TrackPropertyValue(property.FindPropertyRelative("figmaName"), _ => QueueRebuild());

            Sync();
            RebuildBindings();
            return root;
        }

        static VisualElement FieldCell(string caption, float grow, VisualElement field, float fixedWidth)
        {
            var cell = new VisualElement();
            cell.AddToClassList("figma-cell");
            if (fixedWidth > 0)
            {
                cell.style.width = fixedWidth;
                cell.style.flexGrow = 0;
                cell.style.flexShrink = 0;
            }
            else
            {
                cell.style.flexBasis = 0;
                cell.style.flexGrow = grow;
                cell.style.minWidth = 130;
            }
            var cap = new Label(caption);
            cap.AddToClassList("figma-prop-head");
            cell.Add(cap);
            cell.Add(field);
            return cell;
        }

        // Proportional table cell, shared by the property header and rows so they align.
        static VisualElement PropCell(float grow)
        {
            var cell = new VisualElement();
            cell.AddToClassList("figma-cell");
            cell.style.flexBasis = 0;
            cell.style.flexGrow = grow;
            return cell;
        }

        static VisualElement PropHeader()
        {
            var row = new VisualElement();
            row.AddToClassList("figma-prop-headrow");
            row.Add(HeadCell(FigmaStrings.Attribute, 1.5f));
            row.Add(HeadCell(FigmaStrings.Source, 1f));
            row.Add(HeadCell(FigmaStrings.Value, 1.3f));
            var fixedCell = new VisualElement();
            fixedCell.AddToClassList("figma-cell-fixed");
            row.Add(fixedCell);
            return row;
        }

        static VisualElement HeadCell(string text, float grow)
        {
            var cell = PropCell(grow);
            var label = new Label(text);
            label.AddToClassList("figma-prop-head");
            cell.Add(label);
            return cell;
        }

        // Schema attrs minus inherited base noise, own first; shared by preview and binding dropdowns.
        static List<ControlAttrInfo> MappableAttrs(string fullName)
        {
            var attrs = string.IsNullOrEmpty(fullName) ? null : new List<ControlAttrInfo>(ControlCatalog.Attributes(fullName));
            if (attrs == null)
                return new List<ControlAttrInfo>();
            attrs.RemoveAll(a => k_BaseAttrs.Contains(a.name));
            attrs.Sort((x, y) => x.inherited == y.inherited ? string.CompareOrdinal(x.name, y.name) : (x.inherited ? 1 : -1));
            return attrs;
        }

        // The Component Property name cell: names recorded at import as a dropdown with their type,
        // "Other…" for a property not yet seen. With nothing recorded it is a plain text field. Bool
        // attributes list Boolean properties only.
        static VisualElement PropertyNameField(SerializedProperty valP, Type attrType, string figmaName)
        {
            var free = new TextField { tooltip = FigmaStrings.PropertyNameTooltip, isDelayed = true };
            free.SetValueWithoutNotify(valP.stringValue ?? string.Empty);
            free.RegisterValueChangedCallback(evt =>
            {
                valP.stringValue = evt.newValue;
                valP.serializedObject.ApplyModifiedProperties();
            });

            var names = new List<string>();
            var labels = new List<string>();
            foreach (var p in FigmaPropertyCatalog.PropsFor(figmaName))
            {
                if (attrType == typeof(bool) && p.type != "BOOLEAN")
                    continue;
                names.Add(p.name);
                labels.Add(FigmaStrings.PropertyChoice(p.name, p.type == "BOOLEAN"));
            }
            if (labels.Count == 0)
            {
                // Suggestions are a cache of the last import, never a gate: a property authored here
                // ahead of its push is trusted at import, and one that never arrives emits nothing.
                free.tooltip = attrType == typeof(bool)
                    ? FigmaStrings.BooleanPropertyNameUnrecordedTooltip
                    : FigmaStrings.PropertyNameUnrecordedTooltip;
                return free;
            }

            labels.Add(k_OtherName);
            var current = (valP.stringValue ?? string.Empty).Trim();
            var known = -1;
            for (var i = 0; i < names.Count; ++i)
                if (string.Equals(names[i], current, StringComparison.OrdinalIgnoreCase))
                {
                    known = i;
                    break;
                }

            var drop = new DropdownField(labels, 0);
            drop.SetValueWithoutNotify(known >= 0 ? labels[known] : k_OtherName);
            free.style.display = known >= 0 ? DisplayStyle.None : DisplayStyle.Flex;
            free.style.marginTop = 3;
            drop.RegisterValueChangedCallback(evt =>
            {
                var picked = labels.IndexOf(evt.newValue);
                var other = picked < 0 || picked >= names.Count;
                free.style.display = other ? DisplayStyle.Flex : DisplayStyle.None;
                if (!other)
                {
                    valP.stringValue = names[picked];
                    valP.serializedObject.ApplyModifiedProperties();
                }
            });

            var cell = new VisualElement();
            cell.AddToClassList("figma-prop-stack");
            cell.Add(drop);
            cell.Add(free);
            return cell;
        }

        static string SourceLabel(FigmaBindingSource source)
        {
            switch (source)
            {
                case FigmaBindingSource.ComponentProperty: return k_SrcProperty;
                case FigmaBindingSource.DesignText: return k_SrcDesignText;
                case FigmaBindingSource.DesignIcon: return k_SrcDesignIcon;
                default: return k_SrcFixed;
            }
        }

        static FigmaBindingSource SourceFromLabel(string label)
        {
            if (label == k_SrcProperty)
                return FigmaBindingSource.ComponentProperty;
            if (label == k_SrcDesignText)
                return FigmaBindingSource.DesignText;
            if (label == k_SrcDesignIcon)
                return FigmaBindingSource.DesignIcon;
            return FigmaBindingSource.FixedValue;
        }

        // Background is UI Toolkit's image struct; the rest are the asset types it draws from.
        static bool IsAssetType(Type attrType)
        {
            return attrType == typeof(Background)
                || (attrType != null && typeof(UnityEngine.Object).IsAssignableFrom(attrType));
        }

        // Accepts both the emitted project URL and a plain project path.
        static UnityEngine.Object AssetFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;
            var g = url.IndexOf("guid=", StringComparison.Ordinal);
            if (g < 0)
                return AssetDatabase.LoadMainAssetAtPath(url);
            var end = url.IndexOfAny(new[] { '&', '#' }, g);
            var guid = end < 0 ? url.Substring(g + 5) : url.Substring(g + 5, end - g - 5);
            return AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
        }

        // The same URL shape the importer emits for icons, so both read back identically.
        static string UrlFromAsset(UnityEngine.Object asset)
        {
            if (asset == null)
                return string.Empty;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out var guid, out long fileId))
                return string.Empty;
            var path = AssetDatabase.GetAssetPath(asset);
            return "project://database/" + path + "?fileID=" + fileId + "&guid=" + guid + "&type=3#" + asset.name;
        }

        static bool IsEngine(string fullName)
        {
            return !string.IsNullOrEmpty(fullName)
                && fullName.StartsWith("UnityEngine.UIElements.", StringComparison.Ordinal);
        }

        // Catalog engine controls store the bare tag; a non-catalog engine control stores the full name so
        // the resolver trusts it (a bare miss would fall back to a template).
        static void EnsureCatalog()
        {
            if (s_CatalogCached)
                return;

            var controls = new List<DiscoveredControl>(ControlCatalog.Controls());
            controls.Sort((a, b) =>
            {
                if (a.engine != b.engine) return a.engine ? -1 : 1;
                var c = string.CompareOrdinal(a.ns, b.ns);
                return c != 0 ? c : string.CompareOrdinal(a.tag, b.tag);
            });

            var choices = new List<string> { k_None };
            var choiceToStored = new Dictionary<string, string> { { k_None, string.Empty } };
            var storedToChoice = new Dictionary<string, string>();
            var byBareTag = new Dictionary<string, string>();
            foreach (var d in controls)
            {
                var choice = (string.IsNullOrEmpty(d.ns) ? FigmaStrings.GlobalNamespace : d.ns) + "/" + d.tag;
                if (choiceToStored.ContainsKey(choice))
                    continue; // a type can register both its FullName and a custom UXML name - keep the first
                string stored;
                if (!d.engine)
                    stored = d.fullName;
                else if (Array.IndexOf(ControlMap.CatalogControlNames, d.tag) >= 0)
                    stored = d.tag;
                else
                    stored = d.fullName;
                choices.Add(choice);
                choiceToStored[choice] = stored;
                if (!storedToChoice.ContainsKey(stored))
                    storedToChoice[stored] = choice;
                // Bare-tag lookup for typed names; a tag shared by several types is ambiguous (null).
                var lower = d.tag.ToLowerInvariant();
                byBareTag[lower] = byBareTag.ContainsKey(lower) && byBareTag[lower] != stored ? null : stored;
            }
            choices.Add(k_Custom);

            s_Choices = choices;
            s_ChoiceToStored = choiceToStored;
            s_StoredToChoice = storedToChoice;
            s_ByBareTag = byBareTag;
            // Only treat a non-empty result as final, so a schema that registers late is picked up.
            s_CatalogCached = controls.Count > 0;
        }

        // The stored value a typed bare name reaches: a catalog tag, a uniquely-tagged discovered
        // control's stored spelling, or null (unknown or ambiguous, left as typed).
        internal static string CanonicalizeTypedControl(string typed)
        {
            var tag = ControlMap.CanonicalCatalogTag(typed);
            if (tag != null)
                return tag;
            EnsureCatalog();
            if (s_ByBareTag != null && s_ByBareTag.TryGetValue(typed.Trim().ToLowerInvariant(), out var stored))
                return stored;
            return null;
        }
    }
}
