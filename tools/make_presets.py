#!/usr/bin/env python3
"""Make PrusaSlicer (.ini) and Bambu Studio (.json) presets from the Orca presets,
so every slicer gets the same reliability-first UP Box+ settings."""
import json, os, glob, re, shutil
# The Orca presets are the master copy; the PrusaSlicer and Bambu Studio presets are made
# from them. Setting names and allowed values are checked against each slicer's own source:
#   BAMBU_PRINTCONFIG = path to BambuStudio/src/libslic3r/PrintConfig.cpp
#   PRUSA_PRINTCONFIG = path to PrusaSlicer (2.9.x) src/libslic3r/PrintConfig.cpp
HERE = os.path.dirname(os.path.abspath(__file__))
ORCA = os.path.join(HERE, '..', 'presets', 'orca')
OUT_B = os.path.join(HERE, '..', 'presets', 'bambustudio')
OUT_P = os.path.join(HERE, '..', 'presets', 'prusaslicer')
EXE = r'C:\OpenBoxBridge\OpenBoxBridge.exe'   # placeholder: the app rewrites it to its real location on install
import sys; sys.path.insert(0, os.path.dirname(__file__))
from cfgdefs import load
BAMBU = load(os.environ['BAMBU_PRINTCONFIG'])
PRUSA = load(os.environ['PRUSA_PRINTCONFIG'])

orca = {json.load(open(f))['name']: json.load(open(f)) for f in glob.glob(ORCA + '/*.json')}

# ---------------------------------------------------------------- Bambu Studio
DROP_B = {'printer_extruder_variant', 'print_extruder_variant', 'filament_extruder_variant', 'extruder_variant_list',
          'printer_extruder_id', 'print_extruder_id', 'default_nozzle_volume_type', 'extruder_type'}
META = {'type', 'name', 'from', 'inherits', 'version', 'print_settings_id', 'printer_settings_id',
        'filament_settings_id', 'compatible_printers', 'compatible_printers_condition'}
MAP_VAL_B = {('seam_position', 'aligned_back'): 'back', ('ensure_vertical_shell_thickness', 'ensure_all'): 'enabled'}
KEEP_LISTS = {'printable_area', 'compatible_printers', 'post_process', 'filament_settings_id', 'default_filament_profile',
              'extruder_offset', 'extruder_colour', 'nozzle_diameter'}
def bambu(d):
    o = {}
    for k, v in d.items():
        if k in DROP_B: continue
        if k not in META and k not in BAMBU: continue          # Orca-only setting
        if isinstance(v, list) and k not in KEEP_LISTS:
            if k.startswith('machine_max_'): v = v[:2]
            else: v = v[:1]
        if isinstance(v, str) and (k, v) in MAP_VAL_B: v = MAP_VAL_B[(k, v)]
        o[k] = v
    if d.get('only_one_wall_top') == '1': o['top_one_wall_type'] = 'all top'
    if d['type'] == 'machine': o['thumbnail_size'] = ['300x300']
    o['version'] = '1.9.0.0'
    # check enums
    for k, v in o.items():
        if k in BAMBU and BAMBU[k][0].startswith('coEnum') and BAMBU[k][1] and isinstance(v, str) and v not in BAMBU[k][1]:
            raise SystemExit('Bambu: bad value %s=%s (allowed %s)' % (k, v, BAMBU[k][1]))
    return o

shutil.rmtree(OUT_B, ignore_errors=True); os.makedirs(OUT_B)
for n, d in orca.items():
    json.dump(bambu(d), open(os.path.join(OUT_B, n + '.json'), 'w', newline='\r\n'), indent=4)

# ---------------------------------------------------------------- PrusaSlicer
def esc(s):   # PrusaSlicer ini string escaping for multi-line g-code
    return s.replace('\\', '\\\\').replace('\n', '\\n')
