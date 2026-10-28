using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace WB3DAssets.BalustradeModularSystem
{
public partial class BalustradeModularWindow
{
    // Register scene-opened callback at project start (works without tool window)
    [InitializeOnLoadMethod]
    static void RegisterPipelineSwapOnSceneOpen()
    {
        EditorSceneManager.sceneOpened -= OnSceneOpenedStatic;
        EditorSceneManager.sceneOpened += OnSceneOpenedStatic;
        // Also swap right after package import / domain reload, so the prefabs
        // are pipeline-correct before the user ever opens a scene.
        EditorApplication.delayCall += EnsurePipelineMaterials;
    }

    static void OnSceneOpenedStatic(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
    {
        EditorApplication.delayCall += EnsurePipelineMaterials;
    }

    // The Asset Store package must ship with the prefabs referencing the Shared
    // (Standard) materials, so the swap must never run in the project the package
    // is uploaded from. That project carries a marker file in ProjectSettings.
    // ProjectSettings is never part of an Asset Store package, so no buyer can
    // have it. Checking for the Asset Store Publishing Tools instead would also
    // switch the swap off for every buyer who is a publisher himself.
    const string PublisherMarker = "ProjectSettings/WB3DAssets_PublisherProject.txt";

    static bool IsPublisherProject() => System.IO.File.Exists(PublisherMarker);

    // Static material cache: Shared name → pipeline-correct material
    static Dictionary<string, Material> s_pipelineMatCache;

    static void BuildPipelineMatCache()
    {
        s_pipelineMatCache = new Dictionary<string, Material>();
        // Pipeline-specific folder first; Shared (Standard) as the only
        // fallback — never a foreign pipeline's materials via a Root-wide scan.
        string[] folders = { PipelineRoot, Root + "/Shared" };

        foreach (var folder in folders)
        {
            if (!AssetDatabase.IsValidFolder(folder)) continue;
            foreach (var mg in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                var mp = AssetDatabase.GUIDToAssetPath(mg);
                var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (m && !s_pipelineMatCache.ContainsKey(m.name))
                    s_pipelineMatCache[m.name] = m;
            }
        }
    }

    // Swap prefab materials to match active pipeline and save to disk
    static void EnsurePipelineMaterials()
    {
        if (IsPublisherProject()) return;
        if (!AssetDatabase.IsValidFolder(Root)) return;
        BuildPipelineMatCache();

        bool anyChanged = false;
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { Root });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab) continue;

            bool prefabChanged = false;
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (SwapRendererMats(r)) prefabChanged = true;
            }
            if (prefabChanged) { EditorUtility.SetDirty(prefab); anyChanged = true; }
        }

        if (anyChanged) AssetDatabase.SaveAssets();
    }

    static bool SwapRendererMats(Renderer r)
    {
        if (s_pipelineMatCache == null) return false;
        var mats = r.sharedMaterials;
        bool changed = false;
        for (int i = 0; i < mats.Length; i++)
        {
            if (!mats[i]) continue;
            if (s_pipelineMatCache.TryGetValue(mats[i].name, out var found) && found != mats[i])
            { mats[i] = found; changed = true; }
        }
        if (changed) r.sharedMaterials = mats;
        return changed;
    }
bool IsV1PillarInstance(GameObject go)
{
    var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
    if (!src) return false;

    // Your V1 pillar prefab names are defined as constants above
    string n = src.name;
    return n == PillarPrefabName ||
           n == PillarMPrefabName ||
           n == PillarCPrefabName ||
           n == PillarC45PrefabName;
}

bool IsPillarInstance(GameObject go)
{
    var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
    if (!src) return false;

    string n = src.name;

    // Matches pillar_V1E_PREFAB, pillar_V2E_PREFAB, pillar_V1M_PREFAB, etc.
    return n.StartsWith("pillar_") && n.EndsWith("_PREFAB");
}

// --- Hidden-rail system ---
// When a user "deletes" a rail, we don't actually destroy it. We disable its
// renderers/colliders and mark it with a name suffix. The GameObject + all snaps
// stay alive in the hierarchy, so variant switches and BFS reposition see a
// complete balustrade. Curved rails and 45-degree pillars stay aligned because
// the snap topology is never broken.
const string HiddenRailSuffix = "__HIDDEN";
const string HiddenContainerName = "HIDDEN";

bool IsHiddenDeletedRail(GameObject go)
{
    return go && go.name.EndsWith(HiddenRailSuffix) && IsRailInstance(go);
}

bool IsHiddenDeletedPillar(GameObject go)
{
    return go && go.name.EndsWith(HiddenRailSuffix) && IsPillarInstance(go);
}

