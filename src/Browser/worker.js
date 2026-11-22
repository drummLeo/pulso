importScripts("conexao.js");

const pausePage = chrome.runtime.getURL("pausa.html");
const ruleIds = [1, 2];
let socket, expiryTimer, reconnectTimer;
let leaseEnd = 0, focusEnd = 0, revision = -1, sample = 0;
let focusSites = [], limits = [], watched = [], blocked = [];
let supportsUsage = false, lastReport = null, signature = null;
let savedTheme;
let queue = Promise.resolve();

function ordered(action) {
  const task = queue.then(action);
  queue = task.catch(error => console.warn("Pulso:", error.message));
  return task;
}

function covers(domain, host) {
  return host === domain || host.endsWith("." + domain);
}

function pageHost(rawUrl, includePause = false) {
  if (!rawUrl) return "";
  try {
    const url = new URL(rawUrl);
    if (url.protocol === "http:" || url.protocol === "https:") return url.hostname.toLowerCase().replace(/\.$/, "");
    if (includePause && url.href.split("#")[0] === pausePage) return url.hash.slice(1).toLowerCase().replace(/\.$/, "");
  } catch { /* The tab may still be opening. */ }
  return "";
}

function pauseFor(host, now = Date.now()) {
  if (!host || now >= leaseEnd) return { until: 0, limit: false };
  const session = focusEnd > now && focusSites.some(d => covers(d, host)) ? focusEnd : 0;
  let quota = 0;
  for (const item of limits) if (item.until > now && covers(item.domain, host)) quota = Math.max(quota, item.until);
  return { until: Math.max(session, quota), limit: quota > 0 && quota >= session };
}

async function pauseTab(tab) {
  if (tab.id == null || !pauseFor(pageHost(tab.url)).until) return;
  try {
    const current = await chrome.tabs.get(tab.id);
    const host = pageHost(current.url);
    if (!pauseFor(host).until) return;
    await chrome.storage.session.set({ ["tab-" + tab.id]: current.url });
    await chrome.tabs.update(tab.id, { url: pausePage + "#" + host });
  } catch { /* The tab may have closed between the two operations. */ }
}

async function refreshRules() {
  const now = Date.now(), live = leaseEnd > now;
  const focus = live && focusEnd > now ? focusSites : [];
  const timed = live ? limits.filter(l => l.until > now) : [];
  const next = [...new Set([...focus, ...timed.map(l => l.domain)])].sort();
  clearTimeout(expiryTimer);
  if (next.length) {
    let nextChange = leaseEnd;
    if (focus.length) nextChange = Math.min(nextChange, focusEnd);
    for (const l of timed) nextChange = Math.min(nextChange, l.until);
    expiryTimer = setTimeout(() => ordered(refreshRules), Math.max(0, nextChange - now) + 5);
    await chrome.alarms.create("prazo", { when: nextChange });
  } else {
    await chrome.alarms.clear("prazo");
  }
  const nextSignature = JSON.stringify(next);
  if (signature === nextSignature) return;
  const rules = next.length ? [
    { id: 1, priority: 1, action: { type: "redirect", redirect: { regexSubstitution: pausePage + "#\\2" } },
      condition: { requestDomains: next, regexFilter: "^https?://([^/?#]*@)?([^/?#:]+)(:[0-9]+)?([/?#].*)?$", resourceTypes: ["main_frame"] } },
    { id: 2, priority: 1, action: { type: "block" }, condition: { requestDomains: next, resourceTypes: ["sub_frame"] } }
  ] : [];
  await chrome.declarativeNetRequest.updateSessionRules({ removeRuleIds: ruleIds, addRules: rules });
  signature = nextSignature;
  blocked = next;
  if (next.length) await Promise.all((await chrome.tabs.query({})).map(pauseTab));
  void reportForeground();
}

