using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WB3DAssets.BalustradeModularSystem
{
public partial class BalustradeModularWindow
{

// =========================================================================
// CURVED RAIL INNER/OUTER ARC SWAP
// =========================================================================
// Draws a billboard button 1.3 world-units above a selected curved rail
// that has no C45 corner pillar attached. Clicking it swaps the rail
// between inner and outer arc (mirror via 180° Y-rotation) and flips
// adjacent V1C<->V1M pillars. V1E pillars are left alone.
void TryDrawArcSwapButton(SceneView sv)
{
    if (buildMode) return;

    var sel = Selection.activeGameObject;
    if (!sel) return;

    var src = PrefabUtility.GetCorrespondingObjectFromSource(sel);
    if (!src) return;
    if (!src.name.StartsWith("blstrsCrvd_")) return;

    Transform root = sel.transform.parent;
    while (root && !finalizedBalustrades.Contains(root.gameObject))
        root = root.parent;
    if (!root) return;

    var rs1 = FindSnap(sel.transform, RailStartSnap);
    var rs2 = FindSnap(sel.transform, RailEndSnap);
    if (!rs1 || !rs2) return;

    // Skip button if a C45 corner pillar is attached at either rail end.
    float scaleFactor = Mathf.Max(root.lossyScale.x, 1f);
    float c45Tol = 0.15f * scaleFactor;
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (!t || !IsPillarInstance(t.gameObject)) continue;
        var psrc = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (!psrc || !psrc.name.Contains("C45")) continue;
        foreach (var sn in new[] { "SnapPoint1", "SnapPoint2" })
        {
            var s = FindSnap(t, sn);
            if (!s) continue;
            if (Vector3.Distance(s.position, rs1.position) < c45Tol) return;
            if (Vector3.Distance(s.position, rs2.position) < c45Tol) return;
        }
    }

    Vector3 railCenter = (rs1.position + rs2.position) * 0.5f;
    Vector3 btnPos = railCenter + Vector3.up * 1.5f;

    var cam = sv.camera;
    if (!cam) return;
    Quaternion rot = Quaternion.LookRotation(btnPos - cam.transform.position, Vector3.up);
    // Scale icon by camera distance so it stays visually constant on zoom.
    float size = 0.2f * HandleUtility.GetHandleSize(btnPos);

    bool clicked = false;
    if (Handles.Button(btnPos, rot, size, size, (cid, pos, r, s, evt) =>
    {
        if (evt == EventType.Repaint)
        {
            Vector3 right = r * Vector3.right;
            Vector3 up    = r * Vector3.up;
            bool hovered = HandleUtility.nearestControl == cid;
            Color baseOpaque   = new Color(BaseCol.r,   BaseCol.g,   BaseCol.b,   1f);
            Color activeOpaque = new Color(ActiveCol.r, ActiveCol.g, ActiveCol.b, 1f);
            Handles.color = hovered ? activeOpaque : baseOpaque;

            var topArc = new Vector3[10];
            var botArc = new Vector3[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.Lerp(20f, 160f, i / 9f) * Mathf.Deg2Rad;
                topArc[i] = pos + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * s * 0.75f;
                botArc[i] = pos + (right * Mathf.Cos(a + Mathf.PI) + up * Mathf.Sin(a + Mathf.PI)) * s * 0.75f;
            }
            Handles.DrawAAPolyLine(10f, topArc);
            Handles.DrawAAPolyLine(10f, botArc);

            Vector3 topEnd = topArc[9], topEndDir = (topArc[9] - topArc[8]).normalized;
            Vector3 botEnd = botArc[9], botEndDir = (botArc[9] - botArc[8]).normalized;
            Vector3 topSide = Vector3.Cross(topEndDir, cam.transform.forward).normalized * s * 0.2f;
            Vector3 botSide = Vector3.Cross(botEndDir, cam.transform.forward).normalized * s * 0.2f;
            Handles.DrawAAConvexPolygon(topEnd + topEndDir * s * 0.4f, topEnd + topSide, topEnd - topSide);
            Handles.DrawAAConvexPolygon(botEnd + botEndDir * s * 0.4f, botEnd + botSide, botEnd - botSide);
        }
        else if (evt == EventType.Layout)
        {
            HandleUtility.AddControl(cid, HandleUtility.DistanceToCircle(pos, s * 0.9f));
        }
    }))
    {
        clicked = true;
    }

    if (clicked)
        PerformArcSwap(sel, root, rs1.position, rs2.position);

    var et = Event.current.type;
    if (et == EventType.Repaint || et == EventType.MouseMove)
        sv.Repaint();
}

void PerformArcSwap(GameObject oldRail, Transform root, Vector3 snapPosA, Vector3 snapPosB)
{
    if (!oldRail) return;

    // Classify both neighbour pillars and decide via dispatch table which
    // action runs per side (Replace V1M->V1C, Rotate V1E in place, Skip).
    var kindA = GetPillarKindAt(root, snapPosA);
    var kindB = GetPillarKindAt(root, snapPosB);
    if (kindA == ArcSwapPillarKind.V1C && kindB == ArcSwapPillarKind.V1C)
    {
        PerformArcSwap_CC(oldRail, root, snapPosA, snapPosB, "V1");
        return;
    }
    if ((kindA == ArcSwapPillarKind.V1C && kindB == ArcSwapPillarKind.V1E) ||
        (kindA == ArcSwapPillarKind.V1E && kindB == ArcSwapPillarKind.V1C))
    {
        PerformArcSwap_CE(oldRail, root, snapPosA, snapPosB, "V1");
        return;
    }
    if (kindA == ArcSwapPillarKind.V2C && kindB == ArcSwapPillarKind.V2C)
    {
        PerformArcSwap_CC(oldRail, root, snapPosA, snapPosB, "V2");
        return;
    }
    if ((kindA == ArcSwapPillarKind.V2C && kindB == ArcSwapPillarKind.V2E) ||
        (kindA == ArcSwapPillarKind.V2E && kindB == ArcSwapPillarKind.V2C))
    {
        PerformArcSwap_CE(oldRail, root, snapPosA, snapPosB, "V2");
        return;
    }
    // T-pillar on either side → dedicated path (T stays, rotated 45°-wise
    // until all 3 snaps touch rails; opposite pillar uses generic action).
    bool aIsT = kindA == ArcSwapPillarKind.V1T || kindA == ArcSwapPillarKind.V2T;
    bool bIsT = kindB == ArcSwapPillarKind.V1T || kindB == ArcSwapPillarKind.V2T;
    if (aIsT || bIsT)
    {
        PerformArcSwap_T(oldRail, root, snapPosA, snapPosB, kindA, kindB);
        return;
    }
    if (!IsArcSwapAllowed(kindA, kindB)) return;

    int undoGroup = Undo.GetCurrentGroup();
    Undo.SetCurrentGroupName("Arc Swap");

    SafeUiSwap(() =>
    {
        Undo.RecordObject(oldRail.transform, "Arc Swap");

        // 1) 180° Y rotation around the midpoint of the FAR pillar snap
        //    points — i.e. each neighbour pillar's SnapPoint1/SnapPoint2
        //    that is NOT attached to the curved rail.
        float scaleFactor = Mathf.Max(root.lossyScale.x, 1f);
        float tol = 0.15f * scaleFactor;

        // Mid-point = midpoint between the two neighbour pillar PIVOTS
        // (pivots are exactly centered on each pillar). Find each pillar
        // by its SnapPoint1/2 (or SnapPoint1+H1/H2 for V1E) at snapPosA/B.
        Vector3 PillarPivotAt(Vector3 attachedPos)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t || !IsPillarInstance(t.gameObject)) continue;
                var p1 = FindSnap(t, "SnapPoint1");
                var p2 = FindSnap(t, "SnapPoint2");
                if ((p1 && Vector3.Distance(p1.position, attachedPos) < tol) ||
                    (p2 && Vector3.Distance(p2.position, attachedPos) < tol))
                    return t.position;
            }
            return attachedPos; // fallback
        }

        Vector3 pivotA = PillarPivotAt(snapPosA);
        Vector3 pivotB = PillarPivotAt(snapPosB);
        Vector3 mid = (pivotA + pivotB) * 0.5f;

        // Rotate around the RAIL PARENT's local up axis (not world up) so
        // the swap works correctly when the balustrade root is rotated.
        Vector3 upAxis = oldRail.transform.parent ? oldRail.transform.parent.up : Vector3.up;
        Quaternion yRot = Quaternion.AngleAxis(180f, upAxis);
        Vector3 newWorldPos = mid + yRot * (oldRail.transform.position - mid);
        Vector3 posDelta = newWorldPos - oldRail.transform.position;
        if (oldRail.transform.parent)
            oldRail.transform.localPosition += oldRail.transform.parent.InverseTransformVector(posDelta);
        else
            oldRail.transform.position = newWorldPos;
        oldRail.transform.rotation = yRot * oldRail.transform.rotation;

        // 3) Replace both neighbour V1M pillars (touching the curved rail
        //    via their T-SnapPoints) with V1C. Position/rotation/scale
        //    preserved — no re-rotation yet.
        var finalRs1 = FindSnap(oldRail.transform, RailStartSnap);
        var finalRs2 = FindSnap(oldRail.transform, RailEndSnap);
        if (!finalRs1 || !finalRs2) return;
        Vector3 endA = finalRs1.position;
        Vector3 endB = finalRs2.position;

        // Find pillar at rail endpoint via two-stage search:
        // 1) PREFER T-SnapPoint match (V1M docks here) with tight tolerance.
        // 2) FALL BACK to SnapPoint1/2 match for V1E and V1C (these dock
        //    directly via their main snaps, not via T-snaps).
        float tightTol = 0.15f * scaleFactor;
        float fallbackTol = 1.0f * scaleFactor;
        Transform FindPillarAtRailEnd(Vector3 railEndPos)
        {
            Transform bestT = null;
            float bestTDist = float.MaxValue;
            Transform bestFB = null;
            float bestFBDist = float.MaxValue;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t || !IsPillarInstance(t.gameObject)) continue;
                // Stage 1: T-SnapPoints (tight) — V1M
                foreach (var snName in new[] { "SnapPointT1", "SnapPointT2" })
                {
                    var s = FindSnap(t, snName);
                    if (!s) continue;
                    float d = Vector3.Distance(s.position, railEndPos);
                    if (d < tightTol && d < bestTDist) { bestTDist = d; bestT = t; }
                }
                // Stage 2: SnapPoint1/2 only on E and C pillars (generous) — both families
                var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                if (src && (src.name.Contains("V1E") || IsCorner(src.name, "V1") ||
                            src.name.Contains("V2E") || IsCorner(src.name, "V2")))
                {
                    foreach (var snName in new[] { "SnapPoint1", "SnapPoint2" })
                    {
                        var s = FindSnap(t, snName);
                        if (!s) continue;
                        float d = Vector3.Distance(s.position, railEndPos);
                        if (d < fallbackTol && d < bestFBDist) { bestFBDist = d; bestFB = t; }
                    }
                }
            }
            return bestT != null ? bestT : bestFB;
        }

        Transform pA = FindPillarAtRailEnd(endA);
        Transform pB = FindPillarAtRailEnd(endB);
        // Re-classify the actually-found pillars (kindA/kindB from initial
        // snapPosA/B may not correspond to endA/endB after the swap).
        ArcSwapPillarKind ClassifyTransform(Transform t)
        {
            if (!t) return ArcSwapPillarKind.None;
            var s = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
            if (!s) return ArcSwapPillarKind.Other;
            if (s.name.Contains("V1M")) return ArcSwapPillarKind.V1M;
            if (s.name.Contains("V1E")) return ArcSwapPillarKind.V1E;
            if (IsCorner(s.name, "V1")) return ArcSwapPillarKind.V1C;
            if (s.name.Contains("V2M")) return ArcSwapPillarKind.V2M;
            if (s.name.Contains("V2E")) return ArcSwapPillarKind.V2E;
            if (IsCorner(s.name, "V2")) return ArcSwapPillarKind.V2C;
            return ArcSwapPillarKind.Other;
        }
        var kA = ClassifyTransform(pA);
        var kB = ClassifyTransform(pB);
        Vector3 posA = pA ? pA.position : Vector3.zero;
        Vector3 posB = pB ? pB.position : Vector3.zero;
        bool hasA = pA != null;
        bool hasB = pB != null && pB != pA;

        // Execute per-side action defined by the dispatch table.
        if (hasA) ExecutePillarAction(GetPillarAction(kA), pA, posA, endA, root, oldRail);
        if (hasB) ExecutePillarAction(GetPillarAction(kB), pB, posB, endB, root, oldRail);

        // Locate the (possibly newly instantiated) pillars by world position
        // and run the alignment step appropriate for their resulting kind.
        Transform FindPillarAtPos(Vector3 p)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t || !IsPillarInstance(t.gameObject)) continue;
                if (Vector3.Distance(t.position, p) < 0.001f * scaleFactor + 0.001f)
                    return t;
            }
            return null;
        }
        if (hasA) AlignPillarForArcSwap(GetPillarAction(kA), FindPillarAtPos(posA), endA, root, oldRail);
        if (hasB) AlignPillarForArcSwap(GetPillarAction(kB), FindPillarAtPos(posB), endB, root, oldRail);
    }, selectRootAfter: oldRail);

    if (fullDetailMode && root) ApplyFullDetailToBalustrade(root.gameObject, true);

    Undo.CollapseUndoOperations(undoGroup);
}

