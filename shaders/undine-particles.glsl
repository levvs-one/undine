// Undine: a liquid as particles on the GPU, the same scheme as Undine.ParticleFluid on the CPU: position-based
// fluids with the liquid's density, viscosity and surface tension. Particles live in textures; every substep sorts
// them by grid cell (bitonic), builds the cell table, applies the forces, projects the density constraint and takes
// the velocity from the move. Sections are separated by lines starting with //@ and named; the host splits them.
// All sections share the prelude below.

//@ prelude
#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;

uniform sampler2D uPos;        // xyz position, w = 1 alive / 0 free slot
uniform sampler2D uVel;        // xyz velocity
uniform sampler2D uPred;       // xyz predicted position
uniform sampler2D uLambda;     // r λ, g density
uniform sampler2D uKeys;       // r cell key, g particle index (sorted)
uniform sampler2D uCellStart;  // first sorted index in the cell
uniform sampler2D uCellEnd;    // one past the last
uniform int uSide;             // particle texture side
uniform vec3 uBox;             // the tank, metres from the origin corner
uniform float uSpacing;        // rest spacing d; the kernel radius is 2d
uniform float uMass;
uniform float uRestDensity;    // the rest lattice's density
uniform float uLatticeGradient;
uniform float uGamma;          // cohesion strength
uniform float uViscosity;      // kinematic
uniform float uDt;
uniform vec3 uGravity;
uniform ivec3 uCells;          // grid cells per axis
uniform int uCellSide;         // cell table texture side
uniform float uSmoothing;
uniform float uRelaxation;
uniform float uSpeedCap;       // m/s: no particle moves faster; a safety, not physics
uniform sampler2D uWallDensity; // 1D table of the density a wall hides, over distance 0..h

const int MAX_IN_CELL = 24;
const float DEAD = 1e30;

ivec2 texel(int i) { return ivec2(i % uSide, i / uSide); }
int radius2cells() { return 1; }
float h() { return 2.0 * uSpacing; }

float poly6(float r2) {
    float hh = h() * h();
    if (r2 >= hh) return 0.0;
    float d = hh - r2;
    return 315.0 / (64.0 * 3.14159265 * pow(h(), 9.0)) * d * d * d;
}

vec3 spikyGrad(vec3 r) {
    float len = length(r);
    if (len <= 1e-7 || len >= h()) return vec3(0.0);
    float d = h() - len;
    return r * (-45.0 / (3.14159265 * pow(h(), 6.0)) * d * d / len);
}

float cohesionKernel(float r) {
    float hh = h();
    if (r >= hh || r <= 0.0) return 0.0;
    float scale = 32.0 / (3.14159265 * pow(hh, 9.0));
    float a = (hh - r) * (hh - r) * (hh - r) * r * r * r;
    return r > hh / 2.0 ? scale * a : scale * (2.0 * a - pow(hh, 6.0) / 64.0);
}

ivec3 cellOf(vec3 p) {
    return clamp(ivec3(floor(p / h())), ivec3(0), uCells - 1);
}

int keyOf(ivec3 c) {
    return c.x + uCells.x * (c.y + uCells.y * c.z);
}

ivec2 cellTexel(int key) { return ivec2(key % uCellSide, key / uCellSide); }

vec3 confine(vec3 p) {
    float margin = 0.5 * uSpacing;
    return clamp(p, vec3(margin), uBox - margin);
}

// The density the six walls hide, from the table, shares combined by product.
float wallDensity(vec3 p) {
    float margin = 0.5 * uSpacing;
    float remaining = 1.0;
    float ds[6];
    ds[0] = p.x - margin; ds[1] = uBox.x - margin - p.x;
    ds[2] = p.y - margin; ds[3] = uBox.y - margin - p.y;
    ds[4] = p.z - margin; ds[5] = uBox.z - margin - p.z;
    for (int k = 0; k < 6; k++) {
        float d = ds[k];
        if (d < h()) {
            float t = clamp(d, 0.0, h()) / h();
            float hidden = texture(uWallDensity, vec2(t, 0.5)).r;
            remaining *= 1.0 - hidden / uRestDensity;
        }
    }
    return uRestDensity * (1.0 - remaining);
}

