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
    private Bounds localChunkBounds;
    private bool hasChunkBounds;
    private static Material fallbackMaterial;

    // Naming chunk meshes allocates a fresh interpolated string on every mesh
    // apply (a hot main-thread path). The names are only ever useful for
    // Profiler/Frame Debugger inspection, so they are off by default and can be
    // toggled on when debugging.
    public static bool AssignDebugMeshNames;

    private bool hasCollisionMesh;
    private bool collisionDesired;
    private ChunkCollisionBaker collisionBaker;

    public Vector3Int ChunkCoord { get; private set; }
    public bool IsActive { get; private set; }
    public MeshRenderer MeshRenderer => meshRenderer;

    /// <summary>The Unity mesh currently applied to this view, or null.</summary>
    public Mesh CurrentMesh => currentMesh;

    /// <summary>The chunk bounds in the view's local space.</summary>
    public Bounds LocalBounds => hasChunkBounds ? localChunkBounds : new Bounds(Vector3.zero, Vector3.zero);

    /// <summary>The chunk bounds in world space.</summary>
    public Bounds WorldBounds => GetWorldBounds(0.0f);

    /// <summary>
    /// Assigns the shared baker that cooks collision meshes off the main thread. When
    /// null, collision falls back to a synchronous cook.
    /// </summary>
    internal void SetCollisionBaker(ChunkCollisionBaker baker)
    {
      collisionBaker = baker;
    }

    /// <summary>
    /// True when the streamer has deliberately hidden this chunk because it is
    /// outside the camera view. The mesh stays resident so it can be shown again
    /// instantly; only the renderer is disabled. Distinct from a cleared/empty view.
    /// </summary>
    public bool RenderCulled { get; private set; }

    /// <summary>True when this view currently holds a renderable mesh.</summary>
    public bool HasRenderableMesh => currentMesh != null;

    public bool HasActiveCollisionMesh =>
      meshCollider != null && meshCollider.enabled && meshCollider.sharedMesh != null;

    /// <summary>
    /// Shows or hides only the renderer for view-frustum culling, without touching the
    /// mesh, collider, or active/pooling state. A hidden chunk keeps its mesh so
    /// re-showing is free. Never force-enables a renderer that has no mesh.
    /// </summary>
    public void SetRenderVisible(bool visible)
    {
      RenderCulled = !visible;
      ApplyRendererVisibilityForCurrentMesh();
    }

    public Bounds GetWorldBounds(float padding)
    {
      Bounds bounds = hasChunkBounds ? localChunkBounds : new Bounds(Vector3.zero, Vector3.zero);

      if (padding > 0.0f)
      {
        bounds.Expand(padding * 2.0f);
      }

      Vector3 worldCenter = transform.TransformPoint(bounds.center);
      Vector3 scale = transform.lossyScale;
      Vector3 worldSize = new(
        Mathf.Abs(scale.x) * bounds.size.x,
        Mathf.Abs(scale.y) * bounds.size.y,
        Mathf.Abs(scale.z) * bounds.size.z);

      return new Bounds(worldCenter, worldSize);
    }

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
      RenderCulled = false;

      transform.SetParent(parent, false);
      transform.localPosition = VoxelMath.ChunkCoordToWorldPosition(chunkCoord, voxelSize);
      transform.localRotation = Quaternion.identity;
      transform.localScale = Vector3.one;
      gameObject.layer = parent != null ? parent.gameObject.layer : gameObject.layer;

      float chunkWorldSize = VoxelConstants.ChunkSize * Mathf.Max(0.0001f, voxelSize);
      localChunkBounds = new Bounds(Vector3.one * (chunkWorldSize * 0.5f), Vector3.one * chunkWorldSize);
      hasChunkBounds = true;

      gameObject.name = $"Chunk {chunkCoord.x}, {chunkCoord.y}, {chunkCoord.z}";
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
        RequestCollisionBake();
      }
      else
      {
        collisionDesired = false;
        meshCollider.enabled = false;
        meshCollider.sharedMesh = null;
        hasCollisionMesh = false;
      }

      ApplyRendererVisibilityForCurrentMesh();

      if (oldMesh != null)
      {
        Mesh newMesh = currentMesh;
        currentMesh = oldMesh;
        SafeDestroyMesh(currentMesh);
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
        RequestCollisionBake();
      }
      else
      {
        collisionDesired = false;
        meshCollider.enabled = false;
        meshCollider.sharedMesh = null;
        hasCollisionMesh = false;
      }

      if (meshRenderer != null && meshRenderer.sharedMaterial == null)
      {
        meshRenderer.sharedMaterial = GetFallbackMaterial();
      }

      ApplyRendererVisibilityForCurrentMesh();

      if (oldMesh != null)
      {
        Mesh newMesh = currentMesh;
        currentMesh = oldMesh;
        SafeDestroyMesh(currentMesh);
        currentMesh = newMesh;
      }
      else
      {
        DestroyImmediate(oldMesh);
      }
    }

    private void ApplyRendererVisibilityForCurrentMesh()
    {
      if (meshRenderer == null)
      {
        return;
      }

      bool shouldEnable = currentMesh != null && !RenderCulled;
      if (meshRenderer.enabled != shouldEnable)
      {
        meshRenderer.enabled = shouldEnable;
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
      collisionDesired = false;

      if (currentMesh != null)
      {
        SafeDestroyMesh(currentMesh);
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
        // Keep the (already-baked) shared mesh so re-enabling later is instant.
        collisionDesired = false;
        meshCollider.enabled = false;
        return;
      }

      RequestCollisionBake();
    }

    /// <summary>
    /// Requests collision for the current mesh. If a baker is available the physics
    /// cook runs off the main thread and the collider is enabled later via
    /// <see cref="OnCollisionMeshBaked"/>; otherwise it cooks synchronously.
    /// </summary>
    private void RequestCollisionBake()
    {
      collisionDesired = true;

      if (currentMesh == null)
      {
        meshCollider.enabled = false;
        hasCollisionMesh = false;
        return;
      }

      if (hasCollisionMesh && meshCollider.sharedMesh == currentMesh)
      {
        meshCollider.enabled = true;
        return;
      }

      if (collisionBaker != null)
      {
        meshCollider.enabled = false;
        collisionBaker.RequestBake(this, currentMesh);
        return;
      }

      meshCollider.enabled = false;
      meshCollider.sharedMesh = null;
      meshCollider.sharedMesh = currentMesh;
      meshCollider.enabled = true;
      hasCollisionMesh = true;
    }

    /// <summary>
    /// True while this view still wants a collider for <paramref name="mesh"/> and that
    /// mesh is still the active one. Used by the baker to skip stale bake requests.
    /// </summary>
    public bool WantsCollisionBakeFor(Mesh mesh)
    {
      return collisionDesired && IsActive && ReferenceEquals(currentMesh, mesh);
    }

    /// <summary>
    /// Called on the main thread once the baker has cooked <paramref name="mesh"/>.
    /// Assigning the already-baked mesh reuses the cooked data (no main-thread cook).
    /// </summary>
    public void OnCollisionMeshBaked(Mesh mesh)
    {
      EnsureComponents();

      if (!collisionDesired || meshCollider == null || !ReferenceEquals(currentMesh, mesh))
      {
        return;
      }

      meshCollider.sharedMesh = mesh;
      meshCollider.enabled = true;
      hasCollisionMesh = true;
    }

    private void SafeDestroyMesh(Mesh mesh)
    {
      if (mesh == null)
      {
        return;
      }

      if (collisionBaker != null)
      {
        collisionBaker.SafeDestroyMesh(mesh);
      }
      else
      {
        DestroyMesh(mesh);
      }
    }

    public void Release(Transform poolParent)
    {
      EnsureComponents();
      ClearMesh();

      IsActive = false;
      RenderCulled = false;
      ChunkCoord = Vector3Int.zero;
      hasChunkBounds = false;
      localChunkBounds = default;

      transform.SetParent(poolParent, false);
      transform.localPosition = Vector3.zero;
      transform.localRotation = Quaternion.identity;
      transform.localScale = Vector3.one;

      gameObject.name = "Pooled Chunk";
      gameObject.SetActive(false);
    }
  }
}