// Returns the dedicated child container under the balustrade root that
// collects all hidden bookkeeping objects (hidden rails + hidden pillars).
// Created on demand. Always pinned to the LAST sibling index so it appears
// at the bottom of the root in the Hierarchy.
Transform EnsureHiddenContainer(Transform root)
{
    if (!root) return null;
    var existing = root.Find(HiddenContainerName);
    if (existing)
    {
        existing.SetAsLastSibling();
        return existing;
    }

    var go = new GameObject(HiddenContainerName);
    Undo.RegisterCreatedObjectUndo(go, "Create Hidden Container");
    Undo.SetTransformParent(go.transform, root, "Parent Hidden Container");
    go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
    go.transform.localScale = Vector3.one;
    go.transform.SetAsLastSibling();
    return go.transform;
}

// Destroy hidden containers under all finalized balustrades that hold no
// children any more. Called from cleanup paths after rebuilds.
void DestroyEmptyHiddenContainers()
{
    foreach (var rootGO in finalizedBalustrades)
    {
        if (!rootGO) continue;
        var c = rootGO.transform.Find(HiddenContainerName);
        if (c && c.childCount == 0)
            QueueForDeferredDestroy(c.gameObject);
    }
}

// Force every existing HIDDEN container to the last sibling index under its
// balustrade root. Called after any operation that may have shifted sibling
// indices (commits, repairs, variant swaps) so the container always shows
// at the very bottom of the root in the Hierarchy. Schedules an additional
// delayed pin to win against any late hierarchy reorderings Unity does in
// the same frame (selection changes, undo grouping, etc.).
void PinHiddenContainersToBottom()
{
    PinHiddenContainersToBottomImmediate();
    EditorApplication.delayCall -= PinHiddenContainersToBottomImmediate;
    EditorApplication.delayCall += PinHiddenContainersToBottomImmediate;
}

void PinHiddenContainersToBottomImmediate()
{
    foreach (var rootGO in finalizedBalustrades)
    {
        if (!rootGO) continue;
        var c = rootGO.transform.Find(HiddenContainerName);
        if (c) c.SetAsLastSibling();
    }
}

void HideDeletedRail(GameObject rail)
{
    if (!rail || IsHiddenDeletedRail(rail)) return;
    foreach (var r in rail.GetComponentsInChildren<Renderer>(true))
        r.enabled = false;
    foreach (var c in rail.GetComponentsInChildren<Collider>(true))
        c.enabled = false;
    if (!rail.name.EndsWith(HiddenRailSuffix))
        rail.name += HiddenRailSuffix;

    // Move into the dedicated hidden container under the balustrade root.
    Transform root = rail.transform.parent;
    while (root && !finalizedBalustrades.Contains(root.gameObject))
        root = root.parent;
    if (root)
    {
        var container = EnsureHiddenContainer(root);
        if (container)
            Undo.SetTransformParent(rail.transform, container, "Move Rail To Hidden Container");
    }
}

void HideDeletedPillar(GameObject pillar)
{
    if (!pillar || IsHiddenDeletedPillar(pillar)) return;
    foreach (var r in pillar.GetComponentsInChildren<Renderer>(true))
        r.enabled = false;
    foreach (var c in pillar.GetComponentsInChildren<Collider>(true))
        c.enabled = false;
    if (!pillar.name.EndsWith(HiddenRailSuffix))
        pillar.name += HiddenRailSuffix;

    // Move into the dedicated hidden container under the balustrade root.
    Transform root = pillar.transform.parent;
    while (root && !finalizedBalustrades.Contains(root.gameObject))
        root = root.parent;
    if (root)
    {
        var container = EnsureHiddenContainer(root);
        if (container)
            Undo.SetTransformParent(pillar.transform, container, "Move Pillar To Hidden Container");
    }
}

void ShowDeletedPillar(GameObject pillar)
{
    if (!pillar) return;
    foreach (var r in pillar.GetComponentsInChildren<Renderer>(true))
        r.enabled = true;
    foreach (var c in pillar.GetComponentsInChildren<Collider>(true))
        c.enabled = true;
    if (pillar.name.EndsWith(HiddenRailSuffix))
        pillar.name = pillar.name.Substring(0, pillar.name.Length - HiddenRailSuffix.Length);
}

void ShowDeletedRail(GameObject rail)
{
    if (!rail) return;
    foreach (var r in rail.GetComponentsInChildren<Renderer>(true))
        r.enabled = true;
    foreach (var c in rail.GetComponentsInChildren<Collider>(true))
        c.enabled = true;
    if (rail.name.EndsWith(HiddenRailSuffix))
        rail.name = rail.name.Substring(0, rail.name.Length - HiddenRailSuffix.Length);
}