// =========================================================================
// ARC SWAP DISPATCH: pillar classification + per-kind action table
// =========================================================================
enum ArcSwapPillarKind { None, V1M, V1E, V1C, V1T, V2M, V2E, V2C, V2T, Other }
enum ArcSwapAction { Skip, ReplaceV1MToV1C, ReplaceV1CToV1M, RotateV1E, ReplaceV2MToV2C, ReplaceV2CToV2M, RotateV2E }

// True if the prefab name represents a true C-pillar (corner) — NOT C45.
bool IsCorner(string n, string family) =>
    n != null && n.Contains(family + "C") && !n.Contains("C45");

ArcSwapPillarKind GetPillarKindAt(Transform root, Vector3 p)
{
    float sf = Mathf.Max(root.lossyScale.x, 1f);
    float tol = 0.15f * sf;
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (!t || !IsPillarInstance(t.gameObject)) continue;
        var a = FindSnap(t, "SnapPoint1");
        var b = FindSnap(t, "SnapPoint2");
        var c = FindSnap(t, "SnapPoint3");
        bool hit = (a && Vector3.Distance(a.position, p) < tol) ||
                   (b && Vector3.Distance(b.position, p) < tol) ||
                   (c && Vector3.Distance(c.position, p) < tol);
        if (!hit) continue;
        var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (!src) return ArcSwapPillarKind.Other;
        if (src.name.Contains("V1M")) return ArcSwapPillarKind.V1M;
        if (src.name.Contains("V1E")) return ArcSwapPillarKind.V1E;
        if (IsCorner(src.name, "V1")) return ArcSwapPillarKind.V1C;
        if (src.name.Contains("V1T")) return ArcSwapPillarKind.V1T;
        if (src.name.Contains("V2M")) return ArcSwapPillarKind.V2M;
        if (src.name.Contains("V2E")) return ArcSwapPillarKind.V2E;
        if (IsCorner(src.name, "V2")) return ArcSwapPillarKind.V2C;
        if (src.name.Contains("V2T")) return ArcSwapPillarKind.V2T;
        return ArcSwapPillarKind.Other;
    }
    return ArcSwapPillarKind.None;
}

