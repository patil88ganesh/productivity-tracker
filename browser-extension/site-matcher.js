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
  "mail.google.com",
  "drive.google.com",
  "docs.google.com",
  "sheets.google.com",
  "slides.google.com",
]);

function matchesDistractingHost(hostname) {
  const normalized = hostname.toLowerCase();
  return [...DISTRACTING_HOSTS].some(
    (host) => normalized === host || normalized.endsWith(`.${host}`),
  );
}

function getDistractingSiteKey(url) {
  if (!url) {
    return undefined;
  }

  try {
    const hostname = new URL(url).hostname.toLowerCase();
    return [...DISTRACTING_HOSTS].find(
      (host) => hostname === host || hostname.endsWith(`.${host}`),
    );
  } catch {
    return undefined;
  }
}

function isDistractingUrl(url) {
  return Boolean(getDistractingSiteKey(url));
}

globalThis.ProductivityTrackerSites = {
  getDistractingSiteKey,
  isDistractingUrl,
  matchesDistractingHost,
};

if (typeof module !== "undefined") {
  module.exports = globalThis.ProductivityTrackerSites;
}
