OpenBoxBridge  -  USB printing for the Tiertime UP Box+
=======================================================

Slice in OrcaSlicer, PrusaSlicer or Bambu Studio and print on a Tiertime
UP Box+ over the USB cable. No UP Studio needed for printing.

Made for and tested on the UP Box+ (firmware ROM 6.15). Free software
(GPL-3.0-or-later). Not affiliated with or endorsed by Tiertime; "UP" and "UP Box"
are trademarks of Beijing Tiertime Technology Co., Ltd., used here only to
say which printer this works with.

  Slicer "Export G-code"  ->  this app opens with the file  ->  Print
  (the app converts it to the UP's own format and sends it down the USB cable)


WHAT'S IN THIS FOLDER
---------------------
  OpenBoxBridge.exe  The app. Start here. The slicers call it after every
                     export made with the UP Box+ USB printer profile.
  bin\               The UP3D tools that talk to the printer (converter,
                     uploader, status/heaters, controls, bed-centre finder).
  orca\              Orca presets: printer, print settings, ABS and PLA.
  prusaslicer\       The same presets for PrusaSlicer.
  bambustudio\       The same presets for Bambu Studio.
  tests\             STOP test (no heating).
  jobs\              Copies of files sent from the slicer, converted .umc files and
                     history.txt (your print history). Safe to tidy up.
  settings.json      Your nozzle height, bed centre, spool and preferences.
  source\            Source code for everything here (GPL-3.0-or-later).


THE APP
-------
  Dashboard  Print job card (thumbnail, progress, layer x/y, elapsed, time
             left, finish time), temperature graph for nozzle and bed
             (hover for values, 5/10/30/60 min), heat / cool down, jog
             controls, extrude / withdraw, and filament left on the spool.
  Jobs       Recent jobs with thumbnails: load, print again, show in folder.
  Setup      Nozzle height calibration, bed centre, motion, 180-degree turn, printer
             check, slicer presets, dark theme.
  Console    Everything the printer tools reported.
  Top bar    Status badge, Power off and STOP.

Filament tracking is an estimate: each print subtracts what the slicer says it
uses (only the part printed if you cancel). Use "New spool..." when you load
a spool and "Adjust..." if you weigh it.

You can unplug the USB cable or close the app once a print has started - it
runs from the printer's memory. When the app sees the printer again it picks
the job back up (progress, layer, time left). If the print ended while the
app wasn't watching, it asks whether it finished, so the spool and history
come out right.


ONE-TIME SETUP (about 10 minutes)
---------------------------------
0. Install
   - Unzip the whole "OpenBoxBridge" folder somewhere permanent (e.g.
     Documents). Don't run it from inside the zip.
   - The PC needs the UP printer's USB driver, which UP Studio installs.
     If UP Studio has ever talked to the printer on this PC, it's there.
   - Windows may warn that the app is from an unknown publisher (it isn't
     signed): choose "More info" > "Run anyway".
   - Every printer is different: do steps 2 and 3 on YOUR printer. Don't
     copy someone else's nozzle height or bed centre.

1. Connect
   - Close UP Studio (also check the system tray). Only one program can
     use the printer's USB at a time.
   - Plug the USB cable into this PC and switch the printer on.
   - Double-click "OpenBoxBridge.exe", then press "Check printer".
     You should see "Printer found: ..." in the log.
     If not, see "IF CHECK PRINTER FAILS" below.

2. Bed centre (do this before the nozzle height)
   - Setup page > "Find bed centre...". The printer homes, then moves the
     nozzle over the bed. Nudge it with Left/Right/Back/Fwd until it is over
     the middle of the bed, then press "Save as centre".

3. Nozzle height
   - Setup page > "Calibrate...". Nozzle cold with no plastic on the tip,
     a sheet of printer paper ready.
   - Step 1 homes the printer exactly as a print does and puts the nozzle
     over the bed centre.
   - Step 2: press "Fast to 200 mm", then raise the bed 5 mm at a time
     while the gap is big (up to 220 mm), 1 mm until it's about 1 mm, then
     0.1 / 0.05 mm, sliding the paper under the nozzle, until it just drags. The big number is the bed position read
     back from the printer.
   - Step 3: Save. The nozzle height is that position plus 0.1 mm for the
     paper. Closing the window lowers the bed 10 mm away from the nozzle.
   - Don't copy UP Studio's nozzle height: it is measured from a different
     starting point (on this Box+ it was about 25 mm short).

4. Slicer presets
   - Close the slicer, then on the Setup page under "Slicer presets" press
     OrcaSlicer, PrusaSlicer or Bambu Studio. Each copies the UP Box+ USB
     printer, the three print profiles and PLA / ABS into that slicer's own
     preset folder, pointing its "post-processing" step at this app.
   - In the slicer choose printer "UP Box+ USB", print profile
     "UP Box+ 0.20mm" and filament "UP PLA (Box+)" (the default) or
     "UP ABS (Box+)".
   - Use these buttons rather than importing the files by hand: they also
     point the slicer at wherever you put this folder.
   - PrusaSlicer: switch to Expert mode to see every setting. Infill is
     rectilinear there (PrusaSlicer has no crosshatch); seams go to the rear.
   - Bambu Studio is made for Bambu printers; the UP Box+ shows under the
     user presets. Use Export G-code (not Print / Send). If the app doesn't
     open after an export, check Process > Others > Post-processing scripts
     names "OpenBoxBridge.exe".

   Only files sliced with a printer profile whose name contains "UP Box"
   are opened. If you slice for another printer with a profile copied from
   the UP Box+ ones, the export is left alone (noted in jobs\handoff.log).

PRINTING
--------
1. In the slicer, slice with the UP Box+ USB printer and press Export G-code
   (Ctrl+G). Save it anywhere.
2. The app pops up with that file loaded. If it's already open, it just
   loads the new file.
3. Press Print. It converts the file, shows the converter's time estimate,
   asks you to confirm, then sends it over USB.
4. The bed heats first (the converted job waits a fixed time for it, about
   3 minutes for 90 C), then the print starts. The Dashboard shows the
   progress, layer, time left, finish time and both temperatures.

You can also drop any .gcode file onto the window, or use "Open G-code...".
Files from other slicers work too, as long as they're sliced with 0,0 in the
middle of the bed. Arc moves (G2/G3) are fine.

STOP         (top bar) stops the job straight away and turns both heaters
             off. "Cancel print" on the job card does the same. It uses the
             printer's own stop request, so it also ends jobs running from
             the printer's memory. If the printer ever refuses, the app pauses
             the job and tells you to press the button on the printer.
             To check it: load "tests\STOP test (no heat).gcode", press
             Print, wait until the head is drawing squares (no heating, the
             bed stays near the bottom), then press STOP.
Power off    turns the printer's heaters and motors off.
Motion       (Setup) Gentle / Normal / Fast: acceleration used when
             converting (150 / 300 / 750 mm/s2). Normal is the UP3D author's
             recommendation; UP Studio itself uses 250-750. Printing moves
             are capped at 25 mm/s and travels at 100 mm/s in all three.


PRINT SETTINGS (all three slicers)
-----------------------------
Tuned for reliable prints rather than speed, following Tiertime's own
guidance for the UP Box+. PLA is the default. Three print profiles:
"UP Box+ 0.20mm" (everyday), "0.15mm Fine" and "0.25mm Draft".

  Line widths  outer 0.42, inner/infill/solid 0.45, first layer 0.50 mm
  Walls        3, Arachne generator (fills thin areas instead of leaving
               gaps), inner walls first, one wall on top surfaces
  Seams        aligned to the back, staggered, wiped inward at the end of
               each loop. No scarf seams (they're slow on this printer).
  Shells       5 top / 4 bottom layers at 0.20 (at least 1.0 / 0.8 mm),
               monotonic surfaces, full vertical shell thickness
  Infill       15% crosshatch (never crosses itself within a layer)
  Bridges      thick round bridges at 20 mm/s (sag less with fixed airflow)
  Speeds       outer wall 20, inner 25, infill 25, solid 25, top 20,
               first layer 15 (infill 20), travel 100 mm/s - the same
               speeds UP Studio really uses on this printer; slows down on
               overhangs, small perimeters and short layers.
               The converter then works like UP Studio: printing moves are
               capped at 25 mm/s (UP Studio never prints faster on this
               machine) and travel moves are done by the printer's own jog
               routine at up to 100 mm/s. The presets use those speeds, so
               the slicer's time estimate matches the converter's.
  Acceleration The converter plans motion itself (Setup > Motion, Normal
               = 300 mm/s2). Orca's accelerations are set to match so its
               time estimates are realistic.
  Adhesion     2-layer raft (Tiertime's default; grips the perforated board
               and evens out small levelling errors), 1 skirt loop. The
               raft's top is near-solid (3 interface layers, lines 0.2 mm
               apart) with a 0.1 mm gap to the part, for a smooth underside;
               it takes a little more effort to peel off.
               For a flex plate or glass you can set Raft layers to 0.
  Supports     off until you tick them: snug normal supports, 40 degrees,
               0.2 mm gap, 3 dense interface layers (shared with the raft
               settings) for clean undersides
  Retraction   0.8 mm at 25 mm/s, 0.2 mm lift
  PLA          nozzle 210 C first layer then 205 C, bed 60 C
  ABS          nozzle 260 C first layer then 255 C (the Box+ maximum is 260 C),
               bed 100 C (its maximum), plus an 8-minute platform soak
  Start        bed first, then a short soak, then the nozzle, so it doesn't
               ooze while waiting

Part cooling on the Box+ is the airflow knob on the print head (the slicer's
fan settings do nothing). PLA: airflow open. ABS: airflow closed, door shut.
PLA in a warm, closed box can jam (heat creep) - on long PLA prints leave the
door or top open if you see under-extrusion.


FIRST-LAYER TUNING (nozzle height)
----------------------------------
Start with the number Setup > Calibrate measured. A bigger nozzle height
brings the bed closer to the nozzle.
  - First layer not sticking, or round lines with gaps between them:
    raise it by 0.05 - 0.1.
  - Nozzle scraping, or a see-through first layer:
    lower it by 0.05 - 0.1.

The UP Box+'s automatic bed-level compensation is a UP Studio feature and
isn't used here, so the bed needs to be level mechanically (UP Studio's
levelling procedure plus the knobs under the bed).


