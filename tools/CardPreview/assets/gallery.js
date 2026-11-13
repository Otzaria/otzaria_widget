// Renders each scenario (template + data) with the official adaptivecards JS renderer + templating, using a host config
// that approximates the Windows 11 Widgets Board. NOT identical to the board's WinUI renderer - good for judging layout.
(function () {
  const SIZES = { small: [164, 164], medium: [344, 164], large: [344, 356] };
  const GROUPS = { ok: ["ok"], states: ["empty", "missing"], times: ["night", "dawn", "afternoon"], custom: ["customize"] };
  const KINDS = ["RecentBooks", "Bookmarks", "ShamorZachor", "Zmanim", "HebrewCalendar", "Dashboard"];
  const FONT = '"Segoe UI Variable Text","Segoe UI Variable","Segoe UI",system-ui,sans-serif';

  function fg(def, subtle) { return { default: def, subtle: subtle }; }
  function config(theme) {
    const dark = theme === "dark";
    const colors = {
      default: fg(dark ? "#FFFFFF" : "#1B1B1B", dark ? "#C8C8C8" : "#5D5D5D"),
      dark: fg("#1B1B1B", "#5D5D5D"),
      light: fg("#FFFFFF", "#C8C8C8"),
      accent: fg(dark ? "#60CDFF" : "#005FB8", dark ? "#60CDFF" : "#005FB8"),
      good: fg(dark ? "#6CCB5F" : "#0F7B0F", dark ? "#6CCB5F" : "#0F7B0F"),
      warning: fg(dark ? "#FCE100" : "#9D5D00", dark ? "#FCE100" : "#9D5D00"),
      attention: fg(dark ? "#FF99A4" : "#C42B1C", dark ? "#FF99A4" : "#C42B1C"),
    };
    return {
      fontFamily: FONT,
      fontSizes: { small: 12, default: 14, medium: 18, large: 20, extraLarge: 28 },
      fontWeights: { lighter: 400, default: 400, bolder: 600 },
      lineHeights: { small: 16, default: 20, medium: 24, large: 28, extraLarge: 36 },
      spacing: { small: 4, default: 8, medium: 12, large: 16, extraLarge: 24, padding: 0 },
      separator: { lineThickness: 1, lineColor: dark ? "#29FFFFFF" : "#14000000" },
      supportsInteractivity: true,
      imageSizes: { small: 40, medium: 80, large: 160 },
      actions: {
        maxActions: 5, spacing: "default", buttonSpacing: 8, showCard: { actionMode: "inline", inlineTopMargin: 8 },
        actionsOrientation: "horizontal", actionAlignment: "stretch",
      },
      containerStyles: {
        default: { backgroundColor: "#00000000", foregroundColors: colors },
        emphasis: { backgroundColor: dark ? "#0FFFFFFF" : "#0A000000", foregroundColors: colors },
        accent: { backgroundColor: dark ? "#0FFFFFFF" : "#0A000000", foregroundColors: colors },
        good: { backgroundColor: dark ? "#0FFFFFFF" : "#0A000000", foregroundColors: colors },
        attention: { backgroundColor: dark ? "#0FFFFFFF" : "#0A000000", foregroundColors: colors },
        warning: { backgroundColor: dark ? "#0FFFFFFF" : "#0A000000", foregroundColors: colors },
      },
    };
  }

  function el(tag, cls, text) { const e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; }

  function renderCard(sc, theme) {
    const [w, h] = SIZES[sc.size];
    const frame = el("div", "frame " + theme);
    frame.style.width = w + "px"; frame.style.height = h + "px";
    const hdr = el("div", "hdr"); hdr.appendChild(el("i")); hdr.appendChild(el("span", null, "אוצריא")); frame.appendChild(hdr);
    const body = el("div", "body"); frame.appendChild(body);
    let info = "";
    try {
      const expanded = JSON.parse(theme === "dark" ? sc.dark : sc.light);
      if (sc.warnings) info = sc.warnings;
      const card = new AdaptiveCards.AdaptiveCard();
      card.hostConfig = new AdaptiveCards.HostConfig(config(theme));
      card.onExecuteAction = function () {};
      card.parse(expanded);
      const node = card.render();
      body.appendChild(node);
    } catch (e) {
      body.appendChild(el("div", "err", String(e && e.stack || e)));
    }
    return { frame, body };
  }

  function checkOverflow(frame, body) {
    const card = body.firstElementChild;
    if (!card) return "";
    const avail = body.clientHeight - 16;
    // measure the real content (children), not the stretched card box
    let bottom = 0;
    const top = card.getBoundingClientRect().top;
    card.querySelectorAll("*").forEach(function (n) { const r = n.getBoundingClientRect(); if (r.height > 0) bottom = Math.max(bottom, r.bottom - top); });
    const over = Math.round(bottom - avail);
    if (over > 0) { frame.classList.add("overflow"); return "overflow +" + over + "px"; }
    return "";
  }

  function main() {
    const root = document.getElementById("root");
    const q = new URLSearchParams(location.search);
    const kinds = q.get("kind") ? [q.get("kind")] : KINDS;
    const groups = q.get("group") ? [q.get("group")] : Object.keys(GROUPS);
    kinds.forEach(function (kind) {
      const t = el("div", "kind-title", kind + " - " + ((window.SCENARIOS.find(function (s) { return s.kind === kind; }) || {}).name || ""));
      root.appendChild(t);
      groups.forEach(function (g) {
        GROUPS[g].forEach(function (tag) {
          const list = window.SCENARIOS.filter(function (s) { return s.kind === kind && s.tag === tag; });
          if (!list.length) return;
          root.appendChild(el("h2", null, g === "ok" ? "" : tag));
          ["light", "dark"].forEach(function (theme) {
            const row = el("div", "row");
            list.forEach(function (sc) {
              const cell = el("div", "cell");
              const r = renderCard(sc, theme);
              cell.appendChild(r.frame);
              const cap = el("div", "cap", sc.size + " / " + theme + " / " + tag);
              cell.appendChild(cap);
              row.appendChild(cell);
              // measure after layout
              requestAnimationFrame(function () { const o = checkOverflow(r.frame, r.body); if (o) { cap.textContent += " - " + o; cap.classList.add("bad"); } });
            });
            root.appendChild(row);
          });
        });
      });
    });
  }
  window.addEventListener("DOMContentLoaded", main);
})();
