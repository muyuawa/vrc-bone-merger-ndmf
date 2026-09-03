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
            mismatchPath = string.Empty;
            if (left == null || right == null)
            {
                mismatchPath = "<null>";
                return false;
            }

            if (ReferenceEquals(left, right)) return true;

            if (!Equal(left.version, right.version, "version", ref mismatchPath)) return false;
            if (!Equal(left.ignoreOtherPhysBones, right.ignoreOtherPhysBones, "ignoreOtherPhysBones", ref mismatchPath)) return false;
            if (!Equal(left.endpointPosition, right.endpointPosition, "endpointPosition", ref mismatchPath)) return false;
            if (!Equal(left.multiChildType, right.multiChildType, "multiChildType", ref mismatchPath)) return false;

            if (!Equal(left.integrationType, right.integrationType, "integrationType", ref mismatchPath)) return false;
            if (!EqualCurveValue(left.pull, left.pullCurve, right.pull, right.pullCurve, "pull", ref mismatchPath)) return false;
            if (!EqualCurveValue(left.spring, left.springCurve, right.spring, right.springCurve, "spring", ref mismatchPath)) return false;
            if (!EqualCurveValue(left.stiffness, left.stiffnessCurve, right.stiffness, right.stiffnessCurve, "stiffness", ref mismatchPath)) return false;
            if (!EqualCurveValue(left.gravity, left.gravityCurve, right.gravity, right.gravityCurve, "gravity", ref mismatchPath)) return false;

            // Gravity Falloff has no runtime effect while Gravity is zero.
            if (left.gravity != 0f && right.gravity != 0f
                && !EqualCurveValue(left.gravityFalloff, left.gravityFalloffCurve,
                    right.gravityFalloff, right.gravityFalloffCurve, "gravityFalloff", ref mismatchPath))
                return false;

            if (!Equal(left.immobileType, right.immobileType, "immobileType", ref mismatchPath)) return false;
            if (!EqualCurveValue(left.immobile, left.immobileCurve, right.immobile, right.immobileCurve,
                    "immobile", ref mismatchPath)) return false;

            if (!Equal(left.limitType, right.limitType, "limitType", ref mismatchPath)) return false;
            if (!EqualCurveValue(left.maxAngleX, left.maxAngleXCurve, right.maxAngleX, right.maxAngleXCurve,
                    "maxAngleX", ref mismatchPath)) return false;
            if (!EqualCurveValue(left.maxAngleZ, left.maxAngleZCurve, right.maxAngleZ, right.maxAngleZCurve,
                    "maxAngleZ", ref mismatchPath)) return false;
            if (!Equal(left.limitRotation, right.limitRotation, "limitRotation", ref mismatchPath)) return false;
            if (!EqualNormalizedCurve(left.limitRotationXCurve, left.limitRotation.x,
                    right.limitRotationXCurve, right.limitRotation.x, "limitRotationXCurve", ref mismatchPath)) return false;
            if (!EqualNormalizedCurve(left.limitRotationYCurve, left.limitRotation.y,
                    right.limitRotationYCurve, right.limitRotation.y, "limitRotationYCurve", ref mismatchPath)) return false;
            if (!EqualNormalizedCurve(left.limitRotationZCurve, left.limitRotation.z,
                    right.limitRotationZCurve, right.limitRotation.z, "limitRotationZCurve", ref mismatchPath)) return false;

            if (!EqualCurveValue(left.radius, left.radiusCurve, right.radius, right.radiusCurve,
                    "radius", ref mismatchPath)) return false;
            if (!Equal(Permission(left.allowCollision, left.collisionFilter),
                    Permission(right.allowCollision, right.collisionFilter), "allowCollision", ref mismatchPath)) return false;
            if (!SetEquals(left.colliders, right.colliders))
            {
                mismatchPath = "colliders";
                return false;
            }

            if (!EqualCurveValue(left.stretchMotion, left.stretchMotionCurve,
                    right.stretchMotion, right.stretchMotionCurve, "stretchMotion", ref mismatchPath)) return false;
            if (!EqualCurveValue(left.maxStretch, left.maxStretchCurve,
                    right.maxStretch, right.maxStretchCurve, "maxStretch", ref mismatchPath)) return false;
            if (!EqualCurveValue(left.maxSquish, left.maxSquishCurve,
                    right.maxSquish, right.maxSquishCurve, "maxSquish", ref mismatchPath)) return false;

            if (!Equal(Permission(left.allowGrabbing, left.grabFilter),
                    Permission(right.allowGrabbing, right.grabFilter), "allowGrabbing", ref mismatchPath)) return false;
            if (!Equal(Permission(left.allowPosing, left.poseFilter),
                    Permission(right.allowPosing, right.poseFilter), "allowPosing", ref mismatchPath)) return false;
            if (!Equal(left.snapToHand, right.snapToHand, "snapToHand", ref mismatchPath)) return false;
            if (!Equal(left.grabMovement, right.grabMovement, "grabMovement", ref mismatchPath)) return false;

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
            float rightValue, AnimationCurve rightCurve, string name, ref string mismatchPath)
        {
            if (!leftValue.Equals(rightValue))
            {
                mismatchPath = name;
                return false;
            }

            return EqualNormalizedCurve(leftCurve, leftValue, rightCurve, rightValue,
                name + "Curve", ref mismatchPath);
        }

        private static bool EqualNormalizedCurve(AnimationCurve leftCurve, float leftValue,
            AnimationCurve rightCurve, float rightValue, string name, ref string mismatchPath)
        {
            AnimationCurve normalizedLeft = NormalizeCurve(leftCurve, leftValue);
            AnimationCurve normalizedRight = NormalizeCurve(rightCurve, rightValue);
            if (CurvesEqual(normalizedLeft, normalizedRight)) return true;
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

        private static bool CurvesEqual(AnimationCurve left, AnimationCurve right)
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
                if (!a.time.Equals(b.time) || !a.value.Equals(b.value)
                    || !a.inTangent.Equals(b.inTangent) || !a.outTangent.Equals(b.outTangent)
                    || !a.inWeight.Equals(b.inWeight) || !a.outWeight.Equals(b.outWeight)
                    || a.weightedMode != b.weightedMode) return false;
            }
            return true;
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
