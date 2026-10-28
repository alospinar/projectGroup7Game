using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace WB3DAssets.BalustradeModularSystem
{
public partial class BalustradeModularWindow
{
sealed class ChainIndexCache
{
    public readonly Dictionary<BalId, int> idToIndex = new();
    public int nextIndex;
}

ChainIndexCache GetOrCreateIndexCache(GameObject root)
{
    if (!root) return null;

    if (!chainIndexByRoot.TryGetValue(root, out var cache) || cache == null)
    {
        cache = new ChainIndexCache();
        chainIndexByRoot[root] = cache;
    }
    return cache;
}

void AssignIndices(GameObject root, List<GameObject> objects, int startIndex)
{
    if (!root || objects == null || objects.Count == 0) return;

    var cache = GetOrCreateIndexCache(root);
    if (cache == null) return;

    int idx = startIndex;

    for (int i = 0; i < objects.Count; i++)
    {
        var go = objects[i];
        if (!go) continue;

        cache.idToIndex[go.StableId()] = idx;
        idx++;
    }

    cache.nextIndex = Mathf.Max(cache.nextIndex, idx);

    // Consume ghost-rail markers whose snap poses match newly placed rails.
    // One central hook covers every build/commit path that calls AssignIndices.
    foreach (var go in objects)
    {
        if (go && IsRailInstance(go))
            ConsumeGhostRailMarkerIfMatch(root, go);
    }
}

void TransferIndex(GameObject root, GameObject oldGO, GameObject newGO)
{
    if (!root || !oldGO || !newGO) return;

    if (!chainIndexByRoot.TryGetValue(root, out var cache) || cache == null)
        return;

    BalId oldId = oldGO.StableId();
    if (!cache.idToIndex.TryGetValue(oldId, out int idx))
        return;

    cache.idToIndex.Remove(oldId);
    cache.idToIndex[newGO.StableId()] = idx;
}

void RemoveIndexCache(GameObject root)
{
    if (!root) return;
    chainIndexByRoot.Remove(root);
}

bool HasVariantLockMarker(GameObject root)
{
    if (!root) return false;
    // Marker lives at scene root level, named with the root's instance ID
    string markerName = VariantLockMarkerName + "_" + root.StableId();
    foreach (var go in root.scene.GetRootGameObjects())
    {
        if (go.name == markerName)
            return true;
    }
    return false;
}

void EnsureVariantLockMarker(GameObject root)
{
    if (!root || HasVariantLockMarker(root)) return;
    // Create marker at scene root level (not as child of balustrade)
    // to avoid "dangling child" errors during undo operations.
    string markerName = VariantLockMarkerName + "_" + root.StableId();
    var marker = new GameObject(markerName);
    marker.hideFlags = HideFlags.HideInHierarchy;
    EditorUtility.SetDirty(marker);
}

void RemoveVariantLockMarker(GameObject root)
{
    if (!root) return;
    string markerName = VariantLockMarkerName + "_" + root.StableId();
    foreach (var go in root.scene.GetRootGameObjects())
    {
        if (go.name == markerName)
        {
            QueueForDeferredDestroy(go);
        }
    }
}

void EnsureStartMarker(GameObject pillar)
{
    if (!pillar) return;

    // Check if marker already exists
    foreach (Transform c in pillar.transform)
    {
        if (c.name == StartMarkerName)
            return;
    }

    var marker = new GameObject(StartMarkerName);
    marker.transform.SetParent(pillar.transform, false);
    marker.transform.localPosition = Vector3.zero;
    marker.hideFlags = HideFlags.HideInHierarchy;
    // Cannot call Undo APIs during undo processing (e.g. from OnUndoRedoPerformed)
    if (!Undo.isProcessing)
        Undo.RegisterCreatedObjectUndo(marker, "Mark Start Pillar");
}

void TransferStartMarker(GameObject root, GameObject oldPillar, GameObject newPillar)
{
    if (!oldPillar || !newPillar) return;

    // Remove marker from old pillar
    for (int i = oldPillar.transform.childCount - 1; i >= 0; i--)
    {
        var c = oldPillar.transform.GetChild(i);
        if (c.name == StartMarkerName)
        {
            QueueForDeferredDestroy(c.gameObject);
            break;
        }
    }

    // Add marker to new pillar
    EnsureStartMarker(newPillar);
}

Transform FindStartPillarByMarker(GameObject root)
{
    if (!root) return null;

    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (t.name == StartMarkerName)
            return t.parent; // parent of marker = start pillar
    }

    return null;
}

void ScanSceneForExistingBalustrades()
{
    // Clear and rebuild from scene
    finalizedBalustrades.Clear();
    balustradeStartPillars.Clear();
    chainIndexByRoot.Clear();

    // Find all root-level GameObjects named "Balustrade_*"
    var allTransforms = BalustradeIds.FindAll<Transform>();
    
    foreach (var t in allTransforms)
    {
        if (!t) continue;
        if (t.parent != null) continue; // only root objects
        if (!t.name.StartsWith("Balustrade_")) continue;
        
        // Verify it contains balustrade content (pillars or rails)
        bool hasContent = false;
        
        foreach (Transform child in t.GetComponentsInChildren<Transform>(true))
        {
            if (child == t) continue;
            
            var src = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
            if (!src) continue;
            
            string n = src.name;
            
            if (n.StartsWith("pillar_") && n.EndsWith("_PREFAB"))
                hasContent = true;
            
            if ((n.StartsWith("blstrs_") || n.StartsWith("blstrsCrvd_")) && n.EndsWith("_PREFAB"))
                hasContent = true;
            
            if (hasContent) break;
        }
        
        if (hasContent)
        {
            finalizedBalustrades.Add(t.gameObject);
            // NOTE: balustradeStartPillars is set by RebuildChainIndexForRoot
            // which is called right after this scan in OnUndoRedoPerformed.
        }
    }

    // Apply Full Detail Mode to all discovered balustrades
    if (fullDetailMode)
        ApplyFullDetailToAllBalustrades(true);
}

int GetNextBalustradeNumber()
{
    var usedNumbers = new HashSet<int>();

    // Scan scene directly (robust even if finalizedBalustrades is stale)
    foreach (var t in BalustradeIds.FindAll<Transform>())
    {
        if (!t || t.parent != null) continue;
        if (!t.name.StartsWith("Balustrade_")) continue;
        string numStr = t.name.Substring("Balustrade_".Length);
        if (int.TryParse(numStr, out int num))
            usedNumbers.Add(num);
    }

    int next = 1;
    while (usedNumbers.Contains(next))
        next++;
    return next;
}

void RebuildProtectedPillarIdCache()
{
    protectedPillarIds.Clear();

    // finalized balustrades
    foreach (var root in finalizedBalustrades)
    {
        if (!root) continue;

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!t) continue;

            if (IsPillarInstance(t.gameObject))
                protectedPillarIds.Add(t.gameObject.StableId());
        }
    }

// active build session
foreach (var go in currentBuildObjects)
{
    if (!go) continue;

    if (IsPillarInstance(go))
        protectedPillarIds.Add(go.StableId());
}
}