def strs(*items):   # coStrings serialisation
    out = []
    for s in items:
        if re.search(r'[ ;\t\\"\r\n]', s) or s == '':
            out.append('"' + s.replace('\\', '\\\\').replace('"', '\\"').replace('\r', '\\r').replace('\n', '\\n') + '"')
        else: out.append(s)
    return ';'.join(out)

m = orca['UP Box+ USB']
start = m['machine_start_gcode']
start = start.replace('[bed_temperature_initial_layer_single]', '[first_layer_bed_temperature[0]]')
start = start.replace('bed_temperature_initial_layer_single', 'first_layer_bed_temperature[0]')
start = start.replace('[nozzle_temperature_initial_layer]', '[first_layer_temperature[0]]')
start = '; total layers = [total_layer_count]\n' + start
end = m['machine_end_gcode'].replace('printable_height', 'max_print_height')
printer = {
    'printer_technology': 'FFF',
    'printer_notes': 'UP Box+ over USB via UP Box Print. 0,0 is the middle of the bed.',
    'bed_shape': ','.join(m['printable_area']),
    'max_print_height': m['printable_height'],
    'gcode_flavor': 'marlin2',
    'machine_limits_usage': 'time_estimate_only',
    'machine_max_acceleration_x': '300,300', 'machine_max_acceleration_y': '300,300',
    'machine_max_acceleration_z': '500,500', 'machine_max_acceleration_e': '5000,5000',
    'machine_max_acceleration_extruding': '300,300', 'machine_max_acceleration_retracting': '600,600',
    'machine_max_acceleration_travel': '300,300',
    'machine_max_feedrate_x': '100,100', 'machine_max_feedrate_y': '100,100',   # UP Box+ rated 100 mm/s
    'machine_max_feedrate_z': '30,30', 'machine_max_feedrate_e': '60,60',
    'machine_max_jerk_x': '8,8', 'machine_max_jerk_y': '8,8', 'machine_max_jerk_z': '0.4,0.4', 'machine_max_jerk_e': '5,5',
    'machine_min_extruding_rate': '0,0', 'machine_min_travel_rate': '0,0',
    'nozzle_diameter': '0.4', 'min_layer_height': '0.1', 'max_layer_height': '0.3',
    'extruder_colour': '"#FCE94F"', 'extruder_offset': '0x0',
    'retract_length': '0.8', 'retract_speed': '25', 'deretract_speed': '20', 'retract_lift': '0.2',
    'retract_before_travel': '2', 'retract_layer_change': '1', 'retract_restart_extra': '0',
    'retract_before_wipe': '0%', 'wipe': '0', 'retract_length_toolchange': '1',
    'travel_ramping_lift': '0', 'travel_slope': '0', 'travel_lift_before_obstacle': '0',   # plain vertical lift only
    'use_relative_e_distances': '1', 'use_firmware_retraction': '0', 'use_volumetric_e': '0',
    'single_extruder_multi_material': '0',
    'thumbnails': '300x300/PNG', 'binary_gcode': '0',
    'start_gcode': esc(start),
    'end_gcode': esc(end),
    'before_layer_gcode': esc(m['before_layer_change_gcode']),
    'layer_gcode': esc(m['layer_change_gcode']),
    'toolchange_gcode': '', 'pause_print_gcode': '', 'template_custom_gcode': '', 'color_change_gcode': '',
    'remaining_times': '0', 'silent_mode': '0',
    'inherits': '',
    'printer_model': '', 'printer_vendor': '', 'printer_variant': '',
}

