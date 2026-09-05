# Code Review — Task 280, Attempt 1, Round 6 of 6

## Review Summary
- **Round**: 6
- **Theme**: Polish & hardening
- **Mode**: sequential
- **Model**: claude-opus-5 (Slot 3, latest Anthropic Opus, highest reasoning setting)
- **Artifact**: C:\Users\gapat.FAREAST\MiniStopwatch\reviews\task-280-attempt-1-review-6-claude-opus-5.md
- **Issues Found**: 3
- **Verdict**: ISSUES_FOUND

## Evidence Checklist
- [x] Read the complete generated prompt at `C:\Users\gapat.FAREAST\MiniStopwatch\.review-prompts-280\review-round-6.md` (instructions, output contract, and embedded diff).
- [x] Confirmed the reviewed change set against the live working tree: `git --no-pager status` and `git --no-pager diff --stat` show 21 modified files, 1174 insertions / 99 deletions, matching the prompt diff.
- [x] Read the full per-area diffs: `git --no-pager diff -- MiniStopwatch.Core/StopwatchController.cs MiniStopwatch.Core/TrackingController.cs`, `... -- MiniStopwatch.App/SocialMediaPauseBridge.cs ProductivityTracker.NativeHost/Program.cs browser-extension/ browser-extension-tests/`, `... -- macos/`, `... -- MiniStopwatch.Tests/Program.cs`, `... -- README.md docs/index.html MiniStopwatch.App/app.manifest VERSION`.
- [x] Read the post-change source in full for the token protocol: `MiniStopwatch.Core/StopwatchController.cs`, `MiniStopwatch.Core/TrackingController.cs`, `MiniStopwatch.App/SocialMediaPauseBridge.cs`, `MiniStopwatch.App/MainWindow.xaml.cs`, `ProductivityTracker.NativeHost/Program.cs`, `browser-extension/background.js`, `browser-extension/site-matcher.js`, `macos/Sources/TimerEngine.swift`, `macos/Sources/FocusProtection.swift`, `macos/Sources/TimerWindow.swift`, `macos/NativeHost/NativeMessagingHost.swift`.
- [x] Built every reachable .NET project: `dotnet build MiniStopwatch.Core` (0 warnings, 0 errors), `dotnet build MiniStopwatch.App` (0 warnings, 0 errors), `dotnet build ProductivityTracker.NativeHost` (net48, 0 warnings, 0 errors).
- [x] Ran all three test suites: `dotnet run --project MiniStopwatch.Tests` → "All 44 tests passed"; `node browser-extension-tests/background.test.js` → passed (exit 0); `node browser-extension-tests/site-matcher.test.js` → passed (exit 0).
- [x] Reproduced Issue 1 empirically by loading the built `MiniStopwatch.Core.dll` in PowerShell 7.5 with a fake `IMonotonicClock` and replaying the exact production message sequence `(true,"T1") → (false,"T1") → (false,null)`; observed `CanContinueCountingOnDistractingWebsite = True` on an unprotected page and a persistent `HasContinueCountingOverride = True` two simulated hours later.
- [x] Traced the end-to-end token protocol for correctness across all four hops (extension `crypto.randomUUID()` → native-host `NormalizeVisitToken` / `normalizeVisitToken` → pipe/socket `"{0|1}\t{token}\n"` framing → bridge aggregation → timer state machine) including the backward-compatible `separator < 0` path in `SocialMediaPauseBridge.HandleConnectionAsync`.
- [x] Verified release-metadata consistency for 2.8.0: `grep -n "2\.7\.1"` returns matches only inside `.review-prompts-280/` and prior review artifacts; `Directory.Build.props`, `VERSION`, `MiniStopwatch.App/app.manifest`, `browser-extension/manifest.json`, `README.md`, and `docs/index.html` are all 2.8.0, and `macos/build-package.sh` derives `CFBundleShortVersionString`/`CFBundleVersion` from `VERSION`.
- [x] Verified documentation accuracy against behaviour: the README/macos-README claims "returning to the same visit within 30 seconds keeps counting", "visiting an unprotected page ... ends the override", "manual timer pauses, resumes, and timer-mode changes do not end the current visit override", and "Session-lock pausing always remains enforced" all match the implemented state machine and the new tests (`ContinueCountingSurvivesManualPauseAndResume`, `ContinueCountingSurvivesNewTimerInSameVisit`, `ContinueCountingPreservesSessionLockPause`, `ContinueCountingHandoffIncludesExactDeadline`).
- [x] Verified the privacy claim: the visit token is a `crypto.randomUUID()` value, is never derived from the URL/host, and `visitIdentity` (`${tab.id}:${siteKey}`) never leaves the extension; the token is held only in memory (`browserVisitToken`, `continueCountingVisitToken`) and is never persisted to the registry, the stats store, or any log (`Console.Error.WriteLine(exception.Message)` in the native host does not include it).
- [x] Checked the AppKit menu-validation change: `menu.autoenablesItems = false` in `configureMenu()` applies only to the root menu; every root item is either explicitly managed (`continueCountingMenuItem`, `exitTimerMenuItem`, `statsMenuItem`, `toggleMenuItem`) or defaults to `isEnabled == true`, and the `focusMenu`/`opacityMenu` submenus keep their own default auto-validation — no regression.
- [ ] Did not compile the macOS Swift targets — no Swift toolchain is available on this Windows host. Swift findings below are derived from line-by-line comparison with the verified C# implementation, which the Swift code mirrors exactly.

