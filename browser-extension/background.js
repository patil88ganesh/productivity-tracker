importScripts("site-matcher.js");

const NATIVE_HOST = "com.patil88ganesh.productivity_tracker";
const HEARTBEAT_ALARM = "focus-protection-heartbeat";
const RETRY_DELAY_MS = 5000;
const classifyUrl =
  globalThis.ProductivityTrackerSites.classifyDistractingUrl;

let nativePort;
let currentSite = null;
let lastDeliveredState;
let retryTimer;

function updateBadge(active, connected = true) {
  if (!connected) {
    chrome.action.setBadgeText({ text: "!" });
    chrome.action.setBadgeBackgroundColor({ color: "#FF8F00" });
    chrome.action.setTitle({
      title: "Focus Protection: Productivity Tracker is not connected",
    });
    return;
  }

  chrome.action.setBadgeText({ text: active ? "PAUSE" : "" });
  chrome.action.setBadgeBackgroundColor({ color: "#E53935" });
  chrome.action.setTitle({
    title: active
      ? "Focus Protection: tracking paused"
      : "Productivity Tracker Focus Protection",
  });
}

function scheduleRetry() {
  if (retryTimer) {
    return;
  }

  retryTimer = setTimeout(() => {
    retryTimer = undefined;
    evaluateActiveTab(true);
  }, RETRY_DELAY_MS);
}

function connectNativeHost() {
  if (nativePort) {
    return nativePort;
  }

  const port = chrome.runtime.connectNative(NATIVE_HOST);
  nativePort = port;
  port.onMessage.addListener((message) => {
    if (!message || typeof message.appConnected !== "boolean") {
      return;
    }

    if (!message.appConnected) {
      lastDeliveredState = undefined;
      updateBadge(currentSite !== null, false);
      scheduleRetry();
      return;
    }

    if (typeof message.site !== "string") {
      const acknowledgedActive = Boolean(message.active);
      const currentActive = currentSite !== null;
      if (acknowledgedActive !== currentActive) {
        lastDeliveredState = acknowledgedActive ? "other" : null;
        reportState(currentSite, true);
        return;
      }

      lastDeliveredState = currentSite;
      updateBadge(currentSite !== null);
      return;
    }

    lastDeliveredState = normalizeSite(message);
    updateBadge(currentSite !== null);
    if (lastDeliveredState !== currentSite) {
      reportState(currentSite, true);
    }
  });
  port.onDisconnect.addListener(() => {
    if (nativePort === port) {
      nativePort = undefined;
    }
    lastDeliveredState = undefined;
    updateBadge(currentSite !== null, false);
    scheduleRetry();
  });
  return port;
}

function normalizeSite(message) {
  if (!message?.active) {
    return null;
  }

  return message.site === "youtube" ? "youtube" : "other";
}

function reportState(site, force = false) {
  currentSite = site;
  if (!force && lastDeliveredState === site) {
    updateBadge(site !== null);
    return;
  }

  try {
    connectNativeHost().postMessage({
      active: site !== null,
      site: site || "none",
    });
    updateBadge(site !== null);
  } catch {
    nativePort = undefined;
    lastDeliveredState = undefined;
    updateBadge(site !== null, false);
    scheduleRetry();
  }
}

async function evaluateActiveTab(force = false) {
  try {
    const focusedWindow = await chrome.windows.getLastFocused();
    if (!focusedWindow?.focused || typeof focusedWindow.id !== "number") {
      reportState(null, force);
      return;
    }

    const [tab] = await chrome.tabs.query({
      active: true,
      windowId: focusedWindow.id,
    });
    const url = tab?.url || tab?.pendingUrl;
    reportState(tab ? classifyUrl(url) : null, force);
  } catch {
    reportState(null, force);
  }
}

chrome.tabs.onActivated.addListener(() => evaluateActiveTab());
chrome.tabs.onUpdated.addListener((_tabId, changeInfo, tab) => {
  if (tab.active && (changeInfo.url || changeInfo.status === "complete")) {
    evaluateActiveTab();
  }
});
chrome.windows.onFocusChanged.addListener((windowId) => {
  if (windowId === chrome.windows.WINDOW_ID_NONE) {
    reportState(null);
    return;
  }

  evaluateActiveTab();
});
chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === HEARTBEAT_ALARM) {
    evaluateActiveTab(true);
  }
});
chrome.runtime.onStartup.addListener(() => evaluateActiveTab(true));
chrome.runtime.onInstalled.addListener(() => {
  chrome.alarms.create(HEARTBEAT_ALARM, { periodInMinutes: 0.5 });
  evaluateActiveTab(true);
});

chrome.alarms.create(HEARTBEAT_ALARM, { periodInMinutes: 0.5 });
evaluateActiveTab(true);
setInterval(() => evaluateActiveTab(true), RETRY_DELAY_MS);
