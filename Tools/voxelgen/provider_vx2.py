"""Emit a tiny provider-backed .vx2: SE reads the DataProvider chunk and instantiates our mod provider,
then streams terrain/ore from it at every LOD (like a real planet). No voxels stored here."""
import gzip, io, struct, sys

def pack64(lod, x, y, z):
    return ((lod & 0xF) << 60) | ((x & 0xFFFFF) << 40) | ((y & 0xFFFFF) << 20) | (z & 0xFFFFF)

class W:
    def __init__(s): s.b = bytearray()
    def u8(s, v): s.b.append(v & 0xFF)
    def i32(s, v): s.b += struct.pack("<i", v)
    def u16(s, v): s.b += struct.pack("<H", v & 0xFFFF)
    def u64(s, v): s.b += struct.pack("<Q", v & 0xFFFFFFFFFFFFFFFF)
    def v7(s, v):
        v &= 0xFFFFFFFF
        while v >= 0x80: s.b.append((v & 0x7F) | 0x80); v >>= 7
        s.b.append(v)
    def vstr(s, t): e = t.encode("utf-8"); s.v7(len(e)); s.b += e
    def chunk(s, ct, ver, size): s.v7(ct); s.v7(ver); s.v7(size)

def build(size=262144, type_id=770077, material="Stone_01"):
    th = size.bit_length() - 1 - 4          # treeHeight (262144 -> 14)
    agl = min(size.bit_length() - 1, 10)    # access grid lod
    leaf_key = pack64(th, 0, 0, 0)
    w = W()
    w.vstr("Octree"); w.v7(2)               # fileVersion 2
    w.u16(agl)                              # WriteStorageAccess
    w.chunk(1, 1, 17); w.i32(4); w.i32(size); w.i32(size); w.i32(size); w.u8(0)   # StorageMetaData
    mt = W(); mt.i32(1); mt.v7(0); mt.vstr(material)
    w.chunk(2, 1, len(mt.b)); w.b += mt.b   # MaterialIndexTable
    w.chunk(9, 2, 4); w.i32(type_id)        # DataProvider -> our provider TypeId
    w.chunk(3, 2, 0)                        # MacroContentNodes (none)
    w.chunk(4, 2, 0)                        # MacroMaterialNodes (none)
    w.chunk(5, 3, 8); w.u64(leaf_key)       # ContentLeafProvider (root)
    w.chunk(7, 3, 8); w.u64(leaf_key)       # MaterialLeafProvider (root)
    w.chunk(0xFFFF, 0, 0)                   # EndOfFile
    buf = io.BytesIO()
    with gzip.GzipFile(fileobj=buf, mode="wb", mtime=0) as g:
        g.write(bytes(w.b))
    return buf.getvalue(), th, agl

if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "HollowEarth.vx2"
    data, th, agl = build()
    open(out, "wb").write(data)
    print(f"{out}: {len(data)} bytes  size=262144 treeHeight={th} accessLod={agl} providerTypeId=770077")
