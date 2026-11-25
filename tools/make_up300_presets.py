#!/usr/bin/env python3
"""Make the UP300 presets for OrcaSlicer, Bambu Studio and PrusaSlicer from the UP Box+ ones
(run make_presets.py first):
  printer   "UP300 USB"
  process   "UP300 0.15mm Fine", "UP300 0.20mm", "UP300 0.25mm Draft"
  filament  "UP PLA (UP300)", "UP ABS (UP300)"
The UP300 process and filament presets only show for the UP300 USB printer, and the UP Box+ ones
only for UP Box+ USB, so each printer gets its own set by default.

Settings that move Z while printing are kept off in the UP300 process presets (test 6): the UP
printers can only move Z with a separate "jog" command, and a job with thousands of them (scarf
joint seam, spiral vase) stops with "JogCmd Error". So: scarf seam none, spiral vase off; the
printer preset uses a Normal Lift z-hop.

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
    m['default_print_profile'] = 'UP300 0.20mm'
    m['default_filament_profile'] = ['UP PLA (UP300)']
    jsave(os.path.join(d, UP3 + '.json'), m)
    for f in sorted(os.listdir(d)):
        if not f.endswith('.json') or f in (BOX + '.json', UP3 + '.json') or 'UP300' in f: continue
        p = jload(os.path.join(d, f))
        cp = p.get('compatible_printers')
        if not (isinstance(cp, list) and BOX in cp): continue
        if cp != [BOX]:   # the UP Box+ presets are for the UP Box+ only again
            p['compatible_printers'] = [BOX]
            jsave(os.path.join(d, f), p)
        q = json.loads(json.dumps(p))
        q['compatible_printers'] = [UP3]
        for k, v in list(q.items()):
            if isinstance(v, str): q[k] = up300_name(v)
            elif isinstance(v, list) and v and all(isinstance(x, str) for x in v) and k != 'compatible_printers':
                q[k] = [up300_name(x) for x in v]
        if q.get('type') == 'process' or 'print_settings_id' in q:
            q['seam_slope_type'] = 'none'
            q['spiral_mode'] = '0'
        jsave(os.path.join(d, up300_name(f[:-5]) + '.json'), q)


def up300_name(v):
    return v.replace('UP Box+ USB', UP3).replace('(Box+)', '(UP300)').replace('UP Box+ ', 'UP300 ')


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
        for f in sorted(os.listdir(os.path.join(d, kind))):
            if 'UP300' in f: continue
            p = os.path.join(d, kind, f)
            s = open(p, encoding='utf-8').read()
            s2 = s.replace('compatible_printers = "UP Box+ USB";"UP300 USB"\n', 'compatible_printers = "UP Box+ USB"\n')
            if s2 != s: open(p, 'w', newline='\n', encoding='utf-8').write(s2)
            u = s2.replace('compatible_printers = "UP Box+ USB"\n', 'compatible_printers = "UP300 USB"\n')
            u = re.sub(r'^# UP Box\+ USB (\w+) preset', r'# UP300 USB \1 preset', u, flags=re.M)
            u = u.replace('filament_notes = UP Box+ -', 'filament_notes = UP300 -')
            if kind == 'print':
                u = re.sub(r'^scarf_seam_placement = .*$', 'scarf_seam_placement = nowhere', u, flags=re.M)
                u = re.sub(r'^spiral_vase = .*$', 'spiral_vase = 0', u, flags=re.M)
            open(os.path.join(d, kind, up300_name(f[:-4]) + '.ini'), 'w', newline='\n', encoding='utf-8').write(u)


json_slicer('orca')
json_slicer('bambustudio')
prusa()
print('ok')