void HandleRailDeletedAndCleanupPillars(GameObject deletedRail)
{
    if (!deletedRail)
        return;

    // Find owning balustrade root
    var root = FindOwningBalustradeRoot(deletedRail.transform);
    if (!root)
        return;

    // Collect candidate pillars FIRST
    var pillars = new List<Transform>();
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (IsPillarInstance(t.gameObject))
            pillars.Add(t);
    }

    suppressDeleteUndo = true;

    foreach (var pillar in pillars)
    {
        // Check free snaps for all possible slots (1/2 for M/E, 1/2/3 for T).
        bool snap1Free = IsSnapFree(pillar, "SnapPoint1");
        bool snap2Free = IsSnapFree(pillar, "SnapPoint2");

        // T-pillar detection: has SnapPoint3 → originally 3 connections.
        var snap3 = FindSnap(pillar, "SnapPoint3");
        bool isT = snap3 != null;
        bool snap3Free = isT && IsSnapFree(pillar, "SnapPoint3");

        if (isT)
        {
            int freeCount = (snap1Free ? 1 : 0) + (snap2Free ? 1 : 0) + (snap3Free ? 1 : 0);
            // 0 free → still fully connected, skip.
            if (freeCount == 0) continue;
            // 1 free → 2 connections remain → T degrades to M (not E).
            // 2 free → 1 connection remains → T degrades to E.
            // 3 free → orphan, skip (let generic cleanup handle).
            if (freeCount == 1)
            {
                // Pick the free snap for M-replacement orientation.
                Transform freeSnap = snap1Free ? FindSnap(pillar, "SnapPoint1")
                                    : snap2Free ? FindSnap(pillar, "SnapPoint2")
                                    : snap3;
                if (freeSnap) ReplaceTWithM_AtFreeSnap(pillar.gameObject, freeSnap);
                continue;
            }
            if (freeCount == 2)
            {
                // Fall through to V1E replacement using any free snap.
                Transform freeSnap = snap1Free ? FindSnap(pillar, "SnapPoint1")
                                    : snap2Free ? FindSnap(pillar, "SnapPoint2")
                                    : snap3;
                if (freeSnap) ReplacePillarWithV1E_AtFreeSnap(pillar.gameObject, freeSnap);
                continue;
            }
            continue; // 3 free
        }

        if (!snap1Free && !snap2Free)
            continue;

        // Prefer SnapPoint1, fallback SnapPoint2
        string freeSnapName = snap1Free ? "SnapPoint1" : "SnapPoint2";
        var freeSnapNonT = FindSnap(pillar, freeSnapName);
        if (!freeSnapNonT)
            continue;

        ReplacePillarWithV1E_AtFreeSnap(pillar.gameObject, freeSnapNonT);
    }

    suppressDeleteUndo = false;

    if (fullDetailMode && root)
        ApplyFullDetailToBalustrade(root.gameObject, true);

    RebuildProtectedPillarIdCache();
}

void OnObjectChanges_BlockPillarDelete(ref ObjectChangeEventStream stream)
{
    if (suppressDeleteUndo)
        return;

    // Detect rail-delete attempts. Instead of actually deleting, we undo the delete
    // and mark the rail as hidden. The rail keeps its snap topology so variant
    // switches and BFS reposition work as if the rail were still there.
    bool shouldHideRail = lastSelectionWasRail && lastRailDeleteBalustradeRoot;
    bool railHideDone = false;

    for (int i = 0; i < stream.length; i++)
    {
        if (stream.GetEventType(i) != ObjectChangeKind.DestroyGameObjectHierarchy)
            continue;

        stream.GetDestroyGameObjectHierarchyEvent(i, out var evt);
#if UNITY_6000_5_OR_NEWER
        BalId id = BalId.FromEvent(evt.entityId);
#else
        BalId id = BalId.FromEvent(evt.instanceId);
#endif

        // --- HIDE RAIL(S) INSTEAD OF DELETE (run once per change batch) ---
        if (shouldHideRail && !railHideDone)
        {
            railHideDone = true;
            shouldHideRail = false;

            // Snapshot the snap poses of every rail the user selected for
            // delete BEFORE we undo the destroy. pendingRailSnaps was filled
            // by OnSelectionChanged for every selected rail, so it covers
            // multi-select deletes (Ctrl+click multiple rails + Delete).
            var railsToHideByRoot = new Dictionary<GameObject, List<(Vector3 s1, Vector3 s2)>>();
            foreach (var kvp in pendingRailSnaps)
            {
                var data = kvp.Value;
                if (!data.root) continue;
                if (!railsToHideByRoot.TryGetValue(data.root, out var list))
                {
                    list = new List<(Vector3, Vector3)>();
                    railsToHideByRoot[data.root] = list;
                }
                list.Add((data.snap1Pos, data.snap2Pos));
            }

            // Undo the destroy to bring all rail GameObjects back at once.
            suppressDeleteUndo = true;
            Undo.PerformUndo();
            suppressDeleteUndo = false;

            // Identify revived rails by matching their current snap world
            // poses against the captured ones and hide them.
            var affectedRoots = new HashSet<GameObject>();
            foreach (var kv in railsToHideByRoot)
            {
                var revivedRoot = kv.Key;
                affectedRoots.Add(revivedRoot);

                foreach (var (sp1, sp2) in kv.Value)
                {
                    GameObject revived = FindLiveRailBySnapPositions(revivedRoot, sp1, sp2);
                    if (!revived) continue;

                    Undo.RegisterCompleteObjectUndo(revived, "Hide Deleted Rail");
                    HideDeletedRail(revived);

                    bool curvedOr45 = IsCurvedOr45RailDelete(revived, revivedRoot);
                    if (curvedOr45)
                        HidePillarsAndSpawnEndCapsForHiddenRail(revivedRoot, revived);
                }
            }

            // One repair pass per affected root, AFTER all rails were hidden,
            // so end-pillar repair sees the final post-delete topology.
            suppressDeleteUndo = true;
            foreach (var revivedRoot in affectedRoots)
                RepairEndPillarsAfterRailDelete(revivedRoot);
            suppressDeleteUndo = false;

            // Drop selection if it still points at a (now-hidden) rail.
            var sel = Selection.activeGameObject;
            if (sel && IsHiddenDeletedRail(sel))
            {
                suppressSelectionChanged = true;
                Selection.activeGameObject = null;
                EditorApplication.delayCall += () => suppressSelectionChanged = false;
            }

            lastSelectionWasRail = false;
            lastRailDeleteBalustradeRoot = null;
            railCoSelectedPillarIds.Clear();
            pendingRailSnaps.Clear();

            RebuildProtectedPillarIdCache();
            PinHiddenContainersToBottom();
            return;
        }

        // --- BLOCK PILLAR DELETE ---
        // Only block if user manually selected a pillar and pressed Delete.
        // During undo/redo, pillars are destroyed programmatically and must NOT be blocked.
        // Also allow deletion if pillar was auto-co-selected with a rail.
        if (lastSelectionWasPillar && protectedPillarIds.Contains(id))
        {
            suppressDeleteUndo = true;
            Undo.PerformUndo();
            suppressDeleteUndo = false;

            // NOTE: do NOT reset lastSelectionWasPillar here,
            // the pillar is still selected after undo
            RebuildProtectedPillarIdCache();
            return;
        }
    }
}

