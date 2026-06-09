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

    private bool hasCollisionMesh;

    public Vector3Int ChunkCoord { get; private set; }
    public bool IsActive { get; private set; }
    public MeshRenderer MeshRenderer => meshRenderer;

    private void Awake()
    {
      meshFilter = GetComponent<MeshFilter>();
      meshRenderer = GetComponent<MeshRenderer>();
      meshCollider = GetComponent<MeshCollider>();
      if (meshRenderer != null)
      {
        meshRenderer.enabled = false;
      }
    }

    public void Activate(
        Vector3Int chunkCoord,
        float voxelSize,
        Material material,
        Transform parent)
    {
      ChunkCoord = chunkCoord;
      IsActive = true;

      transform.SetParent(parent, false);
      transform.localPosition = VoxelMath.ChunkCoordToWorldPosition(chunkCoord, voxelSize);
      transform.localRotation = Quaternion.identity;
      transform.localScale = Vector3.one;
      gameObject.layer = parent != null ? parent.gameObject.layer : gameObject.layer;

      gameObject.name = $"Chunk {chunkCoord.x}, {chunkCoord.y}, {chunkCoord.z}";
      gameObject.SetActive(true);

      meshRenderer.sharedMaterial = material != null ? material : GetFallbackMaterial();

      if (meshRenderer != null)
      {
        meshRenderer.enabled = false; // enable once a valid mesh is applied
      }
    }

    public void ApplyMesh(MeshData meshData, bool generateCollision)
    {
      ClearMesh();

      if (meshData == null || meshData.IsEmpty)
      {
        return;
      }

      currentMesh = meshData.ToUnityMeshFast();
      currentMesh.name = $"Chunk Mesh {ChunkCoord.x}, {ChunkCoord.y}, {ChunkCoord.z}";
      currentMesh.MarkDynamic();

      meshFilter.sharedMesh = currentMesh;

      meshCollider.enabled = false;
      meshCollider.sharedMesh = null;
      hasCollisionMesh = false;

      if (generateCollision)
      {
        meshCollider.sharedMesh = currentMesh;
        meshCollider.enabled = true;
        hasCollisionMesh = true;
      }

      if (meshRenderer != null)
      {
        meshRenderer.enabled = true;
      }
    }

    public void ApplyMesh(Mesh unityMesh, bool generateCollision)
    {
      ClearMesh();

      if (unityMesh == null)
      {
        return;
      }

      currentMesh = unityMesh;
      currentMesh.name = $"Chunk Mesh {ChunkCoord.x}, {ChunkCoord.y}, {ChunkCoord.z}";
      currentMesh.MarkDynamic();

      meshFilter.sharedMesh = currentMesh;

      meshCollider.enabled = false;
      meshCollider.sharedMesh = null;
      hasCollisionMesh = false;

      if (generateCollision)
      {
        meshCollider.sharedMesh = currentMesh;
        meshCollider.enabled = true;
        hasCollisionMesh = true;
      }

      if (meshRenderer != null)
      {
        if (meshRenderer.sharedMaterial == null)
        {
          meshRenderer.sharedMaterial = GetFallbackMaterial();
        }

        meshRenderer.enabled = true;
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
      if (meshCollider == null)
      {
        return;
      }

      meshCollider.enabled = enabled && hasCollisionMesh;
    }

    public void Release(Transform poolParent)
    {
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