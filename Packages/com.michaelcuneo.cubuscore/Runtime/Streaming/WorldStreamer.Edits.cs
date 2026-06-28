using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    public void RebuildDensityChunks(IEnumerable<Vector3Int> dirtyChunks)
    {
      if (!EnsureRuntimeReferences() || world.Settings.TerrainSystem != TerrainSystem.SmoothDensity) return;

      foreach (Vector3Int dirtyChunk in dirtyChunks)
      {
        for (int i = 0; i < DensityEditAffectedChunkOffsets.Length; i++)
        {
          Vector3Int c = dirtyChunk + DensityEditAffectedChunkOffsets[i];
          if (!world.Settings.IsInsideEffectiveWorldBounds3D(c)) continue;
          knownEmptyChunks.Remove(c);
          desiredChunkCoords.Add(c);
          keepChunkCoords.Add(c);

          QueueRender(c);
        }
      }
    }

    private void HandleBlockChunksEdited(IReadOnlyCollection<Vector3Int> dirtyChunks)
    {
      foreach (Vector3Int dirtyChunk in dirtyChunks)
      {
        QueueEditedBlockChunk(dirtyChunk);
        QueueEditedBlockChunk(dirtyChunk + Vector3Int.left);
        QueueEditedBlockChunk(dirtyChunk + Vector3Int.right);
        QueueEditedBlockChunk(dirtyChunk + Vector3Int.down);
        QueueEditedBlockChunk(dirtyChunk + Vector3Int.up);
        QueueEditedBlockChunk(dirtyChunk + new Vector3Int(0, 0, -1));
        QueueEditedBlockChunk(dirtyChunk + new Vector3Int(0, 0, 1));

        // Write the edited chunk straight to the store so the change survives
        // eviction/quit and reloads fast. The edit also lives in the sparse
        // override layer (re-applied on every streamed load). SaveEditedChunk
        // writes an explicit empty record when the edit erased the whole chunk,
        // so a fully-cleared chunk doesn't reload its stale pre-edit terrain.
        if (persistStreamedChunks && storage != null && storage.ActiveStore != null)
        {
          storage.SaveEditedChunk(dirtyChunk);
        }
      }
    }

    private void QueueEditedBlockChunk(Vector3Int chunkCoord)
    {
      if (!world.Settings.IsInsideEffectiveWorldBounds3D(chunkCoord)) return;
      knownEmptyChunks.Remove(chunkCoord);
      desiredChunkCoords.Add(chunkCoord);
      keepChunkCoords.Add(chunkCoord);
      QueueRender(chunkCoord);
    }

  }
}