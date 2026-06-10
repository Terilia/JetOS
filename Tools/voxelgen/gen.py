"""
voxelgen — generate weird voxel bodies for Space Engineers as 3D density grids.

Output per shape:
  <name>.npy   uint8 grid, 0 = empty, 255 = solid  (content/density)
  <name>.png   3-view depth-shaded preview (look along X, Y, Z)

The .npy grids are the bake-ready geometry. How they become a .vx2/voxel map
(ModAPI mod or SEToolbox) is a separate step — this file only makes the shape.

Run:  python gen.py            # all shapes at default res
      python gen.py torus 256  # one shape at a chosen resolution
"""
import sys
import numpy as np
from PIL import Image

OUT = __file__.rsplit("\\", 1)[0] if "\\" in __file__ else "."


# ---------------------------------------------------------------- coordinate grid
def _coords(n):
    """Return X,Y,Z arrays in [-1,1], shape (n,n,n)."""
    a = np.linspace(-1.0, 1.0, n, dtype=np.float32)
    return np.meshgrid(a, a, a, indexing="ij")


def _solid(mask):
    """bool mask -> uint8 content grid."""
    g = np.zeros(mask.shape, dtype=np.uint8)
    g[mask] = 255
    return g


def _content(sdf_voxels):
    """Signed distance (in voxel units, negative = inside) -> graded 0..255 density.
    Smooth ~1-voxel transition across the isosurface so SE meshes clean curves."""
    return (np.clip(0.5 - sdf_voxels, 0.0, 1.0) * 255.0).astype(np.uint8)


# ---------------------------------------------------------------- shapes
def torus(n=256, R=0.62, r=0.28):
    """Donut. R = ring radius, r = tube radius (both in unit-cube units). Smooth surface."""
    X, Y, Z = _coords(n)
    q = np.sqrt(X * X + Y * Y) - R          # distance from the ring circle (in XY plane)
    d = np.sqrt(q * q + Z * Z) - r          # signed distance to tube surface (unit-cube)
    return _content(d * (n / 2.0))          # -> voxel units -> graded density


def holed_sphere(n=256, radius=0.92, freq=7.0, wall=0.34):
    """A 'Swiss-cheese' sphere: a solid ball carved by a gyroid sponge so it's
    full of interconnected holes/tunnels. Smooth surface (approximate SDF)."""
    X, Y, Z = _coords(n)
    f = freq * np.pi
    g = (np.sin(f * X) * np.cos(f * Y)
         + np.sin(f * Y) * np.cos(f * Z)
         + np.sin(f * Z) * np.cos(f * X))
    ball = (np.sqrt(X * X + Y * Y + Z * Z) - radius) * (n / 2.0)
    shell = (np.abs(g) - wall) / f * (n / 2.0)   # approx distance to gyroid shell
    return _content(np.maximum(ball, shell))     # intersection = max of SDFs


def mandelbulb(n=512, power=8, iters=24, bailout=2.0, slab=24):
    """Power-8 Mandelbulb distance-estimate isosurface, graded + memory-chunked
    so it scales to 512^3 / 1024^3 without exhausting RAM (computed in z-slabs)."""
    out = np.zeros((n, n, n), np.uint8)
    a = np.linspace(-1.0, 1.0, n, dtype=np.float32)
    for z0 in range(0, n, slab):
        z1 = min(z0 + slab, n)
        X, Y, Z = np.meshgrid(a, a, a[z0:z1], indexing="ij")   # (n, n, zw)
        cx, cy, cz = X * 1.2, Y * 1.2, Z * 1.2                  # bulb domain [-1.2,1.2]
        zx = np.zeros_like(cx); zy = np.zeros_like(cx); zz = np.zeros_like(cx)
        dr = np.ones_like(cx); r = np.zeros_like(cx)
        alive = np.ones(cx.shape, dtype=bool)
        for _ in range(iters):
            r = np.sqrt(zx * zx + zy * zy + zz * zz)
            alive &= r <= bailout
            rs = np.where(r == 0, 1e-9, r)
            theta = np.arccos(np.clip(zz / rs, -1.0, 1.0))
            phi = np.arctan2(zy, zx)
            dr = np.where(alive, (rs ** (power - 1)) * power * dr + 1.0, dr)
            zr = rs ** power
            theta *= power; phi *= power
            st = np.sin(theta)
            zx = np.where(alive, zr * st * np.cos(phi) + cx, zx)
            zy = np.where(alive, zr * st * np.sin(phi) + cy, zy)
            zz = np.where(alive, zr * np.cos(theta) + cz, zz)
        rs = np.where(r == 0, 1e-9, r)
        de = 0.5 * np.log(rs) * rs / dr                        # distance estimate
        g = _content(de * (n / 2.4))
        g[alive] = 255                                         # bounded interior solid
        out[:, :, z0:z1] = g
    return out


# ---------------------------------------------------------------- preview render
def _view(solid, axis):
    """Depth-shaded orthographic projection of a bool grid along `axis`."""
    s = np.swapaxes(solid, 0, axis)         # project along new axis 0
    any_hit = s.any(axis=0)
    depth = np.argmax(s, axis=0).astype(np.float32)
    n = s.shape[0]
    shade = 235.0 - 150.0 * (depth / max(n - 1, 1))   # nearer = brighter
    img = np.where(any_hit, shade, 16).astype(np.uint8)
    return np.flipud(img.T)                  # orient upright


def preview_png(solid_u8, name):
    solid = solid_u8 > 0
    views = [_view(solid, a) for a in (0, 1, 2)]
    h = max(v.shape[0] for v in views)
    gap = 8
    canvas = np.full((h, sum(v.shape[1] for v in views) + gap * 2, 3), 24, np.uint8)
    x = 0
    tint = [(150, 230, 150), (150, 200, 230), (230, 200, 150)]  # green/blue/amber per view
    for v, t in zip(views, tint):
        vh, vw = v.shape
        col = np.stack([(v.astype(np.float32) * (c / 255.0)).astype(np.uint8) for c in t], axis=-1)
        canvas[(h - vh) // 2:(h - vh) // 2 + vh, x:x + vw] = col
        x += vw + gap
    p = f"{OUT}\\{name}.png"
    Image.fromarray(canvas, "RGB").save(p)
    return p


# ---------------------------------------------------------------- driver
SHAPES = {
    "torus": torus,
    "holed_sphere": holed_sphere,
    "mandelbulb": mandelbulb,
}


def build(name, res=None):
    fn = SHAPES[name]
    grid = fn(res) if res else fn()
    np.save(f"{OUT}\\{name}.npy", grid)
    solid = int((grid > 0).sum())
    total = grid.size
    png = preview_png(grid, name)
    bb = grid.shape
    print(f"{name:14s} {bb[0]}^3  solid={solid:>10,} ({100*solid/total:5.2f}%)  -> {name}.npy + {name}.png")


if __name__ == "__main__":
    if len(sys.argv) >= 2:
        nm = sys.argv[1]
        res = int(sys.argv[2]) if len(sys.argv) >= 3 else None
        build(nm, res)
    else:
        for nm in SHAPES:
            build(nm)