## Issues

### Issue 1: A pending "Continue counting" offer is never invalidated when the browser reports that no protected site is present
- **Severity**: Medium
- **File**: `MiniStopwatch.Core/StopwatchController.cs` (mirrored in `macos/Sources/TimerEngine.swift`)
- **Line(s)**: `StopwatchController.cs` 45–54 (`CanContinueCountingOnDistractingWebsite`), 153–172 (`OnDistractingWebsiteChanged`), 238–247 (offer creation inside `SetAutomaticPause`); `TimerEngine.swift` 42–49, 135–143, 176–185
- **Description**: The 30-second handoff offer (`continueCountingAvailableUntil` + `continueCountingOfferVisitToken`) is created when a protected visit deactivates while the tracker is auto-paused, but nothing invalidates it when the *next* browser report says there is no protected site at all. The token-mismatch guard at `OnDistractingWebsiteChanged` only fires when `HasContinueCountingOverride` is already true, and the offer-clearing code inside `SetAutomaticPause` is gated on `automaticPauseReasons.Contains(DistractingWebsite)`, which is false once the pause has already been lifted. The production message that exposes this is the exact one `browser-extension/background.js:129-136` emits when the active tab is not a protected site: `reportState(false, undefined, force)` → `{active:false}` → `visitToken == null`.

  Reproduced against the built `MiniStopwatch.Core.dll`:

  ```
  Toggle();                                  # tracker running
  OnDistractingWebsiteChanged(true,  "T1");  # IsRunning=False AutoPaused=True  Can=True  Has=False
  OnDistractingWebsiteChanged(false, "T1");  # tracker window focused -> offer(T1) created, timer resumes
                                             # IsRunning=True  AutoPaused=False Can=True  Has=False
  OnDistractingWebsiteChanged(false, null);  # browser refocused on an UNPROTECTED page
                                             # IsRunning=True  AutoPaused=False Can=True  Has=False   <-- stale offer
  ContinueCountingOnDistractingWebsite();    # user clicks the still-enabled item
                                             # Has=True IsCont=False, menu header = "Stop counting this site"
  clock.Advance(2 hours);                    # Has=True (persists; no further browser report is sent because
                                             #  both the extension and SocialMediaPauseBridge suppress
                                             #  unchanged state, so nothing clears it)
  ```

  Two user-visible consequences follow:
  1. For up to 30 seconds after leaving a protected visit, **"Continue counting" stays enabled while the user is on an ordinary, unprotected page** and the tracker is already counting normally — a control that does nothing meaningful.
  2. If it is clicked, an override is granted bound to a token (`T1`) the extension can no longer reproduce (the `!siteKey` path resets `currentVisitIdentity`, so the next protected visit gets a fresh UUID). `HasContinueCountingOverride` stays true, so `RefreshDisplay()` / `refreshDisplay()` permanently relabels the item **"Stop counting this site"** even though nothing is being counted as an override. Because `SocialMediaPauseBridge.SetConnectionState` only reports on aggregate change and the extension only posts on state change, no further `OnDistractingWebsiteChanged` call arrives to clear it — the wrong label survives indefinitely, until the next protected visit or an explicit cancel.