// Allowed combinations.
bool IsArcSwapAllowed(ArcSwapPillarKind a, ArcSwapPillarKind b)
{
    if (a == ArcSwapPillarKind.V1M && b == ArcSwapPillarKind.V1M) return true;
    if (a == ArcSwapPillarKind.V1M && b == ArcSwapPillarKind.V1E) return true;
    if (a == ArcSwapPillarKind.V1E && b == ArcSwapPillarKind.V1M) return true;
    if (a == ArcSwapPillarKind.V1M && b == ArcSwapPillarKind.V1C) return true;
    if (a == ArcSwapPillarKind.V1C && b == ArcSwapPillarKind.V1M) return true;
    if (a == ArcSwapPillarKind.V2M && b == ArcSwapPillarKind.V2M) return true;
    if (a == ArcSwapPillarKind.V2M && b == ArcSwapPillarKind.V2E) return true;
    if (a == ArcSwapPillarKind.V2E && b == ArcSwapPillarKind.V2M) return true;
    if (a == ArcSwapPillarKind.V2M && b == ArcSwapPillarKind.V2C) return true;
    if (a == ArcSwapPillarKind.V2C && b == ArcSwapPillarKind.V2M) return true;
    return false;
}

ArcSwapAction GetPillarAction(ArcSwapPillarKind k)
{
    switch (k)
    {
        case ArcSwapPillarKind.V1M: return ArcSwapAction.ReplaceV1MToV1C;
        case ArcSwapPillarKind.V1E: return ArcSwapAction.RotateV1E;
        case ArcSwapPillarKind.V1C: return ArcSwapAction.ReplaceV1CToV1M;
        case ArcSwapPillarKind.V2M: return ArcSwapAction.ReplaceV2MToV2C;
        case ArcSwapPillarKind.V2E: return ArcSwapAction.RotateV2E;
        case ArcSwapPillarKind.V2C: return ArcSwapAction.ReplaceV2CToV2M;
        default: return ArcSwapAction.Skip;
    }
}

