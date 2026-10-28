using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace WB3DAssets.BalustradeModularSystem
{
public partial class BalustradeModularWindow
{
// Cache: pillar prefab snap-name -> local position. Used by the hidden-rail
// fixup to reconstruct virtual SnapPoint2 positions on pillars whose original
// rail-side snap was destroyed by repair (V1C->V1E etc.).
static readonly Dictionary<string, Vector3> _pillarSnapLocalCache = new();

Vector3 GetPillarPrefabSnapLocal(string variant, string typeLetter, string snapName)
{
    string key = variant + "|" + typeLetter + "|" + snapName;
    if (_pillarSnapLocalCache.TryGetValue(key, out var v))
        return v;

    Vector3 result = new Vector3(float.NaN, 0f, 0f); // sentinel = not found
    var prefab = FindAsset<GameObject>("pillar_" + variant + typeLetter + "_PREFAB");
    if (prefab)
    {
        var tmp = CreateGhost(prefab);
        var s = FindSnap(tmp.transform, snapName);
        if (s) result = tmp.transform.InverseTransformPoint(s.position);
        DestroyImmediate(tmp);
    }
    _pillarSnapLocalCache[key] = result;
    return result;
}

// Helper: returns true if the transform sits under the HIDDEN container of
// some balustrade root. Used by Variant/Top/Baluster swap loops to skip the
// bookkeeping objects so they are never re-named, re-parented or destroyed
// by a UI operation.
bool IsUnderHiddenContainer(Transform t)
{
    if (!t) return false;
    Transform p = t.parent;
    while (p != null)
    {
        if (p.name == HiddenContainerName) return true;
        p = p.parent;
    }
    return false;
}

// Run a swap-style UI operation that internally destroys/creates GameObjects.
// Disarms BOTH the rail-delete detector (suppressDeleteUndo) and Unity
// selection callbacks (suppressSelectionChanged) for the duration of the
// operation, then resets BOTH synchronously regardless of exceptions. Also
// clears the rail-delete tracking state so any deferred Unity object-change
// event arriving AFTER the finally block cannot be misinterpreted as a user
// delete-rail gesture. If selectRootAfter is non-null, the Selection is
// promoted to that root after the body completes — keeping the UI in focus
// on the balustrade so the user can keep switching variants/tops/balusters.
void SafeUiSwap(System.Action body, GameObject selectRootAfter = null)
{
    bool prevDelete = suppressDeleteUndo;
    bool prevSelect = suppressSelectionChanged;
    suppressDeleteUndo = true;
    suppressSelectionChanged = true;

    // Clear delete-detection cache so deferred events cannot trigger hide-rail
    // after we release suppressDeleteUndo in finally.
    lastSelectionWasRail = false;
    lastSelectionWasPillar = false;
    lastRailDeleteBalustradeRoot = null;
    pendingRailSnaps.Clear();

    try
    {
        body();
        if (selectRootAfter)
            Selection.activeGameObject = selectRootAfter;
    }
    finally
    {
        suppressDeleteUndo = prevDelete;
        suppressSelectionChanged = prevSelect;
    }
}

void ReplaceBalusterStyle(int fromStyle, int toStyle, ContinueVariant variant)
{
var root = GetUiTargetBalustradeRoot();
if (!root) return;

    SafeUiSwap(() =>
    {
    string v = variant == ContinueVariant.V2 ? "V2" : "V1";

    var replaceList = new List<(GameObject go, GameObject prefab)>();

    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        // NEVER touch elements parked in the HIDDEN container — they are
        // bookkeeping for the rail-delete repair system and must keep their
        // current prefab + suffix.
        if (IsUnderHiddenContainer(t)) continue;

        string sceneName = t.name;
        int cut = sceneName.IndexOf(" (");
        if (cut > 0)
            sceneName = sceneName.Substring(0, cut);

        // straight or curved
        bool isStraight = sceneName.StartsWith("blstrs_");
        bool isCurved   = sceneName.StartsWith("blstrsCrvd_");
        if (!isStraight && !isCurved)
            continue;

        string fromTag = $"_{fromStyle}{v}";
        if (!sceneName.Contains(fromTag))
            continue;

        string prefix = isCurved ? "blstrsCrvd_" : "blstrs_";
        string targetName = $"{prefix}{toStyle}{v}_PREFAB";

        var prefab = FindAsset<GameObject>(targetName);
        if (!prefab)
            continue;

        replaceList.Add((t.gameObject, prefab));
    }

    foreach (var (oldGO, prefab) in replaceList)
    {
        Transform parent = oldGO.transform.parent;
        Vector3 pos = oldGO.transform.position;
        Quaternion rot = oldGO.transform.rotation;
        int sibling = oldGO.transform.GetSiblingIndex();

        var newGO = InstantiateAndSwap(prefab);
        TransferIndex(root, oldGO, newGO);
        ApplyCurrentTextureVariantToObject(newGO);

        // --- random visual variation on BALUSTER STYLE switch ---
        var src = PrefabUtility.GetCorrespondingObjectFromSource(newGO);
        if (src)
        {
            string n = src.name;

            if (n.StartsWith("blstrsCrvd_"))
            {
                ApplyRailVisualVariation(newGO);
                ApplyCurvedRailVisualVariation(newGO);
            }
            else if (n.StartsWith("blstrs_"))
            {
                ApplyRailVisualVariation(newGO);
            }
        }

        Undo.RegisterCreatedObjectUndo(newGO, "Replace Baluster Style");

        newGO.transform.SetParent(parent, true);
        newGO.transform.SetPositionAndRotation(pos, rot);
        newGO.transform.localScale = oldGO.transform.localScale;
        newGO.transform.SetSiblingIndex(sibling);

        QueueForDeferredDestroy(oldGO);
    }

    if (fullDetailMode && root)
        ApplyFullDetailToBalustrade(root, true);
    }, selectRootAfter: root);

    FlushDeferredDestroys();
}

void SyncUiFromBalustradeRoot(GameObject root)
{
    if (!root) return;

    // -------------------------------------------------
    // Variant + Baluster Style (existing logic)
    // -------------------------------------------------
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        string n = t.name;
        if (!n.StartsWith("blstrs")) continue;

        int underscore = n.IndexOf('_');
        int vIndex = n.IndexOf('V', underscore + 1);
        if (underscore < 0 || vIndex < 0) continue;

        string styleStr = n.Substring(underscore + 1, vIndex - underscore - 1);
        if (int.TryParse(styleStr, out int style))
            balusterStyleIndex = Mathf.Clamp(style - 1, 0, balusterStylePreviews.Length - 1);

// Only sync variant when an explicit balustrade root is selected
if (IsExplicitBalustradeRootSelected())
{
    selectedVariant = n.Contains("V2")
        ? ContinueVariant.V2
        : ContinueVariant.V1;
}
        break;
    }

    // -------------------------------------------------
    // Texture variant (existing logic)
    // -------------------------------------------------
    var renderers = root.GetComponentsInChildren<Renderer>(true);
    foreach (var r in renderers)
    {
        foreach (var m in r.sharedMaterials)
        {
            if (!m) continue;
            for (int i = 4; i >= 1; i--)
            {
                if (m.name.Contains("worn" + i)) { textureVariantIsWorn = true;  wornTextureIndex = i - 1; goto TOP_SCAN; }
                if (m.name.Contains("new"  + i)) { textureVariantIsWorn = false; newTextureIndex  = i - 1; goto TOP_SCAN; }
            }
        }
    }

TOP_SCAN:
    // -------------------------------------------------
    // TOPS: RECURSIVE SCAN (Root + ALL descendants)
    // -------------------------------------------------
    topPreviewIndex = topPreviews.Length; // default = No Tops

    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (!src) continue;

        for (int i = 0; i < TopPrefabNames.Length; i++)
        {
            if (src.name == TopPrefabNames[i])
            {
                topPreviewIndex = i;
                return; // FIRST top defines UI
            }
        }
    }
}