- **Risk**: A privacy/enforcement-relevant control misreports its own state: the menu claims an override is active on a site the user never approved, and offers to "stop counting" a site they are not on. It also lets the user arm an override that silently does nothing. Time accounting itself stays correct (the next protected visit carries a different token and re-pauses), so this is a state/UI-integrity defect rather than a data-loss defect. The macOS `TimerEngine` reproduces the same logic line for line, so both platforms are affected.
- **Suggested Fix**: Invalidate the pending offer when a report arrives that cannot belong to it. In `StopwatchController.OnDistractingWebsiteChanged`, before calling `SetAutomaticPause`, add:

  ```csharp
  if (!HasContinueCountingOverride &&
      continueCountingOfferVisitToken != null &&
      !string.Equals(continueCountingOfferVisitToken, visitToken, StringComparison.Ordinal))
  {
      continueCountingAvailableUntil = null;
      continueCountingOfferVisitToken = null;
  }
  ```

  The `!HasContinueCountingOverride` guard is required so the legitimate `AwaitingFocusLoss → AwaitingRefocus` handoff window (which is owned by `continueCountingVisitToken`, not the offer token) is not cleared. Mirror the same prologue in `TimerEngine.setAutomaticPause` where `distractingWebsiteVisitToken` is assigned, and add a regression test asserting `CanContinueCountingOnDistractingWebsite == false` after `OnDistractingWebsiteChanged(isActive: false, visitToken: null)` following an offer.

### Issue 2: `TrackingController.OnDistractingWebsiteChanged(bool)` fabricates a constant visit token, and the new tests depend on it
- **Severity**: Medium
- **File**: `MiniStopwatch.Core/TrackingController.cs` (test impact in `MiniStopwatch.Tests/Program.cs`)
- **Line(s)**: `TrackingController.cs` 130–135; `Program.cs` 305–673 (approximately 60 call sites)
- **Description**: The single-argument overload is public API on the shipped `MiniStopwatch.Core` library and synthesizes the literal token `"legacy-browser-visit"` for every call. Verified by `grep`: both production call sites (`MainWindow.xaml.cs:278` and `:669`) use the two-argument overload, so nothing in the product ever calls it — it exists solely because the test suite uses it. Two concrete problems follow:
  1. Because the fabricated token never changes, every caller of this overload gets an override that is *not* bound to a visit: an override approved on site A silently survives into site B, which is precisely the invariant the visit-token protocol was introduced to enforce. Any future caller (a new host, a CLI, a macOS/Windows bridge refactor) inherits that behaviour with no compiler or reviewer signal.
  2. Of the 19 new "Continue counting" tests, 16 drive the state machine through this constant-token overload, so they exercise the timing/handoff state machine but not the token binding. Only `ContinueCountingStatusClearsWhileBrowserIsUnfocused`, `ContinueCountingDoesNotTransferToAnotherProtectedVisit`, and `ContinueCountingCanBeCancelledWhileActive` pass explicit tokens. Notably, **no test covers `OnDistractingWebsiteChanged(isActive: false, visitToken: null)`** — the message the extension actually sends when the user navigates to an unprotected page, which the README documents as the primary "ends the override" path, and which is the trigger for Issue 1 above. The constant token also makes `(false, "legacy-browser-visit")` non-null on every deactivation, so the tests model the browser-unfocus message shape but never the navigate-away message shape.
- **Risk**: Public API that defeats the new per-visit safety invariant with no deprecation signal, plus a test suite that is green while the exact production message contract that governs override termination is untested. This is how Issue 1 reached round 6 undetected.
- **Suggested Fix**: Delete the single-argument overload (or mark it `[Obsolete]`/`internal`) and update the test call sites to pass explicit tokens, e.g. a `const string WorkVisit = "visit-a"` and `OtherVisit = "visit-b"`. Add coverage for the two real message shapes an inactive report can take — `(false, <same token>)` for browser-unfocus and `(false, null)` for navigate-away — asserting the override survives the former and is cleared by the latter.

