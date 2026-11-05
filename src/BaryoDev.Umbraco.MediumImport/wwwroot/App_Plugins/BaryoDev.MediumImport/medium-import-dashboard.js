// Plain custom element with no build step, same approach as the AI Assistant dashboards.
// The base comes from the backoffice's own UmbElementMixin so the auth context is reachable.
const API = "/umbraco/management/api/v1/medium-import";
const BASE = "/App_Plugins/BaryoDev.MediumImport";
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
      const box = e.target.closest("input[data-post]");
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

  #shell(hasPicker) {
    return `
      <link rel="stylesheet" href="${BASE}/brand.css">
      <div class="mi">
        <div class="mi-head"><img src="${BASE}/logo.svg" alt="baryo.dev"><span>Medium import</span></div>
        <div id="mi-banner"></div>
        <div class="mi-grid">
          <div>
            <div class="mi-box">
              <h3>Upload</h3>
              <div class="mi-row"><label for="mi-file">Medium export zip</label>
                <input type="file" id="mi-file" accept=".zip,application/zip"></div>
              <div class="mi-note">Export it from Medium under Settings, Security and apps, Download your information.</div>
            </div>
            <div class="mi-box" id="mi-posts" hidden>
              <h3>Posts</h3>
              <div class="mi-note" id="mi-skipped"></div>
              <div id="mi-list"></div>
            </div>
          </div>
          <div>
            <div class="mi-box">
              <h3>Settings</h3>
              <div class="mi-row"><label>Parent page</label>
                ${hasPicker ? `<umb-input-document id="mi-parent" max="1"></umb-input-document>` : `<input id="mi-parent" placeholder="Parent key (GUID)" style="padding:6px;font:inherit">`}</div>
              <div class="mi-row"><label for="mi-type">Document type</label>
                <select id="mi-type" disabled><option value="">Pick a parent first</option></select></div>
              ${SLOTS.map(([key, label, required]) => `
              <div class="mi-row"><label for="mi-slot-${key}">${label}${required ? " (required)" : ""}</label>
                <select id="mi-slot-${key}" disabled></select></div>`).join("")}
              <div class="mi-row"><label for="mi-publish">Publish</label>
                <select id="mi-publish"><option value="no" selected>Save as draft</option><option value="yes">Yes</option></select></div>
              <div class="mi-row"><label for="mi-canonical">Canonical</label>
                <select id="mi-canonical"><option value="ThisSite" selected>This site</option><option value="KeepMedium">Keep Medium</option></select></div>
            </div>
            <div class="mi-box">
              <div class="mi-actions">
                <button class="mi-btn primary" id="mi-import" disabled>Import 0 posts</button>
                <button class="mi-btn" id="mi-cancel" hidden>Cancel</button>
              </div>
              <div id="mi-progress" hidden>
                <div class="mi-bar"><div id="mi-fill" style="width:0"></div></div>
                <div class="mi-note" id="mi-status"></div>
              </div>
            </div>
          </div>
        </div>
        <div id="mi-result"></div>
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
      this.querySelector("#mi-publish").value = s.publish ? "yes" : "no";
      this.querySelector("#mi-canonical").value = s.canonical || "ThisSite";
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
      type.innerHTML = `<option value="">Pick a parent first</option>`;
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
    type.innerHTML = this.#options.length
      ? `<option value="">Choose a document type</option>` + this.#options.map((o) => `<option value="${esc(o.alias)}">${esc(o.name)}</option>`).join("")
      : `<option value="">No document types are allowed here</option>`;
    type.disabled = !this.#options.length;
    if (this.#options.some((o) => o.alias === want)) type.value = want;
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
      select.innerHTML = (required ? `<option value="">Choose a property</option>` : `<option value="">None</option>`)
        + props.map((p) => `<option value="${esc(p.alias)}">${esc(p.name)}</option>`).join("");
      select.disabled = !option;
      if (props.some((p) => p.alias === keep)) select.value = keep;
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
      const tag = status === "New" ? "" : status === "AlreadyImported" ? "warn" : "bad";
      return `<tr class="${off ? "off" : ""}">
        <td><input type="checkbox" data-post="${esc(s.postId)}" ${this.#isChecked(s) ? "checked" : ""} ${off || this.#running ? "disabled" : ""} aria-label="Import ${esc(s.title)}"></td>
        <td>${esc(s.title)}${off && s.reason ? `<div class="mi-note">${esc(s.reason)}</div>` : ""}</td>
        <td>${esc(date)}</td>
        <td>${s.imageCount}</td>
        <td>${s.gistCount}</td>
        <td><span class="mi-tag ${tag}">${esc(STATUS_TEXT[status] ?? status)}</span>${!off && s.reason ? `<div class="mi-note">${esc(s.reason)}</div>` : ""}</td>
      </tr>`;
    }).join("");
    this.querySelector("#mi-list").innerHTML = `
      <table class="mi-table"><thead><tr><th></th><th>Title</th><th>Date</th><th>Images</th><th>Gists</th><th>Status</th></tr></thead>
      <tbody>${rows}</tbody></table>`;
  }

  #selectedIds() {
    return (this.#preview?.stories ?? []).filter((s) => this.#isChecked(s)).map((s) => s.postId);
  }

  #refreshControls() {
    const n = this.#selectedIds().length;
    const button = this.querySelector("#mi-import");
    button.textContent = `Import ${n} posts`;
    button.disabled = this.#running || this.#busy || n === 0 || !this.#complete() || !this.#uploadId;
    this.querySelector("#mi-cancel").hidden = !this.#running;
    this.querySelector("#mi-progress").hidden = !this.#running && !this.#job;
    for (const box of this.querySelectorAll("#mi-list input[data-post]")) {
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
    this.querySelector("#mi-fill").style.width = `${pct}%`;
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
      (this.#error ? `<div class="mi-err" role="alert">${esc(this.#error)}</div>` : "")
      + (this.#notice ? `<div class="mi-warn">${esc(this.#notice)}</div>` : "")
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
      <table class="mi-table"><tbody>${list.map((s) => `
        <tr><td>${s.contentKey ? `<a href="/umbraco/section/content/workspace/document/edit/${esc(s.contentKey)}" target="_blank" rel="noopener">${esc(s.title)}</a>` : esc(s.title)}</td>
        <td>${esc(s.reason ?? "")}${failuresOf(s).map((f) => `<div class="mi-err">${esc(f)}</div>`).join("")}${(s.warnings ?? []).map((w) => `<div class="mi-note">${esc(w)}</div>`).join("")}</td></tr>`).join("")}
      </tbody></table>`).join("");
    const warnings = (r.warnings ?? []).map((w) => `<div class="mi-warn">${esc(w)}</div>`).join("");
    host.innerHTML = `<div class="mi-box"><h3>Result</h3><div class="mi-note">${esc(counts)}</div>${warnings}${body}</div>`;
  }
}

customElements.define("baryodev-medium-import-dashboard", MediumImportDashboard);

export default MediumImportDashboard;
export { MediumImportDashboard as element };
