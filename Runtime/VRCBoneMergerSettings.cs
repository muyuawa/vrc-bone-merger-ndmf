using System.Collections.Generic;
using UnityEngine;
using nadena.dev.ndmf;

namespace VRCBoneMerger
{
    [System.Serializable]
    public sealed class VRCBoneMergerRecordedGroup
    {
        public string parentPath;
        public int profileCount = 1;
        public int predictedReduction;
        public List<Transform> roots = new List<Transform>();
    }

    /// <summary>
    /// NDMF build instruction. This component is intentionally editor-only and is removed from the built avatar.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("NDMF/VRC Bone Merger")]
    public sealed class VRCBoneMergerSettings : MonoBehaviour, INDMFEditorOnly
    {
        public enum ParameterPolicy
        {
            OnlyMatching,
            UseFirstComponent
        }

        [Tooltip("Merge PhysBones whose roots are direct children of the same parent.")]
        public bool mergeSameParent = true;

        [Tooltip("Run the merger during the NDMF build. Disable this to keep the instruction component without changing PhysBones.")]
        public bool autoMerge = true;

        [Tooltip("If enabled, disabled objects are also scanned during the build.")]
        public bool includeInactive = true;

        [HideInInspector]
        [Tooltip("AAO-style compatibility: effective PhysBone behavior must match except rootTransform.")]
        public ParameterPolicy parameterPolicy = ParameterPolicy.OnlyMatching;

        [Tooltip("When enabled, only the roots saved by the Tools inspector are merged.")]
        public bool mergeOnlySelected = false;

        [HideInInspector]
        public List<Transform> selectedRoots = new List<Transform>();

        [HideInInspector]
        public int recordedScanPhysBoneCount;

        [HideInInspector]
        public int recordedPredictedReduction;

        [HideInInspector]
        public List<VRCBoneMergerRecordedGroup> recordedPlan = new List<VRCBoneMergerRecordedGroup>();

        [Tooltip("Skip short chains that have neither a usable real end bone nor Endpoint Position.")]
        public bool skipMissingEndpoint = true;

        [Tooltip("Name prefix for generated merged roots.")]
        public string generatedNamePrefix = "__NDMF_MergedPB_";

        [HideInInspector]
        [Tooltip("Legacy field. The build-copy instruction is always removed after processing.")]
        public bool removeAfterBuild = true;
    }
}
