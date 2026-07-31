// -----------------------------------------------------------------------------
// File: Scene/SceneObjectGroup.cs
// Purpose: Editable recursive object group.
//
// A group can now contain triangles and child groups. Top-level groups are shown
// in the editor as selectable objects; ungrouping promotes child groups back into
// the scene so compound props such as tables can be edited as legs/top pieces.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Editable scene node containing local triangles, transform state, and optional child groups.</summary>
public sealed class SceneObjectGroup
{
    public int Id { get; }
    public string Name { get; set; }
    public List<Triangle> LocalTriangles { get; } = new();
    public List<SceneObjectGroup> Children { get; } = new();
    public SceneObjectGroup? Parent { get; internal set; }
    public Vec3 Pivot { get; private set; }
    public Vec3 Position { get; set; } = Vec3.Zero;
    public Vec3 Rotation { get; set; } = Vec3.Zero;
    public Vec3 Scale { get; set; } = new(1, 1, 1);
    public Material? ColorOverride { get; set; }

    /// <summary>
    /// Semantic primitive identifier used by native scene serializers.
    /// Examples: cuboid, rectangle, sphere, cylinder, cone, torus, capsule.
    /// Empty/null means the object is stored as ordinary mesh geometry.
    /// </summary>
    public string? PrimitiveKind { get; set; }

    /// <summary>
    /// Original menu/library primitive name used to rebuild procedural objects when saving/loading.
    /// This is intentionally optional so imported meshes remain simple named mesh objects.
    /// </summary>
    public string? PrimitiveSourceName { get; set; }

    /// <summary>Authored primitive parameters. For editor-created objects this is the real model; LocalTriangles are only the render/pick shadow mesh.</summary>
    public Dictionary<string, double> PrimitiveParameters { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when this object can be regenerated from PrimitiveKind + PrimitiveParameters.</summary>
    public bool HasParametricPrimitive => !string.IsNullOrWhiteSpace(PrimitiveKind) && PrimitiveParameters.Count > 0;


    /// <summary>Returns editor metadata describing how gizmos should change authored parameters for this object.</summary>
    public PrimitiveGizmoEditMetadata GetGizmoEditMetadata()
    {
        return ScenePrimitiveRegistry.Find(PrimitiveKind ?? PrimitiveSourceName) is ISceneObjectDefinition definition
            ? definition.GizmoMetadata
            : PrimitiveGizmoEditMetadata.MeshFallback;
    }


    /// <summary>Applies an incremental gizmo move directly to authored primitive origin parameters.</summary>
    public bool ApplyParametricMoveDelta(Vec3 delta)
    {
        return HasParametricPrimitive
            && Children.Count == 0
            && ScenePrimitiveRegistry.Find(PrimitiveKind ?? PrimitiveSourceName) is ISceneObjectDefinition definition
            && definition.ApplyMoveDelta(PrimitiveParameters, delta);
    }


    /// <summary>Applies an incremental gizmo scale directly to authored primitive size parameters.</summary>
    public bool ApplyParametricScaleDelta(char axis, double factor)
    {
        factor = SanitizeScale(factor);
        return Math.Abs(factor - 1.0) > 1e-12
            && HasParametricPrimitive
            && Children.Count == 0
            && ScenePrimitiveRegistry.Find(PrimitiveKind ?? PrimitiveSourceName) is ISceneObjectDefinition definition
            && definition.ApplyScaleDelta(PrimitiveParameters, axis, factor);
    }


    /// <summary>
    /// Applies pending move/scale gizmo transforms to authored primitive parameters.
    /// Returns true when the shadow mesh should be regenerated from parameters.
    /// Rotation intentionally remains as object transform metadata because most
    /// primitive definitions do not have intrinsic rotation fields.
    /// </summary>
    public bool ApplyPendingTransformToPrimitiveParameters()
    {
        if (!HasParametricPrimitive || Children.Count > 0)
            return false;

        if (ScenePrimitiveRegistry.Find(PrimitiveKind ?? PrimitiveSourceName) is not ISceneObjectDefinition definition)
            return false;

        bool changed = definition.ApplyPendingTransform(PrimitiveParameters, Position, Scale);
        if (changed)
        {
            Position = Vec3.Zero;
            Scale = new Vec3(1, 1, 1);
        }

        return changed;
    }


    private bool AddParameter(string key, double delta)
    {
        if (!double.IsFinite(delta) || Math.Abs(delta) <= 1e-12)
            return false;
        PrimitiveParameters[key] = ReadParameter(key, 0.0) + delta;
        return true;
    }

    private bool MultiplyParameter(string key, double factor)
    {
        if (!PrimitiveParameters.ContainsKey(key) || !double.IsFinite(factor) || Math.Abs(factor - 1.0) <= 1e-12)
            return false;
        PrimitiveParameters[key] = Math.Max(1e-6, ReadParameter(key, 1.0) * factor);
        return true;
    }

    private double ReadParameter(string key, double fallback) =>
        PrimitiveParameters.TryGetValue(key, out double value) && double.IsFinite(value) ? value : fallback;

    private static double SanitizeScale(double value) => double.IsFinite(value) && value > 1e-6 ? value : 1.0;

    private static string NormalizePrimitiveKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    /// <summary>Controls whether this group and its descendants participate in preview, raytracing, export, and bounds.</summary>
    public bool Visible { get; set; } = true;
    public bool IsSelectable { get; }

    public bool HasChildren => Children.Count > 0;
    public bool HasLocalGeometry => LocalTriangles.Count > 0;

    public SceneObjectGroup(int id, string name, bool selectable = true)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? $"Object {id}" : name;
        IsSelectable = selectable;
    }

