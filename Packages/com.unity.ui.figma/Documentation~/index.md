# UI Toolkit Connector for Figma design

Import Figma frames as UXML and USS documents, and push supported style, layout, interaction-state and
design-token edits back to the Figma document. The connection runs through the `com.unity.pipeline`
package, so the Editor doesn't open a server of its own.

You connect the plugin to your Editor once, then work in either direction. **Push selection** in Figma
generates documents in your project, and **Push** in the connector window sends your Unity edits back for
Figma to apply. Structure travels one way only, from Figma to Unity.

| **Topic** | **Description** |
| :-------- | :-------------- |
| [How a Figma node becomes a Unity control](control-mapping.md) | The four rules that decide which UI Toolkit element an imported Figma node resolves to, how to override them, how component properties drive captions and attributes, and how Figma font families reach project fonts. |

## Requirements

- Unity 6000.3 or later.
- `com.unity.pipeline`, resolved automatically as a dependency.
- The Figma desktop app, with the **Unity UI Toolkit Connector** plugin (step 1 below).

## Set up the connection

Do this once per Editor. Installing the package is all the opt-in the connector itself needs, but Pipeline
doesn't answer a browser client until you allow it, which is step 2.

To connect Unity and Figma:

1. **Install the plugin in Figma.** In the Figma desktop app, find **Unity UI Toolkit Connector** under
   Plugins and install it. The plugin is published to Figma rather than shipped inside this package, so it
   updates on its own.
2. **Let Pipeline answer the plugin.** A Figma plugin runs in a sandboxed browser frame, and Pipeline
   refuses those by default. Go to **Window** > **Pipeline** > **Settings**, enable
   **Allow Browser Clients**, then restart the server: the setting is read when the server starts.

   Do this before the next step. Restarting the server can land it on a different port, which leaves a
   token you already copied pointing at the wrong one.
3. **Connect.** Go to **Window** > **UI Toolkit** > **Connector for Figma design** in Unity and select
   **Copy token**. In the plugin's **Settings** tab, paste it into **Pipeline token** and select
   **Connect to Unity**.

   Paste it promptly. The clipboard clears 60 seconds after you select **Copy token**.

   What you copy is the Editor's Pipeline port and its token together, so there's no port to choose. Copy
   it again after an Editor restart, which issues a new token, and after a Pipeline port change, which the
   pasted value no longer names.

   The plugin talks to one Editor at a time. To point it at a different Editor, copy that Editor's token,
   paste it over the current one and select **Connect to Unity** again.

