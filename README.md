# Serpentine Hamiltonian Cycles (VCSM)

🇱🇻 Latviski: [LasiMani.md](LasiMani.md)

**VCSM** (Vector Chain Spatial Model) is a set of tools for knight's-move
sequences and the **Hamiltonian cycles/paths** built from them. It is aimed at
prepress, textile and CNC/laser work, where a continuous "serpentine" path that
visits every cell of a surface exactly once is useful for real physical
processing (embroidery, engraving, cutting, etc.).

Paths are encoded as **digit strings** (each digit 1-8 is one of the 8 possible
knight-move directions), which makes it possible to store and transform very
long paths (hundreds of thousands of moves) compactly.

## Contents

- [   ](#workflow)
- [Tools](#tools)
  - [Python](#python-tools)
  - [C#](#c-tools)
  - [Web viewer](#web-viewer)
- [File formats](#file-formats)
- [Requirements](#requirements)

## Workflow

A typical full pipeline, from an image/shape to a finished merged Hamiltonian cycle:

```
1. serpentinaHC3_6x6.exe 6 6
   → generates 6×6 knight's-tour blocks (cikls4abt6x6.hrz)

2. python cycles_to_gatavie_abt_6x6.py cikls4abt6x6.hrz gatavie_abt_6x6
   → converts them into a ready-made "torn" .abt catalogue
     (hub-satellite directional cutting)

3. python png_sensors_to_ffseciba.py image.png FFseciba.txt
   (or the C# version: PngSensorsToFFseciba.exe image.png FFseciba.txt)
   → image (rectangle + sensors AND/OR thin line/spiral/outline)
     → cell grid → Hamiltonian path

4. python serpentina_hc_v12.py gatavie_abt_6x6 FFseciba.txt
   → assembles 6×6 blocks according to the FFseciba.txt layout
     → one long .hrz path

5. HCeluSavienoshanaWinForms.exe
   → merges TWO separate paths into one (break points, rotation,
     transformations, automatic link search)

6. mikroScope.html
   → view the finished path in a browser, export SVG for sharing
```

## Tools

### Python tools

| File | Description |
|---|---|
| `cycles_to_gatavie_abt_6x6.py` | Builds a ready-made "torn" `.abt` catalogue from collected 6×6 knight's-tour cycles in one step (hub-satellite directional cutting + rotation/mirror variants). |
| `serpentina_hc_v12.py` | Assembles 6×6 (or 16×16) blocks into a larger "sheet" according to an FFseciba layout. Supports open and closed paths and prints progress while searching. |
| `diagnose_ffseciba.py` | Diagnostic tool: exhaustively checks every block transition in an FFseciba layout. |
| `png_sensors_to_ffseciba.py` | PNG image → FFseciba.txt. Recognises both a dense rectangle with sensor cut-outs and a thin line/spiral/outline (using thinning, spur pruning and connected-component search). |

**Required libraries:** `numpy`, `Pillow` (PIL), `scikit-image`, `scipy`.

### C# tools

| File | Description |
|---|---|
| `HCeluSavienoshanaWinForms.cs` | Windows Forms tool for merging TWO HC paths: break points (by mouse click or coordinates), rotation of the 2nd path, F1-F9 transformations, automatic link search, and +/- step buttons for quick adjustment of offsets and view centre. |
| `PngSensorsToFFseciba.cs` | C# port of `png_sensors_to_ffseciba.py`. Includes a hand-written Guo-Hall thinning algorithm and connected-component labelling (replacing Python's `scikit-image`/`scipy.ndimage`, which have no direct C# equivalents), plus grid-level cleanup of spurs, small loops and single-cell gaps. |

**Required:** .NET Framework (Visual Studio) with a `System.Drawing` reference
(console app) or Windows Forms (graphical tool).

### Web viewer

| File | Description |
|---|---|
| `mikroScope.html` | Standalone HTML/JS tool for viewing a large HC path ("sheet"): zoom, colour change by move number, break-point search by coordinates, F1-F9 transformations, sheet size in cells, and SVG export (with built-in mouse-wheel zoom, drag-to-pan and double-click detail zoom). |

Open it directly in a browser (double-click). No server needed.

## File formats

- **`.hrz` / `.vrz` / `.txt`**: a path as a digit string (each digit 1-8 is one move direction).
- **`.bin`**: the same data in a more compact binary form.
- **`FFseciba.txt`**: a CSV-style cell grid where each cell holds its visit order number (or is empty if the cell is not part of the path).
- **`.abt`**: a catalogue of 6×6 (or 16×16) "torn" blocks with a manifest, used by `serpentina_hc_v12.py` to match blocks.

## Requirements

- **Python 3.x** with `numpy`, `Pillow`, `scikit-image`, `scipy`
- **.NET Framework** (Visual Studio) for the C# tools
- Any modern browser for `mikroScope.html`

---

*The project grew step by step while solving real problems: assembling large (6×6, 16×16 and bigger) Hamiltonian cycles, converting images into paths, and viewing/merging those paths visually.*