    public void AddChild(SceneObjectGroup child)
    {
        if (child == null) throw new ArgumentNullException(nameof(child));
        if (ReferenceEquals(child, this)) throw new InvalidOperationException("A group cannot be parented to itself.");
        if (child.ContainsDescendant(Id)) throw new InvalidOperationException("A group cannot be parented below one of its descendants.");

        child.Parent?.Children.Remove(child);
        child.Parent = this;
        Children.Add(child);
        RecalculatePivot();
    }

    public bool RemoveChild(SceneObjectGroup child)
    {
        if (!Children.Remove(child)) return false;
        child.Parent = null;
        RecalculatePivot();
        return true;
    }

    public IEnumerable<SceneObjectGroup> SelfAndDescendants()
    {
        yield return this;
        foreach (SceneObjectGroup child in Children)
        {
            foreach (SceneObjectGroup descendant in child.SelfAndDescendants())
                yield return descendant;
        }
    }

    public bool ContainsDescendant(int id) => SelfAndDescendants().Any(g => g.Id == id);

    public void AddTriangle(Vec3 a, Vec3 b, Vec3 c, Material material)
    {
        AddTriangle(a, b, c, new Vec2(0, 0), new Vec2(1, 0), new Vec2(0, 1), material);
    }

    public void AddTriangle(Vec3 a, Vec3 b, Vec3 c, Vec2 uvA, Vec2 uvB, Vec2 uvC, Material material)
    {
        LocalTriangles.Add(new Triangle(a, b, c, uvA, uvB, uvC, material, Id));
    }

    public void AddTriangle(
        Vec3 a, Vec3 b, Vec3 c,
        Vec2 uvA, Vec2 uvB, Vec2 uvC,
        Vec3 normalA, Vec3 normalB, Vec3 normalC,
        Material material)
    {
        LocalTriangles.Add(new Triangle(a, b, c, uvA, uvB, uvC, normalA, normalB, normalC, material, Id));
    }

