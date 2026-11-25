# OpenBoxBridge 1.1 - UP300 test 8 (pre-release)

**For UP300 testers.** UP Box+ owners should stay on [1.0.1](https://github.com/LB-KR/openboxbridge/releases/tag/v1.0.1).

**Download:** `OpenBoxBridge-1.1-UP300-test8.zip`. Unzip it (not in OneDrive or Program Files)
and follow `UP300-TESTING.txt`: please do the extrude test (step 7) again, then step 8.

## What changed since test 7

- **Speed fix:** until now every move on the UP300 ran **1.6 times too fast**. The converter
  timed the moves for the UP Box+'s 50 MHz clock, but the UP300's is 80 MHz. Prints now run
  at the speeds the slicer set, so they take longer than before - that's expected. Tall,
  thin prints (like temperature towers) should do much better.
- **Extruder:** it also ran 1.6 times too fast, so please measure the extruder again.
- **Check:** if a UP300 ever reports a different clock, the Console says so.
- The UP Box+ is not affected: its converted jobs are byte-for-byte the same as before.

Test 7 (included): UP300 slicer profiles ("UP300 0.20mm" etc.), a warning before printing a
file with a scarf seam, spiral vase or sloped z-hop, typed calibration values count as set,
and a second stop after a printer error.

**Note:** on the UP300 the PC and USB cable must stay connected for the whole print, and the
PC mustn't go to sleep. Use the "UP300 USB" printer profile, or one with a print area centred
on 0,0.

Thanks to Alex Wyatt for testing. Technical details: [docs/UP300.md](https://github.com/LB-KR/openboxbridge/blob/main/docs/UP300.md).
