using System.Collections.Generic;
using System.Reflection;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Assets.Demo.Scripts.Rendering
{
  [DisallowMultipleComponent]
  [RequireComponent(typeof(CubusWorld))]
  [RequireComponent(typeof(WorldRenderer))]
  public sealed class DemoDensityBiomeObjectScatterRenderer : MonoBehaviour
  {
    private const string DefaultSettingsResourcePath = "Demo Density Object Scatter Settings";

    [SerializeField] private bool enableScatter = true;
    [SerializeField] private DemoDensityObjectScatterSettings settings;
    [SerializeField][Min(0.1f)] private float cleanupInterval = 1f;
    [SerializeField] private bool logScatterDiagnostics = true;

    private sealed class SpawnedObject
    {
      public GameObject Instance;
      public GameObject Prefab;
    }

    private readonly Dictionary<Vector3Int, List<SpawnedObject>> spawnedByChunk = new();
    private readonly Dictionary<GameObject, Stack<GameObject>> poolsByPrefab = new();
    private readonly Stack<GameObject> fallbackPool = new();

    private CubusWorld world;
    private WorldRenderer worldRenderer;
    private FieldInfo densityViewsField;
    private float nextCleanupAt;
    private bool loggedStartup;

    private void Awake()
    {
      world =