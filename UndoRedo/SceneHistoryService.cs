// -----------------------------------------------------------------------------
// File: UndoRedo/SceneHistoryService.cs
// Purpose: Bounded snapshot-based undo/redo service for user edits.
//
// History is captured as an explicit user-edit transaction: UI handlers call
// BeginUserAction() before they mutate scene content, then normal UI refresh calls
// CommitPendingIfChanged(). A stable scene fingerprint prevents redundant states
// from being recorded, and internal scene rebuilds/restores do not create history.
// -----------------------------------------------------------------------------

using LightingShowcase.SceneGraph;

namespace LightingShowcase.UndoRedo;

/// <summary>Manages bounded undo/redo history for the current editable scene.</summary>
public sealed class SceneHistoryService
{
    private readonly Stack<HistoryEntry> undoSnapshots = new();
    private readonly Stack<HistoryEntry> redoSnapshots = new();
    private readonly int maxSnapshots;

    private SceneSnapshot? pendingSnapshot;
    private ulong pendingFingerprint;
    private bool hasPendingSnapshot;

    public SceneHistoryService(int maxSnapshots = 40)
    {
        if (maxSnapshots < 1)
            throw new ArgumentOutOfRangeException(nameof(maxSnapshots), "History must keep at least one snapshot.");

        this.maxSnapshots = maxSnapshots;
    }

    public int UndoCount => undoSnapshots.Count;
    public int RedoCount => redoSnapshots.Count;
    public bool CanUndo => undoSnapshots.Count > 0;
    public bool CanRedo => redoSnapshots.Count > 0;
    public bool HasPendingUserAction => hasPendingSnapshot;

    /// <summary>
    /// Starts an explicit user edit by taking a pending pre-edit snapshot.
    /// The snapshot is not pushed to undo history until CommitPendingIfChanged()
    /// confirms that editable scene content actually changed.
    /// </summary>
    public void BeginUserAction(Scene scene)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));

        ulong fingerprint = SceneFingerprint.Compute(scene);
        HistoryEntry? top = undoSnapshots.Count == 0 ? null : undoSnapshots.Peek();
        if (top != null && top.Fingerprint == fingerprint)
        {
            hasPendingSnapshot = false;
            pendingSnapshot = null;
            pendingFingerprint = 0UL;
            return;
        }

        pendingSnapshot = scene.CreateSnapshot();
        pendingFingerprint = fingerprint;
        hasPendingSnapshot = true;
    }

    /// <summary>
    /// Commits the pending user-edit snapshot only if the scene changed since the
    /// pending snapshot was captured. Failed operations and no-op edits are dropped.
    /// </summary>
    public bool CommitPendingIfChanged(Scene scene)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (!hasPendingSnapshot || pendingSnapshot == null)
            return false;

        ulong currentFingerprint = SceneFingerprint.Compute(scene);
        if (currentFingerprint == pendingFingerprint)
        {
            ClearPending();
            return false;
        }

        undoSnapshots.Push(new HistoryEntry(pendingSnapshot, pendingFingerprint));
        TrimUndoHistory();
        redoSnapshots.Clear();
        ClearPending();
        return true;
    }

    /// <summary>Discards a pending user-edit snapshot without adding it to history.</summary>
    public void ClearPending()
    {
        pendingSnapshot = null;
        pendingFingerprint = 0UL;
        hasPendingSnapshot = false;
    }

    /// <summary>Restores the previous scene state and stores the current state for redo.</summary>
    public bool Undo(Scene scene)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        ClearPending();
        if (!CanUndo)
            return false;

        redoSnapshots.Push(new HistoryEntry(scene.CreateSnapshot(), SceneFingerprint.Compute(scene)));
        scene.RestoreSnapshot(undoSnapshots.Pop().Snapshot);
        return true;
    }

    /// <summary>Restores the most recently undone scene state.</summary>
    public bool Redo(Scene scene)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        ClearPending();
        if (!CanRedo)
            return false;

        undoSnapshots.Push(new HistoryEntry(scene.CreateSnapshot(), SceneFingerprint.Compute(scene)));
        TrimUndoHistory();
        scene.RestoreSnapshot(redoSnapshots.Pop().Snapshot);
        return true;
    }

    /// <summary>Clears all history. Use when starting an unrelated document/session.</summary>
    public void Clear()
    {
        undoSnapshots.Clear();
        redoSnapshots.Clear();
        ClearPending();
    }

    private void TrimUndoHistory()
    {
        while (undoSnapshots.Count > maxSnapshots)
        {
            HistoryEntry[] newestToOldest = undoSnapshots.ToArray();
            undoSnapshots.Clear();

            // ToArray() returns newest first. Re-push the newest maxSnapshots in
            // oldest-to-newest order so the top remains the most recent snapshot.
            foreach (HistoryEntry snapshot in newestToOldest.Take(maxSnapshots).Reverse())
                undoSnapshots.Push(snapshot);
        }
    }

    private sealed record HistoryEntry(SceneSnapshot Snapshot, ulong Fingerprint);
}
