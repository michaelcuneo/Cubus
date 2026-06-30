using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering
{
  [RequireComponent(typeof(MeshFilter))]
  [RequireComponent(typeof(MeshRenderer))]
  [RequireComponent(typeof(MeshCollider))]
  public sealed class ChunkView : MonoBehaviour
  {
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;

    private Mesh currentMesh;
    private static Material fallbackMaterial;

    // Naming chunk meshes allocates a fresh interpolated string on every mesh
    // apply (a hot main-thread path). The names are only ever useful for
    // Profiler/Frame Debugger inspection, so they are off by default and can be
    // toggled on when debugging.
    public static bool AssignDebugMeshNames;

    // MeshCollider cooking is one of the nastiest main-thread spikes during
    // streaming. Keep visual terrain fast and let collision catch up gradually
    // instead of baking dozens of colliders in the same frame.
    public static int MaxCollisionBakesPerFrame = 1;
    private static int collisionBakeFrame = -1;
    private static int collisionBakesThisFrame;

    private bool hasCollisionMesh;

    public Vector3Int ChunkCoord { get; private set; }
    public bool IsActive { get; private set; }
    public MeshRenderer MeshRenderer => meshRenderer;

    private void Awake()
    {
      EnsureComponents();
      if (meshRenderer != null)
      {
        meshRenderer.enabled = false;
      }
    }

    private void EnsureComponents()
    {
      if (meshFilter == null)
      {
        meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
          meshFilter = gameObject.AddComponent<MeshFilter>();
        }
      }

      if (meshRenderer == null)
      {
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
          meshRenderer = gameObject.AddComponent<MeshRenderer>();
        }
      }

      if (meshCollider == null)
      {
        meshCollider = GetComponent<MeshCollider>();
        if (meshCollider == null)
        {
          meshCollider = gameObject.AddComponent<MeshCollider>();
        }
      }
    }

    public void Activate(
        Vector3Int chunkCoord,
        float voxelSize,
        Material material,
        Transform parent)
    {
      EnsureComponents();

      ChunkCoord = chunkCoord;
      IsActive = true;

      transform.SetParent(parent, false);
      transform.localPosition = VoxelMath.ChunkCoordToWorldPosition(chunkCoord, voxelSize);
      transform.localRotation = Quaternion.identity;
      transform.localScale = Vector3.one;
      gameObject.layer = parent != null ? parent.gameObject.layer : gameObject.layer;

      gameObject.name = AssignDebugMeshNames
        ? $"Chunk {chunkCoord.x}, {chunkCoord.y}, {chunkCoord.z}"
        : "Chunk";
      gameObject.SetActive(true);

      if (meshRenderer != null)
      {
        meshRenderer.sharedMaterial = material != null ? material : GetFallbackMaterial();
      }

      if (meshRenderer != null)
      {
        meshRenderer.enabled = false; // enable once a valid mesh is applied
      }
    }

    public void ApplyMesh(MeshData meshData, bool generateCollision)
    {
      EnsureComponents();

      if (meshData == null || meshData.IsEmpty)
      {
        ClearMesh();
        return;
      }

      Mesh oldMesh = currentMesh;

      currentMesh = meshData.ToUnityMeshFast();

      if (AssignDebugMeshNames)
      {
        currentMesh.name = $"Chunk Mesh {ChunkCoord.x}, {ChunkCoord.y}, {ChunkCoord.z}";
      }

      currentMesh.MarkDynamic();

      meshFilter.sharedMesh = currentMesh;

      if (generateCollision)
      {
        TryApplyCollisionMesh();
      }
      else
      {
        meshCollider.enabled = false;
        meshCollider.sharedMesh = null;
        hasCollisionMesh = false;
      }

      if (meshRenderer != null)
      {
        meshRenderer.enabled = true;
      }

      if (oldMesh != null)
      {
        Mesh newMesh = currentMesh;
        currentMesh = oldMesh;
        DestroyMesh(currentMesh);
        currentMesh = newMesh;
      }
    }

    public void ApplyMesh(Mesh unityMesh, bool generateCollision)
    {
      EnsureComponents();

      if (unityMesh == null)
      {
        ClearMesh();
        return;
      }

      Mesh oldMesh = currentMesh;

      currentMesh = unityMesh;

      if (AssignDebugMeshNames)
      {
        currentMesh.name = $"Chunk Mesh {ChunkCoord.x}, {ChunkCoord.y}, {ChunkCoord.z}";
      }

      currentMesh.MarkDynamic();

      meshFilter.sharedMesh = currentMesh;

      if (generateCollision)
      {
        TryApplyCollisionMesh();
      }
      else
      {
        meshCollider.enabled = false;
        meshCollider.sharedMesh = null;
        hasCollisionMesh = false;
      }

      if (meshRenderer != null)
      {
        if (meshRenderer.sharedMaterial == null)
        {
          meshRenderer.sharedMaterial = GetFallbackMaterial();
        }

        meshRenderer.enabled = true;
      }

      if (oldMesh != null)
      {
        Mesh newMesh = currentMesh;
        currentMesh = oldMesh;
        DestroyMesh(currentMesh);
        currentMesh = newMesh;
      }
    }

    private static void DestroyMesh(Mesh mesh)
    {
      if (mesh == null)
      {
        return;
      }

      if (Application.isPlaying)
      {
        Destroy(mesh);
      }
      else
      {
        DestroyImmediate(mesh);
      }
    }

    private static Material GetFallbackMaterial()
    {
      if (fallbackMaterial != null)
      {
        return fallbackMaterial;
      }

      Shader shader = Shader.Find("Universal Render Pipeline/Lit");
      if (shader == null)
      {
        shader = Shader.Find("Standard");
      }

      fallbackMaterial = new Material(shader)
      {
        name = "Cubus Fallback Chunk Material"
      };

      fallbackMaterial.color = Color.white;
      return fallbackMaterial;
    }

    public void ClearMesh()
    {
      EnsureComponents();

      if (meshFilter != null)
      {
        meshFilter.sharedMesh = null;
      }

      if (meshCollider != null)
      {
        meshCollider.enabled = false;
        meshCollider.sharedMesh = null;
      }

      hasCollisionMesh = false;

      if (currentMesh != null)
      {
        if (Application.isPlaying)
        {
          Destroy(currentMesh);
        }
        else
        {
          DestroyImmediate(currentMesh);
        }

        currentMesh = null;
      }

      if (meshRenderer != null)
      {
        meshRenderer.enabled = false;
      }
    }

    public void SetCollisionEnabled(bool enabled)
    {
      EnsureComponents();

      if (meshCollider == null)
      {
        return;
      }

      if (!enabled)
      {
        meshCollider.enabled = false;
        return;
      }

      if (currentMesh == null)
      {
        meshCollider.enabled = false;
        hasCollisionMesh = false;
        return;
      }

      if (!hasCollisionMesh || meshCollider.sharedMesh != currentMesh)
      {
        TryApplyCollisionMesh();
        return;
      }

      meshCollider.enabled = true;
    }

    private void TryApplyCollisionMesh()
    {
      if (meshCollider == null || currentMesh == null)
      {
        return;
      }

      if (!TryReserveCollisionBakeSlot())
      {
        meshCollider.enabled = false;
        return;
      }

      meshCollider.enabled = false;
      meshCollider.sharedMesh = null;
      meshCollider.sharedMesh = currentMesh;
      hasCollisionMesh = true;
      meshCollider.enabled = true;
    }

    private static bool TryReserveCollisionBakeSlot()
    {
      if (MaxCollisionBakesPerFrame <= 0)
      {
        return false;
      }

      int frame = Time.frameCount;
      if (collisionBakeFrame != frame)
      {
        collisionBakeFrame = frame;
        collisionBakesThisFrame = 0;
      }

      if (collisionBakesThisFrame >= MaxCollisionBakesPerFrame)
      {
        return false;
      }

      collisionBakesThisFrame++;
      return true;
    }

    public void Release(Transform poolParent)
    {
      EnsureComponents();
      ClearMesh();

      IsActive = false;
      ChunkCoord = Vector3Int.zero;

      transform.SetParent(poolParent, false);
      transform.localPosition = Vector3.zero;
      transform.localRotation = Quaternion.identity;
      transform.localScale = Vector3.one;

      gameObject.name = "Pooled Chunk";
      gameObject.SetActive(false);
    }
  }
}