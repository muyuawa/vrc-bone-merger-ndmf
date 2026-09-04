using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using nadena.dev.ndmf.ui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Dynamics.PhysBone.Components;

[assembly: ExportsPlugin(typeof(VRCBoneMerger.VRCBoneMergerPlugin))]

namespace VRCBoneMerger
{
    /// <summary>
    /// NDMF plugin entry point. All hierarchy edits happen on NDMF's temporary build clone.
    /// </summary>
    public sealed class VRCBoneMergerPlugin : Plugin<VRCBoneMergerPlugin>
    {
        public override string QualifiedName => "com.local.vrc-bone-merger";
        public override string DisplayName => "VRC Bone Merger (NDMF)";

        protected override void Configure()
        {
            // AAO performs its PhysBone work in Optimizing. Ordering in an earlier
            // phase cannot be "after AAO", so this pass deliberately shares AAO's
            // phase and declares the real plugin ordering constraint.
            var optimizing = InPhase(BuildPhase.Optimizing)
                .AfterPlugin("nadena.dev.modular-avatar")
                .AfterPlugin("com.anatawa12.avatar-optimizer");

            optimizing.WithRequiredExtension(typeof(AnimatorServicesContext), sequence =>
            {
                sequence.Run(NdmfBoneMergePass.Instance);
            });
        }
    }

    internal sealed class NdmfBoneMergePass : Pass<NdmfBoneMergePass>
    {
        private const string SettingsHolderName = "VRC Bone Merger";
        public override string QualifiedName => "com.local.vrc-bone-merger.merge-physbones";
        public override string DisplayName => "Merge compatible PhysBones";

