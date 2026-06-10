"""Bake torus / mandelbulb / holed_sphere as high-res planetoids out in space above spawn."""
import os, time
import numpy as np
import gen, vx2

DEST = r"C:\Users\xerdi\AppData\Roaming\SpaceEngineers\Saves\76561197981769644\Donut Test"
LOCAL = vx2.__file__.rsplit("\\", 1)[0]

P = np.array([-36689.902727342705, -36694.22242233012, -36695.40719665188])  # player
up = P / np.linalg.norm(P)
t1 = np.cross(up, [0, 0, 1.0]); t1 /= np.linalg.norm(t1)

UPDIST = 5000.0   # metres straight up (into space above EarthLike)
SPREAD = 1600.0   # metres between neighbouring planetoids

#       storage        shape          res   offset-from-centre        entity id
PLAN = [
    ("Donut",      "torus",        512,  P + UPDIST*up,             8800000000000000128),
    ("Mandelbulb", "mandelbulb",   512,  P + UPDIST*up + SPREAD*t1, 8800000000000000256),
    ("Sponge",     "holed_sphere", 512,  P + UPDIST*up - SPREAD*t1, 8800000000000000333),
]

for storage, fn, res, center, entid in PLAN:
    t = time.time()
    grid = gen.SHAPES[fn](res)
    data, stats = vx2.build_vx2(grid)
    open(os.path.join(DEST, storage + ".vx2"), "wb").write(data)
    open(os.path.join(LOCAL, storage + ".vx2"), "wb").write(data)
    corner = center - stats["size"] / 2.0
    solid = int((grid > 127).sum())
    print(f"{storage:11s} {fn:13s} res={res} solid={solid:>10,}  size={stats['size']} "
          f"macro={stats['macroNodes']} leaves={stats['leaves']} "
          f"bytes={len(data):>9,}  {time.time()-t:5.1f}s")
    print(f"    ID {entid}  POS  {corner[0]:.1f} {corner[1]:.1f} {corner[2]:.1f}")
