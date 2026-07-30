// -----------------------------------------------------------------------------
// File: Scene/SceneFormatTypes.cs
// Purpose: Shared scene import/export result and progress contracts.
//
// These types stay in the core application assembly so import/export plugins can
// be moved into separate DLLs while the UI and SceneFormatPlugin interface still
// share one common result/progress model.
// -----------------------------------------------------------------------------

namespace LightingShowcase.SceneGraph;

/// <summary>Result object returned by the OBJ loader.</summary>
public sealed class ObjLoadResult
{
    public string FilePath { get; }
    public int VertexCount { get; }
    public int FaceCount { get; }
    public int TriangleCount { get; }

    /// <summary>Constructs and initializes this component.</summary>
    public ObjLoadResult(string filePath, int vertexCount, int faceCount, int triangleCount)
    {
        FilePath = filePath;
        VertexCount = vertexCount;
        FaceCount = faceCount;
        TriangleCount = triangleCount;
    }
}

/// <summary>Progress update emitted while importing OBJ assets.</summary>
public sealed class ObjLoadProgress
{
    public string Stage { get; }
    public int Percent { get; }
    public int VertexCount { get; }
    public int FaceCount { get; }
    public int TriangleCount { get; }

    /// <summary>Constructs and initializes this component.</summary>
    public ObjLoadProgress(string stage, int percent, int vertexCount, int faceCount, int triangleCount)
    {
        Stage = stage;
        Percent = Math.Max(0, Math.Min(100, percent));
        VertexCount = vertexCount;
        FaceCount = faceCount;
        TriangleCount = triangleCount;
    }
}
