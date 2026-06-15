using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CubusWorld))]
public sealed class CubusWorldEditor : Editor
{
  public override void OnInspectorGUI()
  {
    DrawDefaultInspector();

    EditorGUILayout.Space(12);

    CubusWorld world = (CubusWorld)target;

    if (GUILayout.Button("Generate World Database", GUILayout.Height(32)))
    {
      world.GenerateWorld();
      EditorUtility.SetDirty(world);
    }

    if (GUILayout.Button("Clear Generated World Data", GUILayout.Height(24)))
    {
      world.ClearWorld();
      EditorUtility.SetDirty(world);
    }

    EditorGUILayout.Space(6);

    EditorGUILayout.LabelField("World Ready", world.IsWorldReady ? "Yes" : "No");
    EditorGUILayout.LabelField("Generation Progress", $"{world.GenerationProgress:P1}");
    EditorGUILayout.LabelField("Block Chunks", world.Data.BlockChunks.Count.ToString());
    EditorGUILayout.LabelField("Density Chunks", world.Data.DensityChunks.Count.ToString());
  }
}