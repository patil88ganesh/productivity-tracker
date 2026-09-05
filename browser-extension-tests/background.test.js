const assert = require("node:assert/strict");
const path = require("node:path");

let activeUrl = "https://www.youtube.com/watch?v=example";
const sentMessages = [];
const badges = [];
const listeners = {
  activated: [],
  updated: [],
  focusChanged: [],
};

function event(target) {
  return {
    addListener(listener) {
      target.push(listener);
    },
  };
}

function createNativePort() {
  const messageListeners = [];
  const disconnectListeners = [];
  return {
    onMessage: event(messageListeners),
    onDisconnect: event(disconnectListeners),
    postMessage(message) {
      sentMessages.push(message);
      for (const listener of messageListeners) {
        listener({ ...message, ok: true, appConnected: true });
      }
    },
  };
}

global.importScripts = (script) => {
  require(path.join(__dirname, "..", "browser-extension", script));
};
global.setInterval = () => 1;
global.chrome = {
  action: {
    setBadgeText(value) {
      badges.push(value.text);
    },
    setBadgeBackgroundColor() {},
    setTitle() {},
  },
  alarms: {
    create() {},
    onAlarm: event([]),
  },
  runtime: {
    connectNative: createNativePort,
    onInstalled: event([]),
    onStartup: event([]),
  },
  tabs: {
    async query() {
      return [{ active: true, url: activeUrl }];
    },
    onActivated: event(listeners.activated),
    onUpdated: event(listeners.updated),
  },
  windows: {
    WINDOW_ID_NONE: -1,
    async getLastFocused() {
      return { focused: true, id: 1 };
    },
    onFocusChanged: event(listeners.focusChanged),
  },
};

require("../browser-extension/background.js");

async function settle() {
  await new Promise((resolve) => setImmediate(resolve));
}

(async () => {
  await settle();
  assert.equal(sentMessages.at(-1).active, true);
  assert.equal(badges.includes("PAUSE"), true);

  activeUrl = "https://www.linkedin.com/feed/";
  listeners.updated[0](1, { status: "complete" }, { active: true });
  await settle();
  assert.equal(sentMessages.at(-1).active, true);

  activeUrl = "https://mail.google.com/mail/u/0/";
  listeners.activated[0]();
  await settle();
  assert.equal(sentMessages.at(-1).active, true);

  activeUrl = "https://drive.google.com/drive/my-drive";
  listeners.activated[0]();
  await settle();
  assert.equal(sentMessages.at(-1).active, true);

  activeUrl = "https://example.com/";
  listeners.activated[0]();
  await settle();
  assert.equal(sentMessages.at(-1).active, false);

  console.log("Focus Protection background tests passed.");
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
