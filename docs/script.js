const timerButton = document.querySelector("#demoTimer");
const timerDisplay = document.querySelector("#demoTime");
const year = document.querySelector("#year");

let running = true;
let elapsedSeconds = 18 * 60 + 42;
let lastTick = performance.now();

function formatTime(totalSeconds) {
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = Math.floor(totalSeconds % 60);
  return [hours, minutes, seconds]
    .map((part) => String(part).padStart(2, "0"))
    .join(":");
}

function toggleTimer() {
  running = !running;
  timerButton.classList.toggle("running", running);
  timerButton.setAttribute(
    "aria-label",
    running ? "Stop demo timer" : "Start demo timer",
  );
  lastTick = performance.now();
}

function updateTimer(now) {
  if (running) {
    const delta = Math.floor((now - lastTick) / 1000);
    if (delta > 0) {
      elapsedSeconds += delta;
      lastTick += delta * 1000;
      timerDisplay.textContent = formatTime(elapsedSeconds);
    }
  }
  requestAnimationFrame(updateTimer);
}

timerButton.addEventListener("click", toggleTimer);
timerButton.addEventListener("mousedown", (event) => {
  if (event.button === 1) {
    event.preventDefault();
  }
});
timerButton.addEventListener("auxclick", (event) => {
  if (event.button === 1) {
    event.preventDefault();
    toggleTimer();
  }
});

const revealObserver = new IntersectionObserver(
  (entries) => {
    entries.forEach((entry) => {
      if (entry.isIntersecting) {
        entry.target.classList.add("visible");
        revealObserver.unobserve(entry.target);
      }
    });
  },
  { threshold: 0.14 },
);

document.querySelectorAll(".reveal").forEach((element) => {
  revealObserver.observe(element);
});

year.textContent = new Date().getFullYear();
requestAnimationFrame(updateTimer);