// Step 1 of two-phase action: structural change (replace prefab) if needed.
// Position/rotation/scale must be preserved at this stage.
void ExecutePillarAction(ArcSwapAction action, Transform pillar, Vector3 pos,
                          Vector3 railEnd, Transform root, GameObject curvedRail)
{
    switch (action)
    {
        case ArcSwapAction.ReplaceV1MToV1C:
            ReplaceMWithC(pillar, "V1");
            break;
        case ArcSwapAction.ReplaceV1CToV1M:
            ReplaceCWithM(pillar, "V1");
            break;
        case ArcSwapAction.RotateV1E:
            break;
        case ArcSwapAction.ReplaceV2MToV2C:
            ReplaceMWithC(pillar, "V2");
            break;
        case ArcSwapAction.ReplaceV2CToV2M:
            ReplaceCWithM(pillar, "V2");
            break;
        case ArcSwapAction.RotateV2E:
            break;
    }
}

// Step 2 of two-phase action: rotation/alignment.
void AlignPillarForArcSwap(ArcSwapAction action, Transform pillar, Vector3 railEnd,
                            Transform root, GameObject curvedRail)
{
    if (!pillar) return;
    switch (action)
    {
        case ArcSwapAction.ReplaceV1MToV1C:
        case ArcSwapAction.ReplaceV1CToV1M:
        case ArcSwapAction.ReplaceV2MToV2C:
        case ArcSwapAction.ReplaceV2CToV2M:
            RotatePillarToNearestRailSnaps(pillar,
                new[] { "SnapPoint1", "SnapPoint2" }, root);
            break;
        case ArcSwapAction.RotateV1E:
        case ArcSwapAction.RotateV2E:
            RotatePillarToNearestRailSnaps(pillar,
                new[] { "SnapPoint1" }, root);
            break;
    }
}

// Unified pillar-rotation helper. Rotates `pillar` around world-Y in
// 45° steps and picks the rotation where the given snap names (1 to N
// points) land closest to rail snaps in the scene, with each T-snap
// assigned to a UNIQUE rail snap (greedy). Used by all Arc-Swap paths.
void RotatePillarToNearestRailSnaps(Transform pillar, string[] snapNames, Transform root)
{
    if (!pillar || snapNames == null || snapNames.Length == 0) return;

    // Collect all rail snap positions in the scene.
    var railSnaps = new List<Vector3>();
    foreach (Transform rt in root.GetComponentsInChildren<Transform>(true))
    {
        if (!rt || !IsRailInstance(rt.gameObject)) continue;
        if (IsHiddenDeletedRail(rt.gameObject)) continue;
        foreach (var rsn in new[] { RailStartSnap, RailEndSnap })
        {
            var s = FindSnap(rt, rsn);
            if (s) railSnaps.Add(s.position);
        }
    }
    if (railSnaps.Count == 0) return;

    // Cache pillar-snap offsets.
    var snapOffsets = new List<Vector3>();
    foreach (var n in snapNames)
    {
        var sp = FindSnap(pillar, n);
        if (!sp) return; // bail if any named snap is missing
        snapOffsets.Add(sp.position - pillar.position);
    }

    int bestStep = 0;
    float bestScore = float.MaxValue;
    for (int step = 0; step < 8; step++)
    {
        float trial = step * 45f;
        var simPositions = new List<Vector3>();
        foreach (var off in snapOffsets)
            simPositions.Add(pillar.position + Quaternion.AngleAxis(trial, Vector3.up) * off);

        // Greedy unique assignment: each pillar-snap gets the closest
        // still-unused rail snap.
        var used = new HashSet<int>();
        float score = 0f;
        bool ok = true;
        foreach (var simSp in simPositions)
        {
            int bestIdx = -1;
            float minD = float.MaxValue;
            for (int i = 0; i < railSnaps.Count; i++)
            {
                if (used.Contains(i)) continue;
                float d = Vector3.Distance(railSnaps[i], simSp);
                if (d < minD) { minD = d; bestIdx = i; }
            }
            if (bestIdx < 0) { ok = false; break; }
            used.Add(bestIdx);
            score += minD;
        }
        if (!ok) continue;
        if (score < bestScore) { bestScore = score; bestStep = step; }
    }
    if (bestStep != 0)
    {
        Undo.RecordObject(pillar, "Arc Swap Pillar Rot");
        pillar.Rotate(Vector3.up, bestStep * 45f, Space.World);
    }
}

// Rotates a V1E pillar in 90° steps around world-Y until its SnapPoint1
// world position is closest (XZ only, Y ignored) to railEndPos. The
// pillar itself is rotated; SnapPoint1 is its child and follows.
void AlignV1ESnapToRailEnd(Transform pillar, Vector3 railEndPos, Transform railSnapAtEnd)
{
    if (!pillar) return;
    var sp = FindSnap(pillar, "SnapPoint1");
    if (!sp) return;

    Vector3 pivot = pillar.position;
    Vector3 curOff = sp.position - pivot;  curOff.y = 0f;
    Vector3 tgt = railEndPos;              tgt.y = pivot.y;

    int bestStep = 0;
    float bestD = float.MaxValue;
    for (int step = 0; step < 4; step++)
    {
        Vector3 rotated = Quaternion.AngleAxis(step * 90f, Vector3.up) * curOff;
        float d = Vector3.Distance(pivot + rotated, tgt);
        if (d < bestD) { bestD = d; bestStep = step; }
    }
    if (bestStep == 0) return;
    Undo.RecordObject(pillar, "Arc Swap V1E Align");
    pillar.Rotate(Vector3.up, bestStep * 90f, Space.World);
}

