# OpenBoxBridge 1.1 - UP300 test 7 (pre-release)

**For UP300 testers.** UP Box+ owners should stay on [1.0.1](https://github.com/LB-KR/openboxbridge/releases/tag/v1.0.1).

**Download:** `OpenBoxBridge-1.1-UP300-test7.zip`. Unzip it (not in OneDrive or Program Files)
and follow `UP300-TESTING.txt`. If steps 1-7 were fine before, go straight to step 8.

## What changed since test 6

Test 6 found why prints stopped with "JogCmd Error": Orca's **scarf joint seam** moves the
nozzle up while it prints, and the UP printers can only do that with a separate jog command
for every move. A job with thousands of them stops part-way.

- **UP300 slicer profiles:** process "UP300 0.15mm Fine / 0.20mm / 0.25mm Draft" and filament
  "UP PLA (UP300)" / "UP ABS (UP300)" for OrcaSlicer, Bambu Studio and PrusaSlicer, with the
  scarf seam and spiral vase off. Choosing printer "UP300 USB" picks them. The UP Box+
  profiles are unchanged.
- **Warning before printing** a file with a scarf seam, spiral vase, sloped z-hop or ramping
  lift, which can stop UP printers part-way.
- **Setup:** a nozzle height or bed centre typed in counts as set (no more "not measured"
  warning), and the Calibrate window explains its "Go to" limit.
- **After a printer error** the app stops once more so the printer resets, and leaves
  Initialise to you.
- **Note:** on the UP300 the PC and USB cable must stay connected for the whole print, and
  the PC mustn't go to sleep.

Thanks to Alex Wyatt for testing. Technical details: [docs/UP300.md](https://github.com/LB-KR/openboxbridge/blob/main/docs/UP300.md).
