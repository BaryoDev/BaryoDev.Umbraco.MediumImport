// Plain custom element with no build step, same approach as the AI Assistant dashboards.
// The base comes from the backoffice's own UmbElementMixin so the auth context is reachable.
const API = "/umbraco/management/api/v1/medium-import";
const BASE = "/App_Plugins/BaryoDev.MediumImport";
const V = new URL(import.meta.url).search;
const LOGO = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 134 32" role="img" aria-label="baryo.dev"><rect x="0" y="0" width="32" height="32" rx="8" fill="#0b7a55"/><path d="M9 8v16h7a5 5 0 0 0 0-10h-3" fill="none" stroke="#fbf7ee" stroke-width="3.2" stroke-linecap="round" stroke-linejoin="round"/><circle cx="24" cy="9" r="2.4" fill="#ffcd3c"/><text x="40" y="22" font-family="'Plus Jakarta Sans', system-ui, sans-serif" font-size="18" font-weight="700" fill="currentColor">baryo.dev</text></svg>`;
const STOPPED = "The import stopped when the site restarted. Run it again; posts already imported are kept.";

const Base = await import("@umbraco-cms/backoffice/element-api")
  .then(({ UmbElementMixin }) => UmbElementMixin(HTMLElement))
  .catch(() => HTMLElement);

const SLOTS = [
  ["title", "Title", true],
  ["body", "Body", true],
  ["publishDate", "Publish date", false],
  ["summary", "Summary", false],
  ["cover", "Cover image", false],
  ["tags", "Tags", false],
  ["canonical", "Canonical URL", false],
  ["originalUrl", "Original URL", false],
];

const STATUS = ["New", "AlreadyImported", "EditedSinceImport", "Draft", "Response", "Filtered"];
const OUTCOME = ["Imported", "Updated", "Skipped", "Failed"];
const STATUS_TEXT = { New: "New", AlreadyImported: "Already imported", EditedSinceImport: "Edited", Draft: "Draft", Response: "Response", Filtered: "Filtered" };

function count(n, word) {
  return `${n} ${word}${n === 1 ? "" : "s"}`;
}

function esc(value) {
  return String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
}

// uui-select reuses option elements and only writes the native value when it changes,
// so the native select is synced after render. Setting .value keeps it readable at once.
function setOptions(select, list, want = "") {
  const pick = list.some((o) => o.value === want) ? want : "";
  select.placeholder = list.find((o) => o.value === "")?.name ?? "";
  select.options = list.map((o) => ({ ...o, selected: o.value === pick }));
  select.value = pick;
  select.updateComplete.then(() => {
    const native = select.shadowRoot?.querySelector("#native");
    if (!native) return;
    const i = [...native.options].findIndex((o, n) => n > 0 && o.value === pick);
    if (i > 0) native.selectedIndex = i;
  });
}

const named = (list, v) => (typeof v === "number" ? list[v] : v);

// The document picker registers lazily. Wait for it, but not forever.
function pickerReady() {
  if (customElements.get("umb-input-document")) return Promise.resolve(true);
  let timer;
  const timeout = new Promise((resolve) => { timer = setTimeout(() => resolve(false), 3000); });
  const defined = customElements.whenDefined("umb-input-document").then(() => true, () => false);
  return Promise.race([defined, timeout]).finally(() => clearTimeout(timer));
}

const tokens = new WeakMap();

async function token(host) {
  if (!tokens.has(host)) {
    tokens.set(host, Promise.all([
      import("@umbraco-cms/backoffice/context-api"),
      import("@umbraco-cms/backoffice/auth"),
    ]).then(([{ UmbContextConsumer }, { UMB_AUTH_CONTEXT }]) => {
      const consumer = new UmbContextConsumer(host, UMB_AUTH_CONTEXT);
      const auth = consumer.asPromise();
      consumer.request();
      return auth;
    }).catch(() => null));
  }
  const auth = await tokens.get(host);
  return auth ? auth.getLatestToken() : null;
}

class ApiError extends Error {
  constructor(status, message, code) {
    super(message);
    this.status = status;
    this.code = code;
  }
}