void UpdateVariantAvailabilityFromHierarchy()
{
    variantV1Available = false;
    variantV2Available = false;

    var root = GetUiTargetBalustradeRoot();
    if (!root)
        return;

    // keep index in sync for top/style operations
    selectedBalustradeIndex = finalizedBalustrades.IndexOf(root);

    bool hasV1 = false;
    bool hasV2 = false;

    // Detect variant ONLY from rails (pillars are always V1-prefabs in your setup)
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (!src)
            continue;

        string n = src.name;

        bool isRail =
            (n.StartsWith("blstrs_") || n.StartsWith("blstrsCrvd_")) &&
            n.EndsWith("_PREFAB");

        if (!isRail)
            continue;

        if (n.Contains("V1")) hasV1 = true;
        if (n.Contains("V2")) hasV2 = true;
    }

    // Availability = you can switch to the OTHER variant if current rails exist.
    // If mixed (both), allow both (no locking).
    variantV2Available = hasV1;
    variantV1Available = hasV2;

    // IMPORTANT: no forced reset of selectedVariant here (prevents UI "blocking")
}

void UpdateBalusterStyleFromHierarchy()
{
var sel = GetUiTargetBalustradeRoot();
if (!sel) return;

    foreach (Transform t in sel.GetComponentsInChildren<Transform>(true))
    {
        string n = t.name;

        // match blstrs_#V# or blstrsCrvd_#V#
        if (!n.StartsWith("blstrs"))
            continue;

        // extract style index
        // examples: blstrs_4V1, blstrsCrvd_12V2
        int underscore = n.IndexOf('_');
        int vIndex = n.IndexOf('V', underscore + 1);
        if (underscore < 0 || vIndex < 0)
            continue;

        string styleStr = n.Substring(underscore + 1, vIndex - underscore - 1);
        if (!int.TryParse(styleStr, out int style))
            continue;

        balusterStyleIndex = Mathf.Clamp(style - 1, 0, balusterStylePreviews.Length - 1);

        // keep selectedVariant as UI choice (do not auto-sync from hierarchy)

        return;
    }
}

void UpdateTextureVariantFromHierarchy()
{
var sel = GetUiTargetBalustradeRoot();
if (!sel) return;

    var renderers = sel.GetComponentsInChildren<Renderer>(true);

    // First pass: look for ANY worn variant across all materials
    foreach (var r in renderers)
    {
        foreach (var m in r.sharedMaterials)
        {
            if (!m) continue;
            for (int i = 4; i >= 1; i--)
            {
                if (m.name.Contains("worn" + i))
                {
                    textureVariantIsWorn = true;
                    wornTextureIndex = i - 1;
                    return;
                }
            }
        }
    }

    // Second pass: no worn found, look for new variant
    foreach (var r in renderers)
    {
        foreach (var m in r.sharedMaterials)
        {
            if (!m) continue;
            for (int i = 4; i >= 1; i--)
            {
                if (m.name.Contains("new" + i))
                {
                    textureVariantIsWorn = false;
                    newTextureIndex = i - 1;
                    return;
                }
            }
        }
    }
}

// --- Topology edge for variant switch repositioning ---
struct VariantTopoEdge
{
    public BalId idA, idB;
    public string snapA, snapB;
    public bool isGap;
    public Vector3 gapDirection;    // local space of pillar A (A→B)
    public Vector3 gapDirectionRev; // local space of pillar B (B→A)
    public float gapDistance;
}

// Measure curved rail snap-to-snap distance for a given variant.
// Used when scaling gap-edge distances across V1<->V2 swaps for curved bridges.
float MeasureCurvedRailLength(string variant)
{
    var prefab = FindAsset<GameObject>($"blstrsCrvd_1{variant}_PREFAB");
    if (!prefab) return 0f;
    var tmp = CreateGhost(prefab);
    var s1 = FindSnap(tmp.transform, RailStartSnap);
    var s2 = FindSnap(tmp.transform, RailEndSnap);
    float len = (s1 && s2) ? Vector3.Distance(s1.position, s2.position) : 0f;
    DestroyImmediate(tmp);
    return len;
}

// Measure rail snap-to-snap distance for a given variant
float MeasureRailLength(string variant)
{
    var prefab = FindAsset<GameObject>($"blstrs_1{variant}_PREFAB");
    if (!prefab) return 0f;
    var tmp = CreateGhost(prefab);
    var s1 = FindSnap(tmp.transform, RailStartSnap);
    var s2 = FindSnap(tmp.transform, RailEndSnap);
    float len = (s1 && s2) ? Vector3.Distance(s1.position, s2.position) : 0f;
    DestroyImmediate(tmp);
    return len;
}

// Measure M-pillar snap-to-snap width for a given variant
float MeasurePillarMWidth(string variant)
{
    var prefab = FindAsset<GameObject>($"pillar_{variant}M_PREFAB");
    if (!prefab) return 0f;
    var tmp = CreateGhost(prefab);
    var s1 = FindSnap(tmp.transform, "SnapPoint1");
    var s2 = FindSnap(tmp.transform, "SnapPoint2");
    float w = (s1 && s2) ? Vector3.Distance(s1.position, s2.position) : 0f;
    DestroyImmediate(tmp);
    return w;
}

