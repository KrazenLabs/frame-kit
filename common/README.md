# FrameKit common

The C++ helpers that FrameKit's own panel and the C++ parts of Frame apps share: reading and
writing files, text, reading JSON without ever throwing and writing it, clocks, a
journal-friendly log, clean shutdown on signals, a mailbox for requests from an app's command
line to its service, and a small client for Steam's UI debugger (Chrome DevTools Protocol over a
local WebSocket). C++17, namespace `framekit`. The kit's panel builds from this folder directly; apps
build from a copy of it.

Until 2026-10-17 each app had its own hand-made copy of these files (Base Station Control's,
taken over by Power Bank Meter and the panel), and the copies had drifted apart: a fix in one
never reached the others. This folder is now the only place where they change.

## What's inside (`src/framekit/`)

| file | what |
|---|---|
| `clock.hpp/.cpp` | `wallSeconds()` (Unix time) and `monotonicSeconds()` (`CLOCK_BOOTTIME` on Linux, keeps counting in suspend) |
| `log.hpp` | `log(message)`: `HH:MM:SS message` on stdout, flushed (the user journal under systemd); `Logger`, the callback type |
| `stop_signal.hpp/.cpp` | `installStopHandlers()` for SIGINT/SIGTERM, `stopRequested()`, and `stopSignalFd()` to wake a `poll()` loop |
| `files.hpp/.cpp` | `readFileChecked` (whole file, or whether it is missing or unreadable, with the reason), `readFile` (whole file, or nothing on any error), `writeFileAtomically` (temporary file + rename; `WriteOptions`: `durable` fsyncs the file and its folder, `ownerOnly` makes it 0600; errors give the system's reason), `listDirectory` |
| `text.hpp/.cpp` | `trim`, `startsWith`, `endsWith`, `contains`, `splitWhitespace`, `splitLines`, `toLower`, `capitalizeFirst`, `fnv1aHex`, `joinWords`, `repairUtf8` |
| `json_read.hpp/.cpp` | `parseJson` (never throws; optional error text), `member` / `textMember` / `boolMember` / `numberMember` / `integerMember` (never throw, whatever the JSON holds), `readJsonFile` (missing, unreadable, invalid or a value) |
| `json_write.hpp/.cpp` | `writeJson` (compact or indented, UTF-8 or ASCII escapes) and `writeJsonFile` (indented, atomic) |
| `mailbox.hpp/.cpp` | the command mailbox between an app's command line and its service: request and answer files, expiry of abandoned requests, `waitForAnswer` |
| `devtools.hpp`, `devtools_protocol.cpp`, `devtools_session.cpp` | the DevTools client: find a Steam page by its `vrOverlayKey`, open a session, evaluate JavaScript, add a binding and count its calls; an exchange waits at most 5 s; only ever to 127.0.0.1 |

## Building with it

- Put the copy's `src` folder on the include path (`-I<copy>/src`), include headers as
  `"framekit/text.hpp"`, and compile every `src/framekit/*.cpp`. Never put the copy's top folder
  on the include path: on Windows and macOS file names ignore case, and its `VERSION` file would
  then stand in for the C++ standard header `<version>` (that is why the sources sit one level
  down).
- `json_read`, `json_write`, `mailbox` and the DevTools client need jsoncpp; built and tested with
  jsoncpp 1.9.8 (the copy in the kit's `panel/third_party/jsoncpp`, which Base Station Control and
  Power Bank Meter bundle too).
- `devtools_session.cpp` and `stop_signal.cpp` are Linux-only (sockets, `pipe2`); `text.cpp`,
  `files.cpp`, `clock.cpp`, `json_read.cpp`, `json_write.cpp`, `mailbox.cpp` and
  `devtools_protocol.cpp` also build elsewhere (the panel's tests build them on Windows).
- In an app's own namespace, either qualify (`framekit::readFile`) or name what it uses once with
  `using framekit::readFile;` (the panel does that in `panel/src/common.hpp`).

## Using it in an app

```
framekit common --to third_party/framekit-common      # copy (or update) it
framekit common --check third_party/framekit-common   # is the copy unchanged and current?
```

A copy is the whole folder: this README, `VERSION`, `SHA256SUMS` and `src/`. `SHA256SUMS` is
in `sha256sum` format (line endings counted as LF), so `sha256sum -c SHA256SUMS` checks a copy on
Linux too. Rules:

- **Never edit a copy.** `--check` fails on any changed, missing or added file, and `--to`
  refuses to replace an edited copy. (Notes ending in `.cm`, which a copy may carry, aren't
  checked: `--to` simply replaces them.) A fix belongs in the kit's `common/`, from where every app
  gets it.
- **Helpers only one app needs stay in that app's own files** (Base Station Control's
  `formatNumber` or `isValidNickname`, for instance), not here.
- An app's build can run `framekit common --check` and stop when the copy is out of date.

## Changing it (kit maintainers)

1. Change the files here, and the tests that cover them.
2. Raise `VERSION` (SemVer: patch for a fix, minor for something added, major when a signature
   changes) and add a line to the history below.
3. `framekit common --seal` records the new checksums. It refuses when the files changed but
   `VERSION` didn't, and framekit's tests fail while `SHA256SUMS` doesn't match the files.
4. The panel compiles this folder too: raise `panel/VERSION`, build and sign the panel.
5. Apps pick the new version up with `framekit common --to …` when they next build.

## History

- **1.1.2** (2026-10-19): this README only, no code changed. Notes (`*.cm`) are no longer part of
  the seal. Copies need `framekit common --to` again, since `--check` refuses an older version.
- **1.1.1** (2026-10-18): documentation only, no code changed.
- **1.1.0** (2026-10-18): from Base Station Control's review. `readFileChecked` tells a missing
  file from one that can't be read; write errors carry the system's reason; JSON helpers for the
  apps' own files (`parseJson` with an error text, `boolMember`, `numberMember`,
  `integerMember`, `readJsonFile`, `writeJson`, `writeJsonFile`), so apps stop each writing their
  own jsoncpp boilerplate; the command mailbox (`mailbox.hpp`, replacing the apps' identical
  `requests.cpp` copies: JSON request files, abandoned requests expire instead of running much
  later, `waitForAnswer`); a DevTools exchange waits at most 5 s in total (before, only each
  `recv` had a 2 s timeout, so a Steam UI sending events but no answer could hold the caller far
  longer). Nothing removed, no signature changed (`parseJson` got an optional second parameter).
