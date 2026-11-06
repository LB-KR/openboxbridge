# OpenBoxBridge 1.1 - UP300 test 6 (pre-release)

**For UP300 testers.** UP Box+ owners should stay on [1.0.1](https://github.com/LB-KR/openboxbridge/releases/tag/v1.0.1).

**Download:** `OpenBoxBridge-1.1-UP300-test6.zip`. Unzip it and follow `UP300-TESTING.txt`.
If steps 1-7 were fine with test 5, go straight to step 8.

## What changed since test 5

In test 5 a 20 mm cube stopped at the same point twice: the printer ended the job by itself
("JogCmd Error"), the app didn't notice, and the heaters stayed on.

- **Safety:** if the printer ends a job by itself, the app says so straight away, shows the
  printer's error and turns the heaters off. A stall or a USB failure also turns them off.
- **UP300 slicer profile:** a separate "UP300 USB" printer profile for OrcaSlicer,
  PrusaSlicer and Bambu Studio, with a print area the UP300 can reach. The print and filament
  profiles work with both printers. The app warns if a UP300 job was sliced with the UP Box+
  profile, and it now accepts files from UP300 profiles.
- **Bed heats first:** the bed is heated and the job is sent when the bed is really hot. A
  hot nozzle is turned down to 150 C meanwhile so it doesn't drip.
- **Status while streaming:** temperatures, layer and progress show during the print.
- **After Stop** the head moves away (Initialise) so you can reach the print.
- **Extruder:** default 235 steps/mm (measured in test 5); an untouched 213.5 is changed.
- **Diagnostics:** the app no longer ends the diagnostics' snapshot tool.
- **Note:** on the UP300 the PC and USB cable must stay connected for the whole print, and
  the PC mustn't go to sleep.

Thanks to Alex Wyatt for testing. Technical details: [docs/UP300.md](https://github.com/LB-KR/openboxbridge/blob/main/docs/UP300.md).
