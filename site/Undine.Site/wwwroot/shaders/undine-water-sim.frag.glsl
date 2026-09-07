#version 300 es
// Undine: the surface back in real space, plus what touches it. The spectral step (undine-water-fft, undine-water-evolve)
// advanced the mirrored field exactly; this pass takes the pool's own quadrant out of it, height in the real part and
// vertical velocity in the imaginary, and applies the forces that are not linear waves: a finger holding the surface
// down, wind as a random pressure. The texture written holds height (r, metres) and velocity (g, m/s) per cell.
precision highp float;
precision highp int;

uniform sampler2D uSpectral;   // the evolved field, twice the pool's size
uniform float uCell;           // cell size, metres
uniform float uDt;             // seconds since the last step
uniform float uWind;           // wind on the surface: random pressure, m/s² of vertical acceleration
uniform vec2 uSeed;            // changes every step so the gusts do
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
    ivec2 p = ivec2(gl_FragCoord.xy);
    vec2 state = texelFetch(uSpectral, p, 0).rg;
    float h = state.x, v = state.y;
    if (uWind > 0.0) v += uWind * gust((vec2(p) + 0.5) * uCell / 0.12 + uSeed) * uDt;
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
