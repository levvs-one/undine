#version 300 es
// Undine: one substep of the surface. The texture holds height (r, metres) and vertical velocity (g, m/s) per cell.
// v += c²∇²h·dt + ν∇²v·dt − γ·v·dt; h += v·dt. The field runs at one speed c: the liquid's phase speed for the
// wavelength the touches make, c² = (g/k + σk/ρ)·tanh(kh), which the host evaluates from the liquid's numbers.
// ν is the kinematic viscosity; γ is the extra decay the host asks for beyond it (a pool's rim and surface film).
// Wind is a random pressure over the surface, smooth over a few cells and new every substep.
// Stable while dt < cell/(c·√2) and dt ≤ 0.1·cell²/ν; at the wave bound itself the checkerboard mode grows without limit,
// so the host keeps a margin below it when it picks substeps.
precision highp float;

uniform sampler2D uState;      // previous h, v
uniform float uCell;           // cell size, metres
uniform float uDt;             // substep, seconds
uniform float uWaveSpeed;      // phase speed of the touch wavelength, m/s
uniform float uViscosity;      // kinematic viscosity, m²/s
uniform float uDamping;        // extra decay, 1/s
uniform float uWind;           // wind on the surface: random pressure, m/s² of vertical acceleration
uniform vec2 uSeed;            // changes every substep so the gusts do
uniform vec2 uTouch;           // where a finger is, in cell units; negative when none
uniform float uTouchRadius;    // cells
uniform float uTouchDepth;     // how far a finger holds the surface down at its centre, metres
out vec4 outState;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

// Value noise in [-1, 1], smooth over one unit of p.
float gust(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash(i), b = hash(i + vec2(1.0, 0.0)), c = hash(i + vec2(0.0, 1.0)), d = hash(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y) * 2.0 - 1.0;
}

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
    if (uWind > 0.0) v += uWind * gust((vec2(p) + 0.5) * uCell / 0.07 + uSeed) * uDt;
    v *= exp(-uDamping * uDt);
    float h = c.r + v * uDt;
    if (uTouch.x >= 0.0) {
        // A finger holds the surface down to its own depth, not further: the dent is pulled toward −depth·g with
        // weight g, so it is exact at the centre, nothing at the edge, and never deepens while the finger stays.
        vec2 offset = (vec2(p) + 0.5) - uTouch;
        float g = exp(-dot(offset, offset) / (2.0 * uTouchRadius * uTouchRadius));
        h -= max(0.0, h + uTouchDepth * g) * g;
    }
    // Whatever goes wrong upstream (a lost context, a step too long), the water comes back rather than staying grey.
    if (!(abs(h) < 10.0 && abs(v) < 100.0)) { h = 0.0; v = 0.0; }
    outState = vec4(h, v, 0.0, 1.0);
}
