// Undine's particle liquid on the GPU, driven from .NET: the page sends a spec (the liquid's numbers, the tank, the
// spacing), the simulation and the picture run here every frame. The shaders are loaded from /shaders so the files
// the site hands out are the files it runs. Press on the tank to drop a blob there, hold to pour; drag with the
// right button or two fingers to look around; Ctrl + wheel, a pinch or the buttons to come closer.
window.undineParticles = (() => {
    const VERTEX = `#version 300 es
void main() {
    vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}`;
    const G = 9.80665;
    const TAN_HALF = Math.tan(36 * Math.PI / 360);
    const views = new Map();
    let sources = null;

    // The shader files hold named sections after a shared prelude.
    function split(text) {
        const parts = {};
        let name = null;
        for (const line of text.split("\n")) {
            const m = line.match(/^\/\/@\s+(\w+)/);
            if (m) { name = m[1]; parts[name] = ""; continue; }
            if (name) parts[name] += line + "\n";
        }
        return parts;
    }

    async function loadSources() {
        if (sources) return sources;
        const [sim, render] = await Promise.all([
            fetch("shaders/undine-particles.glsl").then(r => r.text()),
            fetch("shaders/undine-particles-render.glsl").then(r => r.text()),
        ]);
        sources = { sim: split(sim), render: split(render) };
        return sources;
    }

    function compile(gl, type, source) {
        const shader = gl.createShader(type);
        gl.shaderSource(shader, source);
        gl.compileShader(shader);
        if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(shader) + "\n" + source.split("\n").slice(0, 3).join("\n"));
        return shader;
    }

    function link(gl, vertex, fragment) {
        const program = gl.createProgram();
        gl.attachShader(program, compile(gl, gl.VERTEX_SHADER, vertex));
        gl.attachShader(program, compile(gl, gl.FRAGMENT_SHADER, fragment));
        gl.linkProgram(program);
        if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program));
        const u = {};
        const count = gl.getProgramParameter(program, gl.ACTIVE_UNIFORMS);
        for (let i = 0; i < count; i++) {
            const info = gl.getActiveUniform(program, i);
            u[info.name.replace(/\[0\]$/, "")] = gl.getUniformLocation(program, info.name);
        }
        return { program, u };
    }

    function makeTexture(gl, width, height, internal, format, type, data) {
        const texture = gl.createTexture();
        gl.bindTexture(gl.TEXTURE_2D, texture);
        gl.texImage2D(gl.TEXTURE_2D, 0, internal, width, height, 0, format, type, data || null);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
        return texture;
    }

    function makeTarget(gl, width, height, internal, format, type, count, depth) {
        const textures = [];
        const fbo = gl.createFramebuffer();
        gl.bindFramebuffer(gl.FRAMEBUFFER, fbo);
        const buffers = [];
        for (let k = 0; k < (count || 1); k++) {
            const texture = makeTexture(gl, width, height, internal, format, type);
            gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0 + k, gl.TEXTURE_2D, texture, 0);
            textures.push(texture);
            buffers.push(gl.COLOR_ATTACHMENT0 + k);
        }
        gl.drawBuffers(buffers);
        let renderbuffer = null;
        if (depth) {
            renderbuffer = gl.createRenderbuffer();
            gl.bindRenderbuffer(gl.RENDERBUFFER, renderbuffer);
            gl.renderbufferStorage(gl.RENDERBUFFER, gl.DEPTH_COMPONENT24, width, height);
            gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.RENDERBUFFER, renderbuffer);
        }
        if (gl.checkFramebufferStatus(gl.FRAMEBUFFER) !== gl.FRAMEBUFFER_COMPLETE) throw new Error("framebuffer incomplete " + internal);
        gl.clearColor(0, 0, 0, 0);
        gl.clear(gl.COLOR_BUFFER_BIT | (depth ? gl.DEPTH_BUFFER_BIT : 0));
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        return { fbo, textures, texture: textures[0], width, height, renderbuffer };
    }

    // The kernels, as in the package, for the rest lattice's density and gradient scale, the cohesion's strength
    // from σ, and the density each wall hides.
    function constants(spec) {
        const d = spec.spacing, h = 2 * d, mass = spec.density * d * d * d;
        const poly6 = r2 => r2 >= h * h ? 0 : 315 / (64 * Math.PI * h ** 9) * (h * h - r2) ** 3;
        const spikyMag = r => r <= 1e-7 || r >= h ? 0 : 45 / (Math.PI * h ** 6) * (h - r) ** 2;
        const reach = Math.ceil(h / d);
        let density = 0, gradient = 0;
        for (let z = -reach; z <= reach; z++) for (let y = -reach; y <= reach; y++) for (let x = -reach; x <= reach; x++) {
            const r2 = (x * x + y * y + z * z) * d * d;
            density += poly6(r2);
            if (x || y || z) {
                const g = spikyMag(Math.sqrt(r2)) * mass;
                gradient += g * g;
            }
        }
        density *= mass;
        gradient /= density * density;
        const cohesionKernel = r => {
            if (r >= h || r <= 0) return 0;
            const scale = 32 / (Math.PI * h ** 9);
            const a = (h - r) ** 3 * r ** 3;
            return r > h / 2 ? scale * a : scale * (2 * a - h ** 6 / 64);
        };
        let integral = 0;
        const samples = 4000;
        for (let k = 0; k < samples; k++) {
            const r = (k + 0.5) * h / samples;
            integral += cohesionKernel(r) * r ** 4 * h / samples;
        }
        const gamma = spec.surfaceTension / (Math.PI / 8 * spec.density * spec.density * integral);
        const wall = new Float32Array(64);
        for (let k = 0; k < wall.length; k++) {
            const distance = k / (wall.length - 1) * h;
            let sum = 0;
            for (let plane = 0; plane < reach + 1; plane++) {
                const depth = distance + (plane + 1) * d;
                for (let z = -reach - 1; z <= reach + 1; z++) for (let x = -reach - 1; x <= reach + 1; x++) sum += poly6(x * x * d * d + depth * depth + z * z * d * d);
            }
            wall[k] = mass * sum;
        }
        const capillary = 0.3 * Math.sqrt(spec.density * d ** 3 / (2 * Math.PI * Math.max(1e-6, spec.surfaceTension)));
        const viscous = 0.1 * d * d / Math.max(1e-12, spec.kinematicViscosity);
        return { mass, density, gradient, gamma, wall, h, step: Math.min(0.2 * d, capillary, viscous) };
    }

    async function setup(canvas) {
        const gl = canvas.getContext("webgl2", { antialias: false, alpha: false, preserveDrawingBuffer: true });
        if (!gl || !gl.getExtension("EXT_color_buffer_float")) return null;
        const src = await loadSources();
        const P = src.sim.prelude, R = src.render.prelude;
        const view = {
            canvas, gl,
            forces: link(gl, VERTEX, P + src.sim.forces),
            keys: link(gl, VERTEX, P + src.sim.keys),
            sort: link(gl, VERTEX, P + src.sim.sort),
            reorder: link(gl, VERTEX, P + src.sim.reorder),
            cells: link(gl, P + src.sim.cellsVertex, P + src.sim.cellsFragment),
            lambda: link(gl, VERTEX, P + src.sim.lambda),
            delta: link(gl, VERTEX, P + src.sim.delta),
            finish: link(gl, VERTEX, P + src.sim.finish),
            background: link(gl, VERTEX, R + src.render.background),
            sphereDepth: link(gl, R + src.render.sphereVertex, R + src.render.sphereDepth),
            sphereThickness: link(gl, R + src.render.sphereVertex, R + src.render.sphereThickness),
            smooth: link(gl, VERTEX, R + src.render.smooth),
            compose: link(gl, VERTEX, R + src.render.compose),
            spec: null, key: "", side: 0, alive: 0, k: null,
            yaw: 0.6, pitch: 0.35, distance: 0.75,
            pointers: new Map(), pinch: 0, dragging: false, lastX: 0, lastY: 0, pouring: null, pourClock: 0,
            lastTime: 0, frame: 0, fallback: 0, frames: 0, fpsTime: 0, fps: 0,
        };
        attach(view);
        return view;
    }

    function pass(gl, program, target, width, height, setUniforms, textures) {
        gl.useProgram(program.program);
        gl.bindFramebuffer(gl.FRAMEBUFFER, target ? target.fbo : null);
        gl.viewport(0, 0, width, height);
        let unit = 0;
        for (const [name, texture] of Object.entries(textures || {})) {
            gl.activeTexture(gl.TEXTURE0 + unit);
            gl.bindTexture(gl.TEXTURE_2D, texture);
            gl.uniform1i(program.u[name], unit);
            unit++;
        }
        setUniforms(program.u);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
    }

    // Everything the grid needs, rebuilt when the tank, the spacing or the particle budget changes; that empties it.
    function surfaces(view) {
        const { gl, spec } = view;
        const key = [spec.side, spec.spacing, spec.box.join(","), spec.density, spec.surfaceTension].join("/");
        if (view.states && key === view.key) return;
        view.key = key;
        view.side = spec.side;
        const n = spec.side;
        const k = constants(spec);
        view.k = k;
        const cells = spec.box.map(b => Math.max(1, Math.ceil(b / k.h)));
        view.gridCells = cells;
        view.cellSide = Math.ceil(Math.sqrt(cells[0] * cells[1] * cells[2]));
        const f = count => makeTarget(gl, n, n, gl.RGBA32F, gl.RGBA, gl.FLOAT, count || 1);
        // Two (position, velocity) states, one canonical, the other written by the substep, then swapped.
        view.states = [f(2), f(2)];
        view.current = 0;
        view.lambdaTex = f();
        view.velPred = f(2);
        view.sorted = f(3);
        view.predTargets = [f(), f()];
        view.keysA = f(); view.keysB = f();
        view.cellStart = makeTarget(gl, view.cellSide, view.cellSide, gl.R32F, gl.RED, gl.FLOAT);
        view.cellEnd = makeTarget(gl, view.cellSide, view.cellSide, gl.R32F, gl.RED, gl.FLOAT);
        view.wallTex = makeTexture(gl, k.wall.length, 1, gl.R32F, gl.RED, gl.FLOAT, k.wall);
        gl.bindTexture(gl.TEXTURE_2D, view.wallTex);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
        // Every slot free: parked out of the way with w = 0.
        const parked = new Float32Array(n * n * 4);
        for (let i = 0; i < n * n; i++) { parked[i * 4] = -1; parked[i * 4 + 1] = -1; parked[i * 4 + 2] = -1; }
        for (const state of view.states) {
            gl.bindTexture(gl.TEXTURE_2D, state.textures[0]);
            gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, n, n, gl.RGBA, gl.FLOAT, parked);
        }
        view.alive = 0;
        if (spec.fill > 0) fillTank(view, spec.fill);
    }

    // Particles written straight into free slots; after the next sort they take their places.
    function spawn(view, positions, velocity) {
        const { gl, spec } = view;
        const n = view.side, total = n * n;
        const count = Math.min(positions.length, total - view.alive);
        if (count <= 0) return 0;
        // Free slots sit at the end of the sorted order (their key sorts last); before the first sort they are all free.
        const start = view.alive;
        const posData = new Float32Array(count * 4), velData = new Float32Array(count * 4);
        for (let i = 0; i < count; i++) {
            posData[i * 4] = positions[i][0]; posData[i * 4 + 1] = positions[i][1]; posData[i * 4 + 2] = positions[i][2]; posData[i * 4 + 3] = 1;
            velData[i * 4] = velocity[0]; velData[i * 4 + 1] = velocity[1]; velData[i * 4 + 2] = velocity[2];
        }
        // Write row by row (slots are contiguous in memory, rows of n).
        let written = 0;
        while (written < count) {
            const slot = start + written;
            const row = Math.floor(slot / n), col = slot % n;
            const span = Math.min(n - col, count - written);
            const state = view.states[view.current];
            gl.bindTexture(gl.TEXTURE_2D, state.textures[0]);
            gl.texSubImage2D(gl.TEXTURE_2D, 0, col, row, span, 1, gl.RGBA, gl.FLOAT, posData.subarray(written * 4, (written + span) * 4));
            gl.bindTexture(gl.TEXTURE_2D, state.textures[1]);
            gl.texSubImage2D(gl.TEXTURE_2D, 0, col, row, span, 1, gl.RGBA, gl.FLOAT, velData.subarray(written * 4, (written + span) * 4));
            written += span;
        }
        view.alive += count;
        return count;
    }

    function fillTank(view, height) {
        const { spec } = view;
        const d = spec.spacing;
        const positions = [];
        for (let z = d / 2; z < spec.box[2]; z += d) for (let y = d / 2; y < height; y += d) for (let x = d / 2; x < spec.box[0]; x += d) positions.push([x, y, z]);
        spawn(view, positions, [0, 0, 0]);
    }

    function blob(view, centre, radius, velocity) {
        const d = view.spec.spacing;
        const positions = [];
        for (let z = -radius; z <= radius; z += d) for (let y = -radius; y <= radius; y += d) for (let x = -radius; x <= radius; x += d) {
            if (x * x + y * y + z * z <= radius * radius) positions.push([centre[0] + x, centre[1] + y, centre[2] + z]);
        }
        spawn(view, positions, velocity);
    }

    function setCommon(view, u) {
        const { gl, spec, k } = view;
        gl.uniform1i(u.uSide, view.side);
        gl.uniform3f(u.uBox, spec.box[0], spec.box[1], spec.box[2]);
        gl.uniform1f(u.uSpacing, spec.spacing);
        gl.uniform1f(u.uMass, k.mass);
        gl.uniform1f(u.uRestDensity, k.density);
        gl.uniform1f(u.uLatticeGradient, k.gradient);
        gl.uniform1f(u.uGamma, k.gamma);
        gl.uniform1f(u.uViscosity, spec.kinematicViscosity);
        gl.uniform3f(u.uGravity, 0, -G, 0);
        gl.uniform3i(u.uCells, view.gridCells[0], view.gridCells[1], view.gridCells[2]);
        gl.uniform1i(u.uCellSide, view.cellSide);
        gl.uniform1f(u.uSmoothing, 0.1);
        gl.uniform1f(u.uRelaxation, 0.5);
    }

    function substep(view, dt) {
        const { gl } = view;
        const n = view.side, total = n * n;
        gl.disable(gl.BLEND);
        gl.disable(gl.DEPTH_TEST);
        const state = view.states[view.current];
        const grid = { uCellStart: view.cellStart.texture, uCellEnd: view.cellEnd.texture, uWallDensity: view.wallTex };
        // Forces and the prediction, on the grid of the previous substep (the order is unchanged since); before the
        // first sort the cell table is empty and the forces are gravity alone.
        pass(gl, view.forces, view.velPred, n, n, u => { setCommon(view, u); gl.uniform1f(u.uDt, dt); },
            { uPos: state.textures[0], uVel: state.textures[1], uLambda: view.lambdaTex.texture, ...grid });
        const newVel = view.velPred.textures[0], newPred = view.velPred.textures[1];
        // Keys, then the bitonic sort.
        pass(gl, view.keys, view.keysA, n, n, u => setCommon(view, u), { uPred: newPred });
        let a = view.keysA, b = view.keysB;
        for (let stage = 2; stage <= total; stage <<= 1) {
            for (let step = stage >> 1; step > 0; step >>= 1) {
                pass(gl, view.sort, b, n, n, u => { setCommon(view, u); gl.uniform1i(u.uStage, stage); gl.uniform1i(u.uStep, step); }, { uKeys: a.texture });
                [a, b] = [b, a];
            }
        }
        view.keysA = a; view.keysB = b;
        const sortedKeys = a.texture;
        // The arrays in sorted order, and the cell table.
        pass(gl, view.reorder, view.sorted, n, n, u => setCommon(view, u), { uKeys: sortedKeys, uPos: state.textures[0], uVel: newVel, uPred: newPred });
        for (const [which, target] of [[0, view.cellStart], [1, view.cellEnd]]) {
            gl.bindFramebuffer(gl.FRAMEBUFFER, target.fbo);
            gl.viewport(0, 0, view.cellSide, view.cellSide);
            gl.clearColor(0, 0, 0, 0);
            gl.clear(gl.COLOR_BUFFER_BIT);
            gl.useProgram(view.cells.program);
            gl.activeTexture(gl.TEXTURE0);
            gl.bindTexture(gl.TEXTURE_2D, sortedKeys);
            gl.uniform1i(view.cells.u.uKeys, 0);
            setCommon(view, view.cells.u);
            gl.uniform1i(view.cells.u.uWhich, which);
            gl.drawArrays(gl.POINTS, 0, total);
        }
        const [sortedPos, , sortedPred] = view.sorted.textures;
        // The density constraint: four rounds of multipliers and corrections, the predictions ping-ponging.
        let pred = sortedPred;
        for (let iteration = 0; iteration < 4; iteration++) {
            const target = view.predTargets[iteration % 2];
            pass(gl, view.lambda, view.lambdaTex, n, n, u => setCommon(view, u), { uPred: pred, ...grid });
            pass(gl, view.delta, target, n, n, u => setCommon(view, u), { uPred: pred, uLambda: view.lambdaTex.texture, ...grid });
            pred = target.texture;
        }
        // The velocity from the move, smoothed; the position becomes the predicted one; the other state takes over.
        const next = view.states[1 - view.current];
        pass(gl, view.finish, next, n, n, u => { setCommon(view, u); gl.uniform1f(u.uDt, dt); }, { uPred: pred, uPos: sortedPos, uLambda: view.lambdaTex.texture, ...grid });
        view.current = 1 - view.current;
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    }

    function step(view, seconds) {
        const { spec, k } = view;
        const dt = k.step;
        const substeps = Math.min(40, Math.max(1, Math.ceil(seconds / dt)));
        const sub = seconds / substeps;
        for (let i = 0; i < substeps; i++) {
            pourIfDue(view, sub);
            substep(view, sub);
        }
    }

    // The stream: particles released a few at a time from the spout, falling at the speed of the fall so far.
    function pourIfDue(view, dt) {
        const { spec } = view;
        if (!view.pouring) return;
        view.pourClock += dt;
        const perParticle = 1 / spec.pourRate;
        const d = spec.spacing;
        while (view.pourClock >= perParticle) {
            view.pourClock -= perParticle;
            const jitter = () => (Math.random() - 0.5) * d;
            spawn(view, [[view.pouring[0] + jitter(), spec.box[1] - d, view.pouring[1] + jitter()]], [0, -spec.pourSpeed, 0]);
        }
    }

    function camera(view) {
        const centre = [view.spec.box[0] / 2, view.spec.box[1] * 0.3, view.spec.box[2] / 2];
        const cp = Math.cos(view.pitch), sp = Math.sin(view.pitch), cy = Math.cos(view.yaw), sy = Math.sin(view.yaw);
        const eye = [centre[0] + view.distance * sy * cp, centre[1] + view.distance * sp, centre[2] - view.distance * cy * cp];
        const forward = normalize([centre[0] - eye[0], centre[1] - eye[1], centre[2] - eye[2]]);
        const right = normalize(cross([0, 1, 0], forward));
        const up = cross(forward, right);
        return { eye, forward, right, up };
    }
    function normalize(v) { const l = Math.hypot(v[0], v[1], v[2]) || 1; return [v[0] / l, v[1] / l, v[2] / l]; }
    function cross(a, b) { return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]; }

    function matrices(view, width, height) {
        const { eye, forward, right, up } = camera(view);
        // View matrix (column-major): rows are right, up, -forward.
        const viewM = new Float32Array([
            right[0], up[0], -forward[0], 0,
            right[1], up[1], -forward[1], 0,
            right[2], up[2], -forward[2], 0,
            -(right[0] * eye[0] + right[1] * eye[1] + right[2] * eye[2]), -(up[0] * eye[0] + up[1] * eye[1] + up[2] * eye[2]), forward[0] * eye[0] + forward[1] * eye[1] + forward[2] * eye[2], 1,
        ]);
        const inverse = new Float32Array([
            right[0], right[1], right[2], 0,
            up[0], up[1], up[2], 0,
            -forward[0], -forward[1], -forward[2], 0,
            eye[0], eye[1], eye[2], 1,
        ]);
        const aspect = width / height, near = 0.02, far = 20;
        const f = 1 / TAN_HALF;
        const proj = new Float32Array([
            f / aspect, 0, 0, 0,
            0, f, 0, 0,
            0, 0, (far + near) / (near - far), -1,
            0, 0, 2 * far * near / (near - far), 0,
        ]);
        return { eye, forward, right, up, viewM, inverse, proj };
    }

    function lightDirection(spec) {
        const az = spec.lampAzimuth * Math.PI / 180, el = spec.lampElevation * Math.PI / 180;
        return [-Math.sin(az) * Math.cos(el), -Math.sin(el), -Math.cos(az) * Math.cos(el)];
    }

    function fit(view) {
        const c = view.canvas;
        if (view.forceSize) { [c.width, c.height] = view.forceSize; return; }
        const narrow = Math.min(window.innerWidth, document.documentElement.clientWidth) < 700;
        const dpr = narrow ? 1 : Math.min(window.devicePixelRatio || 1, 1.5);
        const width = Math.max(1, Math.min(1400, Math.round(c.clientWidth * dpr)));
        const height = Math.max(1, Math.round(width * 0.625));
        if (c.width !== width || c.height !== height) { c.width = width; c.height = height; }
    }

    function screenTargets(view, width, height) {
        const { gl } = view;
        if (view.screen && view.screen.width === width && view.screen.height === height) return view.screen;
        const s = {
            width, height,
            background: makeTarget(gl, width, height, gl.RGBA32F, gl.RGBA, gl.FLOAT),
            depthA: makeTarget(gl, width, height, gl.R32F, gl.RED, gl.FLOAT, 1, true),
            depthB: makeTarget(gl, width, height, gl.R32F, gl.RED, gl.FLOAT),
            thickness: makeTarget(gl, width, height, gl.R16F, gl.RED, gl.HALF_FLOAT),
        };
        view.screen = s;
        return s;
    }

    function draw(view) {
        const { gl, spec, canvas } = view;
        fit(view);
        const w = canvas.width, h = canvas.height;
        const s = screenTargets(view, w, h);
        const m = matrices(view, w, h);
        const light = lightDirection(spec);
        const common = u => {
            gl.uniform2f(u.uResolution, w, h);
            gl.uniformMatrix4fv(u.uView, false, m.viewM);
            gl.uniformMatrix4fv(u.uProjection, false, m.proj);
            gl.uniform3fv(u.uEye, m.eye);
            gl.uniform3f(u.uBox, spec.box[0], spec.box[1], spec.box[2]);
            gl.uniform1f(u.uRadius, spec.spacing * 0.75);
            gl.uniform3fv(u.uIor, spec.ior);
            gl.uniform3fv(u.uAlpha, spec.alpha);
            gl.uniform3fv(u.uLight, light);
            gl.uniform1f(u.uLampStrength, spec.lamp);
            gl.uniform1f(u.uExposure, spec.exposure);
            gl.uniform1i(u.uFloorStyle, spec.floor);
            if (u.uTanHalf) gl.uniform1f(u.uTanHalf, TAN_HALF);
            if (u.uForward) { gl.uniform3fv(u.uForward, m.forward); gl.uniform3fv(u.uRight, m.right); gl.uniform3fv(u.uUp, m.up); }
            if (u.uInverseView) gl.uniformMatrix4fv(u.uInverseView, false, m.inverse);
            if (u.uDebug) gl.uniform1i(u.uDebug, view.debug || 0);
        };
        gl.disable(gl.BLEND);
        gl.disable(gl.DEPTH_TEST);
        pass(gl, view.background, s.background, w, h, common, {});
        // Sphere depth with the depth test.
        gl.bindFramebuffer(gl.FRAMEBUFFER, s.depthA.fbo);
        gl.viewport(0, 0, w, h);
        gl.clearColor(0, 0, 0, 0);
        gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
        gl.enable(gl.DEPTH_TEST);
        gl.useProgram(view.sphereDepth.program);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, view.states[view.current].textures[0]);
        gl.uniform1i(view.sphereDepth.u.uPos, 0);
        gl.uniform1i(view.sphereDepth.u.uSide, view.side);
        common(view.sphereDepth.u);
        gl.drawArrays(gl.POINTS, 0, view.side * view.side);
        gl.disable(gl.DEPTH_TEST);
        // Thickness, additive.
        gl.bindFramebuffer(gl.FRAMEBUFFER, s.thickness.fbo);
        gl.clear(gl.COLOR_BUFFER_BIT);
        gl.enable(gl.BLEND);
        gl.blendFunc(gl.ONE, gl.ONE);
        gl.useProgram(view.sphereThickness.program);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, view.states[view.current].textures[0]);
        gl.uniform1i(view.sphereThickness.u.uPos, 0);
        gl.uniform1i(view.sphereThickness.u.uSide, view.side);
        common(view.sphereThickness.u);
        gl.drawArrays(gl.POINTS, 0, view.side * view.side);
        gl.disable(gl.BLEND);
        // Smooth the depth twice along each axis.
        const blur = Math.max(4, Math.min(24, spec.spacing * 0.75 * 2 * m.proj[5] * h / (2 * 0.5)));
        let from = s.depthA, to = s.depthB;
        for (let k = 0; k < 2; k++) {
            pass(gl, view.smooth, to, w, h, u => { common(u); gl.uniform2f(u.uAxis, 1, 0); gl.uniform1f(u.uBlurRadius, blur); }, { uDepth: from.texture });
            [from, to] = [to, from];
            pass(gl, view.smooth, to, w, h, u => { common(u); gl.uniform2f(u.uAxis, 0, 1); gl.uniform1f(u.uBlurRadius, blur); }, { uDepth: from.texture });
            [from, to] = [to, from];
        }
        pass(gl, view.compose, null, w, h, common, { uDepth: from.texture, uThickness: s.thickness.texture, uBackground: s.background.texture });
    }

    function schedule(view) {
        if (view.frame) return;
        const run = () => {
            if (!view.frame) return;
            cancelAnimationFrame(view.frame);
            clearTimeout(view.fallback);
            view.frame = 0;
            frame(view, performance.now());
        };
        view.frame = requestAnimationFrame(run);
        view.fallback = setTimeout(run, 250);
    }

    function frame(view, time) {
        if (!view.spec || !document.body.contains(view.canvas)) return;
        const seconds = view.lastTime ? Math.min(0.033, (time - view.lastTime) / 1000) : 1 / 60;
        view.lastTime = time;
        surfaces(view);
        step(view, seconds);
        draw(view);
        view.frames++;
        if (time - view.fpsTime > 1000) { view.fps = view.frames; view.frames = 0; view.fpsTime = time; }
        schedule(view);
    }

    // Where a point on the canvas meets the tank's rim plane, in metres; null when it misses the tank.
    function tankPoint(view, px, py) {
        const { eye, forward, right, up } = camera(view);
        const w = view.canvas.clientWidth, h = view.canvas.clientHeight;
        if (!w || !h) return null;
        const sx = (px / w * 2 - 1) * TAN_HALF * (w / h), sy = (1 - py / h * 2) * TAN_HALF;
        const d = normalize([forward[0] + right[0] * sx + up[0] * sy, forward[1] + right[1] * sx + up[1] * sy, forward[2] + right[2] * sx + up[2] * sy]);
        const level = view.spec.box[1] * 0.5;
        if (Math.abs(d[1]) < 1e-4) return null;
        const t = (level - eye[1]) / d[1];
        if (t < 0) return null;
        const x = eye[0] + d[0] * t, z = eye[2] + d[2] * t;
        const m = view.spec.spacing * 2;
        if (x < m || z < m || x > view.spec.box[0] - m || z > view.spec.box[2] - m) return null;
        return [x, z];
    }

    function attach(view) {
        const c = view.canvas;
        c.addEventListener("contextmenu", e => e.preventDefault());
        c.addEventListener("pointerdown", e => {
            if (!view.spec) return;
            c.setPointerCapture(e.pointerId);
            view.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
            if (view.pointers.size === 2) {
                view.pouring = null;
                const p = [...view.pointers.values()];
                view.pinch = Math.hypot(p[0].x - p[1].x, p[0].y - p[1].y);
                view.dragging = true;
                view.lastX = (p[0].x + p[1].x) / 2;
                view.lastY = (p[0].y + p[1].y) / 2;
                return;
            }
            const rect = c.getBoundingClientRect();
            if (e.button === 2 || e.button === 1) {
                view.dragging = true;
                view.lastX = e.clientX;
                view.lastY = e.clientY;
            } else if (e.button === 0) {
                const at = tankPoint(view, e.clientX - rect.left, e.clientY - rect.top);
                if (at) {
                    blob(view, [at[0], view.spec.box[1] - view.spec.dropRadius - view.spec.spacing, at[1]], view.spec.dropRadius, [0, 0, 0]);
                    view.pouring = at;
                    view.pourClock = 0;
                }
            }
        });
        c.addEventListener("pointermove", e => {
            if (!view.spec) return;
            if (view.pointers.has(e.pointerId)) view.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
            if (view.pointers.size === 2) {
                const p = [...view.pointers.values()];
                const d = Math.hypot(p[0].x - p[1].x, p[0].y - p[1].y);
                if (view.pinch > 0 && d > 0) { view.distance = Math.min(3, Math.max(0.2, view.distance * view.pinch / d)); view.pinch = d; }
                const mx = (p[0].x + p[1].x) / 2, my = (p[0].y + p[1].y) / 2;
                view.yaw -= (mx - view.lastX) * 0.008;
                view.pitch = Math.min(1.5, Math.max(0.05, view.pitch + (my - view.lastY) * 0.008));
                view.lastX = mx; view.lastY = my;
                return;
            }
            const rect = c.getBoundingClientRect();
            if (view.dragging) {
                view.yaw -= (e.clientX - view.lastX) * 0.008;
                view.pitch = Math.min(1.5, Math.max(0.05, view.pitch + (e.clientY - view.lastY) * 0.008));
                view.lastX = e.clientX; view.lastY = e.clientY;
            } else if (view.pouring) {
                view.pouring = tankPoint(view, e.clientX - rect.left, e.clientY - rect.top) || view.pouring;
            }
        });
        const release = e => {
            view.pointers.delete(e.pointerId);
            if (view.pointers.size === 0) { view.pouring = null; view.dragging = false; view.pinch = 0; }
        };
        c.addEventListener("pointerup", release);
        c.addEventListener("pointercancel", release);
        c.addEventListener("wheel", e => {
            if (!(e.ctrlKey || e.metaKey)) return;
            e.preventDefault();
            view.distance = Math.min(3, Math.max(0.2, view.distance * Math.exp(e.deltaY * 0.0012)));
        }, { passive: false });
        if (typeof ResizeObserver !== "undefined") new ResizeObserver(() => fit(view)).observe(c);
    }

    return {
        async render(id, spec) {
            const canvas = document.getElementById(id);
            if (!canvas) return false;
            let view = views.get(canvas);
            if (!view) {
                try { view = await setup(canvas); } catch (error) { console.error("undine: shader failed to build", error); return false; }
                if (!view) return false;
                views.set(canvas, view);
            }
            view.spec = spec;
            schedule(view);
            return true;
        },
        drop(id) {
            const view = views.get(document.getElementById(id));
            if (!view || !view.spec) return;
            const s = view.spec;
            blob(view, [s.box[0] * (0.3 + 0.4 * Math.random()), s.box[1] - s.dropRadius - s.spacing, s.box[2] * (0.3 + 0.4 * Math.random())], s.dropRadius, [0, -s.dropSpeed, 0]);
        },
        splash(id) {
            const view = views.get(document.getElementById(id));
            if (!view || !view.spec) return;
            const s = view.spec;
            blob(view, [s.box[0] / 2, s.box[1] - s.dropRadius * 2 - s.spacing, s.box[2] / 2], s.dropRadius * 2, [0, -s.dropSpeed * 1.5, 0]);
        },
        empty(id) {
            const view = views.get(document.getElementById(id));
            if (view) view.states = null;
        },
        count(id) {
            const view = views.get(document.getElementById(id));
            return view ? view.alive : 0;
        },
        dolly(id, factor) {
            const view = views.get(document.getElementById(id));
            if (view) view.distance = Math.min(3, Math.max(0.2, view.distance * factor));
        },
        fps(id) {
            const view = views.get(document.getElementById(id));
            return view ? view.fps : 0;
        },
        // Positions and velocities read back: count, mean and highest height, RMS speed. For checking against the package.
        stats(id) {
            const view = views.get(document.getElementById(id));
            if (!view || !view.states) return null;
            const { gl } = view;
            const n = view.side;
            const read = index => {
                const state = view.states[view.current];
                gl.bindFramebuffer(gl.FRAMEBUFFER, state.fbo);
                gl.readBuffer(gl.COLOR_ATTACHMENT0 + index);
                const data = new Float32Array(n * n * 4);
                gl.readPixels(0, 0, n, n, gl.RGBA, gl.FLOAT, data);
                gl.readBuffer(gl.COLOR_ATTACHMENT0);
                gl.bindFramebuffer(gl.FRAMEBUFFER, null);
                return data;
            };
            const pos = read(0), vel = read(1);
            let count = 0, sumY = 0, top = -1, sumV2 = 0;
            const heights = [];
            for (let i = 0; i < n * n; i++) {
                if (pos[i * 4 + 3] < 0.5) continue;
                count++;
                sumY += pos[i * 4 + 1];
                heights.push(pos[i * 4 + 1]);
                top = Math.max(top, pos[i * 4 + 1]);
                sumV2 += vel[i * 4] ** 2 + vel[i * 4 + 1] ** 2 + vel[i * 4 + 2] ** 2;
            }
            heights.sort((a, b) => a - b);
            return { count, meanY: sumY / Math.max(1, count), top, top98: heights[Math.floor(heights.length * 0.98)] || 0, rmsSpeed: Math.sqrt(sumV2 / Math.max(1, count)) };
        },
        snapshot(id, width, height, seconds, action, debug) {
            const view = views.get(document.getElementById(id));
            if (!view || !view.spec) return null;
            view.debug = debug || 0;
            view.forceSize = [width || 1280, height || 800];
            surfaces(view);
            if (action === "drop") this.drop(id);
            if (action === "splash") this.splash(id);
            const frames = Math.round((seconds || 0) * 60);
            for (let i = 0; i < frames; i++) step(view, 1 / 60);
            draw(view);
            const url = view.canvas.toDataURL("image/png");
            view.forceSize = null;
            return url;
        },
    };
})();
