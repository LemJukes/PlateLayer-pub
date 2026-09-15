# Changelog

Versioning is `Alpha 1.#`. The number lives in `Directory.Build.props`, `PLL` prints it off the
assembly, and `dev/pack.ps1` refuses to package a build whose manifest disagrees with it.

## Alpha 1.4 — 2026-09-15

First public build. Alpha: in use by its author on real cut files, not yet through many hands.

- `PLL` / `PLATELAYER` — select part geometry, review the plan, generate paper-space plates with
  per-plate approval (`Accept / Redo / Skip / aLl / eXit`). Answering `No` at the Proceed prompt is
  a dry run: the plan prints and the drawing is untouched.
- `PLLSET` — template layout, parts per plate, scale mode, scale policy, dimension strategy,
  viewport margin, per-part labels, material code table, approval mode.
- One viewport per part, one scale per plate, one material per plate. Each part carries an X and a
  Y dimension and nothing else.
- Three scale policies — `Finest`, `Balanced` (default), `Minimum` — with a legibility floor
  measured on the median short side of the material rather than the smallest part.
- Plate balancing: parts are dealt across a material's plates by area so the plates come out
  similarly full, and the attempt is abandoned if the even split would not pack.
- Fill reporting: every plate states its fill in the dry run, and any plate under 35% is named.
- The run summary names whichever constraint decided the sheet count — the one-plate-per-material
  floor, or the parts-per-plate cap.
- Material names expanded from the layer convention against an editable code table seeded with
  `PLY`; the raw layer name is always kept beside the translation, and an unrecognised code is
  reported rather than guessed at.
- Title block: writes `$PLATE_NAME`, `$PIECE/PART_NAME`, `#DRAWING_DATE`, `#`, `##` and nothing
  else. Operator-owned fields are stripped from the write set unconditionally, in `PlateLayer.Core`,
  so a misconfiguration cannot become a silent overwrite of someone's project name.
- Fractions written as MText stacks, so a plug-in dimension reads the same as a hand-typed one.
- Every tile reserves arrowhead room on every edge, because whether AutoCAD pushes a dimension's
  text and arrows outside its extension lines is decided by `DIMATFIT`, `DIMTIX`, `DIMTOFL` and the
  text style, and predicting it was tried and got it wrong.
- Prompt keyword sets live in `PlateLayer.Core` and are validated for shortcut collisions at run
  time and at build time — AutoCAD accepts a colliding set without complaint and silently makes the
  second keyword unreachable.
- Ignored geometry is reported per layer with counts, rather than as a single total.
- Autoloader bundle: install by copying a folder, no `NETLOAD`.
- AutoCAD 2026 only (`SeriesMin="R25.1"`, `SeriesMax="R25.1"`).

### Known rough edges in this build

- **Not 1.0.** The approval-loop paths and the error handling have not been exercised hard against
  production files.
- **No rotation-aware packing.** Parts are tiled at the orientation they arrive in, which leaves
  yield on the table for long thin parts.
- **No part ids and no plot/PDF export.** A plate has no cross-reference back to the cut file beyond
  its material and its dimensions.
- **The sparse-plate threshold is a calibration, not a constant.** Fill counts reserved-but-blank
  annotation room as used sheet, so the 35% line has to be re-tuned whenever tile geometry moves.
- **No ribbon and no toolbar.** The commands are typed.