// Capture full topology (connections + gap edges) BEFORE swap while snaps still align
List<VariantTopoEdge> CaptureBalustradeTopology(GameObject root, string currentVariant)
{
    var edges = new List<VariantTopoEdge>();

    var objects = new List<GameObject>();
    // Pre-collect positions of hidden pillars. The visible repair-V1E spawned
    // by the curved/45 delete path sits at the same world position as one of
    // these hidden M-pillars. Only the hidden pillar belongs in the topology
    // graph (it carries the snap geometry BFS walks through). The visible
    // repair-V1E is filtered out by position match below and is repositioned
    // separately by the post-BFS pass.
    var hiddenPillarXZ = new List<Vector3>();
    foreach (Transform tH in root.GetComponentsInChildren<Transform>(true))
    {
        if (!tH || tH == root.transform) continue;
        if (!IsHiddenDeletedPillar(tH.gameObject)) continue;
        var p = tH.position; p.y = 0f;
        hiddenPillarXZ.Add(p);
    }
    float twinTolSq = 0.3f * 0.3f * Mathf.Max(root.transform.lossyScale.x * root.transform.lossyScale.x, 1f);

    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (t == root.transform) continue;
        var go = t.gameObject;

        // Skip visible repair-V1E pillars that overlap a hidden pillar — only
        // the hidden one participates in BFS topology.
        if (IsPillarInstance(go) && !IsHiddenDeletedPillar(go))
        {
            var pp = go.transform.position; pp.y = 0f;
            bool overlapsHidden = false;
            foreach (var hp in hiddenPillarXZ)
            {
                if ((pp - hp).sqrMagnitude < twinTolSq) { overlapsHidden = true; break; }
            }
            if (overlapsHidden) continue;
        }

        if (IsRailInstance(go) || IsPillarInstance(go))
            objects.Add(go);
    }
    if (objects.Count < 2) return edges;

    // Gather snap transforms per object
    var snapsOf = new Dictionary<BalId, List<(Transform snap, string name)>>();
    foreach (var go in objects)
    {
        var list = new List<(Transform, string)>();
        if (IsRailInstance(go))
        {
            var s = FindSnap(go.transform, RailStartSnap); if (s) list.Add((s, RailStartSnap));
            s = FindSnap(go.transform, RailEndSnap);       if (s) list.Add((s, RailEndSnap));
        }
        else
        {
            foreach (var n in new[] { "SnapPoint1", "SnapPoint2", "SnapPoint3" })
            {
                var s = FindSnap(go.transform, n);
                if (s) list.Add((s, n));
            }
        }
        snapsOf[go.StableId()] = list;
    }

    // Build connected edges by snap proximity
    float topoScaleFactor = 1f;
    foreach (var go in objects)
    {
        float s = go.transform.localScale.x;
        if (s > topoScaleFactor) topoScaleFactor = s;
    }
    float tol = 0.2f * topoScaleFactor;
    // Paar-Key haelt zwei Objekt-IDs; ab 6000.3 sind die 64 Bit und passen nicht
    // mehr gepackt in ein long - daher als geordnetes Tupel.
    var paired = new HashSet<(BalId lo, BalId hi)>();

    for (int i = 0; i < objects.Count; i++)
    {
        BalId idA = objects[i].StableId();
        for (int j = i + 1; j < objects.Count; j++)
        {
            BalId idB = objects[j].StableId();
            foreach (var (sa, na) in snapsOf[idA])
            {
                foreach (var (sb, nb) in snapsOf[idB])
                {
                    if (Vector3.Distance(sa.position, sb.position) < tol)
                    {
                        BalId lo = idA < idB ? idA : idB, hi = idA < idB ? idB : idA;
                        if (paired.Add((lo, hi)))
                        {
                            edges.Add(new VariantTopoEdge
                            {
                                idA = idA, idB = idB, snapA = na, snapB = nb, isGap = false
                            });
                        }
                        goto NEXT_PAIR;
                    }
                }
            }
            NEXT_PAIR:;
        }
    }

    // --- Inject ghost-rail edges from persisted markers ---
    // For each __DeletedRail_* child under root, find the two pillars whose snaps
    // touch the marker's snap poses and add a real connection edge. This treats
    // deleted rails as if they were still present during variant repositioning.
    foreach (Transform child in root.transform)
    {
        if (!child || !child.name.StartsWith(GhostRailMarkerPrefix)) continue;
        var ms1 = child.Find("Snap1");
        var ms2 = child.Find("Snap2");
        if (!ms1 || !ms2) continue;

        GameObject pA = null, pB = null;
        string snapNameA = null, snapNameB = null;
        foreach (var go in objects)
        {
            if (!IsPillarInstance(go)) continue;
            foreach (var (s, n) in snapsOf[go.StableId()])
            {
                if (pA == null && Vector3.Distance(s.position, ms1.position) < tol)
                { pA = go; snapNameA = n; }
                else if (pB == null && Vector3.Distance(s.position, ms2.position) < tol)
                { pB = go; snapNameB = n; }
            }
            if (pA != null && pB != null) break;
        }

        if (pA == null || pB == null || pA == pB) continue;
        BalId idGA = pA.StableId();
        BalId idGB = pB.StableId();
        BalId gLo = idGA < idGB ? idGA : idGB, gHi = idGA < idGB ? idGB : idGA;
        if (paired.Add((gLo, gHi)))
        {
            edges.Add(new VariantTopoEdge
            {
                idA = idGA, idB = idGB, snapA = snapNameA, snapB = snapNameB, isGap = false
            });
        }
    }

    // --- Detect gap edges between disconnected components ---
    var parent = new Dictionary<BalId, BalId>();
    BalId Find(BalId x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
    void Union(BalId a, BalId b) { parent[Find(a)] = Find(b); }

    foreach (var go in objects)
        parent[go.StableId()] = go.StableId();
    foreach (var e in edges)
        Union(e.idA, e.idB);

    // Group pillars by component (using centers, NOT snaps)
    var compPillars = new Dictionary<BalId, List<GameObject>>();
    foreach (var go in objects)
    {
        if (!IsPillarInstance(go)) continue;
        BalId comp = Find(go.StableId());
        if (!compPillars.ContainsKey(comp)) compPillars[comp] = new();
        compPillars[comp].Add(go);
    }

    // Gather all candidate gap connections (center-to-center between pillars)
    var gapCandidates = new List<(float dist, GameObject goA, GameObject goB)>();
    var compKeys = new List<BalId>(compPillars.Keys);

    for (int ci = 0; ci < compKeys.Count; ci++)
    {
        for (int cj = ci + 1; cj < compKeys.Count; cj++)
        {
            float bestDist = float.MaxValue;
            GameObject bestA = null, bestB = null;

            foreach (var a in compPillars[compKeys[ci]])
            {
                foreach (var b in compPillars[compKeys[cj]])
                {
                    Vector3 diff = b.transform.position - a.transform.position;
                    diff.y = 0f;
                    float d = diff.magnitude;
                    if (d < bestDist) { bestDist = d; bestA = a; bestB = b; }
                }
            }

            if (bestA && bestB && bestDist > 0.05f)
                gapCandidates.Add((bestDist, bestA, bestB));
        }
    }

    // MST: sort by distance, only connect unmerged components
    gapCandidates.Sort((a, b) => a.dist.CompareTo(b.dist));
    var gapUF = new Dictionary<BalId, BalId>();
    foreach (var k in compPillars.Keys) gapUF[k] = k;
    BalId GFind(BalId x) { while (gapUF[x] != x) { gapUF[x] = gapUF[gapUF[x]]; x = gapUF[x]; } return x; }

    foreach (var (dist, goA, goB) in gapCandidates)
    {
        BalId cA = GFind(Find(goA.StableId()));
        BalId cB = GFind(Find(goB.StableId()));
        if (cA == cB) continue;
        gapUF[cA] = cB;

        // Store direction in LOCAL space of each pillar
        Vector3 worldDir = goB.transform.position - goA.transform.position;
        worldDir.y = 0f;
        Vector3 localDirA = goA.transform.InverseTransformDirection(worldDir.normalized);
        Vector3 localDirB = goB.transform.InverseTransformDirection(-worldDir.normalized);

        edges.Add(new VariantTopoEdge
        {
            idA = goA.StableId(), idB = goB.StableId(),
            snapA = "", snapB = "", // gap edges use centers, not snaps
            isGap = true,
            gapDirection = localDirA,
            gapDirectionRev = localDirB,
            gapDistance = dist
        });
    }

    return edges;
}