        protected override void Execute(BuildContext context)
        {
            VRCBoneMergerSettings settings = context.AvatarRootObject.GetComponentInChildren<VRCBoneMergerSettings>(true);
            if (settings == null) return;
            if (!settings.enabled)
            {
                RemoveSettings(settings);
                return;
            }

            if (!settings.autoMerge)
            {
                Debug.Log("[VRC Bone Merger] Automatic merge is disabled; no groups were processed.");
                RemoveSettings(settings);
                return;
            }

            if (!settings.mergeSameParent)
            {
                Debug.LogWarning("[VRC Bone Merger] mergeSameParent is disabled; no groups were processed.");
                RemoveSettings(settings);
                return;
            }

            VRCPhysBone[] components = context.AvatarRootObject.GetComponentsInChildren<VRCPhysBone>(settings.includeInactive);
            int physBonesBefore = context.AvatarRootObject.GetComponentsInChildren<VRCPhysBone>(true).Length;
            VRCBoneMergerAnimationSafety animationSafety =
                VRCBoneMergerAnimationSafety.FromNdmf(context, components);
            Dictionary<Transform, List<BoneEntry>> byParent = new Dictionary<Transform, List<BoneEntry>>();
            foreach (VRCPhysBone component in components)
            {
                Transform root = GetRoot(component);
                if (root == null || root.parent == null || root == context.AvatarRootTransform) continue;
                if (!IsInsideAvatar(context.AvatarRootTransform, root))
                {
                    Debug.LogWarning("[VRC Bone Merger] Skipped PhysBone whose effective root is outside the Avatar: " + GetPath(root));
                    continue;
                }

                Transform parent = root.parent;
                List<BoneEntry> entries;
                if (!byParent.TryGetValue(parent, out entries))
                {
                    entries = new List<BoneEntry>();
                    byParent.Add(parent, entries);
                }

                entries.Add(new BoneEntry
                {
                    component = component,
                    root = root,
                    signature = "strict"
                });
            }

            List<MergePlan> plans = byParent
                .SelectMany(pair => BuildMergePlans(pair.Key, pair.Value))
                .ToList();

            int mergedGroups = 0;
            int mergedSources = 0;
            int mergedOutputs = 0;
            int sharedProfileGroups = 0;
            foreach (MergePlan plan in plans)
            {
                bool usesNumericTolerance = plan.profiles.Any(profile =>
                    UsesNumericTolerance(profile.Select(x => x.component)));
                List<Transform> roots = plan.Roots;
                if (HasOverlappingRoots(roots))
                {
                    plan.outcome = "根节点存在父子重叠";
                    Debug.LogWarning("[VRC Bone Merger] Skipped overlapping PhysBone roots under " + GetPath(plan.parent));
                    continue;
                }

                if (settings.skipMissingEndpoint && plan.entries.Any(x => NeedsEndpoint(x.root, x.component)))
                {
                    plan.outcome = "缺少有效末端的短链";
                    Debug.LogWarning("[VRC Bone Merger] Skipped group containing a short chain without a real end bone or Endpoint Position under " + GetPath(plan.parent));
                    continue;
                }

                if (settings.mergeOnlySelected
                    && (settings.selectedRoots == null || roots.Any(root => !settings.selectedRoots.Contains(root))))
                {
                    plan.outcome = "未被选择";
                    Debug.Log("[VRC Bone Merger] Skipped unselected PhysBone group under " + GetPath(plan.parent));
                    continue;
                }

                ISet<VRCPhysBone> familyComponents = plan.profiles.Count > 1
                    ? new HashSet<VRCPhysBone>(plan.entries.Select(x => x.component))
                    : null;
                bool safe = true;
                foreach (List<BoneEntry> profile in plan.profiles)
                {
                    string safetyReason;
                    if (IsSafeAutomaticMerge(context.AvatarRootTransform, components,
                            profile.Select(x => x.component), animationSafety, out safetyReason,
                            familyComponents)) continue;
                    safe = false;
                    plan.outcome = safetyReason;
                    break;
                }
                if (!safe)
                {
                    Debug.LogWarning("[VRC Bone Merger] Skipped group under " + GetPath(plan.parent) + ": " + plan.outcome);
                    continue;
                }

                if (plan.entries.Any(x => AllowsGrabbing(x.component)))
                {
                    Debug.LogWarning("[VRC Bone Merger] Merging a group with grabbing enabled under "
                                     + GetPath(plan.parent)
                                     + "; independent PhysBone grab states will become one shared state per configuration profile.");
                }

                if (usesNumericTolerance)
                {
                    Debug.LogWarning("[VRC Bone Merger] Merging a group with small numeric differences under "
                                     + GetPath(plan.parent)
                                     + "; the merged PhysBone uses values from the first source component.");
                }

                MergeOnBuildClone(context, plan.parent, plan.profiles, settings.generatedNamePrefix, components);
                plan.merged = true;
                plan.outcome = usesNumericTolerance ? "已合并（使用数值容差）" : "已合并";
                mergedGroups++;
                mergedSources += plan.entries.Count;
                mergedOutputs += plan.profiles.Count;
                if (plan.profiles.Count > 1) sharedProfileGroups++;
            }

            if (mergedGroups > 0)
            {
                context.Extension<AnimatorServicesContext>().ObjectPathRemapper.ClearCache();
            }

            int physBonesAfter = context.AvatarRootObject.GetComponentsInChildren<VRCPhysBone>(true).Length;
            int reduced = Math.Max(0, physBonesBefore - physBonesAfter);
            string summary = string.Format(
                "[VRC Bone Merger] NDMF 合并完成：PhysBone {0} → {1}，共合并 {2} 组、处理 {3} 个来源组件、生成 {4} 个合并组件，实际减少 {5} 个 PhysBone。",
                physBonesBefore, physBonesAfter, mergedGroups, mergedSources, mergedOutputs, reduced);
            if (sharedProfileGroups > 0)
                summary += string.Format(" 其中 {0} 组使用了共享根多配置合并。", sharedProfileGroups);
            string audit = BuildAuditText(settings, physBonesBefore, plans);
            if (!string.IsNullOrEmpty(audit)) summary += "\n" + audit;
            Debug.Log(summary);
            ErrorReport.ReportError(new BoneMergeSummaryReport(summary));

            RemoveSettings(settings);
        }

        private static List<List<BoneEntry>> PartitionStrictlyMatching(IEnumerable<BoneEntry> entries)
        {
            var groups = new List<List<BoneEntry>>();
            foreach (BoneEntry entry in entries)
            {
                List<BoneEntry> group = groups.FirstOrDefault(x => AreAutomaticMergeCompatible(
                    x[0].component, x[0].root, entry.component, entry.root));
                if (group == null)
                {
                    group = new List<BoneEntry>();
                    groups.Add(group);
                }
                group.Add(entry);
            }
            return groups;
        }

