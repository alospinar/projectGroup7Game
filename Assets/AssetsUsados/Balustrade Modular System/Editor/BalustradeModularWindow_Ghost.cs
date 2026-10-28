using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace WB3DAssets.BalustradeModularSystem
{
public partial class BalustradeModularWindow
{
void HideLastGhostPillarM()
{
    if (ghostPillarsM.Count == 0) return;

    var lastGhost = ghostPillarsM[^1];
    if (!lastGhost) return;

foreach (var rend in lastGhost.GetComponentsInChildren<Renderer>(true))
        rend.enabled = false;
}

void HideCurvedGhostEndPillar()
{
    if (!curvedGhostActive || !ghostCurvedPillar) return;

    foreach (var rend in ghostCurvedPillar.GetComponentsInChildren<Renderer>(true))
        rend.enabled = false;
}

void ShowCurvedGhostEndPillar()
{
    if (!ghostCurvedPillar) return;

    foreach (var rend in ghostCurvedPillar.GetComponentsInChildren<Renderer>(true))
        rend.enabled = true;
}

void ShowLastGhostPillarM()
{
    if (ghostPillarsM.Count == 0) return;

    var lastGhost = ghostPillarsM[^1];
    if (!lastGhost) return;

foreach (var rend in lastGhost.GetComponentsInChildren<Renderer>(true))
        rend.enabled = true;
}

void ClearCurvedGhost()
{
    // if ghost currently points to the curved root, detach first
    if (ghost == curvedGhostRoot)
        ghost = null;

    if (curvedGhostRoot)
        DestroyImmediate(curvedGhostRoot);

    curvedGhostRoot = null;
    ghostCurvedRail = null;
    ghostCurvedPillar = null;
    curvedGhostEndSnap = null;
}

void ClearHover90Preview()
{
// Do not disable continue ghost here; it's the base visual replacement in continue mode.

    ShowLastGhostPillarM();
    ShowCurvedGhostEndPillar();
    ClearCloseLoopFull();

    if (hover90Root)
        DestroyImmediate(hover90Root);

    hover90Root = null;
    hover90EndPillarE = null;
    hover90Rail = null;
    hover90PillarM = null;
    
    // Clear chain lists (objects are children of hover90Root, already destroyed)
    hoverChainRails.Clear();
    hoverChainPillarsM.Clear();
    hoverChainStartSnap = null;
}

void EnsureHoverChainSegs(int count)
{
    if (!hover90Root) return;
    
    var railPrefab = FindAsset<GameObject>(
        $"blstrs_{balusterStyleIndex + 1}{(selectedVariant == ContinueVariant.V2 ? "V2" : "V1")}_PREFAB"
    );
    var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));
    
    if (!railPrefab || !pillarMPrefab) return;

    while (hoverChainRails.Count < count)
    {
        hoverChainRails.Add(CreateGhost(railPrefab, hover90Root.transform));
        var newPillar = CreateGhost(pillarMPrefab, hover90Root.transform);
        AddGhostTopToPillar(newPillar);  // Add ghost top if balustrade has tops
        hoverChainPillarsM.Add(newPillar);
    }

    while (hoverChainRails.Count > count)
    {
        DestroyImmediate(hoverChainRails[^1]);
        hoverChainRails.RemoveAt(hoverChainRails.Count - 1);

        DestroyImmediate(hoverChainPillarsM[^1]);
        hoverChainPillarsM.RemoveAt(hoverChainPillarsM.Count - 1);
    }
}

void LayoutHoverChain(Vector3 dir, Transform startSnap)
{
    if (hoverChainRails.Count == 0 || !startSnap) return;
    
    Transform target = startSnap;

    for (int i = 0; i < hoverChainRails.Count; i++)
    {
        var rail = hoverChainRails[i];
        var pillar = hoverChainPillarsM[i];

        var rs = FindSnap(rail.transform, RailStartSnap);
        var re = FindSnap(rail.transform, RailEndSnap);
        var ps = FindSnap(pillar.transform, PillarSnapName);
        var psNext = FindSnap(pillar.transform, "SnapPoint2");
        if (!rs || !re || !ps) return;

        // Rail to previous snap
        AlignRailToTarget(rail.transform, rs, -dir, target);

        // PillarM to rail end
        pillar.transform.rotation =
            YawDelta(ps.right, -dir) * pillar.transform.rotation;
        pillar.transform.position += re.position - ps.position;

        target = psNext; // next rail snaps to this pillar
    }
    
    // Update hover90Rail and hover90PillarM to point to the last elements
    if (hoverChainRails.Count > 0)
    {
        hover90Rail = hoverChainRails[^1];
        hover90PillarM = hoverChainPillarsM[^1];
    }
}

GameObject CommitHoverChainOnly()
{
    if (hoverChainRails.Count == 0)
    {
        ApplyFullDetailToCurrentBuild();
        return null;
    }

    var railPrefab = FindAsset<GameObject>(
        $"blstrs_{balusterStyleIndex + 1}{(selectedVariant == ContinueVariant.V2 ? "V2" : "V1")}_PREFAB"
    );
    var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));

    if (!railPrefab || !pillarMPrefab)
        return null;

    GameObject lastCommittedPillarM = null;

    // Commit Corner pillar (V1C/V1C45) if present (for 90°/45° turns)
    if (hover90EndPillarE)
    {
        var src = PrefabUtility.GetCorrespondingObjectFromSource(hover90EndPillarE);
        if (src != null)
        {
            var cornerPrefab = FindAsset<GameObject>(src.name);
            if (cornerPrefab)
            {
                var realCorner = (GameObject)PrefabUtility.InstantiatePrefab(cornerPrefab);
                ApplyCurrentTextureVariantToObject(realCorner);
                ApplyContinueTopToPillar(realCorner);
                Undo.RegisterCreatedObjectUndo(realCorner, "Place Corner Pillar");
                currentBuildObjects.Add(realCorner);

                realCorner.transform.SetPositionAndRotation(
                    hover90EndPillarE.transform.position,
                    hover90EndPillarE.transform.rotation
                );
                realCorner.transform.localScale = continueScale;
            }
        }
    }

    // Commit the entire chain (Rails + PillarsM)
    for (int i = 0; i < hoverChainRails.Count; i++)
    {
        // Commit Rail
        var realRail = (GameObject)PrefabUtility.InstantiatePrefab(railPrefab);
        ApplyCurrentTextureVariantToObject(realRail);
        Undo.RegisterCreatedObjectUndo(realRail, "Place Rail");
        currentBuildObjects.Add(realRail);
        ApplyRailVisualVariation(realRail);

        realRail.transform.SetPositionAndRotation(
            hoverChainRails[i].transform.position,
            hoverChainRails[i].transform.rotation
        );
        realRail.transform.localScale = continueScale;

        // Commit PillarM
        var realPillar = (GameObject)PrefabUtility.InstantiatePrefab(pillarMPrefab);
        ApplyCurrentTextureVariantToObject(realPillar);
        ApplyContinueTopToPillar(realPillar);
        Undo.RegisterCreatedObjectUndo(realPillar, "Place Pillar V1M");
        currentBuildObjects.Add(realPillar);
        ApplyPillarMVisualVariation(realPillar);

        realPillar.transform.SetPositionAndRotation(
            hoverChainPillarsM[i].transform.position,
            hoverChainPillarsM[i].transform.rotation
        );
        realPillar.transform.localScale = continueScale;

        lastCommittedPillarM = realPillar;
    }

    ApplyFullDetailToCurrentBuild();
    return lastCommittedPillarM;
}

