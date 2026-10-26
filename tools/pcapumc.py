#!/usr/bin/env python3
"""Extract the UMC program blocks UP Studio sent to the printer from a USBPcap capture."""
import struct, sys, collections
def read_pcap(p):
    d = open(p, 'rb').read()
    magic, = struct.unpack_from('<I', d, 0)
    assert magic in (0xa1b2c3d4,), hex(magic)
    linktype, = struct.unpack_from('<I', d, 20)
    off = 24; out = []
    while off + 16 <= len(d):
        ts, tus, incl, orig = struct.unpack_from('<IIII', d, off); off += 16
        pkt = d[off:off+incl]; off += incl
        hl, irp, st, fn, info, bus, dev, ep, tr, dl = struct.unpack_from('<HQIHBHHBBI', pkt, 0)
        out.append(dict(t=ts+tus/1e6, info=info, bus=bus, dev=dev, ep=ep, tr=tr, data=pkt[hl:hl+dl]))
    return linktype, out
if __name__ == '__main__':
    lt, pk = read_pcap(sys.argv[1])
    print('linktype', lt, 'packets', len(pk))
    c = collections.Counter((p['dev'], hex(p['ep']), p['tr'], p['info'] & 1, len(p['data']) > 0) for p in pk)
    for k, v in c.most_common(20): print(k, v)
