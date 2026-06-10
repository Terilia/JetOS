"""
vx2.py — write Space Engineers .vx2 (MyOctreeStorage) files from a 3D content grid.

Reverse-engineered 1:1 from the decompiled source:
  MyOctreeStorage.SaveInternal / MyStorageBase.GetData / MySparseOctree / MyOctreeNode
  / MyMicroOctreeLeaf / MyCellCoord / MyMortonCode3D / StreamExtensions.

Layout (all multi-byte values little-endian; 7bit = LEB128):
  gzip(
    string "Octree"; 7bit(fileVersion=2)
    u16(accessGridLod)                                   # WriteStorageAccess
    chunk StorageMetaData  v1 size17: i32(4) i32(Sx) i32(Sy) i32(Sz) u8(0)
    chunk MaterialIndexTable v1: i32(count) {7bit(idx) str(name)}...
    # (no DataProvider chunk — provider is null)
    chunk MacroContentNodes v2 size=N*17: {u64(key) u8(childmask) 8*u8(data)}...
        + a trailing u16(0) for each node whose (keyLod+5)==accessGridLod
    chunk MacroMaterialNodes v2 size=17: one uniform root node
    chunk ContentLeafOctree v3 (per non-uniform 16^3 cell): u64(key) + microOctree
    # (no material leaves — material is uniform)
    chunk EndOfFile
  )

content grid: uint8 [nx,ny,nz], 0 = empty .. 255 = solid (iso at 127).
"""
import gzip, io, struct
import numpy as np

LEAF = 16  # voxels per leaf cell edge (2^4)

# ---------------------------------------------------------------- bit packing
def pack64(lod, x, y, z):
    return ((lod & 0xF) << 60) | ((x & 0xFFFFF) << 40) | ((y & 0xFFFFF) << 20) | (z & 0xFFFFF)

def pack32(lod, x, y, z):
    return ((lod & 0xF) << 28) | ((x & 0x3FF) << 18) | ((y & 0xFF) << 10) | (z & 0x3FF)

# ---------------------------------------------------------------- morton order for a 16^3 leaf
def _morton_tables():
    mx = np.empty(LEAF**3, np.int64); my = np.empty(LEAF**3, np.int64); mz = np.empty(LEAF**3, np.int64)
    for code in range(LEAF**3):
        x = y = z = 0
        for b in range(4):                      # 16 = 2^4 levels
            x |= ((code >> (3*b + 0)) & 1) << b
            y |= ((code >> (3*b + 1)) & 1) << b
            z |= ((code >> (3*b + 2)) & 1) << b
        mx[code], my[code], mz[code] = x, y, z
    return mx, my, mz
_MX, _MY, _MZ = _morton_tables()

# ---------------------------------------------------------------- content filter (signed distance)
def _to_sd(v):  return v / 255.0 * 2.0 - 1.0
def _from_sd(s):
    b = int((s * 0.5 + 0.5) * 255.0 + 0.5)
    return 0 if b < 0 else (255 if b > 255 else b)
def _avg_filter(d):
    n = sum(_to_sd(x) for x in d) / 8.0
    if n != 1.0 and n != -1.0: n *= 0.5
    return _from_sd(n)
def content_filter(d):
    n = _to_sd(d[0])
    if _to_sd(_avg_filter(d)) != n or (n != 1.0 and n != -1.0):
        n *= 0.5
    return _from_sd(n)

def _all_same(d):
    return d[0] == d[1] == d[2] == d[3] == d[4] == d[5] == d[6] == d[7]

# ---------------------------------------------------------------- micro octree (one 16^3 leaf)
def build_micro(vals):
    """vals: numpy uint8 array len 4096 in morton order. Returns [(key32, childmask, data[8])].
    Uniform subtrees are collapsed without recursion (fast path)."""
    nodes = []
    def rec(lod, cx, cy, cz, base):
        data = [0]*8; cm = 0
        if lod == 0:
            for j in range(8):
                data[j] = int(vals[base + j])
            return cm, data, pack32(0, cx, cy, cz)
        span = 8**lod                            # voxels covered by each lod-1 child
        for j in range(8):
            ox, oy, oz = j & 1, (j >> 1) & 1, (j >> 2) & 1
            cb = base + j*span
            seg = vals[cb:cb+span]
            v0 = int(seg[0])
            if (seg == v0).all():                # uniform child -> collapse, no node
                data[j] = v0
            else:
                ccm, cdata, ckey = rec(lod-1, cx*2+ox, cy*2+oy, cz*2+oz, cb)
                data[j] = content_filter(cdata); cm |= (1 << j)
                nodes.append((ckey, ccm, cdata))
        return cm, data, pack32(lod, cx, cy, cz)
    cm, data, key = rec(3, 0, 0, 0, 0)          # height 4 -> start lod 3
    nodes.append((key, cm, data))
    return nodes