// Called after committing a new (visible) rail. Destroys any hidden rails,
// hidden pillars and visible repair-V1E pillars across ALL finalized
// balustrades whose snap positions match either end of the new rail.
// Searching across all roots (instead of walking the new rail's parent
// chain) is necessary because freshly committed rails are not yet parented
// under their final balustrade root at this point in the commit pipeline.
void CleanupHiddenNeighborsForCommittedRail(GameObject newRail)
{
    if (!newRail) return;

    var nrs1 = FindSnap(newRail.transform, RailStartSnap);
    var nrs2 = FindSnap(newRail.transform, RailEndSnap);
    if (!nrs1 && !nrs2) return;

    foreach (var rootGO in finalizedBalustrades)
    {
        if (!rootGO) continue;
        Transform root = rootGO.transform;
        CleanupHiddenNeighborsUnderRoot(root, newRail, nrs1, nrs2);
    }

    DestroyEmptyHiddenContainers();
}

void CleanupHiddenNeighborsUnderRoot(Transform root, GameObject newRail,
                                     Transform nrs1, Transform nrs2)
{
    float scaleFactor = Mathf.Max(root.lossyScale.x, 1f);
    float tol = 0.25f * scaleFactor;

    var toDestroy = new List<GameObject>();
    var hiddenRailEndPositions = new List<Vector3>();

    // PASS 1: hidden rails whose span matches the new rail span
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (!t) continue;
        var go = t.gameObject;
        if (go == newRail) continue;
        if (currentBuildObjects.Contains(go)) continue;
        if (!IsHiddenDeletedRail(go)) continue;

        var hrs1 = FindSnap(t, RailStartSnap);
        var hrs2 = FindSnap(t, RailEndSnap);
        if (!hrs1 || !hrs2) continue;
        bool matchAB =
            (nrs1 && Vector3.Distance(hrs1.position, nrs1.position) < tol) &&
            (nrs2 && Vector3.Distance(hrs2.position, nrs2.position) < tol);
        bool matchBA =
            (nrs1 && Vector3.Distance(hrs2.position, nrs1.position) < tol) &&
            (nrs2 && Vector3.Distance(hrs1.position, nrs2.position) < tol);
        if (matchAB || matchBA)
        {
            toDestroy.Add(go);
            hiddenRailEndPositions.Add(hrs1.position);
            hiddenRailEndPositions.Add(hrs2.position);
        }
    }

    // PASS 2: hidden pillars and visible repair-V1Es near new rail OR
    // near the just-flagged hidden rails' end snaps.
    float pillarTol = tol * 4f;
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (!t) continue;
        var go = t.gameObject;
        if (go == newRail) continue;
        if (currentBuildObjects.Contains(go)) continue;
        if (toDestroy.Contains(go)) continue;
        if (!IsPillarInstance(go) && !IsHiddenDeletedPillar(go)) continue;

        bool isHidden = IsHiddenDeletedPillar(go);
        bool isRepairTwin = !isHidden && FindHiddenTwinUnderSameRoot(go) != null;
        if (!isHidden && !isRepairTwin) continue;

        Vector3 cp = t.position;
        float dMin = float.MaxValue;
        if (nrs1) dMin = Mathf.Min(dMin, Vector3.Distance(cp, nrs1.position));
        if (nrs2) dMin = Mathf.Min(dMin, Vector3.Distance(cp, nrs2.position));
        foreach (var rp in hiddenRailEndPositions)
            dMin = Mathf.Min(dMin, Vector3.Distance(cp, rp));

        if (dMin < pillarTol)
            toDestroy.Add(go);
    }

    foreach (var go in toDestroy)
    {
        if (!go) continue;
        protectedPillarIds.Remove(go.StableId());
        QueueForDeferredDestroy(go);
    }
}

// Find a NON-hidden rail under the root whose snap positions match the given
// two world positions. Used by the multi-rail hide flow to identify each
// freshly revived rail after Undo.PerformUndo() so we can re-hide it.
GameObject FindLiveRailBySnapPositions(GameObject root, Vector3 p1, Vector3 p2)
{
    if (!root) return null;
    float scaleFactor = Mathf.Max(root.transform.lossyScale.x, 1f);
    float tol = 0.15f * scaleFactor;
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (!t || !IsRailInstance(t.gameObject)) continue;
        if (IsHiddenDeletedRail(t.gameObject)) continue;
        var s1 = FindSnap(t, RailStartSnap);
        var s2 = FindSnap(t, RailEndSnap);
        if (!s1 || !s2) continue;
        bool matchAB = Vector3.Distance(s1.position, p1) < tol && Vector3.Distance(s2.position, p2) < tol;
        bool matchBA = Vector3.Distance(s1.position, p2) < tol && Vector3.Distance(s2.position, p1) < tol;
        if (matchAB || matchBA) return t.gameObject;
    }
    return null;
}