void CommitHover90PreviewAndEnterContinueMode(Vector3 newActiveDir)
{
    if (!hover90Root)
        return;

    // Get prefabs
    var railPrefab = FindAsset<GameObject>(
        $"blstrs_{balusterStyleIndex + 1}{(selectedVariant == ContinueVariant.V2 ? "V2" : "V1")}_PREFAB"
    );
    var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));

    if (!railPrefab || !pillarMPrefab)
    {
        ClearHover90Preview();
        return;
    }

    // 1) FINALIZE: Convert hover90 ghost to REAL objects
    
    // If Corner pillar (V1C/V1C45) is present, remove previous V1M (it gets replaced)
    if (hover90EndPillarE && continueAnchorPillar)
    {
        // Temporarily disable delete protection
        bool prevSuppress = suppressDeleteUndo;
        suppressDeleteUndo = true;

        currentBuildObjects.Remove(continueAnchorPillar);
        protectedPillarIds.Remove(continueAnchorPillar.StableId());
        Undo.DestroyObjectImmediate(continueAnchorPillar);
        continueAnchorPillar = null;

        // Re-enable delete protection
        suppressDeleteUndo = prevSuppress;
        RebuildProtectedPillarIdCache();
    }
    // Straight: Show previous V1M again (was hidden in continue mode)
    else if (continueAnchorPillar)
    {
        foreach (var r in continueAnchorPillar.GetComponentsInChildren<Renderer>(true))
            r.enabled = true;
    }

    // Commit Corner pillar (V1C/V1C45) if present (for 90°/45° turns)
    if (hover90EndPillarE)
    {
        var src = PrefabUtility.GetCorrespondingObjectFromSource(hover90EndPillarE);
        if (src != null)
        {
            var cornerPrefab = FindAsset<GameObject>(src.name);
            if (cornerPrefab)
            {
                var realCorner = (GameObject)PrefabUtility.InstantiatePrefab(cornerPrefab);
                ApplyCurrentTextureVariantToObject(realCorner);
                ApplyContinueTopToPillar(realCorner);
                Undo.RegisterCreatedObjectUndo(realCorner, "Place Corner Pillar");
                currentBuildObjects.Add(realCorner);

                realCorner.transform.SetPositionAndRotation(
                    hover90EndPillarE.transform.position,
                    hover90EndPillarE.transform.rotation
                );
                realCorner.transform.localScale = continueScale;
            }
        }
    }

    // Commit the entire chain (Rails + PillarsM)
    GameObject lastCommittedPillarM = null;
    
    for (int i = 0; i < hoverChainRails.Count; i++)
    {
        // Commit Rail
        var realRail = (GameObject)PrefabUtility.InstantiatePrefab(railPrefab);
        ApplyCurrentTextureVariantToObject(realRail);
        Undo.RegisterCreatedObjectUndo(realRail, "Place Rail");
        currentBuildObjects.Add(realRail);
        ApplyRailVisualVariation(realRail);

        realRail.transform.SetPositionAndRotation(
            hoverChainRails[i].transform.position,
            hoverChainRails[i].transform.rotation
        );
        realRail.transform.localScale = continueScale;

        // Commit PillarM
        var realPillar = (GameObject)PrefabUtility.InstantiatePrefab(pillarMPrefab);
        ApplyCurrentTextureVariantToObject(realPillar);
        ApplyContinueTopToPillar(realPillar);
        Undo.RegisterCreatedObjectUndo(realPillar, "Place Pillar V1M");
        currentBuildObjects.Add(realPillar);
        ApplyPillarMVisualVariation(realPillar);

        realPillar.transform.SetPositionAndRotation(
            hoverChainPillarsM[i].transform.position,
            hoverChainPillarsM[i].transform.rotation
        );
        realPillar.transform.localScale = continueScale;
        
        lastCommittedPillarM = realPillar;
        lastPlacedPillarM = realPillar;
    }

    // Clear the hover preview (destroy ghost objects)
    ShowLastGhostPillarM();
    ShowCurvedGhostEndPillar();
    if (hover90Root)
        DestroyImmediate(hover90Root);
    hover90Root = null;
    hover90EndPillarE = null;
    hover90Rail = null;
    hover90PillarM = null;
    hoverChainRails.Clear();
    hoverChainPillarsM.Clear();
    hoverChainStartSnap = null;

    // 2) Set up Continue Build Mode (same pattern as RailPreview -> CornerSelect)
    if (lastCommittedPillarM)
    {
        continueAnchorPillar = lastCommittedPillarM;
        continueAnchorActive = true;

        // Hide the anchor pillar
        foreach (var r in continueAnchorPillar.GetComponentsInChildren<Renderer>(true))
            r.enabled = false;

        // Ensure ghost material exists
        if (!ghostMat)
            ghostMat = FindAsset<Material>(GhostMatName);

        // Find the connected snap (snap1 is connected to the rail)
        var snap1 = FindSnap(continueAnchorPillar.transform, "SnapPoint1");

        // Setup snap proxy
        if (continueSnapProxy)
            DestroyImmediate(continueSnapProxy.gameObject);

        var proxyGO = new GameObject("ContinueSnapProxy");
        proxyGO.hideFlags = HideFlags.HideAndDontSave;
        continueSnapProxy = proxyGO.transform;
        continueSnapProxy.SetPositionAndRotation(snap1.position, snap1.rotation);

        lastPillarSnap = continueSnapProxy;
        
        // Set activeDir to the new direction
        activeDir = newActiveDir.normalized;
        activeDir.y = 0f;
        if (activeDir.sqrMagnitude > 1e-6f) activeDir.Normalize();

        // Create Ghost V1M
        if (continueGhostPillarM)
            DestroyImmediate(continueGhostPillarM);

        if (pillarMPrefab)
        {
            continueGhostPillarM = CreateGhost(pillarMPrefab);
            continueGhostPillarM.name = "ContinueGhostV1M";

            var ghostSnap = FindSnap(continueGhostPillarM.transform, "SnapPoint1");
            if (ghostSnap && lastPillarSnap)
            {
                // Ghost SnapPoint1.right must point INTO the snap (opposite to activeDir)
                continueGhostPillarM.transform.rotation =
                    YawDelta(ghostSnap.right, -activeDir) * continueGhostPillarM.transform.rotation;

                continueGhostPillarM.transform.position +=
                    lastPillarSnap.position - ghostSnap.position;
            }

            AddGhostTopToPillar(continueGhostPillarM);
        }
    }

    ApplyFullDetailToCurrentBuild();
}

