// Undine's water on the GPU, driven from .NET: the page sends a spec (the liquid's numbers, depth, side, lamp),
// the simulation and the picture run here every frame. The shaders are loaded from /shaders so the files the site
// hands out are the files it runs. The surface step is spectral: the field is mirrored to twice its size, FFT'd,
// every mode advanced exactly by the liquid's dispersion relation, and transformed back.
window.undineWater = (() => {
    const VERTEX = `#version 300 es
void main() {
    vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}`;
    // Brightness on the floor is how much surface each texel's light came from: the area of the surface per texel
    // of the map, from the derivatives of the entry point, over the same for a flat surface. Folds add up.
    const AREA_FS = `#version 300 es
precision highp float;
precision highp int;
uniform float uTexelArea;      // metres² of the rest plane under one texel of the map
uniform int uChannel;
in vec2 vSurface;
in float vWeight;
out vec4 outColour;
void main() {
    vec2 dx = dFdx(vSurface), dy = dFdy(vSurface);
    float area = abs(dx.x * dy.y - dx.y * dy.x);
    float energy = vWeight * area / uTexelArea;
    outColour = vec4(uChannel == 0 ? energy : 0.0, uChannel == 1 ? energy : 0.0, uChannel == 2 ? energy : 0.0, 1.0);
}`;
    // The pool's field mirrored into four: even across each wall, so the walls reflect and the transform is periodic.
    const EXTEND_FS = `#version 300 es
precision highp float;
precision highp int;
uniform sampler2D uState;
out vec2 outValue;
void main() {
    ivec2 size = textureSize(uState, 0);
    ivec2 p = ivec2(gl_FragCoord.xy);
    ivec2 q = ivec2(p.x < size.x ? p.x : 2 * size.x - 1 - p.x, p.y < size.y ? p.y : 2 * size.y - 1 - p.y);
    outValue = texelFetch(uState, q, 0).rg;
}`;
    const TAN_HALF = Math.tan(40 * Math.PI / 360);
    const CAUSTIC_SIZE = 512;
    // The caustic map covers the floor and a margin around it, so light bound for the walls is kept.
    const CAUSTIC_MARGIN = 1.5;
    // The deck stands this far above the water at rest, so the walls show dry above the waterline.
    const RIM = 0.12;
    const views = new Map();
    let sources = null;

    async function loadSources() {
        if (sources) return sources;
        const [sim, fft, evolve, caustic, render] = await Promise.all([
            fetch("shaders/undine-water-sim.frag.glsl").then(r => r.text()),
            fetch("shaders/undine-water-fft.frag.glsl").then(r => r.text()),
            fetch("shaders/undine-water-evolve.frag.glsl").then(r => r.text()),
            fetch("shaders/undine-water-caustics.vert.glsl").then(r => r.text()),
            fetch("shaders/undine-water.frag.glsl").then(r => r.text()),
        ]);
        sources = { sim, fft, evolve, caustic, render };
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

    // Float textures are read with NEAREST: linear filtering of floats is an extension some GPUs lack, and a texture
    // filtered that way without it is incomplete and reads as zero. The shaders interpolate by hand.
    function makeTarget(gl, size, complex) {
        const texture = gl.createTexture();
        gl.bindTexture(gl.TEXTURE_2D, texture);
        if (complex) gl.texImage2D(gl.TEXTURE_2D, 0, gl.RG32F, size, size, 0, gl.RG, gl.FLOAT, null);
        else gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA32F, size, size, 0, gl.RGBA, gl.FLOAT, null);
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

    async function setup(canvas) {
        const gl = canvas.getContext("webgl2", { antialias: false, alpha: false, preserveDrawingBuffer: true });
        if (!gl || !gl.getExtension("EXT_color_buffer_float")) return null;
        const src = await loadSources();
        const view = {
            canvas, gl,
            sim: link(gl, VERTEX, src.sim),
            extend: link(gl, VERTEX, EXTEND_FS),
            fft: link(gl, VERTEX, src.fft),
            evolve: link(gl, VERTEX, src.evolve),
            caustic: link(gl, src.caustic, AREA_FS),
            render: link(gl, VERTEX, src.render),
            spec: null, cells: 0, a: null, b: null, sa: null, sb: null, causticMap: null,
            yaw: 0.35, pitch: 0.30, distance: 4.8,
            orbit: false, touch: null, drops: [], lastTime: 0,
            dragging: false, lastX: 0, lastY: 0, pointers: new Map(), pinch: 0,
            frame: 0, fallback: 0, frames: 0, fpsTime: 0, fps: 0, gust: 1, gustClock: 0,
        };
        attach(view);
        return view;
    }

    function surfaces(view) {
        const { gl, spec } = view;
        if (view.cells === spec.cells && view.a) return;
        view.cells = spec.cells;
        view.a = makeTarget(gl, spec.cells);
        view.b = makeTarget(gl, spec.cells);
        view.sa = makeTarget(gl, 2 * spec.cells, true);
        view.sb = makeTarget(gl, 2 * spec.cells, true);
        view.causticMap = view.causticMap || makeTarget(gl, CAUSTIC_SIZE);
    }

    const cellSize = spec => spec.side / spec.cells;
    function lightDirection(spec) {
        const az = spec.lampAzimuth * Math.PI / 180, el = spec.lampElevation * Math.PI / 180;
        return [-Math.sin(az) * Math.cos(el), -Math.sin(el), -Math.cos(az) * Math.cos(el)];
    }

    // One full-screen pass from one texture into another target.
    function pass(gl, program, input, target, size, setUniforms) {
        gl.useProgram(program.program);
        gl.bindFramebuffer(gl.FRAMEBUFFER, target.fbo);
        gl.viewport(0, 0, size, size);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, input.texture);
        setUniforms(program.u);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
    }

    // One step of the surface, exact for the linear waves whatever dt is: mirror, transform, advance each mode by
    // the liquid's dispersion relation, transform back, then the forces that are not waves.
    function stepSurface(view, seconds) {
        const { gl, spec } = view;
        const n = spec.cells, m = 2 * n;
        gl.disable(gl.BLEND);
        pass(gl, view.extend, view.a, view.sa, m, u => gl.uniform1i(u.uState, 0));
        const transform = inverse => {
            for (let axis = 0; axis < 2; axis++) {
                for (let span = 1; span < m; span *= 2) {
                    pass(gl, view.fft, view.sa, view.sb, m, u => {
                        gl.uniform1i(u.uInput, 0);
                        gl.uniform1i(u.uSpan, span);
                        gl.uniform1i(u.uAxis, axis);
                        gl.uniform1i(u.uInverse, inverse ? 1 : 0);
                    });
                    [view.sa, view.sb] = [view.sb, view.sa];
                }
            }
        };
        transform(false);
        pass(gl, view.evolve, view.sa, view.sb, m, u => {
            gl.uniform1i(u.uSpectrum, 0);
            gl.uniform1f(u.uCell, cellSize(spec));
            gl.uniform1f(u.uDepth, spec.depth);
            gl.uniform1f(u.uTension, spec.tensionOverDensity);
            gl.uniform1f(u.uViscosity, spec.kinematicViscosity);
            gl.uniform1f(u.uDamping, spec.damping);
            gl.uniform1f(u.uDt, seconds);
        });
        [view.sa, view.sb] = [view.sb, view.sa];
        transform(true);
        const now = performance.now();
        view.drops = view.drops.filter(d => d.until > now);
        const push = view.touch || view.drops[0];
        pass(gl, view.sim, view.sa, view.b, n, u => {
            gl.uniform1i(u.uSpectral, 0);
            gl.uniform1f(u.uCell, cellSize(spec));
            gl.uniform1f(u.uDt, seconds);
            gl.uniform1f(u.uWind, spec.wind * view.gust);
            gl.uniform2f(u.uSeed, Math.random() * 100, Math.random() * 100);
            if (push) {
                gl.uniform2f(u.uTouch, push.u, push.v);
                gl.uniform1f(u.uTouchRadius, Math.max(1.5, spec.touchRadius / cellSize(spec)));
                gl.uniform1f(u.uTouchDepth, spec.touch * (push.strength || 1));
            } else {
                gl.uniform2f(u.uTouch, -1, -1);
                gl.uniform1f(u.uTouchRadius, 1);
                gl.uniform1f(u.uTouchDepth, 0);
            }
        });
        [view.a, view.b] = [view.b, view.a];
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
    }

    // Wind comes in gusts: a slow random envelope, so the pool has calm spells and rippled ones.
    function gustEnvelope(view, seconds) {
        view.gustClock = (view.gustClock || 0) + seconds;
        const t = view.gustClock;
        const e = 0.5 + 0.5 * Math.sin(t * 0.37 + 1.3) * Math.sin(t * 0.11) + 0.25 * Math.sin(t * 0.83 + 0.4);
        view.gust = Math.max(0, Math.min(1, e));
    }

    function buildCaustics(view) {
        const { gl, spec, caustic } = view;
        const grid = Math.min(513, spec.cells + 1);
        gl.useProgram(caustic.program);
        gl.bindFramebuffer(gl.FRAMEBUFFER, view.causticMap.fbo);
        gl.viewport(0, 0, CAUSTIC_SIZE, CAUSTIC_SIZE);
        gl.clearColor(0, 0, 0, 0);
        gl.clear(gl.COLOR_BUFFER_BIT);
        gl.enable(gl.BLEND);
        gl.blendFunc(gl.ONE, gl.ONE);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, view.a.texture);
        gl.uniform1i(caustic.u.uState, 0);
        gl.uniform1f(caustic.u.uCell, cellSize(spec));
        gl.uniform1f(caustic.u.uDepth, spec.depth);
        gl.uniform3fv(caustic.u.uLight, lightDirection(spec));
        gl.uniform3fv(caustic.u.uIor, spec.ior);
        gl.uniform1i(caustic.u.uGrid, grid);
        gl.uniform1f(caustic.u.uMargin, CAUSTIC_MARGIN);
        gl.uniform1f(caustic.u.uTexelArea, (spec.side * CAUSTIC_MARGIN / CAUSTIC_SIZE) ** 2);
        for (let channel = 0; channel < 3; channel++) {
            gl.uniform1i(caustic.u.uChannel, channel);
            gl.drawArrays(gl.TRIANGLES, 0, (grid - 1) * (grid - 1) * 6);
        }
        gl.disable(gl.BLEND);
        gl.bindFramebuffer(gl.FRAMEBUFFER, null);
        // The area ratio is one on a flat surface by construction.
        return 1;
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

    // Where a point on the canvas meets the rest plane, in cells; null when it misses the pool.
    function surfaceCell(view, px, py) {
        const { eye, forward, right, up } = camera(view);
        const w = view.canvas.clientWidth, h = view.canvas.clientHeight;
        const sx = (px / w * 2 - 1) * TAN_HALF * (w / h), sy = (1 - py / h * 2) * TAN_HALF;
        const d = normalize([forward[0] + right[0] * sx + up[0] * sy, forward[1] + right[1] * sx + up[1] * sy, forward[2] + right[2] * sx + up[2] * sy]);
        if (d[1] >= -1e-4) return null;
        const t = -eye[1] / d[1];
        const x = eye[0] + d[0] * t, z = eye[2] + d[2] * t;
        const half = view.spec.side / 2;
        if (Math.abs(x) > half || Math.abs(z) > half) return null;
        return { u: (x / view.spec.side + 0.5) * view.spec.cells, v: (z / view.spec.side + 0.5) * view.spec.cells };
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

    function draw(view, norm) {
        const { gl, spec, render, canvas } = view;
        fit(view);
        const { eye, forward, right, up } = camera(view);
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
        gl.bindTexture(gl.TEXTURE_2D, view.a.texture);
        gl.uniform1i(render.u.uState, 0);
        gl.activeTexture(gl.TEXTURE1);
        gl.bindTexture(gl.TEXTURE_2D, view.causticMap.texture);
        gl.uniform1i(render.u.uCaustic, 1);
        gl.uniform1f(render.u.uCausticNorm, norm);
        gl.uniform1f(render.u.uMargin, CAUSTIC_MARGIN);
        gl.uniform1f(render.u.uSide, spec.side);
        gl.uniform1f(render.u.uDepth, spec.depth);
        gl.uniform1f(render.u.uRim, RIM);
        gl.uniform3fv(render.u.uIor, spec.ior);
        gl.uniform3fv(render.u.uAlpha, spec.alpha);
        gl.uniform3fv(render.u.uLight, lightDirection(spec));
        gl.uniform1f(render.u.uLampStrength, spec.lamp);
        gl.uniform1i(render.u.uFloor, spec.floor);
        gl.uniform1f(render.u.uExposure, spec.exposure);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
    }

    // One frame per animation frame; a timer stands in when the tab is hidden and frames do not come.
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
        const seconds = view.lastTime ? Math.min(0.05, (time - view.lastTime) / 1000) : 1 / 60;
        view.lastTime = time;
        surfaces(view);
        gustEnvelope(view, seconds);
        stepSurface(view, seconds);
        const norm = buildCaustics(view);
        draw(view, norm);
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
        c.addEventListener("pointerdown", e => {
            if (e.button !== 0 || !view.spec) return;
            c.setPointerCapture(e.pointerId);
            view.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
            if (view.pointers.size === 2) {
                view.touch = null;
                view.dragging = false;
                const p = [...view.pointers.values()];
                view.pinch = Math.hypot(p[0].x - p[1].x, p[0].y - p[1].y);
                return;
            }
            const rect = c.getBoundingClientRect();
            if (view.orbit) {
                view.dragging = true;
                view.lastX = e.clientX;
                view.lastY = e.clientY;
            } else {
                view.touch = surfaceCell(view, e.clientX - rect.left, e.clientY - rect.top);
            }
        });
        c.addEventListener("pointermove", e => {
            if (!view.spec) return;
            if (view.pointers.has(e.pointerId)) view.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
            if (view.pointers.size === 2) {
                const p = [...view.pointers.values()];
                const d = Math.hypot(p[0].x - p[1].x, p[0].y - p[1].y);
                if (view.pinch > 0 && d > 0) {
                    view.distance = Math.min(16, Math.max(2, view.distance * view.pinch / d));
                    view.pinch = d;
                }
                return;
            }
            const rect = c.getBoundingClientRect();
            if (view.dragging) {
                view.yaw -= (e.clientX - view.lastX) * 0.008;
                view.pitch = Math.min(1.5, Math.max(0.08, view.pitch + (e.clientY - view.lastY) * 0.008));
                view.lastX = e.clientX;
                view.lastY = e.clientY;
            } else if (view.touch) {
                view.touch = surfaceCell(view, e.clientX - rect.left, e.clientY - rect.top);
            }
        });
        const release = e => {
            view.pointers.delete(e.pointerId);
            if (view.pointers.size === 0) {
                view.touch = null;
                view.dragging = false;
                view.pinch = 0;
            }
        };
        c.addEventListener("pointerup", release);
        c.addEventListener("pointercancel", release);
        c.addEventListener("wheel", e => {
            if (!(e.ctrlKey || e.metaKey)) return;
            e.preventDefault();
            view.distance = Math.min(16, Math.max(2, view.distance * Math.exp(e.deltaY * 0.0012)));
        }, { passive: false });
        if (typeof ResizeObserver !== "undefined") {
            new ResizeObserver(() => fit(view)).observe(c);
        }
    }

    return {
        // Returns false without WebGL2 float targets; the page then says so instead of showing a still.
        async render(id, spec) {
            const canvas = document.getElementById(id);
            if (!canvas) return false;
            let view = views.get(canvas);
            if (!view) {
                try {
                    view = await setup(canvas);
                } catch (error) {
                    console.error("undine: shader failed to build", error);
                    return false;
                }
                if (!view) return false;
                views.set(canvas, view);
                // Three drops at the start so the water is never still.
                for (const delay of [300, 900, 1700]) setTimeout(() => this.drop(id), delay);
            }
            const restart = !view.spec || view.spec.cells !== spec.cells;
            view.spec = spec;
            if (restart) view.a = null;
            schedule(view);
            return true;
        },
        drop(id) {
            const view = views.get(document.getElementById(id));
            if (!view || !view.spec) return;
            // A drop is a short, hard push: a few touches' worth in a tenth of a second.
            view.drops.push({ u: view.spec.cells * (0.25 + 0.5 * Math.random()), v: view.spec.cells * (0.25 + 0.5 * Math.random()), strength: 3, until: performance.now() + 100 });
        },
        calm(id) {
            const view = views.get(document.getElementById(id));
            if (view) view.a = null;
        },
        orbit(id, on) {
            const view = views.get(document.getElementById(id));
            if (view) view.orbit = !!on;
        },
        dolly(id, factor) {
            const view = views.get(document.getElementById(id));
            if (view) view.distance = Math.min(16, Math.max(2, view.distance * factor));
        },
        fps(id) {
            const view = views.get(document.getElementById(id));
            return view ? view.fps : 0;
        },
        // One frame rendered now and returned as a PNG data URL; for checking the picture without a screen.
        snapshot(id, width, height, seconds) {
            const view = views.get(document.getElementById(id));
            if (!view || !view.spec) return null;
            view.forceSize = [width || 1280, height || 800];
            surfaces(view);
            for (let i = 0; i < Math.round((seconds || 0) * 60); i++) { gustEnvelope(view, 1 / 60); stepSurface(view, 1 / 60); }
            frame(view, view.lastTime + 1000 / 60);
            const url = view.canvas.toDataURL("image/png");
            view.forceSize = null;
            return url;
        },
    };
})();