        private static List<MergePlan> BuildMergePlans(Transform parent, List<BoneEntry> entries)
        {
            List<List<BoneEntry>> profiles = PartitionStrictlyMatching(entries);
            var result = new List<MergePlan>();
            var consumed = new HashSet<BoneEntry>();
            var rootSequences = new List<RootProfileSequence>();
            foreach (IGrouping<Transform, BoneEntry> rootGroup in entries.GroupBy(x => x.root))
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
                rootSequences.Add(new RootProfileSequence(root, ordered));
            }

            var sequenceFamilies = new List<List<RootProfileSequence>>();
            foreach (RootProfileSequence sequence in rootSequences)
            {
                List<RootProfileSequence> family = sequenceFamilies.FirstOrDefault(candidate =>
                {
                    if (candidate[0].entries.Count != sequence.entries.Count) return false;
                    for (int index = 0; index < sequence.entries.Count; index++)
                    {
                        if (!AreAutomaticMergeCompatible(
                                candidate[0].entries[index].component, candidate[0].entries[index].root,
                                sequence.entries[index].component, sequence.entries[index].root))
                            return false;
                    }
                    return true;
                });
                if (family == null)
                {
                    family = new List<RootProfileSequence>();
                    sequenceFamilies.Add(family);
                }
                family.Add(sequence);
            }

            foreach (List<RootProfileSequence> unsortedFamily in sequenceFamilies.Where(x => x.Count >= 2))
            {
                List<RootProfileSequence> family = unsortedFamily
                    .OrderBy(x => x.root.GetSiblingIndex()).ThenBy(x => x.root.GetInstanceID()).ToList();
                var orderedProfiles = Enumerable.Range(0, family[0].entries.Count)
                    .Select(index => family.Select(x => x.entries[index]).ToList()).ToList();
                var familyRoots = new HashSet<Transform>(family.Select(x => x.root));
                bool hasPartialOverlap = orderedProfiles.Any(profile => entries.Any(entry =>
                    !familyRoots.Contains(entry.root)
                    && AreAutomaticMergeCompatible(
                        profile[0].component, profile[0].root, entry.component, entry.root)));
                if (hasPartialOverlap) continue;
                result.Add(new MergePlan(parent, orderedProfiles));
                foreach (BoneEntry entry in family.SelectMany(x => x.entries)) consumed.Add(entry);
            }

            foreach (List<BoneEntry> profile in profiles)
            {
                List<BoneEntry> remaining = profile.Where(x => !consumed.Contains(x)).ToList();
                if (remaining.Count < 2) continue;
                if (remaining.Select(x => x.root).Distinct().Count() != remaining.Count) continue;
                result.Add(new MergePlan(parent, new List<List<BoneEntry>> { remaining }));
            }
            return result;
        }

        internal static bool AreAutomaticMergeCompatible(VRCPhysBone left, Transform leftRoot,
            VRCPhysBone right, Transform rightRoot)
        {
            if (left == null || right == null || leftRoot == null || rightRoot == null) return false;
            if (!VRCPhysBoneStrictCompatibility.AreEqualExceptRootTransform(left, right))
                return false;

            // A merged PhysBone has only one curve for all of its branches. Keep
            // branches with different effective lengths in separate groups so the
            // curve values sampled by the original bones remain equivalent.
            if (!VRCPhysBoneStrictCompatibility.HasAnyEffectiveCurve(left)) return true;
            return GetBoneChainLength(leftRoot, left) == GetBoneChainLength(rightRoot, right);
        }

        internal static bool UsesNumericTolerance(IEnumerable<VRCPhysBone> sourceComponents)
        {
            VRCPhysBone[] sources = sourceComponents.Where(x => x != null).ToArray();
            if (sources.Length < 2) return false;
            return sources.Skip(1).Any(source =>
                !VRCPhysBoneStrictCompatibility.AreExactlyEqualExceptRootTransform(sources[0], source));
        }