void CommitHover90CurvedPreviewAndEnterContinueMode(Vector3 newActiveDir)
{
    if (!hover90Root || !hover90Rail || !hover90PillarM)
        return;

    // Get prefabs
    var curvedPrefab = FindAsset<GameObject>(
        $"blstrsCrvd_{balusterStyleIndex + 1}{(selectedVariant == ContinueVariant.V2 ? "V2" : "V1")}_PREFAB"
    );
    var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));

    if (!curvedPrefab || !pillarMPrefab)
    {
        ClearHover90Preview();
        return;
    }

    // --- CONTINUE ANCHOR CLEANUP ---
    // When inner arc is the FIRST commit from Continue Anchor mode,
    // the anchor (V1E from rail-delete repair or start pillar) must be
    // upgraded to V1M at the ghost V1M position. Otherwise V1E stays
    // stranded and the curved rail ends up detached from it.
    if (continueAnchorActive && continueAnchorPillar && continueGhostPillarM)
    {
        bool prevSuppress = suppressDeleteUndo;
        suppressDeleteUndo = true;

        // 1) Place real V1M at ghost position (upgrade V1E -> V1M)
        var upgradedM = (GameObject)PrefabUtility.InstantiatePrefab(pillarMPrefab);
        ApplyCurrentTextureVariantToObject(upgradedM);
        ApplyContinueTopToPillar(upgradedM);
        ApplyPillarMVisualVariation(upgradedM);
        Undo.RegisterCreatedObjectUndo(upgradedM, "Upgrade Anchor to V1M");
        currentBuildObjects.Add(upgradedM);

        upgradedM.transform.SetPositionAndRotation(
            continueGhostPillarM.transform.position,
            continueGhostPillarM.transform.rotation
        );
        upgradedM.transform.localScale = continueScale;

        // 2) Destroy ghost V1M + snap proxy
        DestroyImmediate(continueGhostPillarM);
        continueGhostPillarM = null;

        if (continueSnapProxy)
        {
            DestroyImmediate(continueSnapProxy.gameObject);
            continueSnapProxy = null;
        }

        // 3) Destroy old V1E anchor
        protectedPillarIds.Remove(continueAnchorPillar.StableId());
        Undo.DestroyObjectImmediate(continueAnchorPillar);
        continueAnchorPillar = null;
        continueAnchorActive = false;

        suppressDeleteUndo = prevSuppress;
        RebuildProtectedPillarIdCache();
    }
    else if (continueAnchorPillar)
    {
        // Not first continue commit — just show anchor renderers again
        foreach (var r in continueAnchorPillar.GetComponentsInChildren<Renderer>(true))
            r.enabled = true;
    }

    // 1) FINALIZE: Commit Curved Rail
    var realCurvedRail = (GameObject)PrefabUtility.InstantiatePrefab(curvedPrefab);
    ApplyCurrentTextureVariantToObject(realCurvedRail);
    Undo.RegisterCreatedObjectUndo(realCurvedRail, "Place Curved Rail");
    currentBuildObjects.Add(realCurvedRail);
    ApplyRailVisualVariation(realCurvedRail);
    ApplyCurvedRailVisualVariation(realCurvedRail);

    realCurvedRail.transform.SetPositionAndRotation(
        hover90Rail.transform.position,
        hover90Rail.transform.rotation
    );
    realCurvedRail.transform.localScale = continueScale;

    // 2) FINALIZE: Commit PillarM
    var realPillar = (GameObject)PrefabUtility.InstantiatePrefab(pillarMPrefab);
    ApplyCurrentTextureVariantToObject(realPillar);
    ApplyContinueTopToPillar(realPillar);
    Undo.RegisterCreatedObjectUndo(realPillar, "Place Pillar V1M");
    currentBuildObjects.Add(realPillar);
    ApplyPillarMVisualVariation(realPillar);

    realPillar.transform.SetPositionAndRotation(
        hover90PillarM.transform.position,
        hover90PillarM.transform.rotation
    );
    realPillar.transform.localScale = continueScale;

    lastPlacedPillarM = realPillar;

    // Clear the hover preview
    ShowLastGhostPillarM();
    ShowCurvedGhostEndPillar();
    if (hover90Root)
        DestroyImmediate(hover90Root);
    hover90Root = null;
    hover90EndPillarE = null;
    hover90Rail = null;
    hover90PillarM = null;

    // 3) Set up Continue Build Mode
    continueAnchorPillar = realPillar;
    continueAnchorActive = true;

    foreach (var r in continueAnchorPillar.GetComponentsInChildren<Renderer>(true))
        r.enabled = false;

    if (!ghostMat)
        ghostMat = FindAsset<Material>(GhostMatName);

    var snap1 = FindSnap(continueAnchorPillar.transform, "SnapPoint1");

    if (continueSnapProxy)
        DestroyImmediate(continueSnapProxy.gameObject);

    var proxyGO = new GameObject("ContinueSnapProxy");
    proxyGO.hideFlags = HideFlags.HideAndDontSave;
    continueSnapProxy = proxyGO.transform;
    continueSnapProxy.SetPositionAndRotation(snap1.position, snap1.rotation);

    lastPillarSnap = continueSnapProxy;

    activeDir = newActiveDir.normalized;
    activeDir.y = 0f;
    if (activeDir.sqrMagnitude > 1e-6f) activeDir.Normalize();

    if (continueGhostPillarM)
        DestroyImmediate(continueGhostPillarM);

    continueGhostPillarM = CreateGhost(pillarMPrefab);
    continueGhostPillarM.name = "ContinueGhostV1M";

    var ghostSnap = FindSnap(continueGhostPillarM.transform, "SnapPoint1");
    if (ghostSnap && lastPillarSnap)
    {
        continueGhostPillarM.transform.rotation =
            YawDelta(ghostSnap.right, -activeDir) * continueGhostPillarM.transform.rotation;
        continueGhostPillarM.transform.position +=
            lastPillarSnap.position - ghostSnap.position;
    }

    AddGhostTopToPillar(continueGhostPillarM);

    ApplyFullDetailToCurrentBuild();
}

