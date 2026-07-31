// -----------------------------------------------------------------------------
// File: Scene/Scene.cs
// Purpose: Scene root.
//
// Owns all object groups and lights, rebuilds acceleration structures, and exposes add/remove/selection helpers.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.Lighting;

namespace LightingShowcase.SceneGraph;

/// <summary>Root scene container for editable objects, lights, and acceleration structures.</summary>
public sealed class Scene
{
    public List<Triangle> Triangles { get; } = new();
    public List<SceneLight> Lights { get; } = new();
    public List<SceneObjectGroup> ObjectGroups { get; } = new();

    private readonly SceneMaterials materials = new();
    private BvhNode? bvhRoot;
    private readonly Stack<SceneObjectGroup> activeGroups = new();
    private int nextGroupId = 1;

    public string Description { get; private set; } = "Built-in room";

    /// <summary>Updates the human-readable scene/document description from application services.</summary>
    public void SetDescription(string description)
    {
        Description = string.IsNullOrWhiteSpace(description) ? "scene" : description;
    }

    /// <summary>Builds default scene content or acceleration data depending on the owning class.</summary>
    public void Build()
    {
        Clear();
        new SceneBuilder(this, materials).Build();
        Description = "Advanced material showcase room";
        RebuildWorldGeometry();
    }


    /// <summary>Opens any supported static model file as a replacement scene through a format plugin.</summary>
    public ObjLoadResult OpenModelFile(string filePath, Action<ObjLoadProgress>? progress = null)
    {
        ISceneFormatPlugin plugin = SceneFormatRegistry.FindImporter(filePath);
        Clear();
        if (!plugin.CarriesLights)
            AddDefaultObjectViewingLights();

        ObjLoadResult result = plugin.Import(this, filePath, new SceneLoadOptions
        {
            FallbackMaterial = materials.WhiteWall,
            TargetSize = 4.75,
            TargetCenter = new Vec3(0.0, 0.0, 3.55),
            FloorY = -1.45,
            ReplaceScene = true,
            Progress = progress
        });

        Description = $"{plugin.DisplayName}: {Path.GetFileName(filePath)} ({result.TriangleCount} triangles)";
        return result;
    }

    /// <summary>Inserts any supported static model file into the current scene through a format plugin.</summary>
    public ObjLoadResult InsertModelFromFile(string filePath, Action<ObjLoadProgress>? progress = null)
    {
        ISceneFormatPlugin plugin = SceneFormatRegistry.FindImporter(filePath);
        ObjLoadResult result = plugin.Import(this, filePath, new SceneLoadOptions
        {
            FallbackMaterial = materials.WhiteWall,
            Progress = progress
        });

        Description = $"Scene with inserted {plugin.DisplayName}: {Path.GetFileName(filePath)} ({result.TriangleCount} triangles)";
        return result;
    }

    /// <summary>Backward-compatible OBJ open helper routed through the plugin registry.</summary>
    public ObjLoadResult OpenObjFile(string filePath, Action<ObjLoadProgress>? progress = null) => OpenModelFile(filePath, progress);

    /// <summary>Backward-compatible OBJ build helper routed through the plugin registry.</summary>
    public ObjLoadResult BuildFromObjFile(string filePath) => OpenObjFile(filePath);

    /// <summary>Backward-compatible OBJ insert helper routed through the plugin registry.</summary>
    public ObjLoadResult InsertObjFromFile(string filePath, Action<ObjLoadProgress>? progress = null) => InsertModelFromFile(filePath, progress);

    /// <summary>Implements the insert ready made object operation for this file's subsystem.</summary>
    public SceneObjectGroup InsertReadyMadeObject(string objectName)
    {
        SceneObjectGroup group = ObjectLibraryRegistry.Insert(this, materials, objectName);
        Description = $"Scene with inserted ready-made object: {group.Name}";
        RebuildWorldGeometry();
        return group;
    }

    /// <summary>Loads a native XML scene through the format plugin registry.</summary>
    public void LoadPropXmlFile(string filePath)
    {
        Clear();
        ISceneFormatPlugin plugin = SceneFormatRegistry.FindImporter(filePath);
        ObjLoadResult result = plugin.Import(this, filePath, new SceneLoadOptions
        {
            FallbackMaterial = materials.WhiteWall,
            ReplaceScene = true
        });
        Description = $"{plugin.DisplayName}: {Path.GetFileName(filePath)} ({result.TriangleCount} triangles)";
        RebuildWorldGeometry();
    }

