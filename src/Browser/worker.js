if (typeof importScripts === "function") importScripts("conexao.js");

const pausePage = chrome.runtime.getURL("pausa.html");
const filters = [
  { key: "porn", name: "Pornografia", group: 1, until: 0, enabled: false, domains: null },
  { key: "gambling", name: "Apostas", group: 2, until: 0, enabled: false, domains: null }
];
// One redirect and one subframe rule per chunk. Reserve at most 192 regex rules
// across the two catalogs and manual sites, including when the catalogs grow.
const chunksPerGroup = 64;
let socket, expiryTimer, reconnectTimer;
let leaseEnd = 0, focusEnd = 0, nextPolicyEnd = 0, scheduledAt = 0, revision = -1, sample = 0;
let focusSites = [], limits = [], watched = [], blocked = [];
let supportsUsage = false, lastReport = null;
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

function matchesCatalog(domains, host) {
  while (host) {
    if (domains.has(host)) return true;
    const dot = host.indexOf(".");
    if (dot < 0) break;
    host = host.slice(dot + 1);
  }
  return false;
}

function activeUntil(until, now) {
  return until === -1 || until > now;
}

function pauseFor(host, now = Date.now()) {
  if (!host || now >= leaseEnd) return { until: 0, limit: false };
  const session = focusEnd > now && focusSites.some(d => covers(d, host)) ? focusEnd : 0;
  let quota = 0;
  for (const item of limits) if (item.until > now && covers(item.domain, host)) quota = Math.max(quota, item.until);
  let category = null;
  for (const filter of filters) {
    if (!filter.enabled || !activeUntil(filter.until, now) || !matchesCatalog(filter.domains, host)) continue;
    if (!category || filter.until === -1 || filter.until > category.until) category = filter;
    if (category.until === -1) break;
  }
  if (category && (category.until === -1 || (category.until >= session && category.until > quota))) {
    return { until: category.until, category: category.name, filtered: true };
  }
  return { until: Math.max(session, quota), limit: quota > 0 && quota >= session, filtered: !!category };
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

function groupIds(group) {
  return Array.from({ length: chunksPerGroup * 2 }, (_, i) => group * chunksPerGroup * 2 + i + 1);
}

function replaceGroup(changes, group, domains) {
  const ids = groupIds(group);
  changes.removeRuleIds.push(...ids);
  const size = Math.max(4096, Math.ceil(domains.length / chunksPerGroup));
  for (let start = 0, index = 0; start < domains.length; start += size, index += 2) {
    const requestDomains = domains.slice(start, start + size);
    changes.addRules.push(
      { id: ids[index], priority: 1, action: { type: "redirect", redirect: { regexSubstitution: pausePage + "#\\2" } },
        condition: { requestDomains, regexFilter: "^https?://([^/?#]*@)?([^/?#:]+)(:[0-9]+)?([/?#].*)?$", resourceTypes: ["main_frame"] } },
      { id: ids[index + 1], priority: 1, action: { type: "block" }, condition: { requestDomains, resourceTypes: ["sub_frame"] } }
    );
  }
}

async function loadDomains(filter) {
  const response = await fetch(chrome.runtime.getURL("site_" + filter.key + ".txt"));
  if (!response.ok) throw new Error("Falha ao ler o catálogo de " + filter.name);
  return (await response.text()).trim().split(/\r?\n/);
}

async function scheduleExpiry(when) {
  if (when === scheduledAt) return;
  clearTimeout(expiryTimer);
  scheduledAt = when;
  if (when) {
    expiryTimer = setTimeout(() => ordered(expire), Math.max(0, when - Date.now()) + 5);
    await chrome.alarms.create("prazo", { when });
  } else await chrome.alarms.clear("prazo");
}

function notifyPages() {
  chrome.runtime.sendMessage("politica").catch(() => { /* No open extension page. */ });
}

async function expire() {
  const now = Date.now();
  if (!scheduledAt || scheduledAt > now) return;
  if (leaseEnd > now && nextPolicyEnd > now) {
    await scheduleExpiry(Math.min(leaseEnd, nextPolicyEnd));
    return;
  }
  await refreshRules();
  notifyPages();
  await reportForeground();
}

async function refreshRules() {
  const now = Date.now(), live = leaseEnd > now;
  const focus = live && focusEnd > now ? focusSites : [];
  const timed = live ? limits.filter(l => l.until > now) : [];
  const manual = [...new Set([...focus, ...timed.map(l => l.domain)])].sort();
  const enabled = filters.filter(filter => live && activeUntil(filter.until, now));
  const changes = { removeRuleIds: [], addRules: [] };
  let scanTabs = false;
  if (manual.length !== blocked.length || manual.some((domain, i) => domain !== blocked[i])) {
    replaceGroup(changes, 0, manual);
    const previous = new Set(blocked);
    scanTabs = manual.some(domain => !previous.has(domain));
  }
  for (const filter of filters) {
    const enable = enabled.includes(filter);
    if (enable === filter.enabled) continue;
    const domains = enable ? await loadDomains(filter) : [];
    replaceGroup(changes, filter.group, domains);
    if (enable) {
      filter.domains = new Set(domains);
      scanTabs = true;
    }
  }
  if (changes.removeRuleIds.length) {
    await chrome.declarativeNetRequest.updateSessionRules(changes);
    blocked = manual;
    for (const filter of filters) {
      filter.enabled = enabled.includes(filter);
      if (!filter.enabled) filter.domains = null;
    }
  }
  nextPolicyEnd = focus.length ? focusEnd : Infinity;
  for (const item of [...timed, ...enabled]) {
    if (item.until > 0) nextPolicyEnd = Math.min(nextPolicyEnd, item.until);
  }
  await scheduleExpiry(live ? Math.min(nextPolicyEnd, leaseEnd) : 0);
  if (scanTabs) await Promise.all((await chrome.tabs.query({})).map(pauseTab));
}

async function reportForeground(force = false) {
  const connection = socket, generation = ++sample, rev = revision;
  if (!supportsUsage || connection?.readyState !== WebSocket.OPEN || rev < 0) return;
  let site = "";
  if ((watched.length || filters.some(filter => filter.enabled)) && Date.now() < leaseEnd) {
    try {
      const win = await chrome.windows.getLastFocused();
      if (win.focused && win.state !== "minimized" && (win.type === "normal" || win.type === "popup")) {
        const [tab] = await chrome.tabs.query({ windowId: win.id, active: true });
        let host = pageHost(tab?.url);
        if (!host) {
          const pausedHost = pageHost(tab?.url, true);
          const pause = pauseFor(pausedHost);
          if (pause.filtered) site = "@pause";
          else if (pause.until) host = pausedHost;
        }
        if (!site) site = watched.find(domain => covers(domain, host)) || "";
      }
    } catch { /* A disappearing or unfocused window contributes no usage. */ }
  }
  if (generation !== sample || rev !== revision || socket !== connection || connection.readyState !== WebSocket.OPEN) return;
  const report = "use:" + rev + ":" + site;
  if (force || report !== lastReport) connection.send(report);
  lastReport = report;
}

async function acceptPolicy(policy) {
  const changed = policy.revision !== revision || Date.now() >= leaseEnd;
  leaseEnd = Date.now() + 40000;
  if (!changed && lastReport && socket?.readyState === WebSocket.OPEN) {
    socket.send(lastReport);
    return;
  }
  revision = policy.revision;
  focusSites = policy.sites;
  focusEnd = policy.until;
  supportsUsage = policy.version === 2;
  watched = (policy.watch || []).slice().sort((a, b) => b.length - a.length);
  limits = policy.limits || [];
  for (const filter of filters) filter.until = policy.filters?.[filter.key] || 0;
  await refreshRules();
  if (changed) notifyPages();
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
  current.onopen = () => current.send(/Edg\//.test(navigator.userAgent) ? "Edge/1.5" : /Firefox\//.test(navigator.userAgent) ? "Firefox/1.5" : "Chrome/1.5");
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
      for (const filter of filters) filter.until = 0;
      await refreshRules();
      notifyPages();
    });
    reconnectTimer = setTimeout(connect, 4000);
  };
}