// Find a hidden rail under the root whose snap positions match the given two world positions
GameObject FindHiddenRailMatching(GameObject root, Vector3 p1, Vector3 p2)
{
    if (!root) return null;
    float scaleFactor = Mathf.Max(root.transform.lossyScale.x, 1f);
    float tol = 0.15f * scaleFactor;
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (!t || !IsHiddenDeletedRail(t.gameObject)) continue;
        if (!IsRailInstance(t.gameObject)) continue;
        var s1 = FindSnap(t, RailStartSnap);
        var s2 = FindSnap(t, RailEndSnap);
        if (!s1 || !s2) continue;
        bool matchAB = Vector3.Distance(s1.position, p1) < tol && Vector3.Distance(s2.position, p2) < tol;
        bool matchBA = Vector3.Distance(s1.position, p2) < tol && Vector3.Distance(s2.position, p1) < tol;
        if (matchAB || matchBA) return t.gameObject;
    }
    return null;
}

// --- Ghost-rail marker system ---
// When a rail is deleted, its snap world poses are persisted as a hidden child
// GameObject under the balustrade root. Variant switches read these markers and
// treat them as real rails, so repositioning across deleted-rail gaps is exact.
// Markers are removed automatically when a new rail is placed at the same poses.
const string GhostRailMarkerPrefix = "__DeletedRail_";

void MaterializeGhostRailMarkersForRoot(GameObject root)
{
    if (!root) return;

    // Build list of markers to create from pendingRailSnaps matching this root
    var toRemove = new List<BalId>();
    foreach (var kvp in pendingRailSnaps)
    {
        var data = kvp.Value;
        if (data.root != root) continue;

        // Skip if a marker at these snap poses already exists (avoid duplicates)
        if (FindGhostRailMarkerMatching(root, data.snap1Pos, data.snap2Pos) != null)
        {
            toRemove.Add(kvp.Key);
            continue;
        }

        int idx = 0;
        while (root.transform.Find(GhostRailMarkerPrefix + idx) != null) idx++;

        var marker = new GameObject(GhostRailMarkerPrefix + idx);
        marker.transform.SetParent(root.transform, worldPositionStays: true);
        marker.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInBuild;
        marker.SetActive(false); // hidden runtime, but still iterable via GetComponentsInChildren(true)

        // Two child transforms carry the snap poses
        var s1 = new GameObject("Snap1");
        s1.transform.SetParent(marker.transform, worldPositionStays: false);
        s1.transform.SetPositionAndRotation(data.snap1Pos, data.snap1Rot);

        var s2 = new GameObject("Snap2");
        s2.transform.SetParent(marker.transform, worldPositionStays: false);
        s2.transform.SetPositionAndRotation(data.snap2Pos, data.snap2Rot);

        // Tag curvature via marker name suffix for later reading
        if (data.isCurved) marker.name += "_curved";

        toRemove.Add(kvp.Key);
    }
    foreach (var id in toRemove) pendingRailSnaps.Remove(id);
}

// Find a ghost-rail marker under root whose snap poses match the given positions (within tol).
GameObject FindGhostRailMarkerMatching(GameObject root, Vector3 p1, Vector3 p2)
{
    if (!root) return null;
    float tol = 0.15f * Mathf.Max(root.transform.localScale.x, 1f);
    foreach (Transform child in root.transform)
    {
        if (!child || !child.name.StartsWith(GhostRailMarkerPrefix)) continue;
        var s1 = child.Find("Snap1");
        var s2 = child.Find("Snap2");
        if (!s1 || !s2) continue;
        bool matchAB = Vector3.Distance(s1.position, p1) < tol && Vector3.Distance(s2.position, p2) < tol;
        bool matchBA = Vector3.Distance(s1.position, p2) < tol && Vector3.Distance(s2.position, p1) < tol;
        if (matchAB || matchBA) return child.gameObject;
    }
    return null;
}

// Remove ghost-rail marker(s) whose snap poses match a freshly placed rail.
// Called after placing any real rail so the marker list stays clean.
void ConsumeGhostRailMarkerIfMatch(GameObject root, GameObject realRail)
{
    if (!root || !realRail) return;
    var s1 = FindSnap(realRail.transform, RailStartSnap);
    var s2 = FindSnap(realRail.transform, RailEndSnap);
    if (!s1 || !s2) return;
    var marker = FindGhostRailMarkerMatching(root, s1.position, s2.position);
    if (marker) QueueForDeferredDestroy(marker);
}

bool IsTopInstance(GameObject go)
{
    var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
    if (!src) return false;

    string n = src.name;
    return n == TopPrefabNames[0] ||
           n == TopPrefabNames[1] ||
           n == TopPrefabNames[2] ||
           n == TopPrefabNames[3];
}

