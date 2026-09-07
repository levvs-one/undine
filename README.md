# Undine

Liquids for design, games, 3D and .NET. Water first.

[![verify](https://github.com/levvs-one/undine/actions/workflows/verify.yml/badge.svg)](https://github.com/levvs-one/undine/actions/workflows/verify.yml)
[![MIT](https://img.shields.io/badge/license-MIT-1d5f74)](LICENSE)

A pool that moves on the GPU and is lit the way water is lit. The surface is the long-wave equation
with the liquid's own viscosity; the light through it is [Caustikon](https://github.com/levvs-one/caustikon)'s
optics for that liquid, one refractive index per colour channel and absorption from its measured k table;
the lamp's caustics on the floor come from refracted rays, not from a texture. Nine liquids, water to
carbon disulfide, each with its handbook density, viscosity and surface tension.

The site at <https://undine.levvs.cc> is the pool: touch it, change the liquid, take the shaders. The surface runs at
the liquid's own ripple speed from its dispersion relation, the light is Caustikon's, and the caustics on the floor
and walls are refracted rays.

## What is in the repository

| Path | What |
| --- | --- |
| `src/Undine` | The .NET package: `Liquid` and `Liquids`, `Surface` (height field with viscous damping), `LiquidOptics` (indices, Fresnel, absorption from Caustikon), `CausticMap`. |
| `shaders/` | GLSL ES 3.00 for the spectral surface step (FFT pass, mode evolution, forces), the caustics and the pool render (what the site runs); an HLSL file for Unity and Unreal; a Godot 4 shader. |
| `site/Undine.Site/` | The site: Blazor WebAssembly in the Caustikon format. The WebGL2 pool, the liquid table, one "Give Me!" page per craft with the numbers and the shaders. `wwwroot/data/liquids.json` is baked by `tools/Undine.Bake`. |
| `tests/` | What the physics must keep doing: water's index and its blue over metres, wave speed, a drop spreading, glycerol dying, a flat surface lighting the floor evenly and a ripple focusing it. |
| `external/caustikon` | Caustikon as a submodule, the source of every optical number. |

## Install

```
git clone --recurse-submodules https://github.com/levvs-one/undine.git
dotnet build Undine.slnx -c Release
dotnet test --project tests/Undine.Tests/Undine.Tests.csproj -c Release -f net10.0
dotnet run --project tools/Undine.Bake -c Release     # site/Undine.Site/wwwroot/data/liquids.json
```

## Use

```csharp
using System.Numerics;
using Undine;

Liquid water = Liquids.Water;                       // optics from Caustikon, mechanics from the CRC Handbook
LiquidOptics optics = new(water);
Vector3 n = optics.IndexRgb;                        // 1.3327, 1.3347, 1.3387 at 610, 550, 465 nm
Vector3 tenMetres = optics.Transmittance(10);       // what white becomes after ten metres: blue

Surface pool = new(cells: 256, sizeMetres: 4, depthMetres: 1.2, water);
pool.Disturb(xMetres: 2, yMetres: 2, radiusMetres: 0.05, amountMetres: -0.01);   // a drop
pool.Step(1 / 60d);                                 // substeps as stability requires
Vector3 normal = pool.NormalAt(128, 128);

CausticMap floor = new(256);
floor.Build(pool, optics, lightDirection: new Vector3(0.3f, -1f, 0.2f), raysPerSide: 512);
```

## What is modelled, and what is not

The surface is a height field: v += c²∇²h·dt + ν∇²v·dt, h += v·dt, with c = √(g·depth) and ν = η/ρ.
The substep is bounded by both the wave and the diffusion stability limits. Light: exact Fresnel per
channel, Snell's law per channel, Beer–Lambert absorption over the path, caustics by ray counting.

Not modelled yet: dispersive gravity–capillary waves (`Liquid.PhaseSpeed` gives the true phase speed,
the height field runs at one speed), breaking, splashes, foam, floating bodies, light reflected off
the floor back up through the surface. The next stages are viscous liquids on particles (oil, honey)
and slime as a viscoelastic material.

## Data

Optics: Caustikon's liquids, generated from the RefractiveIndex.INFO database (CC0 1.0), each with
its citation. Mechanics: CRC Handbook of Chemistry and Physics, 97th ed., values at 20 °C, rounded.

## License

[MIT](LICENSE). Copyright (c) 2026 levvs-one.