void CommitHover90OuterArcPreviewAndEnterContinueMode(Vector3 newActiveDir)
{
    if (!hover90Root || !hover90Rail || !hover90PillarM || !hover90EndPillarE)
        return;

    // Get prefabs
    var curvedPrefab = FindAsset<GameObject>(
        $"blstrsCrvd_{balusterStyleIndex + 1}{(selectedVariant == ContinueVariant.V2 ? "V2" : "V1")}_PREFAB"
    );
    var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));

    if (!curvedPrefab || !pillarMPrefab)
    {
        ClearHover90Preview();
        return;
    }

    // OUTER ARC: Remove previous V1M (replaced by V1C)
    // Temporarily disable delete protection
    bool prevSuppress = suppressDeleteUndo;
    suppressDeleteUndo = true;

    // --- CONTINUE ANCHOR CLEANUP ---
    // When outer arc is the FIRST commit from Continue Anchor mode,
    // continueAnchorPillar (hidden V1E) still exists and must be removed.
    // The V1C corner replaces it in the chain.
    if (continueAnchorActive)
    {
        if (continueGhostPillarM)
            DestroyImmediate(continueGhostPillarM);
        continueGhostPillarM = null;

        if (continueSnapProxy)
            DestroyImmediate(continueSnapProxy.gameObject);
        continueSnapProxy = null;

        if (continueAnchorPillar)
        {
            protectedPillarIds.Remove(continueAnchorPillar.StableId());
            Undo.DestroyObjectImmediate(continueAnchorPillar);
        }
        continueAnchorPillar = null;
        continueAnchorActive = false;
    }

    if (lastPlacedPillarM)
    {
        currentBuildObjects.Remove(lastPlacedPillarM);
        Undo.DestroyObjectImmediate(lastPlacedPillarM);
        lastPlacedPillarM = null;
    }

    // Re-enable delete protection
    suppressDeleteUndo = prevSuppress;
    RebuildProtectedPillarIdCache();

    // 1) FINALIZE: Commit Corner Pillar (V1C)
    var cornerSrc = PrefabUtility.GetCorrespondingObjectFromSource(hover90EndPillarE);
    if (cornerSrc != null)
    {
        var cornerPrefab = FindAsset<GameObject>(cornerSrc.name);
        if (cornerPrefab)
        {
            var realCorner = (GameObject)PrefabUtility.InstantiatePrefab(cornerPrefab);
            ApplyCurrentTextureVariantToObject(realCorner);
            ApplyContinueTopToPillar(realCorner);
            Undo.RegisterCreatedObjectUndo(realCorner, "Place Corner Pillar V1C");
            currentBuildObjects.Add(realCorner);

            realCorner.transform.SetPositionAndRotation(
                hover90EndPillarE.transform.position,
                hover90EndPillarE.transform.rotation
            );
            realCorner.transform.localScale = continueScale;
        }
    }

    // 2) FINALIZE: Commit Curved Rail
    var realCurvedRail = (GameObject)PrefabUtility.InstantiatePrefab(curvedPrefab);
    ApplyCurrentTextureVariantToObject(realCurvedRail);
    Undo.RegisterCreatedObjectUndo(realCurvedRail, "Place Curved Rail");
    currentBuildObjects.Add(realCurvedRail);
    ApplyRailVisualVariation(realCurvedRail);
    ApplyCurvedRailVisualVariation(realCurvedRail);

    realCurvedRail.transform.SetPositionAndRotation(
        hover90Rail.transform.position,
        hover90Rail.transform.rotation
    );
    realCurvedRail.transform.localScale = continueScale;

    // 3) FINALIZE: Commit end pillar
    // Detect existing pillar at the curved rail's far end by matching snap positions.
    // The curved rail's far snap is where the end pillar should connect. Any existing
    // pillar with a free snap at that position is a repair leftover (e.g. V1E that
    // replaced an original V1C45) and should be upgraded to V1C45 to restore the angle.
    GameObject existingAtEnd = null;
    Transform existingFreeSnap = null;

    // Find the curved rail's free snap (the one NOT at the corner pillar position)
    var crvS1 = FindSnap(realCurvedRail.transform, "SnapPoint1");
    var crvS2 = FindSnap(realCurvedRail.transform, "SnapPoint2");
    Vector3 cornerPos = hover90EndPillarE.transform.position;
    Transform curvedFarSnap = null;
    if (crvS1 && crvS2)
        curvedFarSnap = Vector3.Distance(crvS1.position, cornerPos) >
                        Vector3.Distance(crvS2.position, cornerPos) ? crvS1 : crvS2;

    var rootForSearch = continueTargetBalustrade;
    if (rootForSearch && curvedFarSnap)
    {
        float tol = 0.15f * continueScale.x;
        foreach (Transform t in rootForSearch.GetComponentsInChildren<Transform>(true))
        {
            if (!t || t == rootForSearch.transform) continue;
            if (!IsPillarInstance(t.gameObject)) continue;

            var ps1 = FindSnap(t, "SnapPoint1");
            var ps2 = FindSnap(t, "SnapPoint2");
            bool match =
                (ps1 && Vector3.Distance(ps1.position, curvedFarSnap.position) < tol) ||
                (ps2 && Vector3.Distance(ps2.position, curvedFarSnap.position) < tol);
            if (match)
            {
                existingAtEnd = t.gameObject;
                existingFreeSnap =
                    (ps1 && Vector3.Distance(ps1.position, curvedFarSnap.position) < tol) ? ps1 : ps2;
                break;
            }
        }
    }

    GameObject realPillar;
    if (existingAtEnd && existingFreeSnap)
    {
        // Replace existing pillar with V1C45, aligned via snap match
        var c45Prefab = FindAsset<GameObject>(GetPillarPrefabName('4'));
        if (c45Prefab)
        {
            Transform parent = existingAtEnd.transform.parent;
            Quaternion existingRot = existingAtEnd.transform.rotation;
            Vector3 existingSnapPos = existingFreeSnap.position;
            Vector3 existingSnapRight = existingFreeSnap.right;

            bool prevSup = suppressDeleteUndo;
            suppressDeleteUndo = true;
            protectedPillarIds.Remove(existingAtEnd.StableId());
            Undo.DestroyObjectImmediate(existingAtEnd);
            suppressDeleteUndo = prevSup;
            RebuildProtectedPillarIdCache();

            realPillar = (GameObject)PrefabUtility.InstantiatePrefab(c45Prefab);
            ApplyCurrentTextureVariantToObject(realPillar);
            ApplyContinueTopToPillar(realPillar);
            Undo.RegisterCreatedObjectUndo(realPillar, "Place Pillar V1C45");
            currentBuildObjects.Add(realPillar);
            realPillar.transform.localScale = continueScale;

            // Align V1C45's SnapPoint1 (or 2, whichever matches direction) to the curved rail's far snap
            var np1 = FindSnap(realPillar.transform, "SnapPoint1");
            var np2 = FindSnap(realPillar.transform, "SnapPoint2");
            if (np1 && np2)
            {
                // Use SnapPoint1 as input, facing INTO the curved rail's far snap direction
                Vector3 inDir = -curvedFarSnap.right;
                realPillar.transform.rotation =
                    YawDelta(np1.right, inDir) * realPillar.transform.rotation;
                realPillar.transform.position += curvedFarSnap.position - np1.position;
            }
            else
            {
                realPillar.transform.SetPositionAndRotation(existingSnapPos, existingRot);
            }

            if (parent) realPillar.transform.SetParent(parent, true);
        }
        else
        {
            realPillar = (GameObject)PrefabUtility.InstantiatePrefab(pillarMPrefab);
            ApplyCurrentTextureVariantToObject(realPillar);
            ApplyContinueTopToPillar(realPillar);
            Undo.RegisterCreatedObjectUndo(realPillar, "Place Pillar V1M");
            currentBuildObjects.Add(realPillar);
            ApplyPillarMVisualVariation(realPillar);
            realPillar.transform.SetPositionAndRotation(
                hover90PillarM.transform.position,
                hover90PillarM.transform.rotation);
            realPillar.transform.localScale = continueScale;
        }
    }
    else
    {
        realPillar = (GameObject)PrefabUtility.InstantiatePrefab(pillarMPrefab);
        ApplyCurrentTextureVariantToObject(realPillar);
        ApplyContinueTopToPillar(realPillar);
        Undo.RegisterCreatedObjectUndo(realPillar, "Place Pillar V1M");
        currentBuildObjects.Add(realPillar);
        ApplyPillarMVisualVariation(realPillar);

        realPillar.transform.SetPositionAndRotation(
            hover90PillarM.transform.position,
            hover90PillarM.transform.rotation
        );
        realPillar.transform.localScale = continueScale;
    }

    lastPlacedPillarM = realPillar;

    // Clear the hover preview
    ShowLastGhostPillarM();
    ShowCurvedGhostEndPillar();
    if (hover90Root)
        DestroyImmediate(hover90Root);
    hover90Root = null;
    hover90EndPillarE = null;
    hover90Rail = null;
    hover90PillarM = null;

    // 4) Set up Continue Build Mode
    continueAnchorPillar = realPillar;
    continueAnchorActive = true;

    foreach (var r in continueAnchorPillar.GetComponentsInChildren<Renderer>(true))
        r.enabled = false;

    if (!ghostMat)
        ghostMat = FindAsset<Material>(GhostMatName);

    var snap1 = FindSnap(continueAnchorPillar.transform, "SnapPoint1");

    if (continueSnapProxy)
        DestroyImmediate(continueSnapProxy.gameObject);

    var proxyGO = new GameObject("ContinueSnapProxy");
    proxyGO.hideFlags = HideFlags.HideAndDontSave;
    continueSnapProxy = proxyGO.transform;
    continueSnapProxy.SetPositionAndRotation(snap1.position, snap1.rotation);

    lastPillarSnap = continueSnapProxy;

    activeDir = newActiveDir.normalized;
    activeDir.y = 0f;
    if (activeDir.sqrMagnitude > 1e-6f) activeDir.Normalize();

    if (continueGhostPillarM)
        DestroyImmediate(continueGhostPillarM);

    continueGhostPillarM = CreateGhost(pillarMPrefab);
    continueGhostPillarM.name = "ContinueGhostV1M";

    var ghostSnap = FindSnap(continueGhostPillarM.transform, "SnapPoint1");
    if (ghostSnap && lastPillarSnap)
    {
        continueGhostPillarM.transform.rotation =
            YawDelta(ghostSnap.right, -activeDir) * continueGhostPillarM.transform.rotation;
        continueGhostPillarM.transform.position +=
            lastPillarSnap.position - ghostSnap.position;
    }

    AddGhostTopToPillar(continueGhostPillarM);

    ApplyFullDetailToCurrentBuild();
}

