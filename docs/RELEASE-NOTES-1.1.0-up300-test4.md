# OpenBoxBridge 1.1 - UP300 test 4 (pre-release)

**For UP300 testers.** UP Box+ owners should stay on [1.0.1](https://github.com/LB-KR/openboxbridge/releases/tag/v1.0.1).

**Download:** `OpenBoxBridge-1.1-UP300-test4.zip`. Unzip it and follow `UP300-TESTING.txt`.
If test 2 got you through steps 1-5, go straight to step 6.

## What changed since test 2

Test 2 showed that homing, jogging, the bed centre and the nozzle height all work on the UP300,
and 10 mm jogs measured 10 mm. Jobs didn't run: the UP300 can't read a job stored the UP Box+ way,
and showed "SD Card Reading Error" then "JOG Command Error".

- **Jobs are streamed on the UP300.** With "Sending a job" on Automatic, the job is sent to
  the printer's memory while it runs, the same way home and jog moves are sent. Keep the USB
  cable and PC connected until the print ends; use the printer's own button to stop it.
- **No more JOG Command Error from the Control page.** After homing, the head is in a corner
  just outside the UP300's travel. The app now refuses a move the printer would reject and
  explains which way works.
- **Type a height in the nozzle calibration.** A box with "Go to mm" moves the bed straight to
  a height (e.g. 225) before the fine steps. It stops 1 mm short of a saved nozzle height.
  This also works on the UP Box+.
- **Check printer** also writes the steps/mm to the Console.
- High-DPI dialog fix from 1.0.1 is included.

Thanks to Alex Wyatt for testing. Technical details: [docs/UP300.md](https://github.com/LB-KR/openboxbridge/blob/main/docs/UP300.md).
