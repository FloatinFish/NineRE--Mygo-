import { Launcher, type AccountView, type Prefs, type Snapshot, type UpdateInfo } from "./mygo";
import { currentWindow } from "mygo-runtime";

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => {
  const el = document.querySelector<T>(sel);
  if (!el) throw new Error("找不到元素：" + sel);
  return el;
};

const bgVideo = $<HTMLVideoElement>("#bgVideo");
const logo = $<HTMLImageElement>("#logo");
const bgm = $<HTMLAudioElement>("#bgm");
const accountList = $("#accountList");
const accountThumb = $("#accountThumb");
const accountCount = $("#accountCount");
const detailName = $("#detailName");
const detailSub = $("#detailSub");
const infoRows = $("#infoRows");
const enterBtn = $<HTMLButtonElement>("#enterBtn");
const updateBtn = $<HTMLButtonElement>("#updateBtn");
const restartBtn = $<HTMLButtonElement>("#restartBtn");
const retryBtn = $<HTMLButtonElement>("#retryBtn");
const consoleEl = $("#console");
const consoleBody = $<HTMLPreElement>("#consoleBody");
const consoleToggle = $<HTMLButtonElement>("#consoleToggle");
const chevron = $("#chevron");
const modal = $("#modal");
const modalTitle = $("#modalTitle");
const modalBody = $("#modalBody");
const modalActions = $("#modalActions");
const toastEl = $("#toast");

let snap: Snapshot | null = null;
let selected = "";
let consoleOpen = false;
let consoleMode: "server" | "pg" = "server";
let busy = false;
let bgmStarted = false;
let toastTimer = 0;
let thumbTimer = 0;
let pollTimer = 0;
let updateInfo: UpdateInfo | null = null;

function errText(e: unknown): string {
  return String(e instanceof Error ? e.message : e).replace(/^Error:\s*/, "");
}

function toast(msg: string) {
  toastEl.textContent = msg;
  toastEl.classList.remove("hidden");
  window.clearTimeout(toastTimer);
  toastTimer = window.setTimeout(() => toastEl.classList.add("hidden"), 2800);
}

function fmtDuration(secs: number): string {
  if (!secs || secs <= 0) return "—";
  const h = Math.floor(secs / 3600);
  const m = Math.floor((secs % 3600) / 60);
  if (h > 0) return `${h} 小时 ${m} 分`;
  if (m > 0) return `${m} 分钟`;
  return `${secs} 秒`;
}

