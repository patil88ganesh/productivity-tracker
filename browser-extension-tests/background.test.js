const assert = require("node:assert/strict");
const path = require("node:path");

let activeUrl = "https://www.youtube.com/watch?v=example";
let activeTabId = 1;
const sentMessages = [];
const badges = [];
const listeners = {
  activated: [],
  updated: [],
  focusChanged: [],
};
const retryCallbacks = [];
let rejectNextVisitToken = false;

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
        if (rejectNextVisitToken) {
          rejectNextVisitToken = false;
          listener({
            ok: true,
            active: message.active,
            appConnected: true,
          });
        } else {
          listener({ ...message, ok: true, appConnected: true });
        }
      }
    },
  };
}

global.importScripts = (script) => {
  require(path.join(__dirname, "..", "browser-extension", script));
};
global.setInterval = () => 1;
global.setTimeout = (callback) => {
  retryCallbacks.push(callback);
  return retryCallbacks.length;
};
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
      return [{ id: activeTabId, active: true, url: activeUrl }];
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
  const youtubeVisitToken = sentMessages.at(-1).visitToken;
  assert.equal(typeof youtubeVisitToken, "string");
  assert.equal(badges.includes("PAUSE"), true);

  listeners.focusChanged[0](chrome.windows.WINDOW_ID_NONE);
  assert.deepEqual(sentMessages.at(-1), {
    active: false,
    visitToken: youtubeVisitToken,
  });
  listeners.focusChanged[0](1);
  await settle();
  assert.equal(sentMessages.at(-1).active, true);
  assert.equal(sentMessages.at(-1).visitToken, youtubeVisitToken);

  activeUrl = "https://www.linkedin.com/feed/";
  listeners.updated[0](1, { status: "complete" }, { active: true });
  await settle();
  assert.equal(sentMessages.at(-1).active, true);
  assert.notEqual(sentMessages.at(-1).visitToken, youtubeVisitToken);
  const linkedInVisitToken = sentMessages.at(-1).visitToken;

  activeUrl = "https://mail.google.com/mail/u/0/";
  listeners.activated[0]();
  await settle();
  assert.equal(sentMessages.at(-1).active, true);
  assert.notEqual(sentMessages.at(-1).visitToken, linkedInVisitToken);

  activeUrl = "https://drive.google.com/drive/my-drive";
  activeTabId = 2;
  listeners.activated[0]();
  await settle();
  assert.equal(sentMessages.at(-1).active, true);

  activeUrl = "https://example.com/";
  listeners.activated[0]();
  await settle();
  assert.equal(sentMessages.at(-1).active, false);
  assert.equal(sentMessages.at(-1).visitToken, undefined);

  rejectNextVisitToken = true;
  activeUrl = "https://www.youtube.com/watch?v=another";
  activeTabId = 3;
  listeners.activated[0]();
  await settle();
  assert.equal(badges.at(-1), "!");
  assert.equal(retryCallbacks.length > 0, true);

  console.log("Focus Protection background tests passed.");
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