//@ forces
// Gravity, viscosity and cohesion on the current neighbourhoods, then the predicted position: two outputs.
layout(location = 0) out vec4 outVel;
layout(location = 1) out vec4 outPred;
void main() {
    ivec2 t = ivec2(gl_FragCoord.xy);
    int i = t.y * uSide + t.x;
    vec4 P = texelFetch(uPos, t, 0);
    vec3 v = texelFetch(uVel, t, 0).xyz;
    if (P.w < 0.5) { outVel = vec4(0.0); outPred = P; return; }
    vec3 p = P.xyz;
    float densI = texelFetch(uLambda, t, 0).g;
    if (densI <= 0.0) densI = uRestDensity;
    vec3 viscous = vec3(0.0), cohesion = vec3(0.0);
    ivec3 c = cellOf(p);
    float hh = h();
    for (int dz = -1; dz <= 1; dz++) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) {
        ivec3 cc = c + ivec3(dx, dy, dz);
        if (any(lessThan(cc, ivec3(0))) || any(greaterThanEqual(cc, uCells))) continue;
        ivec2 ct = cellTexel(keyOf(cc));
        int start = int(texelFetch(uCellStart, ct, 0).r), end = int(texelFetch(uCellEnd, ct, 0).r);
        for (int j = start; j < end && j < start + MAX_IN_CELL; j++) {
            if (j == i) continue;
            ivec2 tj = texel(j);
            vec3 pj = texelFetch(uPos, tj, 0).xyz;
            vec3 r = p - pj;
            float len = length(r);
            if (len <= 1e-7 || len >= hh) continue;
            vec3 vj = texelFetch(uVel, tj, 0).xyz;
            float densJ = texelFetch(uLambda, tj, 0).g;
            if (densJ <= 0.0) densJ = uRestDensity;
            vec3 vij = v - vj;
            vec3 grad = spikyGrad(r);
            viscous += grad * (10.0 * uViscosity * uMass / densJ * dot(vij, r) / (len * len + 0.01 * hh * hh));
            float kij = 2.0 * uRestDensity / (densI + densJ);
            cohesion += -r / len * (uGamma * uMass * cohesionKernel(len) * kij);
        }
    }
    v += (uGravity + viscous + cohesion) * uDt;
    outVel = vec4(v, texelFetch(uVel, t, 0).w);
    outPred = vec4(confine(p + v * uDt), 1.0);
}

//@ keys
// The sort key of every slot: its predicted cell, or DEAD for a free slot.
out vec4 outKey;
void main() {
    ivec2 t = ivec2(gl_FragCoord.xy);
    int i = t.y * uSide + t.x;
    vec4 P = texelFetch(uPred, t, 0);
    float key = P.w < 0.5 ? DEAD : float(keyOf(cellOf(P.xyz)));
    outKey = vec4(key, float(i), 0.0, 0.0);
}

//@ sort
// One compare-and-swap pass of a bitonic sort over the key texture (Batcher): uStage is the block size, uStep the
// distance between partners; log2(N)·(log2(N)+1)/2 passes sort everything ascending.
uniform int uStage;
uniform int uStep;
out vec4 outKey;
void main() {
    ivec2 t = ivec2(gl_FragCoord.xy);
    int i = t.y * uSide + t.x;
    int partner = i ^ uStep;
    vec4 a = texelFetch(uKeys, t, 0);
    vec4 b = texelFetch(uKeys, texel(partner), 0);
    bool ascending = (i & uStage) == 0;
    bool lower = i < partner;
    bool keep = (a.r < b.r) == (lower == ascending) || a.r == b.r;
    if (a.r == b.r) keep = (a.g < b.g) == (lower == ascending) || a.g == b.g;
    outKey = keep ? a : b;
}

//@ reorder
// The particle arrays in sorted order: three outputs gathered through the sorted keys.
layout(location = 0) out vec4 outPos;
layout(location = 1) out vec4 outVel;
layout(location = 2) out vec4 outPred;
void main() {
    ivec2 t = ivec2(gl_FragCoord.xy);
    int from = int(texelFetch(uKeys, t, 0).g);
    ivec2 tf = texel(from);
    outPos = texelFetch(uPos, tf, 0);
    outVel = texelFetch(uVel, tf, 0);
    outPred = texelFetch(uPred, tf, 0);
}