        internal static bool IsSafeAutomaticMerge(Transform avatarRoot, VRCPhysBone[] allComponents,
            IEnumerable<VRCPhysBone> sourceComponents, VRCBoneMergerAnimationSafety animationSafety,
            out string reason, ISet<VRCPhysBone> completeSharedFamily = null)
        {
            List<VRCPhysBone> sources = sourceComponents.Where(x => x != null).ToList();
            List<Transform> roots = sources.Select(GetRoot).ToList();
            reason = string.Empty;
            if (sources.Count < 2 || sources.Count != roots.Count || roots.Any(x => x == null))
            {
                reason = "来源不足或有效根为空";
                return false;
            }

            if (sources.Any(x => !x.enabled))
            {
                reason = "来源 PhysBone 组件已禁用";
                return false;
            }

            if (roots.Select(x => x.parent).Distinct().Count() != 1 || roots[0].parent == null)
            {
                reason = "有效根不是同一个父物体的直系子节点";
                return false;
            }

            if (sources.Skip(1).Any(x =>
                    !VRCPhysBoneStrictCompatibility.AreEqualExceptRootTransform(sources[0], x)))
            {
                reason = "实际生效的 PhysBone 参数不一致";
                return false;
            }

            if (sources.Any(x => !IsEnumNamed(x, "multiChildType", "Ignore")))
            {
                reason = "Multi Child Type 不是 Ignore；合并根要求 Ignore，否则会改变内部多分支行为";
                return false;
            }

            if (sources.Any(x => !string.IsNullOrEmpty(GetString(x, "parameter"))))
            {
                reason = "设置了 PhysBone Parameter；合并会改变输出通道的来源数量";
                return false;
            }

            if (animationSafety != null && sources.Any(animationSafety.HasAnimatedPhysBone))
            {
                reason = "动画控制了 PhysBone 参数或组件启用状态";
                return false;
            }

            if (animationSafety != null && sources.Select((component, index) =>
                    animationSafety.HasAnimatedActiveState(component.gameObject)
                    || animationSafety.HasAnimatedActiveState(roots[index].gameObject)).Any(x => x))
            {
                reason = "动画直接开关了 PhysBone 对象或有效根对象";
                return false;
            }

            if (roots.Any(root =>
            {
                List<VRCPhysBone> controllers = allComponents
                    .Where(pb => pb != null && GetRoot(pb) == root).ToList();
                return controllers.Count != 1
                    && (completeSharedFamily == null || controllers.Any(pb => !completeSharedFamily.Contains(pb)));
            }))
            {
                reason = "至少一个有效根还被本合并组之外的 PhysBone 共用";
                return false;
            }

            int[] depths = sources.Select((component, index) => GetMaxChainDepth(roots[index], component)).ToArray();
            if (VRCPhysBoneStrictCompatibility.HasAnyEffectiveCurve(sources[0])
                && depths.Distinct().Count() != 1)
            {
                reason = "使用了曲线但来源链长度不同，合并后曲线采样比例会改变";
                return false;
            }

            int affectedTransforms = 1 + sources.Select((component, index) =>
                CountAffectedTransforms(roots[index], component)).Sum();
            if (sources.Select((component, index) => CountAffectedTransforms(roots[index], component))
                .Any(count => count >= 100))
            {
                reason = "至少一条来源链影响 100 个或更多 Transform，按 AAO 安全规则不自动合并";
                return false;
            }
            if (affectedTransforms > 128)
            {
                reason = "预计影响 " + affectedTransforms + " 个 Transform，超过 VRChat/AAO 建议的 128 个上限";
                return false;
            }

            if (roots.Any(root => HasHumanoidPathDependency(avatarRoot, root)))
            {
                reason = "有效根位于 Humanoid 骨骼名称依赖路径上";
                return false;
            }

            if (sources.SelectMany((component, index) => EnumerateAffectedTransforms(roots[index], component))
                .Any(HasConstraintComponent))
            {
                reason = "受影响骨骼上存在 Constraint；移动 PhysBone 组件可能改变约束与物理执行顺序";
                return false;
            }

            if (roots.Any(root => !IsInsideAvatar(avatarRoot, root)))
            {
                reason = "有效根位于 Avatar 外部";
                return false;
            }

            return true;
        }

