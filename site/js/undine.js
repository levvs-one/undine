// Undine's water on the GPU: the surface steps in one texture, the lamp's light on the floor is summed from refracted
// points, and the picture is traced per pixel. The three shaders are loaded from /shaders so the files the site
// hands out are the files it runs.
(async () => {
    const canvas = document.getElementById("water");
    const status = document.getElementById("water-status");
    const gl = canvas.getContext("webgl2", { antialias: false, alpha: false, preserveDrawingBuffer: false });
    const strings = window.undineStrings;
    if (!gl || !gl.getExtension("EXT_color_buffer_float")) {
        status.textContent = strings.t("nogl");
        return;
    }

    const [simSource, causticSource, renderSource, liquidsJson] = await Promise.all([
        fetch("shaders/undine-water-sim.frag.glsl").then(r => r.text()),
        fetch("shaders/undine-water-caustics.vert.glsl").then(r => r.text()),
        fetch("shaders/undine-water.frag.glsl").then(r => r.text()),
        fetch("data/liquids.json").then(r => r.json()),
    ]);
    const liquids = liquidsJson.liquids;

    const VERTEX = `#version 300 es
void main() {
    vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}`;
    const POINT_FS = `#version 300 es
precision highp float;
in vec3 vEnergy;
out vec4 outColour;
void main() { outColour = vec4(vEnergy, 1.0); }`;

    function compile(type, source) {
        const shader = gl.createShader(type);
        gl.shaderSource(shader, source);
        gl.compileShader(shader);
        if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
            throw new Error(gl.getShaderInfoLog(shader));
        }
        return shader;
    }

    function link(vertex, fragment) {
        const program = gl.createProgram();
        gl.attachShader(program, compile(gl.VERTEX_SHADER, vertex));
        gl.attachShader(program, compile(gl.FRAGMENT_SHADER, fragment));
        gl.linkProgram(program);
        if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
            throw new Error(gl.getProgramInfoLog(program));
        }
        const uniforms = {};
        const count = gl.getProgramParameter(program, gl.ACTIVE_UNIFORMS);
        for (let i = 0; i < count; i++) {
            const info = gl.getActiveUniform(program, i);
            uniforms[info.name.replace(/\[0\]$/, "")] = gl.getUniformLocation(program, info.name);
        }
        return { program, u: uniforms };
    }

    let sim, caustic, render;
    try {
        sim = link(VERTEX, simSource);
        caustic = link(causticSource, POINT_FS);
        render = link(VERTEX, renderSource);
    } catch (error) {
        console.error("undine: shader failed to build", error);
        status.textContent = strings.t("nogl");
        return;
    }

    // ---- state ----
    const state = {
        liquid: liquids[0],
        depth: 1.2,        // metres
        side: 4,           // metres
        floor: 0,
        lampAzimuth: 35,   // degrees
        lampElevation: 55,
        lampStrength: 1,
        exposure: 1,
        cells: 256,
        yaw: 0.35, pitch: 0.62, distance: 5.5,
        orbit: false,
        touch: null,       // {u, v} in cells while a finger is down
        lastTime: 0,
        dragging: false, lastX: 0, lastY: 0,
        pointers: new Map(), pinch: 0,
    };

    // ---- textures ----
    // Float textures are read with NEAREST: linear filtering of floats is an extension some GPUs lack, and a texture
    // filtered that way without it is incomplete and reads as zero. The shaders interpolate by hand where they need to.
    function makeTexture(size, internal, format, type) {
        const texture = gl.createTexture();
        gl.bindTexture(gl.TEXTURE_2D, texture);
        gl.texImage2D(gl.TEXTURE_2D, 0, internal, size, size, 0, format, type, null);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
        const fbo = gl.createFramebuffer();
        gl.bindFramebuffer(gl.FRAMEBUFFER, fbo);
        gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, texture, 0);
        const ok = gl.checkFramebufferStatus(gl.FRAMEBUFFER) === gl.FRAMEBUFFER_COMPLETE;
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        return { texture, fbo, size, ok };
    }

    let stateA, stateB, causticMap;
    const CAUSTIC_SIZE = 512;
    function resetSurface() {
        stateA = makeTexture(state.cells, gl.RGBA32F, gl.RGBA, gl.FLOAT);
        stateB = makeTexture(state.cells, gl.RGBA32F, gl.RGBA, gl.FLOAT);
        causticMap = makeTexture(CAUSTIC_SIZE, gl.RGBA32F, gl.RGBA, gl.FLOAT);
        for (const target of [stateA, stateB]) {
            gl.bindFramebuffer(gl.FRAMEBUFFER, target.fbo);
            gl.clearColor(0, 0, 0, 1);
            gl.clear(gl.COLOR_BUFFER_BIT);
        }
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    }
    resetSurface();

    // ---- physics numbers ----
    const G = 9.80665;
    const waveSpeed = () => Math.sqrt(G * state.depth);
    const cell = () => state.side / state.cells;
    const stableStep = () => Math.min(cell() / (waveSpeed() * Math.SQRT2), 0.1 * cell() * cell() / Math.max(1e-12, state.liquid.kinematicViscosity));
    const lightDirection = () => {
        const az = state.lampAzimuth * Math.PI / 180, el = state.lampElevation * Math.PI / 180;
        return [-Math.sin(az) * Math.cos(el), -Math.sin(el), -Math.cos(az) * Math.cos(el)];
    };

    // ---- simulation ----
    function stepSurface(seconds) {
        const substeps = Math.min(40, Math.max(1, Math.ceil(seconds / stableStep())));
        const dt = seconds / substeps;
        gl.useProgram(sim.program);
        gl.viewport(0, 0, state.cells, state.cells);
        gl.disable(gl.BLEND);
        for (let i = 0; i < substeps; i++) {
            gl.bindFramebuffer(gl.FRAMEBUFFER, stateB.fbo);
            gl.activeTexture(gl.TEXTURE0);
            gl.bindTexture(gl.TEXTURE_2D, stateA.texture);
            gl.uniform1i(sim.u.uState, 0);
            gl.uniform1f(sim.u.uCell, cell());
            gl.uniform1f(sim.u.uDt, dt);
            gl.uniform1f(sim.u.uWaveSpeed, waveSpeed());
            gl.uniform1f(sim.u.uViscosity, state.liquid.kinematicViscosity);
            if (state.touch) {
                gl.uniform2f(sim.u.uTouch, state.touch.u, state.touch.v);
                gl.uniform1f(sim.u.uTouchRadius, Math.max(3, 0.05 / cell()));
                // A finger pushes the surface down about three centimetres per tenth of a second.
                gl.uniform1f(sim.u.uTouchAmount, -0.03 * dt / 0.1);
            } else {
                gl.uniform2f(sim.u.uTouch, -1, -1);
                gl.uniform1f(sim.u.uTouchRadius, 1);
                gl.uniform1f(sim.u.uTouchAmount, 0);
            }
            gl.drawArrays(gl.TRIANGLES, 0, 3);
            [stateA, stateB] = [stateB, stateA];
        }
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    }

    function buildCaustics() {
        const grid = state.cells;
        gl.useProgram(caustic.program);
        gl.bindFramebuffer(gl.FRAMEBUFFER, causticMap.fbo);
        gl.viewport(0, 0, CAUSTIC_SIZE, CAUSTIC_SIZE);
        gl.clearColor(0, 0, 0, 0);
        gl.clear(gl.COLOR_BUFFER_BIT);
        gl.enable(gl.BLEND);
        gl.blendFunc(gl.ONE, gl.ONE);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, stateA.texture);
        gl.uniform1i(caustic.u.uState, 0);
        gl.uniform1f(caustic.u.uCell, cell());
        gl.uniform1f(caustic.u.uDepth, state.depth);
        gl.uniform3fv(caustic.u.uLight, lightDirection());
        gl.uniform3fv(caustic.u.uIor, state.liquid.indexRgb);
        gl.uniform1i(caustic.u.uGrid, grid);
        gl.drawArrays(gl.POINTS, 0, grid * grid * 3);
        gl.disable(gl.BLEND);
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        // A flat surface lands grid² points on CAUSTIC_SIZE² cells, each spread over four: that is one.
        return (CAUSTIC_SIZE * CAUSTIC_SIZE) / (grid * grid);
    }

    // ---- camera ----
    const TAN_HALF = Math.tan(34 * Math.PI / 360);
    function camera() {
        const cp = Math.cos(state.pitch), sp = Math.sin(state.pitch), cy = Math.cos(state.yaw), sy = Math.sin(state.yaw);
        const eye = [state.distance * sy * cp, state.distance * sp, -state.distance * cy * cp];
        const forward = normalize([-eye[0], -eye[1], -eye[2]]);
        const right = normalize(cross([0, 1, 0], forward));
        const up = cross(forward, right);
        return { eye, forward, right, up };
    }
    function normalize(v) { const l = Math.hypot(v[0], v[1], v[2]) || 1; return [v[0] / l, v[1] / l, v[2] / l]; }
    function cross(a, b) { return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]; }

    // Where a point on the canvas meets the rest plane, in cells; null when it misses the pool.
    function surfaceCell(px, py) {
        const { eye, forward, right, up } = camera();
        const w = canvas.clientWidth, h = canvas.clientHeight;
        const sx = (px / w * 2 - 1) * TAN_HALF * (w / h), sy = (1 - py / h * 2) * TAN_HALF;
        const d = normalize([forward[0] + right[0] * sx + up[0] * sy, forward[1] + right[1] * sx + up[1] * sy, forward[2] + right[2] * sx + up[2] * sy]);
        if (d[1] >= -1e-4) return null;
        const t = -eye[1] / d[1];
        const x = eye[0] + d[0] * t, z = eye[2] + d[2] * t;
        const half = state.side / 2;
        if (Math.abs(x) > half || Math.abs(z) > half) return null;
        return { u: (x / state.side + 0.5) * state.cells, v: (z / state.side + 0.5) * state.cells };
    }

    // ---- drawing ----
    function fit() {
        const narrow = Math.min(window.innerWidth, document.documentElement.clientWidth) < 700;
        const dpr = narrow ? 1 : Math.min(window.devicePixelRatio || 1, 1.5);
        const width = Math.max(1, Math.min(1400, Math.round(canvas.clientWidth * dpr)));
        const height = Math.max(1, Math.round(width * 0.625));
        if (canvas.width !== width || canvas.height !== height) {
            canvas.width = width;
            canvas.height = height;
        }
    }

    function draw(norm) {
        fit();
        const { eye, forward, right, up } = camera();
        gl.useProgram(render.program);
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        gl.viewport(0, 0, canvas.width, canvas.height);
        gl.uniform2f(render.u.uResolution, canvas.width, canvas.height);
        gl.uniform3fv(render.u.uEye, eye);
        gl.uniform3fv(render.u.uForward, forward);
        gl.uniform3fv(render.u.uRight, right);
        gl.uniform3fv(render.u.uUp, up);
        gl.uniform1f(render.u.uTanHalf, TAN_HALF);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, stateA.texture);
        gl.uniform1i(render.u.uState, 0);
        gl.activeTexture(gl.TEXTURE1);
        gl.bindTexture(gl.TEXTURE_2D, causticMap.texture);
        gl.uniform1i(render.u.uCaustic, 1);
        gl.uniform1f(render.u.uCausticNorm, norm);
        gl.uniform1f(render.u.uSide, state.side);
        gl.uniform1f(render.u.uDepth, state.depth);
        gl.uniform3fv(render.u.uIor, state.liquid.indexRgb);
        gl.uniform3fv(render.u.uAlpha, state.liquid.absorptionPerMetreRgb);
        gl.uniform3fv(render.u.uLight, lightDirection());
        gl.uniform1f(render.u.uLampStrength, state.lampStrength);
        gl.uniform1i(render.u.uFloor, state.floor);
        gl.uniform1f(render.u.uExposure, state.exposure);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
    }

    // One frame per animation frame; a timer stands in when the tab is hidden and frames do not come.
    let pending = 0, fallback = 0;
    function schedule() {
        if (pending) return;
        const run = () => {
            if (!pending) return;
            cancelAnimationFrame(pending);
            clearTimeout(fallback);
            pending = 0;
            frame(performance.now());
        };
        pending = requestAnimationFrame(run);
        fallback = setTimeout(run, 250);
    }

    let frames = 0, fpsTime = 0;
    function frame(time) {
        const seconds = state.lastTime ? Math.min(0.05, (time - state.lastTime) / 1000) : 1 / 60;
        state.lastTime = time;
        stepSurface(seconds);
        const norm = buildCaustics();
        draw(norm);
        frames++;
        if (time - fpsTime > 1000) {
            document.getElementById("fps").textContent = frames.toString();
            frames = 0;
            fpsTime = time;
        }
        schedule();
    }

    // ---- interaction ----
    canvas.addEventListener("pointerdown", e => {
        if (e.button !== 0) return;
        canvas.setPointerCapture(e.pointerId);
        state.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
        if (state.pointers.size === 2) {
            state.touch = null;
            state.dragging = false;
            const p = [...state.pointers.values()];
            state.pinch = Math.hypot(p[0].x - p[1].x, p[0].y - p[1].y);
            return;
        }
        const rect = canvas.getBoundingClientRect();
        if (state.orbit) {
            state.dragging = true;
            state.lastX = e.clientX;
            state.lastY = e.clientY;
        } else {
            state.touch = surfaceCell(e.clientX - rect.left, e.clientY - rect.top);
        }
    });
    canvas.addEventListener("pointermove", e => {
        if (state.pointers.has(e.pointerId)) state.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
        if (state.pointers.size === 2) {
            const p = [...state.pointers.values()];
            const d = Math.hypot(p[0].x - p[1].x, p[0].y - p[1].y);
            if (state.pinch > 0 && d > 0) {
                state.distance = Math.min(16, Math.max(2, state.distance * state.pinch / d));
                state.pinch = d;
            }
            return;
        }
        const rect = canvas.getBoundingClientRect();
        if (state.dragging) {
            state.yaw -= (e.clientX - state.lastX) * 0.008;
            state.pitch = Math.min(1.5, Math.max(0.12, state.pitch + (e.clientY - state.lastY) * 0.008));
            state.lastX = e.clientX;
            state.lastY = e.clientY;
        } else if (state.touch) {
            state.touch = surfaceCell(e.clientX - rect.left, e.clientY - rect.top);
        }
    });
    const release = e => {
        state.pointers.delete(e.pointerId);
        if (state.pointers.size === 0) {
            state.touch = null;
            state.dragging = false;
            state.pinch = 0;
        }
    };
    canvas.addEventListener("pointerup", release);
    canvas.addEventListener("pointercancel", release);
    canvas.addEventListener("wheel", e => {
        if (!(e.ctrlKey || e.metaKey)) return;
        e.preventDefault();
        state.distance = Math.min(16, Math.max(2, state.distance * Math.exp(e.deltaY * 0.0012)));
    }, { passive: false });

    // ---- controls ----
    const liquidSelect = document.getElementById("liquid");
    for (const liquid of liquids) {
        const option = document.createElement("option");
        option.value = liquid.name;
        option.textContent = strings.liquidName(liquid.name);
        liquidSelect.appendChild(option);
    }
    liquidSelect.addEventListener("change", () => {
        state.liquid = liquids.find(l => l.name === liquidSelect.value) || liquids[0];
        readout();
    });
    const bind = (id, key, after) => {
        const range = document.getElementById(id);
        const number = document.getElementById(id + "-value");
        const apply = value => {
            state[key] = value;
            range.value = value;
            number.value = value;
            if (after) after();
            readout();
        };
        range.addEventListener("input", () => apply(parseFloat(range.value)));
        number.addEventListener("change", () => {
            const v = parseFloat(String(number.value).replace(",", "."));
            if (Number.isFinite(v)) apply(Math.min(parseFloat(range.max), Math.max(parseFloat(range.min), v)));
        });
        apply(state[key]);
    };
    bind("depth", "depth");
    bind("side", "side");
    bind("lamp-azimuth", "lampAzimuth");
    bind("lamp-elevation", "lampElevation");
    bind("lamp", "lampStrength");
    bind("exposure", "exposure");
    document.getElementById("floor").addEventListener("change", e => { state.floor = parseInt(e.target.value, 10); });
    document.getElementById("cells").addEventListener("change", e => { state.cells = parseInt(e.target.value, 10); resetSurface(); readout(); });
    document.getElementById("orbit").addEventListener("change", e => { state.orbit = e.target.checked; status.textContent = strings.t(state.orbit ? "hint.orbit" : "hint.touch"); });
    document.getElementById("drop").addEventListener("click", () => {
        const u = state.cells * (0.3 + 0.4 * Math.random()), v = state.cells * (0.3 + 0.4 * Math.random());
        state.touch = { u, v };
        setTimeout(() => { if (state.pointers.size === 0) state.touch = null; }, 60);
    });
    document.getElementById("calm").addEventListener("click", resetSurface);
    document.getElementById("zoom-in").addEventListener("click", () => { state.distance = Math.max(2, state.distance * 0.8); });
    document.getElementById("zoom-out").addEventListener("click", () => { state.distance = Math.min(16, state.distance * 1.25); });

    function readout() {
        const l = state.liquid;
        const c = waveSpeed();
        const f = strings.number;
        document.getElementById("r-speed").textContent = f(c, 2) + " m/s";
        document.getElementById("r-ripple").textContent = f(phaseSpeed(l, 0.02), 2) + " m/s";
        document.getElementById("r-fresnel").textContent = f(l.normalReflectance * 100, 2) + "%";
        document.getElementById("r-critical").textContent = f(l.criticalAngleDegrees, 1) + "°";
        document.getElementById("r-visc").textContent = f(l.viscosityMPaS, l.viscosityMPaS < 10 ? 2 : 0) + " mPa·s";
        const swatch = document.getElementById("r-colour");
        const depthColour = colourAt(l, state.depth * 2);
        swatch.style.background = depthColour;
        document.getElementById("r-colour-text").textContent = depthColour;
        document.getElementById("r-step").textContent = f(stableStep() * 1000, 2) + " ms";
    }
    function phaseSpeed(l, wavelength) {
        const k = 2 * Math.PI / wavelength;
        const sigma = l.surfaceTensionMNPerM * 1e-3;
        return Math.sqrt((G / k + sigma * k / l.densityKgPerM3) * Math.tanh(k * state.depth));
    }
    // The colour of white after a path, from the three absorption coefficients; the baked hex values are the spectral integral.
    function colourAt(l, path) {
        const a = l.absorptionPerMetreRgb;
        const c = a.map(x => Math.exp(-x * path));
        const compand = v => Math.round(255 * (v <= 0.0031308 ? 12.92 * v : 1.055 * Math.pow(v, 1 / 2.4) - 0.055));
        return "#" + c.map(v => compand(v).toString(16).padStart(2, "0")).join("");
    }

    window.addEventListener("resize", () => fit());
    window.undine = {
        state, resetSurface,
        // Reads the height and velocity at a cell; for tests and for anyone curious what the surface is doing.
        peek(x, y) {
            const out = new Float32Array(4);
            gl.bindFramebuffer(gl.FRAMEBUFFER, stateA.fbo);
            gl.readPixels(x, y, 1, 1, gl.RGBA, gl.FLOAT, out);
            gl.bindFramebuffer(gl.FRAMEBUFFER, null);
            return [out[0], out[1]];
        },
        extremes() {
            const n = state.cells, out = new Float32Array(n * n * 4);
            gl.bindFramebuffer(gl.FRAMEBUFFER, stateA.fbo);
            gl.readPixels(0, 0, n, n, gl.RGBA, gl.FLOAT, out);
            gl.bindFramebuffer(gl.FRAMEBUFFER, null);
            let lo = Infinity, hi = -Infinity;
            for (let i = 0; i < n * n; i++) { lo = Math.min(lo, out[i * 4]); hi = Math.max(hi, out[i * 4]); }
            return [lo, hi];
        },
    };
    status.textContent = strings.t("hint.touch");
    readout();
    schedule();
})();