async function reportForeground(force = false) {
  const connection = socket, generation = ++sample, rev = revision;
  if (!supportsUsage || connection?.readyState !== WebSocket.OPEN || rev < 0) return;
  let site = "";
  if (watched.length && Date.now() < leaseEnd) {
    try {
      const win = await chrome.windows.getLastFocused();
      if (win.focused && win.state !== "minimized" && (win.type === "normal" || win.type === "popup")) {
        const [tab] = await chrome.tabs.query({ windowId: win.id, active: true });
        let host = pageHost(tab?.url);
        if (!host) {
          const pausedHost = pageHost(tab?.url, true);
          if (pauseFor(pausedHost).until) host = pausedHost;
        }
        site = watched.find(domain => covers(domain, host)) || "";
      }
    } catch { /* A disappearing or unfocused window contributes no usage. */ }
  }
  if (generation !== sample || rev !== revision || socket !== connection || connection.readyState !== WebSocket.OPEN) return;
  const report = "use:" + rev + ":" + site;
  if (force || report !== lastReport) connection.send(report);
  lastReport = report;
}

async function acceptPolicy(policy) {
  revision = policy.revision;
  focusSites = policy.sites;
  focusEnd = policy.until;
  supportsUsage = policy.version === 2;
  watched = (policy.watch || []).slice().sort((a, b) => b.length - a.length);
  limits = policy.limits || [];
  leaseEnd = Date.now() + 40000;
  await refreshRules();
  if (supportsUsage) await reportForeground(true);
  else if (socket?.readyState === WebSocket.OPEN) socket.send("ok:" + revision);
  const theme = policy.theme === "dark" || policy.theme === "light" ? policy.theme : "system";
  if (theme !== savedTheme) {
    try {
      await chrome.storage.local.set({ theme });
      savedTheme = theme;
    } catch { /* A próxima atualização tentará salvar a aparência de novo. */ }
  }
}

function connect() {
  if (socket && socket.readyState < WebSocket.CLOSING) return;
  clearTimeout(reconnectTimer);
  const port = typeof PULSO_PORT === "number" ? PULSO_PORT : 38473;
  const current = new WebSocket("ws://127.0.0.1:" + port + "/foco", PULSO_PROTOCOL);
  socket = current;
  current.onopen = () => current.send(/Edg\//.test(navigator.userAgent) ? "Edge" : "Chrome");
  current.onmessage = event => {
    ordered(async () => {
      if (socket === current && current.readyState === WebSocket.OPEN) await acceptPolicy(JSON.parse(event.data));
    }).catch(() => current.close());
  };
  current.onclose = () => {
    if (socket !== current) return;
    socket = null;
    ordered(async () => {
      leaseEnd = focusEnd = 0;
      revision = -1;
      supportsUsage = false;
      lastReport = null;
      watched = []; limits = []; focusSites = [];
      await refreshRules();
    });
    reconnectTimer = setTimeout(connect, 4000);
  };
}

function changedTab() {
  connect();
  void reportForeground();
}
chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === "prazo") ordered(refreshRules);
  connect();
});
chrome.windows.onFocusChanged.addListener(changedTab);
chrome.tabs.onActivated.addListener(changedTab);
chrome.tabs.onUpdated.addListener((id, change, tab) => {
  if (change.url && pauseFor(pageHost(change.url)).until) ordered(() => pauseTab(tab));
  if (tab.active && (change.url || change.status === "complete")) changedTab();
});
chrome.tabs.onRemoved.addListener(id => chrome.storage.session.remove("tab-" + id));
chrome.runtime.onMessage.addListener((message, sender, respond) => {
  if (sender.id !== chrome.runtime.id || (message !== "estado" && message?.type !== "pagina")) return;
  connect();
  const connected = socket?.readyState === WebSocket.OPEN;
  if (message?.type === "pagina") {
    const host = pageHost(sender.url, true) || message.host || pageHost(message.previousUrl);
    respond({ connected, ...pauseFor(host) });
  } else {
    respond({ connected, count: blocked.length, watched: watched.length });
  }
});
ordered(async () => {
  await refreshRules();
  await chrome.alarms.create("conectar", { periodInMinutes: 0.5 });
  connect();
});
