# PlateLayer

**An AutoCAD plug-in that turns a CNC cut-layout drawing into paper-space plates — one viewport per
part, at a stated scale, grouped by material, labelled and dimensioned.**

Select the part geometry. PlateLayer groups it by material, measures every part, picks one scale per
plate, packs the parts into tiles, clones your ANSI B layout once per plate, and draws each part in
its own viewport with an X and a Y dimension. What comes off the printer is a catalogue you carry to
the saw.

It takes flat 2D geometry and stops at paper. It does not nest, does not cut, and does not optimise
yield — a plate is for reading, not for feeding a machine.

Downstream of [PartLayer](https://github.com/LemJukes/PartLayer-pub), which is what flattens the 3D
solids into the parts this reads.

**AutoCAD 2026, Windows, 64-bit.** AutoCAD LT cannot run it — LT has no .NET plug-in API.

---

## Install

**[Download PlateLayer Alpha 1.4](https://github.com/LemJukes/PlateLayer-pub/raw/main/dist/PlateLayer-Alpha-1.4.zip)** (53 KB) ·
[SHA-256](dist/PlateLayer-Alpha-1.4.zip.sha256)

1. **Right-click the ZIP → Properties → tick Unblock → OK. Before you extract it.**
2. Close AutoCAD. Copy the `PlateLayer.bundle` folder out of the ZIP into
   `%APPDATA%\Autodesk\ApplicationPlugins\`.
3. Start AutoCAD, type `PLLSET`. Its first line should read `PlateLayer Alpha 1.4`.

That is the whole install. No `NETLOAD`, no installer, no registry.

Step 1 is not optional and is the single most common reason the plug-in appears to install and then
does nothing at all — Windows marks downloaded files, and AutoCAD silently refuses to load a marked
DLL. If `PLLSET` is not recognised, **[docs/alpha/INSTALL.md](docs/alpha/INSTALL.md)** works through
the causes in order. `INSTALL.txt` ships inside the ZIP and says the same thing.

Uninstall: close AutoCAD, delete `%APPDATA%\Autodesk\ApplicationPlugins\PlateLayer.bundle\`.

---

## Commands

| Command | Long name | What it does |
|---|---|---|
| `PLL` | `PLATELAYER` | The main run. Select, review the plan, generate plates with per-plate approval. |
| `PLLSET` | — | Template layout, parts per plate, scale policy, dimension style, margins, approval mode. |

**Start with a dry run.** `PLL`, select the geometry, then answer `No` at the Proceed prompt: the
plan prints to the text window and the drawing is not touched. It tells you how many plates you are
about to get, at what scale, and — this is the part worth reading — *which constraint decided that*.

Per-plate approval is on by default: `Accept / Redo / Skip / aLl / eXit`. `aLl` finishes the run
unattended.

Full keyword reference: **[docs/alpha/COMMANDS.md](docs/alpha/COMMANDS.md)**.

---

## What lands on a plate

Each part gets a viewport, an X dimension and a Y dimension, and nothing else. Fractions are written
as MText stacks, so a plug-in-drawn dimension reads the same as one typed by hand.

Per-part index labels and per-viewport scale notes were built and then removed: on a short part the
vertical dimension text sits at the same height as the label and collides with it. The sheet
identifies itself through the title block instead:

| Field | Carries |
|---|---|
| `Plate Name` | `CNC Part Catalogue` |
| `Piece/Part Name` | the material, expanded: `3/4" Plywood \| rPLY_3-4` |
| `Drawing Date` | the run date |
| `PLATE #` | `nn of NN` |

Those four, and nothing else. Project name, full project name, project number, client, producer,
draftsperson, approved-by and notes belong to whoever set the drawing up; the plug-in cannot know
them and never writes to them, blank or otherwise. The rule lives in `PlateLayer.Core` and holds even
if a field is misconfigured to point at one of those tags.

No scale is printed anywhere. The dimensions carry the real measurements, which is the information a
person on the floor actually needs.

---

## How it picks a scale

One scale per plate. The finest rung that fits is chosen first, then coarsened as far as it keeps
reducing the plate count — bounded by a legibility floor, with ties going to the finer scale.

Three policies, via `PLLSET` → `Coarsen`:

| | Does | 13 equal parts |
|---|---|---|
| `Finest` | No coarsening. Largest drawings, most sheets. | 13 sheets @ 1:8 |
| `Balanced` *(default)* | Fewest sheets the parts-per-plate preference allows. | 2 sheets @ 1:32 |
| `Minimum` | Fewest sheets **per material**. | 1 sheet @ 1:48 |

`Minimum` exists because of a measured finding: on multi-plate runs it is almost always
`MaxPartsPerPage` — not the scale — that decides the sheet count. Coarsening already reaches the
fewest sheets that cap permits, so a policy that genuinely minimises sheets has to set the cap aside,
and `Minimum` does, falling back on the `MAXACTVP` hard limit of 60 viewports.

The legibility floor measures the **typical** part — the median short side across the material,
ignoring anything degenerate on an axis. Median rather than minimum, because a cut file holds 2"
strips and scored lines beside five-foot panels: measured on the smallest part the floor is
unreachable at any scale that fits, so it never operates and coarsening runs away. One strip should
not speak for twenty-one parts.

A hairline part sharing a sheet with large panels is not fixable by scale. The only way to draw a 2"
strip legibly beside a 6' panel is to give it its own sheet, and the run summary says so rather than
pretending otherwise.

**One plate per material is a floor none of the policies cross.** It is the strongest constraint in
the planner and it is deliberate — a catalogue sheet you carry to the saw should describe the sheet
of stock you are cutting. A drawing with eight materials produces eight plates under every policy.
Only mixing materials on a plate would change that, and that throws away the grouping the tool exists
to produce.

So "why so many sheets" is answered on the page rather than guessed at: the run summary names
whichever constraint is actually holding — the material split, the parts-per-plate cap, or neither.

---

## How full a plate gets

Two separate things decide it, and only one of them is a choice.

**Balance.** Given the plates a material needs, parts are dealt across them by area — largest into
whichever plate is currently lightest — so the plates come out similarly full. Greedy shelf packing
alone fills early plates and strands the leftovers: thirteen equal parts came out twelve on one plate
and one alone on the next. Balancing costs nothing — same plate count, same scale — and is abandoned
automatically if the even split would not actually pack.

**Honest emptiness.** A material holding two small parts cannot fill a sheet, and splitting them
across two plates at a finer scale is worse, not better. The dry run states each plate's fill and
names any plate under 35%, so a sparse sheet is a decision you have seen rather than a surprise on
paper.

That figure is the share of the sheet the **tiles** claim, annotation room included — not the share
covered by part outlines. On a plate of small parts the two are far apart, because at that scale a
tile is mostly the room its dimensions need. Tile claim is the right measure of layout waste — it
answers "could more have fitted here" — but it moves when tile geometry moves, so the 35% threshold
is a calibration against this geometry rather than a constant of nature.

---

## What it needs from your drawing

Layers, layout and title block come from a reference template rather than being invented. Out of the
box it expects a layout tab named `ANSI B` (`PLLSET` → `Template` points it elsewhere; resolution is
forgiving — exact, then case-insensitive, then a unique prefix match — and a miss names the layouts
that do exist), viewport frames on `rVIEWPORTS`, dimensions on `rDIMS_PAPER`, and title block
attributes `$PLATE_NAME`, `$PIECE/PART_NAME`, `#DRAWING_DATE`, `#`, `##`.

Layers it needs and cannot find, it creates — and reports at the end of a run, because a missing
convention layer means the drawing was not built from the usual template and the new layer's colour
and plot settings are guesses.

Material names are read off the layer: optional prefix, code, thickness written as `3-4`, optional
qualifier. The raw layer name is always kept beside the translation, because that is the string
someone cross-referencing the cut file will search for. The code table seeds with `PLY` and is
extended with `PLLSET` → `Names`.

Input is sheet goods by contract — PartLayer flattens sheet stock, and stick and board goods never
reach a plate — so an unrecognised code is not left alone quietly. It is named in the run summary,
because it means one of two things and both want looking at: a sheet good missing from the table, or
geometry that should not have been in the run.

---

## Scope — what it is not

These are decisions, not gaps.

- **No nesting and no yield optimisation.** Parts are tiled for legibility. Cut optimisation is a
  different tool's job.
- **No tooling, no kerf, no toolpaths.** The output is a drawing to read, not a program to run.
- **No mixed-material plates.** One material per plate is the point of the tool, not a limitation
  of it.
- **No rotation-aware packing.** Parts are tiled at the orientation they arrive in.
- **No plot or PDF export.** You plot the layouts yourself, the way you plot everything else.
- **No ribbon, no toolbar, no dialogs.** The commands are typed.
- **AutoCAD 2026 only.** The manifest is bounded to `R25.1`. On any other release the bundle is
  ignored, silently.

---

## Alpha — what to expect

It is in use by its author on real cut files. It has not been through many hands.

- Work on a copy of any drawing you care about.
- Run the dry run first. It is free, it changes nothing, and it is the whole plan.
- Read the ignored-layer report. A material drawn on the wrong layer upstream is exactly what it
  catches, and that has already happened once on a real file.
- The approval-loop paths and the error handling have not been exercised hard against production
  files. That is the main reason this is 1.4 and not 1.0 proper.

Bug reports: **[docs/alpha/REPORTING.md](docs/alpha/REPORTING.md)**. The short version is that a
report is useful in proportion to the drawing attached, and the dry-run text is the next best thing.

---

## Building from source

Needs the **.NET 8 SDK** and an installed **AutoCAD 2026** — `PlateLayer.Cad` references `acdbmgd`,
`accoremgd` and `acmgd` in place out of the AutoCAD install directory.

```powershell
dotnet build PlateLayer.sln
dotnet test test/PlateLayer.Core.Tests
```

If AutoCAD is not at the default path:

```powershell
dotnet build PlateLayer.sln -p:AcadDir="D:\Autodesk\AutoCAD 2026\"
```

| Project | Holds | AutoCAD |
|---|---|---|
| `src/PlateLayer.Core` | grouping, measuring, scale, packing, planning | never referenced |
| `src/PlateLayer.Cad` | commands, entity reading, layouts, viewports, annotation | referenced |
| `test/PlateLayer.Core.Tests` | xUnit, runs on any machine | no |

The moment a Core test needs AutoCAD to run, the logic under test is in the wrong assembly. The test
project is `net8.0` with no AutoCAD reference, so `dotnet test` on `PlateLayer.Core.Tests` works on a
machine that has never had AutoCAD installed.

Producing the distributable ZIP is one command:

```powershell
pwsh -File dev/pack.ps1
```

It builds Release, runs the tests, stages the bundle, writes `dist/PlateLayer-<FriendlyVersion>.zip`,
reads the ZIP back to confirm its contents, and writes the SHA-256 beside it. It also refuses to pack
if `PackageContents.xml`'s version has drifted from `Directory.Build.props`, or if any of Autodesk's
assemblies have found their way into the package.

Those checks guard against shipping a structurally broken ZIP. **They are not evidence that the
plug-in loads.** The only thing that asks AutoCAD anything is installing the ZIP and typing `PLL`.

Building `PlateLayer.Cad` in any configuration also deploys the bundle to your own `%APPDATA%`
plug-ins folder, so a clean AutoCAD start exposes the commands with no `NETLOAD`. Two things follow
from that: what is installed locally tracks whichever configuration you built last, and if AutoCAD
is open while you build, the copy step emits `MSB3021` warnings and continues — which means the
deployed bundle is stale, not that the build is broken.

### A note on the test data

`test/PlateLayer.Core.Tests/SampleCutFile.cs` is an invented cut file — round numbers on a 96 × 48
sheet, written for this suite. The fixtures the private repository tests against are decoded from
real production drawings and are not published here, so a few tests that assert facts about those
specific files do not appear in this repository. Everything the public tests assert, they assert
against geometry that was made up on purpose.

### A note on the comments

Source comments cite a design record — `OPEN D15`, `D8-A`, and similar — that lives in a private
development repository and is not published here. The references are left in place because the
reasoning they carry is worth more than the tidiness of removing it. Read them as "there is a reason,
recorded elsewhere", not as a broken link to a file you are missing.

---

## Licence

**Not open source yet, deliberately.** Copyright © 2026 Rob Garner, all rights reserved, with an
explicit grant to download, install, use — including commercially — and build this alpha. See
**[NOTICE.md](NOTICE.md)** for the exact terms and why they are worded that way.

Disclaimer: the majority of this source code was generated by AI/LLM tooling; all design decisions
and responsibility for the software rest solely with the developer.

---

Rob Garner · [rob@rjgarner.com](mailto:rob@rjgarner.com) · [jukeltd.com](https://jukeltd.com)
