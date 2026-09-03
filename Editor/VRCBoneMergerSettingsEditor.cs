using UnityEditor;
using UnityEngine;

namespace VRCBoneMerger
{
    [CustomEditor(typeof(VRCBoneMergerSettings))]
    internal sealed class VRCBoneMergerSettingsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("NDMF 非破坏性配置。删除此对象即可停用合并。", MessageType.Info);
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(6);
            if (GUILayout.Button("打开 NDMF 合并检查器"))
            {
                VRCBoneMergerSettings settings = (VRCBoneMergerSettings)target;
                EditorApplication.delayCall += () =>
                {
                    if (settings == null) return;
                    VRCBoneMergerWindow window = EditorWindow.GetWindow<VRCBoneMergerWindow>("VRC Bone Merger");
                    window.SetAvatarRoot(settings.gameObject);
                    window.Show();
                    window.Focus();
                    window.ScanFromInspector();
                    window.Repaint();
                };
            }
        }
    }
}
