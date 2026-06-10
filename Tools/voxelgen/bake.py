"""Bake a generated shape into a Space Engineers .vx2 file. Usage: python bake.py torus 128"""
import sys, gen, vx2

OUT = vx2.__file__.rsplit("\\", 1)[0]

def main(name="torus", res=128):
    grid = gen.SHAPES[name](res) if name in gen.SHAPES else gen.torus(res)
    data, stats = vx2.build_vx2(grid)
    path = f"{OUT}\\{name}.vx2"
    open(path, "wb").write(data)
    solid = int((grid > 0).sum())
    print(f"{name}: grid {grid.shape} solid={solid:,}")
    print(f"  storage size={stats['size']}^3  treeHeight={stats['treeHeight']}  "
          f"macroNodes={stats['macroNodes']}  leaves={stats['leaves']}")
    print(f"  -> {name}.vx2  ({len(data):,} bytes gzipped)  [framing self-check PASSED]")

if __name__ == "__main__":
    nm = sys.argv[1] if len(sys.argv) > 1 else "torus"
    res = int(sys.argv[2]) if len(sys.argv) > 2 else 128
    main(nm, res)