# ---------------------------------------------------------------- macro octree + leaves
def build_content(grid, size):
    """grid padded to size^3 uint8. Returns (macro_nodes{key64:(cm,data)}, leaves{key64:micronodes}, treeHeight)."""
    th = size.bit_length() - 1 - 4               # size = 2^k -> treeHeight = k-4
    n = size // LEAF                             # leaf cells per axis = 2^treeHeight
    uniform = {}; filt = {}; leaves = {}
    # ---- pass 1: classify every 16^3 leaf cell
    for lx in range(n):
        for ly in range(n):
            for lz in range(n):
                block = grid[lx*LEAF:(lx+1)*LEAF, ly*LEAF:(ly+1)*LEAF, lz*LEAF:(lz+1)*LEAF]
                bmin = int(block.min()); bmax = int(block.max())
                if bmin == bmax:
                    uniform[(lx, ly, lz)] = bmin
                else:
                    vals = np.ascontiguousarray(block[_MX, _MY, _MZ])   # 4096, morton order
                    mnodes = build_micro(vals)
                    leaves[pack64(0, lx, ly, lz)] = mnodes
                    # root filtered value of the micro octree = leaf.GetFilteredValue()
                    root_cm, root_data = mnodes[-1][1], mnodes[-1][2]
                    filt[(lx, ly, lz)] = content_filter(root_data)
    present = set(filt.keys())
    # ---- pass 2: build macro octree bottom-up with collapsing
    macro = {}
    def rec(lod, cx, cy, cz):
        data = [0]*8; cm = 0
        for j in range(8):
            ox, oy, oz = j & 1, (j >> 1) & 1, (j >> 2) & 1
            ax, ay, az = cx*2+ox, cy*2+oy, cz*2+oz
            if lod == 0:
                cell = (ax, ay, az)
                if cell in present:
                    data[j] = filt[cell]; cm |= (1 << j)
                else:
                    data[j] = uniform[cell]
            else:
                ccm, cdata = rec(lod-1, ax, ay, az)
                if ccm == 0 and _all_same(cdata):
                    data[j] = cdata[0]
                else:
                    data[j] = content_filter(cdata); cm |= (1 << j)
                    macro[pack64(lod-1, ax, ay, az)] = (ccm, cdata)
        return cm, data
    rcm, rdata = rec(th-1, 0, 0, 0)
    macro[pack64(th-1, 0, 0, 0)] = (rcm, rdata)
    return macro, leaves, th

# ---------------------------------------------------------------- stream primitives
class W:
    def __init__(s): s.b = bytearray()
    def u8(s, v): s.b.append(v & 0xFF)
    def i32(s, v): s.b += struct.pack("<i", v)
    def u16(s, v): s.b += struct.pack("<H", v & 0xFFFF)
    def u32(s, v): s.b += struct.pack("<I", v & 0xFFFFFFFF)
    def u64(s, v): s.b += struct.pack("<Q", v & 0xFFFFFFFFFFFFFFFF)
    def data8(s, d): s.b += bytes(x & 0xFF for x in d)
    def v7(s, v):
        v &= 0xFFFFFFFF
        while v >= 0x80:
            s.b.append((v & 0x7F) | 0x80); v >>= 7
        s.b.append(v)
    def vstr(s, t):
        e = t.encode("utf-8"); s.v7(len(e)); s.b += e
    def chunk_header(s, ctype, ver, size):
        s.v7(ctype); s.v7(ver); s.v7(size)

def _access_grid_lod(size):
    num = size.bit_length()                      # shifts to reduce 2^k to 0 == k+1 == bit_length
    return min(num - 1, 10)

