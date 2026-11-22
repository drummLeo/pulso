async function refresh() {
  const state = await chrome.runtime.sendMessage("estado");
  document.getElementById("status").textContent = !state.connected
    ? "Abra o Pulso neste PC para conectar a extensão."
    : state.count
      ? state.count + (state.count === 1 ? " site em pausa." : " sites em pausa.")
      : state.watched ? "Conectado ao PC. Os limites de uso estão ativos." : "Conectado ao PC. Os sites estão livres.";
}
refresh();
setInterval(refresh, 2000);
