#version 300 es
// Undine: one substep of the surface. The texture holds height (r, metres) and vertical velocity (g, m/s) per cell.
// v += c²∇²h·dt + ν∇²v·dt; h += v·dt. c = √(g·depth) for long waves, ν the liquid's kinematic viscosity.
// Stable while dt ≤ cell/(c·√2) and dt ≤ 0.1·cell²/ν; the host picks substeps to keep both.
precision highp float;

uniform sampler2D uState;      // previous h, v
uniform float uCell;           // cell size, metres
uniform float uDt;             // substep, seconds
uniform float uWaveSpeed;      // √(g·depth), m/s
uniform float uViscosity;      // kinematic viscosity, m²/s
uniform vec2 uTouch;           // where a finger is, in cell units; negative when none
uniform float uTouchRadius;    // cells
uniform float uTouchAmount;    // metres per substep
out vec4 outState;

void main() {
    ivec2 size = textureSize(uState, 0);
    ivec2 p = ivec2(gl_FragCoord.xy);
    vec2 c = texelFetch(uState, p, 0).rg;
    vec2 l = texelFetch(uState, ivec2(max(p.x - 1, 0), p.y), 0).rg;
    vec2 r = texelFetch(uState, ivec2(min(p.x + 1, size.x - 1), p.y), 0).rg;
    vec2 d = texelFetch(uState, ivec2(p.x, max(p.y - 1, 0)), 0).rg;
    vec2 u = texelFetch(uState, ivec2(p.x, min(p.y + 1, size.y - 1)), 0).rg;
    vec2 lap = l + r + d + u - 4.0 * c;
    float cell2 = uCell * uCell;
    float v = c.g + (uWaveSpeed * uWaveSpeed * lap.r + uViscosity * lap.g) / cell2 * uDt;
    float h = c.r + v * uDt;
    if (uTouch.x >= 0.0) {
        vec2 offset = (vec2(p) + 0.5) - uTouch;
        h += uTouchAmount * exp(-dot(offset, offset) / (2.0 * uTouchRadius * uTouchRadius));
    }
    outState = vec4(h, v, 0.0, 1.0);
}