void OnUndoRedoPerformed()
{
    // 1) Rescan scene for balustrade roots (handles destroyed/restored roots)
    ScanSceneForExistingBalustrades();

    // 2) Rebuild chain indices from actual hierarchy
    RebuildAllChainIndices();

    // 3) Rebuild pillar protection cache
    RebuildProtectedPillarIdCache();

    // 4) Per-root variant lock: only unlock if the FIRST deleted rail has been restored
    var rootsToUnlock = new List<GameObject>();
    foreach (var kvp in deletedRailIdsByRoot)
    {
        var root = kvp.Key;
        var ids  = kvp.Value;
        if (!root || ids.Count == 0) { rootsToUnlock.Add(root); continue; }

        // Remove restored rails from the end of the list (last undo restores last delete)
        while (ids.Count > 0)
        {
            BalId lastId = ids[ids.Count - 1];
            var obj = BalustradeIds.ObjectFromId(lastId);
            if (obj == null) break; // still deleted — stop checking
            ids.RemoveAt(ids.Count - 1);
        }

        // All deleted rails restored → unlock
        if (ids.Count == 0)
            rootsToUnlock.Add(root);
    }

    foreach (var root in rootsToUnlock)
    {
        // Variant lock marker persists (not removed on undo)
        deletedRailIdsByRoot.Remove(root);
    }

    // 5) Reset cached rail selection state (stale after undo)
    //    Skip if we're inside a programmatic undo (pillar delete protection)
    if (!suppressDeleteUndo)
    {
        lastSelectionWasRail = false;
        lastSelectionWasPillar = false;
        lastRailDeleteBalustradeRoot = null;
        railCoSelectedPillarIds.Clear();
    }

    // 6) Re-enable renderers on pillars restored by undo.
    //    When a pillar is hidden during Continue Anchor mode (renderers disabled)
    //    and then destroyed with Undo.DestroyObjectImmediate, undo restores it
    //    with disabled renderers. Fix: re-show any pillar that is NOT the current
    //    continue anchor but has all renderers disabled.
    foreach (var root in finalizedBalustrades)
    {
        if (!root) continue;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!t || t == root.transform) continue;
            if (!IsPillarInstance(t.gameObject)) continue;

            // Skip the currently active continue anchor (it's supposed to be hidden)
            if (continueAnchorActive && continueAnchorPillar == t.gameObject)
                continue;

            // Skip pillars intentionally hidden by the curved/45 rail-delete
            // path. They MUST stay invisible — re-enabling their renderers
            // would expose the M-pillars sitting under the visible repair-V1Es.
            if (IsHiddenDeletedPillar(t.gameObject))
                continue;

            var renderers = t.gameObject.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) continue;

            // Check if ALL renderers are disabled (sign of an undo-restored hidden pillar)
            bool allDisabled = true;
            foreach (var r in renderers)
            {
                if (r.enabled) { allDisabled = false; break; }
            }

            if (allDisabled)
            {
                foreach (var r in renderers)
                    r.enabled = true;
            }
        }
    }

    // 7) Remove variant lock marker if no lock reason remains after undo.
    //    Lock reasons: V1T/V1C/V1C45 pillars present, or tracked deleted rails.
    foreach (var root in finalizedBalustrades)
    {
        if (!root) continue;
        if (!HasVariantLockMarker(root)) continue;

        // Check if any lock-requiring pillar types still exist
        bool hasLockReason = false;

        // Check for tracked deleted rails
        if (deletedRailIdsByRoot.TryGetValue(root, out var delIds) && delIds.Count > 0)
            hasLockReason = true;

        if (!hasLockReason)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root.transform) continue;
                var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                if (!src) continue;
                string n = src.name;
                // Only V1T/V2T (T-pieces) require lock; V1C corners do NOT
                if (n.Contains("V1T_") || n.Contains("V2T_"))
                {
                    hasLockReason = true;
                    break;
                }
            }
        }

        if (!hasLockReason)
            RemoveVariantLockMarker(root);
    }

    // 8) Reapply Full Detail Mode (undo may restore objects with active LODGroups)
    if (fullDetailMode)
        ApplyFullDetailToAllBalustrades(true);

    // 10) Refresh UI
    RefreshUi();
    SceneView.RepaintAll();
}

void RebuildAllChainIndices()
{
    chainIndexByRoot.Clear();

    foreach (var root in finalizedBalustrades)
    {
        if (!root) continue;
        RebuildChainIndexForRoot(root);
    }
}

