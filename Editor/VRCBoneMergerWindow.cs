using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace VRCBoneMerger
{
    public sealed class VRCBoneMergerWindow : EditorWindow
    {
        private const string SettingsHolderName = "VRC Bone Merger";
        private sealed class BoneEntry
        {
            public VRCPhysBone component;
            public Transform root;
            public Transform parent;
            public bool endpointMissing;
            public string componentPath;
            public string skipReason;
        }

        private sealed class MergeGroup
        {
            public Transform parent;
            public List<BoneEntry> entries = new List<BoneEntry>();
            public List<List<BoneEntry>> profiles = new List<List<BoneEntry>>();
            public List<string> warnings = new List<string>();
            public bool blocked;
            public string targetName;
            public bool selected = true;

            public int ProfileCount => profiles.Count > 0 ? profiles.Count : 1;
            public List<Transform> Roots => entries.Where(x => x.root != null)
                .Select(x => x.root).Distinct().ToList();

            public string Status
            {
                get
                {
                    if (blocked) return "禁止合并";
                    return warnings.Count > 0 ? "可合并但需复核" : "可直接合并";
                }
            }
        }

        private sealed class RootProfileSequence
        {
            public Transform root;
            public List<BoneEntry> entries;
        }

        private GameObject avatarRoot;
        private readonly List<MergeGroup> groups = new List<MergeGroup>();
        private readonly List<BoneEntry> scannedBones = new List<BoneEntry>();
        private Vector2 groupScroll;
        private Vector2 detailScroll;
        private Vector2 boneScroll;
        private int selectedGroup = -1;
        private bool showScannedBones;
        private string boneSearch = string.Empty;
        private bool includeInactive = true;
        private bool autoMerge = true;
        private bool mergeOnlySelected;
        private bool skipMissingEndpoint = true;

        [MenuItem("Tools/VRC Bone Merger/扫描与合并计划")]
        public static void Open()
        {
            VRCBoneMergerWindow window = GetWindow<VRCBoneMergerWindow>("VRC Bone Merger");
            if (Selection.activeGameObject != null && window.avatarRoot == null)
            {
                window.avatarRoot = Selection.activeGameObject;
                window.LoadSettingsFromAvatar();
            }
        }

        public void SetAvatarRoot(GameObject root)
        {
            if (root != null && root.GetComponent<VRCBoneMergerSettings>() != null && root.transform.parent != null)
            {
                root = root.transform.parent.gameObject;
            }
            VRCBoneMergerSettings existing = root == null ? null : root.GetComponentInChildren<VRCBoneMergerSettings>(true);
            if (existing != null && existing.gameObject != root && existing.transform.parent != null)
            {
                root = existing.transform.parent.gameObject;
            }
            avatarRoot = root;
            LoadSettingsFromAvatar();
        }

        public void ScanFromInspector()
        {
            Scan();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("VRC PhysBone 扫描与合并计划", EditorStyles.boldLabel);
            EditorGUILayout.Space(8);

            using (new EditorGUILayout.HorizontalScope())
            {
                GameObject nextRoot = (GameObject)EditorGUILayout.ObjectField("Avatar 根节点", avatarRoot, typeof(GameObject), true);
                if (nextRoot != avatarRoot)
                {
                    avatarRoot = nextRoot;
                    LoadSettingsFromAvatar();
                    groups.Clear();
                    scannedBones.Clear();
                }
                if (GUILayout.Button("从选择读取", GUILayout.Width(88)))
                {
                    if (Selection.activeGameObject != null)
                    {
                        avatarRoot = Selection.activeGameObject;
                        LoadSettingsFromAvatar();
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                autoMerge = EditorGUILayout.ToggleLeft("构建时自动合并", autoMerge, GUILayout.Width(125));
                mergeOnlySelected = EditorGUILayout.ToggleLeft("仅合并勾选组", mergeOnlySelected, GUILayout.Width(112));
                includeInactive = EditorGUILayout.ToggleLeft("包含未激活对象", includeInactive, GUILayout.Width(125));
                GUILayout.FlexibleSpace();
                GUI.enabled = avatarRoot != null;
                if (GUILayout.Button("扫描 PhysBone", GUILayout.Width(112))) Scan();
                GUI.enabled = true;
            }

            skipMissingEndpoint = EditorGUILayout.ToggleLeft("跳过缺少有效末端的短链",
                skipMissingEndpoint, GUILayout.Width(185));

            EditorGUILayout.Space(8);
            if (avatarRoot == null)
            {
                EditorGUILayout.HelpBox("请选择 Avatar 根节点，然后点击“扫描 PhysBone”。", MessageType.Info);
                return;
            }

            DrawScanSummary();
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("保存自动合并选择")) SaveSettingsToAvatar();
                if (GUILayout.Button("复制 Avatar 并合并勾选组")) CreateMergedAvatarCopy();
                if (GUILayout.Button("重新扫描", GUILayout.Width(90))) Scan();
            }
            EditorGUILayout.Space(4);

            DrawScannedBones();
            EditorGUILayout.Space(4);
            if (groups.Count == 0)
            {
                EditorGUILayout.HelpBox("没有找到同一父物体下的两个或更多可合并 PhysBone。完整扫描清单已在上方展开。", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawGroupList();
                DrawDetails();
            }
        }

        private void DrawScanSummary()
        {
            int scanned = scannedBones.Count;
            int candidateSources = groups.Sum(x => x.entries.Count);
            int selectedGroups = autoMerge ? groups.Count(x => GetPredictedReduction(x) > 0) : 0;
            int selectedSources = autoMerge ? groups.Where(x => x.selected && !x.blocked).Sum(x => GetMergeEntries(x).Count) : 0;
            int reduction = autoMerge ? groups.Where(x => x.selected).Sum(GetPredictedReduction) : 0;
            int after = Math.Max(0, scanned - reduction);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    string.Format("扫描到 {0} 个 PhysBone · 候选来源 {1} · 候选组 {2}", scanned, candidateSources, groups.Count),
                    EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(
                    string.Format("{0} {1} 组 · 参与合并 {2} 个 · 合并后预计 {3} 个 · 减少 {4} 个", autoMerge ? "自动合并" : "自动合并已关闭", selectedGroups, selectedSources, after, reduction),
                    EditorStyles.miniLabel);
            }
        }

        private void DrawScannedBones()
        {
            showScannedBones = EditorGUILayout.Foldout(showScannedBones, "显示扫描出的全部 PhysBone", true);
            if (!showScannedBones) return;

            boneSearch = EditorGUILayout.TextField("筛选路径", boneSearch);
            using (var scroll = new EditorGUILayout.ScrollViewScope(boneScroll, GUILayout.Height(180)))
            {
                boneScroll = scroll.scrollPosition;
                int shown = 0;
                const int maxRows = 300;
                foreach (BoneEntry entry in scannedBones)
                {
                    string haystack = (entry.componentPath ?? string.Empty) + " " + GetPath(entry.root);
                    if (!string.IsNullOrEmpty(boneSearch) && haystack.IndexOf(boneSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.ObjectField(entry.component, typeof(VRCPhysBone), true, GUILayout.Width(210));
                        EditorGUILayout.LabelField("根: " + GetPath(entry.root), EditorStyles.miniLabel);
                        if (!string.IsNullOrEmpty(entry.skipReason))
                        {
                            EditorGUILayout.LabelField(entry.skipReason, EditorStyles.miniLabel, GUILayout.Width(145));
                        }
                    }
                    if (++shown >= maxRows) break;
                }
                if (shown == 0) EditorGUILayout.LabelField("没有符合筛选条件的 PhysBone。", EditorStyles.miniLabel);
                else if (shown >= maxRows) EditorGUILayout.LabelField("列表已限制为前 300 条，请使用路径筛选。", EditorStyles.miniLabel);
            }
        }

        private void DrawGroupList()
        {
            using (var scroll = new EditorGUILayout.ScrollViewScope(groupScroll, GUILayout.Width(300), GUILayout.ExpandHeight(true)))
            {
                groupScroll = scroll.scrollPosition;
                for (int i = 0; i < groups.Count; i++)
                {
                    MergeGroup group = groups[i];
                    Color old = GUI.backgroundColor;
                    if (i == selectedGroup) GUI.backgroundColor = new Color(.78f, .92f, .96f);
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUI.BeginChangeCheck();
                            bool selected = EditorGUILayout.Toggle(group.selected, GUILayout.Width(18));
                            if (EditorGUI.EndChangeCheck()) group.selected = selected;
                            if (GUILayout.Button(group.targetName, EditorStyles.boldLabel)) selectedGroup = i;
                        }
                        GUI.backgroundColor = old;
                        EditorGUILayout.LabelField(group.Status, EditorStyles.miniLabel);
                        EditorGUILayout.LabelField(group.ProfileCount > 1
                            ? string.Format("{0} 条根链 × {1} 套配置 · 父物体: {2}", group.Roots.Count, group.ProfileCount, group.parent.name)
                            : string.Format("{0} 条链 · 父物体: {1}", group.Roots.Count, group.parent.name), EditorStyles.miniLabel);
                        EditorGUILayout.LabelField(string.Format("预计减少: {0} 个 PhysBone", GetPredictedReduction(group)), EditorStyles.miniLabel);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("定位", GUILayout.Width(48))) FocusGroup(group);
                        }
                    }
                    EditorGUILayout.Space(3);
                }
            }
        }

        private void DrawDetails()
        {
            using (var scroll = new EditorGUILayout.ScrollViewScope(detailScroll, GUILayout.ExpandWidth(true), GUILayout.MinWidth(360), GUILayout.ExpandHeight(true)))
            {
                detailScroll = scroll.scrollPosition;
                if (selectedGroup < 0 || selectedGroup >= groups.Count)
                {
                    EditorGUILayout.HelpBox("选择左侧候选组查看来源链和合并风险。", MessageType.Info);
                    return;
                }

                MergeGroup group = groups[selectedGroup];
                EditorGUILayout.LabelField(group.targetName, EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                group.selected = EditorGUILayout.ToggleLeft("自动合并此组", group.selected);
                if (EditorGUI.EndChangeCheck()) Repaint();
                EditorGUILayout.LabelField("父物体", group.parent.name);
                EditorGUILayout.LabelField("完整路径", GetPath(group.parent), EditorStyles.miniLabel);
                if (group.blocked)
                    EditorGUILayout.LabelField(string.Format("检测到：{0} 个来源组件；已阻止自动合并", group.entries.Count), EditorStyles.miniLabel);
                else
                    EditorGUILayout.LabelField(string.Format("预计：{0} 个来源组件 → {1} 个合并组件，减少 {2} 个 PhysBone", GetMergeEntries(group).Count, GetOutputComponentCount(group), GetPredictedReduction(group)), EditorStyles.miniLabel);
                if (group.ProfileCount > 1)
                    EditorGUILayout.HelpBox("这些根拥有相同数量、相同顺序的多套 PhysBone 配置。根只迁移一次，并在同一个合并根上生成对应数量的 PhysBone。", MessageType.Info);
                EditorGUILayout.Space(5);

                if (group.blocked)
                    EditorGUILayout.HelpBox("已阻止自动合并，请查看风险提示。", MessageType.Error);

                EditorGUILayout.LabelField("来源链", EditorStyles.boldLabel);
                foreach (BoneEntry entry in group.entries)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.ObjectField(entry.component, typeof(VRCPhysBone), true);
                        EditorGUILayout.LabelField("根节点", GetPath(entry.root), EditorStyles.miniLabel);
                        EditorGUILayout.LabelField("链长度", CountChainNodes(entry.root).ToString(), EditorStyles.miniLabel);
                    }
                }

                if (group.warnings.Count > 0)
                {
                    EditorGUILayout.LabelField("风险提示", EditorStyles.boldLabel);
                    foreach (string warning in group.warnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
                }

                EditorGUILayout.Space(5);
                if (GUILayout.Button("定位来源链")) FocusGroup(group);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.enabled = !group.blocked && group.selected;
                    if (GUILayout.Button("复制 Avatar 并合并此组")) CreateMergedAvatarCopy(group);
                    GUI.enabled = true;
                }
            }
        }

        private void Scan()
        {
            groups.Clear();
            scannedBones.Clear();
            selectedGroup = -1;
            if (avatarRoot == null) return;

            VRCPhysBone[] components = avatarRoot.GetComponentsInChildren<VRCPhysBone>(includeInactive);
            VRCBoneMergerAnimationSafety animationSafety = VRCBoneMergerAnimationSafety.FromAvatar(avatarRoot);
            var byParent = new Dictionary<int, List<MergeGroup>>();
            foreach (VRCPhysBone component in components)
            {
                Transform root = GetRoot(component);
                var entry = new BoneEntry
                {
                    component = component,
                    root = root,
                    parent = root != null ? root.parent : null,
                    endpointMissing = root != null && NeedsEndpoint(root, component),
                    componentPath = GetRelativePath(avatarRoot.transform, component.transform)
                };
                scannedBones.Add(entry);

                if (root == null)
                {
                    entry.skipReason = "根节点为空";
                    continue;
                }
                if (root == avatarRoot.transform)
                {
                    entry.skipReason = "Avatar 根本身";
                    continue;
                }
                if (root.parent == null)
                {
                    entry.skipReason = "无共同父物体";
                    continue;
                }
                if (!IsInsideAvatar(avatarRoot.transform, root))
                {
                    Debug.LogWarning("[VRC Bone Merger] 已跳过 Avatar 外部的有效根节点: " + GetPath(root));
                    entry.skipReason = "Avatar 外部，已跳过";
                    continue;
                }
                Transform parent = root.parent;
                List<MergeGroup> parentGroups;
                if (!byParent.TryGetValue(parent.GetInstanceID(), out parentGroups))
                {
                    parentGroups = new List<MergeGroup>();
                    byParent.Add(parent.GetInstanceID(), parentGroups);
                }

                MergeGroup group = parentGroups.FirstOrDefault(x => x.entries.Count > 0
                    && NdmfBoneMergePass.AreAutomaticMergeCompatible(
                        x.entries[0].component, x.entries[0].root, entry.component, entry.root));
                if (group == null)
                {
                    group = new MergeGroup { parent = parent, targetName = "MergedPB_" + parent.name };
                    parentGroups.Add(group);
                }

                entry.parent = parent;
                group.entries.Add(entry);
            }

            var candidateEntries = new HashSet<BoneEntry>();
            foreach (List<MergeGroup> parentGroups in byParent.Values)
            {
                foreach (MergeGroup group in CombineSharedRootProfiles(parentGroups))
                {
                    if (group.Roots.Count < 2) continue;
                    CheckGroup(group, avatarRoot.transform, components, animationSafety);
                    groups.Add(group);
                    foreach (BoneEntry entry in group.entries) candidateEntries.Add(entry);
                }
            }

            foreach (BoneEntry entry in scannedBones)
            {
                if (candidateEntries.Contains(entry) || !string.IsNullOrEmpty(entry.skipReason)) continue;
                if (entry.parent != null)
                {
                    entry.skipReason = "同父级但没有第二条实际生效参数及有效链长均一致的链";
                }
            }

            groups.Sort((a, b) => string.Compare(GetPath(a.parent), GetPath(b.parent), StringComparison.Ordinal));
            CheckCrossGroupOverlaps();
            LoadSelectedGroupsFromSettings();
            if (groups.Count > 0) selectedGroup = 0;
            Repaint();
        }

        private static List<MergeGroup> CombineSharedRootProfiles(List<MergeGroup> rawGroups)
        {
            var result = new List<MergeGroup>();
            var consumed = new HashSet<BoneEntry>();
            var allEntries = rawGroups.SelectMany(x => x.entries).ToList();
            var rootSequences = new List<RootProfileSequence>();
            foreach (IGrouping<Transform, BoneEntry> rootGroup in allEntries.GroupBy(x => x.root))
            {
                Transform root = rootGroup.Key;
                if (root == null) continue;
                List<BoneEntry> controllers = rootGroup.ToList();
                List<BoneEntry> ordered = root.GetComponents<VRCPhysBone>()
                    .Where(pb => GetRoot(pb) == root)
                    .Select(pb => controllers.FirstOrDefault(x => x.component == pb))
                    .Where(x => x != null).ToList();
                if (ordered.Count < 2 || ordered.Count != controllers.Count
                    || ordered.Any(x => x.component.transform != root)) continue;
                rootSequences.Add(new RootProfileSequence { root = root, entries = ordered });
            }

            var families = new List<List<RootProfileSequence>>();
            foreach (RootProfileSequence sequence in rootSequences)
            {
                List<RootProfileSequence> family = families.FirstOrDefault(candidate =>
                {
                    if (candidate[0].entries.Count != sequence.entries.Count) return false;
                    for (int index = 0; index < sequence.entries.Count; index++)
                    {
                        if (!NdmfBoneMergePass.AreAutomaticMergeCompatible(
                                candidate[0].entries[index].component, candidate[0].entries[index].root,
                                sequence.entries[index].component, sequence.entries[index].root))
                            return false;
                    }
                    return true;
                });
                if (family == null)
                {
                    family = new List<RootProfileSequence>();
                    families.Add(family);
                }
                family.Add(sequence);
            }

            foreach (List<RootProfileSequence> unsortedFamily in families.Where(x => x.Count >= 2))
            {
                List<RootProfileSequence> family = unsortedFamily
                    .OrderBy(x => x.root.GetSiblingIndex()).ThenBy(x => x.root.GetInstanceID()).ToList();
                var participants = new HashSet<BoneEntry>(family.SelectMany(x => x.entries));
                var familyRoots = new HashSet<Transform>(family.Select(x => x.root));
                bool hasPartialOverlap = Enumerable.Range(0, family[0].entries.Count).Any(index =>
                    allEntries.Any(entry => !familyRoots.Contains(entry.root)
                        && NdmfBoneMergePass.AreAutomaticMergeCompatible(
                            family[0].entries[index].component, family[0].entries[index].root,
                            entry.component, entry.root)));
                if (hasPartialOverlap) continue;
                var combined = new MergeGroup
                {
                    parent = family[0].root.parent,
                    targetName = "MergedPB_" + family[0].root.parent.name,
                    entries = participants.ToList(),
                    profiles = Enumerable.Range(0, family[0].entries.Count)
                        .Select(index => family.Select(x => x.entries[index]).ToList()).ToList()
                };
                result.Add(combined);
                foreach (BoneEntry entry in participants) consumed.Add(entry);
            }

            foreach (MergeGroup rawGroup in rawGroups)
            {
                List<BoneEntry> remaining = rawGroup.entries.Where(x => !consumed.Contains(x)).ToList();
                var group = new MergeGroup
                {
                    parent = rawGroup.parent,
                    targetName = rawGroup.targetName,
                    entries = remaining
                };
                if (group.Roots.Count < 2 || group.entries.Count != group.Roots.Count) continue;
                group.profiles = new List<List<BoneEntry>> { group.entries.ToList() };
                result.Add(group);
            }
            return result;
        }

        private static void CheckGroup(MergeGroup group, Transform rootOfAvatar, VRCPhysBone[] allComponents,
            VRCBoneMergerAnimationSafety animationSafety)
        {
            group.blocked = false;
            group.warnings.Clear();
            List<Transform> roots = group.Roots;
            for (int i = 0; i < roots.Count; i++)
            {
                for (int j = i + 1; j < roots.Count; j++)
                {
                    Transform a = roots[i];
                    Transform b = roots[j];
                    if (a.IsChildOf(b) || b.IsChildOf(a))
                    {
                        group.blocked = true;
                        group.warnings.Add("来源根节点存在父子重叠，不能安全地迁移到同一个合并根。");
                    }
                }
            }

            int missingEndpoints = group.entries.Count(x => x.endpointMissing);
            if (missingEndpoints > 0)
            {
                group.warnings.Add(string.Format("{0} 条短链既没有真实末端骨骼，也未设置 Endpoint Position。", missingEndpoints));
            }

            if (group.entries.Any(x => NdmfBoneMergePass.AllowsGrabbing(x.component)))
            {
                group.warnings.Add("允许抓取：合并后仍能抓取，但原本多个 PhysBone 的独立抓取状态会合为一个；同时抓不同分支时，后一次抓取可能替换前一次。");
            }

            ISet<VRCPhysBone> familyComponents = group.ProfileCount > 1
                ? new HashSet<VRCPhysBone>(group.entries.Select(x => x.component))
                : null;
            foreach (List<BoneEntry> profile in group.profiles)
            {
                string safetyReason;
                if (NdmfBoneMergePass.IsSafeAutomaticMerge(rootOfAvatar, allComponents,
                        profile.Select(x => x.component), animationSafety, out safetyReason,
                        familyComponents))
                {
                    if (NdmfBoneMergePass.UsesNumericTolerance(profile.Select(x => x.component)))
                    {
                        const string toleranceWarning = "来源组件存在小幅数值差异（最大相对容差 12%）；合并后采用第一条来源链的数值。";
                        if (!group.warnings.Contains(toleranceWarning)) group.warnings.Add(toleranceWarning);
                    }
                    continue;
                }
                group.blocked = true;
                if (!group.warnings.Contains(safetyReason)) group.warnings.Add(safetyReason);
            }
        }

        private void LoadSettingsFromAvatar()
        {
            VRCBoneMergerSettings settings = avatarRoot == null
                ? null
                : avatarRoot.GetComponentInChildren<VRCBoneMergerSettings>(true);
            if (settings == null)
            {
                autoMerge = true;
                mergeOnlySelected = false;
                includeInactive = true;
                skipMissingEndpoint = true;
                return;
            }

            autoMerge = settings.autoMerge;
            mergeOnlySelected = settings.mergeOnlySelected;
            includeInactive = settings.includeInactive;
            skipMissingEndpoint = settings.skipMissingEndpoint;
        }

        private void LoadSelectedGroupsFromSettings()
        {
            VRCBoneMergerSettings settings = avatarRoot == null
                ? null
                : avatarRoot.GetComponentInChildren<VRCBoneMergerSettings>(true);
            if (!mergeOnlySelected || settings == null || settings.selectedRoots == null)
            {
                foreach (MergeGroup group in groups) group.selected = true;
                return;
            }

            foreach (MergeGroup group in groups)
            {
                group.selected = group.Roots.Count > 0 && group.Roots.All(settings.selectedRoots.Contains);
            }
        }

        private void SaveSettingsToAvatar()
        {
            if (avatarRoot == null) return;
            VRCBoneMergerSettings settings = avatarRoot.GetComponentInChildren<VRCBoneMergerSettings>(true);
            Transform holder = avatarRoot.transform.Find(SettingsHolderName);
            GameObject holderObject;
            if (holder == null)
            {
                holderObject = new GameObject(SettingsHolderName);
                Undo.RegisterCreatedObjectUndo(holderObject, "Create VRC Bone Merger settings object");
                holderObject.transform.SetParent(avatarRoot.transform, false);
            }
            else
            {
                holderObject = holder.gameObject;
            }

            VRCBoneMergerSettings holderSettings = holderObject.GetComponent<VRCBoneMergerSettings>();
            if (holderSettings == null)
            {
                holderSettings = Undo.AddComponent<VRCBoneMergerSettings>(holderObject);
                if (settings != null && settings != holderSettings)
                {
                    EditorUtility.CopySerialized(settings, holderSettings);
                    Undo.DestroyObjectImmediate(settings);
                }
            }
            settings = holderSettings;

            Undo.RecordObject(settings, "Save VRC Bone Merger selection");
            settings.autoMerge = autoMerge;
            settings.mergeOnlySelected = mergeOnlySelected;
            settings.includeInactive = includeInactive;
            settings.skipMissingEndpoint = skipMissingEndpoint;
            settings.parameterPolicy = VRCBoneMergerSettings.ParameterPolicy.OnlyMatching;
            settings.removeAfterBuild = true;
            if (settings.selectedRoots == null) settings.selectedRoots = new List<Transform>();
            settings.selectedRoots.Clear();
            if (mergeOnlySelected)
            {
                foreach (MergeGroup group in groups.Where(x => x.selected && !x.blocked))
                {
                    foreach (Transform root in group.Roots)
                    {
                        if (root != null && !settings.selectedRoots.Contains(root)) settings.selectedRoots.Add(root);
                    }
                }
            }
            if (settings.recordedPlan == null) settings.recordedPlan = new List<VRCBoneMergerRecordedGroup>();
            settings.recordedPlan.Clear();
            settings.recordedScanPhysBoneCount = scannedBones.Count;
            settings.recordedPredictedReduction = autoMerge
                ? groups.Where(x => x.selected).Sum(GetPredictedReduction)
                : 0;
            if (autoMerge)
            {
                foreach (MergeGroup group in groups.Where(x => x.selected && GetPredictedReduction(x) > 0))
                {
                    settings.recordedPlan.Add(new VRCBoneMergerRecordedGroup
                    {
                        parentPath = GetRelativePath(avatarRoot.transform, group.parent),
                        profileCount = group.ProfileCount,
                        predictedReduction = GetPredictedReduction(group),
                        roots = group.Roots.ToList()
                    });
                }
            }
            EditorUtility.SetDirty(settings);
            ShowNotification(new GUIContent(autoMerge
                ? (mergeOnlySelected ? "已保存：构建时只合并勾选组" : "已保存：构建时合并全部安全匹配组")
                : "已保存：构建时自动合并已关闭"));
        }

        private List<BoneEntry> GetMergeEntries(MergeGroup group)
        {
            if (group == null || group.blocked || !group.selected) return new List<BoneEntry>();
            if (skipMissingEndpoint && group.entries.Any(x => x.endpointMissing)) return new List<BoneEntry>();
            return group.entries.ToList();
        }

        private int GetPredictedReduction(MergeGroup group)
        {
            int count = GetMergeEntries(group).Count;
            int outputs = GetOutputComponentCount(group);
            return group != null && group.Roots.Count >= 2 ? Math.Max(0, count - outputs) : 0;
        }

        private int GetOutputComponentCount(MergeGroup group)
        {
            return GetMergeEntries(group).Count > 0 ? group.ProfileCount : 0;
        }

        private void CheckCrossGroupOverlaps()
        {
            for (int i = 0; i < groups.Count; i++)
            {
                for (int j = i + 1; j < groups.Count; j++)
                {
                    bool overlaps = groups[i].Roots.Any(a => groups[j].Roots.Any(b =>
                        a == b || a.IsChildOf(b) || b.IsChildOf(a)));
                    if (!overlaps) continue;
                    groups[i].blocked = true;
                    groups[j].blocked = true;
                    groups[i].warnings.Add("与另一候选组存在重叠骨骼链，已阻止批量副本合并。");
                    groups[j].warnings.Add("与另一候选组存在重叠骨骼链，已阻止批量副本合并。");
                }
            }
        }

        private void CreateMergedAvatarCopy()
        {
            CreateMergedAvatarCopy(null);
        }

        private void CreateMergedAvatarCopy(MergeGroup onlyGroup)
        {
            if (avatarRoot == null) return;
            if (onlyGroup != null && GetPredictedReduction(onlyGroup) <= 0) return;
            List<MergeGroup> targets = onlyGroup == null
                ? groups.Where(x => GetPredictedReduction(x) > 0).ToList()
                : new List<MergeGroup> { onlyGroup };
            if (targets.Count == 0) return;

            GameObject copy = Instantiate(avatarRoot);
            copy.name = avatarRoot.name + "_MergedCopy";
            Undo.RegisterCreatedObjectUndo(copy, "Create non-destructive merged Avatar copy");
            SceneView scene = SceneView.lastActiveSceneView;
            if (scene != null)
            {
                copy.transform.position = avatarRoot.transform.position + Vector3.right * Mathf.Max(0.25f, avatarRoot.transform.lossyScale.x * 2f);
                copy.transform.rotation = avatarRoot.transform.rotation;
            }

            int applied = 0;
            VRCBoneMergerAnimationSafety copyAnimationSafety = VRCBoneMergerAnimationSafety.FromAvatar(copy);
            foreach (MergeGroup group in targets)
            {
                HashSet<string> selectedPaths = new HashSet<string>(
                    group.Roots.Select(x => GetRelativePath(avatarRoot.transform, x)));
                MergeGroup copyGroup = FindMatchingGroup(copy.transform, avatarRoot.transform, group,
                    selectedPaths, copyAnimationSafety);
                if (copyGroup == null || copyGroup.blocked) continue;
                MergeGroupOnCopiedAvatar(copyGroup);
                applied++;
            }

            Selection.activeGameObject = copy;
            EditorGUIUtility.PingObject(copy);
            EditorUtility.DisplayDialog("已创建非破坏副本", string.Format("原 Avatar 未改动。已在副本 {0} 上合并 {1} 组。\n\n副本名称：{2}", copy.name, applied, copy.name), "确定");
            Scan();
        }

        private static MergeGroup FindMatchingGroup(Transform copyRoot, Transform sourceAvatarRoot,
            MergeGroup sourceGroup, ISet<string> selectedPaths, VRCBoneMergerAnimationSafety animationSafety)
        {
            Transform parent = FindByPath(copyRoot, GetRelativePath(sourceAvatarRoot, sourceGroup.parent));
            if (parent == null) return null;
            var profiles = new List<List<BoneEntry>>();
            IEnumerable<List<BoneEntry>> sourceProfiles = sourceGroup.profiles.Count > 0
                ? sourceGroup.profiles
                : new[] { sourceGroup.entries };
            foreach (List<BoneEntry> sourceProfile in sourceProfiles)
            {
                var copiedProfile = new List<BoneEntry>();
                foreach (BoneEntry sourceEntry in sourceProfile)
                {
                    string componentPath = GetRelativePath(sourceAvatarRoot, sourceEntry.component.transform);
                    Transform componentTransform = FindByPath(copyRoot, componentPath);
                    if (componentTransform == null) return null;
                    VRCPhysBone[] sourceComponents = sourceEntry.component.transform.GetComponents<VRCPhysBone>();
                    int componentIndex = Array.IndexOf(sourceComponents, sourceEntry.component);
                    VRCPhysBone[] copyComponents = componentTransform.GetComponents<VRCPhysBone>();
                    if (componentIndex < 0 || componentIndex >= copyComponents.Length) return null;
                    VRCPhysBone component = copyComponents[componentIndex];
                    Transform root = GetRoot(component);
                    string relativePath = root == null ? string.Empty : GetRelativePath(copyRoot, root);
                    if (root == null || !IsInsideAvatar(copyRoot, root) || root.parent != parent
                        || (selectedPaths != null && !selectedPaths.Contains(relativePath))
                        || !VRCPhysBoneStrictCompatibility.AreEqualExceptRootTransform(
                            sourceEntry.component, component)) return null;
                    copiedProfile.Add(new BoneEntry
                    {
                        component = component,
                        root = root,
                        parent = parent,
                        endpointMissing = NeedsEndpoint(root, component)
                    });
                }
                profiles.Add(copiedProfile);
            }
            if (profiles.Count == 0 || profiles[0].Select(x => x.root).Distinct().Count() < 2) return null;
            var result = new MergeGroup
            {
                parent = parent,
                profiles = profiles,
                entries = profiles.SelectMany(x => x).Distinct().ToList(),
                targetName = sourceGroup.targetName
            };
            CheckGroup(result, copyRoot, copyRoot.GetComponentsInChildren<VRCPhysBone>(true), animationSafety);
            return result;
        }

        private static void MergeGroupOnCopiedAvatar(MergeGroup group)
        {
            List<List<BoneEntry>> profiles = group.profiles.Count > 0
                ? group.profiles
                : new List<List<BoneEntry>> { group.entries };
            List<Transform> roots = group.Roots;
            GameObject mergedObject = new GameObject(group.targetName);
            mergedObject.transform.SetParent(group.parent, false);
            mergedObject.transform.localPosition = Vector3.zero;
            mergedObject.transform.localRotation = Quaternion.identity;
            mergedObject.transform.localScale = Vector3.one;
            foreach (Transform root in roots) root.SetParent(mergedObject.transform, true);
            var sourceComponents = new HashSet<VRCPhysBone>(group.entries.Select(x => x.component));
            foreach (VRCPhysBone physBone in group.parent.root.GetComponentsInChildren<VRCPhysBone>(true))
            {
                if (physBone == null || sourceComponents.Contains(physBone) || physBone.ignoreTransforms == null) continue;
                if (!physBone.ignoreTransforms.Any(roots.Contains)) continue;
                physBone.ignoreTransforms.RemoveAll(roots.Contains);
                if (!physBone.ignoreTransforms.Contains(mergedObject.transform))
                    physBone.ignoreTransforms.Add(mergedObject.transform);
            }
            foreach (List<BoneEntry> profile in profiles)
            {
                VRCPhysBone merged = mergedObject.AddComponent<VRCPhysBone>();
                EditorUtility.CopySerialized(profile[0].component, merged);
                NdmfBoneMergePass.ApplyMergedCurveCorrection(merged,
                    profile.Select(x => x.component), profile.Select(x => x.root));
                NdmfBoneMergePass.ApplyMergedIgnoreTransforms(merged,
                    profile.Select(x => x.component), profile.Select(x => x.root));
                ClearRootTransform(merged);
                SetEnumByName(merged, "multiChildType", "Ignore");
            }
            foreach (BoneEntry entry in group.entries) if (entry.component != null) DestroyImmediate(entry.component);
        }

        private void FocusGroup(MergeGroup group)
        {
            if (group == null) return;
            Selection.objects = group.Roots.Select(x => x.gameObject).ToArray();
            SceneView scene = SceneView.lastActiveSceneView;
            if (scene != null) scene.FrameSelected();
            Repaint();
        }

        private static Transform GetRoot(VRCPhysBone component)
        {
            if (component == null) return null;
            SerializedObject so = new SerializedObject(component);
            SerializedProperty root = so.FindProperty("rootTransform");
            return root != null && root.objectReferenceValue != null ? (Transform)root.objectReferenceValue : component.transform;
        }

        private static bool IsInsideAvatar(Transform avatarRoot, Transform target)
        {
            return avatarRoot != null && target != null && (target == avatarRoot || target.IsChildOf(avatarRoot));
        }

        private static bool NeedsEndpoint(Transform root, VRCPhysBone component)
        {
            SerializedObject so = new SerializedObject(component);
            SerializedProperty endpoint = so.FindProperty("endpointPosition");
            if (endpoint != null && endpoint.vector3Value != Vector3.zero) return false;
            var ignored = new HashSet<Transform>(component.ignoreTransforms ?? new List<Transform>());
            List<Transform> children = Enumerable.Range(0, root.childCount)
                .Select(root.GetChild)
                .Where(child => !ignored.Contains(child))
                .ToList();
            if (children.Count == 0) return true;
            if (children.Count == 1) return false;
            return children.All(child => Enumerable.Range(0, child.childCount)
                .Select(child.GetChild)
                .All(ignored.Contains));
        }

        private static void ClearRootTransform(VRCPhysBone component)
        {
            SerializedObject so = new SerializedObject(component);
            SerializedProperty root = so.FindProperty("rootTransform");
            if (root != null) root.objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEnumByName(VRCPhysBone component, string propertyName, string enumName)
        {
            SerializedObject so = new SerializedObject(component);
            SerializedProperty property = so.FindProperty(propertyName);
            if (property == null || !property.hasMultipleDifferentValues)
            {
                if (property != null)
                {
                    int index = Array.IndexOf(property.enumNames, enumName);
                    if (index >= 0) property.enumValueIndex = index;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static int CountChainNodes(Transform root)
        {
            if (root == null) return 0;
            int count = 1;
            for (int i = 0; i < root.childCount; i++) count += CountChainNodes(root.GetChild(i));
            return count;
        }

        private static string GetPath(Transform transform)
        {
            if (transform == null) return "<null>";
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null) return string.Empty;
            if (target == root) return string.Empty;
            string path = target.name;
            Transform current = target.parent;
            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return current == root ? path : string.Empty;
        }

        private static Transform FindByPath(Transform root, string relativePath)
        {
            if (root == null) return null;
            if (string.IsNullOrEmpty(relativePath)) return root;
            string[] parts = relativePath.Split('/');
            Transform current = root;
            foreach (string part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                current = current.Find(part);
                if (current == null) return null;
            }
            return current;
        }
    }
}
