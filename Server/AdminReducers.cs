using SpacetimeDB;

public static partial class Module
{
  // Deletes a single world instance from the server-side world database. This removes
  // the deterministic world contract plus all edit/chunk rows for that WorldId.
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

  // Clears every world instance from the server-side world database while leaving
  // player presence and chat tables alone. Use this for dev/test server resets.
  [Reducer]
  public static void ClearAllWorldData(ReducerContext ctx)
  {
    var worldStatesRemoved = 0;
    foreach (var state in ctx.Db.world_state.Iter())
    {
      ctx.Db.world_state.WorldId.Delete(state.WorldId);
      worldStatesRemoved++;
    }

    var editsRemoved = 0;
    foreach (var edit in ctx.Db.voxel_edit.Iter())
    {
      ctx.Db.voxel_edit.Key.Delete(edit.Key);
      editsRemoved++;
    }

    var chunksRemoved = 0;
    foreach (var chunk in ctx.Db.world_chunk.Iter())
    {
      ctx.Db.world_chunk.Key.Delete(chunk.Key);
      chunksRemoved++;
    }

    Log.Info(
        $"ClearAllWorldData: removed {worldStatesRemoved} world_state rows, " +
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