    public void RecalculatePivot()
    {
        List<Vec3> points = new();
        foreach (Triangle tri in LocalTriangles)
        {
            points.Add(tri.A); points.Add(tri.B); points.Add(tri.C);
        }

        foreach (SceneObjectGroup child in Children)
        {
            Aabb childBounds = child.GetWorldBounds();
            points.Add(childBounds.Min);
            points.Add(childBounds.Max);
        }

        if (points.Count == 0)
        {
            Pivot = Vec3.Zero;
            return;
        }

        Vec3 min = points[0], max = points[0];
        foreach (Vec3 point in points)
        {
            min = Min(min, point);
            max = Max(max, point);
        }
        Pivot = (min + max) * 0.5;
    }

    /// <summary>Bakes this group's pending transform into all contained geometry, including descendants.</summary>
    public void BakeCurrentTransform()
    {
        foreach (SceneObjectGroup child in Children)
            child.BakeCurrentTransform();

        if (HasPendingTransform())
            ApplyPointTransformRecursively(TransformPoint, TransformNormal);

        ResetTransform();
        foreach (SceneObjectGroup child in Children)
            child.RecalculatePivot();
        RecalculatePivot();
    }

    public void ApplyColor(Material material)
    {
        if (material == null) throw new ArgumentNullException(nameof(material));

        BakeCurrentTransform();
        ApplyMaterialRecursively(tri =>
        {
            Material updated = new(
                material.Color,
                tri.Material.Emission,
                tri.Material.LightId,
                tri.Material.Texture,
                tri.Material.EmissionColor,
                tri.Material.EmissiveTexture,
                tri.Material.Alpha,
                tri.Material.AlphaBlend,
                tri.Material.Metallic,
                tri.Material.Roughness,
                tri.Material.Transmission,
                tri.Material.MetallicRoughnessTexture,
                tri.Material.NormalTexture,
                tri.Material.OcclusionTexture,
                tri.Material.NormalScale,
                tri.Material.OcclusionStrength,
                tri.Material.AlphaMode,
                tri.Material.AlphaCutoff,
                tri.Material.DoubleSided);
            return new Triangle(tri.A, tri.B, tri.C, tri.UvA, tri.UvB, tri.UvC, tri.NormalA, tri.NormalB, tri.NormalC, updated, tri.GroupId);
        });
        ColorOverride = null;
        RecalculatePivot();
    }

    public void ApplyTexture(TextureMap texture)
    {
        ApplyTexture(texture, TextureRepeatWorldUnits, forceBoxProjection: true);
    }

    /// <summary>Assigns a texture and projects UVs in scene units so long faces tile instead of stretching one bitmap copy.</summary>
    public void ApplyTexture(TextureMap texture, double tileWorldUnits, bool forceBoxProjection = true)
    {
        if (texture == null) throw new ArgumentNullException(nameof(texture));

        BakeCurrentTransform();
        Aabb bounds = GetWorldBounds();
        double safeTileWorldUnits = SanitizeTileWorldUnits(tileWorldUnits);
        ApplyMaterialRecursively(tri =>
        {
            Material updated = new(
                tri.Material.Color,
                tri.Material.Emission,
                tri.Material.LightId,
                texture,
                tri.Material.EmissionColor,
                tri.Material.EmissiveTexture,
                tri.Material.Alpha,
                tri.Material.AlphaBlend,
                tri.Material.Metallic,
                tri.Material.Roughness,
                tri.Material.Transmission,
                tri.Material.MetallicRoughnessTexture,
                tri.Material.NormalTexture,
                tri.Material.OcclusionTexture,
                tri.Material.NormalScale,
                tri.Material.OcclusionStrength,
                tri.Material.AlphaMode,
                tri.Material.AlphaCutoff,
                tri.Material.DoubleSided);

            // Manual replacement textures should preserve authored atlas UVs
            // from OBJ/glTF imports.  Editor-created primitives normally still
            // have the default unit triangle UVs, so they fall through to box
            // projection and tile like before.
            if (!forceBoxProjection && !HasDefaultUnitUvs(tri))
                return new Triangle(tri.A, tri.B, tri.C, tri.UvA, tri.UvB, tri.UvC, tri.NormalA, tri.NormalB, tri.NormalC, updated, tri.GroupId);

            return new Triangle(
                tri.A, tri.B, tri.C,
                GenerateBoxUv(tri.A, tri.Normal, bounds, safeTileWorldUnits),
                GenerateBoxUv(tri.B, tri.Normal, bounds, safeTileWorldUnits),
                GenerateBoxUv(tri.C, tri.Normal, bounds, safeTileWorldUnits),
                tri.NormalA, tri.NormalB, tri.NormalC,
                updated,
                tri.GroupId);
        });
        ColorOverride = null;
        RecalculatePivot();
    }

