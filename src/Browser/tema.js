const systemTheme = matchMedia("(prefers-color-scheme: dark)");
let preference;

function applyTheme() {
  const system = systemTheme.matches ? "dark" : "light";
  document.documentElement.dataset.theme =
    preference === "dark" || preference === "light" ? preference : system;
}

applyTheme();
systemTheme.addEventListener("change", applyTheme);
chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "local" && changes.theme) {
    preference = changes.theme.newValue || "system";
    applyTheme();
  }
});
chrome.storage.local.get("theme").then(({ theme }) => {
  if (preference === undefined) {
    preference = theme || "system";
    applyTheme();
  }
}).catch(() => { /* O tema do sistema já está aplicado. */ });
