# VRC Bone Merger

基于 NDMF 的非破坏性 VRChat PhysBone 合并工具。

## 安装

项目需先安装 VRChat Avatars SDK、NDMF。导入 `VRCBoneMerger.unitypackage` 即可；不要同时安装 UnityPackage 和本地 UPM 版本。

## 使用

1. 打开 `Tools > VRC Bone Merger > 扫描与合并计划`。
2. 选择 Avatar 根节点并扫描。
3. 勾选候选组，查看来源链和预计减少数量。
4. 点击“保存自动合并选择”。Avatar 根下会生成 `VRC Bone Merger` 配置对象，删除该对象即可停用。
5. 构建或进入由 NDMF 处理的播放模式时自动合并。

## 合并条件

- 有效根为 `Root Transform`；留空或指向组件自身时使用组件所在物体。
- 有效根必须是同一个父物体的直接子级。
- 按 AAO 规则比较实际生效值；连续数值与曲线关键帧允许最多 12% 的相对差异（接近零时仅允许极小绝对误差），近似合并后采用第一条来源链的数值。开关、枚举、正负方向、引用和曲线有无仍严格比较。
- `Multi Child Type` 必须为 `Ignore`。
- 不合并重叠根、Avatar 外部根、禁用的 PhysBone、动画控制的 PhysBone 或有效根、Avatar 根节点主 Animator 的 Humanoid 骨架路径以及带 Unity/VRC Constraint 的链；衣服或配件内部的独立 Animator 不参与此项判断。
- 对“同一个根挂有多枚 PhysBone”的情况，仅当每个同级根上的组件数量、组件顺序和逐项实际生效配置都一致，且不存在组外 PhysBone 再引用这些根时才合并。根只迁移一次，合并对象上会按原顺序保留多枚 PhysBone；部分重叠仍禁止。
- 使用曲线时按有效链长分别分组，只合并同长度且至少包含两条来源链的组；合并时会按 AAO 的方式修正新增公共根造成的曲线采样偏移。
- 单条来源链达到 100 个 Transform 时不自动合并；合并组件最多影响 128 个 Transform。
- 普通多节链不要求 Endpoint；“跳过缺少有效末端的短链”可控制是否排除既无可用真实末端骨骼、也无 Endpoint Position 的短链。允许抓取的组仍可合并，但多个独立抓取状态会合为一个。

## 构建行为

插件在 NDMF `Optimizing` 阶段、AAO 之后运行。骨骼改父级、新建合并 PhysBone、删除来源组件都只发生在构建副本；原始 Avatar 保持不变。

构建结束时，Console 和 NDMF 报告会输出本次插件处理前后的 PhysBone 数量、合并组数、来源组件数、生成组件数和实际减少数量。保存扫描计划后，报告还会列出经 Modular Avatar / AAO 后没有完成的计划组及原因。