### Issue 3: The extension marks a visit token as delivered even when the native host dropped it
- **Severity**: Low
- **File**: `browser-extension/background.js`
- **Line(s)**: 66–78
- **Description**: The delivery-confirmation handler falls back to the extension's own value when the host response carries no token:

  ```js
  lastDeliveredVisitToken =
    typeof message.visitToken === "string" ? message.visitToken : currentVisitToken;
  ```

  Both hosts respond with a *normalized* token: `ProductivityTracker.NativeHost/Program.cs:135-153` returns `null` and `macos/NativeHost/NativeMessagingHost.swift:107-115` omits the key when the token fails validation (empty, longer than 64 bytes, or containing anything other than `[A-Za-z0-9-]`). In that case the extension records the token it *sent* as the token that was *delivered*, so the subsequent `lastDeliveredVisitToken !== currentVisitToken` resync check at line 74-75 never fires and `reportState`'s short-circuit at line 96-99 suppresses all further sends for that visit. The app is left with `visitToken == null` for the visit — "Continue counting" is silently unavailable — with no retry and no `"!"` badge. This is latent today, not live: `crypto.randomUUID()` always produces a 36-character `[0-9a-f-]` value that passes both validators, and the `null`/absent response for a genuinely token-less report is correctly matched by `currentVisitToken === undefined`. It becomes reachable the moment the token format, the length cap, or the character allowlist diverges between the extension and either host.
- **Risk**: A future token-format change on either side degrades to a silent, unrecoverable loss of the Continue-counting feature rather than a visible failure, and the existing `appConnected`/badge observability path does not cover it.
- **Suggested Fix**: Distinguish "host sent no token because none was requested" from "host rejected the token". When `currentVisitToken !== undefined` and the response omits or nulls `visitToken`, treat the visit as token-less on the extension side too — clear `currentVisitToken` and `currentVisitIdentity` and surface it through `updateBadge(currentState, false)` once — instead of recording it as delivered. Do not simply set `lastDeliveredVisitToken = undefined`, which would create a 1:1 post/reject resend loop against the 5-second heartbeat.

## Resolution Log
_Updated by the driving agent as findings are addressed._

### Issue 1
- **Status**: Resolved
- **What changed**: `StopwatchController.OnDistractingWebsiteChanged` and
  `TimerEngine.setAutomaticPause` now clear a pending offer whenever the next
  browser report carries a different token or no token. Added
  `UnprotectedPageClearsContinueCountingOffer`.
- **Why**: An offer belongs only to the protected visit that produced it; an
  ordinary page or another visit must not leave the command enabled.
- **How verified**: The full Windows build passed with all 45 tests, including
  the exact `(true, T1) -> (false, T1) -> (false, null)` regression sequence.

### Issue 2
- **Status**: Resolved
- **What changed**: Removed the public single-argument production overload.
  Existing timing tests now use a file-local test extension that supplies an
  explicit test visit token, while protocol-contract tests call the two-argument
  API directly with same, different, and null tokens.
- **Why**: Production callers can no longer accidentally bypass visit binding;
  the compatibility convenience is confined to the test assembly.
- **How verified**: Repository search shows both production call sites use the
  required two-argument API, and the 45-test suite covers both inactive message
  shapes.

### Issue 3
- **Status**: Resolved
- **What changed**: The extension now accepts an acknowledgement only when both
  state and token exactly match. A rejected or omitted token clears delivery
  state, displays the disconnected warning badge, and schedules the bounded
  five-second retry path instead of immediately reposting.
- **Why**: Exact acknowledgement prevents silent protocol drift, while delayed
  retry avoids a synchronous rejection loop and keeps the failure visible.
- **How verified**: `background.test.js` now simulates a host that omits the
  token and asserts the warning badge and scheduled retry; both browser suites
  pass.

---

# Re-Review — 2026-09-05, Round 6 re-verification (post-fix pass 2)

- **Date**: 2026-09-05
- **Reviewer**: claude-opus-5 (Slot 3, highest reasoning setting) — same reviewer
  as the original Round 6 pass above
- **Purpose**: Independently re-verify the three findings above after the
  driving agent's fixes; re-inspect the live diff for regressions.
- **Change set re-reviewed**: live working tree, 21 modified files,
  **1246 insertions / 104 deletions** (was 1174 / 99 at the original pass).