GameObject CreateGhost(GameObject prefab, Transform parent = null)
{
    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    go.hideFlags = HideFlags.HideAndDontSave;

    // Inherit scale from target balustrade in continue mode
    if (continueScale != Vector3.one)
        go.transform.localScale = continueScale;

    if (parent)
        go.transform.SetParent(parent, true);

    // Remove colliders
    foreach (var c in go.GetComponentsInChildren<Collider>(true))
        DestroyImmediate(c);

    // Force LOD0 only: remove LODGroup and destroy LOD1+ children
    var lodGroup = go.GetComponent<LODGroup>();
    if (lodGroup) DestroyImmediate(lodGroup);

    foreach (Transform child in go.transform)
    {
        // Also check nested children (e.g. rails with sub-objects)
        var nestedLodGroup = child.GetComponent<LODGroup>();
        if (nestedLodGroup) DestroyImmediate(nestedLodGroup);
    }

    // Destroy all LOD1+ meshes, keep only LOD0
    var lodChildren = new List<GameObject>();
    foreach (var t in go.GetComponentsInChildren<Transform>(true))
    {
        if (t == go.transform) continue;
        string n = t.name;
        if (n.StartsWith("LOD") && n != "LOD0")
            lodChildren.Add(t.gameObject);
    }
    foreach (var lod in lodChildren)
        DestroyImmediate(lod);

    // Force ghost material ONLY
    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
    {
        var mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
            mats[i] = ghostMat;
        r.sharedMaterials = mats;
    }

    return go;
}

void AddGhostTopToPillar(GameObject ghostPillar)
{
    if (!ghostPillar)
        return;

    // Only add tops if continueTopIndex is valid
    if (continueTopIndex < 0 || continueTopIndex >= TopPrefabNames.Length)
        return;

    var snapTop = FindSnap(ghostPillar.transform, TopSnapName);
    if (!snapTop)
        return;

    var topPrefab = FindAsset<GameObject>(TopPrefabNames[continueTopIndex]);
    if (!topPrefab)
        return;

    // Create ghost top
    var ghostTop = (GameObject)PrefabUtility.InstantiatePrefab(topPrefab);
    ghostTop.hideFlags = HideFlags.HideAndDontSave;

    // Remove colliders
    foreach (var c in ghostTop.GetComponentsInChildren<Collider>(true))
        DestroyImmediate(c);

    // Force LOD0 only
    var topLodGroup = ghostTop.GetComponent<LODGroup>();
    if (topLodGroup) DestroyImmediate(topLodGroup);
    var topLodChildren = new List<GameObject>();
    foreach (var t in ghostTop.GetComponentsInChildren<Transform>(true))
    {
        if (t == ghostTop.transform) continue;
        string n = t.name;
        if (n.StartsWith("LOD") && n != "LOD0")
            topLodChildren.Add(t.gameObject);
    }
    foreach (var lod in topLodChildren)
        DestroyImmediate(lod);

    // Apply ghost material
    foreach (var r in ghostTop.GetComponentsInChildren<Renderer>(true))
    {
        var mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
            mats[i] = ghostMat;
        r.sharedMaterials = mats;
    }

    // Parent to pillar and position at SnapPointTop
    ghostTop.transform.SetParent(ghostPillar.transform, false);
    ghostTop.transform.position = snapTop.position;
    ghostTop.transform.rotation = snapTop.rotation;
}

    static void ApplyGhostTint(GameObject ghostObj, Color color)
    {
        if (!ghostObj) return;

        // Lazy-init: MaterialPropertyBlock cannot be created in static field initializers
        if (ghostPropBlock == null) ghostPropBlock = new MaterialPropertyBlock();

        // Set all color properties to cover HDRP, URP, and Built-in shaders
        ghostPropBlock.SetColor(PropBaseColor, color);   // HDRP Lit + URP Lit/Unlit
        ghostPropBlock.SetColor(PropUnlitColor, color);  // HDRP Unlit
        ghostPropBlock.SetColor(PropColor, color);       // Built-in fallback

        foreach (var r in ghostObj.GetComponentsInChildren<Renderer>(true))
            r.SetPropertyBlock(ghostPropBlock);
    }

    static void ClearGhostTint(GameObject ghostObj)
    {
        if (!ghostObj) return;

        foreach (var r in ghostObj.GetComponentsInChildren<Renderer>(true))
            r.SetPropertyBlock(null);
    }

// -- Close Loop Detection & Try-Fit --

void UpdateCloseLoopDetection()
{
    GameObject lastGhost = null;
    if (state == State.DirectionSelect && dirSelectHoverPillars.Count > 0)
        lastGhost = dirSelectHoverPillars[^1];
    else if (hoverChainPillarsM.Count > 0)
        lastGhost = hoverChainPillarsM[^1];
    else if (hover90PillarM)
        lastGhost = hover90PillarM;

    if (!lastGhost) { if (closeLoopDetected) ClearCloseLoopFull(); return; }

    // Ghost's chain-side snap (Snap1) = where it connects to the chain
    var ghostSnap1 = FindSnap(lastGhost.transform, PillarSnapName);
    // Ghost's free-side snap (Snap2) = the end approaching the target
    var ghostSnap2 = FindSnap(lastGhost.transform, "SnapPoint2");
    if (!ghostSnap1 || !ghostSnap2) { if (closeLoopDetected) ClearCloseLoopFull(); return; }

    Vector3 freePos = ghostSnap2.position;

    // Find nearest real pillar within proximity of the ghost's free end
    GameObject found = null;
    float bestDist = float.MaxValue;
    string[] pillarSnaps = { "SnapPoint1", "SnapPoint2", "SnapPoint3" };

    System.Action<GameObject> testCandidate = (go) =>
    {
        if (!go || go == continueAnchorPillar || go == lastPlacedPillarM) return;
        if ((go.hideFlags & HideFlags.HideAndDontSave) != 0) return;
        var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
        if (!src || !src.name.StartsWith("pillar_") || !src.name.EndsWith("_PREFAB")) return;
        if (src.name.Contains("T_PREFAB")) return; // V1T/V2T fully occupied
        if (src.name.Contains("C45_PREFAB")) return; // V1C45 geometry incompatible with close-loop
        foreach (var sn in pillarSnaps)
        {
            var snap = FindSnap(go.transform, sn);
            if (!snap) continue;
            float d = Vector3.Distance(snap.position, freePos);
            float thresh = 0.35f * Mathf.Max(continueScale.x, 1f);
            if (d < thresh && d < bestDist) { bestDist = d; found = go; }
        }
    };

    foreach (var root in finalizedBalustrades)
    {
        if (!root) continue;
        foreach (Transform child in root.transform)
            testCandidate(child ? child.gameObject : null);
    }
    foreach (var go in currentBuildObjects)
        testCandidate(go);

    if (found != null)
    {
        // Collect all real rail snap positions
        var allRailSnaps = CollectAllRailSnapPositions();

        // Try-fit: place each candidate at the ghost's position, check free snap vs real rails
        char repType = TryFitCloseLoop(ghostSnap1, allRailSnaps, found);

        if (repType == '\0')
        {
            if (closeLoopDetected) ClearCloseLoopFull();
        }
        else if (!closeLoopDetected || closeLoopTargetPillar != found || closeLoopReplacementType != repType)
        {
            ClearCloseLoop();
            closeLoopTargetPillar = found;
            closeLoopDetected = true;
            closeLoopReplacementType = repType;
            closeLoopApproachDir = ghostSnap1.right;
            closeLoopApproachDir.y = 0f;
            if (closeLoopApproachDir.sqrMagnitude > 1e-6f) closeLoopApproachDir.Normalize();

            foreach (var r in found.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;
            TintAllActiveGhosts(GhostCloseLoopCol);
            UpdateCloseLoopGhostSwap(lastGhost, repType);
        }
        else
        {
            TintAllActiveGhosts(GhostCloseLoopCol);
            UpdateCloseLoopGhostSwap(lastGhost, repType);
        }
    }
    else if (closeLoopDetected)
    {
        ClearCloseLoopFull();
    }
}

List<Vector3> CollectAllRailSnapPositions()
{
    var positions = new List<Vector3>(64);
    System.Action<GameObject> collect = (go) =>
    {
        if (!go || (go.hideFlags & HideFlags.HideAndDontSave) != 0) return;
        // Hidden rails ARE included: their snaps are needed by TryFitCloseLoop
        // when the user rebuilds across a deleted curved/45 gap.
        var s = PrefabUtility.GetCorrespondingObjectFromSource(go);
        if (!s) return;
        string n = s.name;
        if (!(n.StartsWith("blstrs_") || n.StartsWith("blstrsCrvd_")) || !n.EndsWith("_PREFAB")) return;
        var rs = FindSnap(go.transform, RailStartSnap);
        var re = FindSnap(go.transform, RailEndSnap);
        if (rs) positions.Add(rs.position);
        if (re) positions.Add(re.position);
    };
    foreach (var root in finalizedBalustrades)
    {
        if (!root) continue;
        // Recursive iteration: hidden rails live under a __Hidden container,
        // not directly under root.
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t && t != root.transform) collect(t.gameObject);
    }
    foreach (var go in currentBuildObjects)
        collect(go);
    return positions;
}

