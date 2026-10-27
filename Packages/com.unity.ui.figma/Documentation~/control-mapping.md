# How a Figma node becomes a Unity control

Every imported node resolves to exactly one UI Toolkit element. Four rules decide which, and the
**first match wins**.

1. A tag set on the node in the Figma plugin.
2. A record in the import mapping.
3. The node's name, matched exactly against the control vocabulary below.
4. The node's type: a TEXT becomes a `Label`, anything else a `VisualElement`.

A tag beats the mapping, the mapping beats the name, and the name beats the type. Rules 1 and 2 say
what you mean exactly, so nothing further down can overrule them.

## Tag a node in the plugin

Select the node in Figma and pick a control from **Tag selection as**. A tag applies to that one node, so
it's the right tool for a one-off: this particular frame is a slider, whatever it's called.

A tag on an instance also wins over the component it came from, so one instance can differ from the
rest without changing the component.

## Add a mapping record

Go to **Project Settings** > **UI Toolkit** > **Connector for Figma design**. Records live in a mapping
asset, shown as **Active mapping**. If the project doesn't have one yet, select **Create mapping** to make
one; until then, imports use the built-in defaults.

A record applies everywhere the component it matches appears, which makes it the tool for anything you want
imported consistently. Each record has the following fields.

| Field | Meaning |
|---|---|
| **Figma name** | Match by component or node name. Punctuation, spacing and case are ignored, the same as the name convention, so `my-button` matches `My Button` |
| **Control** | A built-in control such as `Button`, or **Custom type…** for a fully-qualified type such as `MyCompany.UI.Chip`. Leave it empty to force a generated template instead of a control |
| **Attributes** | The UXML attributes this record emits, one row per attribute, each with a source |

The first record in the list that matches wins. A record with **Control** left empty forces a template, so
it emits no attributes and the **Attributes** section is hidden.

Each **Attributes** row names a UXML attribute and where its value comes from:

- **Fixed Value** is a literal you author, the same on every instance.
- **Component Property** names a Figma component property, and each instance supplies its own value.
  Properties seen in earlier imports are offered as a dropdown with their type, such as `Label (Text)`;
  select **Other…** to type a name not imported yet, as Figma shows it.
- **Design Text** routes the text layer the import absorbs into this attribute; **Design Icon** routes the
  exported icon image. At most one row of each, and only a custom control needs them: a built-in control's
  caption and icon attributes come from its own catalog entry, `text` and `icon-image` for `Button`, `label`
  for `RadioButton` and `Tab`, `placeholder-text` for `TextField`.

A value the import sources from the design, whether a caption text, an exported icon, or a component
property, wins over a fixed value, which serves as the fallback.

### Map a control of your own

A record can target any control in the project, your own or a package's. A control compiled into the
project as a `[UxmlElement]` appears in the **Control** dropdown with its real attributes, so its rows get
the same typed editors as built-in controls. For a control that isn't loaded, select **Custom type…** and
enter the fully-qualified type; a control in the global namespace goes by its bare class name and is
emitted as an unprefixed element.

Add a **Design Text** row naming whichever attribute carries the control's label, and a **Design Icon**
row for whichever carries an image. The type is emitted into UXML as written.

A custom control gets no caption unless you name one, and it keeps its children, because the package can't
know what its visual tree looks like. The exception is a layer whose value a row already carries: a text
layer driven by a component property that a **Component Property** row emits is absorbed rather than
rendered again, since the control draws that value from the attribute.

## Name a component after a control

A component **named after a control** resolves to it. The match is exact once punctuation, spacing
and case are ignored, so `TabView`, `Tab View` and `tab-view` all reach `TabView`.

A `/` in a Figma name nests the component in folders, so each segment is matched too: `Button/Primary`
is a `Button`.

**Nothing else is inferred.** `Tab Bar` isn't a `TabView` and `Slider Track` isn't a `Slider`; both
import as plain elements. That is deliberate. A phrase that merely contains a control name is a guess,
and the guess degrades badly: a `Tab` outside a `TabView` renders as nothing, and a `Slider` in place of
a decorative bar is a working widget where you wanted a graphic.

A component whose name contains a control word without being one is listed in the import report, in the
connector window's **Last import** section:

> 2 component(s) look like controls but are not named exactly like one, so they imported as plain
> elements. Tag them in the plugin or add a mapping: 'Tab Bar', 'Play Button'

Decide what each one becomes, then tag it in the plugin or add a mapping record. After changing a mapping,
select **Re-import last** in the same section to re-run the last push through the import pipeline, with no
Figma round trip.

This rule applies to components and their instances only. A plain frame is never guessed at.

### The vocabulary

Every control below is reachable by its own name. Aliases are alternative names people commonly use.

