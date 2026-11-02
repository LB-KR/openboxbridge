# OpenBoxBridge 1.1 - UP300 test version (pre-release)

**For UP300 testers.** UP Box+ owners should stay on [1.0.0](https://github.com/LB-KR/openboxbridge/releases/tag/v1.0.0) unless
they want to help test.

Experimental support for the **Tiertime UP300**. It was worked out from USB recordings of UP Studio 3
driving a UP300, and hasn't been tested on one yet. Thanks to Alex Wyatt for the recordings.

**Download:** `OpenBoxBridge-1.1-UP300-test.zip`. Unzip it, then follow `UP300-TESTING.txt` step
by step, starting with the tests that don't heat or print.

## What's new

- **UP300 detected automatically.** The app finds its USB channel, reads its printer details and
  uses its motor steps (161 steps/mm).
- **New UP300 section on the Setup page:**
  - connection status;
  - extruder steps/mm (a starting value of 213.5, to be checked with the Extrude test);
  - how to send jobs (automatic, either storing method, or streaming while it prints);
  - a read-only diagnostics button and the testing guide.
- **Two ways of storing a job on the printer,** tried in turn, with streaming offered as a last
  resort.
- **Gentler nozzle-height calibration on the UP300:** the fast move stops at 150 mm.
- **For the UP Box+:** the same features as 1.0, plus the seam presets (wipe, Z-hop on top
  surfaces only, 15% seam gap) and Classic walls with 4.5 mm retraction minimum travel.

Technical details: [docs/UP300.md](https://github.com/LB-KR/openboxbridge/blob/main/docs/UP300.md).

## Please report

Open an issue with:
- the files from **Run diagnostics**;
- the Console text (Console > Copy all);
- the measurements asked for in `UP300-TESTING.txt`.