void RebuildChainIndexForRoot(GameObject root)
{
    if (!root) return;

    var cache = GetOrCreateIndexCache(root);
    cache.idToIndex.Clear();
    cache.nextIndex = 0;

    // --- Collect all pillars and rails in this root ---
    var pillars = new List<Transform>();
    var rails   = new List<Transform>();

    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (t == root.transform) continue;

        var go = t.gameObject;
        if (!go) continue;

        if (IsPillarInstance(go))
            pillars.Add(t);
        else if (IsRailInstance(go))
            rails.Add(t);
    }

    if (pillars.Count == 0) return;

    // --- Build snap-position lookup for all rails ---
    // Key = quantized snap position, Value = (rail Transform, snapName)
    var snapToRail = new Dictionary<long, (Transform rail, string snapName)>();

    foreach (var rail in rails)
    {
        var s1 = FindSnap(rail, RailStartSnap);
        var s2 = FindSnap(rail, RailEndSnap);

        if (s1) snapToRail[PosKey(s1.position)] = (rail, RailStartSnap);
        if (s2) snapToRail[PosKey(s2.position)] = (rail, RailEndSnap);
    }

    // --- Find start pillar ---
    // PRIMARY: look for the persistent __BMS_START__ marker
    Transform startPillar = FindStartPillarByMarker(root);

    // Build pillar snap lookup (needed for chain walk below)
    var snapToPillar = new Dictionary<long, (Transform pillar, string snapName)>();

    foreach (var pillar in pillars)
    {
        var s1 = FindSnap(pillar, "SnapPoint1");
        var s2 = FindSnap(pillar, "SnapPoint2");

        if (s1) snapToPillar[PosKey(s1.position)] = (pillar, "SnapPoint1");
        if (s2) snapToPillar[PosKey(s2.position)] = (pillar, "SnapPoint2");
    }

    // FALLBACK: no marker found (legacy balustrade or marker lost)
    // During undo processing, prefer the previously known start pillar if it still exists,
    // because undo may restore it in a later step and the sibling-index heuristic can pick wrong.
    if (!startPillar)
    {
        // Check if we already know the start pillar from a previous rebuild
        if (balustradeStartPillars.TryGetValue(root, out var knownStart) && knownStart)
        {
            // Verify it's still a child of this root
            if (knownStart.transform.parent == root.transform)
                startPillar = knownStart.transform;
        }
    }

    if (!startPillar)
    {
        // Use V1E/V2E with lowest sibling index among direct root children.
        Transform bestCandidate = null;
        int bestSibling = int.MaxValue;

        foreach (var p in pillars)
        {
            var src = PrefabUtility.GetCorrespondingObjectFromSource(p.gameObject);
            if (!src) continue;
            if (!src.name.Contains("E_PREFAB")) continue;

            // Only consider DIRECT children of root
            if (p.parent != root.transform) continue;

            int si = p.GetSiblingIndex();
            if (si < bestSibling)
            {
                bestSibling = si;
                bestCandidate = p;
            }
        }

        startPillar = bestCandidate;

        // Only add marker outside of undo processing to avoid marking the wrong pillar
        if (startPillar && !Undo.isProcessing)
            EnsureStartMarker(startPillar.gameObject);
    }

    if (!startPillar) return;

    // Update balustradeStartPillars so other systems stay consistent
    balustradeStartPillars[root] = startPillar.gameObject;

    // --- Walk the chain starting from startPillar ---
    var visited = new HashSet<BalId>();
    int index = 0;
    const float ChainSnapTol = 0.01f;

    Transform current = startPillar;

    while (current != null)
    {
        BalId currentId = current.gameObject.StableId();
        if (visited.Contains(currentId))
            break;

        visited.Add(currentId);
        cache.idToIndex[currentId] = index++;

        // Find outgoing snap of current pillar
        // Try SnapPoint1 first, then SnapPoint2
        Transform outSnap = null;
        string[] snapNames = { "SnapPoint1", "SnapPoint2" };

        foreach (var sn in snapNames)
        {
            var snap = FindSnap(current, sn);
            if (!snap) continue;

            long key = PosKey(snap.position);

            // Look for a rail connected at this snap
            if (snapToRail.TryGetValue(key, out var railInfo))
            {
                BalId railId = railInfo.rail.gameObject.StableId();
                if (!visited.Contains(railId))
                {
                    outSnap = snap;
                    break;
                }
            }
        }

        // Distance fallback for outSnap
        if (outSnap == null)
        {
            foreach (var sn in snapNames)
            {
                var snap = FindSnap(current, sn);
                if (!snap) continue;

                float bestDist = ChainSnapTol;
                foreach (var kvp in snapToRail)
                {
                    var railSnap = FindSnap(kvp.Value.rail, kvp.Value.snapName);
                    if (!railSnap) continue;
                    float d = Vector3.Distance(snap.position, railSnap.position);
                    if (d < bestDist && !visited.Contains(kvp.Value.rail.gameObject.StableId()))
                    {
                        bestDist = d;
                        outSnap = snap;
                    }
                }
                if (outSnap != null) break;
            }
        }

        if (outSnap == null)
            break;

        // Find the rail connected to this outgoing snap
        long outKey = PosKey(outSnap.position);
        (Transform rail, string snapName) connectedRail = default;
        bool foundRail = snapToRail.TryGetValue(outKey, out connectedRail);

        // Distance fallback for rail lookup
        if (!foundRail)
        {
            float bestDist = ChainSnapTol;
            foreach (var kvp in snapToRail)
            {
                var railSnap = FindSnap(kvp.Value.rail, kvp.Value.snapName);
                if (!railSnap) continue;
                float d = Vector3.Distance(outSnap.position, railSnap.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    connectedRail = kvp.Value;
                    foundRail = true;
                }
            }
        }

        if (!foundRail)
            break;

        BalId connRailId = connectedRail.rail.gameObject.StableId();
        if (visited.Contains(connRailId))
            break;

        visited.Add(connRailId);
        cache.idToIndex[connRailId] = index++;

        // Find the OTHER snap of this rail (the end that connects to the next pillar)
        string otherSnapName = connectedRail.snapName == RailStartSnap
            ? RailEndSnap
            : RailStartSnap;

        var railOtherSnap = FindSnap(connectedRail.rail, otherSnapName);
        if (!railOtherSnap)
            break;

        // Find the next pillar at the rail's other end
        long nextKey = PosKey(railOtherSnap.position);
        (Transform pillar, string snapName) nextPillarInfo = default;
        bool foundPillar = snapToPillar.TryGetValue(nextKey, out nextPillarInfo);

        // Distance fallback for pillar lookup
        if (!foundPillar)
        {
            float bestDist = ChainSnapTol;
            foreach (var kvp in snapToPillar)
            {
                var pillarSnap = FindSnap(kvp.Value.pillar, kvp.Value.snapName);
                if (!pillarSnap) continue;
                float d = Vector3.Distance(railOtherSnap.position, pillarSnap.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    nextPillarInfo = kvp.Value;
                    foundPillar = true;
                }
            }
        }

        if (!foundPillar)
            break;

        BalId nextPillarId = nextPillarInfo.pillar.gameObject.StableId();
        if (visited.Contains(nextPillarId))
            break;

        current = nextPillarInfo.pillar;
    }

    cache.nextIndex = index;
}

void CleanupFinalizedBalustrades()
{
    for (int i = finalizedBalustrades.Count - 1; i >= 0; i--)
    {
if (finalizedBalustrades[i] == null)
{
    var deadRoot = finalizedBalustrades[i];
    finalizedBalustrades.RemoveAt(i);
balustradeStartPillars.Remove(deadRoot);
deletedRailIdsByRoot.Remove(deadRoot);
RemoveIndexCache(deadRoot);
}
    }

    // Clamp selection index
    if (selectedBalustradeIndex >= finalizedBalustrades.Count)
        selectedBalustradeIndex = finalizedBalustrades.Count - 1;

    if (finalizedBalustrades.Count == 0)
        selectedBalustradeIndex = -1;
}

// Find pillars connected to a rail for visual co-selection.
// onlyOrphans: true = only co-select pillars with exactly one occupied snap point
List<GameObject> FindCoSelectPillarsForRail(GameObject rail, bool onlyOrphans = false)
{
    var result = new List<GameObject>();
    if (!rail) return result;
    var root = FindOwningBalustradeRoot(rail.transform);
    if (!root) return result;

    var rs1 = FindSnap(rail.transform, RailStartSnap);
    var rs2 = FindSnap(rail.transform, RailEndSnap);
    if (!rs1 && !rs2) return result;

    // Collect ALL rail snap positions in this balustrade
    var allRailSnapPositions = new List<Vector3>();
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (!t || !IsRailInstance(t.gameObject)) continue;
        var s1 = FindSnap(t, RailStartSnap);
        var s2 = FindSnap(t, RailEndSnap);
        if (s1) allRailSnapPositions.Add(s1.position);
        if (s2) allRailSnapPositions.Add(s2.position);
    }

    // Scale-aware tolerance (root may be scaled)
    float scaleFactor = root ? Mathf.Max(root.lossyScale.x, root.lossyScale.y, root.lossyScale.z, 1f) : 1f;
    float tol = 0.15f * scaleFactor;
    var connected = new List<(GameObject go, bool bothOccupied)>();

    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (!t || !IsPillarInstance(t.gameObject)) continue;
        var p1 = FindSnap(t, "SnapPoint1");
        var p2 = FindSnap(t, "SnapPoint2");

        bool match =
            (p1 && rs1 && Vector3.Distance(p1.position, rs1.position) < tol) ||
            (p1 && rs2 && Vector3.Distance(p1.position, rs2.position) < tol) ||
            (p2 && rs1 && Vector3.Distance(p2.position, rs1.position) < tol) ||
            (p2 && rs2 && Vector3.Distance(p2.position, rs2.position) < tol);
        if (!match) continue;

        // Count how many snap points are occupied by any rail
        bool p1Occupied = false, p2Occupied = false;
        if (p1) foreach (var rsp in allRailSnapPositions)
            if (Vector3.Distance(p1.position, rsp) < tol) { p1Occupied = true; break; }
        if (p2) foreach (var rsp in allRailSnapPositions)
            if (Vector3.Distance(p2.position, rsp) < tol) { p2Occupied = true; break; }

        connected.Add((t.gameObject, p1Occupied && p2Occupied));
    }

    // onlyOrphans: skip pillars where both snap points are occupied
    foreach (var c in connected)
        if (!onlyOrphans || !c.bothOccupied) result.Add(c.go);

    return result;
}

