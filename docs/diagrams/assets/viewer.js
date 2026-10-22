/* ── system-diagrams viewer ───────────────────────────────────────────────
   No dependencies, no network. Everything the page needs is in model.js.

   The whole point of this file is that identifiers are shared: a box in a
   diagram, a row in the traceability table, and a chip in the inspector all
   carry the same model id, so "show me everything connected to this" is a
   graph walk rather than a guess.                                          */

(function () {
  "use strict";

  var M = window.SYSTEM_MODEL || {};
  var IDX = M.index || {};
  var svg = document.getElementById("canvas");
  var viewport = document.getElementById("viewport");
  var wrap = document.getElementById("canvas-wrap");
  var inspector = document.getElementById("inspector");
  var annLayer = document.getElementById("ann-layer");
  var pageFile = document.body.dataset.pageFile || "index.html";

  if (!svg || !viewport) return;

  /* ── lookup tables ───────────────────────────────────────────────────── */
  var byId = {};
  ["nodes", "requirements", "verifications", "flows", "interfaces"].forEach(function (k) {
    (M[k] || []).forEach(function (o) { byId[o.id] = { type: k, obj: o }; });
  });
  (M.edges || []).forEach(function (e) { byId[e.id] = { type: "edges", obj: e }; });

  var out = {}, inn = {};
  (M.edges || []).forEach(function (e) {
    (out[e.from] = out[e.from] || []).push(e);
    (inn[e.to] = inn[e.to] || []).push(e);
  });

  function label(id) {
    var r = byId[id];
    if (!r) return id;
    return r.obj.name || r.obj.text || id;
  }
  function pageFor(id) { return (IDX.page_for || {})[id] || null; }
  function esc(s) {
    return String(s == null ? "" : s).replace(/[&<>"']/g, function (c) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
    });
  }

  /* ── pan & zoom ──────────────────────────────────────────────────────── */
  var view = { k: 1, x: 0, y: 0 };
  var readout = document.getElementById("zoom-readout");

  function baseScale() {
    var m = svg.getScreenCTM();
    return m ? m.a / view.k : 1;
  }
  function apply() {
    viewport.setAttribute("transform",
      "translate(" + view.x.toFixed(2) + " " + view.y.toFixed(2) + ") scale(" + view.k.toFixed(4) + ")");
    if (readout) readout.textContent = Math.round(view.k * baseScale() * 100) + "%";
  }
  function userPoint(evt) {
    var p = svg.createSVGPoint();
    p.x = evt.clientX; p.y = evt.clientY;
    var m = svg.getScreenCTM();
    return m ? p.matrixTransform(m.inverse()) : { x: 0, y: 0 };
  }
  function zoomAt(px, py, factor) {
    var k2 = Math.min(6, Math.max(0.12, view.k * factor));
    view.x = px - (k2 / view.k) * (px - view.x);
    view.y = py - (k2 / view.k) * (py - view.y);
    view.k = k2;
    apply();
  }
  function zoomCentre(factor) {
    var r = svg.getBoundingClientRect();
    var p = userPoint({ clientX: r.left + r.width / 2, clientY: r.top + r.height / 2 });
    zoomAt(p.x, p.y, factor);
  }
  function fit() { view = { k: 1, x: 0, y: 0 }; apply(); }
  function actualSize() {
    var s = baseScale() || 1;
    var r = svg.getBoundingClientRect();
    var p = userPoint({ clientX: r.left + r.width / 2, clientY: r.top + r.height / 2 });
    zoomAt(p.x, p.y, (1 / s) / view.k);
  }
  function focusBox(bb, targetK) {
    var vb = svg.viewBox.baseVal;
    var k = targetK || Math.min(2.2, Math.max(0.6, Math.min(vb.width / (bb.width + 260), vb.height / (bb.height + 200))));
    view.k = k;
    view.x = vb.width / 2 - k * (bb.x + bb.width / 2);
    view.y = vb.height / 2 - k * (bb.y + bb.height / 2);
    apply();
  }

  svg.addEventListener("wheel", function (e) {
    e.preventDefault();
    var p = userPoint(e);
    zoomAt(p.x, p.y, e.deltaY < 0 ? 1.13 : 1 / 1.13);
  }, { passive: false });

  var drag = null;
  svg.addEventListener("pointerdown", function (e) {
    if (annotateMode && e.target.closest(".node, .edge") == null) return;
    if (e.button !== 0 && e.button !== 1) return;
    drag = { x: e.clientX, y: e.clientY, vx: view.x, vy: view.y, moved: false, s: baseScale() * view.k };
    svg.setPointerCapture(e.pointerId);
    svg.classList.add("grabbing");
  });
  svg.addEventListener("pointermove", function (e) {
    if (!drag) return;
    var dx = e.clientX - drag.x, dy = e.clientY - drag.y;
    if (Math.abs(dx) + Math.abs(dy) > 3) drag.moved = true;
    var s = drag.s || 1;
    view.x = drag.vx + dx / s * view.k;
    view.y = drag.vy + dy / s * view.k;
    apply();
  });
  ["pointerup", "pointercancel"].forEach(function (ev) {
    svg.addEventListener(ev, function () { drag = null; svg.classList.remove("grabbing"); });
  });

  // pinch
  var pts = {}, pinch = null;
  svg.addEventListener("pointerdown", function (e) { pts[e.pointerId] = e; });
  svg.addEventListener("pointermove", function (e) {
    if (!(e.pointerId in pts)) return;
    pts[e.pointerId] = e;
    var ids = Object.keys(pts);
    if (ids.length === 2) {
      drag = null;
      var a = pts[ids[0]], b = pts[ids[1]];
      var d = Math.hypot(a.clientX - b.clientX, a.clientY - b.clientY);
      var mid = userPoint({ clientX: (a.clientX + b.clientX) / 2, clientY: (a.clientY + b.clientY) / 2 });
      if (pinch) zoomAt(mid.x, mid.y, d / pinch);
      pinch = d;
    }
  });
  ["pointerup", "pointercancel"].forEach(function (ev) {
    svg.addEventListener(ev, function (e) { delete pts[e.pointerId]; pinch = null; });
  });

  /* ── selection & tracing ─────────────────────────────────────────────── */
  var selected = null, traceMode = false;

  function clearMarks() {
    svg.querySelectorAll(".dimmed, .traced, .selected").forEach(function (el) {
      el.classList.remove("dimmed", "traced", "selected");
    });
    document.querySelectorAll("tr.hit").forEach(function (t) { t.classList.remove("hit"); });
  }

  function closure(id) {
    var seen = {}, stack = [id];
    while (stack.length) {
      var cur = stack.pop();
      if (seen[cur]) continue;
      seen[cur] = 1;
      (out[cur] || []).forEach(function (e) { stack.push(e.to); });
      (inn[cur] || []).forEach(function (e) { stack.push(e.from); });
    }
    return seen;
  }

  function related(id) {
    var s = {};
    s[id] = 1;
    (out[id] || []).forEach(function (e) { s[e.to] = 1; });
    (inn[id] || []).forEach(function (e) { s[e.from] = 1; });
    ((IDX.children || {})[id] || []).forEach(function (c) { s[c] = 1; });
    var p = (byId[id] && byId[id].obj.parent);
    if (p) s[p] = 1;
    return s;
  }

  function highlight(id) {
    clearMarks();
    if (!id) return;
    var keep = traceMode ? closure(id) : related(id);
    keep[id] = 1;
    svg.querySelectorAll(".node").forEach(function (n) {
      var nid = n.dataset.id;
      if (keep[nid]) { n.classList.add("traced"); if (nid === id) n.classList.add("selected"); }
      else n.classList.add("dimmed");
    });
    svg.querySelectorAll(".edge").forEach(function (e) {
      if (keep[e.dataset.src] && keep[e.dataset.dst]) e.classList.add("traced");
      else e.classList.add("dimmed");
    });
    document.querySelectorAll("tr[data-id]").forEach(function (tr) {
      if (tr.dataset.id === id) tr.classList.add("hit");
    });
  }

  /* ── inspector ───────────────────────────────────────────────────────── */
  function chip(id, cls) {
    var pg = pageFor(id);
    var href = pg ? pg + "?focus=" + encodeURIComponent(id) : "#";
    return '<a class="chip ' + (cls || "") + '" href="' + esc(href) + '" data-goto="' + esc(id) +
      '" title="' + esc(label(id)) + '">' + esc(id) + "</a>";
  }
  function section(title, html) {
    return html ? "<h3>" + esc(title) + "</h3>" + html : "";
  }
  function list(items) {
    if (!items || !items.length) return "";
    return '<ul class="ins-list">' + items.map(function (i) { return "<li>" + esc(i) + "</li>"; }).join("") + "</ul>";
  }

  function annotationsFor(id) {
    return (M.annotations || []).filter(function (a) { return a.target === id; })
      .concat(local().filter(function (a) { return a.target === id; }));
  }

  function showNode(id) {
    var n = (byId[id] || {}).obj;
    if (!n) return;
    var reqs = (IDX.reqs_for_node || {})[id] || [];
    var vers = [];
    reqs.forEach(function (r) {
      ((IDX.vers_for_req || {})[r] || []).forEach(function (v) { if (vers.indexOf(v) < 0) vers.push(v); });
    });
    var flows = (IDX.flows_for_node || {})[id] || [];
    var ifacesProv = (M.interfaces || []).filter(function (i) { return i.provider === id; });
    var ifacesCons = (M.interfaces || []).filter(function (i) { return (i.consumers || []).indexOf(id) >= 0; });
    var outs = (out[id] || []), ins = (inn[id] || []);
    var pg = pageFor(id);
    var anns = annotationsFor(id);

    var h = "<h2>" + esc(n.name || id) + "</h2>";
    h += '<p class="ins-id">' + esc(id) + " &middot; " + esc(n.kind || "") +
      (n.level != null ? " &middot; L" + esc(n.level) : "") +
      (n.status ? " &middot; " + esc(n.status) : "") + "</p>";
    if (n.description) h += "<p>" + esc(n.description) + "</p>";
    if (pg && pg !== pageFile) {
      h += '<p><a class="chip" href="' + esc(pg) + "?focus=" + encodeURIComponent(id) + '">Open its own page &#8594;</a></p>';
    }
    h += section("Owner", n.owner ? "<p>" + esc(n.owner) + "</p>" : "");
    h += section("Technology", (n.tech || []).map(function (t) { return '<span class="chip">' + esc(t) + "</span>"; }).join(""));
    h += section("Responsibilities", list(n.responsibilities));
    h += section("Provides", ifacesProv.map(function (i) { return chip(i.id, "req"); }).join(""));
    h += section("Consumes", ifacesCons.map(function (i) { return chip(i.id, "req"); }).join(""));
    h += section("Outgoing", outs.map(function (e) {
      return '<li><b>&#8594; ' + esc(label(e.to)) + "</b>" + (e.label ? " &mdash; " + esc(e.label) : "") +
        (e.mechanism ? '<br><span class="evidence">' + esc(e.mechanism) + "</span>" : "") + "</li>";
    }).join("") ? '<ul class="ins-list">' + outs.map(function (e) {
      return '<li><b>&#8594; ' + esc(label(e.to)) + "</b>" + (e.label ? " &mdash; " + esc(e.label) : "") +
        (e.mechanism ? '<br><span class="evidence">' + esc(e.mechanism) + "</span>" : "") + "</li>";
    }).join("") + "</ul>" : "");
    h += section("Incoming", ins.length ? '<ul class="ins-list">' + ins.map(function (e) {
      return "<li><b>" + esc(label(e.from)) + " &#8594;</b>" + (e.label ? " &mdash; " + esc(e.label) : "") + "</li>";
    }).join("") + "</ul>" : "");
    h += section("Requirements", reqs.length ? reqs.map(function (r) { return chip(r, "req"); }).join("")
      : '<p class="muted">None allocated. Nothing in the spec justifies this component &mdash; worth checking.</p>');
    h += section("Verified by", vers.map(function (v) { return chip(v, "ver"); }).join(""));
    h += section("Appears in flows", flows.map(function (f) { return chip(f, ""); }).join(""));
    h += section("Evidence in code", (n.evidence || []).map(function (e) {
      return '<div class="evidence">' + esc(e) + "</div>";
    }).join(""));
    h += renderAnnBlock(anns, id);
    inspector.innerHTML = h;
  }

  function showEdge(id) {
    var e = (byId[id] || {}).obj;
    if (!e) return;
    var h = "<h2>" + esc(label(e.from)) + " &#8594; " + esc(label(e.to)) + "</h2>";
    h += '<p class="ins-id">' + esc(id) + " &middot; " + esc(e.kind || "sync") +
      (e.protocol ? " &middot; " + esc(e.protocol) : "") + "</p>";
    if (e.label) h += "<p>" + esc(e.label) + "</p>";
    h += section("Mechanism", e.mechanism ? "<p>" + esc(e.mechanism) + "</p>" :
      '<p class="muted">Not documented. What actually carries this &mdash; retries, timeouts, ordering, idempotency?</p>');
    h += section("Interface", e.interface ? chip(e.interface, "req") : "");
    h += section("Endpoints", [chip(e.from, ""), chip(e.to, "")].join(""));
    h += section("Evidence in code", (e.evidence || []).map(function (x) {
      return '<div class="evidence">' + esc(x) + "</div>";
    }).join(""));
    inspector.innerHTML = h;
  }

  function showReq(id) {
    var r = (byId[id] || {}).obj;
    if (!r) return;
    var vers = (IDX.vers_for_req || {})[id] || [];
    var h = "<h2>" + esc(id) + "</h2>";
    h += '<p class="ins-id">level ' + esc(r.level) + (r.inferred ? " &middot; inferred from code, not a written spec" : "") + "</p>";
    h += "<p>" + esc(r.text || "") + "</p>";
    h += section("Rationale", r.rationale ? "<p>" + esc(r.rationale) + "</p>" : "");
    h += section("Derived from", r.parent ? chip(r.parent, "req") : "");
    h += section("Allocated to", (r.allocated_to || []).map(function (a) { return chip(a, ""); }).join("")
      || '<p class="muted">Unallocated &mdash; no component owns this.</p>');
    h += section("Verified by", vers.map(function (v) { return chip(v, "ver"); }).join("")
      || '<p class="muted">No verification. This would ship unproven.</p>');
    inspector.innerHTML = h;
  }

  function showVer(id) {
    var v = (byId[id] || {}).obj;
    if (!v) return;
    var h = "<h2>" + esc(v.name || id) + "</h2>";
    h += '<p class="ins-id">' + esc(id) + " &middot; " + esc(v.method || "") +
      " &middot; level " + esc(v.level) + " &middot; " + esc(v.status || "unknown") + "</p>";
    h += section("Procedure", v.procedure ? "<p>" + esc(v.procedure) + "</p>" : "");
    h += section("Verifies", (v.verifies || []).map(function (r) { return chip(r, "req"); }).join(""));
    h += section("Evidence", (v.evidence || []).map(function (x) {
      return '<div class="evidence">' + esc(x) + "</div>";
    }).join(""));
    inspector.innerHTML = h;
  }

  function showFlow(id) {
    var f = (byId[id] || {}).obj;
    if (!f) return;
    var h = "<h2>" + esc(f.name || id) + "</h2>";
    h += '<p class="ins-id">' + esc(id) + " &middot; " + ((f.steps || []).length) + " steps</p>";
    if (f.description) h += "<p>" + esc(f.description) + "</p>";
    h += section("Trigger", f.trigger ? "<p>" + esc(f.trigger) + "</p>" : "");
    h += '<p><a class="chip" href="' + esc(pageFor(id) || "#") + '">Open the flow &#8594;</a></p>';
    inspector.innerHTML = h;
  }

  function select(id, opts) {
    opts = opts || {};
    if (!id) { selected = null; clearMarks(); return; }
    selected = id;
    var rec = byId[id];
    if (!rec) return;
    if (rec.type === "nodes") showNode(id);
    else if (rec.type === "edges") showEdge(id);
    else if (rec.type === "requirements") showReq(id);
    else if (rec.type === "verifications") showVer(id);
    else if (rec.type === "flows") showFlow(id);
    else if (rec.type === "interfaces") {
      var i = rec.obj;
      inspector.innerHTML = "<h2>" + esc(i.name || id) + "</h2><p class='ins-id'>" + esc(id) +
        "</p>" + (i.description ? "<p>" + esc(i.description) + "</p>" : "") +
        section("Provider", i.provider ? chip(i.provider, "") : "") +
        section("Consumers", (i.consumers || []).map(function (c) { return chip(c, ""); }).join("")) +
        section("Contract", i.contract ? '<div class="evidence">' + esc(i.contract) + "</div>" : "");
    }
    highlight(id);
    var el = svg.querySelector('.node[data-id="' + cssEsc(id) + '"]');
    if (el && opts.zoom) { try { focusBox(el.getBBox()); } catch (err) { } }
    else if (el && opts.nudge) {
      try {
        var bb = el.getBBox(), vb = svg.viewBox.baseVal;
        var cx = view.x + view.k * (bb.x + bb.width / 2), cy = view.y + view.k * (bb.y + bb.height / 2);
        if (cx < 0 || cy < 0 || cx > vb.width || cy > vb.height) focusBox(bb, view.k);
      } catch (err) { }
    }
    var tr = document.querySelector('tr[data-id="' + cssEsc(id) + '"]');
    if (tr && opts.scroll) tr.scrollIntoView({ block: "center", behavior: "smooth" });
  }

  function cssEsc(s) { return String(s).replace(/["\\]/g, "\\$&"); }

  svg.addEventListener("click", function (e) {
    if (drag && drag.moved) return;
    if (annotateMode) return;
    var node = e.target.closest(".node");
    if (node) { select(node.dataset.id, { nudge: true }); return; }
    var edge = e.target.closest(".edge");
    if (edge) { select(edge.dataset.id); return; }
    var vn = e.target.closest(".v-node");
    if (vn) { filterByLevel(vn); return; }
    select(null);
    inspector.innerHTML = '<div class="ins-empty"><h2>Inspector</h2><p>Select any box, arrow or row to see what it is, where it came from, and everything it traces to.</p></div>';
  });

  svg.addEventListener("keydown", function (e) {
    if (e.key === "Enter" || e.key === " ") {
      var g = e.target.closest(".node, .edge");
      if (g) { e.preventDefault(); select(g.dataset.id, { nudge: true }); }
    }
  });

  document.addEventListener("click", function (e) {
    var g = e.target.closest("[data-goto]");
    if (g && byId[g.dataset.goto] && (pageFor(g.dataset.goto) === pageFile || !pageFor(g.dataset.goto))) {
      e.preventDefault();
      select(g.dataset.goto, { zoom: true, scroll: true });
    }
    var row = e.target.closest("tr[data-id]");
    if (row && !e.target.closest("a")) select(row.dataset.id, { scroll: false });
  });

  function filterByLevel(vn) {
    var lvl = vn.dataset.level, side = vn.dataset.side;
    svg.querySelectorAll(".v-node").forEach(function (n) { n.classList.remove("selected"); });
    vn.classList.add("selected");
    var keep = {};
    (M.requirements || []).forEach(function (r) { if (String(r.level) === lvl) keep[r.id] = 1; });
    (M.verifications || []).forEach(function (v) { if (String(v.level) === lvl) keep[v.id] = 1; });
    document.querySelectorAll("tr[data-id]").forEach(function (tr) {
      tr.style.display = keep[tr.dataset.id] ? "" : "none";
    });
    toast("Filtered to level " + lvl + " (" + (side === "left" ? "requirements" : "verifications") +
      " arm). Click the same box again to clear.");
    if (vn.dataset.on === "1") {
      vn.dataset.on = "";
      vn.classList.remove("selected");
      document.querySelectorAll("tr[data-id]").forEach(function (tr) { tr.style.display = ""; });
    } else {
      svg.querySelectorAll(".v-node").forEach(function (n) { n.dataset.on = ""; });
      vn.dataset.on = "1";
    }
  }

  /* ── annotations ─────────────────────────────────────────────────────── */
  var LS_KEY = "sysdiag:" + ((M.meta || {}).name || "model") + ":" + pageFile;
  var annotateMode = false;
  var localCache = null;

  function local() {
    if (localCache) return localCache;
    try { localCache = JSON.parse(localStorage.getItem(LS_KEY) || "[]"); }
    catch (e) { localCache = []; }
    if (!Array.isArray(localCache)) localCache = [];
    return localCache;
  }
  function saveLocal() {
    try { localStorage.setItem(LS_KEY, JSON.stringify(localCache)); }
    catch (e) {
      toast("Your browser blocked local storage, so these notes live only until you reload. Use \u201cExport notes\u201d to keep them.");
    }
    drawPins();
    if (selected) select(selected);
  }

  function renderAnnBlock(anns, targetId) {
    if (!anns.length) return "";
    var h = "<h3>Annotations</h3>";
    h += anns.map(function (a) {
      var own = a.local ? ' <button class="chip" data-del="' + esc(a.id) + '">delete</button>' : "";
      return '<div class="ins-list"><li><b>' + esc(a.kind || "note") + "</b> &mdash; " +
        esc(a.text) + (a.author ? ' <span class="muted">(' + esc(a.author) + ")</span>" : "") + own + "</li></div>";
    }).join("");
    return h;
  }

  function drawPins() {
    if (!annLayer) return;
    annLayer.innerHTML = "";
    local().forEach(function (a, i) {
      var x = a.x, y = a.y;
      if (a.target) {
        var el = svg.querySelector('.node[data-id="' + cssEsc(a.target) + '"]');
        if (el) { try { var bb = el.getBBox(); x = bb.x + bb.width - 4; y = bb.y + bb.height - 4; } catch (e) { } }
      }
      if (x == null || y == null) return;
      var g = document.createElementNS("http://www.w3.org/2000/svg", "g");
      g.setAttribute("class", "ann-pin");
      g.setAttribute("data-ann", a.id);
      g.innerHTML = '<circle cx="' + x + '" cy="' + y + '" r="10"></circle>' +
        '<text x="' + x + '" y="' + (y + 3.5) + '" text-anchor="middle">' + (i + 1) + "</text>" +
        "<title>" + esc(a.kind + ": " + a.text) + "</title>";
      annLayer.appendChild(g);
    });
  }

  var popup = document.getElementById("ann-popup");
  function openPopup(clientX, clientY, target, x, y) {
    popup.hidden = false;
    popup.style.left = Math.min(window.innerWidth - 320, clientX) + "px";
    popup.style.top = Math.min(window.innerHeight - 200, clientY) + "px";
    popup.innerHTML =
      "<p style='margin:0 0 6px'><b>" + (target ? esc(label(target)) : "Note on the canvas") + "</b></p>" +
      '<textarea id="ann-text" placeholder="What should a reader know here?"></textarea>' +
      '<div class="ap-row"><select id="ann-kind">' +
      '<option value="note">note</option><option value="risk">risk</option>' +
      '<option value="question">question</option><option value="decision">decision</option>' +
      "</select><span class='ap-spacer'></span>" +
      '<button id="ann-cancel">Cancel</button><button class="primary" id="ann-save">Add</button></div>';
    document.getElementById("ann-text").focus();
    document.getElementById("ann-cancel").onclick = function () { popup.hidden = true; };
    document.getElementById("ann-save").onclick = function () {
      var t = document.getElementById("ann-text").value.trim();
      if (!t) { popup.hidden = true; return; }
      local().push({
        id: "ann.local." + Date.now(),
        target: target || null, x: target ? null : x, y: target ? null : y,
        view: pageFile, text: t, kind: document.getElementById("ann-kind").value, local: true
      });
      saveLocal();
      popup.hidden = true;
      toast("Note added. Export it when you want it in the model for good.");
    };
  }

  svg.addEventListener("click", function (e) {
    if (!annotateMode) return;
    var node = e.target.closest(".node");
    var p = userPoint(e);
    var cx = (p.x - view.x) / view.k, cy = (p.y - view.y) / view.k;
    openPopup(e.clientX, e.clientY, node ? node.dataset.id : null, cx, cy);
  });

  document.addEventListener("click", function (e) {
    var d = e.target.closest("[data-del]");
    if (!d) return;
    localCache = local().filter(function (a) { return a.id !== d.dataset.del; });
    saveLocal();
  });

  function exportAnnotations() {
    var all = (M.annotations || []).concat(local().map(function (a) {
      var c = JSON.parse(JSON.stringify(a));
      delete c.local;
      return c;
    }));
    var blob = new Blob([JSON.stringify(all, null, 2)], { type: "application/json" });
    var a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = "annotations.json";
    a.click();
    setTimeout(function () { URL.revokeObjectURL(a.href); }, 2000);
    toast("Downloaded. Paste this into the \u201cannotations\u201d array in model.json to make it permanent.");
  }

  var fileInput = document.getElementById("ann-file");
  if (fileInput) {
    fileInput.addEventListener("change", function () {
      var f = fileInput.files[0];
      if (!f) return;
      var rd = new FileReader();
      rd.onload = function () {
        try {
          var arr = JSON.parse(rd.result);
          if (!Array.isArray(arr)) throw new Error("not an array");
          var known = {};
          (M.annotations || []).forEach(function (a) { known[a.id] = 1; });
          arr.forEach(function (a) {
            if (!known[a.id] && !local().some(function (b) { return b.id === a.id; })) {
              a.local = true;
              local().push(a);
            }
          });
          saveLocal();
          toast("Imported " + arr.length + " note(s).");
        } catch (err) { toast("That file did not look like an annotations export."); }
      };
      rd.readAsText(f);
      fileInput.value = "";
    });
  }

  /* ── toolbar ─────────────────────────────────────────────────────────── */
  document.querySelectorAll(".toolbar button[data-act]").forEach(function (b) {
    b.addEventListener("click", function () {
      var a = b.dataset.act;
      if (a === "zoom-in") zoomCentre(1.25);
      else if (a === "zoom-out") zoomCentre(1 / 1.25);
      else if (a === "fit") fit();
      else if (a === "reset") actualSize();
      else if (a === "annotate") {
        annotateMode = !annotateMode;
        b.setAttribute("aria-pressed", annotateMode);
        svg.classList.toggle("annotating", annotateMode);
        toast(annotateMode ? "Annotate mode: click a box, or empty space, to leave a note."
          : "Annotate mode off.");
      } else if (a === "trace") {
        traceMode = !traceMode;
        b.setAttribute("aria-pressed", traceMode);
        if (selected) highlight(selected);
        toast(traceMode ? "Trace mode: selection now follows every hop, not just neighbours."
          : "Trace mode off.");
      } else if (a === "export-ann") exportAnnotations();
      else if (a === "import-ann" && fileInput) fileInput.click();
    });
  });

  /* ── search ──────────────────────────────────────────────────────────── */
  var search = document.getElementById("search");
  var results = document.getElementById("search-results");
  var pool = [];
  Object.keys(byId).forEach(function (id) {
    var r = byId[id];
    if (r.type === "edges") return;
    pool.push({ id: id, type: r.type, text: (r.obj.name || r.obj.text || "") + " " + id });
  });

  function runSearch() {
    var q = search.value.trim().toLowerCase();
    if (!q) { results.hidden = true; return; }
    var hits = pool.filter(function (p) { return p.text.toLowerCase().indexOf(q) >= 0; }).slice(0, 25);
    results.innerHTML = hits.length ? hits.map(function (h) {
      var pg = pageFor(h.id);
      var href = pg === pageFile || !pg ? "#" : pg + "?focus=" + encodeURIComponent(h.id);
      return '<a href="' + esc(href) + '" data-goto="' + esc(h.id) + '">' + esc(label(h.id)) +
        '<span class="sr-kind"> ' + esc(h.id) + " &middot; " + esc(h.type.replace(/s$/, "")) + "</span></a>";
    }).join("") : '<a class="muted">Nothing matches that.</a>';
    results.hidden = false;
  }
  if (search) {
    search.addEventListener("input", runSearch);
    search.addEventListener("focus", runSearch);
    document.addEventListener("click", function (e) {
      if (!e.target.closest(".topbar-right")) results.hidden = true;
    });
  }

  /* ── dropdowns ───────────────────────────────────────────────────────── */
  document.querySelectorAll(".dropdown .dd-btn").forEach(function (b) {
    b.addEventListener("click", function (e) {
      e.stopPropagation();
      var d = b.parentElement, open = d.classList.contains("open");
      document.querySelectorAll(".dropdown").forEach(function (x) { x.classList.remove("open"); });
      d.classList.toggle("open", !open);
      b.setAttribute("aria-expanded", String(!open));
    });
  });
  document.addEventListener("click", function () {
    document.querySelectorAll(".dropdown").forEach(function (x) { x.classList.remove("open"); });
  });

  /* ── keyboard ────────────────────────────────────────────────────────── */
  document.addEventListener("keydown", function (e) {
    var typing = /^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement.tagName);
    if (e.key === "/" && !typing) { e.preventDefault(); search && search.focus(); return; }
    if (e.key === "Escape") {
      if (popup && !popup.hidden) { popup.hidden = true; return; }
      if (results) results.hidden = true;
      select(null); clearMarks();
      if (search) search.blur();
      return;
    }
    if (typing) return;
    if (e.key === "+" || e.key === "=") zoomCentre(1.25);
    else if (e.key === "-") zoomCentre(1 / 1.25);
    else if (e.key === "0") fit();
    else if (e.key === "1") actualSize();
    else if (e.key === "t") document.querySelector('[data-act="trace"]').click();
    else if (e.key === "a") document.querySelector('[data-act="annotate"]').click();
    else if (e.key.indexOf("Arrow") === 0) {
      var d = 40 / (baseScale() || 1) * view.k;
      if (e.key === "ArrowLeft") view.x += d;
      if (e.key === "ArrowRight") view.x -= d;
      if (e.key === "ArrowUp") view.y += d;
      if (e.key === "ArrowDown") view.y -= d;
      apply();
    }
  });

  /* ── toast ───────────────────────────────────────────────────────────── */
  var toastEl = document.getElementById("toast"), toastT = null;
  function toast(msg) {
    if (!toastEl) return;
    toastEl.textContent = msg;
    toastEl.hidden = false;
    clearTimeout(toastT);
    toastT = setTimeout(function () { toastEl.hidden = true; }, 4200);
  }

  /* ── boot ────────────────────────────────────────────────────────────── */
  apply();
  drawPins();
  var focusId = new URLSearchParams(location.search).get("focus");
  if (focusId && byId[focusId]) {
    setTimeout(function () { select(focusId, { zoom: true, scroll: true }); }, 60);
  }
  window.addEventListener("resize", apply);
})();