The plugin stops searching for Unity once it reaches the Editor, and the connector window's
**Imported screens**, **Sync to Figma** and **Last import** sections become the place you work from. If the
plugin keeps searching, refer to [Troubleshooting](#troubleshooting).

## Import frames

Select one or more frames in Figma and select **Push selection**. Each screen arrives as a UXML document
with a matching USS file under `Assets/Figma/<file name>/`, alongside `tokens.uss` and one stylesheet per
design-token mode.

The result is ordinary UI Toolkit content. Open a generated UXML document in UI Builder, or reference it
from a `UIDocument` component, the same as a document you wrote by hand.

Which Unity control a Figma component maps to is decided by the mapping asset in **Project Settings** >
**UI Toolkit** > **Connector for Figma design**. Components can also be tagged individually from the
plugin's **Tag selection as** dropdown, which wins over the mapping. For the full resolution order, refer
to [How a Figma node becomes a Unity control](control-mapping.md).

Generated USS classes carry a project-wide prefix, set by **USS Class Prefix** in the same settings. A
prefix of `app` produces classes such as `.app-login-button`, which keeps generated classes clear of your
own USS. Leave it empty for no prefix.

To import continuously instead of frame by frame, enable **Auto-push edits** in the plugin's **Sync** tab.
The plugin then pushes edited screens for as long as it stays open, waiting **Auto-push delay (ms)** after
your last Figma edit before it sends. The delay defaults to 2000 ms.

## Send edits back

Style, layout, interaction-state and design-token edits you make in Unity are detected automatically, but
they aren't sent until you push them. The connector window's **Sync to Figma** section lists what it has
detected, grouped by Figma file. Select **Push** on a file to send that file's edits, or **Push all** to
send every detected edit.

Pushing queues the edits rather than completing them. A queued edit stays queued, and is retried, until
Figma acknowledges it, so a push you make while the plugin is closed isn't lost. To accept queued edits,
open the plugin's **Sync** tab and select **Apply changes**, or enable **Auto-pull changes** to accept them
as they arrive. Each entry clears from **Sync to Figma** once Figma has acknowledged it.

Sending edits back is available only in Figma's Design mode. In Dev Mode the plugin's **Apply changes**
control is unavailable, and anything you push from Unity stays queued until you switch back.

Structure is one way. Figma owns the hierarchy, and the generated tree is a copy, so adding or reordering
elements in Unity isn't sent back.

## Known limitations

- Structural edits are one way. Figma owns the hierarchy, and the generated tree is a copy, so adding or
  reordering elements in Unity isn't sent back.
- Auto-layout spacing imports as USS `gap`, which Unity supports from 6000.7.0a4. On older versions the
  spacing is dropped, and **Last import** names the affected frames. Set the spacing with margins on the
  children instead.
- Interaction-state styles aren't imported for a component that holds more than one text layer, because
  the wire format carries one style diff per state rather than one per layer. **Last import** names the
  affected components.
- A drop shadow on a frame or shape isn't imported: USS has no `box-shadow`, and its `drop-shadow()`
  filter draws behind the whole layer rather than clipping behind the shape, so it isn't used as a
  stand-in. **Last import** names the shadowed layers. Shadows on text import as USS `text-shadow`.
- Background blur imports as USS `backdrop-filter`, which Unity supports from 6000.6; on older versions
  it is dropped, and **Last import** names the affected layers. Layer blur imports as `filter: blur()`
  on every supported version.
- Background blur renders on screen-space panels only; a world-space panel draws the fill without the
  blur.
- A vector's fill is baked into the PNG it exports as, so a design token bound to that fill re-themes on
  the next import rather than at runtime through a USS variable. The import doesn't bind the token to
  `background-color`, which would paint a solid box the size of the element behind the glyph.
- An icon is exported once per component it comes from, so instances of one icon component all draw that
  component's own colors. A color override on a single instance isn't carried across.

## Connection and security

The connector isn't a server of its own. It registers the `figma_status`, `figma_import`,
`figma_stage_image`, `figma_commands` and `figma_ack` commands with `com.unity.pipeline`, and Pipeline owns
the listener, the bearer token and CORS.

The transport perimeter is therefore Pipeline's. Pipeline binds to loopback, refuses browser origins other
than the sandboxed `null` that Figma's plugin iframe sends, requires a bearer token on every request before
it routes that request, and compares the token in constant time.

Inside that perimeter, this package limits what an authorized caller can do:

- Writes are confined to `Assets/Figma/` by path containment, with an extension allowlist.
- Payloads whose wire schema this version doesn't read are refused.
- A staged image must be a PNG under 16 MB.

The token is a Pipeline session credential shared with every Pipeline client, so treat it as one. Selecting
**Copy token** puts it on your clipboard for 60 seconds, and the plugin keeps it in Figma's `clientStorage`
so it survives reloads. Anything with access to your machine can read either. The token changes when the
Editor restarts.

The connector deliberately offers no way to revoke the token, because the token is Pipeline's. Rotating it
here would disconnect every other Pipeline client, not just Figma.

## Troubleshooting

The connector window's **Last import** section reports what the import pipeline flagged, and is the first
place to look after a push that didn't produce what you expected.

| Symptom | Cause | Resolution |
| :------ | :---- | :--------- |
| The plugin can't find the Editor, and the connector window shows `Unity Pipeline is not running, so the Figma plugin cannot reach this Editor.` | Pipeline's server isn't running. | Start Pipeline, then copy the token again. |
| The plugin keeps searching for Unity with Pipeline running. | **Allow Browser Clients** is off, or the server wasn't restarted after you enabled it. | Enable it in **Window** > **Pipeline** > **Settings**, then restart the server. |
| **Copy token** appears to do nothing. | Pipeline isn't running, so there's no token to copy. The Editor logs a warning and the clipboard is left alone. | Start Pipeline and copy again. |
| The token you pasted is rejected, or the field pastes empty. | The clipboard clears 60 seconds after **Copy token**. | Copy again and paste it straight away. |
| The connection worked and then stopped. | An Editor restart issues a new token, and a Pipeline port change invalidates the port stored with the old one. | Copy the token again and reconnect. |
| **Apply changes** is unavailable in the plugin. | Figma is in Dev Mode. | Switch the file to Design mode. |
| Pushed edits stay listed in **Sync to Figma**. | Figma hasn't acknowledged them yet. Queued edits are retried, so nothing is lost. | Check that the plugin is connected and in Design mode, then select **Apply changes**. |
| Imported text uses the wrong font. | The Figma font family has no **Font Map** entry, so text falls back to the default font. | Add the family to **Font Map**. **Last import** names the families that were missing. |
| A component imported as a plain element instead of a control. | Its name isn't an exact match for a control. | Tag it in the plugin or add a mapping record. Refer to [How a Figma node becomes a Unity control](control-mapping.md). |
| A caption ignores a component property's value. | The property drives no text layer in that instance's variant, so the import uses the layer's own text. | In Figma, link the variant's text layer to the property. |
| A mapping that used to work stopped applying. | The component was renamed in Figma, so the record's **Figma name** no longer matches. **Last import** names mappings that matched this file before but match nothing now. | Update the record's **Figma name** to the new component name. |

## Additional resources

- [UI Toolkit](https://docs.unity3d.com/Manual/UIElements.html)
- [Structure UI with UXML](https://docs.unity3d.com/Manual/UIE-UXML.html)
- [Style UI](https://docs.unity3d.com/Manual/UIE-USS.html)
