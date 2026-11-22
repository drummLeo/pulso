const title = document.getElementById("title");
const detail = document.getElementById("detail");
const back = document.getElementById("return");
let active = true;
let previousUrl = "";
async function loadPrevious() {
  const tab = await chrome.tabs.getCurrent();
  if (tab?.id == null) return;
  const key = "tab-" + tab.id;
  const stored = (await chrome.storage.session.get(key))[key];
  if (!stored) return;
  try {
    const url = new URL(stored);
    if (["http:", "https:"].includes(url.protocol) && (!location.hash ||
        url.hostname.toLowerCase().replace(/\.$/, "") === location.hash.slice(1))) previousUrl = stored;
  } catch { /* The browser may have discarded a previous tab. */ }
}
async function refresh() {
  const state = await chrome.runtime.sendMessage({ type: "pagina", previousUrl, host: location.hash.slice(1) });
  active = state.connected && state.until > Date.now();
  title.textContent = active ? (state.limit ? "Limite de uso atingido." : "Este site está em pausa.") : "A pausa terminou.";
  if (!active) { detail.textContent = "Você já pode acessar o site novamente."; return; }
  const end = new Date(state.until);
  const day = end.toDateString() === new Date().toDateString() ? "" : " em " + end.toLocaleDateString("pt-BR");
  detail.textContent = "Você poderá voltar" + day + " às " + end.toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" }) + ".";
}
back.addEventListener("submit", async event => {
  event.preventDefault();
  const tab = await chrome.tabs.getCurrent();
  if (!active && previousUrl) { await chrome.tabs.update(tab.id, { url: previousUrl }); return; }
  if (history.length > 1) history.back();
  else { await chrome.tabs.create({}); await chrome.tabs.remove(tab.id); }
});
loadPrevious().then(refresh);
setInterval(refresh, 2000);