//@ cellsVertex
// Points scattered into the cell table: a slot whose key differs from the one before it starts its cell; uWhich = 0
// writes starts, 1 writes ends (one past the last).
uniform int uWhich;
flat out float vValue;
void main() {
    int i = gl_VertexID;
    vec4 k = texelFetch(uKeys, texel(i), 0);
    float key = k.r;
    gl_PointSize = 1.0;
    if (key >= DEAD) { gl_Position = vec4(4.0, 4.0, 0.0, 1.0); vValue = 0.0; return; }
    float other = uWhich == 0
        ? (i > 0 ? texelFetch(uKeys, texel(i - 1), 0).r : -1.0)
        : (i + 1 < uSide * uSide ? texelFetch(uKeys, texel(i + 1), 0).r : DEAD);
    if (other == key) { gl_Position = vec4(4.0, 4.0, 0.0, 1.0); vValue = 0.0; return; }
    ivec2 ct = cellTexel(int(key));
    vec2 clip = (vec2(ct) + 0.5) / float(uCellSide) * 2.0 - 1.0;
    gl_Position = vec4(clip, 0.0, 1.0);
    vValue = uWhich == 0 ? float(i) : float(i + 1);
}

//@ cellsFragment
flat in float vValue;
out vec4 outValue;
void main() { outValue = vec4(vValue, 0.0, 0.0, 1.0); }

//@ lambda
// The density constraint's multiplier for every particle on the predicted positions, with the density it saw.
out vec4 outLambda;
void main() {
    ivec2 t = ivec2(gl_FragCoord.xy);
    int i = t.y * uSide + t.x;
    vec4 P = texelFetch(uPred, t, 0);
    if (P.w < 0.5) { outLambda = vec4(0.0, uRestDensity, 0.0, 0.0); return; }
    vec3 p = P.xyz;
    float density = uMass * poly6(0.0) + wallDensity(p);
    vec3 gradI = vec3(0.0);
    float sumGrad = 0.0;
    float scale = uMass / uRestDensity;
    ivec3 c = cellOf(p);
    for (int dz = -1; dz <= 1; dz++) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) {
        ivec3 cc = c + ivec3(dx, dy, dz);
        if (any(lessThan(cc, ivec3(0))) || any(greaterThanEqual(cc, uCells))) continue;
        ivec2 ct = cellTexel(keyOf(cc));
        int start = int(texelFetch(uCellStart, ct, 0).r), end = int(texelFetch(uCellEnd, ct, 0).r);
        for (int j = start; j < end && j < start + MAX_IN_CELL; j++) {
            if (j == i) continue;
            vec3 r = p - texelFetch(uPred, texel(j), 0).xyz;
            float r2 = dot(r, r);
            if (r2 >= h() * h()) continue;
            density += uMass * poly6(r2);
            vec3 g = spikyGrad(r) * scale;
            gradI += g;
            sumGrad += dot(g, g);
        }
    }
    sumGrad += dot(gradI, gradI);
    float constraint = density / uRestDensity - 1.0;
    float inverse = 1.0 / (sumGrad + uLatticeGradient);
    outLambda = vec4(-constraint * inverse, density, inverse, 0.0);
}

//@ delta
// The position corrections applied, with the artificial pressure against clumping, under-relaxed.
out vec4 outPred;
void main() {
    ivec2 t = ivec2(gl_FragCoord.xy);
    int i = t.y * uSide + t.x;
    vec4 P = texelFetch(uPred, t, 0);
    if (P.w < 0.5) { outPred = P; return; }
    vec3 p = P.xyz;
    vec4 L = texelFetch(uLambda, t, 0);
    float corrDenominator = poly6(0.04 * h() * h());
    vec3 delta = vec3(0.0);
    ivec3 c = cellOf(p);
    for (int dz = -1; dz <= 1; dz++) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) {
        ivec3 cc = c + ivec3(dx, dy, dz);
        if (any(lessThan(cc, ivec3(0))) || any(greaterThanEqual(cc, uCells))) continue;
        ivec2 ct = cellTexel(keyOf(cc));
        int start = int(texelFetch(uCellStart, ct, 0).r), end = int(texelFetch(uCellEnd, ct, 0).r);
        for (int j = start; j < end && j < start + MAX_IN_CELL; j++) {
            if (j == i) continue;
            ivec2 tj = texel(j);
            vec3 r = p - texelFetch(uPred, tj, 0).xyz;
            float r2 = dot(r, r);
            if (r2 >= h() * h()) continue;
            float lambdaJ = texelFetch(uLambda, tj, 0).r;
            float ratio = poly6(r2) / corrDenominator;
            float sCorr = -0.1 * ratio * ratio * ratio * ratio * L.b;
            delta += spikyGrad(r) * (L.r + lambdaJ + sCorr);
        }
    }
    outPred = vec4(confine(p + delta * (uMass / uRestDensity * uRelaxation)), 1.0);
}

