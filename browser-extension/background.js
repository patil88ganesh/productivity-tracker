importScripts("site-matcher.js");

const NATIVE_HOST = "com.patil88ganesh.productivity_tracker";
const HEARTBEAT_ALARM = "focus-protection-heartbeat";
const RETRY_DELAY_MS = 5000;
const { getDistractingSiteKey } = globalThis.ProductivityTrackerSites;

let nativePort;
let currentState = false;
let currentVisitToken;
let currentVisitIdentity;
let lastDeliveredState;
let lastDeliveredVisitToken;
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
      lastDeliveredVisitToken = undefined;
      updateBadge(currentState, false);
      scheduleRetry();
      return;
    }

    const acknowledgedState =
      typeof message.active === "boolean" ? message.active : undefined;
    const acknowledgedVisitToken =
      typeof message.visitToken === "string" ? message.visitToken : undefined;
    if (
      acknowledgedState !== currentState ||
      acknowledgedVisitToken !== currentVisitToken
    ) {
      lastDeliveredState = undefined;
      lastDeliveredVisitToken = undefined;
      updateBadge(currentState, false);
      scheduleRetry();
      return;
    }

    lastDeliveredState = acknowledgedState;
    lastDeliveredVisitToken = acknowledgedVisitToken;
    updateBadge(currentState);
  });
  port.onDisconnect.addListener(() => {
    if (nativePort === port) {
      nativePort = undefined;
    }
    lastDeliveredState = undefined;
    lastDeliveredVisitToken = undefined;
    updateBadge(currentState, false);
    scheduleRetry();
  });
  return port;
}

function reportState(active, visitToken, force = false) {
  currentState = active;
  currentVisitToken = visitToken;
  if (
    !force &&
    lastDeliveredState === active &&
    lastDeliveredVisitToken === visitToken
  ) {
    updateBadge(active);
    return;
  }

  try {
    updateBadge(active);
    connectNativeHost().postMessage({ active, visitToken });
  } catch {
    nativePort = undefined;
    lastDeliveredState = undefined;
    lastDeliveredVisitToken = undefined;
    updateBadge(active, false);
    scheduleRetry();
  }
}

async function evaluateActiveTab(force = false) {
  try {
    const focusedWindow = await chrome.windows.getLastFocused();
    if (!focusedWindow?.focused || typeof focusedWindow.id !== "number") {
      reportState(false, currentVisitToken, force);
      return;
    }

    const [tab] = await chrome.tabs.query({
      active: true,
      windowId: focusedWindow.id,
    });
    const url = tab?.url || tab?.pendingUrl;
    const siteKey = getDistractingSiteKey(url);
    if (!tab || !siteKey) {
      currentVisitIdentity = undefined;
      reportState(false, undefined, force);
      return;
    }

    const visitIdentity = `${tab.id}:${siteKey}`;
    if (visitIdentity !== currentVisitIdentity) {
      currentVisitIdentity = visitIdentity;
      currentVisitToken = crypto.randomUUID();
    }
    reportState(true, currentVisitToken, force);
  } catch {
    currentVisitIdentity = undefined;
    reportState(false, undefined, force);
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
    reportState(false, currentVisitToken);
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