    /// <summary>Reprojects existing textured triangles using a chosen scene-unit tile size.</summary>
    public void RetileTexture(double tileWorldUnits)
    {
        BakeCurrentTransform();
        Aabb bounds = GetWorldBounds();
        double safeTileWorldUnits = SanitizeTileWorldUnits(tileWorldUnits);
        ApplyMaterialRecursively(tri =>
        {
            if (tri.Material.Texture == null)
                return tri;

            return new Triangle(
                tri.A, tri.B, tri.C,
                GenerateBoxUv(tri.A, tri.Normal, bounds, safeTileWorldUnits),
                GenerateBoxUv(tri.B, tri.Normal, bounds, safeTileWorldUnits),
                GenerateBoxUv(tri.C, tri.Normal, bounds, safeTileWorldUnits),
                tri.NormalA, tri.NormalB, tri.NormalC,
                tri.Material,
                tri.GroupId);
        });
        ColorOverride = null;
        RecalculatePivot();
    }


    /// <summary>Counts local mesh triangles in this group and every child group.</summary>
    public int CountLocalTrianglesRecursively()
    {
        int count = LocalTriangles.Count;
        foreach (SceneObjectGroup child in Children)
            count += child.CountLocalTrianglesRecursively();
        return count;
    }

    /// <summary>
    /// Reduces triangle count in this group and its descendants using a fast
    /// spatial decimator.  Transforms are baked first so simplification operates
    /// on the visible object, and materials/UVs remain attached to retained
    /// triangles.
    /// </summary>
    public int SimplifyGeometry(double keepFraction)
    {
        BakeCurrentTransform();
        int before = CountLocalTrianglesRecursively();
        SimplifyLocalGeometryRecursively(keepFraction);
        RecalculatePivot();
        return before - CountLocalTrianglesRecursively();
    }

    private void SimplifyLocalGeometryRecursively(double keepFraction)
    {
        if (LocalTriangles.Count > 3)
        {
            List<Triangle> simplified = MeshSimplifier.Simplify(LocalTriangles, keepFraction);
            LocalTriangles.Clear();
            LocalTriangles.AddRange(simplified);
        }

        foreach (SceneObjectGroup child in Children)
            child.SimplifyLocalGeometryRecursively(keepFraction);
    }

    public void ClearTexture()
    {
        BakeCurrentTransform();
        ApplyMaterialRecursively(tri =>
        {
            Material updated = tri.Material.WithTexture(null);
            return new Triangle(tri.A, tri.B, tri.C, tri.UvA, tri.UvB, tri.UvC, tri.NormalA, tri.NormalB, tri.NormalC, updated, tri.GroupId);
        });
        ColorOverride = null;
        RecalculatePivot();
    }

    /// <summary>Applies a material transformer to every local triangle in this group and its descendants.</summary>
    public void ApplyMaterialProperties(Func<Material, Material> materialTransform)
    {
        if (materialTransform == null) throw new ArgumentNullException(nameof(materialTransform));

        BakeCurrentTransform();
        ApplyMaterialRecursively(tri =>
        {
            Material updated = materialTransform(tri.Material);
            return ReferenceEquals(updated, tri.Material)
                ? tri
                : new Triangle(tri.A, tri.B, tri.C, tri.UvA, tri.UvB, tri.UvC, tri.NormalA, tri.NormalB, tri.NormalC, updated, tri.GroupId);
        });
        ColorOverride = null;
        RecalculatePivot();
    }

