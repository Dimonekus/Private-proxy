# Third-party notices

Private Proxy is a manager application. The actual DPI-bypass engine is built by
other open-source projects and is downloaded separately by `scripts/fetch-engine.ps1`
(see [`engine/manifest.json`](engine/manifest.json) for pinned commits and SHA-256).
Binaries are **not** stored in this repository.

## Components

| Component | Files | Source | License |
|-----------|-------|--------|---------|
| zapret (`winws.exe`) | `engine/bin/winws.exe` | [bol-van/zapret](https://github.com/bol-van/zapret), bundled via [bol-van/zapret-win-bundle](https://github.com/bol-van/zapret-win-bundle) commit `bb6a20af35ed74778eb669f5413990eb8209c711` | MIT (see [`licenses/zapret-LICENSE.txt`](licenses/zapret-LICENSE.txt)) |
| WinDivert | `WinDivert.dll`, `WinDivert64.sys` | [basil00/WinDivert](https://github.com/basil00/WinDivert) (same bundle) | LGPL v3 **or** GPL v2 (see [`licenses/WinDivert-LICENSE.txt`](licenses/WinDivert-LICENSE.txt)) |
| Cygwin runtime | `cygwin1.dll` | [cygwin/cygwin](https://github.com/cygwin/cygwin) (same bundle) | LGPL v3+ with linking exception (see [`licenses/cygwin-COPYING.txt`](licenses/cygwin-COPYING.txt), [`licenses/cygwin-COPYING.LIB.txt`](licenses/cygwin-COPYING.LIB.txt)) |
| Fake packet patterns | `*.bin` in `engine/bin/` | [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) commit `176f56dc3f11ce5915c26abcdde903f93d835b64` | MIT (see [`licenses/flowseal-LICENSE.txt`](licenses/flowseal-LICENSE.txt)) |
| Domain/IP lists | `data/lists/*` | Adapted from Flowseal/zapret-discord-youtube 1.10.4 | MIT |

## Why binaries are not committed

- The repository stays small and contains no executables.
- Every binary is fetched from an **immutable commit URL**, not a moving branch.
- `engine/manifest.json` pins the expected size and SHA-256 of every file.
- `scripts/fetch-engine.ps1` refuses to install a file whose hash does not match.
- The application re-verifies the same hashes at startup and refuses to run the
  engine if the check fails.

## Source code availability

The licenses above require that the corresponding source code remains available:

- zapret: <https://github.com/bol-van/zapret>
- WinDivert: <https://github.com/basil00/WinDivert>
- Cygwin: <https://github.com/cygwin/cygwin>
- Flowseal helper data: <https://github.com/Flowseal/zapret-discord-youtube>

The exact upstream revisions used for the engine binaries are recorded in
`engine/manifest.json`.

## Our own code

The manager application (`src/PrivateProxy.Core`, `src/PrivateProxy.App`), scripts,
strategy descriptions and documentation are licensed under the MIT License — see
[`LICENSE`](LICENSE).