void RemoveTopFromPillar(Transform pillar)
{
    for (int i = pillar.childCount - 1; i >= 0; i--)
    {
        var c = pillar.GetChild(i).gameObject;
        if (IsTopInstance(c))
            QueueForDeferredDestroy(c);
    }
}

void CenterBalustradePivot(GameObject root)
{
    var renderers = root.GetComponentsInChildren<Renderer>();
    if (renderers.Length == 0)
        return;

    Bounds bounds = renderers[0].bounds;
    for (int i = 1; i < renderers.Length; i++)
        bounds.Encapsulate(renderers[i].bounds);

Vector3 center = new Vector3(
    bounds.center.x,
    bounds.min.y,
    bounds.center.z
);
    Vector3 delta = root.transform.position - center;

    // move root to center
    root.transform.position = center;

    // keep children world positions
    foreach (Transform child in root.transform)
        child.position += delta;
}

// Parity with the full version: center a finalized balustrade's pivot when its
// root is selected. Self-heals balustrades whose pivot sits on the first placed
// element. Guarded so it never fires during a build / continue build.
void EnsureBalustradePivotCentered(GameObject root)
{
    if (!root || buildMode || continueAnchorActive || continueTargetBalustrade != null) return;
    var renderers = root.GetComponentsInChildren<Renderer>();
    if (renderers.Length == 0) return;
    Bounds bounds = renderers[0].bounds;
    for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
    Vector3 center = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
    if (Vector3.Distance(root.transform.position, center) < 0.01f) return; // already centered
    Undo.RegisterFullObjectHierarchyUndo(root, "Center Balustrade Pivot");
    Vector3 delta = root.transform.position - center;
    root.transform.position = center;
    foreach (Transform child in root.transform)
        child.position += delta;
}

void SetBalustradeGizmosVisible(bool visible)
{
    GizmoUtility.SetGizmoEnabled(typeof(LODGroup), visible, false);
    GizmoUtility.SetGizmoEnabled(typeof(BoxCollider), visible, false);
    GizmoUtility.SetGizmoEnabled(typeof(MeshCollider), visible, false);
}

bool IsExplicitBalustradeRootSelected()
{
    var sel = Selection.activeGameObject;
    if (!sel) return false;

    // The selection counts as "the balustrade is selected" if either the
    // root itself is picked, or any child prefab (rail, pillar, top, hidden
    // bookkeeping object) belonging to a tracked balustrade root. This lets
    // the user click any visible piece in the scene and immediately use the
    // UI (variant / top / baluster / texture switches) without first having
    // to select the parent root in the Hierarchy.
    return FindBalustradeRootFromSelection(sel) != null;
}

GameObject FindBalustradeRootFromSelection(GameObject go)
{
    if (!go) return null;

    // First try: check existing tracked balustrades
    Transform t = go.transform;
    while (t != null)
    {
        if (finalizedBalustrades.Contains(t.gameObject))
            return t.gameObject;
        t = t.parent;
    }

    // Lazy discovery: walk up to find a Balustrade_ root not yet tracked
    t = go.transform;
    while (t != null)
    {
        if (t.parent == null && t.name.StartsWith("Balustrade_"))
        {
            // Verify it has balustrade content before adding
            bool hasContent = false;
            foreach (Transform child in t.GetComponentsInChildren<Transform>(true))
            {
                if (child == t) continue;
                var src = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                if (!src) continue;
                string n = src.name;
                if (((n.StartsWith("pillar_") || n.StartsWith("blstrs_") || n.StartsWith("blstrsCrvd_"))
                     && n.EndsWith("_PREFAB")))
                { hasContent = true; break; }
            }

            if (hasContent)
            {
                finalizedBalustrades.Add(t.gameObject);
                RebuildChainIndexForRoot(t.gameObject);
                RebuildProtectedPillarIdCache();
                return t.gameObject;
            }
        }
        t = t.parent;
    }

    return null;
}

GameObject GetUiTargetBalustradeRoot()
{
    return FindBalustradeRootFromSelection(Selection.activeGameObject);
}

GameObject GetStartPillar(GameObject balustradeRoot)
{
    if (!balustradeRoot)
        return null;

    balustradeStartPillars.TryGetValue(balustradeRoot, out var pillar);
    return pillar;
}

Transform FindOwningBalustradeRoot(Transform t)
{
    if (!t) return null;
    var root = FindBalustradeRootFromSelection(t.gameObject);
    return root ? root.transform : null;
}

