using System;
using System.Collections.Generic;
using CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.World;
using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Editing
{
  /// <summary>
  /// The voxel engine's terrain-editing entry point. External systems (an inventory, a hotbar,
  /// AI, scripted events, ...) hand it a <see cref="CubusEditToolDefinition"/> and a ray and it
  /// performs the edit against the block or density layer. The engine owns NO tools, keys,
  /// selection or inventory state — a game equips a tool however it likes (e.g. drop a pickaxe
  /// item into a hand slot) then calls <see cref="Apply(CubusEditToolDefinition, Ray)"/> when used.
  /// </summary>
  public sealed class CubusEditController : MonoBehaviour
  {
    [SerializeField] private BlockEditTool blockTool;
    [SerializeField] private SmoothDensityEditTool densityTool;
    [SerializeField] private float traceDistance = 128.0f;

    /// <summary>
    /// Optional hook to route single-voxel block edits through an authoritative path
    /// (e.g. a networking bridge). When set it receives (worldVoxel, materialId) and its
    /// return value is used; when null, block edits are applied locally.
    /// </summary>
    public Func<Vector3Int, ushort, bool> BlockEditForwarder { get; set; }

    /// <summary>
    /// Optional hook to replicate density sculpt edits. After a density tool applies locally,
    /// the exact set of changed voxels (absolute density + material) is handed here so a
    /// networking bridge can broadcast them per voxel (never whole chunks). Null keeps edits local.
    /// </summary>
    public Func<IReadOnlyList<DensityVoxelEdit>, bool> DensityEditForwarder { get; set; }

    private readonly List<DensityVoxelEdit> densityEditBuffer = new();

    /// <summary>
    /// Convenience slot a driver/inventory can set to the currently held tool, then trigger
    /// with <see cref="ApplyEquipped(Ray)"/>. Purely optional — <see cref="Apply(CubusEditToolDefinition, Ray)"/>
    /// is stateless and can be called directly.
    /// </summary>
    public CubusEditToolDefinition EquippedTool { get; set; }

    private void Awake()
    {
      ResolveTools();
    }

    private void ResolveTools()
    {
      if (blockTool == null || !blockTool.HasWorld)
      {
        blockTool = BlockEditTool.FindWorldEditTool() ?? blockTool;
      }

      if (densityTool == null && blockTool != null)
      {
        // The density tool must live on the world object (it resolves CubusWorld +
        // WorldStreamer from its own GameObject); the world-owning block tool is there.
        densityTool = blockTool.GetComponent<SmoothDensityEditTool>()
            ?? blockTool.gameObject.AddComponent<SmoothDensityEditTool>();
      }
    }

    /// <summary>Applies <see cref="EquippedTool"/> if one is set.</summary>
    public bool ApplyEquipped(Ray ray)
    {
      return EquippedTool != null && Apply(EquippedTool, ray);
    }

    /// <summary>
    /// Performs <paramref name="tool"/>'s edit along <paramref name="ray"/>. Returns true if
    /// terrain changed. Safe to call every frame for continuous tools (the density path
    /// self-throttles via its edit interval).
    /// </summary>
    public bool Apply(CubusEditToolDefinition tool, Ray ray)
    {
      if (tool == null)
      {
        return false;
      }

      if (blockTool == null || densityTool == null)
      {
        ResolveTools();
      }

      return tool.EditsBlockLayer ? ApplyBlock(tool, ray) : ApplyDensity(tool, ray);
    }

    private bool ApplyBlock(CubusEditToolDefinition tool, Ray ray)
    {
      if (blockTool == null)
      {
        return false;
      }

      BlockEditOperation operation = tool.effect == EditToolEffect.PlaceBlock
          ? BlockEditOperation.Add
          : BlockEditOperation.Remove;

      // Sphere block edits are area terraforming and apply locally (the networked
      // forwarder is single-voxel). Point edits are single voxels and route through
      // the forwarder when present so they stay authoritative/synced.
      if (tool.shape == EditToolShape.Sphere && tool.range > 1.0f)
      {
        blockTool.EditShape = BlockEditShape.Sphere;
        blockTool.SphereRadiusVoxels = tool.range;
        if (operation == BlockEditOperation.Add)
        {
          blockTool.AddMaterialId = tool.materialId != 0 ? tool.materialId : (ushort)1;
        }

        return blockTool.TryEditFromRay(ray, traceDistance, operation);
      }

      if (!blockTool.TryResolveEditVoxel(ray, traceDistance, operation, out Vector3Int voxel))
      {
        return false;
      }

      if (!blockTool.CanEditVoxelAt(voxel, operation))
      {
        return false;
      }

      ushort material = operation == BlockEditOperation.Add
          ? (tool.materialId != 0 ? tool.materialId : (ushort)1)
          : (ushort)0;

      if (BlockEditForwarder != null)
      {
        return BlockEditForwarder(voxel, material);
      }

      blockTool.ApplyNetworkVoxelEdit(voxel, material);
      return true;
    }

    private bool ApplyDensity(CubusEditToolDefinition tool, Ray ray)
    {
      if (densityTool == null)
      {
        return false;
      }

      SmoothDensityEditTool.DensityBrush brush = tool.effect switch
      {
        EditToolEffect.Raise => SmoothDensityEditTool.DensityBrush.Raise,
        EditToolEffect.Lower => SmoothDensityEditTool.DensityBrush.Lower,
        EditToolEffect.Smooth => SmoothDensityEditTool.DensityBrush.Smooth,
        EditToolEffect.Flatten => SmoothDensityEditTool.DensityBrush.Flatten,
        _ => SmoothDensityEditTool.DensityBrush.Raise,
      };

      // Apply locally (responsive) while capturing the changed voxels, then hand them to the
      // forwarder so they replicate per voxel. Remote clients re-apply the same absolute values.
      densityEditBuffer.Clear();
      List<DensityVoxelEdit> collector = DensityEditForwarder != null ? densityEditBuffer : null;
      bool changed = densityTool.TryApply(ray, brush, tool.range, tool.strength, tool.materialId, collector);

      if (changed && DensityEditForwarder != null && densityEditBuffer.Count > 0)
      {
        DensityEditForwarder(densityEditBuffer);
      }

      return changed;
    }
  }
}