// Replace M pillar with C variant within the same family ("V1" or "V2").
// Carries the top (if any) from oldPillar over to newPillar, using the
// same TopPrefabNames lookup that the rest of the code uses. V1 only —
// V2 has no tops.
void TransferPillarTop(GameObject oldPillar, GameObject newPillar)
{
    if (!oldPillar || !newPillar) return;
    int topIdx = GetTopIndexFromPillar(oldPillar);
    if (topIdx < 0 || topIdx >= TopPrefabNames.Length) return;
    var snapTop = FindSnap(newPillar.transform, TopSnapName);
    var topPrefab = FindAsset<GameObject>(TopPrefabNames[topIdx]);
    if (!snapTop || !topPrefab) return;
    var top = (GameObject)PrefabUtility.InstantiatePrefab(topPrefab);
    ApplyCurrentTextureVariantToObject(top);
    Undo.RegisterCreatedObjectUndo(top, "Arc Swap Top Carry");
    top.transform.SetParent(newPillar.transform, false);
    top.transform.position = snapTop.position;
    top.transform.rotation = snapTop.rotation;
    ApplyTopVisualRotation(top);
}

void ReplaceMWithC(Transform pillar, string family)
{
    if (!pillar) return;
    var src = PrefabUtility.GetCorrespondingObjectFromSource(pillar.gameObject);
    string mTag = family + "M";
    string cTag = family + "C";
    if (!src || !src.name.Contains(mTag)) return;
    string toName = src.name.Replace(mTag, cTag);
    var newPrefab = FindAsset<GameObject>(toName);
    if (!newPrefab) return;

    Transform parent = pillar.parent;
    Vector3 pos = pillar.position;
    Quaternion rot = pillar.rotation;
    Vector3 scl = pillar.localScale;
    int sib = pillar.GetSiblingIndex();
    var oldGo = pillar.gameObject;

    var np = (GameObject)PrefabUtility.InstantiatePrefab(newPrefab);
    Undo.RegisterCreatedObjectUndo(np, "Arc Swap " + mTag + "->" + cTag);
    np.transform.SetParent(parent, true);
    np.transform.SetPositionAndRotation(pos, rot);
    np.transform.localScale = scl;
    np.transform.SetSiblingIndex(sib);
    ApplyCurrentTextureVariantToObject(np);
    TransferPillarTop(oldGo, np);
    Undo.DestroyObjectImmediate(oldGo);
}

// Replace C pillar with M variant within the same family ("V1" or "V2").
void ReplaceCWithM(Transform pillar, string family)
{
    if (!pillar) return;
    var src = PrefabUtility.GetCorrespondingObjectFromSource(pillar.gameObject);
    string cTag = family + "C";
    string mTag = family + "M";
    if (!src || !IsCorner(src.name, family)) return;
    string toName = src.name.Replace(cTag, mTag);
    var newPrefab = FindAsset<GameObject>(toName);
    if (!newPrefab) return;

    Transform parent = pillar.parent;
    Vector3 pos = pillar.position;
    Vector3 scl = pillar.localScale;
    int sib = pillar.GetSiblingIndex();
    var oldGo = pillar.gameObject;

    var np = (GameObject)PrefabUtility.InstantiatePrefab(newPrefab);
    Undo.RegisterCreatedObjectUndo(np, "Arc Swap " + cTag + "->" + mTag);
    np.transform.SetParent(parent, true);
    np.transform.SetPositionAndRotation(pos, parent ? parent.rotation : Quaternion.identity);
    np.transform.localScale = scl;
    np.transform.SetSiblingIndex(sib);
    ApplyCurrentTextureVariantToObject(np);
    TransferPillarTop(oldGo, np);
    Undo.DestroyObjectImmediate(oldGo);
}

// Rotates pillar around world-Y so that ONE of its SnapPoint1/2 touches

// Rotates pillar around world-Y so that ONE of its SnapPoint1/2 touches
// curvedEnd (the curved rail endpoint at this side) AND the OTHER snap
// lands nearest to some OTHER rail snap in the scene.
// Pillar position / scale untouched.
void AlignV1CSnap1ToRailEnd(Transform pillar, Vector3 curvedEnd, Transform root, GameObject curvedRail)
{
    if (!pillar) return;
    var sp1 = FindSnap(pillar, "SnapPoint1");
    var sp2 = FindSnap(pillar, "SnapPoint2");
    if (!sp1 || !sp2) return;

    Vector3 pos = pillar.position;
    Vector3 tgtDir = Vector3.ProjectOnPlane(curvedEnd - pos, Vector3.up);
    if (tgtDir.sqrMagnitude < 1e-6f) return;
    Vector3 tgtN = tgtDir.normalized;

    // Try both candidates: SnapPoint1 -> curvedEnd, and SnapPoint2 -> curvedEnd.
    // For each, simulate the resulting pillar rotation and measure how close
    // the OTHER snap lands to any OTHER rail (not the curved one) snap.
    string[] names = { "SnapPoint1", "SnapPoint2" };
    Transform[] snaps = { sp1, sp2 };
    float bestOther = float.MaxValue;
    float bestAngle = 0f;
    for (int i = 0; i < 2; i++)
    {
        Vector3 cur = Vector3.ProjectOnPlane(snaps[i].position - pos, Vector3.up);
        if (cur.sqrMagnitude < 1e-6f) continue;
        float angle = Vector3.SignedAngle(cur.normalized, tgtN, Vector3.up);
        // Simulate: where does the OTHER snap end up after this Y rotation?
        Transform other = snaps[1 - i];
        Vector3 otherAfter = pos + Quaternion.AngleAxis(angle, Vector3.up) * (other.position - pos);
        // Find nearest rail-snap in scene EXCLUDING the curved rail.
        float minD = float.MaxValue;
        foreach (Transform rt in root.GetComponentsInChildren<Transform>(true))
        {
            if (!rt || !IsRailInstance(rt.gameObject)) continue;
            if (rt.gameObject == curvedRail) continue;
            if (IsHiddenDeletedRail(rt.gameObject)) continue;
            foreach (var sn in new[] { RailStartSnap, RailEndSnap })
            {
                var s = FindSnap(rt, sn);
                if (!s) continue;
                float d = Vector3.Distance(s.position, otherAfter);
                if (d < minD) minD = d;
            }
        }
        if (minD < bestOther) { bestOther = minD; bestAngle = angle; }
    }
    if (bestOther < float.MaxValue)
    {
        Undo.RecordObject(pillar, "Arc Swap V1C Align");
        pillar.Rotate(Vector3.up, bestAngle, Space.World);
    }
}


