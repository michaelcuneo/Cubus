using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Chunks;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming
{
  public sealed partial class WorldStreamer
  {
    public void RebuildDensityChunks(IEnumerable<Vector3Int> dirtyChunks)
    {
      if (!EnsureRuntimeReferences() || !IsDensityTerrainEnabled || dirtyChunks == null) return;

      foreach (Vector3Int dirtyChunk in dirtyChunks)
      {
        for (int i = 0; i < DensityEditAffectedChunkOffsets.Length; i++)
        {
          Vector3Int c = dirtyChunk + DensityEditAffectedChunkOffsets[i];
          if (!world.Settings.IsInsideEffectiveWorldBounds3D(c)) continue;
          if (!desiredChunkCoords.Contains(c) && !keepChunkCoords.Contains(c))
          {
            continue;
          }

          knownEmptyDensityChunks.Remove(c);
          QueueDensityRender(c);
        }
      }
    }

    private void HandleBlockChunksEdited(IReadOnlyCollection<Vector3Int> dirtyChunks)
    {
      editedChunkSet.Clear();
      editedChunksBuffer.Clear();

      foreach (Vector3Int dirtyChunk in dirtyChunks)
      {
        AddEditedBlockChunkCandidate(dirtyChunk);
        AddEditedBlockChunkCandidate(dirtyChunk + Vector3Int.left);
        AddEditedBlockChunkCandidate(dirtyChunk + Vector3Int.right);
        AddEditedBlockChunkCandidate(dirtyChunk + Vector3Int.down);
        AddEditedBlockChunkCandidate(dirtyChunk + Vector3Int.up);
        AddEditedBlockChunkCandidate(dirtyChunk + new Vector3Int(0, 0, -1));
        AddEditedBlockChunkCandidate(dirtyChunk + new Vector3Int(0, 0, 1));

        if (persistStreamedChunks && storage != null && storage.ActiveStore != null)
        {
          storage.SaveEditedChunk(dirtyChunk);
        }
      }

      for (int i = 0; i < editedChunksBuffer.Count; i++)
      {
        QueueEditedBlockChunk(editedChunksBuffer[i]);
      }

      editedChunkSet.Clear();
      editedChunksBuffer.Clear();
    }

    private void QueueEditedBlockChunk(Vector3Int chunkCoord)
    {
      if (!world.Settings.IsInsideEffectiveWorldBounds3D(chunkCoord))
      {
        return;
      }

      if (!desiredChunkCoords.Contains(chunkCoord) && !keepChunkCoords.Contains(chunkCoord))
      {
        return;
      }

      knownEmptyChunks.Remove(chunkCoord);
      QueueBlockRender(chunkCoord);
    }

    private void AddEditedBlockChunkCandidate(Vector3Int chunkCoord)
    {
      if (editedChunkSet.Add(chunkCoord))
      {
        editedChunksBuffer.Add(chunkCoord);
      }
    }
  }
}
