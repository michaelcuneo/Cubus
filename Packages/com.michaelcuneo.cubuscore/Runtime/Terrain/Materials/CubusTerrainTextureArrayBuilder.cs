using UnityEngine;

namespace CubusCore.Packages.com.michaelcuneo.cubuscore.Runtime.Terrain.Materials
{
  public static class CubusTerrainTextureArrayBuilder
  {
    public static void Build(
      CubusTerrainMaterialLibrary library,
      out Texture2DArray albedoArray,
      out Texture2DArray normalArray,
      out Texture2DArray maskArray)
    {
      int size = Mathf.Max(16, library.TextureSize);
      int count = Mathf.Max(1, library.MaterialCount);
      bool mipMaps = library.GenerateMipMaps;

      albedoArray = CreateArray(size, count, library.AlbedoFormat, mipMaps);
      normalArray = CreateArray(size, count, library.NormalFormat, mipMaps);
      maskArray = CreateArray(size, count, library.MaskFormat, mipMaps);

      for (int i = 0; i < count; i++)
      {
        CubusTerrainMaterial material =
          library.Materials != null && i < library.Materials.Count
            ? library.Materials[i]
            : null;

        Texture2D albedo = material != null ? material.Albedo : null;
        Texture2D normal = material != null ? material.Normal : null;
        Texture2D mask = material != null ? material.Mask : null;

        CopyTextureOrFallback(
          albedo,
          albedoArray,
          i,
          size,
          new Color(1.0f, 1.0f, 1.0f, 1.0f));

        CopyTextureOrFallback(
          normal,
          normalArray,
          i,
          size,
          new Color(0.5f, 0.5f, 1.0f, 1.0f));

        CopyTextureOrFallback(
          mask,
          maskArray,
          i,
          size,
          new Color(1.0f, 0.8f, 0.0f, 0.0f));
      }

      albedoArray.Apply(mipMaps, false);
      normalArray.Apply(mipMaps, false);
      maskArray.Apply(mipMaps, false);
    }

    private static Texture2DArray CreateArray(
      int size,
      int count,
      TextureFormat format,
      bool mipMaps)
    {
      Texture2DArray array = new(
        size,
        size,
        count,
        format,
        mipMaps,
        false);

      array.wrapMode = TextureWrapMode.Repeat;
      array.filterMode = FilterMode.Trilinear;
      array.anisoLevel = 8;

      return array;
    }

    private static void CopyTextureOrFallback(
      Texture2D source,
      Texture2DArray destination,
      int slice,
      int size,
      Color fallback)
    {
      Texture2D readable = MakeReadableResized(source, size, fallback);
      Color[] pixels = readable.GetPixels();

      destination.SetPixels(
        pixels,
        slice,
        0);

      Object.Destroy(readable);
    }

    private static Texture2D MakeReadableResized(
      Texture2D source,
      int size,
      Color fallback)
    {
      RenderTexture temporary = RenderTexture.GetTemporary(
        size,
        size,
        0,
        RenderTextureFormat.ARGB32,
        RenderTextureReadWrite.Linear);

      RenderTexture previous = RenderTexture.active;

      if (source != null)
      {
        Graphics.Blit(source, temporary);
      }
      else
      {
        Texture2D fallbackTexture = new(size, size, TextureFormat.RGBA32, false);
        Color[] fill = new Color[size * size];

        for (int i = 0; i < fill.Length; i++)
        {
          fill[i] = fallback;
        }

        fallbackTexture.SetPixels(fill);
        fallbackTexture.Apply(false, false);
        Graphics.Blit(fallbackTexture, temporary);
        Object.Destroy(fallbackTexture);
      }

      RenderTexture.active = temporary;

      Texture2D readable = new(size, size, TextureFormat.RGBA32, false);
      readable.ReadPixels(new Rect(0, 0, size, size), 0, 0);
      readable.Apply(false, false);

      RenderTexture.active = previous;
      RenderTexture.ReleaseTemporary(temporary);

      return readable;
    }
  }
}