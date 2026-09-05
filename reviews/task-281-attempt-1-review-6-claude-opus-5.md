# Code Review - Task 281, Attempt 1, Round 6

## Review Summary
- **Round**: 6
- **Theme**: Polish & hardening
- **Mode**: sequential
- **Model**: Claude Opus 5 (slot 3, highest reasoning effort)
- **Artifact**: C:\Users\gapat.FAREAST\MiniStopwatch\reviews\task-281-attempt-1-review-6-claude-opus-5.md
- **Issues Found**: 0
- **Verdict**: CLEAN

## Evidence Checklist

- [x] Established the true scope of the change by diffing the staged tree against the
      pre-feature release commit rather than only against `HEAD`:
      `git --no-pager diff f2d253c --staged --stat -- . ':(exclude)reviews'` returns exactly
      9 files. Every other file touched by the revert (`SocialMediaPauseBridge.cs`,
      `StopwatchController.cs`, `TrackingController.cs`, `MainWindow.xaml`,
      `MiniStopwatch.Tests/Program.cs`, `browser-extension/background.js`,
      `site-matcher.js`, `background.test.js`, `macos/README.md`,
      `macos/Sources/{TimerEngine,TimerWindow,FocusProtection}.swift`) is byte-identical to
      the already-shipped 2.7.1 tree, so the net new surface is only: the 2.8.1 version bump,
      the two native-host compatibility acknowledgements, and the restored Windows
      `Timer complete` tooltip.
- [x] Confirmed `git --no-pager show --stat 4010e6a` contains nothing except the Continue
      counting feature plus the 2.8.0 version bump, so a full revert is the correct scope and
      no unrelated 2.8.0 work is lost. `git log -S"Timer complete"` shows the macOS
      `TimerWindow.swift:578` tooltip came from `46de8f0` (macOS beta), not from `4010e6a`,
      so restoring the Windows arm removes — rather than creates — a platform divergence.
- [x] Verified the removed behaviour is gone everywhere user-visible. A case-insensitive
      repo-wide grep for `continue.?count|stop counting|visitToken|ContinueCounting` over
      shipped files returns only: the two intentional native-host compatibility fields, and
      two unrelated pre-existing Add-and-Start dialog strings
      (`MiniStopwatch.App/TimerDialog.xaml.cs:25`, `macos/Sources/TimerWindow.swift:388`,
      both meaning "keep the stopwatch counting after adding time"). No
      `ContinueCountingMenuItem` in `MainWindow.xaml`, no `continueCountingMenuItem` in
      `TimerWindow.swift`, no `HasContinueCountingOverride` /
      `hasContinueCountingOverride` in either core engine.
- [x] Verified release metadata is complete and internally consistent.
      `git --no-pager grep -n -E "2\.7\.[0-9]|2\.8\.0" -- . ':(exclude)reviews'
      ':(exclude).review-prompts-281'` returns a single hit, `README.md:52`
      ("Statistics begin with version 2.7.0"), which is a correct historical statement.
      `Directory.Build.props` (Version/Assembly/File/Informational),
      `MiniStopwatch.App/app.manifest` (`2.8.1.0`), `VERSION`,
      `browser-extension/manifest.json`, `README.md`, and `docs/index.html` are all 2.8.1.
      `macos/build-package.sh:5` derives `CFBundleShortVersionString`/`CFBundleVersion` from
      `VERSION`, and `MiniStopwatch.Installer/Program.cs:193-195` derives `DisplayVersion`
      from the assembly version, so neither has a hard-coded string to miss.
- [x] Verified all six `docs/index.html` version/download references
      (lines 38, 46, 56, 300, 309, 318) and all five `README.md` references (download
      heading, three release-download links, release tag URL) point at `v2.8.1`. The
      `docs/index.html` feature card 04 copy and the context-menu mock (lines 269-278) are
      byte-identical to the 2.7.1 page, i.e. the `Continue / Stop counting this site` row and
      the "Continue counting is limited to the protected visit you approve" sentence are both
      gone with no orphaned wording left behind.
- [x] Verified the Windows menu and tooltip. `MainWindow.xaml` context menu is
      Toggle / Reset / Add and Start… / Set Timer… / Exit Timer (collapsed) / Focus
      Protection / Transparency / My stats (mini) / Minimize / Exit — matching the
      `README.md:32-33` prose list with Continue counting removed. The restored tooltip
      (`MainWindow.xaml.cs:408-413`) is exactly the 2.8.0 expression minus the
      `IsContinuingCountingOnDistractingWebsite` arm (compared against
      `git show 4010e6a:MiniStopwatch.App/MainWindow.xaml.cs`).
- [x] Proved the tooltip/fill branch-order difference is unreachable rather than a latent
      mismatch. The fill (`MainWindow.xaml.cs:397-401`) tests `IsTimerCompleted` first while
      the tooltip tests `IsAutomaticallyPaused` first, and macOS
      (`TimerWindow.swift:559-587`) tests `isRunning` before `isTimerCompleted`. All three
      orderings are equivalent because `TrackingController.Update()` calls `stopwatch.Stop()`
      when it sets `IsTimerCompleted`, and `StopwatchController.Stop()` clears
      `resumeAfterAutomaticPause`; since
      `IsAutomaticallyPaused => resumeAfterAutomaticPause && automaticPauseReasons.Count > 0`
      and `SetAutomaticPause` only sets `resumeAfterAutomaticPause` when `IsRunning`, the
      states `IsTimerCompleted && IsRunning` and `IsTimerCompleted && IsAutomaticallyPaused`
      are both unreachable.