def prusa_print(p):
    lh = float(p['layer_height'])
    return {
        'compatible_printers': strs('UP Box+ USB'), 'compatible_printers_condition': '', 'inherits': '',
        'post_process': strs('"' + EXE + '"'),
        'output_filename_format': '{input_filename_base}.gcode',
        'layer_height': p['layer_height'], 'first_layer_height': p['initial_layer_print_height'],
        'perimeters': p['wall_loops'], 'top_solid_layers': p['top_shell_layers'], 'bottom_solid_layers': p['bottom_shell_layers'],
        'top_solid_min_thickness': p['top_shell_thickness'], 'bottom_solid_min_thickness': p['bottom_shell_thickness'],
        'perimeter_generator': 'arachne', 'external_perimeters_first': '0',
        'wall_transition_angle': p['wall_transition_angle'], 'min_bead_width': p['min_bead_width'], 'min_feature_size': p['min_feature_size'],
        'top_one_perimeter_type': 'top', 'only_one_perimeter_first_layer': '0',
        'seam_position': 'rear', 'staggered_inner_seams': '1',
        'ensure_vertical_shell_thickness': 'enabled', 'extra_perimeters': '0', 'extra_perimeters_on_overhangs': '0',
        'thin_walls': '0', 'gap_fill_enabled': '1', 'gap_fill_speed': p['gap_infill_speed'],
        'fill_density': p['sparse_infill_density'], 'fill_pattern': 'rectilinear', 'fill_angle': p['infill_direction'],
        'top_fill_pattern': 'monotoniclines', 'bottom_fill_pattern': 'monotonic',
        'infill_overlap': p['infill_wall_overlap'], 'infill_every_layers': '1', 'solid_infill_below_area': p['minimum_sparse_infill_area'],
        'extrusion_width': '0.45', 'external_perimeter_extrusion_width': p['outer_wall_line_width'],
        'perimeter_extrusion_width': p['inner_wall_line_width'], 'infill_extrusion_width': p['sparse_infill_line_width'],
        'solid_infill_extrusion_width': p['internal_solid_infill_line_width'], 'top_infill_extrusion_width': p['top_surface_line_width'],
        'first_layer_extrusion_width': p['initial_layer_line_width'], 'support_material_extrusion_width': p['support_line_width'],
        'external_perimeter_speed': p['outer_wall_speed'], 'perimeter_speed': p['inner_wall_speed'],
        'small_perimeter_speed': '50%', 'infill_speed': p['sparse_infill_speed'],
        'solid_infill_speed': p['internal_solid_infill_speed'], 'top_solid_infill_speed': p['top_surface_speed'],
        'bridge_speed': p['bridge_speed'][0], 'travel_speed': p['travel_speed'], 'travel_speed_z': '10',
        'first_layer_speed': p['initial_layer_speed'], 'first_layer_infill_speed': p['initial_layer_infill_speed'],
        'support_material_speed': p['support_speed'][0], 'support_material_interface_speed': p['support_interface_speed'][0],
        'enable_dynamic_overhang_speeds': '1', 'overhang_speed_0': '10', 'overhang_speed_1': '20',
        'overhang_speed_2': '30', 'overhang_speed_3': p['outer_wall_speed'],
        'default_acceleration': '300', 'perimeter_acceleration': '300', 'external_perimeter_acceleration': '300',
        'infill_acceleration': '300', 'solid_infill_acceleration': '300', 'top_solid_infill_acceleration': '300',
        'bridge_acceleration': '300', 'first_layer_acceleration': '300', 'travel_acceleration': '300',
        'thick_bridges': '1', 'bridge_flow_ratio': '1', 'bridge_angle': '0', 'dont_support_bridges': '1',
        'raft_layers': p['raft_layers'], 'raft_contact_distance': p['raft_contact_distance'],
        'raft_expansion': p['raft_expansion'], 'raft_first_layer_density': p['raft_first_layer_density'],
        'raft_first_layer_expansion': p['raft_first_layer_expansion'],
        'skirts': p['skirt_loops'], 'skirt_distance': p['skirt_distance'], 'skirt_height': p['skirt_height'],
        'min_skirt_length': p['min_skirt_length'], 'brim_width': '0', 'brim_type': 'no_brim', 'draft_shield': 'disabled',
        'support_material': '0', 'support_material_auto': '1', 'support_material_threshold': p['support_threshold_angle'],
        'support_material_style': 'snug', 'support_material_contact_distance': p['support_top_z_distance'],
        'support_material_bottom_contact_distance': '0', 'support_material_interface_layers': p['support_interface_top_layers'],
        'support_material_bottom_interface_layers': '-1', 'support_material_spacing': p['support_base_pattern_spacing'],
        'support_material_interface_spacing': p['support_interface_spacing'], 'support_material_xy_spacing': p['support_object_xy_distance'],
        'support_material_pattern': 'rectilinear', 'support_material_interface_pattern': 'rectilinear',
        'support_material_buildplate_only': '0',
        'avoid_crossing_perimeters': '1', 'avoid_crossing_perimeters_max_detour': '0',
        'only_retract_when_crossing_perimeters': '1',
        'gcode_resolution': p['resolution'], 'elefant_foot_compensation': '0', 'xy_size_compensation': '0',
        'gcode_label_objects': 'disabled', 'arc_fitting': 'disabled', 'gcode_comments': '0',
        'complete_objects': '0', 'spiral_vase': '0', 'wipe_tower': '0', 'interface_shells': '0',
        'ironing': '0', 'fuzzy_skin': 'none', 'standby_temperature_delta': '-5',
        'infill_first': '0',
        'scarf_seam_placement': 'nowhere',      # scarf seams change Z mid-move: slow and blobby on the UP
    }

