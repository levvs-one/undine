// Undine's pouring on the GPU, driven from .NET: the page sends a spec (the liquid's numbers, the table, the dish,
// the spout), the flow and the picture run here every frame. The shaders are loaded from /shaders so the files
// the site hands out are the files it runs. Press on the table to pour there; drag with the right button or two
// fingers to look around; Ctrl + wheel, a pinch or the buttons to come closer.
window.undineFlow = (() => {
    const VERTEX = `#version 300 es
void main() {
    vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}`;
    // The floor: a table with a round dish sunk into it, written into the alpha channel; the liquid starts empty.
    const FLOOR_FS = `#version 300 es
precision highp float;
uniform float uCells;
uniform float uSide;
uniform vec2 uDish;            // centre, metres from the table's centre
uniform float uDishRadius;
uniform float uDishDepth;
uniform float uTilt;           // the table's slope along x, metres per metre
out vec4 outState;
void main() {
    vec2 xz = (gl_FragCoord.xy / uCells - 0.5) * uSide;
    float r = length(xz - uDish) / max(1e-3, uDishRadius);
    float dish = -uDishDepth * max(0.0, 1.0 - r * r * r * r);
    outState = vec4(0.0, 0.0, 0.0, dish + uTilt * xz.x);
}`;
    const TAN_HALF = Math.tan(38 * Math.PI / 360);
    const G = 9.80665;
    const views = new Map();
    const setupErrors = new Map();
    let sources = null;

    async function loadSources() {
        if (sources) return sources;
        const [step, render] = await Promise.all([
            fetch("shaders/undine-flow-step.frag.glsl").then(r => r.text()),
            fetch("shaders/undine-flow.frag.glsl").then(r => r.text()),
        ]);
        sources = { step, render };
        return sources;
    }

    function compile(gl, type, source) {
        const shader = gl.createShader(type);
        gl.shaderSource(shader, source);
        gl.compileShader(shader);
        if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(shader));
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

    function makeTarget(gl, size) {
        const texture = gl.createTexture();
        gl.bindTexture(gl.TEXTURE_2D, texture);
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA32F, size, size, 0, gl.RGBA, gl.FLOAT, null);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
        const fbo = gl.createFramebuffer();
        gl.bindFramebuffer(gl.FRAMEBUFFER, fbo);
        gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, texture, 0);
        gl.clearColor(0, 0, 0, 0);
        gl.clear(gl.COLOR_BUFFER_BIT);
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        return { texture, fbo, size };
    }

    // The context, with the reason when there is none: a browser that has WebGL1 only, WebGL switched off, or WebGL
    // blocked for this site after its GPU process crashed, which reads the same as a black canvas otherwise.
    function openContext(canvas) {
        let why = "";
        canvas.addEventListener("webglcontextcreationerror", e => { why = e.statusMessage || ""; }, { once: true });
        const gl = canvas.getContext("webgl2", { antialias: false, alpha: false, preserveDrawingBuffer: true });
        if (!gl) {
            const detail = why ? " (" + why + ")" : "";
            const legacy = document.createElement("canvas").getContext("webgl");
            throw new Error(legacy
                ? "WebGL2 is not available in this browser, only WebGL1, and this page needs WebGL2" + detail
                : "WebGL is off in this browser: hardware acceleration is disabled, or the browser blocked WebGL for this site after its GPU process crashed. Reload the page; if it stays so, restart the browser and look at chrome://gpu" + detail);
        }
        if (!gl.getExtension("EXT_color_buffer_float")) throw new Error("This GPU has no float render targets (EXT_color_buffer_float), which the simulation needs");
        return gl;
    }

    async function setup(canvas) {
        const gl = openContext(canvas);
        const src = await loadSources();
        const view = {
            canvas, gl,
            floor: link(gl, VERTEX, FLOOR_FS),
            step: link(gl, VERTEX, src.step),
            render: link(gl, VERTEX, src.render),
            spec: null, cells: 0, a: null, b: null, floorKey: "",
            yaw: 0.5, pitch: 0.42, distance: 0.75,
            spout: null, pointers: new Map(), pinch: 0, dragging: false, lastX: 0, lastY: 0,
            lastTime: 0, frame: 0, fallback: 0, frames: 0, fpsTime: 0, fps: 0, poured: 0, pouring: 0,
        };
        attach(view);
        canvas.addEventListener("webglcontextlost", e => { e.preventDefault(); view.error = "WebGL context lost: the browser's GPU process reset it; reload the page"; });
        canvas.addEventListener("webglcontextrestored", () => { view.error = ""; view.a = null; schedule(view); });
        return view;
    }

    function pass(gl, program, input, target, size, setUniforms) {
        gl.useProgram(program.program);
        gl.bindFramebuffer(gl.FRAMEBUFFER, target ? target.fbo : null);
        gl.viewport(0, 0, size.w || size, size.h || size);
        gl.activeTexture(gl.TEXTURE0);
        if (input) gl.bindTexture(gl.TEXTURE_2D, input.texture);
        setUniforms(program.u);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
    }

    // Targets for the grid, and the floor whenever the table changes shape (which also empties it).
    function surfaces(view) {
        const { gl, spec } = view;
        const key = [spec.cells, spec.side, spec.dishX, spec.dishZ, spec.dishRadius, spec.dishDepth, spec.tilt].join("/");
        if (view.a && key === view.floorKey) return;
        view.floorKey = key;
        view.cells = spec.cells;
        view.a = makeTarget(gl, spec.cells);
        view.b = makeTarget(gl, spec.cells);
        view.poured = 0;
        pass(gl, view.floor, null, view.a, spec.cells, u => {
            gl.uniform1f(u.uCells, spec.cells);
            gl.uniform1f(u.uSide, spec.side);
            gl.uniform2f(u.uDish, spec.dishX, spec.dishZ);
            gl.uniform1f(u.uDishRadius, spec.dishRadius);
            gl.uniform1f(u.uDishDepth, spec.dishDepth);
            gl.uniform1f(u.uTilt, spec.tilt);
        });
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    }

    const cellSize = spec => spec.side / spec.cells;

    // The flow, in substeps that respect the two-dimensional CFL bound for the fastest liquid the cap allows.
    function stepFlow(view, seconds) {
        const { gl, spec } = view;
        const cap = spec.speedCap;
        const dt = 0.25 * cellSize(spec) / (cap + Math.sqrt(G * Math.max(spec.dishDepth + 0.02, 0.02)));
        const substeps = Math.min(24, Math.max(1, Math.ceil(seconds / dt)));
        const sub = seconds / substeps;
        const spout = view.spout;
        if (spout) view.poured += spec.rate * seconds;
        gl.disable(gl.BLEND);
        view.clock = (view.clock || 0);
        for (let i = 0; i < substeps; i++) {
            view.clock += sub;
            // A poured stream is never quite steady: it pulses and wanders a little, which is what ripples the pool.
            const pulse = 1 + 0.2 * Math.sin(2 * Math.PI * 9 * view.clock) + 0.12 * Math.sin(2 * Math.PI * 13.7 * view.clock + 1);
            pass(gl, view.step, view.a, view.b, spec.cells, u => {
                gl.uniform1i(u.uState, 0);
                gl.uniform1f(u.uCell, cellSize(spec));
                gl.uniform1f(u.uDt, sub);
                gl.uniform1f(u.uViscosity, spec.kinematicViscosity);
                gl.uniform1f(u.uSpeedCap, cap);
                gl.uniform1f(u.uRetention, spec.retention);
                if (spout) {
                    const radiusCells = Math.max(1, spec.spoutRadius / cellSize(spec));
                    const cellsUnder = Math.PI * radiusCells * radiusCells;
                    gl.uniform2f(u.uSpout, spout.u, spout.v);
                    gl.uniform1f(u.uSpoutRadius, radiusCells);
                    gl.uniform1f(u.uSpoutRate, spec.rate * pulse / (cellsUnder * cellSize(spec) * cellSize(spec)));
                } else {
                    gl.uniform2f(u.uSpout, -1, -1);
                    gl.uniform1f(u.uSpoutRadius, 1);
                    gl.uniform1f(u.uSpoutRate, 0);
                }
            });
            [view.a, view.b] = [view.b, view.a];
        }
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    }

    function lightDirection(spec) {
        const az = spec.lampAzimuth * Math.PI / 180, el = spec.lampElevation * Math.PI / 180;
        return [-Math.sin(az) * Math.cos(el), -Math.sin(el), -Math.cos(az) * Math.cos(el)];
    }

    function camera(view) {
        const cp = Math.cos(view.pitch), sp = Math.sin(view.pitch), cy = Math.cos(view.yaw), sy = Math.sin(view.yaw);
        const eye = [view.distance * sy * cp, view.distance * sp, -view.distance * cy * cp];
        const forward = normalize([-eye[0], -eye[1], -eye[2]]);
        const right = normalize(cross([0, 1, 0], forward));
        const up = cross(forward, right);
        return { eye, forward, right, up };
    }
    function normalize(v) { const l = Math.hypot(v[0], v[1], v[2]) || 1; return [v[0] / l, v[1] / l, v[2] / l]; }
    function cross(a, b) { return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]; }

    // Where a point on the canvas meets the table plane, in cells and metres; null when it misses the table.
    function tableCell(view, px, py) {
        const { eye, forward, right, up } = camera(view);
        const w = view.canvas.clientWidth, h = view.canvas.clientHeight;
        if (!w || !h) return null;
        const sx = (px / w * 2 - 1) * TAN_HALF * (w / h), sy = (1 - py / h * 2) * TAN_HALF;
        const d = normalize([forward[0] + right[0] * sx + up[0] * sy, forward[1] + right[1] * sx + up[1] * sy, forward[2] + right[2] * sx + up[2] * sy]);
        if (d[1] >= -1e-4) return null;
        const t = -eye[1] / d[1];
        const x = eye[0] + d[0] * t, z = eye[2] + d[2] * t;
        const half = view.spec.side / 2;
        if (Math.abs(x) > half || Math.abs(z) > half) return null;
        return { u: (x / view.spec.side + 0.5) * view.spec.cells, v: (z / view.spec.side + 0.5) * view.spec.cells, x, z };
    }

    function fit(view) {
        const c = view.canvas;
        if (view.forceSize) {
            [c.width, c.height] = view.forceSize;
            return;
        }
        const narrow = Math.min(window.innerWidth, document.documentElement.clientWidth) < 700;
        const dpr = narrow ? 1 : Math.min(window.devicePixelRatio || 1, 1.5);
        const width = Math.max(1, Math.min(1400, Math.round(c.clientWidth * dpr)));
        const height = Math.max(1, Math.round(width * 0.625));
        if (c.width !== width || c.height !== height) {
            c.width = width;
            c.height = height;
        }
    }

    function draw(view) {
        const { gl, spec, render, canvas } = view;
        fit(view);
        const { eye, forward, right, up } = camera(view);
        const spout = view.spout;
        pass(gl, render, view.a, null, { w: canvas.width, h: canvas.height }, u => {
            gl.uniform1i(u.uState, 0);
            gl.uniform2f(u.uResolution, canvas.width, canvas.height);
            gl.uniform3fv(u.uEye, eye);
            gl.uniform3fv(u.uForward, forward);
            gl.uniform3fv(u.uRight, right);
            gl.uniform3fv(u.uUp, up);
            gl.uniform1f(u.uTanHalf, TAN_HALF);
            gl.uniform1f(u.uSide, spec.side);
            gl.uniform3fv(u.uIor, spec.ior);
            gl.uniform3fv(u.uAlpha, spec.alpha);
            gl.uniform3fv(u.uLight, lightDirection(spec));
            gl.uniform1f(u.uLampStrength, spec.lamp);
            gl.uniform1f(u.uExposure, spec.exposure);
            gl.uniform1i(u.uTable, spec.table);
            gl.uniform3f(u.uSpout, spout ? spout.x : 0, spout ? spec.spoutHeight : -1, spout ? spout.z : 0);
            gl.uniform1f(u.uSpoutRadius, spec.spoutRadius);
        });
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
        const interval = view.lastTime ? (time - view.lastTime) / 1000 : 1 / 60;
        // A GPU that cannot keep up gets less simulated time per frame, so the liquid slows down, rather than a frame
        // long enough for the driver to reset the context and the browser to give up on WebGL.
        view.budget = interval > 0.05 ? Math.max(0.05, (view.budget || 1) * 0.7) : Math.min(1, (view.budget || 1) * 1.05);
        const seconds = Math.min(0.05, interval) * view.budget;
        view.lastTime = time;
        try {
            if (view.gl.isContextLost()) throw new Error("WebGL context lost: the browser's GPU process reset it; reload the page, or restart the browser if it stays black");
            surfaces(view);
            stepFlow(view, seconds);
            draw(view);
            if (view.frames < 3) {
                const err = view.gl.getError();
                if (err !== view.gl.NO_ERROR) view.error = "WebGL error " + err + " in the first frames";
            }
        } catch (error) {
            view.error = String(error && error.message || error);
            console.error("undine flow:", error);
            view.frame = 0;
            return;
        }
        view.frames++;
        if (time - view.fpsTime > 1000) {
            view.fps = view.frames;
            view.frames = 0;
            view.fpsTime = time;
        }
        schedule(view);
    }

    function attach(view) {
        const c = view.canvas;
        c.addEventListener("contextmenu", e => e.preventDefault());
        c.addEventListener("pointerdown", e => {
            if (!view.spec) return;
            c.setPointerCapture(e.pointerId);
            view.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
            if (view.pointers.size === 2) {
                view.spout = null;
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
                view.spout = tableCell(view, e.clientX - rect.left, e.clientY - rect.top);
            }
        });
        c.addEventListener("pointermove", e => {
            if (!view.spec) return;
            if (view.pointers.has(e.pointerId)) view.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
            if (view.pointers.size === 2) {
                const p = [...view.pointers.values()];
                const d = Math.hypot(p[0].x - p[1].x, p[0].y - p[1].y);
                if (view.pinch > 0 && d > 0) {
                    view.distance = Math.min(3, Math.max(0.25, view.distance * view.pinch / d));
                    view.pinch = d;
                }
                const mx = (p[0].x + p[1].x) / 2, my = (p[0].y + p[1].y) / 2;
                view.yaw -= (mx - view.lastX) * 0.008;
                view.pitch = Math.min(1.5, Math.max(0.15, view.pitch + (my - view.lastY) * 0.008));
                view.lastX = mx;
                view.lastY = my;
                return;
            }
            const rect = c.getBoundingClientRect();
            if (view.dragging) {
                view.yaw -= (e.clientX - view.lastX) * 0.008;
                view.pitch = Math.min(1.5, Math.max(0.15, view.pitch + (e.clientY - view.lastY) * 0.008));
                view.lastX = e.clientX;
                view.lastY = e.clientY;
            } else if (view.spout) {
                view.spout = tableCell(view, e.clientX - rect.left, e.clientY - rect.top);
            }
        });
        const release = e => {
            view.pointers.delete(e.pointerId);
            if (view.pointers.size === 0) {
                view.spout = null;
                view.dragging = false;
                view.pinch = 0;
            }
        };
        c.addEventListener("pointerup", release);
        c.addEventListener("pointercancel", release);
        c.addEventListener("wheel", e => {
            if (!(e.ctrlKey || e.metaKey)) return;
            e.preventDefault();
            view.distance = Math.min(3, Math.max(0.25, view.distance * Math.exp(e.deltaY * 0.0012)));
        }, { passive: false });
        if (typeof ResizeObserver !== "undefined") {
            new ResizeObserver(() => fit(view)).observe(c);
        }
    }

    return {
        async render(id, spec) {
            const canvas = document.getElementById(id);
            if (!canvas) return false;
            let view = views.get(canvas);
            if (!view) {
                try {
                    view = await setup(canvas);
                } catch (error) {
                    console.error("undine: shader failed to build", error);
                    setupErrors.set(canvas, String(error && error.message || error));
                    return false;
                }
                if (!view) { setupErrors.set(canvas, "The table could not be set up"); return false; }
                views.set(canvas, view);
            }
            view.spec = spec;
            schedule(view);
            return true;
        },
        // The page is going: its context goes with it, since a browser keeps only so many and loses the oldest, and
        // any canvas already gone from the document is freed too.
        dispose(id) {
            for (const [canvas, view] of [...views]) {
                if (canvas.id !== id && canvas.isConnected) continue;
                cancelAnimationFrame(view.frame);
                clearTimeout(view.fallback);
                view.frame = 0;
                view.spec = null;
                const lose = view.gl.getExtension("WEBGL_lose_context");
                if (lose) lose.loseContext();
                views.delete(canvas);
                setupErrors.delete(canvas);
            }
        },

        // Pour at a point of the table given in metres from its centre, for a number of seconds.
        pourAt(id, x, z, seconds) {
            const view = views.get(document.getElementById(id));
            if (!view || !view.spec) return;
            const s = view.spec;
            view.spout = { u: (x / s.side + 0.5) * s.cells, v: (z / s.side + 0.5) * s.cells, x, z };
            clearTimeout(view.pourTimer);
            view.pourTimer = setTimeout(() => { if (view.pointers.size === 0) view.spout = null; }, seconds * 1000);
        },
        empty(id) {
            const view = views.get(document.getElementById(id));
            if (view) view.a = null;
        },
        poured(id) {
            const view = views.get(document.getElementById(id));
            return view ? view.poured : 0;
        },
        dolly(id, factor) {
            const view = views.get(document.getElementById(id));
            if (view) view.distance = Math.min(3, Math.max(0.25, view.distance * factor));
        },
        fps(id) {
            const view = views.get(document.getElementById(id));
            return view ? view.fps : 0;
        },
        error(id) {
            const canvas = document.getElementById(id);
            const view = views.get(canvas);
            return view && view.error ? view.error : setupErrors.get(canvas) || "";
        },
        // One frame rendered now at a fixed size after a spell of simulation, as a PNG data URL, for checking without a screen.
        snapshot(id, width, height, seconds, pourSeconds) {
            const view = views.get(document.getElementById(id));
            if (!view || !view.spec) return null;
            view.forceSize = [width || 1280, height || 800];
            surfaces(view);
            const s = view.spec;
            const spout = { u: s.cells * 0.5 + s.dishX / s.side * s.cells, v: s.cells * 0.5 + s.dishZ / s.side * s.cells, x: s.dishX, z: s.dishZ };
            const frames = Math.round((seconds || 0) * 60);
            for (let i = 0; i < frames; i++) {
                view.spout = i < Math.round((pourSeconds || 0) * 60) ? spout : null;
                stepFlow(view, 1 / 60);
            }
            view.spout = (pourSeconds || 0) >= (seconds || 0) && frames > 0 ? spout : null;
            draw(view);
            view.spout = null;
            const url = view.canvas.toDataURL("image/png");
            view.forceSize = null;
            return url;
        },
    };
})();
