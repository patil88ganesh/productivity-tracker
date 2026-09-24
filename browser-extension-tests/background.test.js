const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

let activeUrl = "https://www.youtube.com/watch?v=example";
let windowFocused = true;
let legacyAcknowledgements = false;
let autoAcknowledge = true;
const sentMessages = [];
const badges = [];
const nativeMessageListeners = [];
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
  const disconnectListeners = [];
  return {
    onMessage: event(nativeMessageListeners),
    onDisconnect: event(disconnectListeners),
    postMessage(message) {
      sentMessages.push(message);
      if (!autoAcknowledge) {
        return;
      }

      for (const listener of nativeMessageListeners) {
        listener(
          legacyAcknowledgements
            ? { active: message.active, ok: true, appConnected: true }
            : { ...message, ok: true, appConnected: true },
        );
      }
    },
  };
}

global.importScripts = (script) => {
  const scriptPath = path.join(
    __dirname,
    "..",
    "browser-extension",
    script,
  );
  vm.runInThisContext(fs.readFileSync(scriptPath, "utf8"), {
    filename: scriptPath,
  });
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
      return { focused: windowFocused, id: 1 };
    },
    onFocusChanged: event(listeners.focusChanged),
  },
};

const backgroundPath = path.join(
  __dirname,
  "..",
  "browser-extension",
  "background.js",
);
vm.runInThisContext(fs.readFileSync(backgroundPath, "utf8"), {
  filename: backgroundPath,
});

async function settle() {
  await new Promise((resolve) => setImmediate(resolve));
}

(async () => {
  await settle();
  assert.equal(sentMessages.at(-1).active, true);
  assert.equal(sentMessages.at(-1).site, "youtube");
  assert.equal(badges.includes("PAUSE"), true);

  activeUrl = "https://www.linkedin.com/feed/";
  listeners.updated[0](1, { status: "complete" }, { active: true });
  await settle();
  assert.equal(sentMessages.at(-1).active, true);
  assert.equal(sentMessages.at(-1).site, "other");

  const messagesBeforeLegacyAck = sentMessages.length;
  legacyAcknowledgements = true;
  activeUrl = "https://www.youtube.com/watch?v=legacy-host";
  listeners.activated[0]();
  await settle();
  assert.equal(sentMessages.length, messagesBeforeLegacyAck + 1);
  assert.equal(sentMessages.at(-1).site, "youtube");
  legacyAcknowledgements = false;

  autoAcknowledge = false;
  const messagesBeforeDelayedAck = sentMessages.length;
  activeUrl = "https://www.linkedin.com/feed/";
  listeners.activated[0]();
  await settle();
  activeUrl = "https://example.com/";
  listeners.activated[0]();
  await settle();
  for (const listener of nativeMessageListeners) {
    listener({ active: true, ok: true, appConnected: true });
  }
  await settle();
  assert.equal(sentMessages.length, messagesBeforeDelayedAck + 3);
  assert.equal(sentMessages.at(-1).active, false);
  for (const listener of nativeMessageListeners) {
    listener({ active: false, ok: true, appConnected: true });
  }
  await settle();
  autoAcknowledge = true;

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
  assert.equal(sentMessages.at(-1).site, "none");

  windowFocused = false;
  listeners.focusChanged[0](1);
  await settle();
  assert.equal(sentMessages.at(-1).active, false);
  assert.equal(sentMessages.at(-1).site, "none");

  console.log("Focus Protection background tests passed.");
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
