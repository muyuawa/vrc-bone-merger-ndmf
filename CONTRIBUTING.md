# Contributing / 参与贡献

Thank you for helping improve VRC Bone Merger. Bug reports, documentation fixes, compatibility findings, and focused pull requests are welcome.

感谢你帮助改进 VRC Bone Merger。欢迎反馈 Bug、完善文档、报告兼容性问题或提交范围清晰的 PR。

## Reporting bugs / 反馈问题

Before opening an [issue](https://github.com/muyuawa/vrc-bone-merger-ndmf/issues), check for an existing report. Please include:

提交 Issue 前先检查是否已有相同问题，并尽量提供：

- VRC Bone Merger version / 插件版本（例如 `1.0.1`）
- Unity, VRChat Avatars SDK, and NDMF versions / Unity、VRChat SDK、NDMF 版本
- Modular Avatar, Avatar Optimizer (AAO), and other relevant packages, if installed / 可选的相关插件及其版本
- Expected and actual behavior / 预期与实际行为
- Minimal, reproducible steps / 最小复现步骤
- Relevant Unity Console messages or NDMF build report / 相关报错和构建报告
- Whether the issue occurs in the original editor scene, the build copy, or both / 问题出现的位置

Please remove credentials, personal information, private avatar assets, and other content you do not have permission to share. A small synthetic reproduction is preferable to uploading a purchased avatar.

请勿公开凭据、个人信息或无权分发的付费 Avatar 素材。优先使用能复现问题的简化测试项目。

## Pull requests / 提交 PR

1. Open an issue for substantial behavior changes so the approach can be discussed first. Small documentation or typo fixes can be submitted directly.
2. Keep each PR focused; explain why the change is needed and link the relevant issue when available.
3. Do not weaken merge-safety checks merely to increase the number of candidates. Explain the compatibility assumptions for any safety-related change.
4. When changing merge behavior, test scanning, saving a plan, running an NDMF build, comparing before/after reports, and confirming the source avatar remains unchanged.
5. Update README and CHANGELOG when a user-visible behavior, version requirement, or installation workflow changes.
6. Submit code and assets only when you have the right to contribute them under this project's MIT license.

1. 涉及较大行为变化时请先开 Issue 讨论，小型文档或拼写修正可以直接提交。
2. 每个 PR 尽量聚焦一件事，说明修改原因并附上相关 Issue。
3. 不要为了增加合并数量而随意放宽安全检查；修改兼容性逻辑时请解释依据。
4. 修改合并逻辑后，请检查扫描、保存计划、NDMF 构建、前后数量报告，以及原始 Avatar 是否保持不变。
5. 用户可见行为、环境要求或安装方式变化时，请同步更新 README 和 CHANGELOG。
6. 只提交你有权以本项目 MIT 许可证贡献的代码和素材。

## Scope / 范围

The project aims to make compatible PhysBone merges non-destructively and predictably. Cases that cannot be merged safely should remain unmerged, with a useful explanation whenever possible.

项目优先保证可预测、非破坏性的合并。无法安全合并的情况应保留原状，并尽可能提供明确原因。