- **Issues Found (this pass)**: 0
- **Verdict**: **CLEAN — all three findings resolved, no new high-confidence issues.**

## Re-Review Evidence Checklist

- [x] Read the complete regenerated prompt
      `C:\Users\gapat.FAREAST\MiniStopwatch\.review-prompts-280\review-round-6.md`
      (2221 lines, 87 KB) including instructions, output contract, and the
      embedded diff.
- [x] Proved the regenerated prompt matches the live tree: extracted the
      embedded diff (lines 82–2220, 2139 lines) and ran `Compare-Object` against
      `git --no-pager diff` (2139 lines) — **no differences**. The prompt diff is
      byte-identical to the working tree, so the reviewed artifact and the live
      code are the same change set.
- [x] `git --no-pager status` / `--stat`: 21 modified files, 1246 / 104; no
      source file was modified by this review.
- [x] Rebuilt every reachable .NET project: `dotnet build MiniStopwatch.App`
      (builds `MiniStopwatch.Core` 2.8.0.0 as well) → 0 warnings, 0 errors;
      `dotnet build ProductivityTracker.NativeHost` (net48) → 0 warnings, 0 errors.
- [x] Re-ran all three suites: `dotnet run --project MiniStopwatch.Tests` →
      **"All 45 tests passed"** (44 before, +1 regression test);
      `node browser-extension-tests/background.test.js` → passed (exit 0);
      `node browser-extension-tests/site-matcher.test.js` → passed (exit 0).
- [x] Replayed the state machine against the rebuilt `MiniStopwatch.Core.dll`
      (2.8.0.0) in PowerShell 7 with a compiled fake `IMonotonicClock`
      (scenarios A–C below).
- [x] Exercised the extension acknowledgement path in an isolated Node harness
      with a native host that rejects the token, counting posts, badges, and
      scheduled timers (scenario D below).
- [x] Ran the real `ProductivityTracker.NativeHost.exe` against a live
      `NamedPipeServerStream` and against stdout-only, replaying eight request
      shapes to confirm normalization and wire framing (scenario E below).
- [x] Re-read the post-fix source in full: `StopwatchController.cs`,
      `TrackingController.cs`, `MainWindow.xaml.cs`, `SocialMediaPauseBridge.cs`,
      `ProductivityTracker.NativeHost/Program.cs`, `background.js`,
      `site-matcher.js`, `MiniStopwatch.Tests/Program.cs`, `TimerEngine.swift`,
      `FocusProtection.swift`, `TimerWindow.swift`, `NativeMessagingHost.swift`.
- [x] Re-verified release metadata: `git grep "2\.7\.1"` outside `reviews/` and
      `.review-prompts-280/` returns **no matches**; `VERSION`,
      `Directory.Build.props`, `app.manifest` (2.8.0.0), `manifest.json`,
      `README.md`, `docs/index.html` are all 2.8.0, and `macos/build-package.sh`
      still derives `CFBundleShortVersionString`/`CFBundleVersion` from `VERSION`.
- [ ] Did not compile the macOS Swift targets — no Swift toolchain on this
      Windows host. Swift conclusions come from line-by-line comparison against
      the verified C# implementation plus a type/precedence read of the new
      Swift constructs (`if let` shorthand, `guard` inside `if`, `Substring`
      literal comparison, `??` between two optionals, key-path `filter(\.active)`).

## Verification of Each Prior Finding

### Issue 1 — pending offer never invalidated → **RESOLVED (verified)**

`StopwatchController.OnDistractingWebsiteChanged` (`StopwatchController.cs:153–178`)
now normalizes the token and clears a pending offer *before* delegating to
`SetAutomaticPause`, guarded by `!HasContinueCountingOverride` so the legitimate
`AwaitingFocusLoss → AwaitingRefocus` handoff (owned by
`continueCountingVisitToken`, not the offer token) is untouched.
`TimerEngine.setAutomaticPause` (`TimerEngine.swift:130–149`) mirrors it exactly,
including the `!hasContinueCountingOverride` guard and the same ordering
relative to the override token-mismatch check.

Scenario A — the exact original repro, replayed against the rebuilt DLL:

