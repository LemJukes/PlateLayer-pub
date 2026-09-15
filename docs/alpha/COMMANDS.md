# Commands

Two commands. `PLL` does the work; `PLLSET` changes how.

| Command | Long name | |
|---|---|---|
| `PLL` | `PLATELAYER` | The run |
| `PLLSET` | — | Settings, saved into the drawing |

---

## PLL — the run

1. **Select the part geometry.** A sloppy window that catches the title block or the sheet-group
   rectangles is fine — excluded layers are filtered out before anything is measured, and what was
   dropped is reported by layer and count at the end.
2. **PlateLayer measures.** It reads the drawing's own `DIMTXT` and `DIMASZ` rather than assuming
   them, and probes the dimension text style to measure how wide a character actually plots. If the
   probe fails it says so and falls back to a deliberately fat estimate. Gutters are sized from
   these numbers, so this step is why dimension text stays on the sheet.
3. **It prints the plan and asks to proceed** — `Yes / No / Dryrun`. **Answer `No` the first time.**
   The plan prints and the drawing is not touched.
4. **On `Yes`, plates are drawn**, with an approval prompt per plate unless approval is set to
   `Batch`.

### The dry run is the useful part

It states, before anything is drawn: how many materials, how many plates, the scale per plate, each
plate's fill percentage, which plates are sparse, which geometry was ignored and on what layer — and
**which constraint decided the plate count**. That last line is the one worth reading. A run that
produced more sheets than expected is almost always the one-plate-per-material floor or the
parts-per-plate cap, not the scale, and the summary names whichever it was.

### The approval prompt

`Accept / Redo / Skip / aLl / eXit`

| | |
|---|---|
| `Accept` | keep this plate, move to the next |
| `Redo` | discard and regenerate this plate |
| `Skip` | discard this plate and move on |
| `aLl` | accept this one and every remaining plate without asking again |
| `eXit` | stop the run here |

`aLl` answers to **L**, not A — `A` has always meant Accept. AutoCAD derives a keyword's shortcut
from its capital letters, so `Accept` and `All` would both answer to A and the second would be
silently unreachable. Every keyword set in the plug-in is checked for that collision at run time,
and a test walks all of them so it fails at build time first.

### A note on units

The scale ladder assumes inches. `INSUNITS` set to a positive non-inch value prompts for
confirmation before the run; `0` (Unitless) does not, because it is common on drawings that are
plainly in inches.

---

## PLLSET — the settings

Printed first, then changed one at a time. Saved into the **drawing**, not into your profile — so a
drawing you send somebody carries its own settings and none of yours.

| Keyword | Key | Sets | Default |
|---|---|---|---|
| `Template` | T | Layout tab cloned for each plate | `ANSI B` |
| `Parts` | P | Max parts per plate | `12` |
| `Scalemode` | S | `UniformPerPlate` / `PerPartBestFit` | `UniformPerPlate` |
| `Dims` | D | `Dimensions` / `MTextOnly` | `Dimensions` |
| `Margin` | M | Viewport margin, paper inches | `0.125` |
| `Coarsen` | C | Scale policy, then the legibility floor | `Balanced`, floor `0.5"` |
| `Labels` | L | Per-part labels and scale notes, together | off |
| `Names` | N | Material code table | seeded with `PLY` |
| `Approval` | A | `PerPage` / `Batch` | `PerPage` |
| `Quit` | Q | Save and leave | |

**`Template`** resolution is forgiving: exact name, then case-insensitive, then a unique prefix match
in either direction. Ambiguity and absence both fail with the layouts actually present named, and
resolution happens *before* the Proceed prompt — so a wrong template name stops the run with the
drawing untouched rather than part way through cloning.

**`Margin`** is breathing room between the part's outer edge and the viewport frame. It is not
cosmetic: without it the part outline lands exactly on the clip boundary and under the non-plotting
viewport border, and plots with no outline at all. The cost is that margin × scale denominator is how
much surrounding model space the viewport reveals, so it wants to stay well under the spacing between
parts in your cut file.

**`Scalemode`** — `UniformPerPlate` gives every part on a plate one scale. `PerPartBestFit` lets each
part take the finest rung that fits a whole sheet, which on a real file puts single parts on plates
of their own; it exists because an early design called for it, and is not the default.

**`Dims`** — `Dimensions` draws real `DIMENSION` entities. `MTextOnly` writes the measurement as
text, for a drawing where a dimension style is fighting you.

**`Labels`** toggles per-part index labels and per-viewport scale notes together. Both are off
because on a part under about an inch of plotted height the vertical dimension text sits at the same
height as the label and lands on top of it. Turning them on is also how to reproduce that.

**`Names`** lists the codes currently recognised and adds or changes one. A code the table does not
know falls back to the bare layer name — and is named in the run summary, because input to this tool
is sheet goods by contract, so an unrecognised code means either a sheet good missing from the table
or geometry that should not have been in the run.

**`Approval`** — `PerPage` prompts after each plate. `Batch` draws the lot without asking.

---

## The scale ladder

Eleven rungs, architectural:

`FULL` · `6"=1'` · `3"=1'` · `1 1/2"=1'` · `1"=1'` · `3/4"=1'` · `1/2"=1'` · `3/8"=1'` · `1/4"=1'` ·
`3/16"=1'` · `1/8"=1'`

— that is 1:1 through 1:96. The floor is 1:96 and is effectively unreachable: on a 15.75" drawable
area it corresponds to an 80-foot part. Legibility, not the floor, is the real bound.

### Policies

`PLLSET` → `Coarsen`:

| | Does |
|---|---|
| `Finest` | The finest rung that fits. No coarsening. |
| `Balanced` *(default)* | Coarsen while it keeps reducing the plate count, bounded by the parts-per-plate preference and the legibility floor. Ties go to the finer scale. |
| `Minimum` | Fewest sheets **per material**: the parts-per-plate preference is set aside in favour of the 60-viewport hard cap, and the scale coarsens to match. Expect crowded plates and small drawings. |

Choosing a policy other than `Finest` then prompts for the legibility floor — the smallest the
**typical** part in a material may plot on its short side, in paper inches, default `0.5`. Typical
means the median across the material, with parts that are degenerate on an axis excluded.

`Minimum` cannot go below one plate per material. A drawing with eight materials produces eight
plates under every policy, and no scale changes that.

---

## Title and layer conventions

| Thing | Default |
|---|---|
| Plate template layout | `ANSI B` |
| Drawable area | `0.125, 0.125` – `15.875, 10.375` |
| Viewport frames | `rVIEWPORTS` |
| Dimensions | `rDIMS_PAPER` |
| Labels | `0-Paperspace Text` |
| Title block attributes written | `$PLATE_NAME`, `$PIECE/PART_NAME`, `#DRAWING_DATE`, `#`, `##` |

Dimensions go on `rDIMS_PAPER` and that is deliberately *not* persisted in the settings, so a stale
saved value cannot move them off it.

Layers the plug-in has to create because the drawing lacks them are reported at the end of a run.
They are shop convention layers, so inventing one means the drawing was not built from the usual
template and its colour and plot settings are guesses.

### Material names

Layer names are read as: optional prefix, material code, thickness written with a hyphen for the
fraction, optional trailing qualifier.

```
rPLY_3-4      ->   3/4" Plywood | rPLY_3-4
PLY_1-2       ->   1/2" Plywood | PLY_1-2      (a shop that does not prefix its layers)
```

The raw layer name is always kept beside the translation, because that is the string someone
cross-referencing the cut file will search for.