    /// <summary>Adds or creates default object viewing lights for this subsystem.</summary>
    private void AddDefaultObjectViewingLights()
    {
        Lights.Add(new SceneLight("ceiling", new Vec3(0.0, 3.25, -0.50), new Vec3(1.0, 0.96, 0.88), 5.2, isDefault: true));
        Lights.Add(new SceneLight("lamp", new Vec3(-3.15, 1.30, 1.55), new Vec3(1.0, 0.78, 0.52), 3.8, isDefault: true));
    }


    /// <summary>Reduces every editable mesh in the scene and returns the number of removed triangles.</summary>
    public int SimplifyAllGeometry(double keepFraction)
    {
        keepFraction = Math.Clamp(keepFraction, 0.02, 1.0);
        int before = ObjectGroups.Sum(g => g.CountLocalTrianglesRecursively());
        foreach (SceneObjectGroup group in ObjectGroups)
            group.SimplifyGeometry(keepFraction);
        RebuildWorldGeometry();
        return before - ObjectGroups.Sum(g => g.CountLocalTrianglesRecursively());
    }

    /// <summary>Regenerates a parametric group shadow mesh using this scene's material palette.</summary>
    public bool RebuildPrimitiveShadowGeometry(SceneObjectGroup group) => ObjectLibraryRegistry.RebuildPrimitiveShadowGeometry(group, materials);

    /// <summary>Returns stats derived from the current state.</summary>
    public SceneStats GetStats() => new(ObjectGroups.SelectMany(g => g.SelfAndDescendants()).Count(), Triangles.Count, Lights.Count);

    public Aabb? GetSceneBounds()
    {
        List<SceneObjectGroup> visibleGroups = ObjectGroups.Where(g => g.Visible && g.BuildWorldTriangles().Any()).ToList();
        if (visibleGroups.Count == 0)
            return null;

        Aabb bounds = visibleGroups[0].GetWorldBounds();
        for (int i = 1; i < visibleGroups.Count; i++)
            bounds = Aabb.Surrounding(bounds, visibleGroups[i].GetWorldBounds());

        return bounds;
    }

    /// <summary>Changes visibility for one recursive object node and rebuilds render geometry.</summary>
    public void SetGroupVisibility(int id, bool visible)
    {
        SceneObjectGroup? group = GroupById(id);
        if (group == null)
            return;

        group.Visible = visible;
        RebuildWorldGeometry();
    }

    /// <summary>Clears  and updates dependent UI/render state.</summary>
    public void Clear()
    {
        Triangles.Clear();
        Lights.Clear();
        ObjectGroups.Clear();
        activeGroups.Clear();
        nextGroupId = 1;
        bvhRoot = null;
        Description = "Empty scene";
    }

    /// <summary>Begins a recursive scene object group. Nested calls attach completed children to the active parent.</summary>
    public SceneObjectGroup BeginGroup(string name, bool selectable = true)
    {
        SceneObjectGroup group = new(nextGroupId++, name, selectable);
        activeGroups.Push(group);
        return group;
    }

    /// <summary>Completes the current group and attaches it to its parent group or the scene root.</summary>
    public SceneObjectGroup EndGroup()
    {
        if (activeGroups.Count == 0)
            throw new InvalidOperationException("No active scene object group exists.");

        SceneObjectGroup completed = activeGroups.Pop();
        completed.RecalculatePivot();
        if (completed.LocalTriangles.Count > 0 || completed.Children.Count > 0)
        {
            if (activeGroups.Count > 0)
                activeGroups.Peek().AddChild(completed);
            else
                ObjectGroups.Add(completed);
        }
        return completed;
    }

    /// <summary>Adds or creates imported group for this subsystem.</summary>
    public SceneObjectGroup AddImportedGroup(string name, bool selectable = true)
    {
        SceneObjectGroup group = new(nextGroupId++, name, selectable);
        if (activeGroups.Count > 0)
            activeGroups.Peek().AddChild(group);
        else
            ObjectGroups.Add(group);
        return group;
    }

    /// <summary>Finds a group anywhere in the recursive scene hierarchy.</summary>
    public SceneObjectGroup? GroupById(int id) => ObjectGroups.SelectMany(g => g.SelfAndDescendants()).FirstOrDefault(g => g.Id == id);

    public SceneSnapshot CreateSnapshot()
    {
        return new SceneSnapshot(Description, ObjectGroups, Lights);
    }

    /// <summary>Implements the restore snapshot operation for this file's subsystem.</summary>
    public void RestoreSnapshot(SceneSnapshot snapshot)
    {
        Clear();
        Description = snapshot.Description;
        Lights.AddRange(snapshot.Lights.Select(SceneCloner.CloneLight));
        ObjectGroups.AddRange(snapshot.ObjectGroups.Select(SceneCloner.CloneGroupPreservingId));
        nextGroupId = ObjectGroups.SelectMany(g => g.SelfAndDescendants()).Select(g => g.Id).DefaultIfEmpty(0).Max() + 1;
        RebuildWorldGeometry();
    }

