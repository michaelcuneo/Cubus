using SpacetimeDB;

public static partial class Module
{
  // Deletes a single game world instance from the server-side world database. This removes
  // the deterministic world contract plus all edit/chunk rows for that WorldId. Player and
  // chat tables are intentionally left alone because they are not part of the terrain instance.
  [Reducer]
  public static void DeleteWorldInstance(ReducerContext ctx, string worldId)
  {
    if (string.IsNullOrWhiteSpace(worldId))
    {
      throw new Exception("DeleteWorldInstance requires a worldId.");
    }

    string normalizedWorldId = worldId.Trim();
    DeleteWorldRows(ctx, normalizedWorldId, out int editsRemoved, out int chunksRemoved);

    var worldStateRemoved = 0;
    if (ctx.Db.world_state.WorldId.Find(normalizedWorldId) is not null)
    {
      ctx.Db.world_state.WorldId.Delete(normalizedWorldId);
      worldStateRemoved = 1;
    }

    Log.Info(
        $"DeleteWorldInstance '{normalizedWorldId}': removed {worldStateRemoved} world_state rows, " +
        $"{editsRemoved} voxel edits and {chunksRemoved} chunks.");
  }

  private static void DeleteWorldRows(
      ReducerContext ctx,
      string worldId,
      out int editsRemoved,
      out int chunksRemoved)
  {
    editsRemoved = 0;
    foreach (var edit in ctx.Db.voxel_edit.WorldId.Filter(worldId))
    {
      ctx.Db.voxel_edit.Key.Delete(edit.Key);
      editsRemoved++;
    }

    chunksRemoved = 0;
    foreach (var chunk in ctx.Db.world_chunk.WorldId.Filter(worldId))
    {
      ctx.Db.world_chunk.Key.Delete(chunk.Key);
      chunksRemoved++;
    }
  }
}
