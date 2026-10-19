# Third-party code in the panel

## jsoncpp 1.9.8

- **What:** JSON reader and writer used by the panel service (`#include <json/json.h>`).
- **Licence:** public domain where possible, MIT otherwise; see `jsoncpp/LICENSE`.
- **Source:** <https://github.com/open-source-parsers/jsoncpp>, release tag `1.9.8` (June 2026),
  archive `jsoncpp-1.9.8.tar.gz`, SHA-256
  `51828cf3574281d2b79ec2a1c56a9e4c20cc1103711321ea96384cffb8d2d904`. Taken on 2026-10-16.
- **Files:** the library's own sources, unchanged and not amalgamated:
  - `jsoncpp/include/json/*.h` from the archive's `include/json/`;
  - `jsoncpp/src/` from its `src/lib_json/`: `json_reader.cpp`, `json_value.cpp`,
    `json_writer.cpp`, `json_tool.h`, `json_valueiterator.inl`.
- **Built by:** `panel/build.ps1`, which compiles `jsoncpp/src/*.cpp` with warnings off and
  includes `jsoncpp/include`.
- **Why this version:** until 2026-10-16 the panel had jsoncpp 1.6.5 (2015, the copy OpenVR
  ships). Its number parser negated the smallest 64-bit integer for every negative number, which
  is undefined behaviour (UBSan: "negation of -9223372036854775808 cannot be represented",
  `jsoncpp.cpp:1759`). 1.9.8 parses the same input cleanly; both were checked with a UBSan build
  run in WSL.
- **Behaviour change:** the 1.9 writer escapes everything outside ASCII as `\uXXXX` and turns
  invalid UTF-8 into U+FFFD (1.6.5 passed UTF-8 through as it was). The panel relies on that:
  its JSON goes straight into a script it runs in Steam's UI.

To update: download the release archive, check that the files above are still all the library
needs, replace the two folders and `LICENSE`, update this note, raise `panel/VERSION` (so
headsets take the new panel), rebuild with `.\panel\build.ps1 -Test` and sign the build with
`framekit panel-sign` (framekit refuses to package an unsigned panel).
