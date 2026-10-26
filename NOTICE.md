# Notices

## OpenBoxBridge

Copyright the OpenBoxBridge contributors. Licensed under the GNU General Public License, version 3
or (at your option) any later version (see `LICENSE`).

Not affiliated with or endorsed by Tiertime. "UP", "UP Box" and "UP Studio" are trademarks of
Beijing Tiertime Technology Co., Ltd. They are used only to describe compatibility.

## UP3D (`up3d/`)

Original work by Maik Stohn and Kai Scheffer: https://github.com/MaikStohn/UP3D, continued at
https://github.com/UP3D-gcode/UP3D (based on commit 97864b3452ca47a4d166dab9288e1999fb507523).
The UP3D repository's top-level licence file is the GNU GPL version 2 (kept as `up3d/LICENSE`),
while most of its source files state "GNU General Public License version 3 or (at your option) any
later version", and its motion planner (`hostplanner.c`, `hoststepper.c`) is derived from Grbl
(https://github.com/grbl/grbl), also GPL-3.0-or-later. OpenBoxBridge is therefore distributed as a
whole under GPL-3.0-or-later, and each file keeps its own header.

OpenBoxBridge's modifications are listed in `docs/up3d-changes.diff` and summarised in
`docs/CHANGES.md`. New files: `up3d/UP3DTOOLS/upcenter.c`, `upctl.c`, `upsnap.c`.

## libusb

The Windows printer tools are statically linked with libusb 1.0.27 (https://libusb.info),
GNU Lesser General Public License, version 2.1. libusb was used unmodified; its source is available
from https://github.com/libusb/libusb/releases/tag/v1.0.27. To relink the tools against another
libusb, use `build/build-tools.sh` with your own `LIBUSB` folder.