    /// <summary>Implements the duplicate group operation for this file's subsystem.</summary>
    public SceneObjectGroup DuplicateGroup(int id)
    {
        SceneObjectGroup source = GroupById(id) ?? throw new ArgumentException("Group not found.", nameof(id));
        SceneObjectGroup duplicate = SceneCloner.CloneGroupWithFreshIds(source, () => nextGroupId++, source.Name + " copy");
        duplicate.Position += new Vec3(0.25, 0.0, 0.25);
        ObjectGroups.Add(duplicate);
        Description = $"Duplicated object: {source.Name}";
        RebuildWorldGeometry();
        return duplicate;
    }

    /// <summary>Implements the delete group operation for this file's subsystem.</summary>
    public void DeleteGroup(int id)
    {
        SceneObjectGroup? group = GroupById(id);
        if (group == null) return;

        if (group.Parent != null)
            group.Parent.RemoveChild(group);
        else
            ObjectGroups.Remove(group);

        RebuildWorldGeometry();
    }

    /// <summary>Creates a new parent group from the selected root-level objects.</summary>
    public SceneObjectGroup GroupSelectedObjects(IEnumerable<int> ids, string name = "Group")
    {
        List<SceneObjectGroup> selected = ids
            .Distinct()
            .Select(GroupById)
            .Where(g => g != null)
            .Cast<SceneObjectGroup>()
            .Where(g => g.Parent == null)
            .ToList();

        if (selected.Count < 2)
            throw new InvalidOperationException("Select at least two top-level objects to group.");

        SceneObjectGroup parent = new(nextGroupId++, name, selectable: true);
        foreach (SceneObjectGroup child in selected)
        {
            ObjectGroups.Remove(child);
            parent.AddChild(child);
        }

        parent.RecalculatePivot();
        ObjectGroups.Add(parent);
        Description = $"Grouped {selected.Count} objects";
        RebuildWorldGeometry();
        return parent;
    }

    /// <summary>Returns true when the selected node is not already a single triangle primitive.</summary>
    public bool CanUngroup(int id)
    {
        SceneObjectGroup? group = GroupById(id);
        if (group == null)
            return false;

        // A raw triangle is the terminal editable primitive.  Everything larger
        // than that can be expanded: existing child groups are promoted, and
        // local multi-triangle geometry is split into one triangle object per face.
        return group.Children.Count > 0 || group.LocalTriangles.Count > 1;
    }

    /// <summary>Promotes children or splits local geometry so any non-triangle item can be ungrouped.</summary>
    public IReadOnlyList<SceneObjectGroup> Ungroup(int id)
    {
        SceneObjectGroup group = GroupById(id) ?? throw new ArgumentException("Group not found.", nameof(id));
        if (!CanUngroup(id))
            throw new InvalidOperationException("The selected object is already a single triangle and cannot be ungrouped further.");

        group.BakeCurrentTransform();

        List<SceneObjectGroup> promoted = group.Children.ToList();
        group.Children.Clear();
        foreach (SceneObjectGroup child in promoted)
            child.Parent = null;

        if (group.LocalTriangles.Count > 0)
        {
            promoted.AddRange(BuildFastUngroupGeometry(group));
            group.LocalTriangles.Clear();
        }

        if (group.Parent != null)
        {
            SceneObjectGroup parent = group.Parent;
            int insertAt = parent.Children.IndexOf(group);
            parent.RemoveChild(group);
            if (insertAt < 0) insertAt = parent.Children.Count;
            foreach (SceneObjectGroup child in promoted)
            {
                child.Parent = parent;
                parent.Children.Insert(insertAt++, child);
            }
            parent.RecalculatePivot();
        }
        else
        {
            int insertAt = ObjectGroups.IndexOf(group);
            ObjectGroups.Remove(group);
            if (insertAt < 0) insertAt = ObjectGroups.Count;
            ObjectGroups.InsertRange(insertAt, promoted);
        }

        Description = $"Ungrouped {group.Name} into {promoted.Count} object(s)";
        RebuildWorldGeometry();
        return promoted;
    }

    private const int MaxDirectFaceUngroupTriangles = 12000;
    private const int SpatialChunkThresholdTriangles = 5000;
    private const int SpatialChunkTargetTriangleCount = 512;
    private const int SpatialChunkMinimumGridAxis = 2;
    private const int SpatialChunkMaximumGridAxis = 32;