static bool IsPillarMPrefab(string prefabName)
{
    // Matches pillar_V1M_PREFAB, pillar_V2M_PREFAB, etc.
    return prefabName.StartsWith("pillar_") && prefabName.Contains("M_") && prefabName.EndsWith("_PREFAB");
}

string GetPillarPrefabName(char type)
{
    // type: 'E', 'M', 'T', 'C', '4' (45°)
    string v = selectedVariant == ContinueVariant.V2 ? "V2" : "V1";

    return type switch
    {
        'E' => $"pillar_{v}E_PREFAB",
        'M' => $"pillar_{v}M_PREFAB",
        'T' => $"pillar_{v}T_PREFAB",
        'C' => $"pillar_{v}C_PREFAB",
        '4' => $"pillar_{v}C45_PREFAB",
        _   => null
    };
}

bool IsSnapFree(Transform pillar, string snapName)
{
    var snap = FindSnap(pillar, snapName);
    if (!snap)
        return false;

    // Check if any rail or curved rail snap occupies this position
    var allSnaps = BalustradeIds.FindAll<Transform>();
    foreach (var t in allSnaps)
    {
        if (t == snap)
            continue;

        if (t.name != RailStartSnap && t.name != RailEndSnap)
            continue;

        if (Vector3.Distance(t.position, snap.position) < 0.001f)
            return false;
    }

    return true;
}

Transform FindLastRealRailFreeSnap()
{
    // 1) Session objects
    for (int i = currentBuildObjects.Count - 1; i >= 0; i--)
    {
        var go = currentBuildObjects[i];
        if (!go) continue;

        var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
        if (!src) continue;

if (src.name.StartsWith("blstrs_") && src.name.EndsWith("_PREFAB"))
            return FindSnap(go.transform, RailEndSnap);

if (src.name.StartsWith("blstrsCrvd_") && src.name.EndsWith("_PREFAB") && !string.IsNullOrEmpty(curvedOutSnapName))
            return FindSnap(go.transform, curvedOutSnapName);
    }

    // 2) Fallback: existing balustrade (Continue Abort without commits)
    if (continueTargetBalustrade)
    {
        var rails = continueTargetBalustrade.GetComponentsInChildren<Transform>(true);
        for (int i = rails.Length - 1; i >= 0; i--)
        {
            var src = PrefabUtility.GetCorrespondingObjectFromSource(rails[i]);
            if (!src) continue;

if (src.name.StartsWith("blstrs_") && src.name.EndsWith("_PREFAB"))
                return FindSnap(rails[i], RailEndSnap);

if (src.name.StartsWith("blstrsCrvd_") && src.name.EndsWith("_PREFAB") && !string.IsNullOrEmpty(curvedOutSnapName))
                return FindSnap(rails[i], curvedOutSnapName);
        }
    }

    return null;
}

void FinalizeAbortedNormalBuild()
{
    currentBuildObjects.Clear();
}

