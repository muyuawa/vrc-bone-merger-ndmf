# VRC Bone Merger

基于 [NDMF](https://github.com/bdunderscore/ndmf) 的非破坏性 VRChat PhysBone 合并工具：在构建 Avatar 时，将满足兼容性和安全条件的 PhysBone 链合并，帮助创作者减少组件数量，同时保持原始编辑场景不变。

[English](README_EN.md) | **简体中文** · [下载最新版本](https://github.com/muyuawa/vrc-bone-merger-ndmf/releases/latest) · [更新记录](CHANGELOG.md)

## 主要功能

- **先扫描、再决定**：扫描 Avatar 中可合并的候选组，展示来源链、预计减少的 PhysBone 数量，由创作者明确选择。
- **非破坏性处理**：保存的是合并计划；实际调整骨骼层级、创建合并组件和删除来源组件仅发生在 NDMF 构建副本中。
- **谨慎判断兼容性**：比较实际生效参数、曲线及链结构，并排除可能由动画、Constraint 或重叠根造成问题的情况。
- **可追踪结果**：构建后在 Unity Console 和 NDMF 报告中查看合并前后数量、成功组数以及未完成计划的原因。
- **容易撤销**：删除 Avatar 根下生成的 `VRC Bone Merger` 配置对象，即可停止应用保存的合并计划。

> 工具只合并满足检查条件的链，不能保证所有 PhysBone 都可被合并，也不承诺在任何 Avatar 上达到相同的性能提升。请在发布前测试实际效果。

## 环境与依赖

仓库中的 `package.json` 声明了以下版本要求：

| 组件 | 声明的版本要求 |
| --- | --- |
| Unity | 2022.3 |
| VRChat Avatars SDK | 3.7.0 或更新 |
| NDMF | 1.7.0 或更新 |

若项目使用 Modular Avatar、Avatar Optimizer（AAO）或其他会修改骨骼与 PhysBone 的工具，也请在最终构建结果中核对兼容性。上述为包清单声明的版本要求，不代表每个更新版本都经过逐一测试。

## 安装

1. 使用 VRChat Creator Companion（VCC）或现有项目管理方式安装 VRChat Avatars SDK 和 NDMF。
2. 从 [Releases](https://github.com/muyuawa/vrc-bone-merger-ndmf/releases) 下载最新的 `VRCBoneMerger.unitypackage`。
3. 在 Unity 中通过 `Assets > Import Package > Custom Package...` 导入该文件及其中的必要内容。
4. 保留一份可正常上传的 Avatar 备份，以便对比构建结果。

仓库也包含 `Editor/`、`Runtime/` 和 `package.json` 源文件，便于审查或按项目需要自行集成。

## 使用方法

1. 在 Unity 菜单打开 `Tools > VRC Bone Merger > 扫描与合并计划`。
2. 选择 Avatar 根节点，执行扫描。
3. 查看候选组的来源链、兼容性提示和预计减少数量，勾选需要合并的组。
4. 点击“保存自动合并选择”。工具会在 Avatar 根下创建 `VRC Bone Merger` 配置对象。
5. 在 NDMF 处理的构建或播放模式下检查结果。若不需要合并，删除该配置对象即可停用。

## 合并条件与安全边界

- 有效根为 `Root Transform`；留空或指向组件自身时，使用组件所在物体。
- 有效根必须为同一父物体的直接子级。
- 按 AAO 风格比较实际生效值：连续数值及曲线关键帧允许最多 **12% 相对差异**（接近零时只允许极小的绝对差异）。近似合并采用第一条来源链的配置并显示警告；开关、枚举、正负方向、引用及曲线是否存在仍需严格匹配。
- `Multi Child Type` 必须为 `Ignore`。
- 不合并重叠根、Avatar 外部根、禁用的 PhysBone、被动画控制的 PhysBone 或有效根、Avatar 根节点主 Animator 的 Humanoid 骨架路径，以及带有 Unity/VRC Constraint 的链。衣服和配件内部的独立 Animator 不参与该主 Animator 检查。
- 同一有效根包含多枚 PhysBone 时，只有各同级根上的组件数量、顺序和每项实际生效配置都一致，且不存在组外 PhysBone 再引用这些根，才可合并。根只移动一次，组件按原顺序保留；部分重叠仍禁止。
- 使用曲线的链按有效链长分组：仅合并长度一致、且至少包含两条来源链的组，并按 AAO 规则补偿新增公共根带来的曲线采样偏移。
- 单条来源链达到 **100 个 Transform** 时不会自动合并；合并后的组件最多影响 **128 个 Transform**。
- 普通多节链不强制要求 Endpoint。“跳过缺少有效末端的短链”可排除既无可用真实末端骨骼、也无 `Endpoint Position` 的短链。允许抓取的组仍可合并，但多个独立抓取状态会合为一个。

## 构建过程与排查

插件在 NDMF 的 `Optimizing` 阶段运行，并按现有实现的顺序在 AAO 之后处理。对原始 Avatar 场景的修改不会持久化；骨骼重设父级、生成 PhysBone、移除来源组件均发生在构建副本上。

构建后请检查 Unity Console 与 NDMF 报告，关注合并前后的 PhysBone 数量、合并组数、来源组件数、生成组件数和实际减少数量。保存了扫描计划但部分组未完成时，报告会列出原因。

如果没有发现候选组，先检查父子关系、实际参数、动画/Constraint、曲线长度与 Transform 数量，而不是强制关闭安全检查。遇到问题时请在 [Issues](https://github.com/muyuawa/vrc-bone-merger-ndmf/issues) 提供可复现步骤和相关版本信息。

## 开源与贡献

- 许可证：[MIT](LICENSE)
- 更新记录：[CHANGELOG.md](CHANGELOG.md)
- Bug 反馈和 PR 指南：[CONTRIBUTING.md](CONTRIBUTING.md)

本项目与 VRChat、NDMF、Avatar Optimizer 的维护团队没有官方关联；对应商标及第三方组件归其各自权利人所有。
