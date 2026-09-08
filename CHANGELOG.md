# Changelog

## 0.1.0 (unreleased)

- First milestone: water. `Surface` height field with viscous damping, `LiquidOptics` from Caustikon's liquid entries,
  `CausticMap` by ray counting, nine liquids with handbook mechanics.
- Site, in the Caustikon format (Blazor WebAssembly, English and Russian): the WebGL2 pool with touch and orbit,
  the liquid table with wave speeds, one "Give Me!" page per craft (design, games, 3D and Blender, web, .NET) that
  writes the numbers for a chosen liquid and depth into GLSL, HLSL, Unity, Unreal, Godot, Blender, glTF, three.js and
  C# with every value explained, and About. The three GLSL files it runs, the HLSL file and the Godot shader are
  served for download; `liquids.json` is baked from the package.
- The pool reworked: the field runs at the liquid's phase speed for the touch wavelength (the full gravity–capillary
  relation) instead of the long-wave √(g·h), with a damping slider and a wind slider (random pressure on the surface);
  the deck stands above the waterline so the walls show dry, wet and refracted; the reflection meets the pool's own
  walls; caustics are Gaussian splats over a map that reaches past the floor and light the walls too; the lining is
  filtered against distance; the camera looks from the deck. `Surface` takes an optional wavelength and damping.
- Particles: `ParticleFluid`, position-based fluids with the liquid's density, viscosity (Monaghan) and surface
  tension (Akinci cohesion, strength from σ by the pairwise-force relation); the constraint against the rest lattice
  with the walls' hidden share counted, under-relaxed, forces before the projection, XSPH. Tests for every liquid: a
  block settles to its volume and to rest, a dam break within Ritter's bound, a stretched free drop rounds itself on
  Rayleigh's time and stays whole. The site's Splash page runs it on the GPU (bitonic cell sort, screen-space fluid).
- Pouring: `ShallowFlow`, the shallow-water equations over a floor with a no-slip bottom (drag 3νu/h²) that blends
  into Huppert's thin-film flux when the drag relaxes the film within a step; HLL flux, hydrostatic reconstruction,
  walls, a spout. Tests for every liquid: a still puddle on an uneven floor stays still and keeps its volume, a
  released sheet runs at Ritter's front speed when free and creeps when viscous, glycerol spreads as Huppert's
  R ∝ t^(1/8), pouring fills a dish and overflows it. The site's Pour page runs the same scheme on the GPU.
- The pool's camera turns with the right button or two fingers; the Orbit switch is gone.
- `LiquidOptics` treats an extinction table that stops short of the visible as no data instead of clamping to its
  edge (glycerol's infrared-only table had made the liquid black).