/** Calls the management API with the signed-in user's token. Pass FormData as the body to upload. */
async function call(host, path, method = "GET", body = undefined) {
  const t = await token(host);
  const form = body instanceof FormData;
  const headers = {
    ...(t ? { Authorization: `Bearer ${t}` } : {}),
    ...(body === undefined || form ? {} : { "Content-Type": "application/json" }),
  };
  const response = await fetch(API + path, {
    method,
    credentials: "same-origin",
    headers,
    body: body === undefined ? undefined : form ? body : JSON.stringify(body),
  });
  if (response.status === 401 || response.status === 403) throw new ApiError(response.status, "You do not have access to this.");
  if (!response.ok) {
    let code = "";
    let detail = "";
    try {
      const j = await response.json();
      code = j.error ?? "";
      detail = j.detail ?? j.error ?? "";
    } catch { /* no body */ }
    throw new ApiError(response.status, detail || `Request failed (${response.status}).`, code);
  }
  if (response.status === 204) return null;
  return (response.headers.get("content-type") ?? "").includes("json") ? response.json() : null;
}

class MediumImportDashboard extends Base {
  #uploadId = null;
  #preview = null;
  #checked = new Map();
  #options = [];
  #saved = null;
  #job = null;
  #result = null;
  #timer = null;
  #previewTimer = null;
  #error = "";
  #notice = "";
  #info = "";
  #busy = false;
  #running = false;
  #disconnected = false;
  #pollFailures = 0;

