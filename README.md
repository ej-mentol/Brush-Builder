HammerTime.BrushBuilder (Gap Filler)
===================================

> [!WARNING]
> ### ⚠️ Experimental Release Candidate (RC) & Format Notice
> **Developed using AI with the active participation of the repository owner.**
>
> This plugin is currently in an experimental Release Candidate (RC) state. As HammerTime and other editors (like J.A.C.K.) evolve, there is a minor theoretical risk of format deviations. Due to continuous development and improvements, the UI/UX is subject to change.
>
> To ensure maximum safety for your primary work and prevent potential data loss (e.g. during modifications to the JMF format), please keep in mind that subtle incompatibilities could occur.
>
> **For absolute reliability, you can:**
> 1. Save your map files in the standard `.map` format.
> 2. Alternatively, perform complex alignment operations in a separate temporary map using a simple orthogonal reference brush (such as a 90° block) as an anchor, then copy the aligned geometry back to your primary editor.

---

### Description

**BrushBuilder** is a plugin for the HammerTime editor designed to automatically construct connecting geometry (gap filling) between two selected brush faces.

### Key Features:
- **Gap Filler Tool**: Automatically fills the volume between two faces (designated as Blue and Green Face).
- **Slicing & Segmentation (Experimental)**: Divide the connection span into multiple equal segments (1 to 20 slices) along the connection vector with automatic geometry interpolation. *Note: Complex twisted alignments might result in non-parallel slices; this feature is currently under active testing.*
- **Triangulation Methods (Experimental)**:
  - **One Solid (Convex)**: Automatically chooses the convex diagonal for non-planar quadrilaterals, generating exactly 1 valid convex solid instead of 24 small tetrahedrons.
  - **One Solid (Diag /) & (Diag \)**: Forces a fixed diagonal direction across all side faces for strict texture alignment and mesh control.
  - **Wedges (Radial) & Tetrahedral**: Splits non-planar transitions into radial wedges or tetrahedrons.
- **Safety Features & Validations**:
  - **Slice Thickness Safeguard**: Prevents building if individual slices end up thinner than `1.0` unit to avoid degenerate flat solids.
  - **Micro-edge Detection**: Validates all generated edges, rejecting geometry with edge lengths under `0.25` units.
  - **NaN Prevention**: Safe vector length checks when computing directions to prevent division-by-zero errors.
  - **Fallbacks**: Resilient string-to-enum mapping with strict fallback defaults for validation modes.
- **UI / UX Improvements**:
  - **Sizable Tool Window**: Configured as a utility window (`SizableToolWindow`) that remains on top and doesn't clutter the Windows taskbar.
  - **Equal Actions Layout**: "Build Brush" and "Reset All" buttons are equally sized (50% / 50% split) for a clean visual appearance.
  - **Flicker-Free Live Previews**: Event-driven UI updates for selections, eliminating timer polling.