#!/usr/bin/env python3
"""Make the "UP300 USB" printer preset for OrcaSlicer, Bambu Studio and PrusaSlicer from the
UP Box+ ones (run make_presets.py first), and let the print and filament presets be used with
both printers.

Why a separate printer: the UP300's travel around the bed centre is smaller than the UP Box+
print area. With the bed centre at X 125, Y 108 the head reaches about X -122..+137 and
Y -102..+105 mm, and a move outside that makes the printer stop with "JogCmd Error". So the
UP300 print area keeps 2 mm inside that on every side: X -120..+120, Y -100..+100.

The start G-code doesn't wait for the bed (M190): for the UP300 the app heats the bed and waits
for the measured temperature before it sends the job (the converter can only wait a fixed time,
which was too short for the UP300 bed). The nozzle is heated after that, so it doesn't ooze."""
import json, os, re

HERE = os.path.dirname(os.path.abspath(__file__))
P = os.path.join(HERE, '..', 'presets')
BOX, UP3 = 'UP Box+ USB', 'UP300 USB'
AREA = ['-120x-100', '120x-100', '120x100', '-120x100']
HEIGHT = '205'

START = """; UP300 (USB) start - reliability first. The printer homes itself before this runs.
; 0,0 is the middle of the bed. The app heats the bed and waits for it before it sends the job.
; Part cooling is the manual airflow knob on the head:
;   PLA - airflow open; ABS - airflow closed and the door shut.
G90
M83
M107
M140 S[bed_temperature_initial_layer_single] ; keep the bed on (already hot)
{if bed_temperature_initial_layer_single >= 80}G4 S300 ; ABS: soak the platform 5 more minutes{else}G4 S20 ; let the platform settle{endif}
M104 S[nozzle_temperature_initial_layer] ; heat the nozzle last so it doesn't ooze
M109 S[nozzle_temperature_initial_layer]
G92 E0
G1 Z5 F600
G1 X-118 Y-96 F6000
G1 Z0.3 F600
G1 X-118 Y40 E7 F900 ; prime line, left edge
G1 X-117.6 Y40 F900
G1 X-117.6 Y-60 E5 F900
G1 E-0.8 F1500
G1 Z1 F600
G92 E0"""

END = """; UP300 (USB) end
G1 E-2 F1500
M104 S0
M140 S0
M107
G1 Z{min(max_layer_z + 20, printable_height)} F600 ; lower the bed away from the part
M84
; the converter adds the finish beeps and powers the printer down"""


def jload(path):
    return json.load(open(path, encoding='utf-8'))


def jsave(path, d):
    json.dump(d, open(path, 'w', newline='\n', encoding='utf-8'), indent=4)


def json_slicer(folder):
    d = os.path.join(P, folder)
    m = jload(os.path.join(d, BOX + '.json'))
    m['name'] = UP3
    if 'printer_settings_id' in m: m['printer_settings_id'] = UP3
    m['printable_area'] = AREA
    m['printable_height'] = HEIGHT
    m['machine_start_gcode'] = START
    m['machine_end_gcode'] = END
    jsave(os.path.join(d, UP3 + '.json'), m)
    for f in os.listdir(d):
        if not f.endswith('.json') or f in (BOX + '.json', UP3 + '.json'): continue
        p = jload(os.path.join(d, f))
        cp = p.get('compatible_printers')
        if isinstance(cp, list) and BOX in cp and UP3 not in cp:
            p['compatible_printers'] = cp + [UP3]
            jsave(os.path.join(d, f), p)


def ini_esc(s):
    return s.replace('\\', '\\\\').replace('\n', '\\n')


def prusa():
    d = os.path.join(P, 'prusaslicer')
    src = open(os.path.join(d, 'printer', BOX + '.ini'), encoding='utf-8').read()
    start = START.replace('[bed_temperature_initial_layer_single]', '[first_layer_bed_temperature[0]]') \
                 .replace('bed_temperature_initial_layer_single', 'first_layer_bed_temperature[0]') \
                 .replace('[nozzle_temperature_initial_layer]', '[first_layer_temperature[0]]')
    start = '; total layers = [total_layer_count]\n' + start
    end = END.replace('printable_height', 'max_print_height')
    out = []
    for line in src.splitlines():
        k = line.split(' = ', 1)[0]
        if k == 'bed_shape': line = 'bed_shape = ' + ','.join(AREA)
        elif k == 'max_print_height': line = 'max_print_height = ' + HEIGHT
        elif k == 'start_gcode': line = 'start_gcode = ' + ini_esc(start)
        elif k == 'end_gcode': line = 'end_gcode = ' + ini_esc(end)
        elif k == 'printer_notes': line = 'printer_notes = UP300 over USB via OpenBoxBridge. 0,0 is the middle of the bed.'
        elif line.startswith('#'): line = '# UP300 USB preset for PrusaSlicer (made from the UP Box+ USB preset)'
        out.append(line)
    open(os.path.join(d, 'printer', UP3 + '.ini'), 'w', newline='\n', encoding='utf-8').write('\n'.join(out) + '\n')
    for kind in ('print', 'filament'):
        for f in os.listdir(os.path.join(d, kind)):
            p = os.path.join(d, kind, f)
            s = open(p, encoding='utf-8').read()
            s2 = s.replace('compatible_printers = "UP Box+ USB"\n', 'compatible_printers = "UP Box+ USB";"UP300 USB"\n')
            if s2 != s: open(p, 'w', newline='\n', encoding='utf-8').write(s2)


json_slicer('orca')
json_slicer('bambustudio')
prusa()
print('ok')