| Control | Also matches |
|---|---|
| `Button` | |
| `Label` | `Text` |
| `Toggle` | `Checkbox` |
| `TextField` | `Input`, `InputField` |
| `DropdownField` | `Dropdown` |
| `Slider`, `SliderInt` | |
| `ProgressBar` | `Progress` |
| `RadioButton` | |
| `HelpBox`, `Image` | |
| `ScrollView` | `Scroll` |
| `Foldout`, `GroupBox` | |
| `TabView` | `Tabs` |
| `Tab` | |
| `RadioButtonGroup` | `RadioGroup` |
| `ToggleButtonGroup` | `ToggleGroup` |

## Name the caption with a component property

A multi-text component leaves the import guessing which text is the caption, and position is a poor guess.
A [Figma text component property](https://help.figma.com/hc/en-us/articles/5579474826519) removes the
guess: the designer declares which text is the label, and the caption follows it, per instance and however
the layers are ordered.

The import picks the caption in this order:

1. An **Attributes** row routing a component property into the caption attribute.
2. Exactly one text property declared in the component, including on a nested typography component.
3. The positional rules: a lone text child, else the first text in layer order.

Declare the property in Figma on the component that owns the text layer. For a design system whose labels
are a nested typography component, that is the typography component, and every component nesting it picks
the property up with nothing further to configure.

The property only counts where it drives a layer. A variant whose text layer isn't linked to the property
falls back to the positional rules, so adopting properties incrementally is safe: unlinked variants keep
importing exactly as before.

A component declaring two or more text properties is ambiguous, so rule 2 stands down. Say which one you
mean with an **Attributes** row: attribute `text`, **Source** set to **Component Property**, and the
property's name as the value.

Boolean properties bind the same way. A row with **Source** set to **Component Property** reads the
instance's value, so a Figma **Boolean** property named `Rich` can drive `enable-rich-text` per instance.
A property that toggles a layer's visibility needs no row at all; hidden layers import as `display: none`.

## Children of a resolved control

A control's own visual tree is built by UI Toolkit, so what Figma drew inside it is treated
differently depending on the control.

**Leaf controls**, such as `Button`, `Toggle`, `Slider` and `TextField`, draw themselves. A single
text inside becomes the caption, wherever it sits, including inside a nested Text component.

A control with an icon slot also takes one layer as its icon. `Button` renders it through `icon-image`, so
a button drawn as an icon beside a label imports with both. The icon can be a shape, a vector, or an icon
component; it is exported as a PNG next to the screen and referenced from the attribute. Only one such
layer can be taken, because a control has one icon slot and choosing between two would be a guess. A
hidden layer is never taken, so a Figma Boolean property that toggles the icon's visibility removes the
icon per instance, and an Instance swap property on the icon layer changes which icon each instance
exports.

Anything a control has nowhere to put is dropped, and the import report names the control and how many
layers went. That is the signal to map it to a control that takes children, or to componentize it.

**Container controls**, such as `ScrollView`, `Foldout`, `GroupBox`, `TabView` and the group controls,
keep their children as content, and take their header from the layer name.

**`Tab` is the exception.** A tab strip is usually drawn as a row of chips that hold their own captions,
with the page each one opens somewhere else in the design. So a `Tab` whose entire content is a single text
takes that text as its `label` and gets no page. An icon drawn beside that text travels with the rest of
the content, and the import report names it.

A `Tab` with more than a single text inside keeps that content as its page, and takes its label from the
layer name.

A `Tab` only renders inside a `TabView`. A tray of tabs is treated as a `TabView` however it is named,
and a `Tab` outside one is flagged in the import report.

## When a name isn't enough

Matching by name is deliberately cautious: a component named after a leaf control that holds two or more
layers it couldn't absorb stays a plain element, because resolving it would throw that content away. Say
what you mean instead, by tagging the component in the plugin or adding a mapping record, and it resolves
whatever it contains.

## When nothing matches

The node imports as a `VisualElement` with its styling intact. Nothing is lost and nothing is guessed;
add a tag or a mapping record when you want a control.

## Map fonts

A Figma font family reaches a project font through **Font Map**, in the same settings. Select
**+ Add font**, enter the family as Figma spells it, and point it at a project Font or TextCore FontAsset.
The import writes the result into `-unity-font-definition`.

A family with no entry falls back to the default font, and **Last import** names what was missing:

> Missing fonts: Inter, Roboto - not in the project, so text falls back to the default font. Add the font
> to your project, or map it in the import settings font map.

Nothing fails when a font is unmapped, so imported text can look right in Figma and wrong in Unity with no
other signal. Check **Last import** after the first push of a new design.

## Additional resources

- [UI Toolkit Connector for Figma design](index.md)
- [Structure UI with UXML](https://docs.unity3d.com/Manual/UIE-UXML.html)
- [Style UI](https://docs.unity3d.com/Manual/UIE-USS.html)
