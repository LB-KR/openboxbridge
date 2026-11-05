# OpenBoxBridge 1.1 - UP300 test 5 (pre-release)

**For UP300 testers.** UP Box+ owners should stay on [1.0.1](https://github.com/LB-KR/openboxbridge/releases/tag/v1.0.1).

**Download:** `OpenBoxBridge-1.1-UP300-test5.zip`. Unzip it and follow `UP300-TESTING.txt`.
If test 4 got you through steps 1-5, go straight to step 6.

## What changed since test 4

Test 4 printed the first 20 mm cube on a UP300, streamed from the app. But Stop in the app
waited until the whole job had been sent, and the printer's own button does not stop a
streamed job, so there was no way to stop the print.

- **Stop works while streaming.** The streaming tool now stops the job itself on the USB
  connection it already holds: it sends no more data, stops the program, clears the printer's
  buffer and turns the heaters off. The app then checks the stop.
- **Closing the app while streaming** asks first, then stops the print the same way (before,
  the printer carried on with what it had already received).
- **Clearer messages:** the streaming confirmation no longer says to use the printer's button,
  and a failed stream no longer says "Write to SD failed".

Thanks to Alex Wyatt for testing. Technical details: [docs/UP300.md](https://github.com/LB-KR/openboxbridge/blob/main/docs/UP300.md).
