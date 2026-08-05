const state = {
  data: null,
  topicId: null,
  fileName: null,
};

function $(id) {
  return document.getElementById(id);
}

function el(tag, props = {}, kids = []) {
  const n = document.createElement(tag);
  Object.entries(props).forEach(([k, v]) => {
    if (k === "className") n.className = v;
    else if (k === "text") n.textContent = v;
    else if (k === "html") n.innerHTML = v;
    else if (v != null) n.setAttribute(k, v);
  });
  kids.forEach((c) => n.appendChild(typeof c === "string" ? document.createTextNode(c) : c));
  return n;
}

function loadSource(topicId, fileName) {
  const key = `${topicId}/${fileName}`;
  const text = state.data.sources?.[key];
  if (typeof text !== "string") {
    throw new Error(`Source missing in bundle: ${key}`);
  }
  return text;
}

function initTheme() {
  const btn = $("theme-toggle");
  if (!btn) return;
  btn.addEventListener("click", () => {
    const next = document.documentElement.getAttribute("data-theme") === "dark" ? "light" : "dark";
    document.documentElement.setAttribute("data-theme", next);
    try {
      localStorage.setItem("theme", next);
    } catch (_) {}
  });
}

function renderSidebar() {
  const side = $("sidebar");
  side.innerHTML = "";
  side.appendChild(el("div", { className: "nav-label", text: "Topics" }));
  state.data.topics.forEach((t) => {
    const btn = el("button", {
      className: "nav-btn" + (state.topicId === t.id ? " is-active" : ""),
      type: "button",
      text: t.title,
      "data-id": t.id,
    });
    btn.addEventListener("click", () => openTopic(t.id));
    side.appendChild(btn);
  });
  side.appendChild(el("div", { className: "nav-label", text: "Tools" }));
  state.data.tools.forEach((tool) => {
    const a = el("a", {
      className: "tool-link",
      href: `#${tool.id}`,
      text: tool.title,
    });
    a.addEventListener("click", (e) => {
      e.preventDefault();
      openTool(tool.id);
    });
    side.appendChild(a);
  });
}

function openTopic(topicId, fileName) {
  const topic = state.data.topics.find((t) => t.id === topicId);
  if (!topic) return;
  state.topicId = topicId;
  state.fileName = fileName || topic.files[0];
  history.replaceState(null, "", `#${topicId}`);
  $("welcome").hidden = true;
  const view = $("topic-view");
  view.hidden = false;
  view.innerHTML = "";

  view.appendChild(el("p", { className: "origin", text: topic.origin }));
  view.appendChild(el("h1", { text: topic.title }));
  view.appendChild(el("p", { className: "lead", text: topic.summary }));
  const hl = el("ul", { className: "highlights" });
  topic.highlights.forEach((h) => hl.appendChild(el("li", { text: h })));
  view.appendChild(hl);

  const tabs = el("div", { className: "file-tabs" });
  topic.files.forEach((f) => {
    const tab = el("button", {
      className: "file-tab" + (f === state.fileName ? " is-active" : ""),
      type: "button",
      text: f,
    });
    tab.addEventListener("click", () => openTopic(topicId, f));
    tabs.appendChild(tab);
  });
  view.appendChild(tabs);

  const panel = el("div", { className: "code-panel" });
  const meta = el("div", { className: "code-meta" });
  meta.appendChild(el("span", { text: `sources/${topicId}/${state.fileName}` }));
  const pre = el("pre", { text: "" });
  panel.appendChild(meta);
  panel.appendChild(pre);
  view.appendChild(panel);

  renderSidebar();

  try {
    const code = loadSource(topicId, state.fileName);
    meta.appendChild(el("span", { text: `${code.split(/\r?\n/).length} lines` }));
    pre.textContent = code;
  } catch (err) {
    meta.appendChild(el("span", { text: "error" }));
    pre.textContent = String(err);
  }
}

function openTool(toolId) {
  const tool = state.data.tools.find((t) => t.id === toolId);
  if (!tool) return;
  state.topicId = null;
  history.replaceState(null, "", `#${toolId}`);
  $("welcome").hidden = true;
  const view = $("topic-view");
  view.hidden = false;
  view.innerHTML = "";
  view.appendChild(el("p", { className: "eyebrow", text: "Personal tool" }));

  const links = el("div", { className: "tool-links" });
  if (tool.repo) {
    links.appendChild(
      el("a", {
        href: tool.repo,
        target: "_blank",
        rel: "noopener noreferrer",
        text: "GitHub",
      })
    );
  }
  if (tool.docs) {
    links.appendChild(
      el("a", {
        href: tool.docs,
        target: "_blank",
        rel: "noopener noreferrer",
        text: "GitBook",
      })
    );
  }

  const card = el("div", { className: "tool-card" }, [
    el("h2", { text: tool.title }),
    el("p", { text: tool.blurb }),
    el("p", { text: tool.note }),
    links,
  ]);
  view.appendChild(card);
  view.appendChild(
    el("p", {
      className: "lead",
      text: "메인 포트폴리오 Personal 섹션의 카드에서도 같은 항목을 확인할 수 있습니다.",
    })
  );
  renderSidebar();
}

function main() {
  initTheme();
  if (!window.PORTFOLIO_DATA) {
    $("welcome").innerHTML =
      "<p>로드 실패: bundle-data.js 가 없습니다. <code>node scripts/build-bundle.mjs</code> 를 실행하세요.</p>";
    return;
  }
  state.data = window.PORTFOLIO_DATA;
  renderSidebar();

  const hash = location.hash.replace(/^#/, "");
  if (hash.startsWith("tools-")) openTool(hash);
  else if (hash && state.data.topics.some((t) => t.id === hash)) openTopic(hash);
}

main();
