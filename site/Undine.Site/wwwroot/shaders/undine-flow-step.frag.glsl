#version 300 es
// Undine: one substep of a liquid flowing over a floor. The texture holds thickness h (r, metres), discharge q = h·u
// (g, b, m²/s) and the floor height (a, metres) per cell. Shallow-water equations, first order: the HLL flux with the
// hydrostatic reconstruction at each face (a still puddle stays still on any floor, h stays ≥ 0), the grid's edges
// are walls, then the spout adds liquid and the bottom's drag 3νu/h² acts implicitly. Where the drag relaxes the
// film within a step the mass flux blends into the lubrication flux −g·h³·∇η/(3ν), Huppert's thin-film equation.
// Both cells at a face evaluate the same flux from the same operands, so what leaves one enters the other exactly.
precision highp float;
precision highp int;

uniform sampler2D uState;
uniform float uCell;           // metres
uniform float uDt;             // seconds, within 0,25·cell/(|u| + √(g·h))
uniform float uViscosity;      // kinematic, m²/s
uniform float uSpeedCap;       // m/s: no liquid moves faster, which keeps the step within its bound
uniform vec2 uSpout;           // where the spout lands, cells; negative when off
uniform float uSpoutRadius;    // cells
uniform float uSpoutRate;      // metres of thickness per second over the spout's disc
out vec4 outState;

const float G = 9.80665;
const float DRY = 1e-7;
const float THIN = 1e-5;

struct Flux { float h; float qn; float qt; float sa; float sb; };

// The flux from cell a into cell b across their face; qn along the face normal, qt across it. a is always the
// lower-index cell so both sides compute it identically.
Flux face(vec4 A, vec4 B, bool an, bool bn) {
    // an, bn: whether the cell exists; a missing cell is a wall.
    Flux f;
    float ha = A.r, hb = B.r, ba = A.a, bb = B.a;
    if (!an) { f.h = 0.0; f.qn = 0.5 * G * hb * hb; f.qt = 0.0; f.sa = 0.0; f.sb = 0.0; return f; }
    if (!bn) { f.h = 0.0; f.qn = 0.5 * G * ha * ha; f.qt = 0.0; f.sa = 0.0; f.sb = 0.0; return f; }
    float bf = max(ba, bb);
    float hl = max(0.0, ha + ba - bf), hr = max(0.0, hb + bb - bf);
    float ul = ha > DRY ? A.g / ha : 0.0, ur = hb > DRY ? B.g / hb : 0.0;
    float vl = ha > DRY ? A.b / ha : 0.0, vr = hb > DRY ? B.b / hb : 0.0;
    f.sa = 0.5 * G * (hl * hl - ha * ha);
    f.sb = 0.5 * G * (hr * hr - hb * hb);
    if (hl <= DRY && hr <= DRY) { f.h = 0.0; f.qn = 0.0; f.qt = 0.0; return f; }
    float cl = sqrt(G * hl), cr = sqrt(G * hr);
    float sl, sr;
    if (hl <= DRY) { sl = ur - 2.0 * cr; sr = ur + cr; }
    else if (hr <= DRY) { sl = ul - cl; sr = ul + 2.0 * cl; }
    else { sl = min(ul - cl, ur - cr); sr = max(ul + cl, ur + cr); }
    float fhl = hl * ul, fhr = hr * ur;
    float fql = hl * ul * ul + 0.5 * G * hl * hl, fqr = hr * ur * ur + 0.5 * G * hr * hr;
    float ftl = hl * ul * vl, ftr = hr * ur * vr;
    if (sl >= 0.0) { f.h = fhl; f.qn = fql; f.qt = ftl; }
    else if (sr <= 0.0) { f.h = fhr; f.qn = fqr; f.qt = ftr; }
    else {
        float span = sr - sl;
        f.h = (sr * fhl - sl * fhr + sl * sr * (hr - hl)) / span;
        f.qn = (sr * fql - sl * fqr + sl * sr * (hr * ur - hl * ul)) / span;
        f.qt = (sr * ftl - sl * ftr + sl * sr * (hr * vr - hl * vl)) / span;
    }
    // The creeping regime: the film relaxes within the step, so the mass flux is the thin-film one.
    float hf = max(hl, hr);
    float relax = hf * hf / (3.0 * uViscosity);
    float inertial = relax / (relax + uDt);
    if (inertial < 0.999) {
        float etaL = ha + ba, etaR = hb + bb;
        float hup = etaL > etaR ? hl : hr;
        float creep = -G * hup * hup * hup / (3.0 * uViscosity) * (etaR - etaL) / uCell;
        float most = 0.25 * hup * uCell / uDt;
        creep = clamp(creep, -most, most);
        f.h = inertial * f.h + (1.0 - inertial) * creep;
        f.qn = inertial * f.qn + (1.0 - inertial) * 0.25 * G * (hl * hl + hr * hr);
        f.qt *= inertial;
    }
    return f;
}

vec4 cellAt(ivec2 p, ivec2 size, out bool exists) {
    exists = p.x >= 0 && p.y >= 0 && p.x < size.x && p.y < size.y;
    return exists ? texelFetch(uState, p, 0) : vec4(0.0);
}

void main() {
    ivec2 size = textureSize(uState, 0);
    ivec2 p = ivec2(gl_FragCoord.xy);
    vec4 me = texelFetch(uState, p, 0);
    bool eL, eR, eD, eU;
    vec4 L = cellAt(p - ivec2(1, 0), size, eL), R = cellAt(p + ivec2(1, 0), size, eR);
    vec4 D = cellAt(p - ivec2(0, 1), size, eD), U = cellAt(p + ivec2(0, 1), size, eU);
    // x faces carry (h, qx, qy); y faces carry (h, qy, qx), so swap the discharge components for them.
    Flux fl = face(L, me, eL, true);
    Flux fr = face(me, R, true, eR);
    Flux fd = face(D.rbga, me.rbga, eD, true);
    Flux fu = face(me.rbga, U.rbga, true, eU);
    float k = uDt / uCell;
    float h = me.r - k * (fr.h - fl.h + fu.h - fd.h);
    float qx = me.g - k * (fr.qn - fr.sa - (fl.qn - fl.sb) + fu.qt - fd.qt);
    float qy = me.b - k * (fu.qn - fu.sa - (fd.qn - fd.sb) + fr.qt - fl.qt);
    // The spout.
    if (uSpout.x >= 0.0) {
        vec2 d = (vec2(p) + 0.5) - uSpout;
        if (dot(d, d) <= uSpoutRadius * uSpoutRadius) h += uSpoutRate * uDt;
    }
    if (h <= DRY) { h = max(0.0, h); qx = 0.0; qy = 0.0; }
    else {
        // Drag, then velocity from discharge without blowing up on a film microns thick, then the cap that keeps the step honest.
        float drag = 1.0 / (1.0 + 3.0 * uViscosity * uDt / (h * h));
        float h2 = h * h;
        float inv = 2.0 * h / (h2 + max(h2, THIN * THIN));
        vec2 u = vec2(qx, qy) * inv * drag;
        float speed = length(u);
        if (speed > uSpeedCap) u *= uSpeedCap / speed;
        qx = h * u.x;
        qy = h * u.y;
    }
    if (!(h < 10.0 && abs(qx) < 100.0 && abs(qy) < 100.0)) { h = 0.0; qx = 0.0; qy = 0.0; }
    outState = vec4(h, qx, qy, me.a);
}
