const NATIVE_HOST = "com.patil88ganesh.productivity_tracker";
const DISTRACTING_HOSTS = new Set([
  "facebook.com",
  "instagram.com",
  "x.com",
  "twitter.com",
  "reddit.com",
  "linkedin.com",
  "youtube.com",
  "tiktok.com",
  "web.whatsapp.com",
]);

let nativePort;
let lastActiveState;

function matchesDistractingHost(hostname) {
  const normalized = hostname.toLowerCase();
  return [...DISTRACTING_HOSTS].some(
    (host) => normalized === host || normalized.endsWith(`.${host}`),
  );
}

function isDistractingUrl(url) {
  if (!url) {
    return false;
  }

  try {
    return matchesDistractingHost(new URL(url).hostname);
  } catch {
    return false;
  }
}

function connectNativeHost() {
  if (nativePort) {
    return nativePort;
  }

  nativePort = chrome.runtime.connectNative(NATIVE_HOST);
  nativePort.onDisconnect.addListener(() => {
    nativePort = undefined;
  });
  return nativePort;
}

function reportState(active, force = false) {
  if (!force && lastActiveState === active) {
    return;
  }

  lastActiveState = active;
  try {
    connectNativeHost().postMessage({ active });
  } catch {
    nativePort = undefined;
  }

  chrome.action.setBadgeText({ text: active ? "PAUSE" : "" });
  chrome.action.setBadgeBackgroundColor({ color: "#E53935" });
}

async function evaluateActiveTab(force = false) {
  const focusedWindow = await chrome.windows.getLastFocused();
  if (!focusedWindow.focused) {
    reportState(false, force);
    return;
  }

  const [tab] = await chrome.tabs.query({
    active: true,
    windowId: focusedWindow.id,
  });
  reportState(Boolean(tab && isDistractingUrl(tab.url)), force);
}

chrome.tabs.onActivated.addListener(evaluateActiveTab);
chrome.tabs.onUpdated.addListener((_tabId, changeInfo, tab) => {
  if (tab.active && (changeInfo.url || changeInfo.status === "complete")) {
    evaluateActiveTab();
  }
});
chrome.windows.onFocusChanged.addListener((windowId) => {
  if (windowId === chrome.windows.WINDOW_ID_NONE) {
    reportState(false);
    return;
  }

  evaluateActiveTab();
});
chrome.runtime.onStartup.addListener(evaluateActiveTab);
chrome.runtime.onInstalled.addListener(evaluateActiveTab);

evaluateActiveTab();
setInterval(() => evaluateActiveTab(true), 5000);
