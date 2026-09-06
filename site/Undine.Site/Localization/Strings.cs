namespace Undine.Site.Localization;

/// <summary>Every string the site shows, English and Russian. Numbers are never typed here; the pages compute them.</summary>
public static class Strings
{
    static Strings()
    {
        foreach ((string key, (string, string) pair) in GiveStrings.Table)
        {
            Table[key] = pair;
        }
    }

    public static readonly Dictionary<string, (string En, string Ru)> Table = new(StringComparer.Ordinal)
    {
        // Layout
        ["nav.water"] = ("Water", "Water"),
        ["nav.liquids"] = ("Liquids", "Liquids"),
        ["nav.give"] = ("Give Me!", "Give Me!"),
        ["nav.about"] = ("About", "О проекте"),
        ["footer.by"] = ("Made by", "Автор:"),
        ["footer.data"] = ("Optics: Caustikon and RefractiveIndex.INFO, CC0 1.0. Mechanics: CRC Handbook, 20 °C.", "Оптика: Caustikon и RefractiveIndex.INFO, CC0 1.0. Механика: справочник CRC, 20 °C."),
        ["ui.more"] = ("More settings", "Ещё настройки"),
        ["ui.copy"] = ("Copy", "Копировать"),
        ["notfound.title"] = ("No page here", "Такой страницы нет"),
        ["notfound.text"] = ("The address does not match a page of this site. <a href=\"\">Start over</a>.", "Адрес не соответствует ни одной странице. <a href=\"\">На главную</a>."),

        // Water (home)
        ["home.title"] = ("Undine", "Undine"),
        ["home.meta"] = ("Water that moves on the GPU, lit with Caustikon's optics: the long-wave equation with the liquid's viscosity, exact Fresnel, absorption from the k table, caustics from refracted rays.", "Вода, которая движется на GPU и светится по оптике Caustikon: уравнение длинных волн с вязкостью жидкости, точный Френель, поглощение из таблицы k, каустики из преломлённых лучей."),
        ["home.h1"] = ("Water that moves, lit the way water is", "Вода, которая движется и светится как вода"),
        ["home.lede"] = ("A pool on the GPU. The surface is the long-wave equation with the liquid's own viscosity; the light through it is Caustikon's optics for that liquid; the lamp's caustics on the floor come from refracted rays, not from a texture. Touch the surface.", "Бассейн на GPU. Поверхность — уравнение длинных волн с вязкостью самой жидкости, свет сквозь неё — оптика Caustikon для этой жидкости, каустики лампы на дне — из преломлённых лучей, а не из текстуры. Тронь поверхность."),
        ["home.hint.touch"] = ("Touch or drag on the water to make waves. Two fingers or Ctrl + wheel bring the camera closer. Turn on Orbit to look around.", "Тронь воду или проведи по ней, чтобы пошли волны. Два пальца или Ctrl + колесо приближают камеру. Включи «Поворот», чтобы осмотреться."),
        ["home.hint.orbit"] = ("Drag to turn the camera. Turn Orbit off to touch the water again.", "Тяни, чтобы повернуть камеру. Выключи «Поворот», чтобы снова трогать воду."),
        ["home.nogl"] = ("This browser has no WebGL2 with float textures, which the simulation needs.", "В этом браузере нет WebGL2 с float-текстурами, которые нужны симуляции."),
        ["home.liquid"] = ("Liquid", "Жидкость"),
        ["home.depth"] = ("Depth", "Глубина"),
        ["home.side"] = ("Pool side", "Сторона бассейна"),
        ["home.floor"] = ("Floor", "Дно"),
        ["home.floor.tiles"] = ("tiles", "плитка"),
        ["home.floor.sand"] = ("sand", "песок"),
        ["home.floor.dark"] = ("dark", "тёмное"),
        ["home.drop"] = ("Drop", "Капля"),
        ["home.calm"] = ("Calm", "Успокоить"),
        ["home.orbit"] = ("Orbit", "Поворот"),
        ["home.lamp.az"] = ("Lamp around", "Лампа по кругу"),
        ["home.lamp.el"] = ("Lamp height", "Высота лампы"),
        ["home.lamp"] = ("Lamp", "Лампа"),
        ["home.exposure"] = ("Exposure", "Экспозиция"),
        ["home.cells"] = ("Grid", "Сетка"),
        ["home.touch"] = ("Touch strength", "Сила касания"),
        ["home.r.speed"] = ("long-wave speed √(g·h)", "скорость длинных волн √(g·h)"),
        ["home.r.ripple"] = ("2 cm ripple speed", "скорость ряби 2 см"),
        ["home.r.fresnel"] = ("reflected looking straight down", "отражается при взгляде сверху"),
        ["home.r.critical"] = ("critical angle inside", "критический угол внутри"),
        ["home.r.visc"] = ("viscosity", "вязкость"),
        ["home.r.colour"] = ("white light after a round trip to the floor", "белый свет после пути до дна и обратно"),
        ["home.r.step"] = ("stable substep", "устойчивый подшаг"),
        ["home.r.fps"] = ("frames per second", "кадров в секунду"),
        ["home.what.h"] = ("What is computed", "Что считается"),
        ["home.what.1"] = ("<strong>The surface</strong> is a height field: each cell keeps a height and a vertical velocity, and each substep applies v += c²∇²h·dt + ν∇²v·dt, h += v·dt. The wave speed c is √(g·depth), the long-wave limit; ν is the liquid's kinematic viscosity, which is why ripples on glycerol die at once and ripples on water ring across the pool. The substep is chosen from the stability bounds of both terms; the readout shows it.", "<strong>Поверхность</strong> — поле высот: у каждой ячейки высота и вертикальная скорость, и каждый подшаг применяет v += c²∇²h·dt + ν∇²v·dt, h += v·dt. Скорость волны c равна √(g·глубина), предел длинных волн; ν — кинематическая вязкость жидкости, поэтому рябь на глицерине гаснет сразу, а на воде звенит через весь бассейн. Подшаг выбирается из условий устойчивости обоих членов; он показан в показаниях."),
        ["home.what.2"] = ("<strong>The light</strong> is Caustikon's: the liquid's dispersion fit gives one index per colour channel, the exact Fresnel equations split each camera ray at the surface, the refracted part is followed to the floor or a wall, and the path back is absorbed by the liquid's own k table. That is why the deep end of a water pool goes blue-green.", "<strong>Свет</strong> — из Caustikon: аппроксимация дисперсии жидкости даёт по показателю на канал, точные уравнения Френеля делят каждый луч камеры на поверхности, преломлённая часть идёт до дна или стены, и обратный путь поглощается по собственной таблице k жидкости. Поэтому глубокий конец бассейна с водой уходит в сине-зелёный."),
        ["home.what.3"] = ("<strong>The caustics</strong> are rays: one per surface cell and colour channel, refracted by the lamp's direction and the local normal, landing on the floor and summed. A flat surface lands every ray in its own cell, so the map reads one; a ripple focuses rays into a bright line and leaves a darker band beside it. The map is rebuilt every frame.", "<strong>Каустики</strong> — лучи: по одному на ячейку поверхности и канал, преломлённые направлением лампы и локальной нормалью, падают на дно и суммируются. Плоская поверхность кладёт каждый луч в свою ячейку, и карта читается как единица; рябь собирает лучи в яркую линию и оставляет рядом тёмную полосу. Карта пересчитывается каждый кадр."),
        ["home.what.4"] = ("<strong>Not modelled yet:</strong> dispersive gravity–capillary waves (the height field runs at one speed), breaking, splashes, foam, floating bodies, light reflected off the floor back up through the surface. Viscous liquids on particles and slime are the next stages.", "<strong>Пока не моделируется:</strong> дисперсионные гравитационно-капиллярные волны (поле высот бежит с одной скоростью), обрушение, брызги, пена, плавающие тела, свет, отражённый от дна обратно через поверхность. Вязкие жидкости на частицах и слайм — следующие этапы."),

        // Liquids
        ["liquids.title"] = ("Liquids — Undine", "Liquids — Undine"),
        ["liquids.meta"] = ("Nine liquids with their optics from Caustikon and their density, viscosity and surface tension from the CRC Handbook.", "Девять жидкостей с оптикой из Caustikon и плотностью, вязкостью и поверхностным натяжением из справочника CRC."),
        ["liquids.h1"] = ("The liquids", "Жидкости"),
        ["liquids.lede"] = ("Optics from Caustikon's catalog, the database's material measurements with a citation each; mechanics from the CRC Handbook at 20 °C. The colours are daylight after 10 cm, 1 m and 10 m, integrated spectrally; a dash means the source has no absorption table.", "Оптика из каталога Caustikon, измерения материалов из базы данных с цитатой на каждое; механика из справочника CRC при 20 °C. Цвета — дневной свет после 10 см, 1 м и 10 м, спектрально проинтегрированный; прочерк означает, что у источника нет таблицы поглощения."),
        ["liquids.col.liquid"] = ("Liquid", "Жидкость"),
        ["liquids.col.n"] = ("n at 610, 550, 465 nm", "n на 610, 550, 465 нм"),
        ["liquids.col.alpha"] = ("α, 1/m", "α, 1/м"),
        ["liquids.col.density"] = ("ρ, kg/m³", "ρ, кг/м³"),
        ["liquids.col.visc"] = ("η, mPa·s", "η, мПа·с"),
        ["liquids.col.tension"] = ("σ, mN/m", "σ, мН/м"),
        ["liquids.col.colours"] = ("10 cm, 1 m, 10 m", "10 см, 1 м, 10 м"),
        ["liquids.col.source"] = ("Optics source", "Источник оптики"),
        ["liquids.open"] = ("Pour it into the pool", "Налить в бассейн"),
        ["liquids.caustikon"] = ("Its page in Caustikon", "Его страница в Caustikon"),
        ["liquids.floor"] = ("Ethanol and ethylene glycol take their k from Sani and Dell'Oro's ellipsometry, whose visible values sit near 10⁻⁷ to 10⁻⁸, the instrument floor rather than the liquid; the darkening after a metre is that floor. Both are clear in a glass. Water's table is Hale and Querry's transmission measurement and is real.", "Этанол и этиленгликоль берут k из эллипсометрии Sani и Dell'Oro, где значения в видимой области около 10⁻⁷…10⁻⁸ это порог прибора, а не жидкость; потемнение после метра и есть этот порог. В стакане оба прозрачны. Таблица воды это измерение пропускания Hale и Querry, и она настоящая."),
        ["liquids.speed.h"] = ("How fast waves run on each", "Как быстро бегут волны на каждой"),
        ["liquids.speed.p"] = ("Phase speed of a wave of the given length on a layer one metre deep, c² = (g/k + σk/ρ)·tanh(kh): long waves are gravity waves and run alike on every liquid; short ripples are surface-tension waves and run faster on water than on ethanol. Viscosity does not change the speed, only how fast the wave dies.", "Фазовая скорость волны данной длины на слое глубиной метр, c² = (g/k + σk/ρ)·tanh(kh): длинные волны — гравитационные и бегут одинаково на всех жидкостях; короткая рябь — капиллярная и на воде бежит быстрее, чем на этаноле. Вязкость скорость не меняет, только то, как быстро волна гаснет."),
        ["liquids.col.wave"] = ("Wave length", "Длина волны"),

        // About
        ["about.title"] = ("About — Undine", "О проекте — Undine"),
        ["about.meta"] = ("What Undine is, how the water is computed, where the numbers come from, how to install the package, what is out of scope.", "Что такое Undine, как считается вода, откуда числа, как поставить пакет, что вне рамок."),
        ["about.h1"] = ("About", "О проекте"),
        ["about.lede"] = ("Liquids for design, games, 3D and .NET. Water first; viscous liquids and slime to follow. The light comes from Caustikon, the motion from here.", "Жидкости для дизайна, игр, 3D и .NET. Сначала вода, потом вязкие жидкости и слайм. Свет из Caustikon, движение отсюда."),
        ["about.what.h"] = ("What this is", "Что это"),
        ["about.what.p1"] = ("Undine is the second half of a pair. Caustikon knows what a material does to light: index, dispersion, absorption, colour, each number cited. Undine knows what a liquid does when it moves: a surface as a height field with the liquid's own viscosity, the caustics its ripples throw on the floor, and a table of liquids with their handbook density, viscosity and surface tension. The site runs the same scheme on the GPU that the package runs on the CPU.", "Undine — вторая половина пары. Caustikon знает, что материал делает со светом: показатель, дисперсия, поглощение, цвет, каждое число с источником. Undine знает, что жидкость делает, когда движется: поверхность как поле высот с вязкостью самой жидкости, каустики, которые её рябь бросает на дно, и таблица жидкостей с плотностью, вязкостью и поверхностным натяжением из справочника. Сайт крутит на GPU ту же схему, что пакет на CPU."),
        ["about.map.h"] = ("Where things are", "Что где"),
        ["about.map.water"] = ("The pool: touch it, pick a liquid, set the depth, watch the caustics.", "Бассейн: тронь его, выбери жидкость, задай глубину, смотри каустики."),
        ["about.map.liquids"] = ("The nine liquids with every number and its source, and how fast waves run on each.", "Девять жидкостей с каждым числом и его источником, и как быстро на каждой бегут волны."),
        ["about.map.give"] = ("One page per craft: design, games, 3D, web, .NET. Pick a liquid and get the numbers and the shaders with each value explained.", "По странице на ремесло: дизайн, игры, 3D, веб, .NET. Выбери жидкость и получи числа и шейдеры с пояснением каждого значения."),
        ["about.install.h"] = ("Install", "Установка"),
        ["about.install.p"] = ("The package targets .NET 8 and .NET 10 and depends only on Caustikon. Until it is on NuGet, clone with submodules and reference the project.", "Пакет нацелен на .NET 8 и .NET 10 и зависит только от Caustikon. Пока его нет на NuGet, клонируй с подмодулями и подключай проект."),
        ["about.model.h"] = ("What is modelled, and what is not", "Что моделируется, а что нет"),
        ["about.model.p"] = ("The surface is a height field: v += c²∇²h·dt + ν∇²v·dt, h += v·dt, with c = √(g·depth) and ν = η/ρ; the substep is bounded by both the wave and the diffusion stability limits. Light: exact Fresnel per channel, Snell's law per channel, Beer–Lambert absorption over the path, caustics by ray counting. Not modelled yet: dispersive gravity–capillary waves (the package gives the true phase speed, the field runs at one), breaking, splashes, foam, floating bodies, light reflected off the floor back up through the surface.", "Поверхность — поле высот: v += c²∇²h·dt + ν∇²v·dt, h += v·dt, где c = √(g·глубина) и ν = η/ρ; подшаг ограничен условиями устойчивости и волны, и диффузии. Свет: точный Френель по каналам, закон Снеллиуса по каналам, поглощение Бера–Ламберта вдоль пути, каустики подсчётом лучей. Пока не моделируется: дисперсионные гравитационно-капиллярные волны (пакет даёт настоящую фазовую скорость, поле бежит с одной), обрушение, брызги, пена, плавающие тела, свет, отражённый от дна обратно через поверхность."),
        ["about.data.h"] = ("Where the numbers come from", "Откуда числа"),
        ["about.data.p"] = ("Optics: Caustikon's liquids, generated from the RefractiveIndex.INFO database (CC0 1.0) with a citation for each entry; water is the Daimon and Masumura 2007 fit with the Hale and Querry 1973 absorption table. Mechanics: CRC Handbook of Chemistry and Physics, 97th ed., values at 20 °C, rounded.", "Оптика: жидкости Caustikon, сгенерированные из базы RefractiveIndex.INFO (CC0 1.0) с цитатой на каждую запись; вода — аппроксимация Даймона и Масумуры 2007 с таблицей поглощения Хейла и Куэрри 1973. Механика: справочник CRC по химии и физике, 97-е изд., значения при 20 °C, округлённые."),
        ["about.next.h"] = ("Next", "Дальше"),
        ["about.next.p"] = ("Viscous liquids on particles (oil, honey), then slime as a viscoelastic material. Both will sit in this repository beside the water.", "Вязкие жидкости на частицах (масло, мёд), потом слайм как вязкоупругий материал. Оба будут в этом же репозитории рядом с водой."),
        ["about.author.h"] = ("Author", "Автор"),
        ["about.author.p"] = ("Made by <a href=\"https://github.com/levvs-one\" rel=\"noopener\">levvs-one</a>. Source, issues and releases live in the <a href=\"https://github.com/levvs-one/undine\" rel=\"noopener\">repository</a>; the light comes from <a href=\"https://caustikon.levvs.cc\" rel=\"noopener\">Caustikon</a>.", "Автор — <a href=\"https://github.com/levvs-one\" rel=\"noopener\">levvs-one</a>. Исходники, задачи и релизы — в <a href=\"https://github.com/levvs-one/undine\" rel=\"noopener\">репозитории</a>; свет — из <a href=\"https://caustikon.levvs.cc\" rel=\"noopener\">Caustikon</a>."),
    };
}