// Find pillars that sit between two or more selected rails (multi-select co-selection).
// A pillar qualifies if its snap points connect to at least 2 different selected rails.
List<GameObject> FindPillarsBetweenRails(GameObject[] selectedRails)
{
    var result = new List<GameObject>();
    if (selectedRails == null || selectedRails.Length < 2) return result;

    // Collect snap positions per rail: (railInstanceId, position)
    var railSnaps = new List<(BalId railId, Vector3 pos)>();
    foreach (var rail in selectedRails)
    {
        if (!rail) continue;
        BalId id = rail.StableId();
        var s1 = FindSnap(rail.transform, RailStartSnap);
        var s2 = FindSnap(rail.transform, RailEndSnap);
        if (s1) railSnaps.Add((id, s1.position));
        if (s2) railSnaps.Add((id, s2.position));
    }

    // Gather all pillars from relevant balustrade roots
    var roots = new HashSet<Transform>();
    foreach (var rail in selectedRails)
    {
        if (!rail) continue;
        var r = FindOwningBalustradeRoot(rail.transform);
        if (r) roots.Add(r);
    }

    // Scale-aware tolerance (roots may be scaled)
    float maxScale = 1f;
    foreach (var root in roots)
        if (root) maxScale = Mathf.Max(maxScale, root.lossyScale.x, root.lossyScale.y, root.lossyScale.z);
    float tol = 0.15f * maxScale;
    var seen = new HashSet<BalId>();

    foreach (var root in roots)
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (!t || !IsPillarInstance(t.gameObject)) continue;
        BalId pid = t.gameObject.StableId();
        if (seen.Contains(pid)) continue;

        var p1 = FindSnap(t, "SnapPoint1");
        var p2 = FindSnap(t, "SnapPoint2");

        // Find which selected rails each snap point touches
        BalId p1Rail = BalId.None, p2Rail = BalId.None;
        foreach (var (railId, pos) in railSnaps)
        {
            if (p1 && p1Rail == BalId.None && Vector3.Distance(p1.position, pos) < tol) p1Rail = railId;
            if (p2 && p2Rail == BalId.None && Vector3.Distance(p2.position, pos) < tol) p2Rail = railId;
        }

        // Both snaps touch different selected rails → pillar is between them
        if (p1Rail != BalId.None && p2Rail != BalId.None && p1Rail != p2Rail)
        {
            seen.Add(pid);
            result.Add(t.gameObject);
        }
    }

    return result;
}

