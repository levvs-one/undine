#version 300 es
// Undine: one substep of a liquid flowing over a floor. The texture holds thickness h (r, metres), discharge q = h·u
// (g, b, m²/s) and the floor height (a, metres) per cell. Shallow-water equations: the HLL flux with the hydrostatic
// reconstruction at each face (a still puddle stays still on any floor, h stays ≥ 0), second order in space from
// minmod-limited slopes of h, the surface and the velocities (so a sheet spreads the same in every direction), the
// grid's edges are walls, then the spout adds liquid and the bottom's drag 3νu/h² acts implicitly. Where the drag
// relaxes the film within a step the mass flux blends into the lubrication flux −g·h³·∇η/(3ν), Huppert's thin-film
// equation. A wet-dry front holds until what pushes it, ½gh² plus the floor's fall plus the momentum flux h·u² of a
// sheet running at it, beats the contact angle's retention, so a puddle stops at its own thickness and a wetting
// liquid spreads to a film; the held line pulls on the edge with the Young force, so a thinner edge draws back.
// Both cells at a face evaluate the same flux from the same operands, so what leaves one enters the other exactly.
precision highp float;
precision highp int;

uniform sampler2D uState;
uniform float uCell;           // metres
uniform float uDt;             // seconds, within 0,25·cell/(|u| + √(g·h))
uniform float uViscosity;      // kinematic, m²/s
uniform float uSpeedCap;       // m/s: no liquid moves faster, which keeps the step within its bound
uniform float uRetention;      // σ(1 − cos θ)/ρ, m³/s²: what the contact line holds a wet-dry front with
uniform vec2 uSpout;           // where the spout lands, cells; negative when off
uniform float uSpoutRadius;    // cells
uniform float uSpoutRate;      // metres of thickness per second over the spout's disc
out vec4 outState;

const float G = 9.80665;
const float DRY = 1e-7;
const float THIN = 1e-5;

struct Flux { float h; float qn; float qt; float sa; float sb; };

float minmod(float a, float b) { return a * b <= 0.0 ? 0.0 : (abs(a) < abs(b) ? a : b); }

// Velocity from discharge without blowing up on a film a few microns thick (Kurganov and Petrova).
vec2 velocity(vec4 c) {
    float t = c.r, t2 = t * t;
    return t > DRY ? c.gb * 2.0 * t / (t2 + max(t2, THIN * THIN)) : vec2(0.0);
}

// A cell's state (h, qn, qt, floor) at one of its faces, side +1 towards next and −1 towards prev, from
// minmod-limited slopes of h, the surface and the velocities; pass the cell itself for a neighbour beyond the grid.
// Beside a dry cell the slopes are dropped, as wet-dry schemes do: a slope towards a dry cell would halve the edge's
// thickness at the face.
vec4 edge(vec4 prev, vec4 c, vec4 next, float side) {
    if (c.r <= DRY || prev.r <= DRY || next.r <= DRY) return vec4(c.r, c.r * velocity(c), c.a);
    float eta = c.r + c.a;
    float sh = minmod(c.r - prev.r, next.r - c.r);
    float se = minmod(eta - prev.r - prev.a, next.r + next.a - eta);
    vec2 u = velocity(c);
    vec2 su = vec2(minmod(u.x - velocity(prev).x, velocity(next).x - u.x), minmod(u.y - velocity(prev).y, velocity(next).y - u.y));
    float he = c.r + 0.5 * side * sh;
    float be = eta + 0.5 * side * se - he;
    vec2 ue = he > DRY ? u + 0.5 * side * su : vec2(0.0);
    return vec4(he, he * ue, be);
}

// The flux from cell a into cell b across their face; qn along the face normal, qt across it. a is always the
// lower-index cell so both sides compute it identically. A, B are the sides' states at the face; etaA, etaB the
// cells' own surface heights, for the creeping flux. The source terms sa, sb are the pressure each side's floor step
// takes off its cell, ½g(h*² − h²) with h the side's own edge value.
float hashCell(ivec2 c) {
    uint x = uint(c.x + 7919 * c.y) * 2654435761u;
    x ^= x >> 13;
    x *= 0x5bd1e995u;
    x ^= x >> 15;
    return float(x & 0xFFFFFFu) / 16777216.0;
}

// The wet share of a disc of radius 3.5 cells centred on the face between ca and cb, weighted towards its centre
// (cells beyond the floor count as dry): half at a straight stretch of the front, whatever its direction, less at a
// bulge, more in a notch.
float wetShare(ivec2 ca, ivec2 cb) {
    ivec2 size = textureSize(uState, 0);
    vec2 c = 0.5 * (vec2(ca) + vec2(cb));
    float wet = 0.0, total = 0.0;
    for (int dy = -4; dy <= 4; dy++) {
        for (int dx = -4; dx <= 4; dx++) {
            ivec2 q = ivec2(floor(c)) + ivec2(dx, dy);
            vec2 d = (vec2(q) - c) / 3.5;
            float r2 = dot(d, d);
            if (r2 > 1.0) continue;
            float w = 1.0 - r2;
            total += w;
            if (q.x >= 0 && q.y >= 0 && q.x < size.x && q.y < size.y && texelFetch(uState, q, 0).r > DRY) wet += w;
        }
    }
    return wet / total;
}

