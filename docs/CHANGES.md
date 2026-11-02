# Changes to UP3D

Against [UP3D-gcode/UP3D](https://github.com/UP3D-gcode/UP3D) at commit `97864b3`. The full patch
is in [`up3d-changes.diff`](up3d-changes.diff).

## Converter (`up3dtranscode`)

- Motion like UP Studio: printing moves ≤ 25 mm/s; travels sent as printer jog moves (≤ 100 mm/s);
  Z ≤ 15 mm/s; segments ≤ 500 ticks with non-zero idle-axis speeds. This fixes the per-layer lean
  (MaikStohn/UP3D#31); see [FINDINGS.md](FINDINGS.md).
- Diagonal cruise speed bug fixed (was up to 41% too fast).
- Z-hop travel lines (`G1 X Y Z<same>`) treated as normal moves; combined XY+Z moves split safely.
- Bed-centre X/Y offset and an optional 180° rotation (the UP Box+ runs X and Y opposite to most
  slicers).
- Unknown G/M commands are skipped and listed, not fatal.
- G2/G3 arcs split into 0.5 mm segments.
- Relative extrusion (M83) fixed when positioning is absolute.
- Layers counted by the first extruding move at a new height.
- Correct exit codes, a time estimate that includes direct moves, and an X/Y speed cap of 100 mm/s.
- Bed heater set as a countdown (UP Box+).

## Printer tools

- `up3dstatus`:
  - STOP uses the printer's "stop program" parameter and turns the heaters off;
  - heaters, initialise, `pos` (position read-back);
  - the status line includes temperatures, targets, position and serial number.
- `up3dctl` (new): home, jog, extrude and withdraw.
- `up3dcenter` (new): go to a position after homing exactly as a print does (bed-centre and
  nozzle-height calibration).
- `up3dsnap` (new): read-only dump of the printer's parameters and info block.
- USB: opens any Tiertime device (VID 0x4745), with clearer error messages.
- Block writes give up when the printer stops answering, instead of hanging while holding USB.
- Starting a program clears a pending stop or pause request first.

## UP300 (experimental, 1.1 test)

- USB:
  - bulk endpoints are read from the device descriptor (the UP300 sends on 0x02, not 0x01);
  - answers are buffered so a single large transfer works.
- The printer-info block is parsed in either layout (UP Box+ in pieces, UP300 in one block).
  Model id is in the status line.
- Machine type `up300` sets X/Y to 161 steps/mm and the extruder to 213.5 steps/mm by default.
  The `UP3D_ESTEPS` environment variable overrides the extruder value.
- `up3dload` can store jobs three ways:
  - method 0 (UP3D, now with a 60 s confirmation wait);
  - method 1 (UP Studio's commit-to-slot-9 sequence);
  - method 2 (streaming).

  The default tries method 0, then method 1.
- `up3dsnap` also saves the raw info answers and the USB product id.
- `tools/usbcap.py` reads `.pcap` and `.pcapng` captures. See [UP300.md](UP300.md).
