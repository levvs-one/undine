#version 300 es
// Undine: the surface advanced by dt in the Fourier domain, exactly. The complex texture holds Z = H + iV, the
// spectra of height and vertical velocity of the mirrored field; Z(k) and Z(−k) give H and V apart. Each mode is
// the damped oscillator ḧ = −ω²h − 2βḣ with ω² = (g·k + σk³/ρ)·tanh(k·h) and β = νk² + γ/2, solved in closed form,
// so there is no stability limit and no substep. The mean is held at zero: the liquid keeps its volume. The 1/size²
// of the inverse transform is folded in here.
precision highp float;
precision highp int;

uniform sampler2D uSpectrum;
uniform float uCell;           // metres per cell
uniform float uDepth;          // metres
uniform float uTension;        // σ/ρ, m³/s²
uniform float uViscosity;      // kinematic, m²/s
uniform float uDamping;        // γ, 1/s
uniform float uDt;             // seconds
out vec2 outValue;

const float G = 9.80665;
const float TWO_PI = 6.283185307;

void main() {
    ivec2 size = textureSize(uSpectrum, 0);
    ivec2 p = ivec2(gl_FragCoord.xy);
    ivec2 q = ivec2((size.x - p.x) % size.x, (size.y - p.y) % size.y);
    vec2 z = texelFetch(uSpectrum, p, 0).rg;
    vec2 w = texelFetch(uSpectrum, q, 0).rg;
    // H = (Z + conj(W))/2, V = (Z − conj(W))/(2i).
    vec2 h = 0.5 * vec2(z.x + w.x, z.y - w.y);
    vec2 v = 0.5 * vec2(z.y + w.y, -(z.x - w.x));
    float dk = TWO_PI / (float(size.x) * uCell);
    float kx = float(p.x < size.x / 2 ? p.x : p.x - size.x) * dk;
    float ky = float(p.y < size.y / 2 ? p.y : p.y - size.y) * dk;
    float k = length(vec2(kx, ky));
    float scale = 1.0 / (float(size.x) * float(size.y));
    if (k == 0.0) { outValue = vec2(0.0); return; }
    float omega2 = (G * k + uTension * k * k * k) * tanh(k * uDepth);
    float beta = uViscosity * k * k + 0.5 * uDamping;
    float disc = omega2 - beta * beta;
    float c, s;
    if (disc > 1e-12) {
        float wf = sqrt(disc);
        c = cos(wf * uDt);
        s = sin(wf * uDt) / wf;
    } else if (disc < -1e-12) {
        float wf = sqrt(-disc);
        float e = exp(wf * uDt), ei = 1.0 / e;
        c = 0.5 * (e + ei);
        s = 0.5 * (e - ei) / wf;
    } else {
        c = 1.0;
        s = uDt;
    }
    float decay = exp(-beta * uDt);
    vec2 nh = decay * (h * c + (v + beta * h) * s);
    vec2 nv = decay * (v * c - (omega2 * h + beta * v) * s);
    // Z' = H' + iV'.
    outValue = vec2(nh.x - nv.y, nh.y + nv.x) * scale;
}