```
after (true,T1)          Run=False AutoP=True  Can=True  Has=False Cont=False
after (false,T1) offer   Run=True  AutoP=False Can=True  Has=False Cont=False
after (false,null)       Run=True  AutoP=False Can=False Has=False Cont=False   <-- was Can=True
after click Continue     Run=True  AutoP=False Can=False Has=False Cont=False   <-- was Has=True
after 2h                 Run=True  AutoP=False Can=False Has=False Cont=False   <-- label no longer stuck
```

Both user-visible consequences are gone: the command is disabled on an
unprotected page, and clicking it can no longer arm an override bound to an
unreproducible token, so `RefreshDisplay()` / `refreshDisplay()` no longer
relabels the item "Stop counting this site" indefinitely.

Scenario B — token mismatch on a *different* protected visit (the other shape
the fix must handle) also behaves correctly, and the offer for `T1` does not
leak into the override granted for `T2`:

```
offer(T1) pending              Run=True  AutoP=False Can=True  Has=False Cont=False
after (true,T2)                Run=False AutoP=True  Can=True  Has=False Cont=False
click Continue                 Run=True  AutoP=False Can=False Has=True  Cont=True
unfocus (false,T2)             Run=True  AutoP=False Can=False Has=True  Cont=False
return (true,T2)               Run=True  AutoP=False Can=False Has=True  Cont=True
switch (true,T1) must re-pause Run=False AutoP=True  Can=True  Has=False Cont=False
```

Regression test `UnprotectedPageClearsContinueCountingOffer`
(`MiniStopwatch.Tests/Program.cs:510–525`) pins the
`(true,"work-visit") → (false,"work-visit") → (false,null)` sequence and asserts
both `CanContinueCountingOnDistractingWebsite == false` **and** that a subsequent
`ContinueCountingOnDistractingWebsite()` leaves `HasContinueCountingOverride`
false. This is the exact production message that
`browser-extension/background.js:134–139` emits on an unprotected page.

### Issue 2 — unsafe constant-token overload → **RESOLVED (verified)**

- `TrackingController.OnDistractingWebsiteChanged(bool)` and the
  `"legacy-browser-visit"` literal are **gone**: `git grep legacy-browser-visit`
  over `*.cs` returns no matches, and `TrackingController.cs:130–135` exposes
  only the two-argument API with **no default value**.
- Both production call sites (`MainWindow.xaml.cs:278`, `:669`) pass an explicit
  `browserVisitToken`; a `grep` for `OnDistractingWebsiteChanged` across the repo
  shows exactly those two production sites plus the Core definitions.
- The compatibility shim is confined to the test assembly as
  `file static class TrackingControllerTestExtensions`
  (`MiniStopwatch.Tests/Program.cs:893–903`), supplying `"test-protected-visit"`.
  Because C# prefers instance methods over extension methods, the 60-odd
  single-argument test call sites can only bind to this `file`-scoped extension —
  confirming at compile time that no production one-argument overload exists.
- The gap that let Issue 1 through is now covered. Protocol-contract tests call
  the real two-argument API with all three real shapes:
  same token (`ContinueCountingStatusClearsWhileBrowserIsUnfocused`, :464),
  different token (`ContinueCountingDoesNotTransferToAnotherProtectedVisit`, :481),
  and null token (`UnprotectedPageClearsContinueCountingOffer`, :510), plus
  `ContinueCountingCanBeCancelledWhileActive` (:496).
- Note (verified, not a finding): `StopwatchController.OnDistractingWebsiteChanged`
  still declares `string? visitToken = null`. Unlike the deleted overload this is
  fail-safe rather than unsafe — a null/blank token is normalized to `null`, which
  makes `CanContinueCountingOnDistractingWebsite` false and makes
  `ContinueCountingThrough` return before granting anything. Confirmed empirically:
  `(true, "")` and `(true, "   ")` both produce `AutoP=True, Can=False, Has=False`.
  An unbound override cannot be created through it.

### Issue 3 — extension recorded a rejected token as delivered → **RESOLVED (verified)**

`background.js:66–83` now requires an exact match on *both* fields
(`acknowledgedState !== currentState || acknowledgedVisitToken !== currentVisitToken`)
before recording delivery. The `?? currentVisitToken` fallback is gone, so the
extension can no longer mark a host-rejected token as delivered and can no longer
suppress subsequent sends for that visit.

Scenario D — isolated Node harness, host omits `visitToken` on a report that
carried one (counts are deltas per step):