def prusa_filament(f):
    g = lambda k: f[k][0]
    return {
        'compatible_printers': strs('UP Box+ USB'), 'compatible_printers_condition': '', 'inherits': '',
        'filament_type': g('filament_type'), 'filament_vendor': 'Generic',
        'filament_diameter': g('filament_diameter'), 'filament_density': g('filament_density'), 'filament_cost': g('filament_cost'),
        'extrusion_multiplier': g('filament_flow_ratio'), 'filament_max_volumetric_speed': '7',
        'temperature': g('nozzle_temperature'), 'first_layer_temperature': g('nozzle_temperature_initial_layer'),
        'bed_temperature': g('hot_plate_temp'), 'first_layer_bed_temperature': g('hot_plate_temp_initial_layer'),
        'cooling': '1', 'fan_always_on': '1', 'min_fan_speed': g('fan_min_speed'), 'max_fan_speed': g('fan_max_speed'),
        'bridge_fan_speed': g('overhang_fan_speed'), 'disable_fan_first_layers': g('close_fan_the_first_x_layers'),
        'fan_below_layer_time': g('fan_cooling_layer_time'), 'slowdown_below_layer_time': g('slow_down_layer_time'),
        'min_print_speed': g('slow_down_min_speed'), 'full_fan_speed_layer': '0',
        'filament_soluble': '0', 'filament_notes': 'UP Box+ - the slicer fan settings do nothing on this printer; use the airflow knob on the head.',
        'start_filament_gcode': esc('; Filament gcode\n'), 'end_filament_gcode': esc('; filament end gcode\n'),
    }

shutil.rmtree(OUT_P, ignore_errors=True)
def write_ini(kind, name, kv):
    for k, v in kv.items():
        if k in ('inherits',): continue
        if k not in PRUSA: raise SystemExit('Prusa: unknown key %s in %s' % (k, name))
        t, ev = PRUSA[k]
        if t.startswith('coEnum') and ev and v not in ev: raise SystemExit('Prusa: bad value %s=%s (allowed %s)' % (k, v, ev))
    os.makedirs(os.path.join(OUT_P, kind), exist_ok=True)
    with open(os.path.join(OUT_P, kind, name + '.ini'), 'w', newline='\r\n') as fh:
        fh.write('# UP Box+ USB Print preset for PrusaSlicer (made from the Orca presets)\n')
        for k in sorted(kv): fh.write('%s = %s\n' % (k, kv[k]))
write_ini('printer', 'UP Box+ USB', printer)
for n, d in orca.items():
    if d['type'] == 'process': write_ini('print', n, prusa_print(d))
    if d['type'] == 'filament': write_ini('filament', n, prusa_filament(d))
print('ok')