// Place each candidate type at the ghost's position (chain-aligned via ghostSnap1),
// then check if any FREE snap touches a real rail snap.
char TryFitCloseLoop(Transform ghostSnap1, List<Vector3> allRailSnaps, GameObject targetPillar)
{
    Vector3 chainDir = ghostSnap1.right; chainDir.y = 0f;
    if (chainDir.sqrMagnitude < 1e-6f) return '\0';
    chainDir.Normalize();
    Vector3 chainPos = ghostSnap1.position;

    // Count occupied snaps on target to determine valid candidates
    var occupiedDirs = new List<Vector3>(4);
    GatherOccupiedSnapDirs(targetPillar, occupiedDirs);
    int occupiedCount = occupiedDirs.Count;

    // 1 occupied + 1 new = 2 total → M/C/C45
    // 2 occupied + 1 new = 3 total → T only
    char[] candidates;
    if (occupiedCount <= 1) candidates = new[] { 'M', 'C', '4' };
    else if (occupiedCount == 2) candidates = new[] { 'T' };
    else return '\0';

    float matchTol = 0.1f * Mathf.Max(continueScale.x, 1f);

    char bestType = '\0';
    int bestScore = 0;

    foreach (char type in candidates)
    {
        var prefab = FindAsset<GameObject>(GetPillarPrefabName(type));
        if (!prefab) continue;

        var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        temp.hideFlags = HideFlags.HideAndDontSave;
        temp.transform.localScale = continueScale;
        Quaternion origRot = temp.transform.rotation;

        // T1/T2 are only real connection snaps on V1T; on M/C/C45 they're build-mode helpers
        string[] sNames = type == 'T'
            ? new[] { "SnapPoint1", "SnapPoint2", "SnapPoint3" }
            : new[] { "SnapPoint1", "SnapPoint2" };

        var snaps = new List<Transform>(4);
        foreach (var sn in sNames)
        {
            var s = FindSnap(temp.transform, sn);
            if (s) snaps.Add(s);
        }

        int typeScore = 0;
        Quaternion bestRot = origRot;
        Vector3 bestPos = Vector3.zero;

        for (int si = 0; si < snaps.Count; si++)
        {
            // Align snap[si].right → chainDir, position snap[si] at chainPos
            temp.transform.SetPositionAndRotation(Vector3.zero, origRot);
            Vector3 sd = snaps[si].right; sd.y = 0f;
            if (sd.sqrMagnitude < 1e-6f) continue;
            sd.Normalize();
            temp.transform.rotation = YawDelta(sd, chainDir) * origRot;
            temp.transform.position = chainPos - snaps[si].position;

            // Count how many OTHER snaps touch a real rail snap
            int matchCount = 0;
            for (int ci = 0; ci < snaps.Count; ci++)
            {
                if (ci == si) continue;
                foreach (var rp in allRailSnaps)
                {
                    if (Vector3.Distance(snaps[ci].position, rp) < matchTol)
                    { matchCount++; break; }
                }
            }

            if (matchCount > typeScore)
            {
                typeScore = matchCount;
                bestRot = temp.transform.rotation;
                bestPos = temp.transform.position;
            }
        }

        DestroyImmediate(temp);

        if (typeScore > bestScore)
        {
            bestScore = typeScore;
            bestType = type;
            closeLoopFitRotation = bestRot;
            closeLoopFitPosition = bestPos;
        }
    }

    return bestScore > 0 ? bestType : '\0';
}

// Collect world-space direction vectors for each occupied snap on a pillar.
// Only counts real rail connections (skips ghost objects).
void GatherOccupiedSnapDirs(GameObject pillar, List<Vector3> outDirs)
{
    // Collect real rail snap positions first
    var railSnapPositions = new List<Vector3>(64);

    System.Action<GameObject> collectRailSnaps = (go) =>
    {
        if (!go || (go.hideFlags & HideFlags.HideAndDontSave) != 0) return;
        // Hidden rails represent the gap to be repaired. They must NOT count
        // as occupying snaps on hidden pillars, otherwise the close-loop
        // detection sees both snaps as occupied and degrades to T-only.
        if (IsHiddenDeletedRail(go)) return;
        var s = PrefabUtility.GetCorrespondingObjectFromSource(go);
        if (!s) return;
        string n = s.name;
        bool isRail = (n.StartsWith("blstrs_") || n.StartsWith("blstrsCrvd_")) && n.EndsWith("_PREFAB");
        if (!isRail) return;

        var rs1 = FindSnap(go.transform, RailStartSnap);
        var rs2 = FindSnap(go.transform, RailEndSnap);
        if (rs1) railSnapPositions.Add(rs1.position);
        if (rs2) railSnapPositions.Add(rs2.position);
    };

    foreach (var root in finalizedBalustrades)
    {
        if (!root) continue;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t && t != root.transform) collectRailSnaps(t.gameObject);
    }
    foreach (var go in currentBuildObjects)
        collectRailSnaps(go);

    // Check each pillar snap against rail positions
    string[] snapNames = { "SnapPoint1", "SnapPoint2", "SnapPoint3" };
    float threshold = 0.05f * Mathf.Max(continueScale.x, 1f);

    foreach (var sn in snapNames)
    {
        var snap = FindSnap(pillar.transform, sn);
        if (!snap) continue;

        bool occupied = false;
        foreach (var rp in railSnapPositions)
        {
            if (Vector3.Distance(rp, snap.position) < threshold)
            {
                occupied = true;
                break;
            }
        }

        if (occupied)
        {
            Vector3 dir = snap.right;
            dir.y = 0;
            if (dir.sqrMagnitude > 1e-6f)
                outDirs.Add(dir.normalized);
        }
    }
}

void TintAllActiveGhosts(Color col)
{
    // Hover chains (CornerSelect)
    foreach (var go in hoverChainRails) ApplyGhostTint(go, col);
    foreach (var go in hoverChainPillarsM) ApplyGhostTint(go, col);
    if (hover90EndPillarE) ApplyGhostTint(hover90EndPillarE, col);
    if (hover90Rail && !hoverChainRails.Contains(hover90Rail)) ApplyGhostTint(hover90Rail, col);
    if (hover90PillarM && !hoverChainPillarsM.Contains(hover90PillarM)) ApplyGhostTint(hover90PillarM, col);

    // Direction select chains
    foreach (var go in dirSelectHoverRails) ApplyGhostTint(go, col);
    foreach (var go in dirSelectHoverPillars) ApplyGhostTint(go, col);

    // Continue ghost
    if (continueGhostPillarM) ApplyGhostTint(continueGhostPillarM, col);

    // Close loop replacement ghost
    if (closeLoopReplacementGhost) ApplyGhostTint(closeLoopReplacementGhost, col);
}

