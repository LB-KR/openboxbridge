# OpenBoxBridge 1.0.0

First public release. Print to a **Tiertime UP Box+** over USB from OrcaSlicer, PrusaSlicer or
Bambu Studio, without UP Studio.

**Download:** `OpenBoxBridge-1.0.0-windows.zip` (Windows 10/11, 64-bit). Unzip the whole folder,
run `OpenBoxBridge.exe`, and follow *One-time setup* in `README.txt`.

## Highlights

- **Fixes the per-layer lean** of UP3D-converted prints
  ([MaikStohn/UP3D#31](https://github.com/MaikStohn/UP3D/issues/31)) by driving the printer the
  way UP Studio does. See [docs/FINDINGS.md](docs/FINDINGS.md).
- **Slicer presets** for OrcaSlicer, PrusaSlicer and Bambu Studio, installed from the app, with
  automatic hand-off after export.
- **Calibration done on the printer:**
  - nozzle-height paper test with bed position read-back;
  - bed-centre finder;
  - a separate calibration per printer (by serial number).
- **Dashboard:**
  - progress, layer, time left and finish time;
  - temperature graph;
  - heat, jog, extrude and withdraw;
  - a STOP that ends jobs running from the printer's memory.
- **Filament tracking and print history**, and the job is picked up again after a disconnect.

## Requirements and notes

- The UP printer's USB driver, installed by UP Studio. Close UP Studio while printing with
  OpenBoxBridge.
- Windows will warn that the app is unsigned: **More info > Run anyway**.
- Tested on an UP Box+ (ROM 6.15). Other UP models are untested.
- Not affiliated with Tiertime. GPL-3.0-or-later.
