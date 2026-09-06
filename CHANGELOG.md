# Changelog

## 0.1.0 (unreleased)

- First milestone: water. `Surface` height field with viscous damping, `LiquidOptics` from Caustikon's liquid entries,
  `CausticMap` by ray counting, nine liquids with handbook mechanics.
- Site, in the Caustikon format (Blazor WebAssembly, English and Russian): the WebGL2 pool with touch and orbit,
  the liquid table with wave speeds, one "Give Me!" page per craft (design, games, 3D and Blender, web, .NET) that
  writes the numbers for a chosen liquid and depth into GLSL, HLSL, Unity, Unreal, Godot, Blender, glTF, three.js and
  C# with every value explained, and About. The three GLSL files it runs, the HLSL file and the Godot shader are
  served for download; `liquids.json` is baked from the package.
- `LiquidOptics` treats an extinction table that stops short of the visible as no data instead of clamping to its
  edge (glycerol's infrared-only table had made the liquid black).