- [x] Empirically validated the Windows compatibility acknowledgement by driving the built
      `ProductivityTracker.NativeHost.exe` (net48, Release) over real length-prefixed native
      messaging frames. Results: a 36-char UUID is echoed verbatim for both `active:true` and
      `active:false`; a 64-char token is echoed; a 65-char token, a JSON object, an array, a
      number, a boolean, an explicit `null`, and an absent field all yield `visitToken:null`
      with **exit code 0** and no stderr — confirming round 3's `object`-typed fix genuinely
      prevents a malformed legacy field from aborting the host loop. A token containing
      `</b>&"q" <script>` is emitted safely escaped as
      `"\u003c/b\u003e\u0026\"q\" \u003cscript\u003e"`, so `JavaScriptSerializer` gives no
      JSON/script injection path back into the extension.
- [x] Confirmed the acknowledgement is correctly scoped — it satisfies the v2.8.0 extension
      without leaking anything. Replaying the three real host responses against the v2.8.0
      guard from `git show 4010e6a:browser-extension/background.js`
      (`typeof message.visitToken === "string" ? message.visitToken : undefined`, compared
      with `===` to `currentVisitToken`) yields a match for all of: Windows `visitToken:null`
      vs `undefined`, Windows echoed UUID vs the same UUID, and the macOS omitted key vs
      `undefined`. So a still-running v2.8.0 worker keeps validating deliveries and never
      falls into the `"!"` badge + 5 s `scheduleRetry` loop.
- [x] Confirmed the token never reaches either desktop app. The Windows host now writes only
      `Encoding.ASCII.GetBytes(active ? "1\n" : "0\n")` and `SendToApplication` no longer
      takes a token parameter; macOS writes only the literal byte pairs `[49,10]`/`[48,10]`.
      `SocialMediaPauseBridge.HandleConnectionAsync` parses `message == "1"` and
      `FocusProtection.swift:240` parses `line.first == 49`, both matching. This keeps
      `README.md:74-77` and `macos/README.md:43-45` ("sends only a boolean active/inactive
      state", "URLs and browsing history never leave the browser") factually accurate.
- [x] Checked the reverse upgrade hazard the acknowledgement does *not* cover, and confirmed
      it is closed by the installer rather than left open. The 2.8.1 Windows bridge dropped
      2.8.0's tolerant `message.IndexOf('\t')` parse, so a stale 2.8.0 host writing
      `"1\t<uuid>\n"` would be read as inactive. `MiniStopwatch.Installer/Program.cs:72-81`
      refuses to install while the app runs, and `StopNativeMessagingHosts()` (lines 325-360)
      kills every `ProductivityTracker.NativeHost` process at the install path before
      `ExtractPayload`, so no 2.8.0 host survives the upgrade. macOS is immune regardless
      because it inspects only the first byte of each frame. Not an issue.
- [x] Build and test gates run locally on the reviewed tree:
      `dotnet build MiniStopwatch.sln -c Release` → **0 warnings, 0 errors** (App, Core,
      Tests, NativeHost, Installer, all stamped 2.8.1);
      `dotnet MiniStopwatch.Tests/bin/Release/net8.0/MiniStopwatch.Tests.dll` → **all 25
      tests passed**, including the Focus Protection cases "Distracting site pauses and
      resumes tracking", "Lock and distracting site require both to clear", and "Manual stop
      during automatic pause prevents resume";
      `node browser-extension-tests/background.test.js` and
      `node browser-extension-tests/site-matcher.test.js` → both pass with exit code 0
      (the latter proves nothing still imports the removed `getDistractingSiteKey` export).
- [x] Confirmed the index matches the reviewed working tree: `git --no-pager diff --stat`
      (index vs worktree) is empty, and `git status --porcelain` shows all 21 source/doc
      files plus the five prior round artifacts staged. The only untracked path is
      `.review-prompts-281/`, which holds the review *prompts*, not artifacts, and matches
      the existing repository convention (`git ls-files .review-prompts-280` is likewise
      empty). This resolves round 3's operational finding.
- [ ] Did not compile `macos/NativeHost/NativeMessagingHost.swift`,
      `macos/Sources/FocusProtection.swift`, `TimerEngine.swift`, or `TimerWindow.swift` —
      no Swift toolchain is available on this Windows host, and
      `.github/workflows/build-macos.yml` is the authoritative gate. Mitigated by review: the
      three Swift source files under `macos/Sources/` are byte-identical to the shipped
      2.7.1 tree, and the only new Swift is the `BrowserState` struct plus the
      `candidate.flatMap { $0.utf8.count <= 64 ? $0 : nil }` bound in
      `NativeMessagingHost.swift:122-123`, which resolves to `String?` under the standard
      `Optional.flatMap<U>((Wrapped) -> U?) -> U?` signature (the constraint solver prefers
      the single-injection solution `U == String`), and whose only consumers are
      `if let visitToken { response["visitToken"] = visitToken }` and the `BrowserState`
      initialiser.

## Conclusion

No issues found. The revert is faithful and minimally scoped: outside the version bump, the
only code that is not byte-identical to the already-shipped pre-feature release is the
bounded, opaque, non-forwarded compatibility acknowledgement in the two native hosts and the
restored Windows `Timer complete` tooltip. All Continue counting menu items, tooltips, state,
wire fields, and documentation are removed on both platforms and from the marketing page;
2.8.1 metadata and download links are complete and consistent; and the build, the 25 desktop
tests, and both browser-extension test suites pass on the reviewed tree.