// Reposition all objects using pre-captured topology and new snap offsets
void RepositionByTopology(GameObject root, List<VariantTopoEdge> edges,
    Dictionary<BalId, GameObject> swapMap, string fromVariant, string toVariant)
{
    // Resolve old IDs to new GameObjects
    GameObject Resolve(BalId oldId)
    {
        if (swapMap.TryGetValue(oldId, out var g) && g) return g;
        return BalustradeIds.ObjectFromId(oldId) as GameObject;
    }

    // Build adjacency using resolved objects
    var adj = new Dictionary<GameObject,
        List<(GameObject nb, string mySnap, string nbSnap, bool isGap, Vector3 gapDir, float gapDist)>>();

    foreach (var e in edges)
    {
        var goA = Resolve(e.idA);
        var goB = Resolve(e.idB);
        if (!goA || !goB) continue;

        if (!adj.ContainsKey(goA)) adj[goA] = new();
        if (!adj.ContainsKey(goB)) adj[goB] = new();

        adj[goA].Add((goB, e.snapA, e.snapB, e.isGap, e.gapDirection, e.gapDistance));
        adj[goB].Add((goA, e.snapB, e.snapA, e.isGap, e.gapDirectionRev, e.gapDistance));
    }

    // Find anchor (start pillar)
    GameObject anchor = null;
    if (balustradeStartPillars.TryGetValue(root, out var sp) && sp)
        anchor = sp;
    if (anchor == null || !adj.ContainsKey(anchor))
    {
        foreach (var k in adj.Keys) { anchor = k; break; }
    }
    if (!anchor) return;

    // Capture pre-BFS world positions of every rail+pillar under root.
    // Used by the post-BFS hidden-rail fixup below: hidden rails lose their
    // snap edges after pillar repair (V1C->V1E removes SnapPoint2), so BFS
    // never reaches them. We translate them by the delta of their nearest
    // anchor pillar so they follow the chain across V1<->V2 switches.
    var initialPos = new Dictionary<GameObject, Vector3>();
    foreach (Transform tCap in root.GetComponentsInChildren<Transform>(true))
    {
        if (tCap == root.transform) continue;
        var gCap = tCap.gameObject;
        if (IsRailInstance(gCap) || IsPillarInstance(gCap))
            initialPos[gCap] = gCap.transform.position;
    }

    // Measure segment dimensions for proportional gap scaling
    float oldRailLen  = MeasureRailLength(fromVariant);
    float newRailLen  = MeasureRailLength(toVariant);
    float oldPillarW  = MeasurePillarMWidth(fromVariant);
    float newPillarW  = MeasurePillarMWidth(toVariant);
    float oldSegment  = oldRailLen + oldPillarW;
    float newSegment  = newRailLen + newPillarW;
    float segRatio    = (oldSegment > 0.01f) ? newSegment / oldSegment : 1f;

    // BFS helper: align all reachable objects from a start node
    var visited = new HashSet<GameObject>();

    void BfsAlign(GameObject start)
    {
        visited.Add(start);
        var queue = new Queue<GameObject>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (!adj.TryGetValue(cur, out var neighbors)) continue;

            foreach (var (nb, mySnapName, nbSnapName, isGap, gapDir, gapDist) in neighbors)
            {
                if (visited.Contains(nb)) continue;
                visited.Add(nb);

                if (!isGap)
                {
                    // Connected: align snaps to coincide
                    var mySnap = FindSnap(cur.transform, mySnapName);
                    var nbSnap = FindSnap(nb.transform, nbSnapName);
                    if (mySnap && nbSnap)
                    {
                        nb.transform.rotation = YawDelta(nbSnap.right, -mySnap.right) * nb.transform.rotation;
                        Vector3 delta = mySnap.position - nbSnap.position;
                        delta.y = 0f;
                        nb.transform.position += delta;
                    }
                }
                else
                {
                    // Gap: center-to-center repositioning, DON'T touch rotation
                    // Rotation is preserved by swap, BFS snap edges handle it within each component
                    float newGapDist = gapDist * segRatio;

                    // Transform stored local direction to current world space
                    Vector3 worldDir = cur.transform.TransformDirection(gapDir);
                    worldDir.y = 0f;
                    worldDir.Normalize();

                    // Place nb center at scaled distance from cur center
                    Vector3 targetPos = cur.transform.position + worldDir * newGapDist;
                    Vector3 offset = targetPos - nb.transform.position;
                    offset.y = 0f;
                    nb.transform.position += offset;
                }

                queue.Enqueue(nb);
            }
        }
    }

    // Main BFS from anchor
    BfsAlign(anchor);

    // Reposition disconnected subgraphs (e.g. detached T-branches)
    foreach (var go in adj.Keys)
    {
        if (!visited.Contains(go))
            BfsAlign(go);
    }

    // Post-BFS hidden-rail fixup. Hidden rails have no snap edges in the
    // topology (their adjacent corner/T pillars were repaired to end-pillars
    // and dropped the rail-side snap), so BFS skipped them and they still
    // sit at the fromVariant world position.
    //
    // Strategy: reconstruct the would-be SnapPoint2 of the adjacent pillar
    // by looking up the corresponding C/C45/M prefab of the toVariant and
    // applying its SnapPoint2 local offset to the live (now BFS-aligned)
    // pillar transform. This gives the V1-correct virtual mating point even
    // though the live pillar is V1E and lacks SnapPoint2 geometry. Snap-align
    // the hidden rail's nearer end-snap to that virtual point.
    string toV = (toVariant == "V2") ? "V2" : "V1";
    string[] candidateTypes = { "M", "C", "C45", "T" };
    string[] candidateSnapNames = { "SnapPoint1", "SnapPoint2", "SnapPoint3" };

    foreach (var kv in initialPos)
    {
        var hr = kv.Key;
        if (!IsHiddenDeletedRail(hr)) continue;
        if (visited.Contains(hr)) continue;

        // Find nearest pillar by initial (pre-BFS) xz distance
        GameObject nearest = null;
        float bestSq = float.MaxValue;
        Vector3 railOld = kv.Value;
        foreach (var kv2 in initialPos)
        {
            if (!IsPillarInstance(kv2.Key)) continue;
            Vector3 d = kv2.Value - railOld;
            d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; nearest = kv2.Key; }
        }
        if (!nearest) continue;

        var rs1 = FindSnap(hr.transform, RailStartSnap);
        var rs2 = FindSnap(hr.transform, RailEndSnap);
        if (!rs1 && !rs2) continue;

        // Try every (pillarType x snapName) combo of the toVariant. Pick the
        // virtual snap whose world position is closest to one of the rail end
        // snaps. Smallest distance corresponds to the snap that originally
        // mated the rail before the pillar was repaired.
        Transform bestRailSnap = null;
        Vector3 bestVirtualWorld = Vector3.zero;
        float bestDelta = float.MaxValue;

        foreach (var typeLetter in candidateTypes)
        {
            foreach (var snapName in candidateSnapNames)
            {
                Vector3 snapLocal = GetPillarPrefabSnapLocal(toV, typeLetter, snapName);
                if (float.IsNaN(snapLocal.x)) continue;

                Vector3 virtualWorld = nearest.transform.position
                                     + nearest.transform.rotation * snapLocal;

                float d1 = rs1 ? Vector3.Distance(rs1.position, virtualWorld) : float.MaxValue;
                float d2 = rs2 ? Vector3.Distance(rs2.position, virtualWorld) : float.MaxValue;
                float dMin = Mathf.Min(d1, d2);
                Transform pick = (d1 <= d2) ? rs1 : rs2;
                if (!pick) continue;

                if (dMin < bestDelta)
                {
                    bestDelta = dMin;
                    bestRailSnap = pick;
                    bestVirtualWorld = virtualWorld;
                }
            }
        }

        bool aligned = false;
        float scaleF = Mathf.Max(root.transform.lossyScale.x, 1f);
        float sanityMax = Mathf.Max(newRailLen, oldRailLen) * 0.6f * scaleF;

        if (bestRailSnap != null && bestDelta < sanityMax)
        {
            Vector3 delta = bestVirtualWorld - bestRailSnap.position;
            delta.y = 0f;
            hr.transform.position += delta;
            aligned = true;
        }

        if (!aligned)
        {
            // Final fallback: translate hidden rail by nearest pillar's BFS
            // delta. Corrects only the chain-direction component but at least
            // keeps the rail attached to its anchor pillar.
            Vector3 delta = nearest.transform.position - initialPos[nearest];
            delta.y = 0f;
            if (delta.sqrMagnitude > 1e-12f)
                hr.transform.position += delta;
        }
    }

    // Post-BFS visible-repair-pillar fixup. The visible V1E pillars that were
    // spawned by the curved/45 rail-delete path overlap a hidden M-pillar at
    // the same world position. They were filtered out of the topology so BFS
    // never touched them and they still hold the fromVariant transform after
    // swap. For each such pillar, find its hidden parent (nearest hidden
    // pillar at the pre-BFS position), copy the now-correct toVariant
    // transform from it, then re-apply the SnapPoint1->surviving rail snap
    // alignment so the visible pillar closes the chain like a real repair.
    foreach (Transform tEC in root.GetComponentsInChildren<Transform>(true))
    {
        if (tEC == root.transform) continue;
        var ec = tEC.gameObject;
        if (!IsPillarInstance(ec)) continue;
        if (IsHiddenDeletedPillar(ec)) continue;

        Vector3 ecOldPos = initialPos.TryGetValue(ec, out var ep) ? ep : ec.transform.position;

        // Find nearest hidden pillar at pre-BFS coordinates. Only treat this
        // pillar as a visible repair-twin if such a hidden pillar exists at
        // (essentially) the same XZ position.
        GameObject hiddenP = null;
        float bestSq = float.MaxValue;
        float overlapTolSq = 0.3f * 0.3f * Mathf.Max(root.transform.lossyScale.x * root.transform.lossyScale.x, 1f);
        foreach (var kv in initialPos)
        {
            if (!IsHiddenDeletedPillar(kv.Key)) continue;
            Vector3 d = kv.Value - ecOldPos;
            d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; hiddenP = kv.Key; }
        }
        if (!hiddenP || bestSq > overlapTolSq) continue;

        // Copy hidden pillar's new (BFS-aligned) transform
        ec.transform.SetPositionAndRotation(hiddenP.transform.position, hiddenP.transform.rotation);
        ec.transform.localScale = hiddenP.transform.localScale;

        // Identify which snap of the hidden pillar still mates a visible
        // (non-hidden) rail; that is the surviving side. Align the end-cap's
        // SnapPoint1 to that rail snap exactly like RepairEndPillarsAfterRailDelete.
        var hps1 = FindSnap(hiddenP.transform, "SnapPoint1");
        var hps2 = FindSnap(hiddenP.transform, "SnapPoint2");

        float scaleF = Mathf.Max(root.transform.lossyScale.x, 1f);
        float tol = 0.2f * scaleF;

        Transform survivingRailSnap = null;
        foreach (Transform tR in root.GetComponentsInChildren<Transform>(true))
        {
            if (!tR || !IsRailInstance(tR.gameObject)) continue;
            if (IsHiddenDeletedRail(tR.gameObject)) continue;
            var s1 = FindSnap(tR, RailStartSnap);
            var s2 = FindSnap(tR, RailEndSnap);
            foreach (var rs in new[] { s1, s2 })
            {
                if (!rs) continue;
                if (hps1 && Vector3.Distance(rs.position, hps1.position) < tol) { survivingRailSnap = rs; break; }
                if (hps2 && Vector3.Distance(rs.position, hps2.position) < tol) { survivingRailSnap = rs; break; }
            }
            if (survivingRailSnap) break;
        }

        if (!survivingRailSnap) continue;

        var endSnap1 = FindSnap(ec.transform, PillarSnapName);
        if (!endSnap1) continue;

        Vector3 toDir = -survivingRailSnap.right;
        toDir.y = 0f;
        if (toDir.sqrMagnitude > 1e-6f)
        {
            ec.transform.rotation =
                YawDelta(endSnap1.right, toDir.normalized) * ec.transform.rotation;
        }
        ec.transform.position += survivingRailSnap.position - endSnap1.position;
    }
}