```
baseline posts: 1  badge: PAUSE  timers: 0
after rejection -> posts added: 1  badge: !  title: "Focus Protection: Productivity Tracker is not connected"  timers added: 1
after retry#1   -> posts added: 1  timers added: 1  badge: !
after retry#2   -> posts added: 1  timers added: 1
host accepts    -> posts added: 1  timers added: 0  badge: "PAUSE"  title: "Focus Protection: tracking paused"
```

This confirms all three required properties: (a) the failure is **visible** —
the `"!"` badge and disconnected title are set; (b) the retry is **bounded** —
exactly one post and at most one pending timer per rejection, rate-limited by
the `retryTimer` guard at `RETRY_DELAY_MS` (5 s), never worse than the
pre-existing 5 s `setInterval` heartbeat; (c) there is **no synchronous loop** —
the handler calls `scheduleRetry()` instead of the previous inline
`reportState(currentState, true)`, so even the synchronous test transport does
not recurse. Recovery is automatic: once the host accepts, the badge returns to
normal and no further retry is scheduled.

Scenario E — real `ProductivityTracker.NativeHost.exe`, eight request shapes:

```
{"active":true,"visitToken":"3f2a1b4c-0d5e-6f70-8192-a3b4c5d6e7f8"} -> {"ok":true,"active":true,"visitToken":"3f2a...e7f8","appConnected":...}
{"active":false,"visitToken":"3f2a...e7f8"}                          -> visitToken echoed (browser-unfocus shape preserved)
{"active":false}                                                     -> "visitToken":null
{"active":true,"visitToken":"bad token with spaces"}                 -> "visitToken":null   (rejection path Issue 3 guards)
65- and 66-character tokens                                          -> "visitToken":null
{"active":true,"visitToken":"tab\tinjected"}                         -> "visitToken":null
{"active":true,"visitToken":"line\ninjected"}                        -> "visitToken":null
```

Against a live `NamedPipeServerStream`, the app-side wire bytes were
`1<TAB>3f2a1b4c-0d5e-6f70-8192-a3b4c5d6e7f8<NL>`, matching
`SocialMediaPauseBridge.HandleConnectionAsync`'s parser. Because the
`[A-Za-z0-9-]` allowlist runs *before* the `"{0|1}\t{token}\n"` write, `\t` and
`\n` cannot be injected into the framing. A rejected token still forwards
`1\t\n`, so the app still pauses (enforcement preserved) but with
`visitToken == null`, i.e. it fails safe with Continue counting unavailable.

## Additional Checks Requested for This Pass

