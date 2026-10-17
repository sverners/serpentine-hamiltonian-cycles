# Serpentine Hamiltonian Cycles (VCSM)

🇱🇻 Latviski: [LasiMani.md](LasiMani.md)

**VCSM** (Vector Cycling Stitching Method) is a set of tools for knight's-move
sequences and the **Hamiltonian cycles/paths** built from them. It is aimed at
prepress, textile and CNC/laser work, where a continuous "serpentine" path that
visits every cell of a surface exactly once is useful for real physical
processing (embroidery, engraving, cutting, etc.).

Paths are encoded as **digit strings** (each digit 1-8 is one of the 8 possible
knight-move directions), which makes it possible to store and transform very
long paths (hundreds of thousands of moves) compactly.

## Contents

- [Links](#links)
- [Workflow](#workflow)
- [Tools](#tools)
  - [Python](#python-tools)
  - [C#](#c-tools)
  - [Web viewer](#web-viewer)
- [Png2Seciba in detail](#png2seciba-in-detail)
- [Empty cells and how they are filled](#empty-cells-and-how-they-are-filled)
- [File formats](#file-formats)
- [Requirements](#requirements)

## Links

- 🌐 Project page (GitHub Pages): <https://sverners.github.io/serpentine-hamiltonian-cycles/>
- 🏠 Author's website: <https://verneraweb.wuaze.com/index.html>

## Workflow

A typical full pipeline, from an image/shape to a finished merged Hamiltonian cycle:

```
1. serpentinaHC3_6x6.exe 6 6
   → generates 6×6 knight's-tour blocks (cikls4abt6x6.hrz)

2. python cycles_to_gatavie_abt_6x6.py cikls4abt6x6.hrz gatavie_abt_6x6
   → converts them into a ready-made "torn" .abt catalogue
     (hub-satellite directional cutting)

3. Png2Seciba.exe image.png FFseciba.txt
   (built from PngSensorsToFFseciba.cs; the older Python version is
    python png_sensors_to_ffseciba.py image.png FFseciba.txt)
   → image (filled shape with sensor cut-outs AND/OR thin line/spiral/outline)
     → cell grid → order of the blocks (FFseciba.txt)
   → also writes FFseciba_karte.png/.txt (diagnostic map) and
     FFseciba_tuksas.txt (cells left empty; the file is empty if there are none)

4. python serpentina_hc_v12.py gatavie_abt_6x6 FFseciba.txt
   → assembles blocks (6×6, 16×16, ...) according to the FFseciba.txt order
     → one long .hrz path

5. Png2Seciba.exe --ffseciba=FFseciba.txt --aizpildit=cikls.hrz
   → only if step 3 reported empty cells (red notice): fills them with
     separate closed blocks and merges them into the cycle (see below)

6. HCeluSavienoshanaWinForms.exe
   → merges TWO separate paths into one (break points, rotation,
     transformations, automatic link search)

7. mikroScope.html
   → view the finished path in a browser, merge two paths there as well,
     export SVG for sharing
```

The block order in `FFseciba.txt` does not depend on the block size, so the same
file can be used to assemble the final cycle from 6×6, 16×16 or other blocks.

## Tools

### Python tools

| File | Description |
|---|---|
| `cycles_to_gatavie_abt_6x6.py` | Builds a ready-made "torn" `.abt` catalogue from collected 6×6 knight's-tour cycles in one step (hub-satellite directional cutting + rotation/mirror variants). |
| `serpentina_hc_v12.py` | Assembles 6×6 (or 16×16) blocks into a larger "sheet" according to an FFseciba layout. Supports open and closed paths and prints progress while searching. |
| `diagnose_ffseciba.py` | Diagnostic tool: exhaustively checks every block transition in an FFseciba layout. |
| `png_sensors_to_ffseciba.py` | PNG image → FFseciba.txt (original Python version). Recognises both a dense rectangle with sensor cut-outs and a thin line/spiral/outline (using thinning, spur pruning and connected-component search). The newer features listed under [Png2Seciba in detail](#png2seciba-in-detail) exist in the C# version. |

**Required libraries:** `numpy`, `Pillow` (PIL), `scikit-image`, `scipy`.

### C# tools

| File | Description |
|---|---|
| `HCeluSavienoshanaWinForms.cs` | Windows Forms tool for merging TWO HC paths: break points (by mouse click or coordinates), rotation of the 2nd path, F1-F9 transformations (F5/F9 reverse the path and keep the break point on the same edge), automatic link search, +/- step buttons for quick adjustment of offsets and view centre, SVG export of what is on screen. |
| `PngSensorsToFFseciba.cs` | PNG → FFseciba.txt (built as `Png2Seciba.exe`). Hand-written Guo-Hall thinning and connected-component labelling (replacing Python's `scikit-image`/`scipy.ndimage`), recognition of filled shapes of any outline, chessboard-colour balancing with clustered empty cells, closed-cycle construction, diagnostic map and **filling of empty cells in a finished `.hrz` cycle**. See [Png2Seciba in detail](#png2seciba-in-detail). |

**Required:** .NET Framework (Visual Studio) with a `System.Drawing` reference
(console app) or Windows Forms (graphical tool).

### Web viewer

| File | Description |
|---|---|
| `mikroScope.html` | Standalone HTML/JS tool for viewing and merging large HC paths ("sheets"). Viewing: zoom, colour change by move number, break-point search by coordinates, sheet size in cells. Merging (like the WinForms tool): a second path (yellow) with offsets and +/- step buttons, break points for both paths (also by clicking on the drawing), link validity status, automatic snapping of the openings, F1-F9 transformations for either path, export of the merged cycle as `.hrz`/`.bin` and of what is on screen as SVG (with built-in mouse-wheel zoom, drag-to-pan and double-click detail zoom). |

Open it directly in a browser (double-click). No server needed.

## Png2Seciba in detail

```
Png2Seciba image.png [FFseciba.txt] [options]
Png2Seciba image.png --aizpildit=cikls.hrz
Png2Seciba --ffseciba=FFseciba.txt --aizpildit=cikls.hrz
```

**Image conventions.** Black = surface to be visited, white = sensors / forbidden
zones. One cell = 4×4 pixels. Black areas thinner than 12 px are thinned to a
one-cell line; thicker areas are treated as filled surface of any outline.
Tiny black specks (under 60 px) are ignored as noise. Leftover pixels at the
right/bottom edge count as a full cell, and an odd number of rows/columns is made
even by copying the last filled row/column.

**Output files**

| File | Content |
|---|---|
| `FFseciba.txt` | The block order (visit order number per cell). Written only for a closed cycle (see `--atlaut-atvertu`). |
| `FFseciba_karte.png` / `.txt` | Diagnostic map: **green** = surface (OK), **white** = forbidden (sensors), **red** = cells left empty, **yellow** = forced end. A banner states whether a cycle is possible and why not. |
| `FFseciba_tuksas.txt` | Cells left empty (`row,column`); empty file if there are none. |
| `FFseciba_starprezultats.txt` / `.png` | Intermediate result when only an **open** path exists: numbered path and a picture with start/end marked, so that the picture can be corrected. |

If an output file is locked by another program (e.g. Excel), the result is not
lost: it is saved as `name_1.txt`, `name_2.txt`, ... and a warning is printed.

**Options**

| Option | Meaning |
|---|---|
| `--atlaut-atvertu` | Accept an open path as the final result. Thin-line shapes always have line ends, so they need this option. |
| `--tikai-karte` | Write the map and diagnostics and stop before the open-path search. |
| `--laiks=SEC` | Time limit per search stage (default 120, 0 = none). |
| `--forse` | Search even if a cycle/path is proven impossible (fill mode: allow a smaller `.hrz` grid). |
| `--neizlidzinat` | Do not balance the chessboard colours (no cells are left empty). |
| `--vecais-tikls` | Old grid behaviour (leftover pixels ignored, no even-size padding). |
| `--aizpildit=FILE.hrz` | Fill mode: the existing closed cycle to fill. |
| `--ffseciba=FILE.txt` | Fill mode without the image: surface from the FFseciba file (+ `*_tuksas.txt`). |
| `--hrz-izvade=FILE.hrz` | Name of the filled cycle (default `<input>_aizpildits.hrz`). |
| `--tuksas=FILE` | Empty-cell list if it is not `<FFseciba>_tuksas.txt`. |
| `--bloks=N` | Block size (normally detected automatically). |
| `--cikli=FILE` | Own catalogue of closed N×N block cycles (one cycle per line, digits); they are tried first. |

## Empty cells and how they are filled

**Why cells can stay empty.** Colour the cells like a chessboard. Every step of a
cell-level cycle changes the colour, so a closed cycle over all cells needs equally
many cells of both colours. Shapes with, for example, a circular cut-out often have
a few cells more of one colour. Png2Seciba then leaves the minimum number of cells
**empty**, placed together as one diagonal chain (the start cell is never removed),
verifies that a cycle exists for the rest, and lists the empty cells: in
`FFseciba_tuksas.txt` and as a **red notice** in the console (nothing is printed
if there are no empty cells).

**How they are filled.** Every cell is a closed block (a knight's-tour cycle) by
itself. After the final `.hrz` has been assembled, each empty cell is filled with its
own closed N×N cycle and merged into the big cycle by swapping two edges: an edge
a–b of the big cycle and an edge c–d of the new block are replaced by a–c and
b–d (both must be knight moves). The result is **one** closed cycle that also
visits the previously empty cells.

- The block size N is detected automatically: the area of the `.hrz` equals the
  grid size × N (e.g. 640 × 576 over a 40 × 36 grid → N = 16). Any even N ≥ 6 works.
- The y-axis orientation of the `.hrz` is detected automatically.
- The result is verified: a single closed cycle, all points unique, every block complete.
- Needs a closed `.hrz` built from complete N×N blocks and the same image or
  FFseciba file. A cell without any visited neighbour cannot be filled.

## File formats

- **`.hrz` / `.vrz` / `.txt`**: a path as a digit string (each digit 1-8 is one move direction).
- **`.bin`**: the same data in a more compact binary form.
- **`FFseciba.txt`**: a CSV-style cell grid where each cell holds its visit order number (or is empty if the cell is not visited: sensor, outside the shape, or a cell left empty).
- **`FFseciba_tuksas.txt`**: cells left empty, one `row,column` per line.
- **`FFseciba_karte.png` / `.txt`**: diagnostic map of the detected surface.
- **`.abt`**: a catalogue of 6×6 (or 16×16) "torn" blocks with a manifest, used by `serpentina_hc_v12.py` to match blocks.

## Requirements

- **Python 3.x** with `numpy`, `Pillow`, `scikit-image`, `scipy`
- **.NET Framework** (Visual Studio) for the C# tools
- Any modern browser for `mikroScope.html`

---

*The project grew step by step while solving real problems: assembling large (6×6, 16×16 and bigger) Hamiltonian cycles, converting images into paths, and viewing/merging those paths visually.*
