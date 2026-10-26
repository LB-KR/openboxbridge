#!/usr/bin/env python3
"""Replay a .umc file and track motor positions under two models of the printer's
step arithmetic, to look for position drift. Usage: umcsim.py file.umc file.gcode"""
import struct, sys, math, re
import numpy as np

def i32(v):  # C int32 wrap
    v &= 0xFFFFFFFF
    return v - (1 << 32) if v & 0x80000000 else v

def f32(v):
    return struct.unpack('f', struct.pack('f', float(v)))[0]

def steps_conv(p1, v, a):   # what the converter assumes (float + int32 maths, see hoststepper.c)
    s = i32(i32(v * p1) + i32(i32(i32(a * (p1 - 1)) * p1) // 2 if i32(i32(a * (p1 - 1)) * p1) >= 0 else -((-i32(i32(a * (p1 - 1)) * p1)) // 2)))
    return math.floor(f32(s) / 512.0)

def tdiv(a, b):  # C truncating division
    q = abs(a) // abs(b)
    return q if (a >= 0) == (b > 0) else -q

def steps_exact(p1, v, a):  # exact integer maths, floor
    s = v * p1 + tdiv(a * (p1 - 1) * p1, 2)
    return s // 512

def run(umc):
    data = open(umc, 'rb').read()
    n = len(data) // 20
    posA = [0, 0, 0]; posB = [0, 0, 0]
    samples = []
    for k in range(n):
        cmd, = struct.unpack_from('<I', data, k * 20)
        if cmd == 4:
            p1, p2, p3, p4, p5, p6, p7, p8 = struct.unpack_from('<HHhhhhhh', data, k * 20 + 4)
            for ax, (v, a) in enumerate(((p3, p6), (p4, p7), (p5, p8))):
                posA[ax] += steps_conv(p1, v, a)
                posB[ax] += steps_exact(p1, v, a)
        elif cmd == 3:
            # MoveF pair: first block X/Y, second Z/A. record a sample at every Z move
            f = struct.unpack_from('<ffff', data, k * 20 + 4)
            nxt = struct.unpack_from('<I', data, (k + 1) * 20)[0] if k + 1 < n else 0
            samples.append((k, tuple(posA[:2]), tuple(posB[:2]), f))
        elif cmd == 8:
            pass
    return samples

if __name__ == '__main__':
    s = run(sys.argv[1])
    print(len(s), 'MoveF blocks')
    steps = 644.0
    # first sample after homing as reference
    base = s[2]
    for i in list(range(2, len(s), max(1, len(s) // 25))) + [len(s) - 1]:
        k, a, b, f = s[i]
        d = [(b[j] - a[j]) / steps for j in range(2)]
        print('%6d  conv-model X %9.3f Y %9.3f | exact-model minus conv-model: %+.3f %+.3f mm' % (i, a[0] / steps, a[1] / steps, d[0], d[1]))