    /// <summary>
    /// Splits local mesh data in stages without O(n²) triangle comparisons.
    /// First pass returns disconnected solid parts, so cuboids such as table legs
    /// stay intact.  A second ungroup on a single connected part now returns raw
    /// triangles directly; it no longer tries to infer or remap triangles into
    /// rectangular face objects.  Very large connected meshes are chunked first
    /// to keep the UI responsive.
    /// </summary>
    private List<SceneObjectGroup> BuildFastUngroupGeometry(SceneObjectGroup group)
    {
        IReadOnlyList<Triangle> triangles = group.LocalTriangles;
        Material? colorOverride = group.ColorOverride;

        if (triangles.Count <= 2)
            return BuildTriangleGroups(group.Name, triangles, colorOverride);

        // Large imported meshes should become editable quickly.  Do the spatial
        // split first so ungrouping does not spend time constructing full
        // topological components for tens of thousands of triangles.  Each chunk contains triangles with nearby centroids, so
        // the resulting selectable objects are useful editor regions rather
        // than arbitrary file-order batches.
        if (triangles.Count > SpatialChunkThresholdTriangles)
            return BuildSpatialChunkGroups(group.Name, triangles, colorOverride, SpatialChunkTargetTriangleCount);

        List<List<int>> components = BuildConnectedTriangleComponents(triangles);
        if (components.Count > 1)
            return BuildGroupsFromIndexSets(group.Name, triangles, components, colorOverride, "part");

        if (triangles.Count > MaxDirectFaceUngroupTriangles)
            return BuildSpatialChunkGroups(group.Name, triangles, colorOverride, SpatialChunkTargetTriangleCount);

        // Do not infer rectangles during ungroup.  Rectangle pairing changed the
        // user's original triangle topology and made some imported/editable meshes
        // feel surprising.  Once an object is a single connected part, the next
        // ungroup step exposes its original triangles directly.
        return BuildTriangleGroups(group.Name, triangles, colorOverride);
    }

    private List<SceneObjectGroup> BuildTriangleGroups(string baseName, IReadOnlyList<Triangle> triangles, Material? colorOverride)
    {
        List<SceneObjectGroup> groups = new(triangles.Count);
        for (int i = 0; i < triangles.Count; i++)
            groups.Add(CreateGroupFromTriangles($"{baseName} triangle {i + 1}", new[] { triangles[i] }, colorOverride));
        return groups;
    }

    private List<SceneObjectGroup> BuildGroupsFromIndexSets(string baseName, IReadOnlyList<Triangle> triangles, IReadOnlyList<List<int>> indexSets, Material? colorOverride, string label)
    {
        List<SceneObjectGroup> groups = new(indexSets.Count);
        for (int i = 0; i < indexSets.Count; i++)
            groups.Add(CreateGroupFromTriangles($"{baseName} {label} {i + 1}", indexSets[i].Select(index => triangles[index]), colorOverride));
        return groups;
    }

    private List<SceneObjectGroup> BuildChunkGroups(string baseName, IReadOnlyList<Triangle> triangles, Material? colorOverride, int chunkSize)
    {
        List<SceneObjectGroup> groups = new((triangles.Count + chunkSize - 1) / chunkSize);
        for (int start = 0, chunk = 1; start < triangles.Count; start += chunkSize, chunk++)
        {
            int count = Math.Min(chunkSize, triangles.Count - start);
            groups.Add(CreateGroupFromTriangles($"{baseName} chunk {chunk}", triangles.Skip(start).Take(count), colorOverride));
        }
        return groups;
    }

