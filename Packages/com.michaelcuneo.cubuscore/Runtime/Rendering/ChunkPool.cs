using System.Collections.Generic;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering
{
  public sealed class ChunkPool
  {
    private readonly Stack<ChunkView> pooledViews = new();
    private readonly Transform activeParent;
    private readonly Transform poolParent;

    public int PooledCount => pooledViews.Count;

    public ChunkPool(Transform activeParent)
    {
      this.activeParent = activeParent;

      GameObject poolObject = new("Chunk Pool");
      poolObject.transform.SetParent(activeParent, false);
      poolObject.SetActive(false);

      poolParent = poolObject.transform;
    }

    public ChunkView Acquire(
        Vector3Int chunkCoord,
        float voxelSize,
        Material material)
    {
      ChunkView chunkView = null;

      while (pooledViews.Count > 0 && chunkView == null)
      {
        chunkView = pooledViews.Pop();
      }

      if (chunkView == null)
      {
        GameObject chunkObject = new("Chunk");
        chunkView = chunkObject.AddComponent<ChunkView>();
      }

      chunkView.Activate(
          chunkCoord,
          voxelSize,
          material,
          activeParent
      );

      return chunkView;
    }

    public void Release(ChunkView chunkView)
    {
      if (chunkView == null)
      {
        return;
      }

      chunkView.Release(poolParent);
      pooledViews.Push(chunkView);
    }

    public void DestroyAll()
    {
      while (pooledViews.Count > 0)
      {
        ChunkView chunkView = pooledViews.Pop();

        if (chunkView == null)
        {
          continue;
        }

        if (Application.isPlaying)
        {
          Object.Destroy(chunkView.gameObject);
        }
        else
        {
          Object.DestroyImmediate(chunkView.gameObject);
        }
      }

      if (poolParent != null)
      {
        if (Application.isPlaying)
        {
          Object.Destroy(poolParent.gameObject);
        }
        else
        {
          Object.DestroyImmediate(poolParent.gameObject);
        }
      }
    }
  }
}