function changedTab() {
  connect();
  void reportForeground();
}
chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === "prazo") ordered(expire);
  connect();
});
chrome.windows.onFocusChanged.addListener(changedTab);
chrome.windows.onBoundsChanged?.addListener(changedTab);
chrome.permissions?.onAdded?.addListener(() => ordered(async () => {
  await Promise.all((await chrome.tabs.query({})).map(pauseTab));
  await reportForeground(true);
}));
chrome.tabs.onActivated.addListener(changedTab);
chrome.tabs.onUpdated.addListener((id, change, tab) => {
  if (change.url && pauseFor(pageHost(change.url)).until) ordered(() => pauseTab(tab));
  if (tab.active && (change.url || change.status === "complete")) changedTab();
});
chrome.tabs.onRemoved.addListener(id => chrome.storage.session.remove("tab-" + id));
chrome.runtime.onMessage.addListener((message, sender, respond) => {
  if (sender.id !== chrome.runtime.id || (message !== "estado" && message?.type !== "pagina")) return;
  connect();
  const connected = socket?.readyState === WebSocket.OPEN && Date.now() < leaseEnd;
  if (message?.type === "pagina") {
    const host = pageHost(sender.url, true) || message.host || pageHost(message.previousUrl);
    respond({ connected, ...pauseFor(host) });
  } else {
    respond({ connected, count: blocked.length, watched: watched.length,
      filters: filters.filter(f => f.enabled && activeUntil(f.until, Date.now())).map(f => f.name) });
  }
});
ordered(async () => {
  // A service worker can restart with session rules left by its previous lease.
  await chrome.declarativeNetRequest.updateSessionRules({ removeRuleIds: [0, 1, 2].flatMap(groupIds) });
  await chrome.alarms.clear("prazo");
  notifyPages();
  await chrome.alarms.create("conectar", { periodInMinutes: 0.5 });
  connect();
});