function fmtDate(unix: number): string {
  if (!unix || unix <= 0) return "—";
  const d = new Date(unix * 1000);
  const p = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}/${d.getMonth() + 1}/${d.getDate()} ${p(d.getHours())}:${p(d.getMinutes())}`;
}

function initials(name: string): string {
  const t = name.trim();
  return t ? Array.from(t)[0] ?? "?" : "?";
}

function accountSub(a: AccountView): string {
  const lv = a.hasStat && a.statLevel > 0 ? a.statLevel : a.level;
  const parts = [`Lv.${lv || 1}`];
  if (!a.registered) parts.push(snap && !snap.services.postgres ? "服务端检查中…" : "未在服务端登记");
  else {
    if (a.tierName) parts.push(a.tierName);
    if (a.hasStat) parts.push(`${a.species}种/${a.copies}张`);
    else parts.push("已登记 · 无存档");
  }
  return parts.join(" · ");
}

function byId(id: string): AccountView | undefined {
  return snap?.accounts.find((a) => a.name === id);
}

/* ── 渲染 ───────────────────────────────────────────── */

function renderAccounts() {
  const accounts = snap?.accounts ?? [];
  accountCount.textContent = accounts.length ? `${accounts.length} 位` : "";
  accountList.replaceChildren();
  if (!accounts.length) {
    const empty = document.createElement("div");
    empty.className = "empty";
    empty.textContent = "还没有账号。点下面的「添加账号」，第一次进入会自动建号。";
    accountList.append(empty);
    updateThumb();
    return;
  }
  for (const a of accounts) {
    const item = document.createElement("div");
    item.className = "account-item" + (a.name === selected ? " on" : "");
    item.dataset.name = a.name;

    const avatar = document.createElement("div");
    avatar.className = "avatar";
    avatar.textContent = initials(a.name);

    const meta = document.createElement("div");
    meta.className = "account-meta";
    const name = document.createElement("div");
    name.className = "account-name";
    name.textContent = a.name;
    const sub = document.createElement("div");
    sub.className = "account-sub";
    sub.textContent = accountSub(a);
    meta.append(name, sub);

    item.append(avatar, meta);
    item.addEventListener("click", () => void selectAccount(a.name));
    accountList.append(item);
  }
  updateThumb();
}

function infoRow(label: string, value: string): HTMLElement {
  const row = document.createElement("div");
  row.className = "info-row";
  const l = document.createElement("span");
  l.className = "label";
  l.textContent = label;
  const v = document.createElement("span");
  v.className = "value";
  v.textContent = value || "—";
  row.append(l, v);
  return row;
}

function renderDetail() {
  const a = byId(selected);
  if (!a) {
    detailName.textContent = "未选择账号";
    detailSub.textContent = "在左侧选择账号，或点「添加账号」";
    infoRows.replaceChildren();
    return;
  }
  detailName.textContent = a.name;
  detailSub.textContent = !a.registered
    ? (snap && !snap.services.postgres ? "本机账号 · 服务端检查中…" : "本机账号 · 服务端未登记")
    : a.uid
      ? `UID ${a.uid}`
      : "本机账号 · 已登记";
  infoRows.replaceChildren(
    infoRow("等级", String((a.hasStat && a.statLevel > 0 ? a.statLevel : a.level) || 1)),
    infoRow("段位", a.tierName || "—"),
    infoRow("主线进度", a.hasStat ? `${a.stages} 关` : "—"),
    infoRow("卡牌", a.hasStat ? `${a.species} 种 / ${a.copies} 张` : "—"),
    infoRow("金币", a.hasStat ? String(a.money) : "—"),
    infoRow("玉", a.hasStat ? String(a.jade) : "—"),
    infoRow("游玩时长", fmtDuration(a.secs)),
    infoRow("上次游玩", fmtDate(a.last)),
  );
}

function setSeg(root: HTMLElement, selector: string, attr: string, value: number | string) {
  for (const b of root.querySelectorAll<HTMLButtonElement>(selector)) {
    b.classList.toggle("on", b.dataset[attr] === String(value));
  }
}

function renderAction() {
  if (!snap) return;
  const p = snap.prefs;
  setSeg($("#screenMode"), "button", "full", snap.screenFull);
  const sb = $("#serviceBackendSelect") as HTMLSelectElement;
  if (sb) sb.value = snap.serviceBackend || "node";
  const res = $("#resolution") as HTMLSelectElement;
  const key = `${snap.screenW}x${snap.screenH}`;
  if (res.options.length === 0) {
    for (const [w, h] of [[1280, 720], [1600, 900], [1920, 1080], [2560, 1440]] as const) {
      const o = document.createElement("option");
      o.value = `${w}x${h}`;
      o.textContent = `${w} × ${h}`;
      res.append(o);
    }
  }
  if (res.value !== key && res.querySelector(`option[value="${key}"]`)) res.value = key;
  setSeg($("#quality"), "button", "q", p.quality);

  for (const t of document.querySelectorAll<HTMLInputElement>("[data-toggle]")) {
    const k = t.dataset.toggle;
    if (k === "AntiAliasing") t.checked = p.antiAliasing;
    else if (k === "Bloom") t.checked = p.bloom;
    else if (k === "FPSHigh") t.checked = p.fpsHigh;
  }

  ($("#bgmVol") as HTMLInputElement).value = String(Math.round(p.bgmVolume * 100));
  ($("#seVol") as HTMLInputElement).value = String(Math.round(p.seVolume * 100));
  $("#bgmMute").classList.toggle("on", p.bgmMute);
  $("#seMute").classList.toggle("on", p.seMute);

  const s = snap.services;
  const setSvc = (sel: string, ok: boolean) => {
    const row = $(sel);
    row.classList.toggle("ok", ok);
    row.classList.toggle("bad", !ok);
    const b = row.querySelector("b");
    if (b) b.textContent = ok ? "就绪" : "未就绪";
  };
  setSvc("#svcPg", s.postgres);
  setSvc("#svcSrv", s.server);
  setSvc("#svcHttp", s.http);
  retryBtn.classList.toggle("hidden", s.allReady);

  const ready = s.allReady;
  enterBtn.disabled = !ready || snap.gameRunning || !selected || busy;
  enterBtn.textContent = snap.gameRunning
    ? "游戏运行中"
    : busy
      ? "启动中…"
      : ready
        ? "进入游戏"
        : "等待服务就绪…";
}

function renderChrome() {
  if (!snap) return;
  // 先用打包进前端的 LOGO/海报立即出画面，拿到 assetBase 后再无感替换为外部素材。
  if (snap.assetBase && !bgVideo.src) {
    bgVideo.src = `${snap.assetBase}/login_bg.mp4`;
    bgVideo.poster = `${snap.assetBase}/login_bg.jpg`;
    void bgVideo.play().catch(() => {});
  }
  if (snap.assetBase && !logo.dataset.external) {
    logo.dataset.external = "1";
    logo.src = `${snap.assetBase}/title_logo.png`;
  }
  if (snap.assetBase && !bgmStarted) {
    bgm.src = `${snap.assetBase}/bgm_preview.m4a`;
    bgmStarted = true;
    syncBgm();
    void bgm.play().catch(() => {});
  }
}

function syncBgm() {
  const p = snap?.prefs;
  if (!p) return;
  bgm.volume = p.bgmMute ? 0 : Math.max(0, Math.min(1, p.bgmVolume));
}

function renderConsoleTab() {
  for (const b of document.querySelectorAll<HTMLButtonElement>(".console-tabs .tab")) {
    b.classList.toggle("active", b.dataset.log === consoleMode);
  }
}

function renderAll() {
  if (!snap) return;
  renderChrome();
  renderAccounts();
  renderDetail();
  renderAction();
  renderConsoleTab();
}

/* ── 数据刷新 ───────────────────────────────────────── */

async function refresh() {
  try {
    snap = await Launcher.snapshot();
    selected = snap.selected;
    if (snap.error) toast(snap.error);
    renderAll();
  } catch (e) {
    toast("读取状态失败：" + errText(e));
  }
}

async function selectAccount(name: string) {
  try {
    snap = await Launcher.selectAccount(name);
    selected = snap.selected;
    renderAll();
  } catch (e) {
    toast("切换账号失败：" + errText(e));
  }
}

async function ensureServices() {
  toast("正在部署/检查本地服务…");
  try {
    await Launcher.ensureServices();
  } catch (e) {
    toast("本地服务部署失败：" + errText(e));
  }
  await refresh();
}

async function restartServer() {
  if (busy) return;
  busy = true;
  renderAction();
  toast("正在重启服务器…");
  try {
    await Launcher.restartServer();
    toast("服务器已重启");
  } catch (e) {
    toast("重启失败：" + errText(e));
  } finally {
    busy = false;
    await refresh();
  }
}

async function enterGame() {
  if (busy || !snap) return;
  if (!snap.services.allReady) {
    toast("服务还没就绪：" + (snap.services.message || "稍候"));
    return;
  }
  if (snap.gameRunning) {
    toast("游戏已在运行");
    return;
  }
  if (!selected) {
    openAddAccount();
    return;
  }
  busy = true;
  renderAction();
  try {
    bgm.pause();
    const msg = await Launcher.enterGame(selected);
    toast(msg || "正在启动游戏…");
  } catch (e) {
    toast("启动失败：" + errText(e));
  } finally {
    busy = false;
    await refresh();
  }
}

/* ── 弹窗 ───────────────────────────────────────────── */

type ModalAction = { label: string; primary?: boolean; action: () => void | Promise<void> };

function showModal(title: string, body: Node, actions: ModalAction[]) {
  modalTitle.textContent = title;
  modalBody.replaceChildren(body);
  modalActions.replaceChildren();
  for (const a of actions) {
    const b = document.createElement("button");
    b.className = a.primary ? "primary" : "ghost";
    b.textContent = a.label;
    b.addEventListener("click", () => void a.action());
    modalActions.append(b);
  }
  modal.classList.remove("hidden");
}

function closeModal() {
  modal.classList.add("hidden");
}

function openAddAccount() {
  const wrap = document.createElement("div");
  wrap.textContent = "输入账号名。密码可留空；第一次进入会自动建号。";
  const input = document.createElement("input");
  input.id = "accName";
  input.placeholder = "账号名";
  input.autocomplete = "off";
  const pwd = document.createElement("input");
  pwd.id = "accPwd";
  pwd.type = "password";
  pwd.placeholder = "密码（可留空）";
  pwd.autocomplete = "off";
  wrap.append(input, pwd);
  showModal("添加账号", wrap, [
    { label: "取消", action: closeModal },
    {
      label: "添加",
      primary: true,
      action: async () => {
        const name = input.value.trim();
        if (!name) {
          toast("账号名不能为空");
          return;
        }
        try {
          snap = await Launcher.addAccount(name, pwd.value);
          selected = snap.selected;
          closeModal();
          renderAll();
          toast(`已添加账号 ${name}`);
        } catch (e) {
          toast(errText(e));
        }
      },
    },
  ]);
  window.setTimeout(() => input.focus(), 30);
}

function openDeleteAccount() {
  const a = byId(selected);
  if (!a) {
    toast("先选择一个账号");
    return;
  }
  if (snap?.gameRunning) {
    toast("游戏运行中不能删除账号");
    return;
  }
  const body = document.createElement("div");
  body.textContent = `从本机列表移除「${a.name}」？\n服务端存档不会被删除，重新添加同名账号即可找回进度。`;
  showModal("删除账号", body, [
    { label: "取消", action: closeModal },
    {
      label: "删除",
      primary: true,
      action: async () => {
        try {
          snap = await Launcher.deleteAccount(a.name);
          selected = snap.selected;
          closeModal();
          renderAll();
          toast("已移除 " + a.name);
        } catch (e) {
          toast(errText(e));
        }
      },
    },
  ]);
}

async function checkUpdate(manual = false) {
  try {
    updateInfo = await Launcher.checkUpdate();
    renderUpdateButton();
    if (manual) {
      if (updateInfo.error) toast(updateInfo.error);
      else if (updateInfo.available) toast(`发现新版本 v${updateInfo.version}`);
      else toast("已是最新版本");
    }
  } catch (e) {
    if (manual) toast("检查更新失败：" + errText(e));
  }
}

function renderUpdateButton() {
  if (updateInfo?.available) {
    updateBtn.textContent = `更新到 v${updateInfo.version}`;
    updateBtn.classList.add("update-available");
  } else {
    updateBtn.textContent = "更新启动器";
    updateBtn.classList.remove("update-available");
  }
}

function onUpdateClick() {
  if (!updateInfo?.available) {
    void checkUpdate(true);
    return;
  }
  const info = updateInfo;
  const body = document.createElement("div");
  body.textContent =
    `当前版本 ${info.current} → 新版本 ${info.version}

