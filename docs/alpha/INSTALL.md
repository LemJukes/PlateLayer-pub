# Installing PlateLayer Alpha 1.4

AutoCAD 2026, Windows, 64-bit. AutoCAD LT cannot run it.

The same text ships as `INSTALL.txt` inside the ZIP.

---

## 1. Unblock the ZIP — before extracting it

**Do this first.** It is the single most common reason the plug-in appears to install and then does
nothing at all.

Windows tags every file downloaded from the internet, and **AutoCAD will not load a tagged DLL**.
Extracting a tagged ZIP copies the tag onto every file that comes out of it, so unblocking after
extraction means unblocking each DLL separately.

1. Right-click the downloaded ZIP → **Properties**
2. If there is an **Unblock** checkbox at the bottom of the General tab, tick it
3. **OK**

Or in PowerShell:

```powershell
Unblock-File -Path .\PlateLayer-Alpha-1.4.zip
```

No Unblock checkbox means the file was not tagged and there is nothing to do.

## 2. Extract

**Close AutoCAD first.** It holds a loaded plug-in DLL open for the life of the process, so an
install over a running AutoCAD fails or is ignored.

Copy the `PlateLayer.bundle` folder out of the ZIP into:

```
%APPDATA%\Autodesk\ApplicationPlugins\
```

Paste that path into the Explorer address bar to get there. When you are done you should have
exactly:

```
%APPDATA%\Autodesk\ApplicationPlugins\PlateLayer.bundle\PackageContents.xml
%APPDATA%\Autodesk\ApplicationPlugins\PlateLayer.bundle\Contents\PlateLayer.Cad.dll
%APPDATA%\Autodesk\ApplicationPlugins\PlateLayer.bundle\Contents\PlateLayer.Core.dll
```

Do not rename the folder. The `.bundle` suffix is how AutoCAD finds it.

## 3. Check

Start AutoCAD, open any drawing, type:

```
PLLSET
```

Its first line should read `PlateLayer Alpha 1.4`. That string is read off the assembly rather than
from a literal in the source, so if it says anything else you are running a different build than you
think you are. Answer `Quit` to leave without changing anything.

There is no `NETLOAD` step and no ribbon button in this alpha. The commands are typed.

---

## If PLLSET is not recognised

The failure is silent by design — AutoCAD does not report a plug-in it declined to load — and the
symptom is identical for all the causes. Work through them in order.

### 1. The zone marker

Most likely cause by a wide margin. If you extracted before unblocking, delete the folder you
extracted, go back to step 1, and redo it. **Unblocking the ZIP after extraction does not fix the
extracted files.**

### 2. The layout

`PackageContents.xml` must sit in the root of `PlateLayer.bundle`, and the two DLLs in a `Contents`
subfolder. One extra nested folder — `PlateLayer.bundle\PlateLayer.bundle\` — is the usual
extraction slip.

### 3. Trusted locations

AutoCAD only loads code from folders it trusts (`TRUSTEDPATHS`, gated by `SECURELOAD`). Type
`TRUSTEDPATHS` and check whether the list covers the plug-ins folder. If it does not, add it:

```
Options > Files > Trusted Locations > Add
%APPDATA%\Autodesk\ApplicationPlugins\...
```

The three dots are literal AutoCAD syntax and mean "and all subfolders". AutoCAD will warn that the
folder is not read-only; that warning is expected for a per-user folder and can be accepted.

**Never set `SECURELOAD` to 0.** That switches the executable-load check off for every drawing you
open, including ones you did not author. It is not needed here.

> **Known open question.** Autodesk documents three autoloader locations —
> `%PROGRAMFILES%\Autodesk\ApplicationPlugins`, `%ALLUSERSPROFILE%\Autodesk\ApplicationPlugins` and
> `%APPDATA%\Autodesk\ApplicationPlugins` — and states that the `%PROGRAMFILES%` one is trusted
> without a signature check. It makes no such statement about the other two, and AutoCAD 2026's
> current security page does not mention `ApplicationPlugins` at all. Whether `%APPDATA%` is trusted
> by default on an untouched AutoCAD 2026 is **not confirmed** either way, which is why step 4
> exists. If you hit this, saying so in a bug report is genuinely useful.
>
> Sources: [About Installing and Uninstalling Plug-In Applications](https://help.autodesk.com/cloudhelp/2019/ENU/AutoCAD-MAC-Customization/files/GUID-5E50A846-C80B-4FFD-8DD3-C20B22098008.htm)
> · [About Security and Protecting Against Viruses](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-9C7E997D-28F8-4605-8583-09606610F26D.htm).

### 4. Install machine-wide instead

If step 3 does not stick, use the location Autodesk's own documentation describes as trusted. Needs
administrator rights. Close AutoCAD, delete the copy under `%APPDATA%`, and put `PlateLayer.bundle`
here instead:

```
%PROGRAMFILES%\Autodesk\ApplicationPlugins\
```

Same folder layout, same everything else.

### 5. Wrong AutoCAD release

This alpha supports **AutoCAD 2026 only** — the manifest is bounded to `SeriesMin="R25.1"`,
`SeriesMax="R25.1"`. On any other release the bundle is correctly ignored, and ignored silently.
Check `HELP > ABOUT`.

### 6. Your own build replaced it

If you also build from source, be aware that building `PlateLayer.Cad` deploys its own bundle over
`%APPDATA%\Autodesk\ApplicationPlugins\PlateLayer.bundle` — the same folder the ZIP installs to.
So a local build silently replaces the release you installed, with whichever configuration you built
last. If the version string is not what you expect, that is usually why.

### Still nothing

Send the output of `TRUSTEDPATHS`, your AutoCAD version from the ABOUT box, and a screenshot of the
`PlateLayer.bundle` folder. Those three answer all of the above at once. See
[REPORTING.md](REPORTING.md).

---

## Before your first run

PlateLayer clones a paper-space layout as its plate template and reads shop conventions out of the
drawing rather than inventing them. Out of the box it expects a layout tab named `ANSI B` and a
title block carrying the attributes `$PLATE_NAME`, `$PIECE/PART_NAME`, `#DRAWING_DATE`, `#` and
`##`. `PLLSET` → `Template` points it at a different tab.

**Run `PLL` and answer `No` at the Proceed prompt first.** That is a dry run: it prints the whole
plan — plate count, scale per plate, fill, ignored geometry — and changes nothing.

## Updating

Close AutoCAD, **delete the whole `PlateLayer.bundle` folder**, then install the new ZIP from step 1.
Deleting first matters — a leftover file from an older version is not overwritten by a newer package
that no longer contains it.

## Uninstalling

Close AutoCAD and delete:

```
%APPDATA%\Autodesk\ApplicationPlugins\PlateLayer.bundle\
```

That is the whole uninstall. Nothing is written to the registry and nothing is installed elsewhere.

Two things are left behind on purpose, and neither affects AutoCAD: the plate layouts it drew, and
its settings — both of which live inside the drawings you ran it on rather than on the machine.

## Verifying the download

```powershell
Get-FileHash .\PlateLayer-Alpha-1.4.zip -Algorithm SHA256
```

against [`dist/PlateLayer-Alpha-1.4.zip.sha256`](../../dist/PlateLayer-Alpha-1.4.zip.sha256).
