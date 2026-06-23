using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Lod
{
  /// <summary>
  /// Renders Distant-Horizon LOD tiles by reusing the standard
  /// <see cref="ChunkView"/>/<see cref="ChunkPool"/> machinery. Each tile is an
  /// ordinary chunk view positioned with the level's coarse voxel size
  /// (<see cref="LodConstants.VoxelSizeForLevel"/>) and drawn with the shared
  /// biome-atlas material, but never gets a collider (distant scenery is
  /// non-interactive). Tiles live under a dedicated container so they can be
  /// inspected/toggled independently of the full-detail chunks.
  ///
  /// Pure presentation: the owner (LOD streamer) decides which tiles exist; this
  /// class just shows, replaces and hides their meshes.
  /// </summary>
  public sealed class LodChunkRenderer
  {
    private readonly ChunkPool pool;
    private readonly Transform container;
    private readonly Dictionary<LodTileKey, ChunkView> activeTiles = new();

    public int ActiveTileCount => activeTiles.Count;
    public Transform Container => container;

    /// <param name="worldTransform">
    /// The world/renderer transform. LOD tiles are parented under a child of this
    /// so their coordinate math lines up with the full-detail chunks.
    /// </param>
    public LodChunkRenderer(Transform worldTransform)
    {
      var containerObject = new GameObject("LOD Chunks");
      containerObject.transform.SetParent(worldTransform, false);
      containerObject.transform.localPosition = Vector3.zero;
      containerObject.transform.localRotation = Quaternion.identity;
      containerObject.transform.localScale = Vector3.one;
      container = containerObject.transform;

      pool = new ChunkPool(container);
    }

    public bool HasTile(LodTileKey key)
    {
      return activeTiles.ContainsKey(key);
    }

    /// <summary>
    /// Shows or replaces a tile's mesh. An empty/null mesh hides the tile instead.
    /// The caller retains ownership of <paramref name="meshData"/> (it is copied
    /// into a Unity mesh here) and may return it to its pool afterwards.
    /// </summary>
    public void ShowTile(LodTileKey key, MeshData meshData, float baseVoxelSize, Material material)
    {
      if (meshData == null || meshData.IsEmpty)
      {
        HideTile(key);
        return;
      }

      float voxelSize = LodConstants.VoxelSizeForLevel(key.Level, baseVoxelSize);

      if (!activeTiles.TryGetValue(key, out ChunkView view) || view == null)
      {
        view = pool.Acquire(key.Coord, voxelSize, material);
        activeTiles[key] = view;
      }

      // Distant LOD tiles never get colliders.
      view.ApplyMesh(meshData, false);
    }

    public bool HideTile(LodTileKey key)
    {
      if (!activeTiles.TryGetValue(key, out ChunkView view))
      {
        return false;
      }

      activeTiles.Remove(key);
      pool.Release(view);
      return true;
    }

    /// <summary>
    /// Adds every active tile key into <paramref name="buffer"/> (cleared first),
    /// so the streamer can diff against its desired set without allocating.
    /// </summary>
    public void CollectActiveKeys(List<LodTileKey> buffer)
    {
      buffer.Clear();

      foreach (LodTileKey key in activeTiles.Keys)
      {
        buffer.Add(key);
      }
    }

    public void HideAll()
    {
      foreach (KeyValuePair<LodTileKey, ChunkView> pair in activeTiles)
      {
        if (pair.Value != null)
        {
          pool.Release(pair.Value);
        }
      }

      activeTiles.Clear();
    }

    public void Destroy()
    {
      HideAll();
      pool.DestroyAll();

      if (container != null)
      {
        if (Application.isPlaying)
        {
          Object.Destroy(container.gameObject);
        }
        else
        {
          Object.DestroyImmediate(container.gameObject);
        }
      }
    }
  }
}