void ClearTintAllActiveGhosts()
{
    foreach (var go in hoverChainRails) ClearGhostTint(go);
    foreach (var go in hoverChainPillarsM) ClearGhostTint(go);
    if (hover90EndPillarE) ClearGhostTint(hover90EndPillarE);
    if (hover90Rail) ClearGhostTint(hover90Rail);
    if (hover90PillarM) ClearGhostTint(hover90PillarM);
    foreach (var go in dirSelectHoverRails) ClearGhostTint(go);
    foreach (var go in dirSelectHoverPillars) ClearGhostTint(go);
    if (continueGhostPillarM) ClearGhostTint(continueGhostPillarM);
    if (closeLoopReplacementGhost) ClearGhostTint(closeLoopReplacementGhost);
}

// Commit the ghost hover chain for a close-loop finalize.
// All intermediate pillars = V1M, last pillar = replacement type.
// Deletes the hidden target pillar and clears close-loop state.
GameObject CommitCloseLoopChain()
{
    if (!closeLoopDetected || closeLoopReplacementType == '\0')
        return null;

    var railPrefab = FindAsset<GameObject>(
        $"blstrs_{balusterStyleIndex + 1}{(selectedVariant == ContinueVariant.V2 ? "V2" : "V1")}_PREFAB"
    );
    var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));
    var repPrefab = FindAsset<GameObject>(GetPillarPrefabName(closeLoopReplacementType));
    if (!railPrefab || !pillarMPrefab || !repPrefab) return null;

    // Commit corner pillar if present
    if (hover90EndPillarE)
    {
        var src = PrefabUtility.GetCorrespondingObjectFromSource(hover90EndPillarE);
        if (src != null)
        {
            var cornerPrefab = FindAsset<GameObject>(src.name);
            if (cornerPrefab)
            {
                var realCorner = (GameObject)PrefabUtility.InstantiatePrefab(cornerPrefab);
                ApplyCurrentTextureVariantToObject(realCorner);
                ApplyContinueTopToPillar(realCorner);
                Undo.RegisterCreatedObjectUndo(realCorner, "Place Corner Pillar");
                currentBuildObjects.Add(realCorner);
                realCorner.transform.SetPositionAndRotation(
                    hover90EndPillarE.transform.position,
                    hover90EndPillarE.transform.rotation);
                realCorner.transform.localScale = continueScale;
            }
        }
    }

    GameObject lastPillar = null;
    int lastIdx = hoverChainRails.Count - 1;

    for (int i = 0; i < hoverChainRails.Count; i++)
    {
        // Commit rail
        var realRail = (GameObject)PrefabUtility.InstantiatePrefab(railPrefab);
        ApplyCurrentTextureVariantToObject(realRail);
        Undo.RegisterCreatedObjectUndo(realRail, "Place Rail");
        currentBuildObjects.Add(realRail);
        ApplyRailVisualVariation(realRail);
        realRail.transform.SetPositionAndRotation(
            hoverChainRails[i].transform.position,
            hoverChainRails[i].transform.rotation);
        realRail.transform.localScale = continueScale;

        if (i < lastIdx)
        {
            // Intermediate pillar → V1M
            var realPillar = (GameObject)PrefabUtility.InstantiatePrefab(pillarMPrefab);
            ApplyCurrentTextureVariantToObject(realPillar);
            ApplyContinueTopToPillar(realPillar);
            Undo.RegisterCreatedObjectUndo(realPillar, "Place Pillar V1M");
            currentBuildObjects.Add(realPillar);
            ApplyPillarMVisualVariation(realPillar);
            realPillar.transform.SetPositionAndRotation(
                hoverChainPillarsM[i].transform.position,
                hoverChainPillarsM[i].transform.rotation);
            realPillar.transform.localScale = continueScale;
        }
        else
        {
            // Last pillar → replacement type at replacement ghost pos/rot
            var realPillar = (GameObject)PrefabUtility.InstantiatePrefab(repPrefab);
            ApplyCurrentTextureVariantToObject(realPillar);
            ApplyContinueTopToPillar(realPillar);
            Undo.RegisterCreatedObjectUndo(realPillar, "Place Close Loop Pillar");
            currentBuildObjects.Add(realPillar);

            if (closeLoopReplacementGhost)
            {
                realPillar.transform.SetPositionAndRotation(
                    closeLoopReplacementGhost.transform.position,
                    closeLoopReplacementGhost.transform.rotation);
            }
            else
            {
                realPillar.transform.SetPositionAndRotation(
                    hoverChainPillarsM[i].transform.position,
                    hoverChainPillarsM[i].transform.rotation);
            }
            realPillar.transform.localScale = continueScale;

            lastPillar = realPillar;
        }
    }

    // Permanently delete the hidden target pillar (and its visible repair-V1E
    // twin if any: when the close-loop target is a hidden M-pillar from the
    // curved/45 delete path, a visible repair-V1E sits at the same world
    // position and must also be removed).
    if (closeLoopTargetPillar)
    {
        bool prevSuppress = suppressDeleteUndo;
        suppressDeleteUndo = true;
        currentBuildObjects.Remove(closeLoopTargetPillar);
        Undo.DestroyObjectImmediate(closeLoopTargetPillar);
        suppressDeleteUndo = prevSuppress;
    }

    // Clear close-loop state (no re-show, target is destroyed)
    ClearCloseLoopGhostSwap();
    closeLoopTargetPillar = null;
    closeLoopDetected = false;
    closeLoopReplacementType = '\0';

    ApplyFullDetailToCurrentBuild();
    return lastPillar;
}

void ClearCloseLoop()
{
    ClearCloseLoopGhostSwap();
    if (closeLoopTargetPillar)
        foreach (var r in closeLoopTargetPillar.GetComponentsInChildren<Renderer>(true))
            r.enabled = true;
    closeLoopTargetPillar = null;
    closeLoopDetected = false;
    closeLoopReplacementType = '\0';
}

// Find the visible repair-V1E pillar nearest to a world position. Used to
// temporarily hide it during close-loop preview when its underlying hidden
// M-pillar is the close-loop target, so the green ghost does not visually
// overlap with the existing repair pillar.
GameObject FindRepairTwinAtPosition(Vector3 worldPos)
{
    float scale = Mathf.Max(continueScale.x, 1f);
    float tol = 0.3f * scale;
    float bestSq = tol * tol;
    GameObject best = null;
    foreach (var root in finalizedBalustrades)
    {
        if (!root) continue;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!t) continue;
            var go = t.gameObject;
            if (!IsPillarInstance(go)) continue;
            if (IsHiddenDeletedPillar(go)) continue;
            if (!OverlapsHiddenPillar(go)) continue;
            Vector3 d = t.position - worldPos;
            d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = go; }
        }
    }
    return best;
}

// True if the given visible pillar sits at (essentially) the same XZ position
// as a hidden M-pillar under the same root. Identifies a visible repair-V1E
// that was spawned by the curved/45 rail-delete path.
bool OverlapsHiddenPillar(GameObject pillar)
{
    return FindHiddenTwinUnderSameRoot(pillar) != null;
}

// Return the hidden M-pillar that sits at the same XZ position as the given
// visible pillar (or null). Used by close-loop detection to substitute the
// hidden pillar as the actual target — its full snap geometry is required
// for the repair fit, the visible repair-V1E only has one usable snap.
//
// Tolerance is half a pillar width: the visible repair-V1E is shifted from
// the hidden M's center to the surviving rail's snap (~half pillar width
// away) by SpawnRepairPillarAndHidePillar, so an exact-position match would
// fail. Anything within that radius counts as "overlapping" for our purpose.
GameObject FindHiddenTwinUnderSameRoot(GameObject pillar)
{
    if (!pillar || IsHiddenDeletedPillar(pillar)) return null;

    Transform root = pillar.transform;
    while (root && !finalizedBalustrades.Contains(root.gameObject))
        root = root.parent;
    if (!root) return null;

    Vector3 pp = pillar.transform.position; pp.y = 0f;
    float scale = Mathf.Max(root.lossyScale.x, 1f);
    float tol = 0.3f * scale;
    float tolSq = tol * tol;

    GameObject best = null;
    float bestSq = tolSq;
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (!t || t.gameObject == pillar) continue;
        if (!IsHiddenDeletedPillar(t.gameObject)) continue;
        Vector3 hp = t.position; hp.y = 0f;
        float sq = (pp - hp).sqrMagnitude;
        if (sq < bestSq) { bestSq = sq; best = t.gameObject; }
    }
    return best;
}

