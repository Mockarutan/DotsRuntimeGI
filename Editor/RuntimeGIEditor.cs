using UnityEditor;
using UnityEngine;

namespace RuntimeGI
{
    [CustomEditor(typeof(RuntimeGIManager))]
    public class RuntimeGIEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var obj = (RuntimeGIManager)target;
            bool hasWorld = obj.HasWorld();
            var buildLabel = hasWorld ? "Rebuild GI World" : "Build GI World";

            if (GUILayout.Button(buildLabel))
            {
                obj.SetupGIWorld();
            }

            EditorGUI.BeginDisabledGroup(hasWorld == false);
            EditorGUI.BeginDisabledGroup(obj.UpdateGI);
            EditorGUILayout.Space();

            if (GUILayout.Button("Step Update World In Editor"))
            {
                obj.StepUpdateGI = true;
                obj.Update();
            }

            if (GUILayout.Button("Step Update World"))
                obj.StepUpdateGI = true;
            if (GUILayout.Button("Update Rays"))
                obj.UpdateRays = true;
            if (GUILayout.Button("Update Lights"))
                obj.UpdateLights = true;
            if (GUILayout.Button("Update Lightmaps"))
                obj.UpdateLightmaps = true;

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.Space();

            if (GUILayout.Button("Create Textures"))
            {
                obj.CreateTextures();
            }

            EditorGUILayout.Space();
            EditorGUILayout.Space();

            if (GUILayout.Button("Dispose GI World"))
            {
                obj.DisposeWorld();
            }
            EditorGUI.EndDisabledGroup();

            
        }
    }
}