void FinalizeAbortedContinueBuild()
{
    currentBuildObjects.Clear();
}

    static void AlignRailToTarget(Transform root, Transform railSnap, Vector3 toTargetDir, Transform targetSnap)
    {
        if (!root || !railSnap || !targetSnap) return;
        root.rotation = YawDelta(railSnap.right, toTargetDir) * root.rotation;
        root.position += targetSnap.position - railSnap.position;
    }

    static void SetFullDetailOnObject(GameObject go, bool enable)
    {
        if (!go) return;

        var lodGroup = go.GetComponent<LODGroup>();
        if (!lodGroup) return; // No LODGroup -> nothing to do

        lodGroup.enabled = !enable;

        // Show/hide LOD1+ children
        foreach (Transform child in go.transform)
        {
            string n = child.name;
            if (n.StartsWith("LOD") && n != "LOD0")
                child.gameObject.SetActive(!enable);
        }
    }

    static void ApplyFullDetailToBalustrade(GameObject root, bool enable)
    {
        if (!root) return;

        // Process root itself
        SetFullDetailOnObject(root, enable);

        // Process all children recursively
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root.transform) continue;
            SetFullDetailOnObject(t.gameObject, enable);
        }
    }

    void ApplyFullDetailToAllBalustrades(bool enable)
    {
        foreach (var root in finalizedBalustrades)
            ApplyFullDetailToBalustrade(root, enable);
    }

    void ApplyFullDetailToCurrentBuild()
    {
        if (fullDetailMode)
        {
            foreach (var go in currentBuildObjects)
            {
                if (!go) continue;
                // Process object AND all children (e.g. tops with own LODGroup)
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    SetFullDetailOnObject(t.gameObject, true);
            }
        }

        // Cleanup hidden bookkeeping objects whose span was just rebuilt by
        // newly committed rails. Runs unconditionally because hidden cleanup
        // is independent of fullDetailMode.
        for (int i = currentBuildObjects.Count - 1; i >= 0; i--)
        {
            var go = currentBuildObjects[i];
            if (!go) continue;
            if (!IsRailInstance(go)) continue;
            if (IsHiddenDeletedRail(go)) continue;
            CleanupHiddenNeighborsForCommittedRail(go);
        }

        // Keep the HIDDEN container pinned to the bottom of every root after
        // any commit, since freshly placed rails / pillars shift sibling
        // indices around it.
        PinHiddenContainersToBottom();
    }


    static Vector3 MouseOnPlane(Vector2 mouse, Vector3 fallback)
    {
        var ray = HandleUtility.GUIPointToWorldRay(mouse);
        var plane = new Plane(Vector3.up, new Vector3(0, fallback.y, 0));
        return plane.Raycast(ray, out var d) ? ray.GetPoint(d) : fallback;
    }

    static Vector3 MouseOnSurface(Vector2 mouse, Vector3 fallback, out bool isFlat)
    {
        var ray = HandleUtility.GUIPointToWorldRay(mouse);

        // Raycast against all scene colliders
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
        {
            // Surface is flat when its normal points mostly upward
            isFlat = Vector3.Dot(hit.normal, Vector3.up) >= FlatNormalThreshold;
            return hit.point;
        }

        // No surface hit -> snap to Y0 plane
        isFlat = true;
        return MouseOnPlane(mouse, new Vector3(fallback.x, 0f, fallback.z));
    }

    static Vector3 PickDir(Vector3 delta, Vector3 last)
    {
        delta.y = 0;
        if (delta.sqrMagnitude < 0.0001f) return last;
        delta.Normalize();

        float rx = Vector3.Dot(delta, Vector3.right);
        float lx = Vector3.Dot(delta, Vector3.left);
        float fz = Vector3.Dot(delta, Vector3.forward);
        float bz = Vector3.Dot(delta, Vector3.back);

        if (rx >= lx && rx >= fz && rx >= bz) return Vector3.right;
        if (lx >= fz && lx >= bz) return Vector3.left;
        if (fz >= bz) return Vector3.forward;
        return Vector3.back;
    }

static void DrawDirGizmos(Vector3 pos, Vector3 active)
{
    DrawArrow(pos, Vector3.right,   active == Vector3.right);
    DrawArrow(pos, Vector3.left,    active == Vector3.left);
    DrawArrow(pos, Vector3.forward, active == Vector3.forward);
    DrawArrow(pos, Vector3.back,    active == Vector3.back);
}

static void DrawArrow(
    Vector3 pos,
    Vector3 dir,
    bool active,
    Color? overrideColor = null,
    float lengthMul = 1f
)
{
    float s = HandleUtility.GetHandleSize(pos);
    Handles.color = overrideColor ?? (active ? ActiveCol : BaseCol);

    float len = ArrowLength * lengthMul * s;
    Vector3 end = pos + dir.normalized * len;

    Handles.ConeHandleCap(
        0,
        end,
        Quaternion.LookRotation(dir),
        ArrowHeadSize * (active ? 1.15f : 1f) * s,
        EventType.Repaint
    );
}

static void DrawArcArrow90(
    Vector3 center,
    Vector3 fromDir,
    Vector3 normal,
    float radius,
    bool clockwise,
    Color col
)
{
    Handles.color = col;

float sweep = clockwise ? -45f : 45f;

Vector3 arcNormal = normal.normalized;

const float startTrimDeg = -30f; // trims the arc start
Vector3 startDir =
    Quaternion.AngleAxis(clockwise ? startTrimDeg : -startTrimDeg, arcNormal)
    * fromDir.normalized;

    Handles.DrawWireArc(
        center,
        arcNormal,
        startDir,
        sweep,
        radius
    );

    // arrow head
    Vector3 endDir =
        Quaternion.AngleAxis(sweep, arcNormal) * startDir;

    Vector3 endPos = center + endDir * radius;

Vector3 arrowDir = Vector3.Cross(arcNormal, endDir).normalized;
if (clockwise) arrowDir = -arrowDir;

Handles.ConeHandleCap(
    0,
    endPos,
    Quaternion.LookRotation(arrowDir, arcNormal),
    ArrowHeadSize * 1.2f * HandleUtility.GetHandleSize(endPos),
    EventType.Repaint
);
}