void RepairEndPillarsAfterRailDelete(GameObject balustradeRoot)
{
    if (!balustradeRoot)
        return;

    // Build a map of ALL rail snap positions (start + end) still present in this root
    var railSnapByPos = new Dictionary<long, Transform>();

    foreach (Transform t in balustradeRoot.GetComponentsInChildren<Transform>(true))
    {
        if (!t) continue;

        var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (!src) continue;

        string n = src.name;
        bool isRail =
            (n.StartsWith("blstrs_") || n.StartsWith("blstrsCrvd_")) &&
            n.EndsWith("_PREFAB");

        if (!isRail)
            continue;

        // Skip hidden rails: they exist only as topology anchors and should not
        // count as "rail still present" for end-pillar repair logic.
        if (IsHiddenDeletedRail(t.gameObject))
            continue;

        var s1 = FindSnap(t, RailStartSnap);
        var s2 = FindSnap(t, RailEndSnap);

        if (s1)
            railSnapByPos[PosKey(s1.position)] = s1;
        if (s2)
            railSnapByPos[PosKey(s2.position)] = s2;
    }

    // Separate lists: pillars to DELETE, REPLACE with V1E, or REPLACE V1T→V1M
    var toDelete = new List<GameObject>();
    var toReplace = new List<GameObject>();
    var toReplaceTPillarWithM = new List<GameObject>();
    var toReplaceTPillarWithC = new List<(GameObject pillar, Transform railSnap0, Transform railSnap1)>();

    foreach (Transform t in balustradeRoot.GetComponentsInChildren<Transform>(true))
    {
        if (!t) continue;

        if (!IsPillarInstance(t.gameObject))
            continue;

        // Skip pillars that were already hidden by the curved/45 rail-delete
        // path. They keep their full snap geometry on purpose for BFS walking,
        // and a separate visible V1E repair-pillar was spawned alongside them.
        if (IsHiddenDeletedPillar(t.gameObject))
            continue;

        var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (!src) continue;

        string prefabName = src.name;
        bool isEndPillar = prefabName.Contains("V1E") || prefabName.Contains("V2E");
        bool isTPillar   = prefabName.Contains("V1T") || prefabName.Contains("V2T");

        var p1 = FindSnap(t, "SnapPoint1");
        var p2 = FindSnap(t, "SnapPoint2");

        // If snaps are missing, do nothing (invalid prefab instance)
        if (!p1 && !p2)
            continue;

        bool occ1 = p1 && railSnapByPos.ContainsKey(PosKey(p1.position));
        bool occ2 = p2 && railSnapByPos.ContainsKey(PosKey(p2.position));

        // Fallback: PosKey may miss snaps after variant switches or scaling → distance check
        if (!occ1 || !occ2)
        {
            float scaleFactor = Mathf.Max(balustradeRoot.transform.lossyScale.x,
                                           balustradeRoot.transform.lossyScale.y,
                                           balustradeRoot.transform.lossyScale.z, 1f);
            float tol = Mathf.Max(0.01f, 0.15f * (scaleFactor > 1.001f ? scaleFactor : 1f));
            foreach (var kvp in railSnapByPos)
            {
                if (!occ1 && p1 && Vector3.Distance(p1.position, kvp.Value.position) < tol)
                    occ1 = true;
                if (!occ2 && p2 && Vector3.Distance(p2.position, kvp.Value.position) < tol)
                    occ2 = true;
                if (occ1 && occ2) break;
            }
        }

        // V1E/V2E: DELETE if has free snap (they only have SnapPoint1)
        if (isEndPillar)
        {
            if (!occ1)
                toDelete.Add(t.gameObject);
            continue;
        }

        // V1T/V2T: Count rails connected to this T-pillar.
        // IMPORTANT: V1T is placed with 90° Y rotation relative to V1M, so its snap
        // world positions do NOT match the rail snap positions. The rails were placed
        // to connect to the PREVIOUS V1M's snap positions before it was replaced by V1T.
        // Therefore we cannot use snap-to-snap matching at all.
        // Instead, count how many rail snaps are near the V1T's CENTER position.
        if (isTPillar)
        {
            // Compute tolerance from the pillar's own snap-to-center distances
            float maxSnapDist = 0f;
            var pt1 = FindSnap(t, "SnapPointT1");
            var pt2 = FindSnap(t, "SnapPointT2");
            if (p1)  maxSnapDist = Mathf.Max(maxSnapDist, Vector3.Distance(t.position, p1.position));
            if (p2)  maxSnapDist = Mathf.Max(maxSnapDist, Vector3.Distance(t.position, p2.position));
            if (pt1) maxSnapDist = Mathf.Max(maxSnapDist, Vector3.Distance(t.position, pt1.position));
            if (pt2) maxSnapDist = Mathf.Max(maxSnapDist, Vector3.Distance(t.position, pt2.position));
            float tolerance = maxSnapDist + 0.1f; // snap radius + buffer

            // Count unique rails that have a snap within tolerance of this pillar's center
            var nearbyRails = new HashSet<Transform>(); // track by rail root transform
            var nearbyRailSnaps = new List<Transform>(); // one snap per unique rail
            foreach (var kvp in railSnapByPos)
            {
                Transform railSnap = kvp.Value;
                if (Vector3.Distance(t.position, railSnap.position) < tolerance)
                {
                    // Add the rail's root (parent) to avoid counting both snaps of same rail
                    if (nearbyRails.Add(railSnap.parent))
                        nearbyRailSnaps.Add(railSnap);
                }
            }

            int connectedRailCount = nearbyRails.Count;

            // V1T has exactly 3 connections (user confirmed: all or nothing)
            if (connectedRailCount >= 3)
            {
                // All rails still connected → keep V1T (deleted rail was unrelated)
            }
            else if (connectedRailCount == 2)
            {
                // 2 rails remain: check angle to decide V1C (corner) vs V1M (straight)
                Vector3 d0 = (nearbyRailSnaps[0].position - t.position); d0.y = 0f; d0.Normalize();
                Vector3 d1 = (nearbyRailSnaps[1].position - t.position); d1.y = 0f; d1.Normalize();
                float dot = Vector3.Dot(d0, d1);

                if (dot > -0.7f) // angle < ~135° → corner → V1C
                    toReplaceTPillarWithC.Add((t.gameObject, nearbyRailSnaps[0], nearbyRailSnaps[1]));
                else
                    toReplaceTPillarWithM.Add(t.gameObject);
            }
            else if (connectedRailCount == 1)
            {
                // Lost 2 connections → replace with V1M (single-axis pillar)
                toReplaceTPillarWithM.Add(t.gameObject);
            }
            else
            {
                // No connections → delete
                toDelete.Add(t.gameObject);
            }
            continue;
        }

        // Other pillars: check snap occupancy
        bool bothOccupied = occ1 && occ2;
        bool hasFreeSnap = !occ1 || !occ2;

        if (bothOccupied)
        {
            // Both snaps still connected -> no action needed
            continue;
        }
        else if (hasFreeSnap && (occ1 || occ2))
        {
            // One snap free, one occupied -> REPLACE with V1E
            toReplace.Add(t.gameObject);
        }
        else
        {
            // Both snaps free -> DELETE
            toDelete.Add(t.gameObject);
        }
    }

    // --- DELETE pillars ---
    foreach (var pillar in toDelete)
    {
        if (!pillar) continue;
        protectedPillarIds.Remove(pillar.StableId());
        QueueForDeferredDestroy(pillar);
    }

    // --- REPLACE pillars with V1E ---
    if (toReplace.Count > 0)
    {
        var pillarEPrefab = FindAsset<GameObject>($"pillar_{(DetectVariantFromBalustradeRoot(balustradeRoot) == ContinueVariant.V2 ? "V2" : "V1")}E_PREFAB");
        if (!pillarEPrefab)
            return;

        foreach (var oldPillar in toReplace)
        {
            if (!oldPillar) continue;

            Transform parent = oldPillar.transform.parent;
            int sibling = oldPillar.transform.GetSiblingIndex();

            // Preserve top choice from the pillar we replace
            int topIdx = GetTopIndexFromPillar(oldPillar);

            // Find which side is still connected to a rail
            var oldT = oldPillar.transform;
            var oldS1 = FindSnap(oldT, "SnapPoint1");
            var oldS2 = FindSnap(oldT, "SnapPoint2");

            Transform connectedRailSnap = null;

            if (oldS1)
            {
                var k = PosKey(oldS1.position);
                if (railSnapByPos.TryGetValue(k, out var rs))
                    connectedRailSnap = rs;
            }

            if (!connectedRailSnap && oldS2)
            {
                var k = PosKey(oldS2.position);
                if (railSnapByPos.TryGetValue(k, out var rs))
                    connectedRailSnap = rs;
            }

            // Distance fallback after variant switch drift
            if (!connectedRailSnap)
            {
                float bestDist = 0.01f;
                foreach (var kvp in railSnapByPos)
                {
                    if (oldS1)
                    {
                        float d = Vector3.Distance(oldS1.position, kvp.Value.position);
                        if (d < bestDist) { bestDist = d; connectedRailSnap = kvp.Value; }
                    }
                    if (oldS2)
                    {
                        float d = Vector3.Distance(oldS2.position, kvp.Value.position);
                        if (d < bestDist) { bestDist = d; connectedRailSnap = kvp.Value; }
                    }
                }
            }

            // Create new V1E
            var newPillar = InstantiateAndSwap(pillarEPrefab);
            TransferIndex(balustradeRoot, oldPillar, newPillar);
            ApplyCurrentTextureVariantToObject(newPillar);
            Undo.RegisterCreatedObjectUndo(newPillar, "Remove Rail");

            if (parent)
                newPillar.transform.SetParent(parent, true);
            newPillar.transform.SetSiblingIndex(sibling);

            newPillar.transform.SetPositionAndRotation(oldPillar.transform.position, oldPillar.transform.rotation);
            newPillar.transform.localScale = oldPillar.transform.localScale;
            var v1eSnap1 = FindSnap(newPillar.transform, PillarSnapName);
            if (v1eSnap1 && connectedRailSnap)
            {
                Vector3 toDir = -connectedRailSnap.right;
                toDir.y = 0f;
                if (toDir.sqrMagnitude > 1e-6f)
                {
                    newPillar.transform.rotation =
                        YawDelta(v1eSnap1.right, toDir.normalized) * newPillar.transform.rotation;
                }

                newPillar.transform.position += connectedRailSnap.position - v1eSnap1.position;
            }

            // Apply top back
            if (topIdx >= 0 && topIdx < TopPrefabNames.Length)
            {
                var snapTop = FindSnap(newPillar.transform, TopSnapName);
                var topPrefab = FindAsset<GameObject>(TopPrefabNames[topIdx]);
                if (snapTop && topPrefab)
                {
                    RemoveTopFromPillar(newPillar.transform);

                    var top = InstantiateAndSwap(topPrefab);
                    ApplyCurrentTextureVariantToObject(top);
                    Undo.RegisterCreatedObjectUndo(top, "Restore Top");

                    top.transform.SetParent(newPillar.transform, false);
                    top.transform.position = snapTop.position;
                    top.transform.rotation = snapTop.rotation;

                    ApplyTopVisualRotation(top);
                }
            }

            // Remove old pillar
            protectedPillarIds.Remove(oldPillar.StableId());
            QueueForDeferredDestroy(oldPillar);
        }
    }

    // --- REPLACE V1T/V2T pillars with V1M/V2M (T-branch rail deleted) ---
    if (toReplaceTPillarWithM.Count > 0)
    {
        string v = DetectVariantFromBalustradeRoot(balustradeRoot) == ContinueVariant.V2 ? "V2" : "V1";
        var pillarMPrefab = FindAsset<GameObject>($"pillar_{v}M_PREFAB");
        if (pillarMPrefab)
        {
            foreach (var oldPillar in toReplaceTPillarWithM)
            {
                if (!oldPillar) continue;

                Transform parent = oldPillar.transform.parent;
                int sibling = oldPillar.transform.GetSiblingIndex();
                int topIdx = GetTopIndexFromPillar(oldPillar);

                // V1M uses SnapPoint1 + SnapPoint2, same as V1T main axis
                var newPillar = InstantiateAndSwap(pillarMPrefab);
                TransferIndex(balustradeRoot, oldPillar, newPillar);
                ApplyCurrentTextureVariantToObject(newPillar);
                Undo.RegisterCreatedObjectUndo(newPillar, "Replace V1T With V1M");

                if (parent)
                    newPillar.transform.SetParent(parent, true);
                newPillar.transform.SetSiblingIndex(sibling);

                newPillar.transform.SetPositionAndRotation(
                    oldPillar.transform.position,
                    oldPillar.transform.rotation
                );
                newPillar.transform.localScale = oldPillar.transform.localScale;
                if (topIdx >= 0 && topIdx < TopPrefabNames.Length)
                {
                    var snapTop = FindSnap(newPillar.transform, TopSnapName);
                    var topPrefab = FindAsset<GameObject>(TopPrefabNames[topIdx]);
                    if (snapTop && topPrefab)
                    {
                        RemoveTopFromPillar(newPillar.transform);

                        var top = InstantiateAndSwap(topPrefab);
                        ApplyCurrentTextureVariantToObject(top);
                        Undo.RegisterCreatedObjectUndo(top, "Restore Top");

                        top.transform.SetParent(newPillar.transform, false);
                        top.transform.position = snapTop.position;
                        top.transform.rotation = snapTop.rotation;

                        ApplyTopVisualRotation(top);
                    }
                }

                // Remove old T-pillar
                protectedPillarIds.Remove(oldPillar.StableId());
                QueueForDeferredDestroy(oldPillar);
            }
        }
    }

    // --- REPLACE V1T/V2T pillars with V1C/V2C (corner case: branch rail kept) ---
    if (toReplaceTPillarWithC.Count > 0)
    {
        string v = DetectVariantFromBalustradeRoot(balustradeRoot) == ContinueVariant.V2 ? "V2" : "V1";
        var pillarCPrefab = FindAsset<GameObject>($"pillar_{v}C_PREFAB");
        if (pillarCPrefab)
        {
            foreach (var (oldPillar, rs0, rs1) in toReplaceTPillarWithC)
            {
                if (!oldPillar) continue;

                Transform parent = oldPillar.transform.parent;
                int sibling = oldPillar.transform.GetSiblingIndex();
                int topIdx = GetTopIndexFromPillar(oldPillar);

                var corner = InstantiateAndSwap(pillarCPrefab);
                TransferIndex(balustradeRoot, oldPillar, corner);
                ApplyCurrentTextureVariantToObject(corner);
                Undo.RegisterCreatedObjectUndo(corner, "Replace V1T With V1C");

                if (parent)
                    corner.transform.SetParent(parent, true);
                corner.transform.SetSiblingIndex(sibling);
                corner.transform.localScale = oldPillar.transform.localScale;

                // Orient V1C to best match both connected rail snaps
                var cSnap1 = FindSnap(corner.transform, "SnapPoint1");
                var cSnap2 = FindSnap(corner.transform, "SnapPoint2");

                if (cSnap1 && cSnap2)
                {
                    Quaternion origRot = corner.transform.rotation;
                    Vector3 origPos = corner.transform.position;
                    float bestScore = float.MinValue;
                    Quaternion bestRot = origRot;
                    Vector3 bestPos = origPos;

                    Transform[] cArr = { cSnap1, cSnap2 };
                    Transform[] rArr = { rs0, rs1 };

                    // Try all 4 snap-to-rail configurations, pick best alignment
                    for (int ci = 0; ci < 2; ci++)
                    {
                        for (int ri = 0; ri < 2; ri++)
                        {
                            var cIn = cArr[ci];
                            var cOut = cArr[1 - ci];
                            var rIn = rArr[ri];
                            var rOut = rArr[1 - ri];

                            corner.transform.SetPositionAndRotation(origPos, origRot);
                            corner.transform.rotation =
                                YawDelta(cIn.right, -rIn.right) * corner.transform.rotation;
                            corner.transform.position += rIn.position - cIn.position;

                            float score = Vector3.Dot(cOut.right.normalized, -rOut.right.normalized);
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestRot = corner.transform.rotation;
                                bestPos = corner.transform.position;
                            }
                        }
                    }

                    corner.transform.SetPositionAndRotation(bestPos, bestRot);
                }

                // Restore top
                if (topIdx >= 0 && topIdx < TopPrefabNames.Length)
                {
                    var snapTop = FindSnap(corner.transform, TopSnapName);
                    var topPrefab = FindAsset<GameObject>(TopPrefabNames[topIdx]);
                    if (snapTop && topPrefab)
                    {
                        RemoveTopFromPillar(corner.transform);
                        var top = InstantiateAndSwap(topPrefab);
                        ApplyCurrentTextureVariantToObject(top);
                        Undo.RegisterCreatedObjectUndo(top, "Restore Top");
                        top.transform.SetParent(corner.transform, false);
                        top.transform.position = snapTop.position;
                        top.transform.rotation = snapTop.rotation;
                        ApplyTopVisualRotation(top);
                    }
                }

                protectedPillarIds.Remove(oldPillar.StableId());
                QueueForDeferredDestroy(oldPillar);
            }
        }
    }

    // Check if balustrade is now empty (no rails left) -> delete root and remove from list
    bool hasRailsLeft = false;
    foreach (Transform t in balustradeRoot.GetComponentsInChildren<Transform>(true))
    {
        if (!t) continue;
        var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (!src) continue;
        
        string n = src.name;
        if ((n.StartsWith("blstrs_") || n.StartsWith("blstrsCrvd_")) && n.EndsWith("_PREFAB"))
        {
            hasRailsLeft = true;
            break;
        }
    }
    
    if (!hasRailsLeft)
    {
        // Remove from tracking lists
        finalizedBalustrades.Remove(balustradeRoot);
        balustradeStartPillars.Remove(balustradeRoot);
        chainIndexByRoot.Remove(balustradeRoot);
        deletedRailIdsByRoot.Remove(balustradeRoot);
        
        // Delete the empty root
        QueueForDeferredDestroy(balustradeRoot);
    }

    // Rebuild chain index for this balustrade (topology changed)
    if (hasRailsLeft)
        RebuildChainIndexForRoot(balustradeRoot);

    // Reapply Full Detail to newly created replacement pillars
    if (hasRailsLeft && fullDetailMode)
        ApplyFullDetailToBalustrade(balustradeRoot, true);

    // Rebuild protection cache
    RebuildProtectedPillarIdCache();

    FlushDeferredDestroys();
}

