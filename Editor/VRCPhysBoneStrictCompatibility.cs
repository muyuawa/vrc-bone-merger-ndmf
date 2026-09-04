using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace VRCBoneMerger
{
    /// <summary>
    /// Compares the effective runtime configuration of two PhysBones. Values which
    /// cannot affect simulation are normalized in the same way as AAO's compatible
    /// PhysBone merger. rootTransform and per-branch ignoreTransforms are handled by
    /// the hierarchy merger and are intentionally excluded here.
    /// </summary>
    internal static class VRCPhysBoneStrictCompatibility
    {
        internal const float NumericRelativeTolerance = 0.12f;
        private const float MinimumAbsoluteTolerance = 0.0001f;

        private struct EffectivePermission : IEquatable<EffectivePermission>
        {
            public bool self;
            public bool others;

            public bool Equals(EffectivePermission other)
            {
                return self == other.self && others == other.others;
            }
        }

        public static bool AreEqualExceptRootTransform(VRCPhysBone left, VRCPhysBone right)
        {
            string ignored;
            return AreEqualExceptRootTransform(left, right, out ignored);
        }

        public static bool AreEqualExceptRootTransform(VRCPhysBone left, VRCPhysBone right,
            out string mismatchPath)
        {
            return AreEqualExceptRootTransform(left, right, out mismatchPath, NumericRelativeTolerance);
        }

        internal static bool AreExactlyEqualExceptRootTransform(VRCPhysBone left, VRCPhysBone right)
        {
            string ignored;
            return AreEqualExceptRootTransform(left, right, out ignored, 0f);
        }

        private static bool AreEqualExceptRootTransform(VRCPhysBone left, VRCPhysBone right,
            out string mismatchPath, float relativeTolerance)
        {
            mismatchPath = string.Empty;
            if (left == null || right == null)
            {
                mismatchPath = "<null>";
                return false;
            }

            if (ReferenceEquals(left, right)) return true;

            if (!Equal(left.version, right.version, "version", ref mismatchPath)) return false;
            if (!Equal(left.ignoreOtherPhysBones, right.ignoreOtherPhysBones, "ignoreOtherPhysBones", ref mismatchPath)) return false;
            if (!EqualNumeric(left.endpointPosition, right.endpointPosition, "endpointPosition", relativeTolerance, ref mismatchPath)) return false;
            if (!Equal(left.multiChildType, right.multiChildType, "multiChildType", ref mismatchPath)) return false;

            if (!Equal(left.integrationType, right.integrationType, "integrationType", ref mismatchPath)) return false;
            if (!EqualCurveValue(left.pull, left.pullCurve, right.pull, right.pullCurve, "pull", relativeTolerance, ref mismatchPath)) return false;
            if (!EqualCurveValue(left.spring, left.springCurve, right.spring, right.springCurve, "spring", relativeTolerance, ref mismatchPath)) return false;
            if (!EqualCurveValue(left.stiffness, left.stiffnessCurve, right.stiffness, right.stiffnessCurve, "stiffness", relativeTolerance, ref mismatchPath)) return false;
            if (!EqualCurveValue(left.gravity, left.gravityCurve, right.gravity, right.gravityCurve, "gravity", relativeTolerance, ref mismatchPath)) return false;

            // Gravity Falloff has no runtime effect while Gravity is zero.
            if (left.gravity != 0f && right.gravity != 0f
                && !EqualCurveValue(left.gravityFalloff, left.gravityFalloffCurve,
                    right.gravityFalloff, right.gravityFalloffCurve, "gravityFalloff", relativeTolerance, ref mismatchPath))
                return false;

            if (!Equal(left.immobileType, right.immobileType, "immobileType", ref mismatchPath)) return false;
            if (!EqualCurveValue(left.immobile, left.immobileCurve, right.immobile, right.immobileCurve,
                    "immobile", relativeTolerance, ref mismatchPath)) return false;

            if (!Equal(left.limitType, right.limitType, "limitType", ref mismatchPath)) return false;
            if (!EqualCurveValue(left.maxAngleX, left.maxAngleXCurve, right.maxAngleX, right.maxAngleXCurve,
                    "maxAngleX", relativeTolerance, ref mismatchPath)) return false;
            if (!EqualCurveValue(left.maxAngleZ, left.maxAngleZCurve, right.maxAngleZ, right.maxAngleZCurve,
                    "maxAngleZ", relativeTolerance, ref mismatchPath)) return false;
            if (!EqualNumeric(left.limitRotation, right.limitRotation, "limitRotation", relativeTolerance, ref mismatchPath)) return false;
            if (!EqualNormalizedCurve(left.limitRotationXCurve, left.limitRotation.x,
                    right.limitRotationXCurve, right.limitRotation.x, "limitRotationXCurve", relativeTolerance, ref mismatchPath)) return false;
            if (!EqualNormalizedCurve(left.limitRotationYCurve, left.limitRotation.y,
                    right.limitRotationYCurve, right.limitRotation.y, "limitRotationYCurve", relativeTolerance, ref mismatchPath)) return false;
            if (!EqualNormalizedCurve(left.limitRotationZCurve, left.limitRotation.z,
                    right.limitRotationZCurve, right.limitRotation.z, "limitRotationZCurve", relativeTolerance, ref mismatchPath)) return false;

            if (!EqualCurveValue(left.radius, left.radiusCurve, right.radius, right.radiusCurve,
                    "radius", relativeTolerance, ref mismatchPath)) return false;
            if (!Equal(Permission(left.allowCollision, left.collisionFilter),
                    Permission(right.allowCollision, right.collisionFilter), "allowCollision", ref mismatchPath)) return false;
            if (!SetEquals(left.colliders, right.colliders))
            {
                mismatchPath = "colliders";
                return false;
            }

            if (!EqualCurveValue(left.stretchMotion, left.stretchMotionCurve,
                    right.stretchMotion, right.stretchMotionCurve, "stretchMotion", relativeTolerance, ref mismatchPath)) return false;
            if (!EqualCurveValue(left.maxStretch, left.maxStretchCurve,
                    right.maxStretch, right.maxStretchCurve, "maxStretch", relativeTolerance, ref mismatchPath)) return false;
            if (!EqualCurveValue(left.maxSquish, left.maxSquishCurve,
                    right.maxSquish, right.maxSquishCurve, "maxSquish", relativeTolerance, ref mismatchPath)) return false;

            if (!Equal(Permission(left.allowGrabbing, left.grabFilter),
                    Permission(right.allowGrabbing, right.grabFilter), "allowGrabbing", ref mismatchPath)) return false;
            if (!Equal(Permission(left.allowPosing, left.poseFilter),
                    Permission(right.allowPosing, right.poseFilter), "allowPosing", ref mismatchPath)) return false;
            if (!Equal(left.snapToHand, right.snapToHand, "snapToHand", ref mismatchPath)) return false;
            if (!EqualNumeric(left.grabMovement, right.grabMovement, "grabMovement", relativeTolerance, ref mismatchPath)) return false;

            if (!Equal(left.isAnimated, right.isAnimated, "isAnimated", ref mismatchPath)) return false;
            if (!Equal(left.resetWhenDisabled, right.resetWhenDisabled, "resetWhenDisabled", ref mismatchPath)) return false;
            if (!Equal(left.parameter ?? string.Empty, right.parameter ?? string.Empty,
                    "parameter", ref mismatchPath)) return false;

            return true;
        }

        internal static bool HasAnyEffectiveCurve(VRCPhysBone component)
        {
            if (component == null) return false;
            return IsEffective(component.pullCurve, component.pull)
                   || IsEffective(component.springCurve, component.spring)
                   || IsEffective(component.stiffnessCurve, component.stiffness)
                   || IsEffective(component.gravityCurve, component.gravity)
                   || (component.gravity != 0f && IsEffective(component.gravityFalloffCurve, component.gravityFalloff))
                   || IsEffective(component.immobileCurve, component.immobile)
                   || IsEffective(component.maxAngleXCurve, component.maxAngleX)
                   || IsEffective(component.maxAngleZCurve, component.maxAngleZ)
                   || IsEffective(component.limitRotationXCurve, component.limitRotation.x)
                   || IsEffective(component.limitRotationYCurve, component.limitRotation.y)
                   || IsEffective(component.limitRotationZCurve, component.limitRotation.z)
                   || IsEffective(component.radiusCurve, component.radius)
                   || IsEffective(component.stretchMotionCurve, component.stretchMotion)
                   || IsEffective(component.maxStretchCurve, component.maxStretch)
                   || IsEffective(component.maxSquishCurve, component.maxSquish);
        }

        internal static bool AllowsGrabbing(VRCPhysBone component)
        {
            if (component == null) return false;
            EffectivePermission permission = Permission(component.allowGrabbing, component.grabFilter);
            return permission.self || permission.others;
        }

        private static EffectivePermission Permission(VRCPhysBoneBase.AdvancedBool allow,
            VRCPhysBoneBase.PermissionFilter filter)
        {
            switch (allow)
            {
                case VRCPhysBoneBase.AdvancedBool.False:
                    return new EffectivePermission { self = false, others = false };
                case VRCPhysBoneBase.AdvancedBool.True:
                    return new EffectivePermission { self = true, others = true };
                case VRCPhysBoneBase.AdvancedBool.Other:
                    return new EffectivePermission { self = filter.allowSelf, others = filter.allowOthers };
                default:
                    return new EffectivePermission { self = false, others = false };
            }
        }

        private static bool EqualCurveValue(float leftValue, AnimationCurve leftCurve,
            float rightValue, AnimationCurve rightCurve, string name, float relativeTolerance,
            ref string mismatchPath)
        {
            if (!NearlyEqual(leftValue, rightValue, relativeTolerance))
            {
                mismatchPath = name;
                return false;
            }

            return EqualNormalizedCurve(leftCurve, leftValue, rightCurve, rightValue,
                name + "Curve", relativeTolerance, ref mismatchPath);
        }

        private static bool EqualNormalizedCurve(AnimationCurve leftCurve, float leftValue,
            AnimationCurve rightCurve, float rightValue, string name, float relativeTolerance,
            ref string mismatchPath)
        {
            AnimationCurve normalizedLeft = NormalizeCurve(leftCurve, leftValue);
            AnimationCurve normalizedRight = NormalizeCurve(rightCurve, rightValue);
            if (CurvesEqual(normalizedLeft, normalizedRight, relativeTolerance)) return true;
            mismatchPath = name;
            return false;
        }

        private static AnimationCurve NormalizeCurve(AnimationCurve curve, float value)
        {
            return IsEffective(curve, value) ? curve : null;
        }

        private static bool IsEffective(AnimationCurve curve, float value)
        {
            return value != 0f && curve != null && curve.length > 0;
        }

        private static bool CurvesEqual(AnimationCurve left, AnimationCurve right, float relativeTolerance)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;
            if (left.preWrapMode != right.preWrapMode || left.postWrapMode != right.postWrapMode
                || left.length != right.length) return false;

            Keyframe[] leftKeys = left.keys;
            Keyframe[] rightKeys = right.keys;
            for (int i = 0; i < leftKeys.Length; i++)
            {
                Keyframe a = leftKeys[i];
                Keyframe b = rightKeys[i];
                if (!NearlyEqual(a.time, b.time, relativeTolerance)
                    || !NearlyEqual(a.value, b.value, relativeTolerance)
                    || !NearlyEqual(a.inTangent, b.inTangent, relativeTolerance)
                    || !NearlyEqual(a.outTangent, b.outTangent, relativeTolerance)
                    || !NearlyEqual(a.inWeight, b.inWeight, relativeTolerance)
                    || !NearlyEqual(a.outWeight, b.outWeight, relativeTolerance)
                    || a.weightedMode != b.weightedMode) return false;
            }
            return true;
        }

        private static bool EqualNumeric(Vector3 left, Vector3 right, string name,
            float relativeTolerance, ref string mismatchPath)
        {
            if (NearlyEqual(left.x, right.x, relativeTolerance)
                && NearlyEqual(left.y, right.y, relativeTolerance)
                && NearlyEqual(left.z, right.z, relativeTolerance)) return true;
            mismatchPath = name;
            return false;
        }

        private static bool EqualNumeric(float left, float right, string name,
            float relativeTolerance, ref string mismatchPath)
        {
            if (NearlyEqual(left, right, relativeTolerance)) return true;
            mismatchPath = name;
            return false;
        }

        private static bool NearlyEqual(float left, float right, float relativeTolerance)
        {
            if (left.Equals(right)) return true;
            if (relativeTolerance <= 0f) return false;
            if (float.IsNaN(left) || float.IsNaN(right)
                || float.IsInfinity(left) || float.IsInfinity(right)) return false;
            float scale = Mathf.Max(Mathf.Abs(left), Mathf.Abs(right));
            float tolerance = Mathf.Max(MinimumAbsoluteTolerance,
                scale * Mathf.Max(0f, relativeTolerance));
            return Mathf.Abs(left - right) <= tolerance;
        }

        private static bool SetEquals<T>(IEnumerable<T> left, IEnumerable<T> right)
        {
            return new HashSet<T>(left ?? Enumerable.Empty<T>())
                .SetEquals(right ?? Enumerable.Empty<T>());
        }

        private static bool Equal<T>(T left, T right, string name, ref string mismatchPath)
        {
            if (EqualityComparer<T>.Default.Equals(left, right)) return true;
            mismatchPath = name;
            return false;
        }
    }
}
