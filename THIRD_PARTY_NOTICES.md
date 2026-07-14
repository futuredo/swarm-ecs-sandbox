# Third-Party Notices

Repository-owned source is licensed under the [MIT License](LICENSE), except where an individual file or the entries below state otherwise.

## RVO2

Parts of the fixed-point ORCA obstacle-line construction and linear-program structure are adapted from:

- Project: [RVO2](https://github.com/snape/RVO2)
- Reference source: [`src/Agent.cc`](https://github.com/snape/RVO2/blob/main/src/Agent.cc)
- Copyright: 2008 University of North Carolina at Chapel Hill
- License: Apache License 2.0

The adaptation changes numeric representation, storage, query integration and deterministic tie-breaks for this repository. The adapted portions remain subject to Apache License 2.0. A copy is provided at [`LICENSES/Apache-2.0.txt`](LICENSES/Apache-2.0.txt).

## Package dependencies

These dependencies are referenced through pinned Unity Package Manager Git tags and remain subject to their own licenses:

| Dependency | Pinned version | License | Source |
|---|---|---|---|
| YooAsset | 3.0.4 | Apache License 2.0 | [tuyoogame/YooAsset](https://github.com/tuyoogame/YooAsset/tree/3.0.4) |
| HybridCLR Unity package | 8.12.0 | MIT License | [focus-creative-games/hybridclr_unity](https://github.com/focus-creative-games/hybridclr_unity/tree/v8.12.0) |

Unity Editor, built-in modules, Unity Test Framework and other Unity Package Manager components are governed by their respective Unity/package terms and are not relicensed by this repository’s MIT License.

Before redistributing a Player, AssetBundle or third-party source, verify the exact dependency versions, notices and applicable Unity distribution terms again.
