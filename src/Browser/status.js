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
  document.getElementById("status").textContent = message;
}
refresh();
chrome.runtime.onMessage.addListener(message => { if (message === "politica") void refresh(); });
