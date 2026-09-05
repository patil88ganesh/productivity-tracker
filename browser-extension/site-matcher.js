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
  if (!url) {
    return false;
  }

  try {
    return matchesDistractingHost(new URL(url).hostname);
  } catch {
    return false;
  }
}

globalThis.ProductivityTrackerSites = {
  isDistractingUrl,
  matchesDistractingHost,
};

if (typeof module !== "undefined") {
  module.exports = globalThis.ProductivityTrackerSites;
}