static void DrawVisualTurnArc90(
    Vector3 pos,
    Vector3 activeDir,
    bool turnRight,
    float radius,
    Color col
)
{
    Vector3 up = Vector3.up;
    Vector3 dir = activeDir.normalized;
    Vector3 side = Vector3.Cross(up, dir).normalized;

// 1) shared base starting point (both arrows same)
Vector3 sharedOrigin =
    pos
    + dir * radius;

// shared forward offset
float forwardOffset = radius * 0.01f;
sharedOrigin += dir * forwardOffset;

// 2) individual lateral offset PER arrow
float sideOffset = radius * -1.01f;

Vector3 localOrigin =
    turnRight
        ? sharedOrigin + side * sideOffset
        : sharedOrigin - side * sideOffset;

// 3) circle center relative to LOCAL origin
Vector3 center =
    turnRight
        ? localOrigin + side * radius
        : localOrigin - side * radius;

// 4) Start tangent stays local
const float startTrimDeg = -120f; // <-- HERE trim start

Vector3 rawDir = turnRight ? -side : side;
Vector3 fromDir =
    Quaternion.AngleAxis(
        turnRight ? startTrimDeg : -startTrimDeg,
        up
    ) * rawDir;

    Handles.color = col;

    float sweep = turnRight ? -45f : 45f;

    Handles.DrawWireArc(
        center,
        up,
        fromDir,
        sweep,
        radius
    );

    // Arrow head
    Vector3 endDir =
        Quaternion.AngleAxis(sweep, up) * fromDir;

    Vector3 endPos = center + endDir * radius;

    Vector3 arrowDir = Vector3.Cross(up, endDir).normalized;
    if (turnRight) arrowDir = -arrowDir;

    Handles.ConeHandleCap(
        0,
        endPos,
        Quaternion.LookRotation(arrowDir, up),
        ArrowHeadSize * 1.1f * HandleUtility.GetHandleSize(endPos),
        EventType.Repaint
    );
}

    static Transform FindSnap(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

T FindAsset<T>(string exactName) where T : Object
{
    // Prefabs: search only prefabs
    if (typeof(T) == typeof(GameObject))
    {
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { Root });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!asset)
                continue;

            if (asset.name == exactName)
                return asset as T;
        }
        return null;
    }

    // Materials & other assets: search pipeline-specific folder first (HDRP or URP)
    string pipelineFolder = PipelineRoot;
    string[] searchFolders = pipelineFolder != Root
        ? new[] { pipelineFolder, Root }  // pipeline folder first, then fallback
        : new[] { Root };                  // Built-in: only general root

    foreach (var folder in searchFolders)
    {
        if (!AssetDatabase.IsValidFolder(folder)) continue;

        var allGuids = AssetDatabase.FindAssets(exactName, new[] { folder });
        foreach (var guid in allGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) continue;

            if (asset.name == exactName)
                return asset;
        }
    }

    return null;
}

bool IsRailInstance(GameObject go)
{
    if (!go) return false;

    var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
    if (!src) return false;

    string n = src.name;

    // Straight and curved rails are identified by prefab naming
    bool isRail =
        (n.StartsWith("blstrs_") || n.StartsWith("blstrsCrvd_")) &&
        n.EndsWith("_PREFAB");

    return isRail;
}

static long PosKey(Vector3 p)
{
    // Quantize to avoid floating point noise (0.0001 units)
    int x = Mathf.RoundToInt(p.x * 10000f);
    int y = Mathf.RoundToInt(p.y * 10000f);
    int z = Mathf.RoundToInt(p.z * 10000f);

    // Pack into a single 64-bit key
    // Note: Using 21 bits per axis (fits typical scene scale). This is fine for modular placement.
    long key = 0;
    key |= ((long)(x & 0x1FFFFF) << 42);
    key |= ((long)(y & 0x1FFFFF) << 21);
    key |= ((long)(z & 0x1FFFFF));
    return key;
}

    static Quaternion YawDelta(Vector3 from, Vector3 to)
    {
        from.y = 0; to.y = 0;
        if (from.sqrMagnitude < 1e-6f || to.sqrMagnitude < 1e-6f) return Quaternion.identity;
        return Quaternion.LookRotation(to.normalized, Vector3.up) *
               Quaternion.Inverse(Quaternion.LookRotation(from.normalized, Vector3.up));
    }

    // Measure pillar M snap-to-snap width for accurate chain segment length
    float MeasurePillarWidth()
    {
        var prefab = FindAsset<GameObject>(GetPillarPrefabName('M'));
        if (!prefab) return 0f;
        var tmp = CreateGhost(prefab);
        var s1 = FindSnap(tmp.transform, PillarSnapName);
        var s2 = FindSnap(tmp.transform, "SnapPoint2");
        float w = (s1 && s2) ? Vector3.Distance(s1.position, s2.position) : 0f;
        DestroyImmediate(tmp);
        return w;
    }

    // Instantiate prefab (used by Variant/Chain swap code)
    static GameObject InstantiateAndSwap(GameObject prefab)
    {
        return (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    }

}
} // namespace WB3DAssets.BalustradeModularSystem
