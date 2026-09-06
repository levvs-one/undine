# Undine

Liquids for design, games, 3D and .NET. Water first.

`Undine` is the motion: a surface as a height field with the long-wave equation and viscous damping,
a caustic map for the light on the floor, and a table of liquids with the handbook numbers that
set wave speed and damping. The light itself comes from [Caustikon](https://github.com/levvs-one/caustikon):
each liquid's dispersion fit and absorption table, the exact Fresnel split, and Snell's law.

```csharp
using System.Numerics;
using Undine;

Liquid water = Liquids.Water;                       // optics from Caustikon, mechanics from the CRC Handbook
LiquidOptics optics = new(water);
Vector3 n = optics.IndexRgb;                        // 1.3315, 1.3339, 1.3382 at 610, 550, 465 nm
Vector3 tenMetres = optics.Transmittance(10);       // what white becomes after ten metres: blue

Surface pool = new(cells: 256, sizeMetres: 4, depthMetres: 1.5, water);
pool.Disturb(xMetres: 2, yMetres: 2, radiusMetres: 0.05, amountMetres: -0.01);   // a drop
pool.Step(1 / 60d);                                 // substeps as stability requires
Vector3 normal = pool.NormalAt(128, 128);

CausticMap floor = new(256);
floor.Build(pool, optics, lightDirection: new Vector3(0.3f, -1f, 0.2f), raysPerSide: 512);
```

The site at <https://undine.levvs.cc> runs the same scheme on the GPU with touch, and hands out the
shaders for WebGL, Unity, Unreal and Godot.