void ReplaceBalustradeVariant(string fromTag, string toTag)
{
    if (!IsExplicitBalustradeRootSelected())
        return;

    var root = GetUiTargetBalustradeRoot();
    if (!root)
        return;

    SafeUiSwap(() =>
    {
    // --- PHASE 1: Capture topology BEFORE swap (snaps still aligned) ---
    var topology = CaptureBalustradeTopology(root, fromTag);

    // --- PHASE 2: Swap all prefabs, track old → new mapping ---
    var swapMap = new Dictionary<BalId, GameObject>();
    var replaceList = new List<(GameObject go, GameObject prefab)>();

    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (t == root.transform)
            continue;

        string sceneName = t.name;
        int cut = sceneName.IndexOf(" (");
        if (cut > 0)
            sceneName = sceneName.Substring(0, cut);

        // Strip hidden suffix (rails AND pillars). Flag preserved on the GO
        // via name; re-applied below after the swap. Variant-switch DOES
        // need to swap hidden elements so their geometry stays consistent
        // with the rest of the chain.
        bool wasHidden = sceneName.EndsWith(HiddenRailSuffix);
        if (wasHidden)
            sceneName = sceneName.Substring(0, sceneName.Length - HiddenRailSuffix.Length);

        if (!sceneName.Contains(fromTag))
            continue;

        string targetName = sceneName.Replace(fromTag, toTag);
        var targetPrefab = FindAsset<GameObject>(targetName);
        if (!targetPrefab)
            continue;

        replaceList.Add((t.gameObject, targetPrefab));
    }

    foreach (var (oldGO, prefab) in replaceList)
    {
        BalId oldId = oldGO.StableId();
        Transform parent = oldGO.transform.parent;
        Vector3 pos = oldGO.transform.position;
        Quaternion rot = oldGO.transform.rotation;
        int sibling = oldGO.transform.GetSiblingIndex();
        bool wasHiddenRail = IsHiddenDeletedRail(oldGO);
        bool wasHiddenPillar = IsHiddenDeletedPillar(oldGO);

        // Check for start marker BEFORE destroying
        bool hadStartMarker = false;
        foreach (Transform c in oldGO.transform)
        {
            if (c.name == StartMarkerName)
            {
                hadStartMarker = true;
                break;
            }
        }

        var newGO = InstantiateAndSwap(prefab);
        TransferIndex(root, oldGO, newGO);
        ApplyCurrentTextureVariantToObject(newGO);

        // Random visual variation
        var src = PrefabUtility.GetCorrespondingObjectFromSource(newGO);
        if (src)
        {
            string n = src.name;
            if (n.StartsWith("blstrsCrvd_"))
            {
                ApplyRailVisualVariation(newGO);
                ApplyCurvedRailVisualVariation(newGO);
            }
            else if (n.StartsWith("blstrs_"))
            {
                ApplyRailVisualVariation(newGO);
            }
            else if (IsPillarMPrefab(n))
            {
                ApplyPillarMVisualVariation(newGO);
            }
        }

        Undo.RegisterCreatedObjectUndo(newGO, "Switch Variant");

        newGO.transform.SetParent(parent, true);
        newGO.transform.SetPositionAndRotation(pos, rot);
        newGO.transform.localScale = oldGO.transform.localScale;
        newGO.transform.SetSiblingIndex(sibling);

        // Preserve hidden state across swap (rails AND pillars)
        if (wasHiddenRail)
            HideDeletedRail(newGO);
        if (wasHiddenPillar)
            HideDeletedPillar(newGO);

        // Park old object in hidden graveyard + queue for deferred destroy.
        // Unity 6 silently forbids Undo.DestroyObjectImmediate in many OnGUI
        // contexts; the graveyard hides orphans while we retry across frames.
        QueueForDeferredDestroy(oldGO);

        swapMap[oldId] = newGO;

        if (hadStartMarker)
        {
            EnsureStartMarker(newGO);
            balustradeStartPillars[root] = newGO;
        }
    }

    RebuildProtectedPillarIdCache();

    // --- PHASE 3: Reposition using captured topology + new dimensions ---
    RepositionByTopology(root, topology, swapMap, fromTag, toTag);

    if (fullDetailMode)
        ApplyFullDetailToBalustrade(root, true);

    PinHiddenContainersToBottom();
    }, selectRootAfter: root);

    FlushDeferredDestroys();
}

