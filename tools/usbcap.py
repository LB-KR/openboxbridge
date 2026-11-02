#!/usr/bin/env python3
"""Print the conversation between UP software and an UP printer from a USBPcap capture
(.pcap or .pcapng, as saved by USBPcapCMD or Wireshark).

  usbcap.py capture.pcapng            summary per device and endpoint
  usbcap.py capture.pcapng --dump     every command and the printer's answer
"""
import struct, sys, collections

def packets(path):
    d = open(path, 'rb').read()
    magic, = struct.unpack_from('<I', d, 0)
    if magic == 0xa1b2c3d4:                       # classic pcap
        off = 24
        while off + 16 <= len(d):
            ts, tus, incl, orig = struct.unpack_from('<IIII', d, off); off += 16
            yield ts + tus / 1e6, 0, d[off:off + incl]; off += incl
        return
    off = 0; le = '<'                             # pcapng
    while off + 12 <= len(d):
        bt, bl = struct.unpack_from(le + 'II', d, off)
        if bt == 0x0A0D0D0A:
            le = '<' if d[off + 8:off + 12] == b'\x4d\x3c\x2b\x1a' else '>'
            bt, bl = struct.unpack_from(le + 'II', d, off)
        elif bt == 6:
            iid, th, tl, cap, orig = struct.unpack_from(le + 'IIIII', d, off + 8)
            yield ((th << 32) | tl) / 1e6, iid, d[off + 28:off + 28 + cap]
        off += bl

def usb(pkt):
    hl, irp, st, fn, info, bus, dev, ep, tr, dl = struct.unpack_from('<HQIHBHHBBI', pkt, 0)
    return dict(info=info, bus=bus, dev=dev, ep=ep, tr=tr, data=pkt[hl:hl + dl])

def main():
    path = sys.argv[1]; dump = '--dump' in sys.argv
    pk = [(t, i, usb(p)) for t, i, p in packets(path)]
    up = set()
    for t, i, u in pk:   # find Tiertime devices from their device descriptors
        d = u['data']
        if u['ep'] == 0x80 and (u['info'] & 1) and len(d) >= 18 and d[1] == 1:
            vid, pid = struct.unpack_from('<HH', d, 8)
            if vid == 0x4745:
                up.add((i, u['dev'])); print('UP printer: interface %d device %d  USB %04x:%04x' % (i, u['dev'], vid, pid))
    c = collections.Counter()
    t0 = pk[0][0] if pk else 0
    for t, i, u in pk:
        if (i, u['dev']) not in up or u['tr'] != 3 or not u['data']: continue
        out = not (u['info'] & 1)
        c[(u['dev'], hex(u['ep']), 'OUT' if out else 'IN')] += 1
        if dump: print('%9.3f dev%-3d %s %4d %s' % (t - t0, u['dev'], '>' if out else '<', len(u['data']), u['data'][:48].hex()))
    for k, v in sorted(c.items()): print(k, v)

if __name__ == '__main__':
    main()