// =========================================================================
// DEDICATED T-PILLAR SWAP PATH (T-pillar on one side, any pillar on other)
// =========================================================================
// T stays as T — rotated in 45° steps until all 3 SnapPoints sit on some
// rail snap. Opposite pillar uses the generic per-kind action (M→C, C→M,
// E rotated in place via AlignPillarForArcSwap).
void PerformArcSwap_T(GameObject oldRail, Transform root, Vector3 snapPosA, Vector3 snapPosB,
                       ArcSwapPillarKind kindA, ArcSwapPillarKind kindB)
{
    if (!oldRail) return;

    float scaleFactor = Mathf.Max(root.lossyScale.x, 1f);
    float tol = 0.15f * scaleFactor;

    // Resolve both neighbour pillars via SnapPoint1/2/3 at snapPosA/B.
    Transform PillarAt(Vector3 p)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!t || !IsPillarInstance(t.gameObject)) continue;
            foreach (var sn in new[] { "SnapPoint1", "SnapPoint2", "SnapPoint3" })
            {
                var s = FindSnap(t, sn);
                if (s && Vector3.Distance(s.position, p) < tol) return t;
            }
        }
        return null;
    }
    var pA = PillarAt(snapPosA);
    var pB = PillarAt(snapPosB);
    if (!pA || !pB) return;

    bool aIsT = kindA == ArcSwapPillarKind.V1T || kindA == ArcSwapPillarKind.V2T;
    bool bIsT = kindB == ArcSwapPillarKind.V1T || kindB == ArcSwapPillarKind.V2T;
    bool bothT = aIsT && bIsT;
    Transform pT     = aIsT ? pA : pB;
    Transform pOther = aIsT ? pB : pA;
    var kindOther    = aIsT ? kindB : kindA;
    Vector3 otherEnd = aIsT ? snapPosB : snapPosA;

    Vector3 mid = (pA.position + pB.position) * 0.5f;

    int undoGroup = Undo.GetCurrentGroup();
    Undo.SetCurrentGroupName("Arc Swap");

    SafeUiSwap(() =>
    {
        Undo.RecordObject(oldRail.transform, "Arc Swap");

        // 180° rotation of curved rail around pillar-pivot mid-point.
        Vector3 upAxis = oldRail.transform.parent ? oldRail.transform.parent.up : Vector3.up;
        Quaternion yRot = Quaternion.AngleAxis(180f, upAxis);
        Vector3 newWorldPos = mid + yRot * (oldRail.transform.position - mid);
        Vector3 posDelta = newWorldPos - oldRail.transform.position;
        if (oldRail.transform.parent)
            oldRail.transform.localPosition += oldRail.transform.parent.InverseTransformVector(posDelta);
        else
            oldRail.transform.position = newWorldPos;
        oldRail.transform.rotation = yRot * oldRail.transform.rotation;

        // Rotate first T (always present).
        var tSnaps = new[] { "SnapPoint1", "SnapPoint2", "SnapPoint3" };
        RotatePillarToNearestRailSnaps(pT, tSnaps, root);

        if (bothT)
        {
            // T + T: rotate the second T as well, no replace action.
            RotatePillarToNearestRailSnaps(pOther, tSnaps, root);
        }
        else
        {
            // Opposite pillar: generic per-kind action (M→C, C→M, E rotate).
            var action = GetPillarAction(kindOther);
            Vector3 otherPos = pOther.position;
            ExecutePillarAction(action, pOther, otherPos, otherEnd, root, oldRail);
            // Re-locate (may have been replaced) and align.
            Transform newOther = null;
            float bestD2 = float.MaxValue;
            float searchTol = 0.05f * scaleFactor;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t || !IsPillarInstance(t.gameObject)) continue;
                float d = Vector3.Distance(t.position, otherPos);
                if (d < searchTol && d < bestD2) { bestD2 = d; newOther = t; }
            }
            if (newOther) AlignPillarForArcSwap(action, newOther, otherEnd, root, oldRail);
        }
    }, selectRootAfter: oldRail);

    if (fullDetailMode && root) ApplyFullDetailToBalustrade(root.gameObject, true);
    Undo.CollapseUndoOperations(undoGroup);
}