// ---------- Deferred destroy infrastructure (Unity 6 compatibility) ----------
// Old GameObjects from variant / baluster / top swaps are parked here.
// Unity 6 forbids destroy in virtually every editor context we can reach,
// so we do not even try. Graveyard is hidden and not saved, and is cleared
// naturally on domain reload / editor restart.
GameObject _swapGraveyard;

GameObject GetOrCreateSwapGraveyard()
{
    if (_swapGraveyard) return _swapGraveyard;
    _swapGraveyard = new GameObject("__BalustradeSwapGraveyard__");
    _swapGraveyard.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
    return _swapGraveyard;
}

void QueueForDeferredDestroy(GameObject go)
{
    if (!go) return;
    // Snapshot BOTH the GameObject (active flag) and its Transform
    // (parent + localPosition/rotation/scale) BEFORE any change, so a
    // subsequent Ctrl+Z fully restores the object: active=true, re-parented
    // to the original balustrade, at its original position.
    Undo.RegisterCompleteObjectUndo(
        new UnityEngine.Object[] { go, go.transform },
        "Remove Old Object");
    go.transform.SetParent(GetOrCreateSwapGraveyard().transform, true);
    go.SetActive(false);
}

// Kept for call-site compatibility; nothing to flush.
void FlushDeferredDestroys() { }

