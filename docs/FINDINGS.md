# Why prints leaned, and how it was fixed

Prints made with the UP3D converter on an UP Box+ leaned: each layer landed slightly offset from the
one below, so a 20 mm cube came out as a parallelogram. The lean was identical from print to print.
UP Studio printed the same model straight on the same machine. Other UP Box and UP Mini owners
reported the same thing in [MaikStohn/UP3D#31](https://github.com/MaikStohn/UP3D/issues/31)
("drifting ... with every layer", "printing at a 3-5 degree angle"). It was never resolved there.

## What was ruled out

| Suspect | How it was checked | Result |
|---|---|---|
| Steps/mm or scaling | 20 × 20 mm cube measured with calipers | Correct size |
| Skew compensation stored by UP Studio | Read the printer's info block (XY/ZX/ZY scale) | All zero, nothing to apply |
| Converter step rounding vs the printer | Replayed UP Studio's own job against its absolute-position checkpoints | The printer floors per segment, as the converter assumes (UP Studio's checkpoints agree to ±1 step) |
| Fast travels sent as direct jog moves | Orca writes z-hop travels as `G1 X.. Y.. Z..`; these were going out as 120 mm/s jog moves. Fixed. | Same lean, so not the cause |
| Accumulated software drift | Simulated every motion block; positions are consistent at every layer | No drift in the file itself |

## Comparing with UP Studio

A complete UP Studio job was recorded with USBPcap and its motion blocks were extracted
(`tools/pcapumc.py`). Compared with the converter's output for the same model:

| | UP Studio | UP3D converter (before) |
|---|---|---|
| Fastest printing move | **26 mm/s** (its "30/45/50 mm/s" settings are not head speeds) | 60–70 mm/s, up to 135 mm/s on diagonals |
| Acceleration | 250–750 mm/s² | 300 mm/s² |
| Travel moves | printer's own jog routine (MoveF) | planned motion segments (MoveL) |
| Segment length | always < 512 ticks (max 499) | up to 65 535 ticks |
| Idle axis speed | never 0 (small non-zero value) | 0 |
| Steps per tick | ≤ 12.8 | up to 64 |

The original author's diagnosis in #31 was that the printer is "instructed to drive 100 steps but is
doing 99 only but reporting back 100". The printer's own position counter is fooled, so absolute
re-positioning can't correct it. Driving the motors the way UP Studio does avoids it.

## Changes in OpenBoxBridge

1. **Printing moves capped at 25 mm/s** (`UMC_PRINT_MAX` in `umcwriter.c`), like UP Studio.
2. **Travel moves (no extrusion, ≥ 1 mm) sent as jog moves** (`MoveF`), like UP Studio, capped at
   100 mm/s. Z moves are capped at 15 mm/s.
3. **Motion segments re-cut UP Studio-style** (`umcwriter_write_movel`): ≤ 500 ticks, about 4 kHz
   ticks, and speed 1 instead of 0 for idle axes. Exact step counts are preserved per segment.
4. **Bug fix:** constant-speed segments were timed by the longest single axis rather than the path
   length (`hoststepper.c`), so diagonals ran up to 41% faster than planned. The speed also jumped
   at the end of each acceleration phase.
5. Lines that only name the Z they're already at are treated as ordinary moves. A real combined XY+Z
   move is split into lift then travel, or travel then lower.

Result: the same G-code that leaned identically twice printed as a square cube. (Print time for the
20 mm test cube: about 1 h 10 min, close to UP Studio.)

## Also found along the way

- **Nozzle height:** UP Studio's figure (≈206 mm on this printer) is measured from a different zero
  point than the converter's homing (≈232 mm). The app measures it in the converter's own frame (see
  also UP3D #11, where 233.71 vs 208.12 was reported).
- **STOP:** clearing the program buffer doesn't stop a job running from the printer's memory.
  Setting parameter `0x0D` ("stop program") does.
- **Stuck uploads:** `UP3D_WriteBlocks` waited forever when the printer stopped answering, holding
  the USB connection. It now gives up.

## Tools

- `tools/pcapumc.py capture.pcap` extracts the motion blocks UP Studio sent (USBPcap, link type 249).
- `tools/umcsim.py job.umc` replays a `.umc` file with the printer's step arithmetic.
