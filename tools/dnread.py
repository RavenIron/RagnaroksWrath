#!/usr/bin/env python3
"""Minimal ECMA-335 metadata reader: lists types and methods from a .NET assembly."""
import struct, sys

class Reader:
    def __init__(self, path):
        self.d = open(path, 'rb').read()
        self._parse_pe()
        self._parse_metadata()

    def u16(self, o): return struct.unpack_from('<H', self.d, o)[0]
    def u32(self, o): return struct.unpack_from('<I', self.d, o)[0]

    def _parse_pe(self):
        pe = self.u32(0x3C)
        assert self.d[pe:pe+4] == b'PE\0\0'
        coff = pe + 4
        nsec = self.u16(coff + 2)
        optsz = self.u16(coff + 16)
        opt = coff + 20
        magic = self.u16(opt)
        dd = opt + (96 if magic == 0x10b else 112)
        self.sections = []
        so = opt + optsz
        for i in range(nsec):
            b = so + i * 40
            name = self.d[b:b+8].rstrip(b'\0').decode()
            vsize, vaddr, rsize, raddr = struct.unpack_from('<IIII', self.d, b + 8)
            self.sections.append((vaddr, vsize, raddr, rsize))
        cli_rva = self.u32(dd + 14 * 8)
        self.cli = self.rva2off(cli_rva)

    def rva2off(self, rva):
        for vaddr, vsize, raddr, rsize in self.sections:
            if vaddr <= rva < vaddr + max(vsize, rsize):
                return raddr + (rva - vaddr)
        raise ValueError(f'bad rva {rva:x}')

    def _parse_metadata(self):
        md_rva = self.u32(self.cli + 8)
        md = self.rva2off(md_rva)
        assert self.d[md:md+4] == b'BSJB'
        vlen = self.u32(md + 12)
        p = md + 16 + vlen
        p += 2  # flags
        nstreams = self.u16(p); p += 2
        self.streams = {}
        for _ in range(nstreams):
            off, size = struct.unpack_from('<II', self.d, p); p += 8
            e = self.d.index(b'\0', p)
            name = self.d[p:e].decode()
            p = e + 1
            p = (p + 3) & ~3
            self.streams[name] = (md + off, size)
        self.strings = self.streams['#Strings'][0]
        self.tilde = self.streams['#~'][0]
        self._parse_tables()

    def s(self, idx):
        e = self.d.index(b'\0', self.strings + idx)
        return self.d[self.strings + idx:e].decode('utf-8', 'replace')

    def _parse_tables(self):
        t = self.tilde
        heapsizes = self.d[t + 6]
        self.sidx = 4 if heapsizes & 1 else 2
        self.gidx = 4 if heapsizes & 2 else 2
        self.bidx = 4 if heapsizes & 4 else 2
        valid = struct.unpack_from('<Q', self.d, t + 8)[0]
        p = t + 24
        self.rows = {}
        for i in range(64):
            if valid >> i & 1:
                self.rows[i] = self.u32(p); p += 4
        self.tables_start = p

    def n(self, table): return self.rows.get(table, 0)
    def tidx(self, table): return 4 if self.n(table) >= 65536 else 2
    def coded(self, tables, bits):
        mx = max(self.n(t) for t in tables)
        return 4 if mx >= (1 << (16 - bits)) else 2

    def row_size(self, table):
        S, B, G = self.sidx, self.bidx, self.gidx
        TypeDefOrRef = self.coded([0x02, 0x01, 0x1B], 2)
        ResolutionScope = self.coded([0x00, 0x1A, 0x23, 0x01], 2)
        sizes = {
            0x00: 2 + S + G * 3,
            0x01: ResolutionScope + S * 2,
            0x02: 4 + S * 2 + TypeDefOrRef + self.tidx(0x04) + self.tidx(0x06),
            0x03: self.tidx(0x04),
            0x04: 2 + S + B,
            0x05: self.tidx(0x06),
            0x06: 4 + 2 + 2 + S + B + self.tidx(0x08),
        }
        return sizes[table]

    def table_off(self, table):
        o = self.tables_start
        for t in sorted(self.rows):
            if t == table: return o
            o += self.rows[t] * self.row_size(t)
        raise ValueError('table not present')

    def typedefs(self):
        base = self.table_off(0x02)
        rs = self.row_size(0x02)
        S = self.sidx
        TypeDefOrRef = self.coded([0x02, 0x01, 0x1B], 2)
        mi = self.tidx(0x06)
        out = []
        rd = (lambda o, w: self.u16(o) if w == 2 else self.u32(o))
        for i in range(self.n(0x02)):
            o = base + i * rs
            flags = self.u32(o); o += 4
            name = self.s(rd(o, S)); o += S
            ns = self.s(rd(o, S)); o += S
            o += TypeDefOrRef
            o += self.tidx(0x04)
            mlist = rd(o, mi)
            out.append((ns, name, mlist, flags))
        return out

    def methods(self):
        base = self.table_off(0x06)
        rs = self.row_size(0x06)
        S, B = self.sidx, self.bidx
        rd = (lambda o, w: self.u16(o) if w == 2 else self.u32(o))
        out = []
        for i in range(self.n(0x06)):
            o = base + i * rs
            o += 4 + 2
            flags = self.u16(o); o += 2
            name = self.s(rd(o, S)); o += S
            sig = rd(o, B)
            out.append((name, flags, sig))
        return out

if __name__ == '__main__':
    r = Reader(sys.argv[1])
    tds = r.typedefs()
    ms = r.methods()
    want = set(sys.argv[2:])
    for i, (ns, name, mlist, tflags) in enumerate(tds):
        if want and name not in want: continue
        end = tds[i+1][2] - 1 if i + 1 < len(tds) else len(ms)
        print(f'=== {ns + "." if ns else ""}{name} ===')
        for mi in range(mlist - 1, min(end, len(ms))):
            mname, mflags, _ = ms[mi]
            static = 'static ' if mflags & 0x0010 else ''
            vis = {0:'compilercontrolled',1:'private',2:'famandassem',3:'assembly',
                   4:'family',5:'famorassem',6:'public'}.get(mflags & 7, '?')
            print(f'  {vis} {static}{mname}')
