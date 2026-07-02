using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Core;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Streaming;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using SpacetimeDB;
using SpacetimeDB.Types;
using UnityEngine;

namespace Assets.Demo.Scripts.Multiplayer
{
  /// <summary>
  /// Server-authoritative smooth-density sculpting. A local sculpt applies immediately for
  /// responsiveness; the exact set of changed voxels is batched into a single <c>EditDensity</c>
  /// reducer call. The resulting authoritative <c>density_edit</c> rows are applied on every client
  /// (idempotent, last-write-wins per voxel) so all players converge — the same per-voxel model as
  /// block <c>voxel_edit</c>, never whole chunks.
  /// </summary>
  public sealed class NetworkedDensityEditBridge : MonoBehaviour
  {
    private DemoNetworkManager net;
    private CubusWorld world;
    private WorldStreamer streamer;
    private bool callbacksRegistered;

    private readonly HashSet<Vector3Int> pendingDirty = new();

    // Reusable batch buffers for the reducer call (built + sent synchronously).
    private readonly List<int> xs = new();
    private readonly List<int> ys = new();
    private readonly List<int> zs = new();
    private readonly List<float> densities = new();
    private readonly List<uint> materials = new();

    public bool IsReady => net != null && net.IsConnected && net.IsSubscriptionApplied;

    private void Awake()
    {
      ResolveWorldRefs();
    }

    private void ResolveWorldRefs()
    {
      if (world == null)
      {
        world = FindAnyObjectByType<CubusWorld>();
      }
      if (streamer == null && world != null)
      {
        streamer = world.GetComponent<WorldStreamer>();
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

    private void Update()
    {
      if (pendingDirty.Count == 0)
      {
        return;
      }

      ResolveWorldRefs();
      if (streamer != null)
      {
        // Re-mesh (and locally persist) the chunks touched by replicated edits, once per frame.
        streamer.RebuildDensityChunks(pendingDirty);
      }

      pendingDirty.Clear();
    }

    private void HandleConnected(DbConnection conn, Identity identity) => RegisterCallbacks(conn);

    private void HandleDisconnected() => UnregisterCallbacks();

    private void RegisterCallbacks(DbConnection conn)
    {
      if (callbacksRegistered || conn == null)
      {
        return;
      }

      conn.Db.DensityEdit.OnInsert += HandleInsert;
      conn.Db.DensityEdit.OnUpdate += HandleUpdate;
      callbacksRegistered = true;

      // Replay edits already present in the local cache (initial subscription state).
      foreach (DensityEdit edit in conn.Db.DensityEdit.Iter())
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

      net.Conn.Db.DensityEdit.OnInsert -= HandleInsert;
      net.Conn.Db.DensityEdit.OnUpdate -= HandleUpdate;
      callbacksRegistered = false;
    }

    private void HandleInsert(EventContext ctx, DensityEdit edit) => Apply(edit);

    private void HandleUpdate(EventContext ctx, DensityEdit oldEdit, DensityEdit newEdit) => Apply(newEdit);

    private void Apply(DensityEdit edit)
    {
      if (net != null && edit.WorldId != net.WorldId)
      {
        return;
      }

      ResolveWorldRefs();
      if (world == null)
      {
        return;
      }

      Vector3Int voxel = new(edit.X, edit.Y, edit.Z);

      // Absolute value write. Returns false (no-op) when it already matches - which is exactly
      // what happens for the originator's own echoed edits, so they cost nothing.
      if (world.SetDensityVoxelAtWorldVoxel(voxel, edit.Density, (ushort)edit.Material))
      {
        pendingDirty.Add(VoxelMath.WorldVoxelToChunkCoord(voxel));
      }
    }

    /// <summary>
    /// Batches a set of locally-applied density voxel changes into one authoritative reducer call.
    /// Returns false (and does nothing) when offline - the local sculpt already applied.
    /// </summary>
    public bool SendEdits(IReadOnlyList<DensityVoxelEdit> edits)
    {
      if (!IsReady || edits == null || edits.Count == 0)
      {
        return false;
      }

      xs.Clear();
      ys.Clear();
      zs.Clear();
      densities.Clear();
      materials.Clear();

      for (int i = 0; i < edits.Count; i++)
      {
        DensityVoxelEdit e = edits[i];
        xs.Add(e.Voxel.x);
        ys.Add(e.Voxel.y);
        zs.Add(e.Voxel.z);
        densities.Add(e.Density);
        materials.Add(e.Material);
      }

      // Called from main-thread input; InternalCallReducer serialises synchronously so reusing
      // the member buffers is safe.
      net.Conn.Reducers.EditDensity(net.WorldId, xs, ys, zs, densities, materials);
      return true;
    }
  }
}
