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

function isDistractingUrl(url) {
  return classifyDistractingUrl(url) !== null;
}

function classifyDistractingUrl(url) {
  if (!url) {
    return null;
  }

  try {
    const hostname = new URL(url).hostname.toLowerCase();
    if (!matchesDistractingHost(hostname)) {
      return null;
    }

    return hostname === "youtube.com" || hostname.endsWith(".youtube.com")
      ? "youtube"
      : "other";
  } catch {
    return null;
  }
}

globalThis.ProductivityTrackerSites = {
  classifyDistractingUrl,
  isDistractingUrl,
  matchesDistractingHost,
};

if (typeof module !== "undefined") {
  module.exports = globalThis.ProductivityTrackerSites;
}