    /// <summary>Returns the first material found in this group or any descendant.</summary>
    public Material? FirstMaterialOrDefault()
    {
        foreach (SceneObjectGroup group in SelfAndDescendants())
        {
            if (group.LocalTriangles.Count > 0)
                return group.LocalTriangles[0].Material;
        }

        return null;
    }

    public Aabb GetWorldBounds(bool includeHidden = false)
    {
        bool hasPoint = false;
        Vec3 min = Vec3.Zero;
        Vec3 max = Vec3.Zero;

        foreach (Triangle tri in BuildWorldTriangles(includeHidden))
        {
            if (!hasPoint)
            {
                min = tri.A;
                max = tri.A;
                hasPoint = true;
            }

            AddPoint(tri.A, ref min, ref max);
            AddPoint(tri.B, ref min, ref max);
            AddPoint(tri.C, ref min, ref max);
        }

        return hasPoint ? new Aabb(min, max) : new Aabb(Vec3.Zero, Vec3.Zero);
    }

    /// <summary>Builds visible world triangles for this whole recursive group, using this group's id as the selectable owner.</summary>
    public IEnumerable<Triangle> BuildWorldTriangles() => BuildWorldTriangles(includeHidden: false);

    /// <summary>Builds world triangles, optionally including hidden groups for inspector/detail calculations.</summary>
    public IEnumerable<Triangle> BuildWorldTriangles(bool includeHidden)
    {
        if (!includeHidden && !Visible)
            yield break;

        foreach (Triangle tri in LocalTriangles)
        {
            Material material = ColorOverride ?? tri.Material;
            yield return new Triangle(
                TransformPoint(tri.A), TransformPoint(tri.B), TransformPoint(tri.C),
                tri.UvA, tri.UvB, tri.UvC,
                TransformNormal(tri.NormalA), TransformNormal(tri.NormalB), TransformNormal(tri.NormalC),
                material, Id);
        }

        foreach (SceneObjectGroup child in Children)
        {
            foreach (Triangle childTri in child.BuildWorldTriangles(includeHidden))
            {
                Material material = ColorOverride ?? childTri.Material;
                yield return new Triangle(
                    TransformPoint(childTri.A),
                    TransformPoint(childTri.B),
                    TransformPoint(childTri.C),
                    childTri.UvA,
                    childTri.UvB,
                    childTri.UvC,
                    TransformNormal(childTri.NormalA),
                    TransformNormal(childTri.NormalB),
                    TransformNormal(childTri.NormalC),
                    material,
                    Id);
            }
        }
    }

    public Vec3 TransformPoint(Vec3 p)
    {
        return TransformConverter.ApplySrt(p, Pivot, Position, Rotation, Scale);
    }

    public Vec3 TransformNormal(Vec3 normal)
    {
        return TransformConverter.ApplySrtNormal(normal, Rotation, Scale);
    }

    private bool HasPendingTransform() =>
        Position.Length() > 1e-12 || Rotation.Length() > 1e-12 ||
        Math.Abs(Scale.X - 1.0) > 1e-12 || Math.Abs(Scale.Y - 1.0) > 1e-12 || Math.Abs(Scale.Z - 1.0) > 1e-12;

    private void ApplyPointTransformRecursively(Func<Vec3, Vec3> pointTransform, Func<Vec3, Vec3> normalTransform)
    {
        for (int i = 0; i < LocalTriangles.Count; i++)
        {
            Triangle tri = LocalTriangles[i];
            LocalTriangles[i] = new Triangle(
                pointTransform(tri.A), pointTransform(tri.B), pointTransform(tri.C),
                tri.UvA, tri.UvB, tri.UvC,
                normalTransform(tri.NormalA), normalTransform(tri.NormalB), normalTransform(tri.NormalC),
                tri.Material, tri.GroupId);
        }

        foreach (SceneObjectGroup child in Children)
            child.ApplyPointTransformRecursively(pointTransform, normalTransform);
    }

