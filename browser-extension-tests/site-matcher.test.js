const assert = require("node:assert/strict");
const {
  isDistractingUrl,
  matchesDistractingHost,
} = require("../browser-extension/site-matcher.js");

const protectedUrls = [
  "https://www.youtube.com/watch?v=example",
  "https://m.youtube.com/",
  "https://www.linkedin.com/feed/",
  "https://mail.google.com/mail/u/0/",
  "https://drive.google.com/drive/my-drive",
  "https://docs.google.com/document/d/example/edit",
  "https://sheets.google.com/spreadsheets/d/example/edit",
  "https://slides.google.com/presentation/d/example/edit",
  "https://web.whatsapp.com/",
];

for (const url of protectedUrls) {
  assert.equal(isDistractingUrl(url), true, `${url} should be protected`);
}

assert.equal(matchesDistractingHost("youtube.com"), true);
assert.equal(matchesDistractingHost("WWW.LINKEDIN.COM"), true);
assert.equal(isDistractingUrl("https://google.com/"), false);
assert.equal(isDistractingUrl("https://youtube.com.example.org/"), false);
assert.equal(isDistractingUrl("not a URL"), false);

console.log("Focus Protection site matching tests passed.");