        private static void MergeOnBuildClone(BuildContext context, Transform parent,
            List<List<BoneEntry>> profiles,
            string prefix, VRCPhysBone[] allComponents)
        {
            List<BoneEntry> entries = profiles.SelectMany(x => x).ToList();
            List<Transform> roots = profiles[0].Select(x => x.root).Distinct().ToList();
            string safePrefix = string.IsNullOrEmpty(prefix) ? "__NDMF_MergedPB_" : prefix;
            GameObject mergedObject = new GameObject(MakeUniqueName(parent, safePrefix + parent.name));
            mergedObject.transform.SetParent(parent, false);
            mergedObject.transform.localPosition = Vector3.zero;
            mergedObject.transform.localRotation = Quaternion.identity;
            mergedObject.transform.localScale = Vector3.one;

            foreach (Transform root in roots)
            {
                root.SetParent(mergedObject.transform, true);
            }

            // Preserve existing parent PhysBones that explicitly ignored one or more
            // source roots. They must ignore the generated root after reparenting.
            var sourceRoots = new HashSet<Transform>(roots);
            var sourceComponents = new HashSet<VRCPhysBone>(entries.Select(x => x.component));
            foreach (VRCPhysBone physBone in allComponents)
            {
                if (physBone == null || sourceComponents.Contains(physBone) || physBone.ignoreTransforms == null) continue;
                if (!physBone.ignoreTransforms.Any(sourceRoots.Contains)) continue;
                physBone.ignoreTransforms.RemoveAll(sourceRoots.Contains);
                if (!physBone.ignoreTransforms.Contains(mergedObject.transform))
                    physBone.ignoreTransforms.Add(mergedObject.transform);
            }

            ObjectRegistry.GetReference(mergedObject);
            foreach (List<BoneEntry> profile in profiles)
            {
                VRCPhysBone merged = mergedObject.AddComponent<VRCPhysBone>();
                EditorUtility.CopySerialized(profile[0].component, merged);
                ApplyMergedCurveCorrection(merged,
                    profile.Select(x => x.component), profile.Select(x => x.root));
                ApplyMergedIgnoreTransforms(merged, profile.Select(x => x.component), profile.Select(x => x.root));
                ClearRootTransform(merged);
                SetEnumByName(merged, "multiChildType", "Ignore");
                ObjectRegistry.GetReference(merged);
            }

            foreach (BoneEntry entry in entries)
            {
                if (entry.component != null) UnityEngine.Object.DestroyImmediate(entry.component);
            }
        }

        private static bool HasOverlappingRoots(List<Transform> roots)
        {
            for (int i = 0; i < roots.Count; i++)
            {
                for (int j = i + 1; j < roots.Count; j++)
                {
                    Transform a = roots[i];
                    Transform b = roots[j];
                    if (a.IsChildOf(b) || b.IsChildOf(a)) return true;
                }
            }
            return false;
        }

        private static Transform GetRoot(VRCPhysBone component)
        {
            SerializedObject serialized = new SerializedObject(component);
            SerializedProperty root = serialized.FindProperty("rootTransform");
            return root != null && root.objectReferenceValue != null ? (Transform)root.objectReferenceValue : component.transform;
        }

        private static bool IsInsideAvatar(Transform avatarRoot, Transform target)
        {
            return avatarRoot != null && target != null && (target == avatarRoot || target.IsChildOf(avatarRoot));
        }