` +
    (info.notes || "（这个版本没有更新说明）");
  showModal("更新启动器", body, [
    { label: "稍后", action: closeModal },
    {
      label: "立即更新",
      primary: true,
      action: async () => {
        closeModal();
        toast("正在下载更新，启动器会自动重启…");
        try {
          await Launcher.applyUpdate(info.url, info.sumsUrl);
        } catch (e) {
          toast("更新失败：" + errText(e));
        }
      },
    },
  ]);
}

function openFeedback() {
  const wrap = document.createElement("div");
  wrap.textContent = "写下你的反馈或建议。提交后会在浏览器打开 GitHub Issue 页面，确认发布即可。";
  const ta = document.createElement("textarea");
  ta.id = "fbText";
  ta.placeholder = "例如：希望增加……";
  ta.rows = 7;
  ta.addEventListener("keydown", (ev) => {
    if ((ev.ctrlKey || ev.metaKey) && ev.key === "Enter") {
      ev.preventDefault();
      modalActions.querySelector<HTMLButtonElement>(".primary")?.click();
    }
  });
  wrap.append(ta);
  showModal("反馈", wrap, [
    { label: "取消", action: closeModal },
    {
      label: "一键提交",
      primary: true,
      action: async () => {
        const text = ta.value.trim();
        if (!text) {
          toast("先写点内容");
          return;
        }
        try {
          await Launcher.submitFeedback(text);
          closeModal();
          toast("已在浏览器打开 GitHub，确认后提交即可");
        } catch (e) {
          toast("打开失败：" + errText(e));
        }
      },
    },
  ]);
  window.setTimeout(() => ta.focus(), 30);
}

function openAnnouncement() {
  const body = document.createElement("div");
  body.textContent = snap?.announcement || "暂无公告。";
  showModal("公告", body, [{ label: "知道了", primary: true, action: closeModal }]);
}

/* ── 账号列表滚动条 ─────────────────────────────────── */

function updateThumb() {
  const ratio = accountList.clientHeight / Math.max(1, accountList.scrollHeight);
  if (ratio >= 1) {
    accountThumb.style.opacity = "0";
    return;
  }
  const h = Math.max(28, accountList.clientHeight * ratio);
  const maxTop = Math.max(0, accountList.clientHeight - h);
  const top = accountList.scrollHeight <= accountList.clientHeight
    ? 0
    : (accountList.scrollTop / (accountList.scrollHeight - accountList.clientHeight)) * maxTop;
  accountThumb.style.height = `${h}px`;
  accountThumb.style.top = `${top}px`;
}

function revealThumb() {
  updateThumb();
  accountThumb.style.opacity = "1";
  window.clearTimeout(thumbTimer);
  thumbTimer = window.setTimeout(() => {
    accountThumb.style.opacity = "0";
  }, 900);
}

/* ── 控制台 ─────────────────────────────────────────── */

async function refreshConsole() {
  if (!consoleOpen) return;
  try {
    const text = await Launcher.logs(consoleMode);
    if (consoleBody.textContent !== text) {
      consoleBody.textContent = text;
      consoleBody.scrollTop = consoleBody.scrollHeight;
    }
  } catch {
    /* 日志读不到就留空 */
  }
}

function toggleConsole() {
  consoleOpen = !consoleOpen;
  consoleEl.classList.toggle("open", consoleOpen);
  chevron.textContent = consoleOpen ? "▾" : "▸";
  if (consoleOpen) void refreshConsole();
}

/* ── 事件绑定 ───────────────────────────────────────── */

function bindEvents() {
  $("#minBtn").addEventListener("click", () => void currentWindow.minimize());
  $("#closeBtn").addEventListener("click", () => void currentWindow.close());
  $("#announceBtn").addEventListener("click", openAnnouncement);
  $("#feedbackBtn").addEventListener("click", openFeedback);
  $("#addBtn").addEventListener("click", openAddAccount);
  $("#deleteBtn").addEventListener("click", openDeleteAccount);
  restartBtn.addEventListener("click", () => void restartServer());
  retryBtn.addEventListener("click", () => void ensureServices());
  enterBtn.addEventListener("click", () => void enterGame());
  updateBtn.addEventListener("click", () => void onUpdateClick());
  consoleToggle.addEventListener("click", toggleConsole);

  for (const b of document.querySelectorAll<HTMLButtonElement>(".console-tabs .tab")) {
    b.addEventListener("click", () => {
      consoleMode = b.dataset.log === "pg" ? "pg" : "server";
      renderConsoleTab();
      void refreshConsole();
    });
  }

  for (const b of document.querySelectorAll<HTMLButtonElement>("#screenMode button")) {
    b.addEventListener("click", async () => {
      if (!snap) return;
      const full = Number(b.dataset.full ?? "0");
      const w = snap.screenW || 1920;
      const h = snap.screenH || 1080;
      try {
        snap = await Launcher.setScreen(full, w, h);
        renderAction();
      } catch (e) {
        toast("写入画面设置失败：" + errText(e));
      }
    });
  }

  ($("#serviceBackendSelect") as HTMLSelectElement | null)?.addEventListener("change", async (ev) => {
    if (!snap) return;
    const backend = (ev.target as HTMLSelectElement).value || "node";
    if ((snap.serviceBackend || "node") === backend) return;
    try {
      snap = await Launcher.setServiceBackend(backend);
      renderAction();
      toast(backend === "go"
        ? "已切换 GO（实验，仍在内测迭代）：正在重启服务器…"
        : "已切换 Node：正在重启服务器…");
      await restartServer();
    } catch (e) {
      toast("切换失败：" + errText(e));
      renderAction();
    }
  });

  ($("#resolution") as HTMLSelectElement).addEventListener("change", async (ev) => {
    if (!snap) return;
    const v = (ev.target as HTMLSelectElement).value;
    const [w, h] = v.split("x").map(Number);
    if (!w || !h) return;
    try {
      snap = await Launcher.setScreen(snap.screenFull, w, h);
      renderAction();
    } catch (e) {
      toast("写入分辨率失败：" + errText(e));
    }
  });

  for (const b of document.querySelectorAll<HTMLButtonElement>("#quality button")) {
    b.addEventListener("click", async () => {
      try {
        const p = await Launcher.setQuality(Number(b.dataset.q ?? "3"));
        if (snap) snap.prefs = p;
        renderAction();
      } catch (e) {
        toast("写入画质失败：" + errText(e));
      }
    });
  }

  for (const t of document.querySelectorAll<HTMLInputElement>("[data-toggle]")) {
    t.addEventListener("change", async () => {
      const key = t.dataset.toggle;
      if (!key) return;
      try {
        const p = await Launcher.setToggle(key, t.checked);
        if (snap) snap.prefs = p;
        renderAction();
      } catch (e) {
        toast("写入画面开关失败：" + errText(e));
      }
    });
  }

  const bgmVol = $("#bgmVol") as HTMLInputElement;
  const seVol = $("#seVol") as HTMLInputElement;
  bgmVol.addEventListener("input", () => {
    const v = Number(bgmVol.value) / 100;
    bgm.volume = snap?.prefs.bgmMute ? 0 : v;
  });
  bgmVol.addEventListener("change", async () => {
    try {
      const p = await Launcher.setVolume("bgm", Number(bgmVol.value) / 100);
      if (snap) snap.prefs = p;
      syncBgm();
    } catch (e) {
      toast("写入音乐音量失败：" + errText(e));
    }
  });
  seVol.addEventListener("change", async () => {
    try {
      const p = await Launcher.setVolume("se", Number(seVol.value) / 100);
      if (snap) snap.prefs = p;
    } catch (e) {
      toast("写入音效音量失败：" + errText(e));
    }
  });

  $("#bgmMute").addEventListener("click", async () => {
    if (!snap) return;
    try {
      const p = await Launcher.setMute("bgm", !snap.prefs.bgmMute);
      snap.prefs = p;
      syncBgm();
      renderAction();
    } catch (e) {
      toast("写入静音失败：" + errText(e));
    }
  });
  $("#seMute").addEventListener("click", async () => {
    if (!snap) return;
    try {
      const p = await Launcher.setMute("se", !snap.prefs.seMute);
      snap.prefs = p;
      renderAction();
    } catch (e) {
      toast("写入静音失败：" + errText(e));
    }
  });

  accountList.addEventListener("scroll", revealThumb);
  accountList.addEventListener("mouseenter", () => {
    if (accountList.scrollHeight > accountList.clientHeight + 1) revealThumb();
  });
  window.addEventListener("resize", updateThumb);

  modal.addEventListener("click", (ev) => {
    if (ev.target === modal) closeModal();
  });

  window.addEventListener("keydown", (ev) => {
    const tag = (ev.target as HTMLElement | null)?.tagName;
    const typing = tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT";
    if (ev.key === "Escape") {
      if (!modal.classList.contains("hidden")) closeModal();
      else if (consoleOpen) toggleConsole();
      return;
    }
    if (typing) {
      if (ev.key === "Enter") {
        const btn = modalActions.querySelector<HTMLButtonElement>(".primary");
        btn?.click();
      }
      return;
    }
    if (ev.key === "Enter") {
      ev.preventDefault();
      void enterGame();
    } else if (ev.key === "Delete") {
      ev.preventDefault();
      openDeleteAccount();
    } else if (ev.ctrlKey && ev.key.toLowerCase() === "r") {
      ev.preventDefault();
      void restartServer();
    } else if (ev.ctrlKey && ev.key.toLowerCase() === "n") {
      ev.preventDefault();
      openAddAccount();
    }
  });

  document.addEventListener("pointerdown", () => {
    if (bgmStarted && bgm.paused && !snap?.gameRunning) {
      void bgm.play().catch(() => {});
    }
  }, { once: true });
}

/* ── 启动 ───────────────────────────────────────────── */

bindEvents();
void refresh().then(() => {
  window.setTimeout(() => void checkUpdate(), 2500);
  void ensureServices();
  pollTimer = window.setInterval(() => void refresh(), 1200);
  window.setInterval(() => {
    if (consoleOpen) void refreshConsole();
  }, 2000);
});

window.addEventListener("beforeunload", () => {
  window.clearInterval(pollTimer);
});