// --- Curved/45 rail-delete: hide adjacent pillars + spawn visible end-caps ---

// Returns true if the rail being deleted is curved, or is a straight rail
// attached to at least one V1C45/V2C45 corner pillar. These cases need the
// hide-pillars-and-spawn-endcap flow because the curved/diagonal geometry
// scales differently across V1<->V2 than straight rail segments, and the
// standard pillar repair (which destroys M-pillars) breaks BFS continuity
// across the gap during variant switches.
bool IsCurvedOr45RailDelete(GameObject hiddenRail, GameObject root)
{
    if (!hiddenRail || !root) return false;
    var src = PrefabUtility.GetCorrespondingObjectFromSource(hiddenRail);
    if (!src) return false;

    if (src.name.StartsWith("blstrsCrvd_"))
        return true;

    if (!src.name.StartsWith("blstrs_"))
        return false;

    // Straight rail: check if any adjacent pillar is C45.
    var rs1 = FindSnap(hiddenRail.transform, RailStartSnap);
    var rs2 = FindSnap(hiddenRail.transform, RailEndSnap);
    if (!rs1 && !rs2) return false;

    float scaleFactor = Mathf.Max(root.transform.lossyScale.x, 1f);
    float tol = 0.2f * scaleFactor;

    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (!t) continue;
        var p = t.gameObject;
        if (!IsPillarInstance(p)) continue;
        if (IsHiddenDeletedPillar(p)) continue;

        var psrc = PrefabUtility.GetCorrespondingObjectFromSource(p);
        if (!psrc || !psrc.name.Contains("C45")) continue;

        var ps1 = FindSnap(t, "SnapPoint1");
        var ps2 = FindSnap(t, "SnapPoint2");
        foreach (var ps in new[] { ps1, ps2 })
        {
            if (!ps) continue;
            if (rs1 && Vector3.Distance(ps.position, rs1.position) < tol) return true;
            if (rs2 && Vector3.Distance(ps.position, rs2.position) < tol) return true;
        }
    }
    return false;
}

