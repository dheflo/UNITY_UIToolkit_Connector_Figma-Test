# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this package adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0-exp.1] - 2026-09-22

### Added

- Added **Sync to Figma** for `text-shadow`, `filter` and `backdrop-filter`, including `filter: none` to
  remove an effect. Needs plugin version 5.
- Added **Last import** notes for gradient fills and text strokes, which the connector does not import yet.

### Changed

- Changed the minimum `com.unity.pipeline` version to 0.7.0-exp.1.
- Changed the connector window, the import settings page and the import report so their text follows the
  Editor language. English remains the default; translations arrive as language packs are added.
- Changed the **Last import** note about a text property that drives no layer so it only appears when the
  instance set the property to something other than the component's default. Needs plugin version 5;
  older plugins keep the previous behaviour.

### Fixed

- Fixed **Sync to Figma** reporting edits that were never made after an element was added, removed or moved
  in a generated document. The affected part of the document now stops syncing and a warning says why,
  until the screen is pushed from Figma again.
- Fixed screens importing with the variable collection's default mode instead of the mode their content
  uses in Figma. Component templates no longer link a mode stylesheet, so a runtime mode switch re-themes them
  too. Needs plugin version 5 for the frame's mode.

## [0.1.0-exp.1] - 2026-09-08

### Added

This is the first release of UI Toolkit Connector for Figma design, connected through
`com.unity.pipeline` (0.6.0-exp.1 or newer). For details and current limitations, refer to the package
documentation.

- Added import of selected Figma frames as UXML and USS documents, with design tokens emitted as one
  stylesheet per token mode.
- Added write-back to Figma for supported style, layout, interaction-state and design-token edits, pushed
  from the **Connector for Figma design** window.
- Added continuous sync: **Auto-push edits** in the plugin pushes edited screens as you work, and
  **Auto-pull changes** accepts queued Unity edits as they arrive.
- Added a settings page under **Project Settings** > **UI Toolkit** for the import mapping, the
  **Font Map** that routes Figma font families to project fonts, and the **USS Class Prefix** applied to
  generated classes.