void ClearCloseLoopFull()
{
    ClearCloseLoop();
    ClearTintAllActiveGhosts();
}

// Swap the last ghost pillar to visually match the replacement type.
// V1M stays as-is (already the default ghost). Other types get a visual override.
void UpdateCloseLoopGhostSwap(GameObject lastGhost, char repType)
{
    if (repType == '\0' || repType == 'M')
    {
        ClearCloseLoopGhostSwap();
        return;
    }

    if (closeLoopReplacementGhost && closeLoopOriginalGhost == lastGhost)
    {
        ApplyGhostTint(closeLoopReplacementGhost, GhostCloseLoopCol);
        return;
    }

    ClearCloseLoopGhostSwap();

    var prefab = FindAsset<GameObject>(GetPillarPrefabName(repType));
    if (!prefab || !closeLoopTargetPillar) return;

    closeLoopReplacementGhost = CreateGhost(prefab);
    closeLoopReplacementGhost.name = "CloseLoopSwapGhost";
    AddGhostTopToPillar(closeLoopReplacementGhost);

    // Use the exact rotation+position found by TryFitCloseLoop
    closeLoopReplacementGhost.transform.SetPositionAndRotation(closeLoopFitPosition, closeLoopFitRotation);

    ApplyGhostTint(closeLoopReplacementGhost, GhostCloseLoopCol);

    closeLoopOriginalGhost = lastGhost;
    foreach (var r in lastGhost.GetComponentsInChildren<Renderer>(true))
        r.enabled = false;
}

void ClearCloseLoopGhostSwap()
{
    if (closeLoopReplacementGhost)
        DestroyImmediate(closeLoopReplacementGhost);
    closeLoopReplacementGhost = null;

    // Restore original ghost visibility
    if (closeLoopOriginalGhost)
        foreach (var r in closeLoopOriginalGhost.GetComponentsInChildren<Renderer>(true))
            r.enabled = true;
    closeLoopOriginalGhost = null;
}

// Commit arc preview objects + close-loop chain in one transaction.
// isOuterArc: true for outer arc (has V1C corner), false for inner arc.
void CommitArcAndCloseLoop(bool isOuterArc)
{
    if (!hover90Root || !hover90Rail || !hover90PillarM) return;

    var curvedPrefab = FindAsset<GameObject>(
        $"blstrsCrvd_{balusterStyleIndex + 1}{(selectedVariant == ContinueVariant.V2 ? "V2" : "V1")}_PREFAB");
    if (!curvedPrefab) { ClearHover90Preview(); return; }

    // --- 1) Commit straight rail segments, skip last V1M (close-loop handles junction) ---
    CommitRailSegs(null, skipLastPillar: true);

    // --- 2) Outer arc: remove pre-existing lastPlacedPillarM (V1C corner replaces it) ---
    bool ps = suppressDeleteUndo; suppressDeleteUndo = true;
    if (isOuterArc && lastPlacedPillarM)
    {
        currentBuildObjects.Remove(lastPlacedPillarM);
        Undo.DestroyObjectImmediate(lastPlacedPillarM);
        lastPlacedPillarM = null;
    }
    suppressDeleteUndo = ps;
    if (isOuterArc) RebuildProtectedPillarIdCache();

    // --- 3) Commit arc objects ---
    // Outer arc: V1C corner pillar
    if (isOuterArc && hover90EndPillarE)
    {
        var src = PrefabUtility.GetCorrespondingObjectFromSource(hover90EndPillarE);
        if (src)
        {
            var pf = FindAsset<GameObject>(src.name);
            if (pf)
            {
                var rc = (GameObject)PrefabUtility.InstantiatePrefab(pf);
                ApplyCurrentTextureVariantToObject(rc);
                ApplyContinueTopToPillar(rc);
                Undo.RegisterCreatedObjectUndo(rc, "Place V1C (Arc Close Loop)");
                currentBuildObjects.Add(rc);
                rc.transform.SetPositionAndRotation(
                    hover90EndPillarE.transform.position,
                    hover90EndPillarE.transform.rotation);
                rc.transform.localScale = continueScale;
            }
        }
    }

    // Curved rail
    var realCrv = (GameObject)PrefabUtility.InstantiatePrefab(curvedPrefab);
    ApplyCurrentTextureVariantToObject(realCrv);
    Undo.RegisterCreatedObjectUndo(realCrv, "Place Curved Rail (Arc Close Loop)");
    currentBuildObjects.Add(realCrv);
    ApplyRailVisualVariation(realCrv);
    ApplyCurvedRailVisualVariation(realCrv);
    realCrv.transform.SetPositionAndRotation(hover90Rail.transform.position, hover90Rail.transform.rotation);
    realCrv.transform.localScale = continueScale;

    // Save junction position before destroying preview
    Vector3 jPos = hover90PillarM.transform.position;
    Quaternion jRot = hover90PillarM.transform.rotation;

    // Clear hover90 preview
    ShowLastGhostPillarM();
    ShowCurvedGhostEndPillar();
    if (hover90Root) DestroyImmediate(hover90Root);
    hover90Root = null; hover90EndPillarE = null; hover90Rail = null; hover90PillarM = null;

    // --- 4) Junction pillar (between curved rail and close-loop chain) ---
    // If close-loop detection already determined the replacement type, use it directly.
    // Otherwise fall back to FitV1TToRails probing (V1C45, V1C, V1M).
    char jType;
    if (closeLoopDetected && closeLoopReplacementType != '\0')
    {
        jType = closeLoopReplacementType;
        // Use the exact pose from detection so snap alignment stays consistent
        if (closeLoopReplacementGhost)
        {
            jPos = closeLoopReplacementGhost.transform.position;
            jRot = closeLoopReplacementGhost.transform.rotation;
        }
    }
    else
    {
        jType = 'M';
        foreach (char cand in new[] { '4', 'C' })
        {
            var pf = FindAsset<GameObject>(GetPillarPrefabName(cand));
            if (!pf) continue;
            var tmp = (GameObject)PrefabUtility.InstantiatePrefab(pf);
            tmp.transform.localScale = continueScale;
            tmp.transform.SetPositionAndRotation(jPos, jRot);
            bool fits = FitV1TToRails(tmp, jPos);
            DestroyImmediate(tmp);
            if (fits) { jType = cand; break; }
        }
    }
    var jPrefab = FindAsset<GameObject>(GetPillarPrefabName(jType));
    if (jPrefab)
    {
        var jp = (GameObject)PrefabUtility.InstantiatePrefab(jPrefab);
        jp.transform.localScale = continueScale;
        ApplyCurrentTextureVariantToObject(jp);
        ApplyContinueTopToPillar(jp);
        Undo.RegisterCreatedObjectUndo(jp, $"Place Curved Rail");
        currentBuildObjects.Add(jp);
        jp.transform.SetPositionAndRotation(jPos, jRot);
        // Only re-run FitV1TToRails if we didn't already use a detected close-loop pose
        if (!(closeLoopDetected && closeLoopReplacementType != '\0'))
            FitV1TToRails(jp, jPos);
        if (jType == 'M') ApplyPillarMVisualVariation(jp);
    }

    // --- 5) Commit close-loop chain ---
    CommitCloseLoopChain();

    // --- 6) Handle continue anchor ---
    CleanupContinueAnchorForCloseLoop(isOuterArc);
}

}
} // namespace WB3DAssets.BalustradeModularSystem