- **1.0.0** (2026-10-17): the panel's copies made the one source. Compared with the copies in
  Base Station Control and Power Bank Meter (as of 2026-10-17) it has: JSON from Steam or apps read
  without ever throwing (`parseJson`, `member`, `textMember`; their copies call jsoncpp
  accessors that throw on unexpected types); an error reply without an id ends a DevTools
  exchange at once instead of waiting for the timeout (`isErrorWithoutId`); page bindings
  (`addBinding`, `drainEvents`, `takeBindingCalls`; Power Bank Meter's copy has none);
  `stopSignalFd`; `repairUtf8` and `capitalizeFirst`; `readFile` failing on a read error or a
  folder (all three copies returned what they had read, or an empty string); `WriteOptions` with
  `durable` (from Base Station Control's `durable` flag, now also syncing the folder) and
  `ownerOnly`. Not taken over (unused by the panel, kept by the apps in their own files):
  `equalsIgnoreCase`, `hexByte`, `isValidNickname`, `parseInteger`, `parseNumber`,
  `parseLeadingInteger`, `parseKeyValueLines`, `isValidBankName`, `formatNumber`, `pathExists`,
  `readTrimmed`, `sleepInterruptibly`. Changed signature: `writeFileAtomically(path, content,
  error, options)` (Base Station Control's copy: `(path, content, durable, error)`).
