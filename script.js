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

function escapeHtml(text) {
  return text
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}

const CS_KEYWORDS = new Set([
  "abstract","as","base","bool","break","byte","case","catch","char","checked",
  "class","const","continue","decimal","default","delegate","do","double","else",
  "enum","event","explicit","extern","false","finally","fixed","float","for",
  "foreach","goto","if","implicit","in","int","interface","internal","is","lock",
  "long","namespace","new","null","object","operator","out","override","params",
  "private","protected","public","readonly","ref","return","sbyte","sealed",
  "short","sizeof","stackalloc","static","string","struct","switch","this",
  "throw","true","try","typeof","uint","ulong","unchecked","unsafe","ushort",
  "using","virtual","void","volatile","while","async","await","var","when",
  "nameof","record","required","init","get","set","add","remove","partial",
  "where","yield","from","select","group","into","orderby","join","let","on",
  "equals","by","ascending","descending","file","scoped",
]);

const CS_TYPES = new Set([
  "Action","Func","Task","UniTask","List","Dictionary","HashSet","IEnumerable",
  "IEnumerator","IDisposable","StringBuilder","Vector2","Vector3","Vector2Int",
  "Vector3Int","Quaternion","Transform","GameObject","MonoBehaviour","ScriptableObject",
  "Component","Collider","Mesh","MeshCollider","BoxCollider","Material","Color",
  "Debug","Mathf","Time","Input","Object","Type","Exception","ArgumentException",
  "CancellationToken","CancellationTokenSource","Span","ReadOnlySpan","Array",
  "Queue","Stack","Tuple","ValueTuple","Nullable","Enum","Attribute","SerializeField",
  "Header","Tooltip","Range","Min","Max","FormerlySerializedAs","Serializable",
]);

/** Lightweight C# highlighter (file:// safe, no CDN). */
function highlightCSharp(code) {
  const tokens = [];
  let i = 0;
  const len = code.length;

  const push = (type, value) => {
    if (!value) return;
    tokens.push({ type, value });
  };

  while (i < len) {
    // line comment
    if (code[i] === "/" && code[i + 1] === "/") {
      let j = i + 2;
      while (j < len && code[j] !== "\n") j++;
      push("c", code.slice(i, j));
      i = j;
      continue;
    }
    // block comment
    if (code[i] === "/" && code[i + 1] === "*") {
      let j = i + 2;
      while (j < len && !(code[j] === "*" && code[j + 1] === "/")) j++;
      j = Math.min(len, j + 2);
      push("c", code.slice(i, j));
      i = j;
      continue;
    }
    // preprocessor
    if (code[i] === "#" && (i === 0 || code[i - 1] === "\n")) {
      let j = i + 1;
      while (j < len && code[j] !== "\n") j++;
      push("p", code.slice(i, j));
      i = j;
      continue;
    }
    // attribute: [SerializeField] / [Header("x")] — not list[index]
    if (code[i] === "[" && /[A-Z_]/.test(code[i + 1] || "")) {
      let j = i + 1;
      let depth = 1;
      while (j < len && depth > 0) {
        if (code[j] === "[") depth++;
        else if (code[j] === "]") depth--;
        else if (code[j] === '"') {
          j++;
          while (j < len && code[j] !== '"') {
            if (code[j] === "\\") j++;
            j++;
          }
        }
        j++;
      }
      const chunk = code.slice(i, j);
      if (chunk.length < 240 && !/\n\s*\n/.test(chunk)) {
        push("a", chunk);
        i = j;
        continue;
      }
    }
    // strings (verbatim / regular / interpolated simplified)
    if (code[i] === "@" && code[i + 1] === '"') {
      let j = i + 2;
      while (j < len) {
        if (code[j] === '"' && code[j + 1] === '"') {
          j += 2;
          continue;
        }
        if (code[j] === '"') {
          j++;
          break;
        }
        j++;
      }
      push("s", code.slice(i, j));
      i = j;
      continue;
    }
    if (code[i] === "$" && code[i + 1] === '"') {
      let j = i + 2;
      while (j < len) {
        if (code[j] === "\\") {
          j += 2;
          continue;
        }
        if (code[j] === '"') {
          j++;
          break;
        }
        j++;
      }
      push("s", code.slice(i, j));
      i = j;
      continue;
    }
    if (code[i] === '"') {
      let j = i + 1;
      while (j < len) {
        if (code[j] === "\\") {
          j += 2;
          continue;
        }
        if (code[j] === '"') {
          j++;
          break;
        }
        if (code[j] === "\n") break;
        j++;
      }
      push("s", code.slice(i, j));
      i = j;
      continue;
    }
    if (code[i] === "'") {
      let j = i + 1;
      if (code[j] === "\\") j += 2;
      else j++;
      if (code[j] === "'") j++;
      push("s", code.slice(i, j));
      i = j;
      continue;
    }
    // numbers
    if (/[0-9]/.test(code[i]) && (i === 0 || !/[A-Za-z_]/.test(code[i - 1]))) {
      let j = i;
      while (j < len && /[0-9_.xXa-fA-F]/.test(code[j])) j++;
      if (/[fFdDmMuUlL]/.test(code[j])) j++;
      push("n", code.slice(i, j));
      i = j;
      continue;
    }
    // identifiers / keywords / types
    if (/[A-Za-z_]/.test(code[i])) {
      let j = i + 1;
      while (j < len && /[A-Za-z0-9_]/.test(code[j])) j++;
      const word = code.slice(i, j);
      if (CS_KEYWORDS.has(word)) push("k", word);
      else if (CS_TYPES.has(word) || /^[A-Z][A-Za-z0-9_]*$/.test(word)) push("t", word);
      else push("", word);
      i = j;
      continue;
    }
    // plain punctuation / whitespace chunk
    let j = i + 1;
    while (
      j < len &&
      !/[A-Za-z_0-9/#"'@$]/.test(code[j]) &&
      !(code[j] === "/" && (code[j + 1] === "/" || code[j + 1] === "*"))
    ) {
      j++;
    }
    push("", code.slice(i, j));
    i = j;
  }

  return tokens
    .map(({ type, value }) => {
      const safe = escapeHtml(value);
      return type ? `<span class="${type}">${safe}</span>` : safe;
    })
    .join("");
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
  const pre = el("pre");
  const codeEl = el("code", { className: "hl" });
  pre.appendChild(codeEl);
  panel.appendChild(meta);
  panel.appendChild(pre);
  view.appendChild(panel);

  renderSidebar();

  try {
    const code = loadSource(topicId, state.fileName);
    meta.appendChild(el("span", { text: `${code.split(/\r?\n/).length} lines` }));
    codeEl.innerHTML = highlightCSharp(code);
  } catch (err) {
    meta.appendChild(el("span", { text: "error" }));
    codeEl.textContent = String(err);
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
