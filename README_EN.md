# VRC Bone Merger

A non-destructive VRChat PhysBone merging tool built on [NDMF](https://github.com/bdunderscore/ndmf). During avatar builds, it combines compatible PhysBone chains after safety checks, with the goal of reducing component counts while keeping the original Unity scene unchanged.

**English** | [简体中文](README.md) · [Latest release](https://github.com/muyuawa/vrc-bone-merger-ndmf/releases/latest) · [Changelog](CHANGELOG.md)

## Highlights

- **Scan before applying:** Inspect candidate groups, their source chains, and the estimated reduction in PhysBone components. You choose which groups to include.
- **Non-destructive workflow:** The saved configuration records a merge plan. Reparenting transforms, creating merged components, and removing source components happen only in NDMF build copies.
- **Conservative compatibility checks:** Compare effective PhysBone values, curves, and chain structure; reject risky cases involving animation, constraints, or overlapping roots.
- **Auditable results:** Unity Console and NDMF build reports show before/after counts, merged groups, and reasons a planned merge was skipped.
- **Easy to disable:** Remove the `VRC Bone Merger` configuration object under the avatar root to stop applying the saved plan.

> Not every PhysBone chain can or should be merged. The tool does not guarantee a particular performance improvement. Always verify the result in your own avatar before publishing.

## Requirements

The repository's `package.json` declares the following version requirements:

| Component | Declared requirement |
| --- | --- |
| Unity | 2022.3 |
| VRChat Avatars SDK | 3.7.0 or later |
| NDMF | 1.7.0 or later |

If you also use Modular Avatar, Avatar Optimizer (AAO), or other packages that alter transforms or PhysBones, verify the final build for compatibility. These are manifest requirements, not a claim that every newer version has been individually tested.

## Installation

1. Install VRChat Avatars SDK and NDMF in your Unity project, using VRChat Creator Companion (VCC) or your existing package workflow.
2. Download `VRCBoneMerger.unitypackage` from [GitHub Releases](https://github.com/muyuawa/vrc-bone-merger-ndmf/releases).
3. In Unity, choose `Assets > Import Package > Custom Package...` and import the required contents.
4. Keep a working backup of your avatar so you can compare the build output.

The repository also includes the `Editor/`, `Runtime/`, and `package.json` sources for review or manual integration.

## Usage

1. Open `Tools > VRC Bone Merger > 扫描与合并计划` in Unity.
2. Select the avatar root and run a scan.
3. Examine the source chains, compatibility warnings, and expected component reduction. Select the groups you want to merge.
4. Click `保存自动合并选择` (Save automatic merge selection). A `VRC Bone Merger` configuration object is created under the avatar root.
5. Inspect the result during an NDMF-processed build or play-mode workflow. Delete the configuration object to disable the plan.

The editor's menu labels currently appear in Chinese; the Chinese labels above match the existing UI.

## Merge eligibility and safeguards

- The effective root is the PhysBone `Root Transform`. If it is unset or points to the component's own GameObject, that GameObject is used.
- Effective roots must be direct children of the same parent.
- Effective values are compared in an AAO-style manner. Continuous values and curve keyframes may differ by up to **12% relatively** (with a very small absolute tolerance near zero). An approximate merge uses the first source chain's configuration and displays a warning. Toggles, enums, signs/directions, object references, and curve presence still require exact compatibility.
- `Multi Child Type` must be `Ignore`.
- Overlapping roots, roots outside the avatar, disabled PhysBones, animation-controlled PhysBones or effective roots, Humanoid skeleton paths of the avatar root's main Animator, and chains using Unity/VRC Constraints are excluded. Independent Animators inside clothing or accessories do not participate in the main-Animator test.
- Where each effective root has multiple PhysBone components, the sibling roots must match in component count, order, and every effective configuration item; no outside PhysBone may reference those roots. Roots are moved only once, components retain their order, and partial overlaps remain disallowed.
- Curve-based chains are grouped by effective chain length. A group must contain at least two source chains of equal length. Curve sampling is adjusted for the new shared root using the AAO-style correction.
- A single source chain with **100 or more Transforms** is not auto-merged; a merged component may affect no more than **128 Transforms**.
- A normal multi-segment chain does not require an Endpoint. The option to skip short chains lacking a usable end excludes chains without either a real end bone or `Endpoint Position`. Grabbable groups may still merge, but independent grabbing states are combined into one.

## Build behavior and troubleshooting

The plugin runs in the NDMF `Optimizing` phase, after AAO in the current integration order. Reparenting, creating merged PhysBones, and deleting source components only affect the build copy; the original avatar scene is preserved.

After a build, inspect the Unity Console and NDMF report for PhysBone counts before and after processing, number of merge groups, source component count, generated component count, and actual reduction. When a saved group cannot be merged after other processors run, the report explains why.

If no candidates appear, inspect parent relationships, effective values, animation/constraints, curve length, and Transform limits instead of bypassing safeguards. For reproducible problems, open an [issue](https://github.com/muyuawa/vrc-bone-merger-ndmf/issues) with relevant versions and reproduction steps.

## Open source and contributing

- License: [MIT](LICENSE)
- Release history: [CHANGELOG.md](CHANGELOG.md)
- Bug reports and pull requests: [CONTRIBUTING.md](CONTRIBUTING.md)

This is an independent community project and is not officially affiliated with VRChat, NDMF, or Avatar Optimizer. Their trademarks and third-party components remain the property of their respective owners.
