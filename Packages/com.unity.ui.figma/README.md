# UI Toolkit Connector for Figma design

Imports Figma frames as UXML and USS documents, and pushes supported style, layout, interaction-state
and design-token edits back to the Figma document.

The Editor does not open a server of its own. The connection runs through `com.unity.pipeline`, which
owns the listener, the bearer token and CORS.

## Requirements

- Unity 6000.3 or later.
- `com.unity.pipeline`, resolved automatically as a dependency.
- The **Unity UI Toolkit Connector** plugin, installed in Figma.

USS `gap` needs 6000.7.0a4 or later. Below that it is reported as unsupported rather than converted.

## Getting started

See [Documentation~/index.md](Documentation~/index.md) for installing the Figma plugin, connecting it
to the Editor, importing frames and sending edits back.

Installing the package is the opt-in; there is no feature toggle.