# ---------------------------------------------------------------- top-level writer
def build_vx2(grid, material_name="Stone_01"):
    grid = np.ascontiguousarray(grid, dtype=np.uint8)
    dim = max(grid.shape)
    size = 1
    while size < dim or size < 32:               # min size 32 -> treeHeight >= 1
        size <<= 1
    pad = np.zeros((size, size, size), np.uint8)
    pad[:grid.shape[0], :grid.shape[1], :grid.shape[2]] = grid
    macro, leaves, th = build_content(pad, size)
    agl = _access_grid_lod(size)
    access_lod = agl - 5                          # content macro nodes at this key-lod carry a u16

    w = W()
    w.vstr("Octree"); w.v7(2)                     # fileVersion 2
    w.u16(agl)                                    # WriteStorageAccess

    # StorageMetaData
    w.chunk_header(1, 1, 17)
    w.i32(4); w.i32(size); w.i32(size); w.i32(size); w.u8(0)

    # MaterialIndexTable (just the one material we use, index 0)
    mt = W(); mt.i32(1); mt.v7(0); mt.vstr(material_name)
    w.chunk_header(2, 1, len(mt.b)); w.b += mt.b

    # MacroContentNodes (+ interleaved access u16 for nodes at access_lod)
    w.chunk_header(3, 2, len(macro) * 17)
    for key, (cm, data) in macro.items():
        w.u64(key); w.u8(cm); w.data8(data)
        if (key >> 60) == access_lod:
            w.u16(0)

    # MacroMaterialNodes — single uniform root (material 0 = Stone_01 everywhere)
    w.chunk_header(4, 2, 17)
    w.u64(pack64(th-1, 0, 0, 0)); w.u8(0); w.data8([0]*8)

    # ContentLeafOctree per non-uniform cell
    for key, mnodes in leaves.items():
        ser = 5 + len(mnodes) * 13               # MySparseOctree.SerializedSize
        w.chunk_header(6, 3, ser + 8)
        w.u64(key)
        w.i32(4); w.u8(0)                        # micro treeHeight=4, defaultContent=0
        for k32, cm, data in mnodes:
            w.u32(k32); w.u8(cm); w.data8(data)

    # (no material leaves)
    w.chunk_header(0xFFFF, 0, 0)                  # EndOfFile

    raw = bytes(w.b)
    verify_stream(raw, size, access_lod)         # round-trip framing check; raises on mismatch
    buf = io.BytesIO()
    with gzip.GzipFile(fileobj=buf, mode="wb", mtime=0) as g:
        g.write(raw)
    return buf.getvalue(), dict(size=size, treeHeight=th, macroNodes=len(macro), leaves=len(leaves))

# ---------------------------------------------------------------- self-verifier (mirrors LoadInternal framing)
class R:
    def __init__(s, b): s.b = b; s.i = 0
    def u8(s): v = s.b[s.i]; s.i += 1; return v
    def i32(s): v = struct.unpack_from("<i", s.b, s.i)[0]; s.i += 4; return v
    def u16(s): v = struct.unpack_from("<H", s.b, s.i)[0]; s.i += 2; return v
    def skip(s, n): s.i += n
    def v7(s):
        n = 0; sh = 0
        while True:
            x = s.u8(); n |= (x & 0x7F) << sh
            if not (x & 0x80): return n
            sh += 7
    def vstr(s):
        n = s.v7(); v = s.b[s.i:s.i+n].decode("utf-8"); s.i += n; return v

def verify_stream(raw, size, access_lod):
    r = R(raw)
    assert r.vstr() == "Octree"; assert r.v7() == 2
    r.u16()                                       # access grid lod
    while True:
        ctype = r.v7(); cver = r.v7(); csize = r.v7()
        if ctype == 0xFFFF: break
        if ctype == 3:                            # macro content: nodes + interleaved access
            cnt = csize // 17
            for _ in range(cnt):
                key = struct.unpack_from("<Q", r.b, r.i)[0]; r.skip(17)
                if (key >> 60) == access_lod: r.u16()
        else:
            r.skip(csize)
    if r.i != len(raw):
        raise AssertionError(f"framing mismatch: consumed {r.i} of {len(raw)} bytes")