  async connectedCallback() {
    super.connectedCallback?.();
    this.#disconnected = false;
    const hasPicker = await pickerReady();
    if (this.#disconnected) return;
    this.innerHTML = this.#shell(hasPicker);
    this.#initOptions();
    this.querySelector("#mi-file").addEventListener("change", () => this.#upload());
    const parent = this.querySelector("#mi-parent");
    parent.addEventListener("change", () => this.#onParent());
    if (!hasPicker) parent.addEventListener("input", () => this.#onParent());
    this.querySelector("#mi-type").addEventListener("change", () => this.#onType());
    for (const id of ["mi-publish", "mi-canonical"]) {
      this.querySelector(`#${id}`).addEventListener("change", () => this.#onSettings());
    }
    for (const [key] of SLOTS) {
      this.querySelector(`#mi-slot-${key}`).addEventListener("change", () => this.#onSettings());
    }
    this.querySelector("#mi-import").addEventListener("click", () => this.#start());
    this.querySelector("#mi-cancel").addEventListener("click", () => this.#cancel());
    this.querySelector("#mi-list").addEventListener("change", (e) => {
      const box = e.target.closest("uui-checkbox[data-post]");
      if (box) {
        this.#checked.set(box.dataset.post, box.checked);
        this.#refreshControls();
      }
    });
    this.#loadSettings();
  }

  disconnectedCallback() {
    super.disconnectedCallback?.();
    this.#disconnected = true;
    clearTimeout(this.#timer);
    clearTimeout(this.#previewTimer);
  }

  #initOptions() {
    setOptions(this.querySelector("#mi-type"), [{ name: "Pick a parent first", value: "" }]);
    setOptions(this.querySelector("#mi-publish"), [{ name: "Save as draft", value: "no" }, { name: "Yes", value: "yes" }], "no");
    setOptions(this.querySelector("#mi-canonical"), [{ name: "This site", value: "ThisSite" }, { name: "Keep Medium", value: "KeepMedium" }], "ThisSite");
    for (const [key, , required] of SLOTS) {
      setOptions(this.querySelector(`#mi-slot-${key}`), [{ name: required ? "Choose a property" : "None", value: "" }]);
    }
  }

  #shell(hasPicker) {
    return `
      <link rel="stylesheet" href="${BASE}/dashboard.css${V}">
      <div class="mi">
        <div class="mi-head">${LOGO}<h1 class="mi-title">Medium import</h1></div>
        <div id="mi-banner"></div>
        <div class="mi-grid">
          <div class="mi-col">
            <uui-box headline="Upload">
              <div class="mi-row"><label class="mi-label" for="mi-file">Medium export zip</label>
                <input type="file" class="mi-file" id="mi-file" accept=".zip,application/zip"></div>
              <div class="mi-note">Export it from Medium under Settings, Security and apps, Download your information.</div>
            </uui-box>
            <uui-box headline="Posts" id="mi-posts" hidden>
              <div class="mi-note" id="mi-skipped"></div>
              <div id="mi-list"></div>
            </uui-box>
          </div>
          <div class="mi-col">
            <uui-box headline="Settings">
              <div class="mi-row"><span class="mi-label">Parent page</span>
                ${hasPicker ? `<umb-input-document id="mi-parent" max="1"></umb-input-document>` : `<uui-input id="mi-parent" label="Parent page" placeholder="Parent key (GUID)"></uui-input>`}</div>
              <div class="mi-row"><span class="mi-label">Document type</span>
                <uui-select id="mi-type" label="Document type" disabled></uui-select></div>
              ${SLOTS.map(([key, label, required]) => `
              <div class="mi-row"><span class="mi-label">${label}${required ? " (required)" : ""}</span>
                <uui-select id="mi-slot-${key}" label="${label}${required ? " (required)" : ""}" disabled></uui-select></div>`).join("")}
              <div class="mi-row"><span class="mi-label">Publish</span>
                <uui-select id="mi-publish" label="Publish"></uui-select></div>
              <div class="mi-row"><span class="mi-label">Canonical</span>
                <uui-select id="mi-canonical" label="Canonical"></uui-select></div>
            </uui-box>
            <uui-box headline="Import">
              <div class="mi-actions">
                <uui-button look="primary" color="positive" id="mi-import" label="Import 0 posts" disabled>Import 0 posts</uui-button>
                <uui-button look="secondary" id="mi-cancel" label="Cancel" hidden>Cancel</uui-button>
              </div>
              <div id="mi-progress" hidden>
                <uui-progress-bar id="mi-bar" role="progressbar" aria-label="Import progress" aria-valuemin="0" aria-valuemax="100" aria-valuenow="0" progress="0"></uui-progress-bar>
                <div class="mi-note" id="mi-status"></div>
              </div>
            </uui-box>
          </div>
        </div>
        <div id="mi-result" class="mi-result"></div>
      </div>`;
  }

  // Settings form

  #parentKey() {
    const el = this.querySelector("#mi-parent");
    return (el?.selection?.[0] ?? el?.value ?? "").trim();
  }

  #settings() {
    const mapping = {};
    for (const [key, , required] of SLOTS) {
      const v = this.querySelector(`#mi-slot-${key}`).value;
      mapping[key] = v || (required ? "" : null);
    }
    return {
      parentKey: this.#parentKey() || "00000000-0000-0000-0000-000000000000",
      docTypeAlias: this.querySelector("#mi-type").value,
      mapping,
      publish: this.querySelector("#mi-publish").value === "yes",
      canonical: this.querySelector("#mi-canonical").value,
    };
  }

  #complete() {
    const s = this.#settings();
    return !!this.#parentKey() && !!s.docTypeAlias && !!s.mapping.title && !!s.mapping.body;
  }

  async #loadSettings() {
    try {
      const s = await call(this, "/settings");
      if (!s) return;
      this.#saved = s;
      const publish = this.querySelector("#mi-publish");
      const canonical = this.querySelector("#mi-canonical");
      setOptions(publish, publish.options, s.publish ? "yes" : "no");
      setOptions(canonical, canonical.options, s.canonical || "ThisSite");
      const parent = this.querySelector("#mi-parent");
      if (s.parentKey && s.parentKey !== "00000000-0000-0000-0000-000000000000") {
        if ("selection" in parent || parent.tagName.toLowerCase() === "umb-input-document") parent.selection = [s.parentKey];
        else parent.value = s.parentKey;
        await this.#onParent();
      }
    } catch (e) {
      this.#fail(e);
    }
  }

  async #onParent() {
    const key = this.#parentKey();
    const type = this.querySelector("#mi-type");
    this.#options = [];
    if (!key) {
      setOptions(type, [{ name: "Pick a parent first", value: "" }]);
      type.disabled = true;
      this.#fillSlots();
      this.#refreshControls();
      return;
    }
    try {
      this.#options = await call(this, `/options?parentKey=${encodeURIComponent(key)}`);
    } catch (e) {
      this.#fail(e);
      return;
    }
    const want = type.value || this.#saved?.docTypeAlias || "";
    setOptions(type, this.#options.length
      ? [{ name: "Choose a document type", value: "" }, ...this.#options.map((o) => ({ name: o.name, value: o.alias }))]
      : [{ name: "No document types are allowed here", value: "" }], want);
    type.disabled = !this.#options.length;
    this.#fillSlots();
    this.#onSettings();
  }

  #onType() {
    this.#fillSlots();
    this.#onSettings();
  }

  #fillSlots() {
    const alias = this.querySelector("#mi-type").value;
    const option = this.#options.find((o) => o.alias === alias);
    for (const [key, , required] of SLOTS) {
      const select = this.querySelector(`#mi-slot-${key}`);
      const keep = select.value || (alias === this.#saved?.docTypeAlias ? this.#saved?.mapping?.[key] : "") || "";
      const props = option?.slots?.[key] ?? [];
      setOptions(select, [{ name: required ? "Choose a property" : "None", value: "" }, ...props.map((p) => ({ name: p.name, value: p.alias }))], keep);
      select.disabled = !option;
    }
  }

  #onSettings() {
    this.#refreshControls();
    if (this.#complete()) {
      call(this, "/settings", "PUT", this.#settings()).catch(() => { /* saved again on import */ });
    }
    if (this.#uploadId) {
      clearTimeout(this.#previewTimer);
      this.#previewTimer = setTimeout(() => this.#loadPreview(), 400);
    }
  }

  // Upload and preview

  async #upload() {
    const file = this.querySelector("#mi-file").files?.[0];
    if (!file) return;
    this.#error = "";
    this.#info = "";
    this.#notice = "";
    this.#result = null;
    this.#busy = true;
    this.#renderBanner();
    this.#renderResult();
    try {
      const form = new FormData();
      form.append("file", file);
      const r = await call(this, "/upload", "POST", form);
      this.#uploadId = r.uploadId;
      this.#checked.clear();
      this.#job = null;
      await this.#loadPreview();
    } catch (e) {
      this.#uploadId = null;
      this.#preview = null;
      this.#fail(e);
    }
    this.#busy = false;
    this.#renderBanner();
    this.#renderList();
    this.#refreshControls();
  }

  async #loadPreview() {
    if (!this.#uploadId) return;
    try {
      this.#preview = await call(this, "/preview", "POST", { uploadId: this.#uploadId, settings: this.#settings() });
      this.#checked.clear();
      this.#error = "";
    } catch (e) {
      this.#preview = null;
      if (e.status === 404 && e.code === "upload") {
        this.#uploadId = null;
        this.#checked.clear();
        this.#fail(new Error("Upload the zip again."));
      } else {
        this.#fail(e);
      }
    }
    this.#renderList();
    this.#refreshControls();
  }

  #disabledStory(s) {
    const status = named(STATUS, s.status);
    return status === "EditedSinceImport" || status === "Filtered" || status === "Draft" || status === "Response";
  }

  #isChecked(s) {
    if (this.#disabledStory(s)) return false;
    if (this.#checked.has(s.postId)) return this.#checked.get(s.postId);
    return named(STATUS, s.status) === "New";
  }

  #renderList() {
    const posts = this.querySelector("#mi-posts");
    const p = this.#preview;
    posts.hidden = !p;
    if (!p) {
      this.querySelector("#mi-list").innerHTML = "";
      this.querySelector("#mi-skipped").textContent = "";
      return;
    }
    this.querySelector("#mi-skipped").textContent = `${count(p.stories.length, "post")}, ${count(p.drafts, "draft")} and ${count(p.responses, "response")} skipped`;
    const rows = p.stories.map((s) => {
      const status = named(STATUS, s.status);
      const off = this.#disabledStory(s);
      const date = s.published ? new Date(s.published).toLocaleDateString() : "";
      const tag = status === "New" ? `look="secondary"` : status === "AlreadyImported" ? `look="primary" color="warning"` : `look="primary" color="danger"`;
      return `<uui-table-row>
        <uui-table-cell><uui-checkbox data-post="${esc(s.postId)}" label="Import ${esc(s.title)}" ${this.#isChecked(s) ? "checked" : ""} ${off || this.#running ? "disabled" : ""}><span class="${off ? "mi-off" : ""}">${esc(s.title)}</span></uui-checkbox>${off && s.reason ? `<div class="mi-note">${esc(s.reason)}</div>` : ""}</uui-table-cell>
        <uui-table-cell>${esc(date)}</uui-table-cell>
        <uui-table-cell>${esc(s.imageCount)}</uui-table-cell>
        <uui-table-cell>${esc(s.gistCount)}</uui-table-cell>
        <uui-table-cell><uui-tag ${tag}>${esc(STATUS_TEXT[status] ?? status)}</uui-tag>${!off && s.reason ? `<div class="mi-note">${esc(s.reason)}</div>` : ""}</uui-table-cell>
      </uui-table-row>`;
    }).join("");
    this.querySelector("#mi-list").innerHTML = `
      <div class="mi-scroll"><uui-table><uui-table-head><uui-table-head-cell>Title</uui-table-head-cell><uui-table-head-cell>Date</uui-table-head-cell><uui-table-head-cell>Images</uui-table-head-cell><uui-table-head-cell>Gists</uui-table-head-cell><uui-table-head-cell>Status</uui-table-head-cell></uui-table-head>
      ${rows}</uui-table></div>`;
  }

  #selectedIds() {
    return (this.#preview?.stories ?? []).filter((s) => this.#isChecked(s)).map((s) => s.postId);
  }

  #refreshControls() {
    const n = this.#selectedIds().length;
    const button = this.querySelector("#mi-import");
    button.textContent = `Import ${n} posts`;
    button.setAttribute("label", `Import ${n} posts`);
    button.disabled = this.#running || this.#busy || n === 0 || !this.#complete() || !this.#uploadId;
    this.querySelector("#mi-cancel").hidden = !this.#running;
    this.querySelector("#mi-progress").hidden = !this.#running && !this.#job;
    for (const box of this.querySelectorAll("#mi-list uui-checkbox[data-post]")) {
      const s = this.#preview?.stories?.find((x) => x.postId === box.dataset.post);
      box.disabled = this.#running || !s || this.#disabledStory(s);
    }
    this.querySelector("#mi-file").disabled = this.#running;
  }

  // Running a job

  async #start() {
    this.#error = "";
    this.#notice = "";
    this.#info = "";
    this.#result = null;
    this.#renderBanner();
    this.#renderResult();
    try {
      const r = await call(this, "/import", "POST", { uploadId: this.#uploadId, postIds: this.#selectedIds(), settings: this.#settings() });
      this.#job = { id: r.jobId, state: "Queued", done: 0, total: this.#selectedIds().length, current: null };
      this.#running = true;
      this.#pollFailures = 0;
      this.#renderProgress();
      this.#refreshControls();
      this.#poll();
    } catch (e) {
      if (e.status === 409) this.#error = "An import is already running on this site. Wait for it to finish.";
      else if (e.status === 404) this.#error = "The uploaded file is gone. Upload the zip again.";
      else this.#error = e.message;
      this.#renderBanner();
    }
  }

  async #poll() {
    clearTimeout(this.#timer);
    if (this.#disconnected) return;
    let ended = false;
    try {
      const j = await call(this, `/jobs/${this.#job.id}`);
      if (this.#disconnected) return;
      this.#pollFailures = 0;
      this.#job = j;
      this.#renderProgress();
      if (j.state === "Queued" || j.state === "Running") {
        this.#timer = setTimeout(() => this.#poll(), 1000);
        return;
      }
      this.#running = false;
      if (j.state === "Failed") this.#error = j.error || "The import failed. The details are in the server log.";
      else if (j.state === "Cancelled") this.#notice = "The import was cancelled. Posts already imported are kept.";
      this.#result = j.result;
      ended = true;
    } catch (e) {
      if (this.#disconnected) return;
      if (e.status !== 404 && this.#pollFailures < 3) {
        this.#pollFailures++;
        this.#timer = setTimeout(() => this.#poll(), 1000);
        return;
      }
      this.#running = false;
      if (e.status === 404) this.#error = STOPPED;
      else this.#error = e.message;
    }
    if (ended) {
      // The server deletes the upload when a job ends, so the list cannot be rebuilt.
      this.#uploadId = null;
      this.#preview = null;
      this.#checked.clear();
      this.#info = "Upload the zip again to import more posts.";
      this.querySelector("#mi-file").value = "";
    }
    this.#renderBanner();
    this.#renderResult();
    this.#renderList();
    this.#refreshControls();
  }

  async #cancel() {
    if (!this.#job) return;
    this.querySelector("#mi-cancel").disabled = true;
    try {
      await call(this, `/jobs/${this.#job.id}/cancel`, "POST");
    } catch (e) {
      this.#fail(e);
    }
    this.querySelector("#mi-cancel").disabled = false;
  }

  #renderProgress() {
    const j = this.#job;
    if (!j) return;
    const pct = j.total ? Math.round((j.done / j.total) * 100) : 0;
    const bar = this.querySelector("#mi-bar");
    bar.progress = pct;
    bar.setAttribute("aria-valuenow", String(pct));
    this.querySelector("#mi-status").textContent = this.#running
      ? `${j.done} of ${j.total}${j.current ? `, ${j.current}` : ""}`
      : `${j.state}: ${j.done} of ${j.total}`;
    this.querySelector("#mi-progress").hidden = false;
  }

  // Output

  #fail(e) {
    this.#error = e.message;
    this.#renderBanner();
  }

  #renderBanner() {
    this.querySelector("#mi-banner").innerHTML =
      (this.#error ? `<div class="mi-msg mi-msg--danger" role="alert">${esc(this.#error)}</div>` : "")
      + (this.#notice ? `<div class="mi-msg mi-msg--warning" role="status">${esc(this.#notice)}</div>` : "")
      + (this.#info ? `<div class="mi-msg">${esc(this.#info)}</div>` : "")
      + (this.#busy ? `<div class="mi-msg">Reading the export...</div>` : "");
  }

  #renderResult() {
    const host = this.querySelector("#mi-result");
    const r = this.#result;
    if (!r) {
      host.innerHTML = "";
      return;
    }
    const groups = OUTCOME.map((name) => [name, r.stories.filter((s) => named(OUTCOME, s.outcome) === name)]);
    const failuresOf = (s) => (s.failures ?? []).filter((f) => f !== s.reason);
    const withFailures = r.stories.filter((s) => named(OUTCOME, s.outcome) !== "Failed" && failuresOf(s).length).length;
    const counts = groups.map(([name, list]) => `${list.length} ${name.toLowerCase()}`).join(", ")
      + (withFailures ? `, ${withFailures} with failures` : "");
    const body = groups.filter(([, list]) => list.length).map(([name, list]) => `
      <h4>${name}</h4>
      <div class="mi-scroll"><uui-table>${list.map((s) => `
        <uui-table-row><uui-table-cell>${s.contentKey ? `<a href="/umbraco/section/content/workspace/document/edit/${esc(s.contentKey)}" target="_blank" rel="noopener">${esc(s.title)}</a>` : esc(s.title)}</uui-table-cell>
        <uui-table-cell>${esc(s.reason ?? "")}${failuresOf(s).map((f) => `<div class="mi-fail">${esc(f)}</div>`).join("")}${(s.warnings ?? []).map((w) => `<div class="mi-note">${esc(w)}</div>`).join("")}</uui-table-cell></uui-table-row>`).join("")}
      </uui-table></div>`).join("");
    const warnings = (r.warnings ?? []).map((w) => `<div class="mi-msg mi-msg--warning">${esc(w)}</div>`).join("");
    host.innerHTML = `<uui-box headline="Result"><div class="mi-note">${esc(counts)}</div>${warnings}${body}</uui-box>`;
  }
}

customElements.define("baryodev-medium-import-dashboard", MediumImportDashboard);

export default MediumImportDashboard;
export { MediumImportDashboard as element };