    /// <summary>
    /// Splits a very large connected mesh into nearby spatial chunks instead of
    /// arbitrary triangle-order chunks.  This keeps the editor responsive while
    /// producing pieces that are useful to select, hide, move, or ungroup again.
    ///
    /// The grid is chosen from the mesh bounds and target chunk size.  Triangles
    /// are assigned by centroid, so adjacent faces usually land in the same or
    /// neighboring chunk.  Oversized buckets are recursively split by triangle
    /// order only as a final safety valve.
    /// </summary>
    private List<SceneObjectGroup> BuildSpatialChunkGroups(string baseName, IReadOnlyList<Triangle> triangles, Material? colorOverride, int targetTrianglesPerChunk)
    {
        if (triangles.Count <= targetTrianglesPerChunk)
            return BuildGroupsFromIndexSets(baseName, triangles, new List<List<int>> { Enumerable.Range(0, triangles.Count).ToList() }, colorOverride, "chunk");

        GetCentroidBounds(triangles, out Vec3 min, out Vec3 max);
        int desiredChunkCount = Math.Max(1, (int)Math.Ceiling(triangles.Count / (double)Math.Max(1, targetTrianglesPerChunk)));
        int gridX, gridY, gridZ;
        ChooseSpatialGrid(min, max, desiredChunkCount, out gridX, out gridY, out gridZ);

        Dictionary<SpatialCellKey, List<int>> buckets = new(desiredChunkCount * 2);
        Vec3 span = new(Math.Max(1e-9, max.X - min.X), Math.Max(1e-9, max.Y - min.Y), Math.Max(1e-9, max.Z - min.Z));

        for (int i = 0; i < triangles.Count; i++)
        {
            Vec3 c = triangles[i].Centroid;
            int x = ToCell(c.X, min.X, span.X, gridX);
            int y = ToCell(c.Y, min.Y, span.Y, gridY);
            int z = ToCell(c.Z, min.Z, span.Z, gridZ);
            SpatialCellKey key = new(x, y, z);
            if (!buckets.TryGetValue(key, out List<int>? bucket))
            {
                bucket = new List<int>();
                buckets[key] = bucket;
            }
            bucket.Add(i);
        }

        List<List<int>> chunks = buckets
            .OrderBy(pair => pair.Key.X)
            .ThenBy(pair => pair.Key.Y)
            .ThenBy(pair => pair.Key.Z)
            .Select(pair => pair.Value)
            .Where(list => list.Count > 0)
            .ToList();

        // Keep chunk sizes bounded.  A dense area can otherwise produce one huge
        // cell, which would still be slow to edit after ungrouping.
        List<List<int>> boundedChunks = new(chunks.Count);
        foreach (List<int> chunk in chunks)
        {
            if (chunk.Count <= targetTrianglesPerChunk * 2)
            {
                boundedChunks.Add(chunk);
                continue;
            }

            foreach (List<int> split in SplitOversizedSpatialChunk(chunk, triangles, targetTrianglesPerChunk))
                boundedChunks.Add(split);
        }

        return BuildGroupsFromIndexSets(baseName, triangles, boundedChunks, colorOverride, "spatial chunk");
    }

    private static IEnumerable<List<int>> SplitOversizedSpatialChunk(List<int> indices, IReadOnlyList<Triangle> triangles, int targetTrianglesPerChunk)
    {
        if (indices.Count <= targetTrianglesPerChunk * 2)
        {
            yield return indices;
            yield break;
        }

        GetCentroidBounds(indices, triangles, out Vec3 min, out Vec3 max);
        double spanX = max.X - min.X;
        double spanY = max.Y - min.Y;
        double spanZ = max.Z - min.Z;

        IOrderedEnumerable<int> ordered = spanX >= spanY && spanX >= spanZ
            ? indices.OrderBy(i => triangles[i].Centroid.X)
            : spanY >= spanZ
                ? indices.OrderBy(i => triangles[i].Centroid.Y)
                : indices.OrderBy(i => triangles[i].Centroid.Z);

        List<int> sorted = ordered.ToList();
        for (int start = 0; start < sorted.Count; start += targetTrianglesPerChunk)
            yield return sorted.Skip(start).Take(Math.Min(targetTrianglesPerChunk, sorted.Count - start)).ToList();
    }

    private static void GetCentroidBounds(IReadOnlyList<Triangle> triangles, out Vec3 min, out Vec3 max)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

        foreach (Triangle tri in triangles)
        {
            Vec3 c = tri.Centroid;
            minX = Math.Min(minX, c.X); minY = Math.Min(minY, c.Y); minZ = Math.Min(minZ, c.Z);
            maxX = Math.Max(maxX, c.X); maxY = Math.Max(maxY, c.Y); maxZ = Math.Max(maxZ, c.Z);
        }

