# Reporting a bug

Email **[rob@rjgarner.com](mailto:rob@rjgarner.com)** with "PlateLayer" in the subject line.

## The two things that matter most

**Attach the drawing**, and **paste the dry-run text**.

A bug report is useful roughly in proportion to whether the geometry that caused it came with it. If
a plate comes out wrong, the cut file that produced it is worth more than any description — and if
the full drawing is not something you can send, the parts from the one material that misbehaved,
copied into a blank drawing with your layout template, is enough.

The dry run is the next best thing and costs nothing: `PLL`, select, answer `No` at the Proceed
prompt, then `F2` for the text window. It carries the part count, the material split, the scale and
fill per plate, whatever geometry was ignored and on what layer, and which constraint decided the
plate count. Most reports about plate counts and scales are answered by that text alone.

## What else to include

- **AutoCAD release**, from `HELP > ABOUT` — the full line, not just the year.
- **What `PLLSET` prints.** It is the whole settings state in one block. If it prints nothing, that
  is itself the report — see below.
- **What you did**, in numbered steps.
- **What you expected, and what happened instead.**
- **Every time, or once?**
- A screenshot or a PDF of the plate, if the fault is something you can see on paper.

## If the plug-in will not load at all

Work through [INSTALL.md](INSTALL.md)'s troubleshooting first — most of the causes are fixable from
your end in under a minute, and one is a supported-release mismatch.

If none of it helps, send these three and nothing else is needed:

1. The output of `TRUSTEDPATHS`
2. The AutoCAD version from the ABOUT box
3. A screenshot of your `PlateLayer.bundle` folder, expanded to show `Contents`

Say whether the ZIP was unblocked before extraction. "I think so" is a useful answer; guessing Yes is
not.

## Things that are known, not bugs

Reporting these again is fine but will not tell anyone anything new:

- **More sheets than expected.** Read the last lines of the dry run. One plate per material is a
  hard floor no scale policy crosses, and past that it is usually the parts-per-plate cap. The
  summary names whichever it was.
- **A plate that is mostly empty.** A material holding two small parts cannot fill a sheet, and
  splitting them across two plates at a finer scale is worse. Plates under 35% are named in the run
  summary because it is a decision, not an accident.
- **A hairline part drawn tiny beside large panels.** Not fixable by scale — the only thing that
  would fix it is giving that part its own sheet.
- **Parts tiled at the orientation they arrived in.** Rotation-aware packing is not built.
- **No scale printed on the sheet.** Deliberate. The dimensions carry the real measurements.
- **No part ids, no plot or PDF export, no ribbon button.** Not built.
- **Nothing happens on AutoCAD 2025 or 2027.** The manifest is bounded to 2026, and out-of-range
  releases ignore the bundle silently.

## Things that are worth reporting even though they look like the above

- **A material missing from a run entirely.** That has happened for real: parts drawn on a layer the
  default exclusion list drops. Ignored geometry is now reported per layer with counts — if that
  report names a layer you believe holds parts, say so.
- **A layer the plug-in had to create.** It means the drawing was not built from the usual template,
  and the new layer's colour and plot settings are guesses.
- **An unrecognised material code.** Input to this tool is sheet goods by contract, so either the
  table is missing a sheet good or something reached a plate that should not have.
- **Annotation touching a part, a neighbour, or the border.** Tile geometry reserves room for
  dimension text and arrowheads, and a case where that allowance is not enough is a real fault.

## Feature requests

Same address, "PlateLayer feedback" in the subject. Requests that would push PlateLayer into nesting,
yield optimisation, toolpaths or kerf are out of scope on purpose rather than unbuilt — those belong
downstream — so raise those only if you think the scope line itself is in the wrong place.
