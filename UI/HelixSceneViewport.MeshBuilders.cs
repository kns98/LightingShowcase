// -----------------------------------------------------------------------------
// File: UI/HelixSceneViewport.MeshBuilders.cs
// Purpose: Converts scene geometry into Helix/WPF mesh models.
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using LightingShowcase.SceneGraph;
using SceneMaterial = LightingShowcase.SceneGraph.Material;
using SceneTriangle = LightingShowcase.SceneGraph.Triangle;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using WpfMaterial = System.Windows.Media.Media3D.Material;

namespace LightingShowcase.UI;

public sealed partial class HelixSceneViewport
{
    private static readonly ConcurrentDictionary<string, WpfBrush> ViewportBrushCache = new();

    /// <summary>Converts one scene object group into one or more Helix/WPF geometry models.</summary>
    private IEnumerable<GeometryModel3D> BuildModels(SceneObjectGroup group, bool selected)
    {
        // Keep the root group's transform out of the mesh vertices.  The WPF
        // GeometryModel3D receives the root transform through model.Transform.
        // This prevents the classic group-edit bug where geometry is rebuilt with
        // rotation already baked into vertices and then the live preview transform
        // applies the same rotation/scale again.  Child transforms are still baked
        // into the root display mesh because the editor stores one viewport model
        // collection per selectable top-level group.
        List<SceneTriangle> triangles = BuildViewportBaseTriangles(group).ToList();
        if (triangles.Count == 0)
            yield break;

        Transform3D rootTransform = CreateGroupTransform(group);

        // WPF GeometryModel3D supports one material per mesh.  glTF files can use
        // multiple materials inside one logical node/primitive, and COLOR_0 import
        // creates per-triangle material colors.  Splitting the preview model by
        // material keeps Helix colors aligned with the raytracer instead of showing
        // the first material across the whole object.
        foreach (IGrouping<string, SceneTriangle> materialGroup in triangles.GroupBy(t => ViewportMaterialKey(t.Material)))
        {
            List<SceneTriangle> materialTriangles = materialGroup.ToList();
            if (materialTriangles.Count == 0)
                continue;

            MeshGeometry3D mesh = new();
            Point3DCollection positions = new(materialTriangles.Count * 3);
            Int32Collection indices = new(materialTriangles.Count * 3);
            PointCollection textureCoordinates = new(materialTriangles.Count * 3);

            int index = 0;
            foreach (SceneTriangle tri in materialTriangles)
            {
                positions.Add(ToPoint(tri.A));
                positions.Add(ToPoint(tri.B));
                positions.Add(ToPoint(tri.C));
                textureCoordinates.Add(ToPoint(tri.UvA));
                textureCoordinates.Add(ToPoint(tri.UvB));
                textureCoordinates.Add(ToPoint(tri.UvC));
                indices.Add(index++);
                indices.Add(index++);
                indices.Add(index++);
            }

            mesh.Positions = positions;
            mesh.TextureCoordinates = textureCoordinates;
            mesh.TriangleIndices = indices;

            SceneMaterial firstMaterial = materialTriangles[0].Material;
            WpfMaterial material = CreateViewportMaterial(firstMaterial, selected);

            yield return new GeometryModel3D
            {
                Geometry = mesh,
                Material = material,
                BackMaterial = material,
                Transform = rootTransform
            };
        }
    }


    /// <summary>
    /// Builds viewport triangles with the selectable root transform intentionally
    /// omitted. The root transform is applied by GeometryModel3D.Transform so live
    /// group rotation never changes mesh proportions and never double-applies.
    /// </summary>
    private static IEnumerable<SceneTriangle> BuildViewportBaseTriangles(SceneObjectGroup group, bool includeHidden = false)
    {
        if (!includeHidden && !group.Visible)
            yield break;

        foreach (SceneTriangle tri in group.LocalTriangles)
        {
            SceneMaterial material = group.ColorOverride ?? tri.Material;
            yield return new SceneTriangle(tri.A, tri.B, tri.C, tri.UvA, tri.UvB, tri.UvC, material, group.Id);
        }

        foreach (SceneObjectGroup child in group.Children)
        {
            foreach (SceneTriangle childTri in child.BuildWorldTriangles(includeHidden))
            {
                SceneMaterial material = group.ColorOverride ?? childTri.Material;
                yield return new SceneTriangle(
                    childTri.A, childTri.B, childTri.C,
                    childTri.UvA, childTri.UvB, childTri.UvC,
                    material,
                    group.Id);
            }
        }
    }

    /// <summary>Creates a WPF material that mirrors raytracer texture/color state in the Helix preview.</summary>
    private static WpfMaterial CreateViewportMaterial(SceneMaterial material, bool selected)
    {
        MaterialGroup group = new();
        WpfBrush brush = CreateViewportBrush(material);
        group.Children.Add(new DiffuseMaterial(brush));
        if (selected)
            group.Children.Add(new EmissiveMaterial(new SolidColorBrush(WpfColors.Gold)));
        if (group.CanFreeze)
            group.Freeze();
        return group;
    }

    private static WpfMaterial CreateSelectedViewportMaterial(WpfMaterial baseMaterial)
    {
        MaterialGroup group = new();
        group.Children.Add(baseMaterial);
        group.Children.Add(new EmissiveMaterial(new SolidColorBrush(WpfColors.Gold)));
        if (group.CanFreeze)
            group.Freeze();
        return group;
    }

    private static WpfBrush CreateViewportBrush(SceneMaterial material)
    {
        if (material.Texture == null)
        {
            SolidColorBrush brush = new(ToColor(material.Color, material.SampleAlpha(0.5, 0.5)));
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }

        string key = TextureBrushKey(material.Texture);
        return ViewportBrushCache.GetOrAdd(key, _ => CreateViewportTextureBrush(material));
    }

    private static WpfBrush CreateViewportTextureBrush(SceneMaterial material)
    {
        try
        {
            using Bitmap bitmap = material.Texture!.CreateBitmap();
            using MemoryStream stream = new();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            stream.Position = 0;

            BitmapImage image = new();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            ImageBrush brush = new(image)
            {
                // MeshGeometry3D.TextureCoordinates are normalized UVs.  Using
                // an absolute viewport makes WPF treat the bitmap like a screen-space
                // image tile, which breaks atlas UVs from glTF files such as Lantern.
                // A relative 0..1 viewport keeps authored atlas coordinates intact.
                TileMode = TileMode.Tile,
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                Viewport = new Rect(0, 0, 1, 1),
                Stretch = Stretch.Fill
            };
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }
        catch
        {
            SolidColorBrush fallback = new(ToColor(material.Color, material.SampleAlpha(0.5, 0.5)));
            if (fallback.CanFreeze) fallback.Freeze();
            return fallback;
        }
    }

    private static string TextureBrushKey(TextureMap texture) =>
        FormattableString.Invariant($"{texture.Name}|{texture.SourcePath}|{texture.Width}x{texture.Height}|checker={texture.IsBuiltInChecker}");

    private static string ViewportMaterialKey(SceneMaterial material) =>
        FormattableString.Invariant($"{material.Color.X:F4}|{material.Color.Y:F4}|{material.Color.Z:F4}|{material.Emission:F4}|alpha={material.Alpha:F4}|blend={material.AlphaBlend}|metal={material.Metallic:F4}|rough={material.Roughness:F4}|trans={material.Transmission:F4}|{material.Texture?.Name ?? string.Empty}|{material.Texture?.SourcePath ?? string.Empty}");

    private static System.Windows.Point ToPoint(LightingShowcase.Math3D.Vec2 uv) => new(uv.U, uv.V);
}