// =========================================================================
// DEDICATED V1C + Rail + V1C SWAP PATH (minimal: just the mirror rotation)
// =========================================================================
void PerformArcSwap_CC(GameObject oldRail, Transform root, Vector3 snapPosA, Vector3 snapPosB, string family)
{
    if (!oldRail) return;

    float scaleFactor = Mathf.Max(root.lossyScale.x, 1f);
    float tol = 0.15f * scaleFactor;

    // Resolve both V1C pillars.
    Transform pA = null, pB = null;
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (!t || !IsPillarInstance(t.gameObject)) continue;
        var s = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (!s || !IsCorner(s.name, family)) continue;
        var p1 = FindSnap(t, "SnapPoint1");
        var p2 = FindSnap(t, "SnapPoint2");
        bool hitA = (p1 && Vector3.Distance(p1.position, snapPosA) < tol) ||
                    (p2 && Vector3.Distance(p2.position, snapPosA) < tol);
        bool hitB = (p1 && Vector3.Distance(p1.position, snapPosB) < tol) ||
                    (p2 && Vector3.Distance(p2.position, snapPosB) < tol);
        if (hitA && !pA) pA = t;
        else if (hitB && !pB) pB = t;
        if (pA && pB) break;
    }
    if (!pA || !pB) return;

    // Pillar pivots are exactly centered → mid-point between the two
    // pillar pivots is the correct mirror axis. No helper snaps needed.
    Vector3 mid = (pA.position + pB.position) * 0.5f;

    int undoGroup = Undo.GetCurrentGroup();
    Undo.SetCurrentGroupName("Arc Swap");

    SafeUiSwap(() =>
    {
        Undo.RecordObject(oldRail.transform, "Arc Swap");

        Vector3 upAxis = oldRail.transform.parent ? oldRail.transform.parent.up : Vector3.up;
        Quaternion yRot = Quaternion.AngleAxis(180f, upAxis);
        Vector3 newWorldPos = mid + yRot * (oldRail.transform.position - mid);
        Vector3 posDelta = newWorldPos - oldRail.transform.position;
        if (oldRail.transform.parent)
            oldRail.transform.localPosition += oldRail.transform.parent.InverseTransformVector(posDelta);
        else
            oldRail.transform.position = newWorldPos;
        oldRail.transform.rotation = yRot * oldRail.transform.rotation;

        // Replace both C → M.
        Vector3 posA = pA.position;
        Vector3 posB = pB.position;
        ReplaceCWithM(pA, family);
        ReplaceCWithM(pB, family);

        // Locate new pillars by world position.
        Transform FindPillarAtPos(Vector3 p)
        {
            Transform best = null;
            float bestD = float.MaxValue;
            float searchTol = 0.05f * scaleFactor;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t || !IsPillarInstance(t.gameObject)) continue;
                float d = Vector3.Distance(t.position, p);
                if (d < searchTol && d < bestD) { bestD = d; best = t; }
            }
            return best;
        }
        var newA = FindPillarAtPos(posA);
        var newB = FindPillarAtPos(posB);

        // Both new M-pillars re-oriented via unified 45°-step helper.
        var snapNames = new[] { "SnapPoint1", "SnapPoint2" };
        RotatePillarToNearestRailSnaps(newA, snapNames, root);
        if (newB != newA) RotatePillarToNearestRailSnaps(newB, snapNames, root);
    }, selectRootAfter: oldRail);

    if (fullDetailMode && root) ApplyFullDetailToBalustrade(root.gameObject, true);
    Undo.CollapseUndoOperations(undoGroup);
}

// =========================================================================
// DEDICATED V1C + Rail + V1E SWAP PATH
// =========================================================================
// Mid-point from V1C Helper SnapPoint + V1E Helper SnapPoint.
// V1C → V1M (rotate-until-snap). V1E stays V1E (rotated to face curved rail).
void PerformArcSwap_CE(GameObject oldRail, Transform root, Vector3 snapPosA, Vector3 snapPosB, string family)
{
    if (!oldRail) return;

    float scaleFactor = Mathf.Max(root.lossyScale.x, 1f);
    float tol = 0.15f * scaleFactor;

    // Resolve V1C pillar and V1E pillar from snapPosA / snapPosB.
    Transform pV1C = null, pV1E = null;
    Vector3 v1cAttachedPos = Vector3.zero, v1eAttachedPos = Vector3.zero;
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (!t || !IsPillarInstance(t.gameObject)) continue;
        var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (!src) continue;
        var p1 = FindSnap(t, "SnapPoint1");
        var p2 = FindSnap(t, "SnapPoint2");
        bool hitA = (p1 && Vector3.Distance(p1.position, snapPosA) < tol) ||
                    (p2 && Vector3.Distance(p2.position, snapPosA) < tol);
        bool hitB = (p1 && Vector3.Distance(p1.position, snapPosB) < tol) ||
                    (p2 && Vector3.Distance(p2.position, snapPosB) < tol);
        if ((hitA || hitB) && !pV1C && IsCorner(src.name, family))
        {
            pV1C = t;
            v1cAttachedPos = hitA ? snapPosA : snapPosB;
        }
        else if ((hitA || hitB) && !pV1E && src.name.Contains(family + "E"))
        {
            pV1E = t;
            v1eAttachedPos = hitA ? snapPosA : snapPosB;
        }
        if (pV1C && pV1E) break;
    }
    if (!pV1C || !pV1E) return;

    // Pillar pivots are exactly centered → mid-point between the two
    // pillar pivots is the correct mirror axis.
    Vector3 mid = (pV1C.position + pV1E.position) * 0.5f;

    int undoGroup = Undo.GetCurrentGroup();
    Undo.SetCurrentGroupName("Arc Swap");

    SafeUiSwap(() =>
    {
        Undo.RecordObject(oldRail.transform, "Arc Swap");

        // 180° rotation around pillar-pivot mid-point.
        Vector3 upAxis = oldRail.transform.parent ? oldRail.transform.parent.up : Vector3.up;
        Quaternion yRot = Quaternion.AngleAxis(180f, upAxis);
        Vector3 newWorldPos = mid + yRot * (oldRail.transform.position - mid);
        Vector3 posDelta = newWorldPos - oldRail.transform.position;
        if (oldRail.transform.parent)
            oldRail.transform.localPosition += oldRail.transform.parent.InverseTransformVector(posDelta);
        else
            oldRail.transform.position = newWorldPos;
        oldRail.transform.rotation = yRot * oldRail.transform.rotation;

        // C → M, then rotate via unified 45°-step helper.
        Vector3 v1cPos = pV1C.position;
        ReplaceCWithM(pV1C, family);
        Transform FindPillarAtPos(Vector3 p)
        {
            Transform best = null;
            float bestD2 = float.MaxValue;
            float searchTol = 0.05f * scaleFactor;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t || !IsPillarInstance(t.gameObject)) continue;
                float d = Vector3.Distance(t.position, p);
                if (d < searchTol && d < bestD2) { bestD2 = d; best = t; }
            }
            return best;
        }
        var newV1M = FindPillarAtPos(v1cPos);
        if (newV1M)
            RotatePillarToNearestRailSnaps(newV1M,
                new[] { "SnapPoint1", "SnapPoint2" }, root);

        // E-pillar: unified 45°-step helper too.
        if (pV1E)
            RotatePillarToNearestRailSnaps(pV1E,
                new[] { "SnapPoint1" }, root);
    }, selectRootAfter: oldRail);

    if (fullDetailMode && root) ApplyFullDetailToBalustrade(root.gameObject, true);
    Undo.CollapseUndoOperations(undoGroup);
}


