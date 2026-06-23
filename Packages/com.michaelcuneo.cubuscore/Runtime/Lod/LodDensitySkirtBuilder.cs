using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Meshing;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Lod
{
  /// <summary>
  /// Appends downward "curtain" skirts to a meshed SmoothDensity LOD tile so the
  /// cracks between adjacent LOD bands (which mesh the surface at different
  /// resolutions) are hidden.
  ///
  /// The block skirt builder works off the voxel grid, but a marching-cubes
  /// surface has no axis-aligned columns, so this operates on the finished mesh
  /// instead: any triangle edge whose two endpoints both lie on one of the four
  /// vertical tile-boundary planes (x=0, x=size, z=0, z=size) is a boundary edge
  /// of the surface on that face. Extruding each such edge straight down by the
  /// skirt depth produces a continuous wall that plugs the gap to the neighbouring
  /// band. The curtains are double-sided so they hide the seam regardless of which
  /// side of the boundary sits higher or where the viewer is.
  /// </summary>
  public static class LodDensitySkirtBuilder
  {
    /// <param name="tileWorldSize">World-space size of the tile along one axis (numCellsAxis * cellWorldSize).</param>
    /// <param name="skirtDepthWorld">How far down, in world units, each curtain hangs.</param>
    public static void AppendSkirts(MeshData mesh, float tileWorldSize, float skirtDepthWorld)
    {
      if (mesh == null || skirtDepthWorld <= 0.0f || tileWorldSize <= 0.0f)
      {
        return;
      }

      float epsilon = Mathf.Max(1e-4f, tileWorldSize * 1e-4f);

      // Snapshot the triangle count up front: we append new triangles as we go and
      // must only iterate the original surface triangles.
      int originalTriangleCount = mesh.Triangles.Count;

      for (int t = 0; t + 2 < originalTriangleCount; t += 3)
      {
        int a = mesh.Triangles[t];
        int b = mesh.Triangles[t + 1];
        int c = mesh.Triangles[t + 2];

        TryAppendEdgeCurtain(mesh, a, b, tileWorldSize, skirtDepthWorld, epsilon);
        TryAppendEdgeCurtain(mesh, b, c, tileWorldSize, skirtDepthWorld, epsilon);
        TryAppendEdgeCurtain(mesh, c, a, tileWorldSize, skirtDepthWorld, epsilon);
      }
    }

    private static void TryAppendEdgeCurtain(
        MeshData mesh,
        int i0,
        int i1,
        float tileWorldSize,
        float skirtDepthWorld,
        float epsilon)
    {
      Vector3 p0 = mesh.Vertices[i0];
      Vector3 p1 = mesh.Vertices[i1];

      Vector3 outwardNormal;

      if (Mathf.Abs(p0.x) <= epsilon && Mathf.Abs(p1.x) <= epsilon)
      {
        outwardNormal = new Vector3(-1.0f, 0.0f, 0.0f);
      }
      else if (Mathf.Abs(p0.x - tileWorldSize) <= epsilon && Mathf.Abs(p1.x - tileWorldSize) <= epsilon)
      {
        outwardNormal = new Vector3(1.0f, 0.0f, 0.0f);
      }
      else if (Mathf.Abs(p0.z) <= epsilon && Mathf.Abs(p1.z) <= epsilon)
      {
        outwardNormal = new Vector3(0.0f, 0.0f, -1.0f);
      }
      else if (Mathf.Abs(p0.z - tileWorldSize) <= epsilon && Mathf.Abs(p1.z - tileWorldSize) <= epsilon)
      {
        outwardNormal = new Vector3(0.0f, 0.0f, 1.0f);
      }
      else
      {
        return;
      }

      Color32 c0 = mesh.Colors[i0];
      Color32 c1 = mesh.Colors[i1];

      Vector3 drop = new(0.0f, skirtDepthWorld, 0.0f);
      Vector3 b0 = p0 - drop;
      Vector3 b1 = p1 - drop;

      // Double-sided so the curtain hides the seam from either side without
      // depending on the surface winding.
      AppendQuad(mesh, p0, p1, b1, b0, outwardNormal, c0, c1, true);
      AppendQuad(mesh, p0, p1, b1, b0, -outwardNormal, c0, c1, false);
    }

    private static void AppendQuad(
        MeshData mesh,
        Vector3 topA,
        Vector3 topB,
        Vector3 bottomB,
        Vector3 bottomA,
        Vector3 normal,
        Color32 colorA,
        Color32 colorB,
        bool frontWinding)
    {
      int baseIndex = mesh.Vertices.Count;

      mesh.Vertices.Add(topA);
      mesh.Normals.Add(normal);
      mesh.UVs.Add(ProjectUv(topA, normal));
      mesh.Colors.Add(colorA);

      mesh.Vertices.Add(topB);
      mesh.Normals.Add(normal);
      mesh.UVs.Add(ProjectUv(topB, normal));
      mesh.Colors.Add(colorB);

      mesh.Vertices.Add(bottomB);
      mesh.Normals.Add(normal);
      mesh.UVs.Add(ProjectUv(bottomB, normal));
      mesh.Colors.Add(colorB);

      mesh.Vertices.Add(bottomA);
      mesh.Normals.Add(normal);
      mesh.UVs.Add(ProjectUv(bottomA, normal));
      mesh.Colors.Add(colorA);

      if (frontWinding)
      {
        mesh.Triangles.Add(baseIndex + 0);
        mesh.Triangles.Add(baseIndex + 1);
        mesh.Triangles.Add(baseIndex + 2);
        mesh.Triangles.Add(baseIndex + 0);
        mesh.Triangles.Add(baseIndex + 2);
        mesh.Triangles.Add(baseIndex + 3);
      }
      else
      {
        mesh.Triangles.Add(baseIndex + 0);
        mesh.Triangles.Add(baseIndex + 2);
        mesh.Triangles.Add(baseIndex + 1);
        mesh.Triangles.Add(baseIndex + 0);
        mesh.Triangles.Add(baseIndex + 3);
        mesh.Triangles.Add(baseIndex + 2);
      }
    }

    // Matches DensityMeshDataBuilder.ProjectUv for side faces so the curtain
    // samples the biome atlas the same way as the surface it hangs from.
    private static Vector2 ProjectUv(Vector3 position, Vector3 normal)
    {
      return Mathf.Abs(normal.x) >= Mathf.Abs(normal.z)
          ? new Vector2(position.z, position.y)
          : new Vector2(position.x, position.y);
    }
  }
}