    private void ApplyMaterialRecursively(Func<Triangle, Triangle> transform)
    {
        for (int i = 0; i < LocalTriangles.Count; i++)
            LocalTriangles[i] = transform(LocalTriangles[i]);

        foreach (SceneObjectGroup child in Children)
            child.ApplyMaterialRecursively(transform);
    }


    private const double TextureRepeatWorldUnits = 0.25;

    private static bool HasDefaultUnitUvs(Triangle tri) =>
        IsUnitUv(tri.UvA) && IsUnitUv(tri.UvB) && IsUnitUv(tri.UvC);

    private static bool IsUnitUv(Vec2 value) =>
        IsZeroOrOne(value.U) && IsZeroOrOne(value.V);

    private static bool IsZeroOrOne(double value) =>
        Math.Abs(value) < 1e-9 || Math.Abs(value - 1.0) < 1e-9;

    private static Vec2 GenerateBoxUv(Vec3 point, Vec3 normal, Aabb bounds, double tileWorldUnits)
    {
        double nx = Math.Abs(normal.X), ny = Math.Abs(normal.Y), nz = Math.Abs(normal.Z);

        // Project onto the dominant plane for the face normal.  Coordinates are
        // converted to scene-space tile units rather than normalized to 0..1, so
        // large faces show repeated texture copies instead of one stretched copy.
        if (ny >= nx && ny >= nz)
            return new Vec2(ToTileCoordinate(point.X, bounds.Min.X, tileWorldUnits), ToTileCoordinate(point.Z, bounds.Min.Z, tileWorldUnits));
        if (nx >= ny && nx >= nz)
            return new Vec2(ToTileCoordinate(point.Z, bounds.Min.Z, tileWorldUnits), ToTileCoordinate(point.Y, bounds.Min.Y, tileWorldUnits));
        return new Vec2(ToTileCoordinate(point.X, bounds.Min.X, tileWorldUnits), ToTileCoordinate(point.Y, bounds.Min.Y, tileWorldUnits));
    }

    private static double ToTileCoordinate(double value, double origin, double tileWorldUnits) =>
        (value - origin) / tileWorldUnits;

    private static double SanitizeTileWorldUnits(double tileWorldUnits) =>
        double.IsFinite(tileWorldUnits) && tileWorldUnits > 1e-6 ? tileWorldUnits : TextureRepeatWorldUnits;

    private void ResetTransform()
    {
        Position = Vec3.Zero;
        Rotation = Vec3.Zero;
        Scale = new Vec3(1, 1, 1);
        ColorOverride = null;
    }

    private static Vec3 Rotate(Vec3 p, Vec3 r)
    {
        double cx = Math.Cos(r.X), sx = Math.Sin(r.X);
        double cy = Math.Cos(r.Y), sy = Math.Sin(r.Y);
        double cz = Math.Cos(r.Z), sz = Math.Sin(r.Z);

        Vec3 x = new(p.X, p.Y * cx - p.Z * sx, p.Y * sx + p.Z * cx);
        Vec3 y = new(x.X * cy + x.Z * sy, x.Y, -x.X * sy + x.Z * cy);
        return new Vec3(y.X * cz - y.Y * sz, y.X * sz + y.Y * cz, y.Z);
    }

    private static void AddPoint(Vec3 p, ref Vec3 min, ref Vec3 max)
    {
        min = Min(min, p);
        max = Max(max, p);
    }

    private static Vec3 Min(Vec3 a, Vec3 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    private static Vec3 Max(Vec3 a, Vec3 b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
}