void SwapAdjacentPillarAt(Transform root, Vector3 snapWorldPos)
{
    float scaleFactor = Mathf.Max(root.lossyScale.x, 1f);
    float tol = 0.15f * scaleFactor;

    GameObject target = null;
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (!t || !IsPillarInstance(t.gameObject)) continue;
        foreach (var sn in new[] { "SnapPoint1", "SnapPoint2" })
        {
            var s = FindSnap(t, sn);
            if (!s) continue;
            if (Vector3.Distance(s.position, snapWorldPos) < tol) { target = t.gameObject; break; }
        }
        if (target) break;
    }
    if (!target) return;

    var src = PrefabUtility.GetCorrespondingObjectFromSource(target);
    if (!src) return;
    string n = src.name;
    if (n.Contains("E_PREFAB")) return;
    if (n.Contains("C45")) return;

    string toName = null;
    if      (n.Contains("V1M")) toName = n.Replace("V1M", "V1C");
    else if (n.Contains("V2M")) toName = n.Replace("V2M", "V2C");
    else if (n.Contains("V1C")) toName = n.Replace("V1C", "V1M");
    else if (n.Contains("V2C")) toName = n.Replace("V2C", "V2M");
    if (toName == null) return;

    var newPrefab = FindAsset<GameObject>(toName);
    if (!newPrefab) return;

    Transform parent = target.transform.parent;
    Vector3 pos = target.transform.position;
    Quaternion rot = target.transform.rotation;
    Vector3 scl = target.transform.localScale;
    int sib = target.transform.GetSiblingIndex();

    // Preserve top
    int topIdx = -1;
    foreach (Transform c in target.transform)
    {
        if (!c || !IsTopInstance(c.gameObject)) continue;
        var ts = PrefabUtility.GetCorrespondingObjectFromSource(c.gameObject);
        if (!ts) continue;
        for (int i = 0; i < TopPrefabNames.Length; i++)
            if (TopPrefabNames[i] == ts.name) { topIdx = i; break; }
        if (topIdx >= 0) break;
    }

    var newPillar = (GameObject)PrefabUtility.InstantiatePrefab(newPrefab);
    newPillar.transform.SetParent(parent, true);
    newPillar.transform.SetPositionAndRotation(pos, rot);
    newPillar.transform.localScale = scl;
    newPillar.transform.SetSiblingIndex(sib);
    ApplyCurrentTextureVariantToObject(newPillar);

    // Find the rail snap at this world position (skip HIDDEN rails).
    Transform railSnap = null;
    float rtol = 0.15f * scaleFactor;
    foreach (Transform rt in root.GetComponentsInChildren<Transform>(true))
    {
        if (!rt || !IsRailInstance(rt.gameObject)) continue;
        if (IsHiddenDeletedRail(rt.gameObject)) continue;
        var rs1 = FindSnap(rt, RailStartSnap);
        var rs2 = FindSnap(rt, RailEndSnap);
        if (rs1 && Vector3.Distance(rs1.position, snapWorldPos) < rtol) { railSnap = rs1; break; }
        if (rs2 && Vector3.Distance(rs2.position, snapWorldPos) < rtol) { railSnap = rs2; break; }
    }

    // Rotate newPillar in 90° steps around its OWN pivot Y-axis. Pick the
    // step where SnapPoint1 is closest to the rail snap position.
    if (railSnap)
    {
        Quaternion startRot = newPillar.transform.rotation;
        float bestDist = float.MaxValue;
        int bestStep = 0;
        for (int step = 0; step < 4; step++)
        {
            newPillar.transform.rotation = startRot * Quaternion.Euler(0f, 90f * step, 0f);
            var sp1 = FindSnap(newPillar.transform, "SnapPoint1");
            if (!sp1) continue;
            float d = Vector3.Distance(sp1.position, railSnap.position);
            if (d < bestDist) { bestDist = d; bestStep = step; }
        }
        newPillar.transform.rotation = startRot * Quaternion.Euler(0f, 90f * bestStep, 0f);
    }

    // Re-add top at the new top snap position/orientation
    if (topIdx >= 0 && selectedVariant == ContinueVariant.V1)
    {
        var topPrefab = FindAsset<GameObject>(TopPrefabNames[topIdx]);
        var snapTop = FindSnap(newPillar.transform, TopSnapName);
        if (topPrefab && snapTop)
        {
            var top = (GameObject)PrefabUtility.InstantiatePrefab(topPrefab);
            ApplyCurrentTextureVariantToObject(top);
            top.transform.SetParent(newPillar.transform, false);
            top.transform.position = snapTop.position;
            top.transform.rotation = snapTop.rotation;
            ApplyTopVisualRotation(top);
            Undo.RegisterCreatedObjectUndo(top, "Arc Swap Top");
        }
    }

    Undo.RegisterCreatedObjectUndo(newPillar, "Arc Swap Pillar");
    Undo.DestroyObjectImmediate(target);
}
}
} // namespace WB3DAssets.BalustradeModularSystem
