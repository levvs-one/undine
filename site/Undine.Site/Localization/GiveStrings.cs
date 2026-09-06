namespace Undine.Site.Localization;

/// <summary>Text of the "Give Me!" pages: one per craft. Numbers are never typed here; the page fills them in from the packages.</summary>
public static class GiveStrings
{
    // Example placeholders: {0} liquid, {1} n_d, {2} face reflectance %, {3} depth m, {4} tint hex, {5} viscosity mPa·s,
    // {6} shallow wave speed m/s, {7} critical angle °, {8} speed of a 2 cm ripple m/s.
    public static readonly Dictionary<string, (string En, string Ru)> Table = new(StringComparer.Ordinal)
    {
        ["give.sec.code"] = ("Implementation", "Реализация"),
        ["give.sec.params"] = ("What each value is", "Что означает каждое значение"),
        ["give.sec.files"] = ("The shaders", "Шейдеры"),
        ["give.sec.examples"] = ("Worked examples", "Разобранные примеры"),
        ["give.sec.methods"] = ("What the physics asks for", "Что требует физика"),
        ["give.sec.advice"] = ("Advice", "Советы"),

        ["give.fig.rgb"] = ("n at 610, 550, 465 nm", "n на 610, 550, 465 нм"),
        ["give.fig.face"] = ("reflected looking straight down", "отражается при взгляде сверху"),
        ["give.fig.tint"] = ("white light down to the floor at {0} m and back", "белый свет до дна на {0} м и обратно"),
        ["give.fig.visc"] = ("dynamic viscosity, 20 °C", "динамическая вязкость, 20 °C"),
        ["give.fig.speed"] = ("shallow-water wave speed at this depth", "скорость волны на мелкой воде при этой глубине"),

        ["give.p.tint"] = ("Tint", "Оттенок"),
        ["give.p.face"] = ("Face reflectance", "Отражение поверхности"),
        ["give.p.depth"] = ("Depth", "Глубина"),
        ["give.p.critical"] = ("Critical angle", "Критический угол"),

        ["give.x.noabs"] = ("no measured absorption in the visible", "поглощение в видимой области не измерено"),
        ["give.x.tint"] = ("the colour white light has after going down to the floor and back up through the liquid; the only thing CSS can carry of the liquid's optics.", "цвет, который белый свет приобретает, сходив до дна и обратно через жидкость; единственное, что CSS способен унести из оптики жидкости."),
        ["give.x.face"] = ("the share of light a level surface reflects when looked at straight down; at grazing angles it rises to 100%.", "доля света, которую ровная поверхность отражает при взгляде сверху; при скользящих углах растёт до 100%."),
        ["give.x.ior"] = ("the refractive index at the sodium d line, 587,6 nm; one number for renderers that do not disperse.", "показатель преломления на d-линии натрия, 587,6 нм; одно число для рендеров без дисперсии."),
        ["give.x.abbe"] = ("the Abbe number of the liquid and the dispersion value three.js and glTF use, 20 divided by it. Low Abbe means strong colour fringes.", "число Аббе жидкости и величина dispersion, которую берут three.js и glTF, 20 делённое на него. Малое число Аббе означает сильные цветные каёмки."),
        ["give.x.alpha"] = ("absorption per metre in the three channels; the colour a deep pool takes on is exp(−α·path).", "поглощение на метр в трёх каналах; цвет глубокого бассейна равен exp(−α·путь)."),
        ["give.x.depth"] = ("the distance from the rest surface to the floor. It sets the wave speed and how far light travels through the liquid.", "расстояние от уровня покоя до дна. Задаёт скорость волн и длину пути света в жидкости."),
        ["give.x.rgb"] = ("the refractive index at the three wavelengths the shaders use; the spread between them is the colour fringing on the floor.", "показатель преломления на трёх длинах волн, которые берут шейдеры; разброс между ними и есть цветные каёмки на дне."),
        ["give.x.speed"] = ("√(g·depth): how fast a long wave crosses the pool. The simulation time step must not let a wave cross more than one cell.", "√(g·глубина): скорость длинной волны по бассейну. Шаг симуляции не должен позволять волне пройти больше одной ячейки."),
        ["give.x.visc"] = ("kinematic viscosity: the rate at which ripples smooth out. Water barely damps; glycerol freezes a ripple within a frame.", "кинематическая вязкость: скорость, с которой рябь сглаживается. Вода почти не гасит; глицерин замораживает рябь за кадр."),
        ["give.x.critical"] = ("above this angle from the normal, light inside the liquid cannot leave: the floor seen from below turns into a mirror beyond a circle.", "выше этого угла от нормали свет изнутри жидкости выйти не может: дно, увиденное снизу, за пределами круга становится зеркалом."),

        ["give.file.sim"] = ("The height-field step: one pass over two float textures, viscosity as a Laplacian, touches as Gaussian dents.", "Шаг поля высот: один проход по двум float-текстурам, вязкость как лапласиан, касания как гауссовы вмятины."),
        ["give.file.caustic"] = ("Caustics: one point per cell and channel, refracted through the surface normal and splatted onto the floor.", "Каустики: по точке на ячейку и канал, преломлённой через нормаль поверхности и брошенной на дно."),
        ["give.file.render"] = ("The view: per-channel refraction to the floor, Fresnel reflection of the sky, absorption along the path.", "Вид: преломление к дну по каналам, френелевское отражение неба, поглощение вдоль пути."),
        ["give.file.hlsl"] = ("The same refraction and absorption as one HLSL function for Unity and Unreal.", "То же преломление и поглощение одной функцией HLSL для Unity и Unreal."),
        ["give.file.godot"] = ("Godot 4 spatial shader with the same uniforms.", "Пространственный шейдер Godot 4 с теми же uniform."),

        ["give.fmt.cssurface.name"] = ("C#: surface", "C#: поверхность"),
        ["give.fmt.csoptics.name"] = ("C#: optics", "C#: оптика"),
        ["give.fmt.cscaustics.name"] = ("C#: caustics", "C#: каустики"),

        ["give.fmt.glsl.p"] = ("The constants the three shaders take. The files are linked below; the pool on the front page runs exactly them.", "Константы, которые принимают три шейдера. Файлы ниже; бассейн на главной гоняет ровно их."),
        ["give.fmt.css.p"] = ("CSS has no refraction. What survives is the liquid's tint over the depth and the sheen of the surface; motion needs the canvas.", "В CSS нет преломления. Остаётся оттенок жидкости на этой глубине и блик поверхности; движение требует canvas."),
        ["give.fmt.unity.p"] = ("A displaced plane and a Custom Function node with the HLSL file, or HDRP Lit for a single-index shortcut.", "Смещённая плоскость и узел Custom Function с файлом HLSL, либо HDRP Lit как сокращение с одним показателем."),
        ["give.fmt.unreal.p"] = ("A translucent material with world position offset from the height texture; the Custom node carries the per-channel physics.", "Полупрозрачный материал со смещением из текстуры высот; узел Custom несёт физику по каналам."),
        ["give.fmt.godot.p"] = ("The shader file on a subdivided PlaneMesh; these are the uniforms for this liquid.", "Файл шейдера на подразделённом PlaneMesh; это uniform для данной жидкости."),
        ["give.fmt.hlsl.p"] = ("The constants for the HLSL function, one call per channel.", "Константы для функции HLSL, по вызову на канал."),
        ["give.fmt.blender.p"] = ("Cycles does dispersion and volume absorption natively; the values below reproduce the liquid exactly.", "Cycles умеет дисперсию и объёмное поглощение сам; значения ниже воспроизводят жидкость точно."),
        ["give.fmt.gltf.p"] = ("The transmission, IOR, dispersion and volume extensions; thickness is the depth of the pool.", "Расширения transmission, IOR, dispersion и volume; thickness равен глубине бассейна."),
        ["give.fmt.threejs.p"] = ("MeshPhysicalMaterial with transmission, dispersion and attenuation matching the liquid.", "MeshPhysicalMaterial с transmission, dispersion и затуханием, соответствующими жидкости."),
        ["give.fmt.webgl.p"] = ("The same shaders the pool runs, with the uniforms for this liquid.", "Те же шейдеры, что гоняет бассейн, с uniform для этой жидкости."),
        ["give.fmt.cssurface.p"] = ("The height field on the CPU: stable substeps chosen from the wave speed and viscosity, normals for lighting, heights to upload.", "Поле высот на CPU: устойчивые подшаги из скорости волны и вязкости, нормали для освещения, высоты для загрузки."),
        ["give.fmt.csoptics.p"] = ("Index, absorption, Fresnel and refraction per channel from the Caustikon glass record of the liquid.", "Показатель, поглощение, Френель и преломление по каналам из стеклянной записи Caustikon для жидкости."),
        ["give.fmt.cscaustics.p"] = ("Ray counting onto the floor: the caustic pattern as a texture without a GPU.", "Подсчёт лучей на дне: рисунок каустик как текстура без GPU."),

        // Design
        ["give.design.name"] = ("Design", "Дизайн"),
        ["give.design.tag"] = ("Liquid in interfaces: real tint, real sheen, a live pool in the page.", "Жидкость в интерфейсах: настоящий оттенок, настоящий блик, живой бассейн на странице."),
        ["give.design.h1"] = ("Liquid for design", "Жидкость для дизайна"),
        ["give.design.lede"] = ("A water surface in a page is either a picture or a simulation. The numbers here are what the picture must match and what the simulation must take.", "Водная поверхность на странице это либо картинка, либо симуляция. Числа здесь то, чему картинка должна соответствовать и что симуляция должна принять."),
        ["give.design.examples.1"] = ("<strong>A hero with a pool behind the text.</strong> {0} at {3} m: the floor is seen through <span class=\"swatch\" style=\"background:{4}\"></span> {4}, and the surface throws back {2}% of the sky straight down, more at the far edge. Text stays legible because the floor pattern is low-contrast; the caustics are the motion, not the pattern.", "<strong>Первый экран с бассейном за текстом.</strong> {0} на {3} м: дно видно сквозь <span class=\"swatch\" style=\"background:{4}\"></span> {4}, а поверхность отдаёт назад {2}% неба при взгляде сверху, больше у дальнего края. Текст читается, потому что рисунок дна малоконтрастный; движение это каустики, а не рисунок."),
        ["give.design.examples.2"] = ("<strong>Ripples on a button.</strong> A 2 cm ripple in {0} travels at {8} m/s: on a 300 px button standing for 30 cm it crosses in about a second. Slower reads as syrup, faster as a video played back at double speed.", "<strong>Рябь на кнопке.</strong> Рябь в 2 см по {0} идёт со скоростью {8} м/с: по кнопке 300 px, изображающей 30 см, она проходит примерно за секунду. Медленнее читается как сироп, быстрее как видео на удвоенной скорости."),
        ["give.design.examples.3"] = ("<strong>A still.</strong> Set the touch radius to 3 cm and stop the orbit; download the frame. The fringes on the floor are the three indices doing their work, and they are what a stock picture never has.", "<strong>Стоп-кадр.</strong> Радиус касания 3 см, орбита выключена; сохрани кадр. Каёмки на дне это работа трёх показателей, и их никогда нет на стоковой картинке."),
        ["give.design.methods.1"] = ("Reflection and refraction share every ray: what is not reflected is refracted, and the share moves with the angle. A surface that is equally shiny everywhere is a paint, not a liquid.", "Отражение и преломление делят каждый луч: что не отразилось, то преломилось, и доля движется с углом. Поверхность, одинаково блестящая везде, это краска, а не жидкость."),
        ["give.design.methods.2"] = ("Colour comes from the path, not from a tint layer. Twice the depth is the square of the transmittance, which is why a pool goes from clear to blue rather than from light blue to dark blue.", "Цвет берётся из пути, а не из слоя оттенка. Вдвое большая глубина это квадрат пропускания, поэтому бассейн идёт от прозрачного к синему, а не от светло-синего к тёмно-синему."),
        ["give.design.advice.1"] = ("Keep the floor pattern quiet; the caustics need contrast headroom.", "Держи рисунок дна спокойным; каустикам нужен запас контраста."),
        ["give.design.advice.2"] = ("Match the lamp to the page's light source; a pool lit from the left under a page lit from the right looks pasted.", "Согласуй лампу с источником света страницы; бассейн, освещённый слева под страницей, освещённой справа, выглядит наклеенным."),
        ["give.design.advice.3"] = ("One drop per interaction. Several at once turn into noise within a second.", "Одна капля на взаимодействие. Несколько сразу за секунду превращаются в шум."),

        // Games
        ["give.games.name"] = ("Games", "Игры"),
        ["give.games.tag"] = ("Unity, Unreal, Godot: the water shader with this liquid's numbers.", "Unity, Unreal, Godot: шейдер воды с числами этой жидкости."),
        ["give.games.h1"] = ("Liquid for games", "Жидкость для игр"),
        ["give.games.lede"] = ("Engines ship a water shader; what they do not ship is the liquid. These are the constants that make it {0}, and the shader source to see where each one goes.", "Движки поставляют шейдер воды; чего они не поставляют, так это жидкость. Здесь константы, которые делают её {0}, и исходник шейдера, чтобы видеть, куда идёт каждая."),
        ["give.games.examples.1"] = ("<strong>A pool the player wades through.</strong> {0} at {3} m: a long wave crosses at {6} m/s, so a 20 m pool settles in about {6} seconds of cross-and-back divided by the damping. Viscosity {5} mPa·s decides whether footsteps leave rings or a smear.", "<strong>Бассейн, через который идёт игрок.</strong> {0} на {3} м: длинная волна идёт со скоростью {6} м/с, и 20-метровый бассейн успокаивается за несколько проходов туда и обратно с учётом гашения. Вязкость {5} мПа·с решает, оставляют ли шаги кольца или размазанный след."),
        ["give.games.examples.2"] = ("<strong>Looking up from below.</strong> Beyond {7}° from the vertical the surface is a mirror from underneath: the sky fits in a circle and the rest reflects the floor. An underwater camera that sees sky everywhere is wrong.", "<strong>Взгляд снизу.</strong> За {7}° от вертикали поверхность снизу становится зеркалом: небо помещается в круг, остальное отражает дно. Подводная камера, которая видит небо везде, ошибается."),
        ["give.games.examples.3"] = ("<strong>Blood, oil, slime.</strong> Only the numbers change: index, absorption, viscosity. Take the closest liquid in the table, raise the viscosity, and the same shader stops being water.", "<strong>Кровь, масло, слизь.</strong> Меняются только числа: показатель, поглощение, вязкость. Возьми ближайшую жидкость из таблицы, подними вязкость, и тот же шейдер перестаёт быть водой."),
        ["give.games.methods.1"] = ("The height field is a wave equation with damping; the time step is bounded by the wave speed and the cell size, and by viscosity when it is large. Undine.Surface computes the bound and substeps.", "Поле высот это волновое уравнение с гашением; шаг по времени ограничен скоростью волны и размером ячейки, а при большой вязкости и ею. Undine.Surface считает границу и делит шаг."),
        ["give.games.methods.2"] = ("Caustics are the same refraction seen from the floor: refract one ray per cell through the surface normal and count where it lands. The point splat in the vertex shader does this in one pass.", "Каустики это то же преломление, увиденное со дна: преломи по лучу на ячейку через нормаль поверхности и посчитай, куда он попал. Точечный сплат в вершинном шейдере делает это за один проход."),
        ["give.games.advice.1"] = ("Keep the height texture as a real float; half floats make the surface step visibly in still water.", "Держи текстуру высот настоящим float; половинные float делают поверхность заметно ступенчатой в стоячей воде."),
        ["give.games.advice.2"] = ("Refract screen colour per channel even if you skip dispersion elsewhere; the fringes are cheap and are most of what reads as water.", "Преломляй цвет экрана по каналам, даже если пропускаешь дисперсию в остальном; каёмки дёшевы и составляют большую часть того, что читается как вода."),
        ["give.games.advice.3"] = ("Reserve the substep count for the liquid, not the frame rate: glycerol at 512 cells needs many; water needs one.", "Число подшагов задавай от жидкости, а не от частоты кадров: глицерину на 512 ячейках их нужно много; воде один."),

        // Blender
        ["give.blender.name"] = ("3D and Blender", "3D и Blender"),
        ["give.blender.tag"] = ("Cycles, glTF, three.js: IOR, Abbe, volume absorption for this liquid.", "Cycles, glTF, three.js: IOR, Аббе, объёмное поглощение для этой жидкости."),
        ["give.blender.h1"] = ("Liquid for 3D", "Жидкость для 3D"),
        ["give.blender.lede"] = ("Offline renderers disperse and absorb on their own; they only need the right numbers. These are the numbers for {0}, and what each slider in Blender means physically.", "Офлайновые рендеры сами рассеивают по спектру и поглощают; им нужны только правильные числа. Здесь числа для {0} и что физически значит каждый ползунок в Blender."),
        ["give.blender.examples.1"] = ("<strong>A glass of {0}.</strong> Model the liquid as a closed mesh that overlaps the glass slightly; IOR {1}, Volume Absorption with the density and colour below. Without the volume the liquid is invisible at any depth.", "<strong>Стакан с {0}.</strong> Смоделируй жидкость закрытым мешем, слегка перекрывающим стекло; IOR {1}, Volume Absorption с плотностью и цветом ниже. Без объёма жидкость невидима на любой глубине."),
        ["give.blender.examples.2"] = ("<strong>A pool at {3} m.</strong> The Ocean modifier makes the surface; the water body under it must be closed for absorption. White light returning from the floor is <span class=\"swatch\" style=\"background:{4}\"></span> {4}, which the render must reproduce or the absorption is wrong.", "<strong>Бассейн на {3} м.</strong> Модификатор Ocean делает поверхность; тело воды под ним должно быть закрыто для поглощения. Белый свет, вернувшийся от дна, это <span class=\"swatch\" style=\"background:{4}\"></span> {4}, что рендер обязан воспроизвести, иначе поглощение неверно."),
        ["give.blender.examples.3"] = ("<strong>Caustics on the floor.</strong> Cycles: enable Caustics on the light and Shadow Caustics on the floor, and mark the water as a caustics caster. Path tracing alone loses them in noise.", "<strong>Каустики на дне.</strong> Cycles: включи Caustics на источнике света и Shadow Caustics на дне, отметь воду как caustics caster. Трассировка путей сама по себе теряет их в шуме."),
        ["give.blender.methods.1"] = ("Cycles absorbs at density × (1 − colour) per channel; the pair below is solved from the three absorption coefficients so the exact α comes out.", "Cycles поглощает с плотностью × (1 − цвет) по каналам; пара ниже решена из трёх коэффициентов поглощения так, чтобы вышло точное α."),
        ["give.blender.methods.2"] = ("glTF and three.js express dispersion as 20 divided by the Abbe number; the volume extension expresses absorption as an attenuation colour at a distance, which is exp(−α·distance).", "glTF и three.js выражают дисперсию как 20, делённое на число Аббе; расширение volume выражает поглощение как цвет затухания на расстоянии, то есть exp(−α·расстояние)."),
        ["give.blender.advice.1"] = ("Work in metres. Every absorption number here is per metre; a scene in centimetres absorbs a hundred times too little.", "Работай в метрах. Каждое число поглощения здесь на метр; сцена в сантиметрах поглощает в сто раз меньше."),
        ["give.blender.advice.2"] = ("Roughness zero. Liquids have no microfacets; a rough surface is a wave that the mesh should carry instead.", "Шероховатость ноль. У жидкостей нет микрограней; шероховатая поверхность это волна, которую должен нести меш."),

        // Web
        ["give.web.name"] = ("Web", "Веб"),
        ["give.web.tag"] = ("three.js and raw WebGL: the pool on this site, for your page.", "three.js и чистый WebGL: бассейн с этого сайта для твоей страницы."),
        ["give.web.h1"] = ("Liquid for the web", "Жидкость для веба"),
        ["give.web.lede"] = ("The pool on the front page is three shaders and two float textures. Here are their uniforms for {0} and the material for three.js when a mesh is enough.", "Бассейн на главной это три шейдера и две float-текстуры. Здесь их uniform для {0} и материал для three.js, когда достаточно меша."),
        ["give.web.examples.1"] = ("<strong>A pool behind the page.</strong> 256 cells at 60 fps costs one simulation pass, one caustic splat and one view pass per frame; on a phone drop to 128 cells before dropping the frame rate.", "<strong>Бассейн за страницей.</strong> 256 ячеек при 60 fps стоят один проход симуляции, один сплат каустик и один проход вида на кадр; на телефоне снижай до 128 ячеек раньше, чем частоту кадров."),
        ["give.web.examples.2"] = ("<strong>{0} in a product shot.</strong> MeshPhysicalMaterial with the values below; the dispersion parameter makes the fringes, thickness {3} m makes the tint. A transmission material needs the transmission render pass enabled.", "<strong>{0} в предметной съёмке.</strong> MeshPhysicalMaterial со значениями ниже; параметр dispersion делает каёмки, thickness {3} м делает оттенок. Материалу с transmission нужен включённый проход transmission."),
        ["give.web.methods.1"] = ("Float render targets must be sampled NEAREST unless the float-linear extension is present; a LINEAR float texture reads zeros on many phones and the water lies dead. The view shader interpolates by hand.", "Float-цели рендера нужно читать NEAREST, если нет расширения float-linear; LINEAR float-текстура на многих телефонах читается нулями, и вода лежит мёртвой. Шейдер вида интерполирует вручную."),
        ["give.web.methods.2"] = ("Refraction per channel means three texture reads of the floor at three offsets; that is the whole cost of the fringes.", "Преломление по каналам это три чтения текстуры дна с тремя смещениями; в этом вся цена каёмок."),
        ["give.web.advice.1"] = ("Run the simulation on its own clock; if a tab is throttled, several small steps catch up and one big step explodes.", "Гоняй симуляцию по своим часам; если вкладку придушили, несколько малых шагов догонят, а один большой взорвётся."),
        ["give.web.advice.2"] = ("Pause when the canvas is off screen; a pool nobody sees still burns the battery.", "Ставь на паузу, когда canvas вне экрана; бассейн, которого никто не видит, всё равно жжёт батарею."),

        // .NET
        ["give.dotnet.name"] = (".NET", ".NET"),
        ["give.dotnet.tag"] = ("The Undine package: surface, optics and caustics in C#.", "Пакет Undine: поверхность, оптика и каустики на C#."),
        ["give.dotnet.h1"] = ("Liquid for .NET", "Жидкость для .NET"),
        ["give.dotnet.lede"] = ("The package under this site: a height-field surface with a stable step, the optics of each liquid from its Caustikon glass record, and a ray-counted caustic map. No GPU required.", "Пакет под этим сайтом: поверхность как поле высот с устойчивым шагом, оптика каждой жидкости из её стеклянной записи Caustikon и карта каустик подсчётом лучей. GPU не нужен."),
        ["give.dotnet.examples.1"] = ("<strong>Baking a caustic texture.</strong> Build a Surface, disturb it, step a fraction of a second, build a CausticMap with 512 rays per side and write it out. One frame of {0} at {3} m takes well under a second on a laptop.", "<strong>Запечь текстуру каустик.</strong> Создай Surface, возмути, шагни на долю секунды, построй CausticMap с 512 лучами на сторону и запиши. Один кадр {0} на {3} м занимает заметно меньше секунды на ноутбуке."),
        ["give.dotnet.examples.2"] = ("<strong>A server-side preview.</strong> Surface.Heights is a row-major float span: upload it as R32F to any client, or hand it to the caustic map. The step count Surface chooses for {0} keeps the simulation stable at any frame rate.", "<strong>Превью на сервере.</strong> Surface.Heights это float-спан построчно: загрузи его как R32F любому клиенту или отдай карте каустик. Число подшагов, которое Surface выбирает для {0}, держит симуляцию устойчивой при любой частоте кадров."),
        ["give.dotnet.methods.1"] = ("Surface integrates the damped wave equation with substeps from min(cell/(c√2), 0,1·cell²/ν). LiquidOptics evaluates the glass record at 610, 550 and 465 nm and turns extinction into absorption per metre.", "Surface интегрирует волновое уравнение с гашением подшагами из min(ячейка/(c√2), 0,1·ячейка²/ν). LiquidOptics вычисляет стеклянную запись на 610, 550 и 465 нм и превращает экстинкцию в поглощение на метр."),
        ["give.dotnet.advice.1"] = ("Reference Caustikon.Glasses through the submodule as the package does; the liquids are catalog entries with provenance, not constants.", "Ссылайся на Caustikon.Glasses через сабмодуль, как это делает пакет; жидкости это записи каталога с источником, а не константы."),
        ["give.dotnet.advice.2"] = ("Check PeakHeight and Motion, not the sum of squared heights: the damped equation does not conserve energy and the tests do not pretend it does.", "Проверяй PeakHeight и Motion, а не сумму квадратов высот: уравнение с гашением не сохраняет энергию, и тесты не делают вид, что сохраняет."),
    };
}
