"""Parse a vanilla .vx2 to validate the framing I reverse-engineered from MyOctreeStorage.cs."""
import gzip, struct, sys

CHUNK = {1:"StorageMetaData",2:"MaterialIndexTable",3:"MacroContentNodes",4:"MacroMaterialNodes",
         5:"ContentLeafProvider",6:"ContentLeafOctree",7:"MaterialLeafProvider",8:"MaterialLeafOctree",
         9:"DataProvider",0xFFFF:"EndOfFile"}

class R:
    def __init__(self, b): self.b=b; self.i=0
    def u8(self): v=self.b[self.i]; self.i+=1; return v
    def bytes(self,n): v=self.b[self.i:self.i+n]; self.i+=n; return v
    def i32(self): v=struct.unpack_from("<i",self.b,self.i)[0]; self.i+=4; return v
    def u16(self): v=struct.unpack_from("<H",self.b,self.i)[0]; self.i+=2; return v
    def v7(self):
        n=0;s=0
        while True:
            x=self.u8(); n|=(x&0x7f)<<s
            if not (x&0x80): return n
            s+=7
    def vstr(self):
        n=self.v7(); s=self.bytes(n).decode("utf-8"); return s

def main(path):
    raw = open(path,"rb").read()
    if raw[0]==0x1f and raw[1]==0x8b:
        data = gzip.decompress(raw); print(f"[gzip] {len(raw)} -> {len(data)} bytes")
    else:
        data = raw; print(f"[raw] {len(raw)} bytes")
    r = R(data)
    name = r.vstr(); ver = r.v7()
    print(f"type string = {name!r}   fileVersion = {ver}")
    if ver == 2:
        acc = r.u16(); print(f"storageAccess gridLod = {acc}")
    # walk chunks
    for _ in range(40):
        ctype = r.v7();
        if ctype == 0xFFFF:
            print("EndOfFile"); break
        cver = r.v7(); csize = r.v7()
        nm = CHUNK.get(ctype, f"?{ctype}")
        print(f"  chunk {nm:20s} v{cver} size={csize}")
        if nm == "StorageMetaData":
            j=r.i; four=r.i32(); sx=r.i32(); sy=r.i32(); sz=r.i32(); b0=r.u8()
            print(f'      meta: lead={four} size=({sx},{sy},{sz}) trailer={b0}')
        elif nm == "MaterialIndexTable":
            j=r.i; cnt=r.i32(); print(f"      materials: {cnt}")
            for _ in range(min(cnt,6)):
                idx=r.v7(); s=r.vstr(); print(f"        [{idx}] {s}")
            r.i = j + csize
        else:
            r.bytes(csize)  # NOTE: macro-content access bytes not handled; fine for provider-based vanilla files

if __name__=="__main__":
    main(sys.argv[1])