        min = new Vec3(minX, minY, minZ);
        max = new Vec3(maxX, maxY, maxZ);
    }

    private static void GetCentroidBounds(IReadOnlyList<int> indices, IReadOnlyList<Triangle> triangles, out Vec3 min, out Vec3 max)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

        foreach (int index in indices)
        {
            Vec3 c = triangles[index].Centroid;
            minX = Math.Min(minX, c.X); minY = Math.Min(minY, c.Y); minZ = Math.Min(minZ, c.Z);
            maxX = Math.Max(maxX, c.X); maxY = Math.Max(maxY, c.Y); maxZ = Math.Max(maxZ, c.Z);
        }

        min = new Vec3(minX, minY, minZ);
        max = new Vec3(maxX, maxY, maxZ);
    }

    private static void ChooseSpatialGrid(Vec3 min, Vec3 max, int desiredChunkCount, out int gridX, out int gridY, out int gridZ)
    {
        double spanX = Math.Max(1e-9, max.X - min.X);
        double spanY = Math.Max(1e-9, max.Y - min.Y);
        double spanZ = Math.Max(1e-9, max.Z - min.Z);
        double volume = spanX * spanY * spanZ;
        double cellVolume = volume / Math.Max(1, desiredChunkCount);
        double cellSize = Math.Pow(Math.Max(1e-9, cellVolume), 1.0 / 3.0);

        gridX = ClampGridAxis((int)Math.Ceiling(spanX / cellSize));
        gridY = ClampGridAxis((int)Math.Ceiling(spanY / cellSize));
        gridZ = ClampGridAxis((int)Math.Ceiling(spanZ / cellSize));

        // Very flat meshes should still split along their useful surface axes,
        // but not waste cells along a near-zero thickness axis.
        if (spanX < cellSize * 0.25) gridX = 1;
        if (spanY < cellSize * 0.25) gridY = 1;
        if (spanZ < cellSize * 0.25) gridZ = 1;

        if (gridX * gridY * gridZ < desiredChunkCount / 2)
        {
            // Add cells to the longest axes until we are close to the desired
            // chunk count.  This helps long furniture or imported room meshes.
            while (gridX * gridY * gridZ < desiredChunkCount && Math.Max(gridX, Math.Max(gridY, gridZ)) < SpatialChunkMaximumGridAxis)
            {
                if (spanX / gridX >= spanY / gridY && spanX / gridX >= spanZ / gridZ) gridX++;
                else if (spanY / gridY >= spanZ / gridZ) gridY++;
                else gridZ++;
            }
        }
    }

    private static int ClampGridAxis(int value)
    {
        if (value <= 1)
            return 1;

        return Math.Max(SpatialChunkMinimumGridAxis, Math.Min(SpatialChunkMaximumGridAxis, value));
    }

    private static int ToCell(double value, double min, double span, int grid)
    {
        if (grid <= 1)
            return 0;

        double normalized = (value - min) / span;
        int cell = (int)Math.Floor(normalized * grid);
        return Math.Max(0, Math.Min(grid - 1, cell));
    }

    private readonly record struct SpatialCellKey(int X, int Y, int Z);

    private SceneObjectGroup CreateGroupFromTriangles(string name, IEnumerable<Triangle> triangles, Material? colorOverride)
    {
        SceneObjectGroup child = new(nextGroupId++, name, selectable: true);
        foreach (Triangle tri in triangles)
        {
            Material material = colorOverride ?? tri.Material;
            child.AddTriangle(
                tri.A, tri.B, tri.C, tri.UvA, tri.UvB, tri.UvC,
                tri.NormalA, tri.NormalB, tri.NormalC, material);
        }
        child.RecalculatePivot();
        return child;
    }

    private static List<List<int>> BuildConnectedTriangleComponents(IReadOnlyList<Triangle> triangles)
    {
        DisjointSet disjointSet = new(triangles.Count);
        Dictionary<VertexKey, int> firstTriangleAtVertex = new(triangles.Count * 2);

        for (int i = 0; i < triangles.Count; i++)
        {
            ConnectVertex(firstTriangleAtVertex, disjointSet, Quantize(triangles[i].A), i);
            ConnectVertex(firstTriangleAtVertex, disjointSet, Quantize(triangles[i].B), i);
            ConnectVertex(firstTriangleAtVertex, disjointSet, Quantize(triangles[i].C), i);
        }

        Dictionary<int, List<int>> byRoot = new();
        for (int i = 0; i < triangles.Count; i++)
        {
            int root = disjointSet.Find(i);
            if (!byRoot.TryGetValue(root, out List<int>? list))
            {
                list = new List<int>();
                byRoot[root] = list;
            }
            list.Add(i);
        }

        return byRoot.Values.OrderByDescending(list => list.Count).ToList();
    }

    private static void ConnectVertex(Dictionary<VertexKey, int> firstTriangleAtVertex, DisjointSet disjointSet, VertexKey key, int triangleIndex)
    {
        if (firstTriangleAtVertex.TryGetValue(key, out int previousTriangleIndex))
            disjointSet.Union(previousTriangleIndex, triangleIndex);
        else
            firstTriangleAtVertex[key] = triangleIndex;
    }

    private static List<List<int>> BuildRectangularFacePairs(IReadOnlyList<Triangle> triangles)
    {
        Dictionary<EdgeKey, List<int>> edgeToTriangles = new(triangles.Count * 3);
        for (int i = 0; i < triangles.Count; i++)
        {
            AddEdge(edgeToTriangles, triangles[i].A, triangles[i].B, i);
            AddEdge(edgeToTriangles, triangles[i].B, triangles[i].C, i);
            AddEdge(edgeToTriangles, triangles[i].C, triangles[i].A, i);
        }

        bool[] used = new bool[triangles.Count];
        List<List<int>> groups = new();

        foreach (List<int> candidates in edgeToTriangles.Values)
        {
            if (candidates.Count != 2)
                continue;

            int a = candidates[0];
            int b = candidates[1];
            if (used[a] || used[b])
                continue;

            if (!LooksLikeRectanglePair(triangles[a], triangles[b]))
                continue;

            used[a] = true;
            used[b] = true;
            groups.Add(new List<int> { a, b });
        }

        for (int i = 0; i < triangles.Count; i++)
        {
            if (!used[i])
                groups.Add(new List<int> { i });
        }

        return groups;
    }

    private static void AddEdge(Dictionary<EdgeKey, List<int>> edgeToTriangles, Vec3 a, Vec3 b, int triangleIndex)
    {
        EdgeKey edge = new(Quantize(a), Quantize(b));
        if (!edgeToTriangles.TryGetValue(edge, out List<int>? list))
        {
            list = new List<int>(2);
            edgeToTriangles[edge] = list;
        }
        list.Add(triangleIndex);
    }

    private static bool LooksLikeRectanglePair(Triangle a, Triangle b)
    {
        // Opposite winding gives opposite normals for the same visual plane in
        // some imported meshes, so compare absolute dot product.
        if (Math.Abs(a.Normal.Dot(b.Normal)) < 0.999)
            return false;

        List<VertexKey> unique = new(6);
        AddUnique(unique, Quantize(a.A)); AddUnique(unique, Quantize(a.B)); AddUnique(unique, Quantize(a.C));
        AddUnique(unique, Quantize(b.A)); AddUnique(unique, Quantize(b.B)); AddUnique(unique, Quantize(b.C));
        return unique.Count == 4;
    }

    private static void AddUnique(List<VertexKey> values, VertexKey key)
    {
        if (!values.Contains(key))
            values.Add(key);
    }

    private static VertexKey Quantize(Vec3 value) => new(
        (long)Math.Round(value.X * 1_000_000.0),
        (long)Math.Round(value.Y * 1_000_000.0),
        (long)Math.Round(value.Z * 1_000_000.0));

    private readonly record struct VertexKey(long X, long Y, long Z);

    private readonly record struct EdgeKey
    {
        public VertexKey A { get; }
        public VertexKey B { get; }

        public EdgeKey(VertexKey a, VertexKey b)
        {
            if (Compare(a, b) <= 0)
            {
                A = a;
                B = b;
            }
            else
            {
                A = b;
                B = a;
            }
        }

        private static int Compare(VertexKey a, VertexKey b)
        {
            int x = a.X.CompareTo(b.X);
            if (x != 0) return x;
            int y = a.Y.CompareTo(b.Y);
            if (y != 0) return y;
            return a.Z.CompareTo(b.Z);
        }
    }

    private sealed class DisjointSet
    {
        private readonly int[] parent;
        private readonly byte[] rank;

        public DisjointSet(int count)
        {
            parent = new int[count];
            rank = new byte[count];
            for (int i = 0; i < count; i++)
                parent[i] = i;
        }

        public int Find(int value)
        {
            while (parent[value] != value)
            {
                parent[value] = parent[parent[value]];
                value = parent[value];
            }
            return value;
        }

        public void Union(int a, int b)
        {
            int rootA = Find(a);
            int rootB = Find(b);
            if (rootA == rootB)
                return;

            if (rank[rootA] < rank[rootB])
                parent[rootA] = rootB;
            else if (rank[rootA] > rank[rootB])
                parent[rootB] = rootA;
            else
            {
                parent[rootB] = rootA;
                rank[rootA]++;
            }
        }
    }

    /// <summary>Implements the rebuild world geometry operation for this file's subsystem.</summary>
    public void RebuildWorldGeometry()
    {
        Triangles.Clear();
        foreach (SceneObjectGroup group in ObjectGroups)
            Triangles.AddRange(group.BuildWorldTriangles());
        RebuildAccelerationStructure();
    }

    /// <summary>Implements the rebuild acceleration structure operation for this file's subsystem.</summary>
    public void RebuildAccelerationStructure()
    {
        bvhRoot = BvhNode.Build(Triangles);
    }

    /// <summary>Releases editor hierarchy and CPU acceleration data after world triangles are packed for a headless GPU render.</summary>
    public void ReleaseEditorGeometryForHeadlessRender()
    {
        ObjectGroups.Clear();
        activeGroups.Clear();
        bvhRoot = null;
    }

    /// <summary>Tests a ray against the primitive or bounds and returns hit information.</summary>
    public Hit? Intersect(Ray ray)
    {
        if (bvhRoot != null)
            return bvhRoot.Intersect(ray, 1e-6, double.PositiveInfinity);

        Hit? closest = null;
        foreach (Triangle tri in Triangles)
        {
            Hit? hit = tri.Intersect(ray);
            if (hit != null && (closest == null || hit.T < closest.T))
                closest = hit;
        }
        return closest;
    }

    /// <summary>Implements the any intersection operation for this file's subsystem.</summary>
    public bool AnyIntersection(Ray ray, double maxDistance)
    {
        if (bvhRoot != null)
            return bvhRoot.AnyIntersection(ray, 1e-6, maxDistance);

        foreach (Triangle tri in Triangles)
        {
            Hit? hit = tri.Intersect(ray);
            if (hit != null && hit.T < maxDistance)
                return true;
        }

        return false;
    }

    /// <summary>Returns approximate opacity along a shadow ray so transparent/transmission materials do not cast solid black shadows.</summary>
    public double ShadowOpacity(Ray ray, double maxDistance, int maxSamples = 8)
    {
        if (bvhRoot != null)
            return bvhRoot.ShadowOpacity(ray, 1e-6, maxDistance, maxSamples);

        double remaining = 1.0;
        int samples = 0;
        foreach (Triangle tri in Triangles)
        {
            Hit? hit = tri.Intersect(ray);
            if (hit == null || hit.T >= maxDistance)
                continue;

            double opacity = hit.Material.SampleAlpha(hit.TextureU, hit.TextureV) * (1.0 - hit.Material.Transmission * 0.82);
            remaining *= 1.0 - Math.Clamp(opacity, 0.0, 1.0);
            samples++;
            if (remaining <= 0.02 || samples >= maxSamples)
                break;
        }

        return 1.0 - remaining;
    }

    /// <summary>Implements the quad operation for this file's subsystem.</summary>
    public void Quad(Vec3 a, Vec3 b, Vec3 c, Vec3 d, Material material)
    {
        AddTriangle(a, b, c, new Vec2(0, 0), new Vec2(1, 0), new Vec2(1, 1), material);
        AddTriangle(a, c, d, new Vec2(0, 0), new Vec2(1, 1), new Vec2(0, 1), material);
    }

    /// <summary>Implements the box operation for this file's subsystem.</summary>
    public void Box(Vec3 min, Vec3 max, Material material)
    {
        double x0 = min.X, y0 = min.Y, z0 = min.Z, x1 = max.X, y1 = max.Y, z1 = max.Z;
        Vec3 p000 = new(x0, y0, z0), p001 = new(x0, y0, z1), p010 = new(x0, y1, z0), p011 = new(x0, y1, z1);
        Vec3 p100 = new(x1, y0, z0), p101 = new(x1, y0, z1), p110 = new(x1, y1, z0), p111 = new(x1, y1, z1);

        Quad(p001, p101, p111, p011, material);
        Quad(p100, p000, p010, p110, material);
        Quad(p000, p001, p011, p010, material);
        Quad(p101, p100, p110, p111, material);
        Quad(p010, p011, p111, p110, material);
        Quad(p000, p100, p101, p001, material);
    }

    /// <summary>Adds or creates triangle for this subsystem.</summary>
    public void AddTriangle(Vec3 a, Vec3 b, Vec3 c, Material material)
    {
        AddTriangle(a, b, c, new Vec2(0, 0), new Vec2(1, 0), new Vec2(0, 1), material);
    }

    /// <summary>Adds or creates triangle for this subsystem.</summary>
    public void AddTriangle(Vec3 a, Vec3 b, Vec3 c, Vec2 uvA, Vec2 uvB, Vec2 uvC, Material material)
    {
        if (activeGroups.Count > 0)
            activeGroups.Peek().AddTriangle(a, b, c, uvA, uvB, uvC, material);
        else
            Triangles.Add(new Triangle(a, b, c, uvA, uvB, uvC, material));
    }

    public void AddTriangle(
        Vec3 a, Vec3 b, Vec3 c,
        Vec2 uvA, Vec2 uvB, Vec2 uvC,
        Vec3 normalA, Vec3 normalB, Vec3 normalC,
        Material material)
    {
        if (activeGroups.Count > 0)
            activeGroups.Peek().AddTriangle(a, b, c, uvA, uvB, uvC, normalA, normalB, normalC, material);
        else
            Triangles.Add(new Triangle(a, b, c, uvA, uvB, uvC, normalA, normalB, normalC, material));
    }
}
