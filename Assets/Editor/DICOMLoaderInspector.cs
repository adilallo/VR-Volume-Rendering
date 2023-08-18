using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DICOMLoader))]
public class DICOMLoaderInspector : Editor
{
    public override void OnInspectorGUI()
    {
        DICOMLoader dicomLoader = (DICOMLoader)target;

        EditorGUILayout.LabelField("DICOM Directory");

        EditorGUI.indentLevel++;

        EditorGUILayout.BeginHorizontal();
        dicomLoader.dicomDir = EditorGUILayout.TextField(dicomLoader.dicomDir);

        if (GUILayout.Button("Select Directory"))
        {
            string path = EditorUtility.OpenFolderPanel("Select DICOM Directory", "", "");

            if (!string.IsNullOrEmpty(path))
            {
                dicomLoader.dicomDir = path;
            }
        }

        EditorGUILayout.EndHorizontal();

        EditorGUI.indentLevel--;

        DrawDefaultInspector();
    }
}
