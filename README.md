# OpenBoxBridge

**Print to a Tiertime UP Box+ over USB from OrcaSlicer, PrusaSlicer or Bambu Studio. No UP Studio needed.**

OpenBoxBridge is a small Windows app plus an improved version of the open-source
[UP3D](https://github.com/UP3D-gcode/UP3D) printer tools. You slice in a modern slicer, press
*Export G-code*, and OpenBoxBridge converts the file to the UP's own machine format and sends it to
the printer down the USB cable.

> Made for and tested on the **UP Box+** (firmware ROM 6.15) on Windows 10/11.
> Other UP models (UP Box, UP Mini, UP Plus 2) may work but are untested.
>
> OpenBoxBridge is not affiliated with or endorsed by Tiertime. "UP" and "UP Box" are trademarks of
> Beijing Tiertime Technology Co., Ltd., used here only to say which printer this works with.

## Features

- **One-click hand-off from the slicer.** Presets for OrcaSlicer, PrusaSlicer and Bambu Studio; every
  export opens in OpenBoxBridge. Files sliced for other printers are ignored.
- **Dashboard.** Job card with thumbnail, progress, layer, elapsed time, time left and finish time;
  live nozzle and bed temperature graph; heat, cool down, jog, home, extrude and withdraw.
- **Nozzle-height calibration** measured on the printer (paper test with read-back of the bed position).
  UP Studio's number is measured from a different zero point and is about 25 mm off in this app's terms.
- **Bed-centre finder** and a 180° option (the UP Box+ runs X and Y the opposite way to most slicers).
- **Several printers.** Each printer, identified by serial number, keeps its own calibration; the app
  switches automatically.
- **STOP that works** for jobs running from the printer's memory.
- **Filament tracking**, print history, and the job is picked up again after a USB disconnect or
  app restart.
- **Reliability-first presets**: PLA and ABS, 0.15 / 0.20 / 0.25 mm, raft tuned for the perforated
  board, speeds matched to what UP Studio actually uses on this printer.

## The layer-lean fix (UP3D issue #31)

Prints converted with the original UP3D tools lean a little more with every layer on the UP Box and
UP Mini ([MaikStohn/UP3D#31](https://github.com/MaikStohn/UP3D/issues/31), open since 2017).
OpenBoxBridge fixes this. We recorded UP Studio's USB traffic for a whole job and compared it,
instruction by instruction, with the converter's output. The converter now drives the printer the
way UP Studio does:

- printing moves capped at 25 mm/s (UP Studio never exceeds about 26 mm/s on this machine,
  whatever its speed settings say);
- travel moves sent to the printer's own jog routine;
- motion segments under 512 ticks and never a speed of exactly 0 on an idle axis;
- a bug fixed where diagonal moves ran up to 41% faster than planned.

A 20 mm test cube that leaned identically in two prints came out square. Details:
[docs/FINDINGS.md](docs/FINDINGS.md).

## Download and install

1. Download `OpenBoxBridge-<version>-windows.zip` from **[Releases](../../releases)** and unzip the
   whole folder somewhere permanent (for example Documents). Don't run it from inside the zip.
2. The PC needs the UP printer's USB driver, which **UP Studio** installs. If UP Studio has ever
   talked to the printer on this PC, it's there. Close UP Studio while using OpenBoxBridge, because
   only one program can use the printer at a time.
3. Run `OpenBoxBridge.exe`. Windows may say it's from an unknown publisher (it isn't code-signed):
   choose **More info > Run anyway**.
4. Follow *One-time setup* in the included `README.txt`:
   - check the printer;
   - **find the bed centre**;
   - **calibrate the nozzle height**;
   - install the presets for your slicer.

## Using it

1. In your slicer choose printer **UP Box+ USB**, print profile **UP Box+ 0.20mm** and filament
   **UP PLA (Box+)**.
2. Slice and **Export G-code**. OpenBoxBridge opens with the file.
3. Press **Print**. Watch the first layer of your first prints; the printer's own button is always
   your emergency stop.

## Repository layout

| Folder | What's in it |
|---|---|
| `app/` | The Windows app (C#, WinForms, .NET Framework 4.8) and its build script |
| `up3d/` | The UP3D printer tools with OpenBoxBridge's changes (C) |
| `presets/` | OrcaSlicer, PrusaSlicer and Bambu Studio presets |
| `tools/` | Preset generator, `.umc` simulator and USB-capture decoder used during development |
| `tests/` | A no-heat test job (head moves in the air) for checking STOP |
| `build/` | Build scripts for the printer tools |
| `docs/` | Findings, the full patch against upstream UP3D, release notes |
| `release/` | The user guide that goes into the download (`README.txt`) |

## Building from source

**Printer tools:** cross-compiled with [llvm-mingw](https://github.com/mstorsjo/llvm-mingw) and a
static libusb 1.0.27:

```
MINGW_BIN=/path/to/llvm-mingw/bin LIBUSB=/path/to/libusb ./build/build-tools.sh     # -> out/bin/*.exe
./build/build-transcode-linux.sh                                                  # native converter for testing
```

**App:** built with the C# compiler inside PowerShell 7; no Visual Studio needed:

```
pwsh ./app/build-app.ps1        # -> out/OpenBoxBridge.exe
```

To assemble a release, put `OpenBoxBridge.exe`, `out/bin/` (as `bin/`), `presets/orca`,
`presets/prusaslicer`, `presets/bambustudio` (each at the top level), `tests/` and
`release/README.txt` in one folder.

## How it talks to the printer

The UP doesn't understand G-code. `up3dtranscode` plans the motion (a Grbl-derived planner) and
writes UP machine code (`.umc`). `up3dload` stores the job on the printer over USB and starts it.
`up3dstatus` polls temperatures and progress and handles STOP and heaters. `up3dctl` and
`up3dcenter` handle jogging, extruding and calibration moves.

## Safety

This software drives a machine with a hot nozzle and a heated bed. Calibrate before printing, watch
first prints, and keep the printer's own button within reach. Provided without warranty
(see the licence).

## Credits and licence

- [UP3D](https://github.com/MaikStohn/UP3D) by Maik Stohn and Kai Scheffer, continued at
  [UP3D-gcode/UP3D](https://github.com/UP3D-gcode/UP3D): the protocol work and original converter
  this is built on (GPL; most files GPL-3.0-or-later).
- [libusb](https://libusb.info): LGPL-2.1, statically linked in the Windows printer tools.

OpenBoxBridge is free software under the **GNU General Public License v3.0 or later**; see [LICENSE](LICENSE)
and [NOTICE.md](NOTICE.md). The full set of changes to UP3D is in
[docs/up3d-changes.diff](docs/up3d-changes.diff), against upstream commit `97864b3`.