        private static bool IsEnumNamed(VRCPhysBone component, string propertyName, string expectedName)
        {
            SerializedObject serialized = new SerializedObject(component);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Enum) return false;
            int index = property.enumValueIndex;
            return index >= 0 && index < property.enumNames.Length && property.enumNames[index] == expectedName;
        }

        internal static bool AllowsGrabbing(VRCPhysBone component)
        {
            return VRCPhysBoneStrictCompatibility.AllowsGrabbing(component);
        }

        private static string GetString(VRCPhysBone component, string propertyName)
        {
            SerializedProperty property = new SerializedObject(component).FindProperty(propertyName);
            return property == null ? string.Empty : property.stringValue;
        }

        private static int CountAffectedTransforms(Transform root, VRCPhysBone component)
        {
            return EnumerateAffectedTransforms(root, component).Count();
        }

        internal static int GetMaxChainDepth(Transform root, VRCPhysBone component)
        {
            var ignored = new HashSet<Transform>(component.ignoreTransforms ?? new List<Transform>());
            return GetDepth(root);

            int GetDepth(Transform node)
            {
                if (node == null || ignored.Contains(node)) return 0;
                int childDepth = 0;
                for (int i = 0; i < node.childCount; i++)
                    childDepth = Math.Max(childDepth, GetDepth(node.GetChild(i)));
                return childDepth + 1;
            }
        }

        private static int GetBoneChainLength(Transform root, VRCPhysBone component)
        {
            int maxBoneChainIndex = Math.Max(0, GetMaxChainDepth(root, component) - 1);
            if (component != null && component.endpointPosition != Vector3.zero) maxBoneChainIndex++;
            return maxBoneChainIndex;
        }

        internal static void ApplyMergedCurveCorrection(VRCPhysBone merged,
            IEnumerable<VRCPhysBone> sourceComponents, IEnumerable<Transform> sourceRoots)
        {
            if (merged == null || sourceComponents == null || sourceRoots == null) return;
            VRCPhysBone[] components = sourceComponents.ToArray();
            Transform[] roots = sourceRoots.ToArray();
            int count = Math.Min(components.Length, roots.Length);
            if (count == 0) return;

            int maxChainLength = Enumerable.Range(0, count)
                .Where(index => components[index] != null && roots[index] != null)
                .Select(index => GetBoneChainLength(roots[index], components[index]))
                .DefaultIfEmpty(0)
                .Max();
            int boneCurveLength = maxChainLength - 1;

            merged.pullCurve = FixCurveForMergedRoot(merged.pullCurve, boneCurveLength);
            merged.springCurve = FixCurveForMergedRoot(merged.springCurve, boneCurveLength);
            merged.stiffnessCurve = FixCurveForMergedRoot(merged.stiffnessCurve, boneCurveLength);
            merged.gravityCurve = FixCurveForMergedRoot(merged.gravityCurve, boneCurveLength);
            merged.gravityFalloffCurve = FixCurveForMergedRoot(merged.gravityFalloffCurve, boneCurveLength);
            merged.immobileCurve = FixCurveForMergedRoot(merged.immobileCurve, boneCurveLength);
            merged.maxAngleXCurve = FixCurveForMergedRoot(merged.maxAngleXCurve, boneCurveLength);
            merged.maxAngleZCurve = FixCurveForMergedRoot(merged.maxAngleZCurve, boneCurveLength);
            merged.limitRotationXCurve = FixCurveForMergedRoot(merged.limitRotationXCurve, boneCurveLength);
            merged.limitRotationYCurve = FixCurveForMergedRoot(merged.limitRotationYCurve, boneCurveLength);
            merged.limitRotationZCurve = FixCurveForMergedRoot(merged.limitRotationZCurve, boneCurveLength);
            merged.radiusCurve = FixCurveForMergedRoot(merged.radiusCurve, maxChainLength);
            merged.stretchMotionCurve = FixCurveForMergedRoot(merged.stretchMotionCurve, boneCurveLength);
            merged.maxStretchCurve = FixCurveForMergedRoot(merged.maxStretchCurve, boneCurveLength);
            merged.maxSquishCurve = FixCurveForMergedRoot(merged.maxSquishCurve, boneCurveLength);
        }

        private static AnimationCurve FixCurveForMergedRoot(AnimationCurve curve, int chainLength)
        {
            if (curve == null || curve.length == 0) return new AnimationCurve();
            if (chainLength <= 0)
            {
                float value = curve.Evaluate(0f);
                return AnimationCurve.Constant(0f, 1f, value);
            }

            float offset = 1f / (chainLength + 1f);
            float tangentRatio = (chainLength + 1f) / chainLength;
            Keyframe[] keys = curve.keys;
            for (int index = 0; index < keys.Length; index++)
            {
                Keyframe key = keys[index];
                key.time = Mathf.LerpUnclamped(offset, 1f, key.time);
                key.inTangent *= tangentRatio;
                key.outTangent *= tangentRatio;
                keys[index] = key;
            }

            return new AnimationCurve(keys)
            {
                preWrapMode = curve.preWrapMode,
                postWrapMode = curve.postWrapMode
            };
        }

        private static IEnumerable<Transform> EnumerateAffectedTransforms(Transform root, VRCPhysBone component)
        {
            var ignored = new HashSet<Transform>(component.ignoreTransforms ?? new List<Transform>());
            var stack = new Stack<Transform>();
            if (root != null) stack.Push(root);
            while (stack.Count > 0)
            {
                Transform current = stack.Pop();
                if (current == null || ignored.Contains(current)) continue;
                yield return current;
                for (int i = 0; i < current.childCount; i++) stack.Push(current.GetChild(i));
            }
        }

        private static bool HasConstraintComponent(Transform transform)
        {
            foreach (Component component in transform.GetComponents<Component>())
            {
                if (component == null) continue;
                Type type = component.GetType();
                if (typeof(UnityEngine.Animations.IConstraint).IsAssignableFrom(type)) return true;
                if ((type.Namespace ?? string.Empty).StartsWith("VRC.SDK3.Dynamics.Constraint", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool HasHumanoidPathDependency(Transform avatarRoot, Transform candidate)
        {
            if (avatarRoot == null || candidate == null) return false;
            // Only the Animator on the avatar descriptor root defines the avatar's
            // Humanoid skeleton. Outfit/accessory prefabs can contain their own
            // Humanoid Animator; treating those as the avatar skeleton creates
            // false positives for otherwise independent clothing PhysBone roots.
            Animator animator = avatarRoot.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return false;
            for (HumanBodyBones bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
            {
                Transform mapped;
                try
                {
                    mapped = animator.GetBoneTransform(bone);
                }
                catch (InvalidOperationException)
                {
                    continue;
                }
                if (mapped != null && (mapped == candidate || mapped.IsChildOf(candidate))) return true;
            }
            return false;
        }

        internal static void ApplyMergedIgnoreTransforms(VRCPhysBone merged,
            IEnumerable<VRCPhysBone> sourceComponents, IEnumerable<Transform> sourceRoots)
        {
            if (merged == null) return;
            VRCPhysBone[] components = sourceComponents.ToArray();
            Transform[] roots = sourceRoots.ToArray();
            var ignores = new HashSet<Transform>();
            for (int i = 0; i < Math.Min(components.Length, roots.Length); i++)
            {
                VRCPhysBone component = components[i];
                Transform root = roots[i];
                if (component == null || root == null || component.ignoreTransforms == null) continue;
                foreach (Transform ignored in component.ignoreTransforms)
                {
                    if (ignored != null && (ignored == root || ignored.IsChildOf(root))) ignores.Add(ignored);
                }
            }

            SerializedObject serialized = new SerializedObject(merged);
            SerializedProperty property = serialized.FindProperty("ignoreTransforms");
            if (property == null || !property.isArray) return;
            Transform[] ordered = ignores.OrderBy(GetPath).ToArray();
            property.arraySize = ordered.Length;
            for (int i = 0; i < ordered.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = ordered[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool NeedsEndpoint(Transform root, VRCPhysBone component)
        {
            SerializedObject serialized = new SerializedObject(component);
            SerializedProperty endpoint = serialized.FindProperty("endpointPosition");
            if (endpoint != null && endpoint.vector3Value != Vector3.zero) return false;
            if (root == null) return true;
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

        private static string MakeUniqueName(Transform parent, string baseName)
        {
            string candidate = baseName;
            int suffix = 1;
            while (parent.Find(candidate) != null)
            {
                candidate = baseName + "_" + suffix++;
            }
            return candidate;
        }

        private static string EnumText(SerializedObject serialized, string name)
        {
            SerializedProperty property = serialized.FindProperty(name);
            return property == null ? name + "=?" : name + "=" + property.enumDisplayNames[property.enumValueIndex];
        }

        private static string NumberText(SerializedObject serialized, string name)
        {
            SerializedProperty property = serialized.FindProperty(name);
            return property == null ? name + "=?" : name + "=" + property.floatValue.ToString("0.###");
        }

        private static string BoolText(SerializedObject serialized, string name)
        {
            SerializedProperty property = serialized.FindProperty(name);
            return property == null ? name + "=?" : name + "=" + (property.boolValue ? "True" : "False");
        }

        private static string VectorText(SerializedObject serialized, string name)
        {
            SerializedProperty property = serialized.FindProperty(name);
            return property == null ? name + "=?" : name + "=" + property.vector3Value.ToString("F2");
        }

        private static void ClearRootTransform(VRCPhysBone component)
        {
            SerializedObject serialized = new SerializedObject(component);
            SerializedProperty root = serialized.FindProperty("rootTransform");
            if (root != null) root.objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEnumByName(VRCPhysBone component, string propertyName, string enumName)
        {
            SerializedObject serialized = new SerializedObject(component);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property != null)
            {
                int index = Array.IndexOf(property.enumNames, enumName);
                if (index >= 0) property.enumValueIndex = index;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string BuildAuditText(VRCBoneMergerSettings settings, int physBonesBefore,
            List<MergePlan> plans)
        {
            if (settings.recordedPlan == null || settings.recordedPlan.Count == 0) return string.Empty;

            var builder = new StringBuilder();
            builder.AppendFormat("保存扫描：{0} 个 PhysBone、计划减少 {1} 个；进入本插件时：{2} 个。",
                settings.recordedScanPhysBoneCount, settings.recordedPredictedReduction, physBonesBefore);
            int changed = settings.recordedScanPhysBoneCount - physBonesBefore;
            if (changed > 0)
                builder.AppendFormat(" 上游 Modular Avatar / AAO 已先减少 {0} 个。", changed);
            else if (changed < 0)
                builder.AppendFormat(" 上游 Modular Avatar / AAO 处理后增加 {0} 个。", -changed);

            int shown = 0;
            foreach (VRCBoneMergerRecordedGroup recorded in settings.recordedPlan)
            {
                if (recorded == null || recorded.roots == null) continue;
                List<Transform> liveRoots = recorded.roots.Where(x => x != null).Distinct().ToList();
                MergePlan match = plans.FirstOrDefault(plan => plan.profiles.Count == Math.Max(1, recorded.profileCount)
                    && new HashSet<Transform>(plan.Roots).SetEquals(liveRoots));
                string outcome;
                if (liveRoots.Count != recorded.roots.Count)
                    outcome = "上游处理后部分来源已不存在";
                else if (match == null)
                    outcome = "进入本插件时已不再构成完整匹配组";
                else
                    outcome = match.outcome;

                if (outcome == "已合并") continue;
                if (++shown > 20) continue;
                builder.AppendFormat("\n未合并计划：{0}（{1} 根 × {2} 配置，预计 -{3}）：{4}。",
                    string.IsNullOrEmpty(recorded.parentPath) ? "<未知父级>" : recorded.parentPath,
                    recorded.roots.Count, Math.Max(1, recorded.profileCount),
                    recorded.predictedReduction, string.IsNullOrEmpty(outcome) ? "未形成候选" : outcome);
            }
            if (shown > 20) builder.AppendFormat("\n另有 {0} 条未合并计划未展开。", shown - 20);
            return builder.ToString();
        }

        private static void RemoveSettings(VRCBoneMergerSettings settings)
        {
            if (settings == null) return;

            GameObject settingsObject = settings.gameObject;
            Component[] components = settingsObject.GetComponents<Component>();
            if (settingsObject.name == SettingsHolderName
                && components.Length == 2
                && settingsObject.transform.childCount == 0)
            {
                UnityEngine.Object.DestroyImmediate(settingsObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        private static string GetPath(Transform transform)
        {
            if (transform == null) return "<null>";
            string path = transform.name;
            Transform current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        private sealed class BoneEntry
        {
            public VRCPhysBone component;
            public Transform root;
            public string signature;
        }

        private sealed class MergePlan
        {
            public readonly Transform parent;
            public readonly List<List<BoneEntry>> profiles;
            public readonly List<BoneEntry> entries;
            public bool merged;
            public string outcome = "未通过构建时检查";

            public MergePlan(Transform parent, List<List<BoneEntry>> profiles)
            {
                this.parent = parent;
                this.profiles = profiles;
                entries = profiles.SelectMany(x => x).Distinct().ToList();
            }

            public List<Transform> Roots => profiles.Count == 0
                ? new List<Transform>()
                : profiles[0].Select(x => x.root).Distinct().ToList();
        }

        private sealed class RootProfileSequence
        {
            public readonly Transform root;
            public readonly List<BoneEntry> entries;

            public RootProfileSequence(Transform root, List<BoneEntry> entries)
            {
                this.root = root;
                this.entries = entries;
            }
        }

        private sealed class BoneMergeSummaryReport : IError
        {
            private readonly string message;

            public BoneMergeSummaryReport(string message)
            {
                this.message = message;
            }

            public ErrorSeverity Severity => ErrorSeverity.Information;

            public VisualElement CreateVisualElement(ErrorReport report)
            {
                return new Label(message);
            }

            public string ToMessage()
            {
                return message;
            }

            public void AddReference(ObjectReference obj)
            {
            }
        }
    }
}
