"""Decode a .vx2 back to a content grid the way SE does, to verify what the game actually sees."""
import gzip, struct, sys
import numpy as np

def unpack64(k): return (k>>60)&0xF, (k>>40)&0xFFFFF, (k>>20)&0xFFFFF, k&0xFFFFF
def unpack32(k): return (k>>28)&0xF, (k>>18)&0x3FF, (k>>10)&0xFF, k&0x3FF

class R:
    def __init__(s,b): s.b=b; s.i=0
    def u8(s): v=s.b[s.i]; s.i+=1; return v
    def i32(s): v=struct.unpack_from("<i",s.b,s.i)[0]; s.i+=4; return v
    def u16(s): v=struct.unpack_from("<H",s.b,s.i)[0]; s.i+=2; return v
    def u32(s): v=struct.unpack_from("<I",s.b,s.i)[0]; s.i+=4; return v
    def u64(s): v=struct.unpack_from("<Q",s.b,s.i)[0]; s.i+=8; return v
    def data8(s): v=list(s.b[s.i:s.i+8]); s.i+=8; return v
    def skip(s,n): s.i+=n
    def v7(s):
        n=0;sh=0
        while True:
            x=s.u8(); n|=(x&0x7f)<<sh
            if not(x&0x80): return n
            sh+=7
    def vstr(s):
        n=s.v7(); v=s.b[s.i:s.i+n].decode("utf-8"); s.i+=n; return v

# morton decode for 16^3
def morton_xyz():
    out=[]
    for c in range(16**3):
        x=y=z=0
        for b in range(4):
            x|=((c>>(3*b))&1)<<b; y|=((c>>(3*b+1))&1)<<b; z|=((c>>(3*b+2))&1)<<b
        out.append((x,y,z))
    return out
_MORT=morton_xyz()

def decode_micro(nodes):
    """nodes: {pack32:(cm,data)} height-4 octree -> 16^3 numpy [x,y,z]."""
    block=np.zeros((16,16,16),np.uint8)
    code=[0]
    def walk(lod,cx,cy,cz,inherited):
        key=(lod<<28)|(cx<<18)|(cy<<10)|cz
        if key not in nodes:
            # uniform subtree
            span=8**lod
            for _ in range(span):
                x,y,z=_MORT[code[0]]; block[x,y,z]=inherited; code[0]+=1
            return
        cm,data=nodes[key]
        for j in range(8):
            ox,oy,oz=j&1,(j>>1)&1,(j>>2)&1
            if lod==0:
                x,y,z=_MORT[code[0]]; block[x,y,z]=data[j]; code[0]+=1
            else:
                if cm&(1<<j): walk(lod-1,cx*2+ox,cy*2+oy,cz*2+oz,data[j])
                else:
                    span=8**lod
                    for _ in range(span):
                        x,y,z=_MORT[code[0]]; block[x,y,z]=data[j]; code[0]+=1
    walk(3,0,0,0,0)
    return block

def read_vx2(path):
    raw=open(path,"rb").read()
    data=gzip.decompress(raw) if raw[:2]==b"\x1f\x8b" else raw
    r=R(data); assert r.vstr()=="Octree"; fver=r.v7()
    if fver==2: r.u16()
    size=None; macro={}; leaves={}
    while True:
        ct=r.v7(); cv=r.v7(); cs=r.v7()
        if ct==0xFFFF: break
        if ct==1:   # metadata
            r.i32(); size=r.i32(); r.i32(); r.i32(); r.u8()
        elif ct==2: # material table
            r.skip(cs)
        elif ct==3: # macro content + access bytes
            agl=min(size.bit_length()-1,10); access_lod=agl-5
            cnt=cs//17
            for _ in range(cnt):
                key=r.u64(); cm=r.u8(); d=r.data8()
                macro[key]=(cm,d)
                if (key>>60)==access_lod: r.u16()
        elif ct==4: # macro material — skip
            r.skip(cs)
        elif ct==6: # content leaf octree
            key=r.u64(); th=r.i32(); dc=r.u8()
            nodes={}; cnt=(cs-8-5)//13
            for _ in range(cnt):
                k=r.u32(); cm=r.u8(); dd=r.data8(); nodes[k]=(cm,dd)
            leaves[key]=nodes
        else:
            r.skip(cs)
    return size, macro, leaves

def reconstruct(path):
    size, macro, leaves = read_vx2(path)
    th = size.bit_length()-1-4
    grid=np.zeros((size,size,size),np.uint8)
    def fill(x0,y0,z0,edge,val):
        grid[x0:x0+edge,y0:y0+edge,z0:z0+edge]=val
    def walk(lod,cx,cy,cz,inherited):
        key=(lod<<60)|(cx<<40)|(cy<<20)|cz
        edge=1<<(lod+5)
        if key not in macro:
            fill(cx*edge,cy*edge,cz*edge,edge,inherited); return
        cm,data=macro[key]
        for j in range(8):
            ox,oy,oz=j&1,(j>>1)&1,(j>>2)&1
            ax,ay,az=cx*2+ox,cy*2+oy,cz*2+oz
            if lod==0:                       # children are 16^3 leaf cells
                if cm&(1<<j):
                    lkey=(0<<60)|(ax<<40)|(ay<<20)|az
                    block=decode_micro(leaves[lkey]) if lkey in leaves else None
                    if block is not None: grid[ax*16:ax*16+16,ay*16:ay*16+16,az*16:az*16+16]=block
                    else: fill(ax*16,ay*16,az*16,16,data[j])
                else:
                    fill(ax*16,ay*16,az*16,16,data[j])
            else:
                if cm&(1<<j): walk(lod-1,ax,ay,az,data[j])
                else:
                    ce=1<<(lod-1+5); fill(ax*ce,ay*ce,az*ce,ce,data[j])
    walk(th-1,0,0,0,0)
    return grid, dict(size=size, macro=len(macro), leaves=len(leaves))

if __name__=="__main__":
    g,info=reconstruct(sys.argv[1])
    solid=int((g>127).sum())
    print(f"decoded {info}  solid(>127)={solid:,} ({100*solid/g.size:.2f}%)")
    # compare to original torus if available
    try:
        import gen
        ref=gen.torus(info['size']) if info['size']<=192 else None
        if ref is not None and ref.shape==g.shape:
            match=int((( g>127)==(ref>127)).sum()); tot=g.size
            print(f"vs gen.torus({info['size']}): {100*match/tot:.3f}% voxels match  "
                  f"(orig solid={int((ref>127).sum()):,})")
    except Exception as e:
        print("compare skipped:", e)
    # save a preview of what SE sees
    import importlib; gen=importlib.import_module("gen")
    gen.preview_png(g, "decoded_torus")
    print("preview -> decoded_torus.png")
