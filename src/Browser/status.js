const grant = document.getElementById("grant");

async function refresh() {
  const state = await chrome.runtime.sendMessage("estado");
  let message = "Abra o Pulso neste PC para conectar a extensão.";
  if (state.connected) {
    const active = [];
    if (state.count) active.push(state.count + (state.count === 1 ? " site em pausa." : " sites em pausa."));
    if (state.filters.length === 1) active.push("O filtro de " + state.filters[0] + " está ativo.");
    if (state.filters.length > 1) active.push("Os filtros de " + state.filters.join(" e ") + " estão ativos.");
    if (state.watched) active.push("Os limites de uso estão ativos.");
    message = "Conectado ao PC. " + (active.join(" ") || "Os sites estão livres.");
  }
  let missingAccess = false;
  try {
    if (chrome.permissions?.contains) {
      missingAccess = !(await chrome.permissions.contains({ origins: ["http://*/*", "https://*/*"] }));
    }
  } catch { /* Navegador já concede permissões declaradas no manifesto. */ }
  grant.hidden = !missingAccess;
  if (missingAccess) message += " Conceda acesso aos sites abaixo para ativar o bloqueio.";
  document.getElementById("status").textContent = message;
}
grant.addEventListener("click", async () => {
  try {
    await chrome.permissions.request({ origins: ["http://*/*", "https://*/*"] });
  } finally {
    await refresh();
  }
});
refresh();
chrome.runtime.onMessage.addListener(message => { if (message === "politica") void refresh(); });
