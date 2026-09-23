# Notice

Copyright © 2026 Rob Garner. **All rights reserved.**

There is no open-source licence on this repository yet. That is deliberate, not an oversight — the
licence question is still open, and publishing under a permissive licence is a one-way door. Until a
`LICENSE` file appears here, the terms are these:

## What you may do

For the duration of the alpha, you are granted permission to:

- download the release ZIP from `dist/`, install it, and use it — including commercially, in a
  shop, on paying work;
- read the source in this repository;
- build it yourself and run your own build;
- report bugs, and attach drawings or code excerpts needed to do so.

No fee, no registration, no time limit on a copy you already have.

## What you may not do

Without written permission: redistribute the plug-in or its source, host it elsewhere, publish
modified versions, or incorporate the code into another project.

## Why it is worded this way

"All rights reserved with a use grant" can be relaxed later; a permissive licence cannot be
retracted from code already published under it. Tightening is the direction that does not work, so
the alpha starts at the end that leaves the decision open.

The reasoning behind this decision is recorded in the JukeBox project's internal licensing research —
not a publicly accessible document.

## Third-party code

None of Autodesk's code is redistributed here. `PlateLayer.Cad` references `acdbmgd`, `accoremgd`
and `acmgd` from a locally installed AutoCAD, in place and with `<Private>false</Private>`, so the
host supplies those assemblies at runtime and none of them are in the ZIP — `dev/pack.ps1` fails the
release if one turns up in the package. `System.Text.Json` is in-box in the .NET 8 shared framework
the host provides. The two DLLs in the package — `PlateLayer.Cad.dll` and `PlateLayer.Core.dll` —
are the whole payload, and both are this project's own code.

**AutoCAD LT cannot run this and never will.** LT has no .NET plug-in API.
