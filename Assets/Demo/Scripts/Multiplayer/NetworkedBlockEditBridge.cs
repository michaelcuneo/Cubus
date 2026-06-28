using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Editing;
using SpacetimeDB;
using SpacetimeDB.Types;
using UnityEngine;

namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// Server-authoritative block editing. Local edit intents are forwarded to the server via the
  /// <c>EditBlock</c> reducer; the resulting authoritative <c>voxel_edit</c> rows are applied to the
  /// world on every client (including the originator) so all players converge on the same terrain.
  /// </summary>
  [RequireComponent(typeof(BlockEditTool))]
  public sealed class NetworkedBlockEditBridge : MonoBehaviour
  {
    [SerializeField] private BlockEditTool editTool;

    private DemoNetworkManager net;
    private bool callbacksRegistered;

    public bool IsReady => net != null && net.IsConnected && net.IsSubscriptionApplied;

    private void Awake()
    {
      if (editTool == null)
      {
        editTool = GetComponent<BlockEditTool>();
      }
      if (editTool == null || !editTool.HasWorld)
      {
        // The RequireComponent above guarantees a local BlockEditTool, but that
        // one has no CubusWorld. Bind to the world-owning tool so edits apply.
        editTool = BlockEditTool.FindWorldEditTool() ?? editTool;
      }
    }

    private void OnEnable()
    {
      net = DemoNetworkManager.Instance;
      if (net != null)
      {
        net.Connected += HandleConnected;
        net.Disconnected += HandleDisconnected;
        if (net.IsConnected)
        {
          HandleConnected(net.Conn, net.LocalIdentity);
        }
      }
    }

    private void OnDisable()
    {
      if (net != null)
      {
        net.Connected -= HandleConnected;
        net.Disconnected -= HandleDisconnected;
      }
      UnregisterCallbacks();
    }

    private void HandleConnected(DbConnection conn, Identity identity) => RegisterCallbacks(conn);

    private void HandleDisconnected() => UnregisterCallbacks();

    private void RegisterCallbacks(DbConnection conn)
    {
      if (callbacksRegistered || conn == null)
      {
        return;
      }

      conn.Db.VoxelEdit.OnInsert += HandleVoxelEditInsert;
      conn.Db.VoxelEdit.OnUpdate += HandleVoxelEditUpdate;
      callbacksRegistered = true;

      // Replay any edits already present in the local cache (initial subscription state).
      foreach (VoxelEdit edit in conn.Db.VoxelEdit.Iter())
      {
        Apply(edit);
      }
    }

    private void UnregisterCallbacks()
    {
      if (!callbacksRegistered || net == null || net.Conn == null)
      {
        callbacksRegistered = false;
        return;
      }

      net.Conn.Db.VoxelEdit.OnInsert -= HandleVoxelEditInsert;
      net.Conn.Db.VoxelEdit.OnUpdate -= HandleVoxelEditUpdate;
      callbacksRegistered = false;
    }

    private void HandleVoxelEditInsert(EventContext ctx, VoxelEdit edit) => Apply(edit);

    private void HandleVoxelEditUpdate(EventContext ctx, VoxelEdit oldEdit, VoxelEdit newEdit) => Apply(newEdit);

    private void Apply(VoxelEdit edit)
    {
      if (net != null && edit.WorldId != net.WorldId)
      {
        return;
      }

      editTool.ApplyNetworkVoxelEdit(
          new Vector3Int(edit.X, edit.Y, edit.Z),
          (ushort)edit.Material);
    }

    /// <summary>
    /// Forwards a local edit intent to the server. The change becomes visible once the
    /// authoritative <c>voxel_edit</c> row is replicated back. Material 0 removes the voxel.
    /// </summary>
    public bool SendEdit(Vector3Int worldVoxel, ushort material)
    {
      if (!IsReady)
      {
        return false;
      }

      net.Conn.Reducers.EditBlock(net.WorldId, worldVoxel.x, worldVoxel.y, worldVoxel.z, material);
      return true;
    }
  }
}