// For each pillar adjacent to the just-hidden rail: spawn a visible V1E
// end-cap at the same position (using the same SnapPoint1-to-survivingRail
// alignment that RepairEndPillarsAfterRailDelete would apply), then hide
// the original pillar so it remains in the topology with all snaps intact.
void HidePillarsAndSpawnEndCapsForHiddenRail(GameObject root, GameObject hiddenRail)
{
    if (!root || !hiddenRail) return;

    var rs1 = FindSnap(hiddenRail.transform, RailStartSnap);
    var rs2 = FindSnap(hiddenRail.transform, RailEndSnap);
    if (!rs1 && !rs2) return;

    float scaleFactor = Mathf.Max(root.transform.lossyScale.x, 1f);
    float tol = 0.2f * scaleFactor;

    // Find adjacent pillars by snap proximity to either rail end.
    var adjacents = new List<GameObject>();
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (!t) continue;
        var p = t.gameObject;
        if (!IsPillarInstance(p)) continue;
        if (IsHiddenDeletedPillar(p)) continue;

        var ps1 = FindSnap(t, "SnapPoint1");
        var ps2 = FindSnap(t, "SnapPoint2");
        bool adj = false;
        foreach (var ps in new[] { ps1, ps2 })
        {
            if (!ps) continue;
            if (rs1 && Vector3.Distance(ps.position, rs1.position) < tol) { adj = true; break; }
            if (rs2 && Vector3.Distance(ps.position, rs2.position) < tol) { adj = true; break; }
        }
        if (adj && !adjacents.Contains(p)) adjacents.Add(p);
    }

    string variant = DetectVariantFromBalustradeRoot(root) == ContinueVariant.V2 ? "V2" : "V1";
    var pillarEPrefab = FindAsset<GameObject>($"pillar_{variant}E_PREFAB");
    if (!pillarEPrefab) return;

    foreach (var oldPillar in adjacents)
    {
        SpawnEndCapAndHidePillar(root, oldPillar, hiddenRail, pillarEPrefab, tol);
    }
}

void SpawnEndCapAndHidePillar(GameObject root, GameObject oldPillar, GameObject hiddenRail,
                              GameObject pillarEPrefab, float tol)
{
    var oldT = oldPillar.transform;
    var oldSrc = PrefabUtility.GetCorrespondingObjectFromSource(oldPillar);

    // If the original pillar was a T (3 rail slots), losing ONE rail leaves
    // 2 connections → correct repair type is M, not E. Also: the T's current
    // rotation already has SnapPoint1/2 aligned with the two surviving rails,
    // so spawning an M at the same transform matches automatically.
    bool isT = oldSrc && (oldSrc.name.Contains("V1T") || oldSrc.name.Contains("V2T"));
    GameObject repairPrefab = pillarEPrefab;
    if (isT)
    {
        string variant = (oldSrc.name.Contains("V2")) ? "V2" : "V1";
        var mPrefab = FindAsset<GameObject>($"pillar_{variant}M_PREFAB");
        if (mPrefab) repairPrefab = mPrefab;
    }
    var ps1 = FindSnap(oldT, "SnapPoint1");
    var ps2 = FindSnap(oldT, "SnapPoint2");
    var hrs1 = FindSnap(hiddenRail.transform, RailStartSnap);
    var hrs2 = FindSnap(hiddenRail.transform, RailEndSnap);

    // Identify the pillar snap that was attached to the (now hidden) rail and
    // the snap on the surviving side.
    Transform survivingPillarSnap = null;
    foreach (var ps in new[] { ps1, ps2 })
    {
        if (!ps) continue;
        bool nearHidden =
            (hrs1 && Vector3.Distance(ps.position, hrs1.position) < tol) ||
            (hrs2 && Vector3.Distance(ps.position, hrs2.position) < tol);
        if (!nearHidden)
        {
            survivingPillarSnap = ps;
            break;
        }
    }

    // Find the surviving rail's snap that is connected to survivingPillarSnap.
    Transform survivingRailSnap = null;
    if (survivingPillarSnap)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!t || !IsRailInstance(t.gameObject)) continue;
            if (IsHiddenDeletedRail(t.gameObject)) continue;
            var s1 = FindSnap(t, RailStartSnap);
            var s2 = FindSnap(t, RailEndSnap);
            if (s1 && Vector3.Distance(s1.position, survivingPillarSnap.position) < tol)
            { survivingRailSnap = s1; break; }
            if (s2 && Vector3.Distance(s2.position, survivingPillarSnap.position) < tol)
            { survivingRailSnap = s2; break; }
        }
    }

    // Spawn repair pillar at the old pillar's transform
    var endcap = InstantiateAndSwap(repairPrefab);
    ApplyCurrentTextureVariantToObject(endcap);
    Undo.RegisterCreatedObjectUndo(endcap, "Spawn Repair Pillar");

    endcap.transform.SetParent(root.transform, worldPositionStays: true);
    endcap.transform.SetPositionAndRotation(oldT.position, oldT.rotation);
    endcap.transform.localScale = oldT.localScale;

    // For T→M: rotation of the old T already aligns SnapPoint1/2 with the
    // two surviving rails, so no rotation adjustment is needed.
    if (!isT)
    {
        var endSnap1 = FindSnap(endcap.transform, PillarSnapName);
        if (endSnap1 && survivingRailSnap)
        {
            Vector3 toDir = -survivingRailSnap.right;
            toDir.y = 0f;
            if (toDir.sqrMagnitude > 1e-6f)
            {
                endcap.transform.rotation =
                    YawDelta(endSnap1.right, toDir.normalized) * endcap.transform.rotation;
            }
            endcap.transform.position += survivingRailSnap.position - endSnap1.position;
        }
    }

    // Carry over the top from the original pillar
    int topIdx = GetTopIndexFromPillar(oldPillar);
    if (topIdx >= 0 && topIdx < TopPrefabNames.Length)
    {
        var snapTop = FindSnap(endcap.transform, TopSnapName);
        var topPrefab = FindAsset<GameObject>(TopPrefabNames[topIdx]);
        if (snapTop && topPrefab)
        {
            var top = InstantiateAndSwap(topPrefab);
            ApplyCurrentTextureVariantToObject(top);
            Undo.RegisterCreatedObjectUndo(top, "Replace Rail");
            top.transform.SetParent(endcap.transform, false);
            top.transform.position = snapTop.position;
            top.transform.rotation = snapTop.rotation;
            ApplyTopVisualRotation(top);
        }
    }

    // Hide (don't destroy) the original pillar so it stays in the topology
    Undo.RegisterCompleteObjectUndo(oldPillar, "Hide Adjacent Pillar");
    HideDeletedPillar(oldPillar);
}

}
} // namespace WB3DAssets.BalustradeModularSystem
