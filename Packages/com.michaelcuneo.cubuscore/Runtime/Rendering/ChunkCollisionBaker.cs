using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Rendering
{
  /// <summary>
  /// Cooks MeshCollider physics data off the main thread with Physics.BakeMesh.
  ///
  /// Assigning a mesh to a MeshCollider normally triggers a synchronous physics cook
  /// on the main thread, which is the classic per-chunk hitch when terrain streams in
  /// (or a chunk enters the collision radius while walking). By pre-baking the mesh on
  /// a worker thread, the later MeshCollider.sharedMesh assignment reuses the cooked
  /// data and is nearly free.
  ///
  /// The baker also owns destruction of any mesh with a bake in flight so BakeMesh
  /// never touches a mesh that was freed on the main thread mid-cook.
  /// </summary>
  internal sealed class ChunkCollisionBaker
  {
    private sealed class BakeJob
    {
      public ChunkView View;
      public Mesh Mesh;
      public EntityId MeshId;
      public Task Task;
    }

    private readonly Queue<BakeJob> pending = new();
    private readonly List<BakeJob> inFlight = new();
    private readonly List<Mesh> deferredDestroy = new();

    private int maxConcurrentBakes = 4;

    public void Configure(int maxConcurrent)
    {
      maxConcurrentBakes = Mathf.Clamp(maxConcurrent, 1, 16);
    }

    public void RequestBake(ChunkView view, Mesh mesh)
    {
      if (view == null || mesh == null)
      {
        return;
      }

      pending.Enqueue(new BakeJob { View = view, Mesh = mesh, MeshId = mesh.GetEntityId() });
    }

    /// <summary>
    /// Destroys a mesh immediately unless it currently has a bake in flight, in which
    /// case destruction is deferred until the bake finishes.
    /// </summary>
    public void SafeDestroyMesh(Mesh mesh)
    {
      if (mesh == null)
      {
        return;
      }

      if (IsBaking(mesh))
      {
        if (!deferredDestroy.Contains(mesh))
        {
          deferredDestroy.Add(mesh);
        }

        return;
      }

      DestroyMeshNow(mesh);
    }

    public void Update()
    {
      while (inFlight.Count < maxConcurrentBakes && pending.Count > 0)
      {
        BakeJob job = pending.Dequeue();
        if (job.View == null || job.Mesh == null || !job.View.WantsCollisionBakeFor(job.Mesh))
        {
          continue;
        }

        EntityId meshId = job.MeshId;
        job.Task = Task.Run(() => Physics.BakeMesh(meshId, false));
        inFlight.Add(job);
      }

      for (int i = inFlight.Count - 1; i >= 0; i--)
      {
        BakeJob job = inFlight[i];
        if (job.Task != null && !job.Task.IsCompleted)
        {
          continue;
        }

        inFlight.RemoveAt(i);

        if (job.View != null && job.Mesh != null)
        {
          job.View.OnCollisionMeshBaked(job.Mesh);
        }
      }

      for (int i = deferredDestroy.Count - 1; i >= 0; i--)
      {
        Mesh mesh = deferredDestroy[i];
        if (mesh == null)
        {
          deferredDestroy.RemoveAt(i);
          continue;
        }

        if (!IsBaking(mesh))
        {
          deferredDestroy.RemoveAt(i);
          DestroyMeshNow(mesh);
        }
      }
    }

    public void Clear()
    {
      // In-flight bakes are short; wait briefly so no worker is still reading a mesh
      // when we drop our references and destroy the deferred set.
      for (int i = 0; i < inFlight.Count; i++)
      {
        try
        {
          inFlight[i].Task?.Wait(50);
        }
        catch
        {
          // Ignore: BakeMesh only reads a mesh id and cannot meaningfully fault here.
        }
      }

      pending.Clear();
      inFlight.Clear();

      for (int i = 0; i < deferredDestroy.Count; i++)
      {
        DestroyMeshNow(deferredDestroy[i]);
      }

      deferredDestroy.Clear();
    }

    private bool IsBaking(Mesh mesh)
    {
      for (int i = 0; i < inFlight.Count; i++)
      {
        if (ReferenceEquals(inFlight[i].Mesh, mesh))
        {
          return true;
        }
      }

      foreach (BakeJob job in pending)
      {
        if (ReferenceEquals(job.Mesh, mesh))
        {
          return true;
        }
      }

      return false;
    }

    private static void DestroyMeshNow(Mesh mesh)
    {
      if (mesh == null)
      {
        return;
      }

      if (Application.isPlaying)
      {
        Object.Destroy(mesh);
      }
      else
      {
        Object.DestroyImmediate(mesh);
      }
    }
  }
}