void ApplyTopToSelectedBalustrade(int topIndex)
{
    if (!IsExplicitBalustradeRootSelected())
        return;

    if (selectedVariant != ContinueVariant.V1)
        return;

    var root = GetUiTargetBalustradeRoot();
    if (!root)
        return;

    if (topIndex < 0 || topIndex >= TopPrefabNames.Length)
    {
        RemoveAllTopsFromSelectedBalustrade();
        return;
    }

    var topPrefab = FindAsset<GameObject>(TopPrefabNames[topIndex]);
    if (!topPrefab)
        return;

    SafeUiSwap(() =>
    {
        // 1) COLLECT pillars FIRST (no hierarchy mutation here). Skip pillars
        //    parked in the HIDDEN container — they are bookkeeping and must
        //    not get tops mounted on them.
        var pillars = new List<Transform>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (IsUnderHiddenContainer(t)) continue;
            if (IsPillarInstance(t.gameObject))
                pillars.Add(t);
        }

        // 2) APPLY tops (safe, isolated loop)
        foreach (var pillar in pillars)
        {
            var snapTop = FindSnap(pillar, TopSnapName);
            if (!snapTop)
                continue;

            RemoveTopFromPillar(pillar);

            var top = InstantiateAndSwap(topPrefab);
            ApplyCurrentTextureVariantToObject(top);
            Undo.RegisterCreatedObjectUndo(top, "Place Top");

            top.transform.SetParent(pillar, false);
            top.transform.position = snapTop.position;
            top.transform.rotation = snapTop.rotation;

            // random visual rotation (Y only)
            ApplyTopVisualRotation(top);
        }

        if (fullDetailMode && root)
            ApplyFullDetailToBalustrade(root, true);
    }, selectRootAfter: root);
}

void RemoveAllTopsFromSelectedBalustrade()
{
    if (!IsExplicitBalustradeRootSelected())
        return;

    var root = GetUiTargetBalustradeRoot();
    if (!root)
        return;

    SafeUiSwap(() =>
    {
        // 1) collect tops first. Skip anything under HIDDEN container.
        var tops = new List<GameObject>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (IsUnderHiddenContainer(t)) continue;
            if (IsTopInstance(t.gameObject))
                tops.Add(t.gameObject);
        }

        // 2) park in graveyard + queue for deferred destroy (Unity 6 safe)
        foreach (var top in tops)
        {
            QueueForDeferredDestroy(top);
        }
    }, selectRootAfter: root);

    FlushDeferredDestroys();
}

ContinueVariant DetectVariantFromBalustradeRoot(GameObject root)
{
    if (!root) return ContinueVariant.V1;

    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (!src) continue;

        string n = src.name;

        bool isRail =
            (n.StartsWith("blstrs_") || n.StartsWith("blstrsCrvd_")) &&
            n.EndsWith("_PREFAB");

        if (!isRail)
            continue;

        if (n.Contains("V2")) return ContinueVariant.V2;
        if (n.Contains("V1")) return ContinueVariant.V1;
    }

    return ContinueVariant.V1;
}

void ApplyUiStateFromBalustradeRoot(GameObject root)
{
    if (!root) return;

    // ---------- Defaults ----------
    ContinueVariant detectedVariant = ContinueVariant.V1;
bool hasAnyTop = false;
int detectedTopIndex = -1;
    int detectedBalusterStyle = 0;
    bool detectedWorn = false;
    int detectedWornIndex = 0;
    int detectedNewIndex = 0;

    // ---------- Scan ----------
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (!src) continue;

        string n = src.name;

        // Variant detection (pillar or rail)
        if (n.Contains("V2")) detectedVariant = ContinueVariant.V2;

// Tops detection (exact index)
for (int i = 0; i < TopPrefabNames.Length; i++)
{
    if (n == TopPrefabNames[i])
    {
        hasAnyTop = true;
        detectedTopIndex = i;
        break;
    }
}

// Baluster style detection (supports 1..12 and curved)
if (n.StartsWith("blstrs_") || n.StartsWith("blstrsCrvd_"))
{
    int us = n.IndexOf('_');
    int v  = n.IndexOf('V', us + 1);
    if (us >= 0 && v > us + 1)
    {
        string s = n.Substring(us + 1, v - us - 1);
        if (int.TryParse(s, out int style))
            detectedBalusterStyle = Mathf.Clamp(style - 1, 0, balusterStylePreviews.Length - 1);
    }
}

        // Texture variant detection
var r = t.GetComponent<Renderer>();
if (r)
{
    var mats = r.sharedMaterials;
    for (int mi = 0; mi < mats.Length; mi++)
    {
        var m = mats[mi];
        if (!m) continue;

        string mn = m.name.ToLowerInvariant();
        for (int wi = 4; wi >= 1; wi--)
        {
            if (mn.Contains("worn" + wi)) { detectedWorn = true; detectedWornIndex = wi - 1; break; }
        }
        if (!detectedWorn)
        {
            for (int ni = 4; ni >= 1; ni--)
            {
                if (mn.Contains("new" + ni)) { detectedNewIndex = ni - 1; break; }
            }
        }
    }
}
    }

    // ---------- Apply to UI ----------
    selectedVariant = detectedVariant;
// ALWAYS sync top when switching UI target balustrade
if (hasAnyTop)
{
    topPreviewIndex = detectedTopIndex >= 0
        ? detectedTopIndex
        : topPreviews.Length;
}
else
{
    topPreviewIndex = topPreviews.Length; // No Tops
}

// Only sync baluster style if UI index is out of range (initial sync)
// Do NOT overwrite user interaction
if (balusterStyleIndex < 0 || balusterStyleIndex >= balusterStylePreviews.Length)
{
    balusterStyleIndex =
        Mathf.Clamp(detectedBalusterStyle, 0, balusterStylePreviews.Length - 1);
}

    textureVariantIsWorn = detectedWorn;
    if (detectedWorn)
        wornTextureIndex = detectedWornIndex;
    else
        newTextureIndex = detectedNewIndex;
}