- **Windows/macOS parity** — `TimerEngine.swift` matches `StopwatchController.cs`
  statement for statement across all five new areas: the offer-clearing prologue
  and override token-mismatch check (`TimerEngine.swift:131–149`), the
  `AwaitingFocusLoss/Confirmed/AwaitingRefocus` transitions, the offer creation
  gated on `automaticPauseReasons.count == 1 && resumeAfterAutomaticPause &&
  distractingWebsiteVisitToken != nil`, `continueCounting(through:)`
  (:262–290, whose `guard` is the exact De Morgan dual of the C# early return),
  `clearContinueCountingOverride()` (:299–305), and `stop()`'s conditional
  clearing of `continueCountingAvailableUntil` (:259–261).
  `FocusSocketServer.publishAggregateState` mirrors
  `SocialMediaPauseBridge.GetAggregateState` including the
  "most recent active, else most recent token-bearing" fallback and the
  sequence-number tie-break, and both now *remove* a disconnected client's state
  rather than forcing it inactive. `TimerWindow.toggleFocusProtection` mirrors
  `SocialMediaPauseMenuItem_Click`, including the trailing
  `cancelContinueCountingOnDistractingWebsite()` when protection is switched off.
  Both platforms refresh at 100 ms (`displayTimer` / `refreshTimer`), so the
  30-second offer expiry disables the menu item on both without a browser event.
- **Menu cancellation** — `ContinueCountingMenuItem_Click`
  (`MainWindow.xaml.cs:213–223`) and `TimerWindowController.continueCounting()`
  (`TimerWindow.swift:434–443`) both branch on `HasContinueCountingOverride` /
  `hasContinueCountingOverride` and call the cancel API, then refresh. The item's
  enabled state is `Has || Can` on both platforms, so the item stays clickable
  during `AwaitingRefocus` (verified in scenario B: `Has=True` after
  `unfocus (false,T2)`) and cancellation there correctly does *not* re-pause
  (`distractingWebsiteIsActive == false`). Cancelling while the site is active
  re-arms the pause — covered by `ContinueCountingCanBeCancelledWhileActive`.
  `menu.autoenablesItems = false` is scoped to the root `NSMenu` only; every root
  item is either explicitly managed or keeps `NSMenuItem`'s default
  `isEnabled == true`, and the `focusMenu`/`opacityMenu` submenus retain their own
  auto-validation, so no other item can be left disabled.
- **Documentation accuracy** — every behavioural claim added to `README.md`,
  `macos/README.md`, and `docs/index.html` was re-checked against the state
  machine and the tests: tab+site binding (`${tab.id}:${siteKey}`),
  "same visit within 30 seconds keeps counting"
  (`ContinueCountingHandoffIncludesExactDeadline` pins the inclusive 30 s
  boundary), "different protected tab or site … ends the override"
  (`ContinueCountingDoesNotTransferToAnotherProtectedVisit`), "visiting an
  unprotected page … ends the override" (`UnprotectedPageClearsContinueCountingOffer`
  plus the override branch at `StopwatchController.cs:172–177`),
  "more than 30 seconds ends the override" (`ContinueCountingHandoffExpires`,
  `ConfirmedContinueCountingHandoffExpires`), "manual timer pauses, resumes, and
  timer-mode changes do not end the current visit override"
  (`ContinueCountingSurvivesManualPauseAndResume`,
  `ContinueCountingSurvivesNewTimerInSameVisit`), "Session-lock pausing always
  remains enforced" (`ContinueCountingPreservesSessionLockPause`,
  `SessionLockBlocksPendingContinueCountingOffer`), and "includes that time in
  My stats (mini)" (`ContinueCountingContributesProductiveStatistics`). The
  privacy claim also still holds: the token is `crypto.randomUUID()`, the site
  key computed by `getDistractingSiteKey` never leaves the extension, and no
  token is written to the registry, the stats store, or any log.

## Checked and Explicitly Not Defects

Recorded so a future pass does not re-litigate them:

1. **`HasContinueCountingOverride` stays true through an expired
   `AwaitingRefocus` window until the next browser report.** The expiry is
   evaluated inside `SetAutomaticPause`, so if the user arms the override and
   then never touches the browser again, the menu keeps reading "Stop counting
   this site". There is no enforcement hole — the very next report of any shape
   (`(true, same)`, `(true, other)`, `(false, null)`) removes the override and
   re-pauses if appropriate, which `ContinueCountingHandoffExpires` and
   `ConfirmedContinueCountingHandoffExpires` pin. Making the read-side
   time-sensitive would be a design change, not a bug fix, and would break the
   long-lived `AwaitingFocusLoss`/`Confirmed` states.
2. **Transient `"!"` badge when two differing reports are in flight.** An
   acknowledgement that arrives after a newer `reportState` mismatches and briefly
   shows the disconnected badge. It self-corrects on the very next
   acknowledgement (milliseconds) and the redundant `scheduleRetry` is
   indistinguishable from the existing 5 s heartbeat. Verified in scenario D that
   the state converges as soon as the host acknowledges the current payload.
3. **Swift normalizes with `visitToken?.isEmpty == false` while C# uses
   `string.IsNullOrWhiteSpace`.** A whitespace-only token would survive on macOS
   and be nulled on Windows, but both native hosts enforce a `[A-Za-z0-9-]`
   allowlist before anything reaches the socket/pipe (confirmed in scenario E),
   so the divergent input is unreachable.

## Final Verdict

**CLEAN.** Issues 1, 2, and 3 are fixed as described in the Resolution Log, and
each fix was independently re-verified rather than taken on trust. The
regenerated prompt diff is byte-identical to the live tree, all three suites pass
(45 / 45 .NET, both Node suites), both .NET projects build with 0 warnings and
0 errors, release metadata is consistently 2.8.0, and the Windows and macOS
implementations remain behaviourally aligned. No high-confidence issue remains.

_This re-review made no changes to any source file._