SEVERAL PRINTERS
----------------
Each printer keeps its own nozzle height, bed centre and 180-degree setting,
stored under its serial number. The app reads the serial whenever it talks
to the printer and switches automatically: plug in another UP and its own
calibration is loaded. A printer it hasn't seen before starts uncalibrated
(the app tells you, and won't print until the nozzle height is set). The
first printer the app ever sees keeps the calibration that was already
there. The spool and print history are shared.


SETTING THE BED CENTRE BY HAND
------------------------------
Slice a 20 mm cube placed in the middle of Orca's plate and print just the
first layer or two. Measure how far its centre is from the middle of the bed:
  - lands N mm too far right   -> lower "Bed centre X" by N
  - lands N mm too far left    -> raise "Bed centre X" by N
  - lands N mm too far back    -> lower "Bed centre Y" by N
  - lands N mm too far forward -> raise "Bed centre Y" by N
If text on a test print comes out mirrored, stop: the axis direction for the
Box+ needs changing in the converter (x_dir / y_dir in settings_box, file
source\UP3DCOMMON\up3dconf.c) and the tools rebuilt.


IF CHECK PRINTER FAILS
----------------------
"No UP printer found"
    Check the cable, switch the printer on and wait 10 seconds. Try another
    USB port (not a hub).

"Access denied (insufficient permissions)"
    Usually a printer tool from this app got stuck holding the USB
    connection (e.g. the printer was switched off while a job was being
    sent). The app now ends stuck tools by itself - wait a few seconds or
    reopen the app. If it persists, check UP Studio is closed.

"Another program is using the printer" / "Claim Interface"
    Close UP Studio completely, including the tray icon, and try again.

"no compatible USB driver"
    Windows has the printer on a driver these tools can't talk to. Fix it
    with Zadig (free, from https://zadig.akeo.ie):
      1. Run Zadig, then Options > List All Devices.
      2. Pick the UP printer (USB ID 4745 xxxx).
      3. Choose "WinUSB" as the driver, then Replace Driver.
    Note: UP Studio may stop seeing the printer after this. To go back,
    open Device Manager, find the printer, choose Uninstall device (tick
    "delete the driver"), unplug and replug it, and reinstall UP Studio's
    driver if needed.

The log window shows exactly what each tool said. Copy it if you need help.


GOOD TO KNOW
------------
- This Box+ runs X and Y the opposite way to Orca (X+ goes left, Y+ goes
  to the front), so the converter turns every print 180 degrees about the
  bed centre. Prints land on the bed exactly as the slicer shows them, and the
  jog buttons follow Orca's directions (X+ right, Y+ back).
- Commands the UP doesn't use (progress, acceleration, fan, etc.) are
  skipped. The app lists which ones under each conversion. The printer
  controls its own fans.
- Arc moves are split into short straight lines. Arc fitting is already off
  in the Orca preset.
- Pause/resume and filament change in the middle of a print aren't
  supported.
- Each print ends with three beeps, then the printer powers down.
- Watch the first few prints. STOP, the button on the printer and its
  power switch are your safety net.


CREDITS AND LICENCE
-------------------
OpenBoxBridge is free software under the GNU GPL version 3 or later. Its full source
is in the source\ folder and on GitHub (search "OpenBoxBridge").

The printer tools are UP3D by Maik Stohn and Kai Scheffer
(github.com/MaikStohn/UP3D, github.com/UP3D-gcode/UP3D), GPL.
Changes made for this setup (full source in source\):
  - finds UP printers with any Tiertime USB ID, and gives clearer USB errors
  - skips unsupported G-code commands instead of stopping
  - converts G2/G3 arcs to line segments
  - fixes relative extrusion (M83) when positioning is absolute
  - adds a bed-centre X/Y offset and correct error exit codes
  - counts layers by extrusion height (better progress on the printer)
  - new up3dcenter tool (find bed centre)
  - STOP uses the printer's stop request (works for SD-card jobs)
  - travel moves that carry a Z height (Orca's z-hop travels) are planned
    like any other move instead of being sent as fast direct jog moves;
    direct Z moves are limited to 15 mm/s
  - motor segments are written the way UP Studio writes them (short
    segments and ticks, never a speed of 0 on an idle axis), against the
    slow per-layer drift reported in UP3D issue #31
  - printing moves capped at 25 mm/s and travels sent as jog moves, as
    UP Studio does (found by recording UP Studio's USB traffic)
  - fixes diagonal cruise speed (was timed by the longest axis, so
    diagonals ran up to 41% too fast) and the converter's time estimate
  - position read-back for nozzle height calibration
  - sending gives up if the printer stops answering, instead of waiting
    forever while holding the USB connection
Built for 64-bit Windows 10/11 with libusb 1.0.27 (LGPL).