void ApplyTextureVariantToSelectedBalustrade(string fromToken, string toToken)
{
    if (!IsExplicitBalustradeRootSelected())
        return;

    var root = GetUiTargetBalustradeRoot();
    if (!root)
        return;

    SafeUiSwap(() =>
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);

        foreach (var r in renderers)
        {
            // Skip renderers parked under the HIDDEN container — bookkeeping
            // objects must keep whatever materials they were tagged with at
            // delete time so variant switches across the gap stay consistent.
            if (IsUnderHiddenContainer(r.transform)) continue;

            var mats = r.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (!m) continue;

                if (!m.name.Contains(fromToken))
                    continue;

                string targetName = m.name.Replace(fromToken, toToken);
                var newMat = FindAsset<Material>(targetName);
                if (!newMat) continue;

                mats[i] = newMat;
                changed = true;
            }

            if (changed)
                r.sharedMaterials = mats;
        }
    }, selectRootAfter: root);
}

void ApplyCurrentTextureVariantToObject(GameObject go)
{
    if (!go)
        return;

    // Determine the target token based on current UI state
    string targetToken = !textureVariantIsWorn
        ? "new"  + (newTextureIndex  + 1)
        : "worn" + (wornTextureIndex + 1);

    // Replace any variant token with the current target
    string[] allTokens = { "new1", "new2", "new3", "new4", "worn1", "worn2", "worn3", "worn4" };
    foreach (string from in allTokens)
    {
        if (from == targetToken) continue;
        ApplyTextureVariantToObjectInternal(go, from, targetToken);
    }
}

void ApplyTextureVariantToObjectInternal(
    GameObject root,
    string fromToken,
    string toToken
)
{
    var renderers = root.GetComponentsInChildren<Renderer>(true);

    foreach (var r in renderers)
    {
        var mats = r.sharedMaterials;
        bool changed = false;

        for (int i = 0; i < mats.Length; i++)
        {
            var m = mats[i];
            if (!m) continue;

            if (!m.name.Contains(fromToken))
                continue;

            string targetName = m.name.Replace(fromToken, toToken);
            var newMat = FindAsset<Material>(targetName);
            if (!newMat) continue;

            mats[i] = newMat;
            changed = true;
        }

        if (changed)
            r.sharedMaterials = mats;
    }
}

void ApplyRailVisualVariation(GameObject railRoot)
{
    // Apply visual-only variation to LOD meshes
    // Never touch the rail root or snap points

    if (!railRoot)
        return;

    // Collect LOD roots
    var lods = new List<Transform>();

    var lod0 = railRoot.transform.Find("LOD0");
    var lod1 = railRoot.transform.Find("LOD1");
    var lod2 = railRoot.transform.Find("LOD2");

    if (lod0) lods.Add(lod0);
    if (lod1) lods.Add(lod1);
    if (lod2) lods.Add(lod2);

    if (lods.Count == 0)
        return;

// One random decision per rail (important: same for all LODs)
int mirrorMode = Random.Range(0, 4); // 0 = none, 1 = mirror X, 2 = mirror Z, 3 = mirror X+Z

    Vector3 scale = Vector3.one;
if (mirrorMode == 1 || mirrorMode == 3) scale.x = -1f;
if (mirrorMode == 2 || mirrorMode == 3) scale.z = -1f;

    // Apply to all LODs equally
    foreach (var lod in lods)
    {
lod.localRotation = Quaternion.identity;
        lod.localScale = scale;
    }
}

void ApplyCurvedRailVisualVariation(GameObject railRoot)
{
    // Visual-only variation for CURVED rails
    // Rule: at most 2 unmirrored in a row -> at least every 3rd must be mirrored
    // Only local X mirroring (Z would flip concave/convex)

    if (!railRoot)
        return;

    var lods = new List<Transform>();

    var lod0 = railRoot.transform.Find("LOD0");
    var lod1 = railRoot.transform.Find("LOD1");
    var lod2 = railRoot.transform.Find("LOD2");

    if (lod0) lods.Add(lod0);
    if (lod1) lods.Add(lod1);
    if (lod2) lods.Add(lod2);

    if (lods.Count == 0)
        return;

    bool mirrorX;

    if (consecutiveUnmirroredCurvedRails >= 2)
    {
        // Force mirror to guarantee variation
        mirrorX = true;
    }
    else
    {
        // Optional mirror before the limit
        mirrorX = Random.value > 0.5f;
    }

    Vector3 scale = Vector3.one;
    if (mirrorX) scale.x = -1f;

    foreach (var lod in lods)
    {
        lod.localRotation = Quaternion.identity;
        lod.localScale = scale;
    }

    // Update streak counter
    if (mirrorX)
        consecutiveUnmirroredCurvedRails = 0;
    else
        consecutiveUnmirroredCurvedRails++;
}

void ApplyPillarMVisualVariation(GameObject pillarRoot)
{
    // Visual-only variation for V1M pillars
    // Rule: at most 2 unmirrored in a row -> at least every 3rd must be mirrored
    // Local X/Z only, never touch root or snap points

    if (!pillarRoot)
        return;

    var lods = new List<Transform>();

    var lod0 = pillarRoot.transform.Find("LOD0");
    var lod1 = pillarRoot.transform.Find("LOD1");
    var lod2 = pillarRoot.transform.Find("LOD2");

    if (lod0) lods.Add(lod0);
    if (lod1) lods.Add(lod1);
    if (lod2) lods.Add(lod2);

    if (lods.Count == 0)
        return;

    bool mirror;

    if (consecutiveUnmirroredPillarM >= 2)
    {
        // Force mirror to guarantee variation
        mirror = true;
    }
    else
    {
        // Optional mirror before the limit
        mirror = Random.value > 0.5f;
    }

    // Decide axis when mirroring
    int mirrorMode = mirror ? Random.Range(1, 4) : 0; // 1=X, 2=Z, 3=X+Z

    Vector3 scale = Vector3.one;
    if (mirrorMode == 1 || mirrorMode == 3) scale.x = -1f;
    if (mirrorMode == 2 || mirrorMode == 3) scale.z = -1f;

    foreach (var lod in lods)
    {
        lod.localRotation = Quaternion.identity;
        lod.localScale = scale;
    }

    // Update streak counter
    if (mirror)
        consecutiveUnmirroredPillarM = 0;
    else
        consecutiveUnmirroredPillarM++;
}

void ApplyTopVisualRotation(GameObject topRoot)
{
    // Visual-only rotation for tops
    // Allowed rotations: 90° steps around LOCAL Y
    // Safe because tops have no snap points and are centered

    if (!topRoot)
        return;

    int step = Random.Range(0, 4); // 0..3
    float angle = step * 90f;

    topRoot.transform.localRotation =
        Quaternion.Euler(0f, angle, 0f);
}

}
} // namespace WB3DAssets.BalustradeModularSystem