//@ finish
// The velocity from the move, then XSPH from the neighbours' moves; the position becomes the predicted one. The
// velocity's w carries the air the particle has trapped: Ihmsen, Akinci, Akinci and Teschner's trapped-air
// potential (2012), Σ|v_ij|(1 − v̂_ij·r̂_ij)(1 − r/h) over the neighbours, the relative motion of liquid closing on
// the particle, which is where a splash folds air in and turns white; it is gained at that rate while the particle
// itself moves, and fades with a lifetime of half a second. Where the white is comes from the flow; how much of it
// is a chosen scale, since the air itself is not simulated.
layout(location = 0) out vec4 outPos;
layout(location = 1) out vec4 outVel;
void main() {
    ivec2 t = ivec2(gl_FragCoord.xy);
    int i = t.y * uSide + t.x;
    vec4 P = texelFetch(uPred, t, 0);
    vec3 pos = texelFetch(uPos, t, 0).xyz;
    if (P.w < 0.5) { outPos = P; outVel = vec4(0.0); return; }
    vec3 p = P.xyz;
    vec3 v = (p - pos) / uDt;
    vec3 blend = vec3(0.0);
    float trapped = 0.0;
    ivec3 c = cellOf(p);
    for (int dz = -1; dz <= 1; dz++) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) {
        ivec3 cc = c + ivec3(dx, dy, dz);
        if (any(lessThan(cc, ivec3(0))) || any(greaterThanEqual(cc, uCells))) continue;
        ivec2 ct = cellTexel(keyOf(cc));
        int start = int(texelFetch(uCellStart, ct, 0).r), end = int(texelFetch(uCellEnd, ct, 0).r);
        for (int j = start; j < end && j < start + MAX_IN_CELL; j++) {
            if (j == i) continue;
            ivec2 tj = texel(j);
            vec4 Pj = texelFetch(uPred, tj, 0);
            vec3 r = p - Pj.xyz;
            float r2 = dot(r, r);
            if (r2 >= h() * h()) continue;
            vec3 vj = (Pj.xyz - texelFetch(uPos, tj, 0).xyz) / uDt;
            float densJ = texelFetch(uLambda, tj, 0).g;
            if (densJ <= 0.0) densJ = uRestDensity;
            blend += (vj - v) * (uMass / densJ * poly6(r2));
            vec3 vij = v - vj;
            float closing = length(vij), dist = sqrt(r2);
            if (closing > 1e-6 && dist > 1e-6) trapped += closing * (1.0 - dot(vij / closing, r / dist)) * (1.0 - dist / h());
        }
    }
    v += blend * uSmoothing;
    float speed = length(v);
    if (speed > uSpeedCap) v *= uSpeedCap / speed;
    // Measured on the tank: a liquid at rest sums under 0.3 m/s of closing for 99 in 100 particles (0.85 at most),
    // a dropped blob's impact 1.4 at the 90th percentile and 4.6 at most; below 1 nothing is trapped, 4 is a
    // splash's full share.
    float foam = texelFetch(uVel, t, 0).w * exp(-uDt / 0.5) + uDt * 3.0 * clamp((trapped - 1.0) / 3.0, 0.0, 1.0) * min(1.0, speed / 0.5);
    outPos = vec4(p, 1.0);
    outVel = vec4(v, min(foam, 2.0));
}