Flux face(vec4 A, vec4 B, float etaA, float etaB, bool an, bool bn, ivec2 ca, ivec2 cb) {
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
    // The contact line.
    if ((hr <= DRY) != (hl <= DRY)) {
        bool leftWet = hl > DRY;
        float thickness = leftWet ? hl : hr;
        float fall = leftWet ? max(0.0, ba - bb) : max(0.0, bb - ba);
        // What pushes the line: the edge's weight, the floor's fall, and the momentum flux h·u² of a sheet running at
        // it; the sheet's whole speed counts once it heads within 30° of the face's normal, so a front running
        // diagonally is pushed as hard as one along an axis.
        float un = leftWet ? ul : -ur, ut = leftWet ? vl : vr;
        float speed2 = un * un + ut * ut;
        float heading = speed2 > 0.0 ? clamp(2.0 * un * inversesqrt(speed2), 0.0, 1.0) : 0.0;
        float head = 0.5 * G * thickness * thickness + G * thickness * fall + thickness * speed2 * heading;
        // The line's own curvature: a disc across the face is half wet at a straight stretch, less at a bulge, more
        // in a notch; the line holds a bulge harder and gives way in a notch, which draws the shape into a round puddle.
        float curvature = clamp(1.0 + 8.0 * (0.5 - wetShare(ca, cb)), 0.2, 4.0);
        // Contact-angle hysteresis: the table holds the line unevenly from spot to spot, ±20% in a fixed pattern.
        float grain = 0.8 + 0.4 * hashCell(leftWet ? cb : ca);
        float retention = uRetention * curvature * grain;
        // The held line is not a wall: it pulls on the edge with the Young force, the retention, as the face's
        // momentum flux; an edge thinner than the puddle thickness is drawn back by the unbalanced part. A cell on a
        // diagonal stretch has two such faces and holds √2 of front, so it is not scaled (measured: near round).
        if (head < retention) { f.h = 0.0; f.qt = 0.0; f.qn = retention; return f; }
    }
    // The creeping regime: the film relaxes within the step, so the mass flux is the thin-film one.
    float hf = max(hl, hr);
    float relax = hf * hf / (3.0 * uViscosity);
    float inertial = relax / (relax + uDt);
    if (inertial < 0.999) {
        float etaL = etaA, etaR = etaB;
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
    bool eLL, eRR, eDD, eUU;
    vec4 LL = cellAt(p - ivec2(2, 0), size, eLL), RR = cellAt(p + ivec2(2, 0), size, eRR);
    vec4 DD = cellAt(p - ivec2(0, 2), size, eDD), UU = cellAt(p + ivec2(0, 2), size, eUU);
    // Each side's state at each of the four faces from its own cell's slopes (a missing neighbour is the cell itself).
    vec4 meL = edge(eL ? L : me, me, eR ? R : me, -1.0), meR = edge(eL ? L : me, me, eR ? R : me, 1.0);
    vec4 meD = edge(eD ? D.rbga : me.rbga, me.rbga, eU ? U.rbga : me.rbga, -1.0), meU = edge(eD ? D.rbga : me.rbga, me.rbga, eU ? U.rbga : me.rbga, 1.0);
    vec4 Lr = edge(eLL ? LL : L, L, me, 1.0), Rl = edge(me, R, eRR ? RR : R, -1.0);
    vec4 Du = edge(eDD ? DD.rbga : D.rbga, D.rbga, me.rbga, 1.0), Ud = edge(me.rbga, U.rbga, eUU ? UU.rbga : U.rbga, -1.0);
    float eta = me.r + me.a;
    // x faces carry (h, qx, qy); y faces carry (h, qy, qx), so swap the discharge components for them.
    Flux fl = face(Lr, meL, L.r + L.a, eta, eL, true, p - ivec2(1, 0), p);
    Flux fr = face(meR, Rl, eta, R.r + R.a, true, eR, p, p + ivec2(1, 0));
    Flux fd = face(Du, meD, D.r + D.a, eta, eD, true, p - ivec2(0, 1), p);
    Flux fu = face(meU, Ud, eta, U.r + U.a, true, eU, p, p + ivec2(0, 1));
    float k = uDt / uCell;
    float h = me.r - k * (fr.h - fl.h + fu.h - fd.h);
    float qx = me.g - k * (fr.qn - fr.sa - (fl.qn - fl.sb) + fu.qt - fd.qt);
    float qy = me.b - k * (fu.qn - fu.sa - (fd.qn - fd.sb) + fr.qt - fl.qt);
    // The cell's own floor slope between its two edges, −g·h̄·(b⁺ − b⁻) (Audusse's centred source): at second order
    // this is what keeps a level surface still on a sloping floor.
    qx -= k * G * 0.5 * (meL.r + meR.r) * (meR.a - meL.a);
    qy -= k * G * 0.5 * (meD.r + meU.r) * (meU.a - meD.a);
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
