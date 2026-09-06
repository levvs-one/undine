// The page's words in English and Russian; the language is remembered, English until chosen.
window.undineStrings = (() => {
    const table = {
        "nav.water": ["Water", "Вода"],
        "nav.liquids": ["Liquids", "Жидкости"],
        "nav.shaders": ["Shaders", "Шейдеры"],
        "nav.dotnet": [".NET", ".NET"],
        "h1": ["Water that moves, lit the way water is", "Вода, которая движется и светится как вода"],
        "lede": ["A pool on the GPU: the surface is the long-wave equation with the liquid's own viscosity, the light through it is Caustikon's optics for that liquid, and the lamp's caustics on the floor come from refracted rays, not a texture. Touch the surface.", "Бассейн на GPU: поверхность — уравнение длинных волн с вязкостью самой жидкости, свет сквозь неё — оптика Caustikon для этой жидкости, каустики лампы на дне — из преломлённых лучей, а не из текстуры. Тронь поверхность."],
        "hint.touch": ["Touch or drag on the water to make waves. Two fingers or Ctrl + wheel bring the camera closer. Turn on Orbit to look around.", "Тронь воду или проведи по ней, чтобы пошли волны. Два пальца или Ctrl + колесо приближают камеру. Включи «Поворот», чтобы осмотреться."],
        "hint.orbit": ["Drag to turn the camera. Turn Orbit off to touch the water again.", "Тяни, чтобы повернуть камеру. Выключи «Поворот», чтобы снова трогать воду."],
        "nogl": ["This browser has no WebGL2 with float textures, which the simulation needs.", "В этом браузере нет WebGL2 с float-текстурами, которые нужны симуляции."],
        "c.liquid": ["Liquid", "Жидкость"],
        "c.depth": ["Depth", "Глубина"],
        "c.side": ["Pool side", "Сторона бассейна"],
        "c.floor": ["Floor", "Дно"],
        "c.floor.tiles": ["tiles", "плитка"],
        "c.floor.sand": ["sand", "песок"],
        "c.floor.dark": ["dark", "тёмное"],
        "c.drop": ["Drop", "Капля"],
        "c.calm": ["Calm", "Успокоить"],
        "c.orbit": ["Orbit", "Поворот"],
        "c.more": ["More settings", "Ещё настройки"],
        "c.lamp.az": ["Lamp around", "Лампа по кругу"],
        "c.lamp.el": ["Lamp height", "Высота лампы"],
        "c.lamp": ["Lamp", "Лампа"],
        "c.exposure": ["Exposure", "Экспозиция"],
        "c.cells": ["Grid", "Сетка"],
        "r.speed": ["long-wave speed √(g·h)", "скорость длинных волн √(g·h)"],
        "r.ripple": ["2 cm ripple speed", "скорость ряби 2 см"],
        "r.fresnel": ["reflected looking straight down", "отражается при взгляде сверху"],
        "r.critical": ["critical angle inside", "критический угол внутри"],
        "r.visc": ["viscosity", "вязкость"],
        "r.colour": ["white light after a round trip to the floor", "белый свет после пути до дна и обратно"],
        "r.step": ["stable substep", "устойчивый подшаг"],
        "r.fps": ["frames per second", "кадров в секунду"],
        "what.h": ["What is computed", "Что считается"],
        "what.1": ["<strong>The surface</strong> is a height field: each cell keeps a height and a vertical velocity, and each substep applies v += c²∇²h·dt + ν∇²v·dt, h += v·dt. The wave speed c is √(g·depth), the long-wave limit; ν is the liquid's kinematic viscosity from the handbook table below, which is why ripples on glycerol die at once and ripples on water ring across the pool. The substep is chosen from the stability bounds of both terms; the readout shows it.", "<strong>Поверхность</strong> — поле высот: у каждой ячейки высота и вертикальная скорость, и каждый подшаг применяет v += c²∇²h·dt + ν∇²v·dt, h += v·dt. Скорость волны c равна √(g·глубина), предел длинных волн; ν — кинематическая вязкость жидкости из таблицы ниже, поэтому рябь на глицерине гаснет сразу, а на воде звенит через весь бассейн. Подшаг выбирается из условий устойчивости обоих членов; он показан в показаниях."],
        "what.2": ["<strong>The light</strong> is Caustikon's: the liquid's dispersion fit gives one index per colour channel, the exact Fresnel equations split each camera ray at the surface, the refracted part is followed to the floor, and the path back is absorbed by the liquid's own k table. That is why the deep end of a water pool goes blue-green and a pool of carbon disulfide would not.", "<strong>Свет</strong> — из Caustikon: аппроксимация дисперсии жидкости даёт по показателю на канал, точные уравнения Френеля делят каждый луч камеры на поверхности, преломлённая часть идёт до дна, и обратный путь поглощается по собственной таблице k жидкости. Поэтому глубокий конец бассейна с водой уходит в сине-зелёный, а бассейн с сероуглеродом — нет."],
        "what.3": ["<strong>The caustics</strong> are rays: one per surface cell and colour channel, refracted by the lamp's direction and the local normal, landing on the floor and summed. A flat surface lands every ray in its own cell, so the map reads one; a ripple focuses rays into a bright line and leaves a darker band beside it. The map is rebuilt every frame from the current surface.", "<strong>Каустики</strong> — лучи: по одному на ячейку поверхности и канал, преломлённые направлением лампы и локальной нормалью, падают на дно и суммируются. Плоская поверхность кладёт каждый луч в свою ячейку, и карта читается как единица; рябь собирает лучи в яркую линию и оставляет рядом тёмную полосу. Карта пересчитывается каждый кадр по текущей поверхности."],
        "what.4": ["<strong>Not modelled yet:</strong> dispersive gravity–capillary waves (the height field runs at one speed), breaking, splashes, foam, floating bodies, and the light that reflects off the floor back up through the surface. Viscous liquids and slime are the next stages.", "<strong>Пока не моделируется:</strong> дисперсионные гравитационно-капиллярные волны (поле высот бежит с одной скоростью), обрушение, брызги, пена, плавающие тела и свет, отражённый от дна обратно через поверхность. Вязкие жидкости и слайм — следующие этапы."],
        "liquids.h": ["The liquids", "Жидкости"],
        "liquids.p": ["Optics from Caustikon's catalog (the database's material measurements, CC0), mechanics from the CRC Handbook at 20 °C. The colours are daylight after 10 cm, 1 m and 10 m of the liquid, integrated spectrally by Caustikon; a dash means the source has no absorption table.", "Оптика из каталога Caustikon (измерения материалов из базы данных, CC0), механика из справочника CRC при 20 °C. Цвета — дневной свет после 10 см, 1 м и 10 м жидкости, спектрально проинтегрированный Caustikon; прочерк означает, что у источника нет таблицы поглощения."],
        "col.liquid": ["Liquid", "Жидкость"],
        "col.n": ["n at 610, 550, 465 nm", "n на 610, 550, 465 нм"],
        "col.alpha": ["α, 1/m", "α, 1/м"],
        "col.density": ["ρ, kg/m³", "ρ, кг/м³"],
        "col.visc": ["η, mPa·s", "η, мПа·с"],
        "col.tension": ["σ, mN/m", "σ, мН/м"],
        "col.colours": ["10 cm, 1 m, 10 m", "10 см, 1 м, 10 м"],
        "col.source": ["Optics source", "Источник оптики"],
        "shaders.h": ["The shaders", "Шейдеры"],
        "shaders.p": ["Three GLSL ES 3.00 files run this page and are what you download: the surface step, the caustic points and the render. The HLSL and Godot files carry the render side for engines that bring their own height texture; the constants they need per liquid are in the table above and in liquids.json.", "Три файла GLSL ES 3.00 крутят эту страницу, и их же ты скачиваешь: шаг поверхности, точки каустик и рендер. Файлы HLSL и Godot несут рендерную часть для движков со своей текстурой высот; константы для каждой жидкости — в таблице выше и в liquids.json."],
        "shaders.sim": ["surface step, fragment", "шаг поверхности, фрагментный"],
        "shaders.caustic": ["caustic points, vertex", "точки каустик, вершинный"],
        "shaders.render": ["pool render, fragment", "рендер бассейна, фрагментный"],
        "shaders.hlsl": ["Unity, Unreal: surface shading", "Unity, Unreal: шейдинг поверхности"],
        "shaders.godot": ["Godot 4: surface shading", "Godot 4: шейдинг поверхности"],
        "shaders.data": ["every liquid's numbers", "числа всех жидкостей"],
        "dotnet.h": ["The same on the processor", "То же на процессоре"],
        "dotnet.p": ["The Undine package runs the same scheme in C#: the surface, the caustic map and the liquid table, with the optics from Caustikon. Tools, tests and engines that want the heights on the CPU take it from there.", "Пакет Undine крутит ту же схему на C#: поверхность, карту каустик и таблицу жидкостей с оптикой из Caustikon. Инструменты, тесты и движки, которым нужны высоты на CPU, берут её оттуда."],
        "footer.by": ["Made by", "Автор:"],
        "footer.data": ["Optics: Caustikon and RefractiveIndex.INFO, CC0 1.0. Mechanics: CRC Handbook, 20 °C.", "Оптика: Caustikon и RefractiveIndex.INFO, CC0 1.0. Механика: справочник CRC, 20 °C."],
        "next.h": ["Next", "Дальше"],
        "next.p": ["Viscous liquids on particles (oil, honey), then slime as a viscoelastic material. Both will sit in this repository beside the water.", "Вязкие жидкости на частицах (масло, мёд), потом слайм как вязкоупругий материал. Оба будут в этом же репозитории рядом с водой."],
    };
    const liquidNames = {
        "Water": ["Water", "Вода"], "Ethanol": ["Ethanol", "Этанол"], "Methanol": ["Methanol", "Метанол"], "Acetone": ["Acetone", "Ацетон"],
        "Glycerol": ["Glycerol", "Глицерин"], "Ethylene glycol": ["Ethylene glycol", "Этиленгликоль"], "Benzene": ["Benzene", "Бензол"],
        "Toluene": ["Toluene", "Толуол"], "Carbon disulfide": ["Carbon disulfide", "Сероуглерод"],
    };
    let code = "en";
    try { code = localStorage.getItem("undine.lang") === "ru" ? "ru" : "en"; } catch { }
    const api = {
        get code() { return code; },
        t(key) { const pair = table[key]; return pair ? pair[code === "ru" ? 1 : 0] : key; },
        liquidName(name) { const pair = liquidNames[name]; return pair ? pair[code === "ru" ? 1 : 0] : name; },
        number(value, decimals) {
            const text = Number(value).toFixed(decimals);
            return code === "ru" ? text.replace(".", ",") : text;
        },
        set(next) {
            code = next === "ru" ? "ru" : "en";
            try { localStorage.setItem("undine.lang", code); } catch { }
            api.apply();
        },
        apply() {
            document.documentElement.lang = code;
            for (const el of document.querySelectorAll("[data-i18n]")) {
                const key = el.getAttribute("data-i18n");
                if (el.hasAttribute("data-i18n-html")) el.innerHTML = api.t(key); else el.textContent = api.t(key);
            }
            for (const el of document.querySelectorAll("[data-lang]")) {
                el.classList.toggle("active", el.getAttribute("data-lang") === code);
            }
            document.dispatchEvent(new CustomEvent("undine-language"));
        },
    };
    return api;
})();
