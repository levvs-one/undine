// Language switch and the liquid table; kept out of the page so the site can serve a strict Content-Security-Policy.
        (() => {
    const s = window.undineStrings;
    for (const button of document.querySelectorAll("[data-lang]")) {
        button.addEventListener("click", () => s.set(button.getAttribute("data-lang")));
    }
    s.apply();
    fetch("data/liquids.json").then(r => r.json()).then(json => {
        const body = document.querySelector("#liquids-table tbody");
        const render = () => {
            body.innerHTML = "";
            for (const l of json.liquids) {
                const tr = document.createElement("tr");
                const f = (v, d) => s.number(v, d);
                const sw = hex => hex ? `<span class="swatch" style="background:${hex}"></span> ${hex}` : "—";
                tr.innerHTML = `<td>${s.liquidName(l.name)}</td>` +
                    `<td class="num">${f(l.indexRgb[0], 4)}, ${f(l.indexRgb[1], 4)}, ${f(l.indexRgb[2], 4)}</td>` +
                    `<td class="num">${l.hasAbsorption ? f(l.absorptionPerMetreRgb[0], 3) + ", " + f(l.absorptionPerMetreRgb[1], 3) + ", " + f(l.absorptionPerMetreRgb[2], 3) : "—"}</td>` +
                    `<td class="num">${f(l.densityKgPerM3, 1)}</td><td class="num">${f(l.viscosityMPaS, l.viscosityMPaS < 10 ? 3 : 0)}</td><td class="num">${f(l.surfaceTensionMNPerM, 1)}</td>` +
                    `<td>${sw(l.colourAfter10cm)} ${sw(l.colourAfter1m)} ${sw(l.colourAfter10m)}</td>` +
                    `<td class="source">${l.opticsCitation}</td>`;
                body.appendChild(tr);
            }
        };
        render();
        document.addEventListener("undine-language", render);
    });
})();
