using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.SceneManagement;

namespace WB3DAssets.BalustradeModularSystem
{
public partial class BalustradeModularWindow : EditorWindow
{
    enum State { Idle, FreeMove, DirectionSelect, RailPreview, CornerSelect }

enum ContinueVariant
{
    V1,
    V2
}

bool variantV1Available;

bool variantV2Available;

Texture2D variantV1Preview;

Texture2D variantV2Preview;

Texture2D[] balusterStylePreviews;

int balusterStyleIndex = 0;

Texture2D[] topPreviews;

int topPreviewIndex = 0;

ContinueVariant selectedVariant = ContinueVariant.V1;

bool textureVariantIsWorn = false;

int newTextureIndex = 0; // 0 = new1, 1 = new2, 2 = new3, 3 = new4
int wornTextureIndex = 0; // 0 = worn1, 1 = worn2, 2 = worn3, 3 = worn4

    const string Root = "Assets/Balustrade Modular System";

    const string RootHDRP = Root + "/HDRP";

    const string RootURPBuiltIn = Root + "/URP & Built-In";
    const string RootURP  = RootURPBuiltIn + "/URP";
    const string RootBuiltIn = RootURPBuiltIn + "/Built-In";

    static string PipelineRoot
    {
        get
        {
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (rp == null) return RootBuiltIn;
            string typeName = rp.GetType().Name;
            if (typeName.Contains("HDRenderPipeline")) return RootHDRP;
            return RootURP;
        }
    }

    const string PillarPrefabName = "pillar_V1E_PREFAB";

    const string RailPrefabName   = "blstrs_1V1_PREFAB";

    const string CurvedRailPrefabName = "blstrsCrvd_1V1_PREFAB";

const string PillarMPrefabName = "pillar_V1M_PREFAB";

const string PillarTPrefabName = "pillar_V1T_PREFAB";

const string PillarCPrefabName = "pillar_V1C_PREFAB";

const string PillarC45PrefabName = "pillar_V1C45_PREFAB";

    const string GhostMatName     = "GhostPreview_MAT";

    const string PillarSnapName   = "SnapPoint1"; // X+ = build dir

    const string RailStartSnap    = "SnapPoint1"; // Rail SnapPoint1 local X+ points to start pillar

const string RailEndSnap      = "SnapPoint2";

const string TopSnapName      = "SnapPointTop";

static readonly string[] TopPrefabNames =
{
    "top1_PREFAB",
    "top2_PREFAB",
    "top3_PREFAB",
    "top4_PREFAB",
};

const float ArrowLength = 0.4f;

const float ArrowHeadSize = 0.12f;

static readonly int ContinueGizmoIdHint = "BMS_ContinueDoubleArrow".GetHashCode();

const float Dot22_5 = 0.9238795f; // cos(22.5°)

const float Dot67_5 = 0.3826834f; // cos(67.5°)

    static readonly Color BaseCol   = new(0.45f, 0.75f, 1f, 0.55f);

    static readonly Color ActiveCol = new(0.65f, 1f, 0.30f, 0.95f);

    State state;

    bool buildMode;

bool canContinueBuild;

GameObject selectedContinuePillar;

readonly List<GameObject> finalizedBalustrades = new();

readonly Dictionary<GameObject, GameObject> balustradeStartPillars = new();

readonly Dictionary<GameObject, ChainIndexCache> chainIndexByRoot = new();

readonly Dictionary<GameObject, List<BalId>> deletedRailIdsByRoot = new();

const string StartMarkerName = "__BMS_START__";

const string VariantLockMarkerName = "__BMS_VARIANT_LOCKED__";

int selectedBalustradeIndex = -1;

System.Action onHierarchyChanged;

bool suppressDeleteUndo;

readonly HashSet<BalId> protectedPillarIds = new();

bool lastSelectionWasRail;

bool lastSelectionWasPillar;

GameObject lastRailDeleteBalustradeRoot;

// Cache: selected rail ID → captured snap world poses + isCurved flag.
// Used to materialize a ghost-rail under the balustrade root when the rail is deleted,
// so variant switches can rebuild correctly across the gap.
struct PendingRailSnap
{
    public Vector3 snap1Pos, snap2Pos;
    public Quaternion snap1Rot, snap2Rot;
    public bool isCurved;
    public GameObject root;
}
readonly Dictionary<BalId, PendingRailSnap> pendingRailSnaps = new();

readonly HashSet<BalId> railCoSelectedPillarIds = new(); // Pillars visually co-selected with rail
bool suppressSelectionChanged; // Prevent recursion when setting Selection.objects

    GameObject ghost;

    Material ghostMat;

static MaterialPropertyBlock ghostPropBlock;

static readonly Color GhostInvalidCol = new(1f, 0.45f, 0.4f, 0.65f);  // bright reddish warning

static readonly int PropBaseColor  = Shader.PropertyToID("_BaseColor");   // HDRP Lit + URP

static readonly int PropUnlitColor = Shader.PropertyToID("_UnlitColor");  // HDRP Unlit

static readonly int PropColor      = Shader.PropertyToID("_Color");       // Built-in fallback

const float FlatNormalThreshold = 0.9998f; // dot(normal, up) must exceed this

bool fullDetailMode = true;

    GameObject lastPlacedPillarM;

GameObject lastPlacedPillarE;

readonly List<GameObject> currentBuildObjects = new();

    Vector3 frozenPos;

    Vector3 activeDir = Vector3.right;

    Transform lastPillarSnap;

bool continueDirOverrideActive;

Vector3 continueDirOverride;

string continueTSnapOverride; // "SnapPointT1" or "SnapPointT2"

bool isV1MContinueMode; // Flag for reduced CornerSelect from V1M

readonly List<GameObject> ghostPillarsM = new();

    GameObject railPreviewRoot;

    readonly List<GameObject> railSegs = new();

    Vector3 railAnchorPos;

    float segLen;

GameObject curvedGhostRoot;

GameObject ghostCurvedRail;

GameObject ghostCurvedPillar;

bool curvedGhostActive;

int consecutiveUnmirroredCurvedRails = 0;

int consecutiveUnmirroredPillarM = 0;

Transform curvedGhostEndSnap;

string curvedOutSnapName;

GameObject hover90Root;

GameObject hover90EndPillarE;

GameObject hover90Rail;

GameObject hover90PillarM;

readonly List<GameObject> hoverChainRails = new();

readonly List<GameObject> hoverChainPillarsM = new();

Vector3 hoverChainAnchorPos;

Vector3 hoverChainDir;

float hoverChainSegLen;

Transform hoverChainStartSnap; // Cached start snap for corner pillars

GameObject dirSelectHoverRoot;

readonly List<GameObject> dirSelectHoverRails = new();

readonly List<GameObject> dirSelectHoverPillars = new();

float dirSelectSegLen;

GameObject continueAnchorPillar;   // hidden V1E

bool continueAnchorActive;


Transform continueSnapProxy;

GameObject continueTargetBalustrade;
Vector3 continueScale = Vector3.one; // Scale inherited from target balustrade

int continueUndoGroup = -1; // Undo group captured at Continue Build start, collapsed on finalize

int continueTopIndex = -1; // -1 = no tops

GameObject continueGhostPillarM;

// Close loop detection
GameObject closeLoopTargetPillar;
bool closeLoopDetected;
char closeLoopReplacementType; // 'E','M','C','4'(C45),'T' or '\0'
Vector3 closeLoopApproachDir;
Quaternion closeLoopFitRotation;
Vector3 closeLoopFitPosition;
GameObject closeLoopReplacementGhost; // visual swap ghost
GameObject closeLoopOriginalGhost;    // hidden original last ghost
static readonly Color GhostCloseLoopCol = new(0.2f, 1f, 0.35f, 0.9f);

bool ENABLE_CONTINUE_BUILD = true;
bool ENABLE_V1M_VARIANT_LOCK = false; // Set true to re-enable V1M variant lock dialog

// ===================== FREE VERSION (Asset Store sampler) =====================
// Paid Asset Store listing the padlocks and the upsell button open.
const string FullVersionUrl = "https://assetstore.unity.com/packages/3d/props/exterior/balustrade-modular-system-232938";

bool _freeComputed;
readonly HashSet<ContinueVariant> _availPillar = new HashSet<ContinueVariant>();
readonly HashSet<int> _availBaluster = new HashSet<int>(); // 1-based baluster style numbers present
readonly HashSet<int> _availTop = new HashSet<int>();      // 1-based top numbers present
// "new|0" style keys: which individual finish slots ship in this build
readonly HashSet<string> _availFinish = new HashSet<string>();

static string FinishKey(bool worn, int idx) => (worn ? "worn" : "new") + "|" + idx;

void EnsureFreeAvailability()
{
    if (_freeComputed && _availBaluster.Count > 0) return; // self-heal: recompute while still empty

    _availPillar.Clear();
    _availBaluster.Clear();
    _availTop.Clear();
    _availFinish.Clear();

    foreach (ContinueVariant v in System.Enum.GetValues(typeof(ContinueVariant)))
        if (FindAsset<GameObject>("pillar_" + VariantTagOf(v) + "E_PREFAB") != null) _availPillar.Add(v);

    for (int i = 1; i <= 12; i++)
        if (FindAsset<GameObject>("blstrs_" + i + "V1_PREFAB") != null) _availBaluster.Add(i);

    for (int i = 1; i <= TopPrefabNames.Length; i++)
        if (FindAsset<GameObject>("top" + i + "_PREFAB") != null) _availTop.Add(i);

    for (int i = 1; i <= 4; i++)
    {
        if (FindAsset<Material>("blstrs_new"  + i + "_MAT") != null) _availFinish.Add(FinishKey(false, i - 1));
        if (FindAsset<Material>("blstrs_worn" + i + "_MAT") != null) _availFinish.Add(FinishKey(true,  i - 1));
    }

    // Only cache once assets are actually present. If this runs during a domain
    // reload (before the AssetDatabase is ready) nothing is found - leave it
    // uncached so the next refresh retries instead of caching an empty result.
    if (_availBaluster.Count > 0) _freeComputed = true;
}

bool IsPillarVariantAvailable(ContinueVariant v)
{
    EnsureFreeAvailability();
    return _availPillar.Contains(v);
}

// The last carousel slot is "No Tops" and always ships.
bool IsTopAvailable(int idx)
{
    EnsureFreeAvailability();
    if (idx >= TopPrefabNames.Length) return true;
    return _availTop.Contains(idx + 1);
}

bool IsBalusterAvailable(int idx)
{
    EnsureFreeAvailability();
    return _availBaluster.Contains(idx + 1);
}

bool IsFinishAvailable(bool worn, int idx)
{
    EnsureFreeAvailability();
    return _availFinish.Contains(FinishKey(worn, idx));
}

    [MenuItem("Tools/Balustrade Modular System")]
    static void Open()
    {
        var w = GetWindow<BalustradeModularWindow>("Balustrade Modular System");
        // Lower bound only. The former fixed 300x560 pinned the window even on a
        // large monitor and squeezed the preview panels; the layout scrolls now.
        w.minSize = new Vector2(300f, 420f);
        w.maxSize = new Vector2(4000f, 4000f);
        w.Show();
    }

void OnEnable()
{
    Selection.selectionChanged += OnSelectionChanged;
SceneView.duringSceneGui += OnSceneGUI_Overlay;
onHierarchyChanged = () =>
{
    CleanupFinalizedBalustrades();
    RebuildProtectedPillarIdCache();
    RefreshUi();
};
EditorApplication.hierarchyChanged += onHierarchyChanged;
ObjectChangeEvents.changesPublished += OnObjectChanges_BlockPillarDelete;
Undo.undoRedoPerformed += OnUndoRedoPerformed;

// Scan scene for existing balustrades (survives Editor restart)
ScanSceneForExistingBalustrades();
RebuildProtectedPillarIdCache();

// Delay asset loading until AssetDatabase is fully ready
EditorApplication.delayCall += () =>
{
    EnsurePipelineMaterials();
    LoadPreviewTextures();
    OnSelectionChanged();
    RefreshUi();
};

balusterStyleIndex = 0;
if (balusterStylePreviews == null) balusterStylePreviews = new Texture2D[12];
if (topPreviews == null) topPreviews = new Texture2D[4];
topPreviewIndex = 4; // start with "No Tops"
textureVariantIsWorn = false; // default = NEW
selectedVariant = ContinueVariant.V1; // hard default on window open
previewVariant  = ContinueVariant.V1;
}

void LoadPreviewTextures()
{
    variantV1Preview = FindAsset<Texture2D>("pillar_V1_PREVIEW");
    variantV2Preview = FindAsset<Texture2D>("pillar_V2_PREVIEW");
    balusterStylePreviews = new Texture2D[]
    {
        FindAsset<Texture2D>("blstrs_1_PREVIEW"),
        FindAsset<Texture2D>("blstrs_2_PREVIEW"),
        FindAsset<Texture2D>("blstrs_3_PREVIEW"),
        FindAsset<Texture2D>("blstrs_4_PREVIEW"),
        FindAsset<Texture2D>("blstrs_5_PREVIEW"),
        FindAsset<Texture2D>("blstrs_6_PREVIEW"),
        FindAsset<Texture2D>("blstrs_7_PREVIEW"),
        FindAsset<Texture2D>("blstrs_8_PREVIEW"),
        FindAsset<Texture2D>("blstrs_9_PREVIEW"),
        FindAsset<Texture2D>("blstrs_10_PREVIEW"),
        FindAsset<Texture2D>("blstrs_11_PREVIEW"),
        FindAsset<Texture2D>("blstrs_12_PREVIEW"),
    };

    topPreviews = new Texture2D[]
    {
        FindAsset<Texture2D>("top1_PREVIEW"),
        FindAsset<Texture2D>("top2_PREVIEW"),
        FindAsset<Texture2D>("top3_PREVIEW"),
        FindAsset<Texture2D>("top4_PREVIEW"),
    };
    topPreviewIndex = topPreviews.Length;
}

void OnSelectionChanged()
{
    if (suppressSelectionChanged) return;

    var sel = Selection.activeGameObject;

// Cache last rail selection so we can react after user deletes it (Destroy event has no object reference)
lastSelectionWasRail = sel && IsRailInstance(sel);
lastSelectionWasPillar = sel && IsPillarInstance(sel);
lastRailDeleteBalustradeRoot = lastSelectionWasRail
    ? FindOwningBalustradeRoot(sel.transform)?.gameObject
    : null;

// Cache snap world poses of ALL currently selected rails so OnObjectChanges
// can later materialize ghost-rail markers under the root for variant reposition.
// Drop stale entries first: a rail that's no longer selected must not stay in
// pendingRailSnaps, otherwise a later delete on a DIFFERENT rail would also
// hide the previously-cached one.
{
    var currentSelIds = new HashSet<BalId>();
    foreach (var o in Selection.objects)
        if (o is GameObject g) currentSelIds.Add(g.StableId());
    var stale = new List<BalId>();
    foreach (var k in pendingRailSnaps.Keys)
        if (!currentSelIds.Contains(k)) stale.Add(k);
    foreach (var k in stale) pendingRailSnaps.Remove(k);
}
foreach (var obj in Selection.objects)
{
    var go = obj as GameObject;
    if (!go || !IsRailInstance(go)) continue;
    var s1 = FindSnap(go.transform, RailStartSnap);
    var s2 = FindSnap(go.transform, RailEndSnap);
    if (!s1 || !s2) continue;
    var rootGo = FindOwningBalustradeRoot(go.transform)?.gameObject;
    if (!rootGo) continue;
    var srcAsset = PrefabUtility.GetCorrespondingObjectFromSource(go);
    bool isCurved = srcAsset && srcAsset.name.StartsWith("blstrsCrvd_");
    pendingRailSnaps[go.StableId()] = new PendingRailSnap
    {
        snap1Pos = s1.position, snap1Rot = s1.rotation,
        snap2Pos = s2.position, snap2Rot = s2.rotation,
        isCurved = isCurved,
        root = rootGo
    };
}

// Rail selected → co-select connected pillars
// Single: orphan pillars only. Multi: pillars between selected rails.
bool isSingleSelect = Selection.objects.Length <= 1;
if (isSingleSelect) railCoSelectedPillarIds.Clear();
if (lastSelectionWasRail && sel)
{
    if (isSingleSelect)
    {
        var pillars = FindCoSelectPillarsForRail(sel, onlyOrphans: true);
        if (pillars.Count > 0)
        {
            foreach (var p in pillars)
                railCoSelectedPillarIds.Add(p.StableId());

            var capturedPillars = pillars.ToArray();
            var capturedSelection = Selection.objects;
            suppressSelectionChanged = true;
            EditorApplication.delayCall += () =>
            {
                var objs = new List<Object>(capturedSelection);
                foreach (var p in capturedPillars)
                    if (p && !objs.Contains(p)) objs.Add(p);
                if (objs.Count > capturedSelection.Length)
                    Selection.objects = objs.ToArray();
                EditorApplication.delayCall += () => suppressSelectionChanged = false;
            };
        }
    }
    else
    {
        // Multi-select: strip old co-selected pillars, find between-pillars
        var selectedRails = new List<GameObject>();
        var cleanSelection = new List<Object>();
        foreach (var o in Selection.objects)
        {
            var go = o as GameObject;
            if (go && railCoSelectedPillarIds.Contains(go.StableId())) continue;
            cleanSelection.Add(o);
            if (go && IsRailInstance(go)) selectedRails.Add(go);
        }
        railCoSelectedPillarIds.Clear();

        var pillars = FindPillarsBetweenRails(selectedRails.ToArray());
        foreach (var p in pillars)
            railCoSelectedPillarIds.Add(p.StableId());

        bool needsUpdate = pillars.Count > 0 || cleanSelection.Count < Selection.objects.Length;
        if (needsUpdate)
        {
            var capturedPillars = pillars.ToArray();
            var capturedClean = cleanSelection.ToArray();
            suppressSelectionChanged = true;
            EditorApplication.delayCall += () =>
            {
                var objs = new List<Object>(capturedClean);
                foreach (var p in capturedPillars)
                    if (p && !objs.Contains(p)) objs.Add(p);
                Selection.objects = objs.ToArray();
                EditorApplication.delayCall += () => suppressSelectionChanged = false;
            };
        }
    }
}

    // --- TRANSFORM GIZMO CONTROL ---
if (!sel)
{
    Tools.hidden = false;
    SetBalustradeGizmosVisible(true);
}
else if (finalizedBalustrades.Contains(sel))
{
    Tools.hidden = false;          // Transform gizmos ON
    SetBalustradeGizmosVisible(false); // MeshCollider + LOD gizmos OFF
    SyncUiFromBalustradeRoot(sel);
    EnsureBalustradePivotCentered(sel); // full-version parity: center pivot on root selection
}
else
{
    // Lazy discovery: find owning root (may add untracked Balustrade_ root)
    var root = FindBalustradeRootFromSelection(sel);
    bool isBalustradeChild = root != null;
    bool isRoot = isBalustradeChild && root == sel;

    Tools.hidden = isBalustradeChild && !isRoot;
    SetBalustradeGizmosVisible(!isBalustradeChild);

    if (isRoot) SyncUiFromBalustradeRoot(sel);
}

    UpdateContinueBuildState();
    SceneView.RepaintAll();
    RefreshUi();
}

void OnDisable()
{
    Selection.selectionChanged -= OnSelectionChanged;
SceneView.duringSceneGui -= OnSceneGUI_Overlay;
if (onHierarchyChanged != null)
    EditorApplication.hierarchyChanged -= onHierarchyChanged;
ObjectChangeEvents.changesPublished -= OnObjectChanges_BlockPillarDelete;
Undo.undoRedoPerformed -= OnUndoRedoPerformed;
onHierarchyChanged = null;
    StopBuildMode();

SetBalustradeGizmosVisible(true);
Tools.hidden = false;
}

// ===================== UI (UI Toolkit) =====================
// Element lookups are resolved once in CreateGUI. RefreshUi runs on every
// selection change and on a timer, so walking the tree by name each time
// would be wasted work.
Button uiBuildBtn, uiUpsellBtn;
Button uiPillarPrev, uiPillarNext, uiTopPrev, uiTopNext, uiBalusterPrev, uiBalusterNext;
Button uiNewPrev, uiNewNext, uiWornPrev, uiWornNext;
Toggle uiFullDetail;
VisualElement uiPillarPreview, uiPillarLock, uiTopPreview, uiTopLock;
VisualElement uiBalusterPreview, uiBalusterLock, uiNewPreview, uiNewLock, uiWornPreview, uiWornLock;
Label uiPillarName, uiPillarDesc, uiTopName, uiTopDesc, uiTopEmpty;
Label uiBalusterName, uiNewName, uiWornName, uiTextureDesc, uiLiteHint;

// ---- browse indices ---------------------------------------------------------
// Every carousel has two positions: the browse index the arrows move through
// ALL slots with, and the working index the build code reads. The working one
// never lands on a locked slot, so no prefab or material lookup can miss.
ContinueVariant previewVariant = ContinueVariant.V1;
int lastVariantSeen = -1;
int balusterBrowseIndex;
int lastBalusterSeen = -1;
int topBrowseIndex;
int lastTopSeen = -1;
int newPreviewIndex;
int wornPreviewIndex;
int lastNewIdxSeen = -1;
int lastWornIdxSeen = -1;
GameObject lastUiRootSeen;

// The browse indices follow the working ones whenever those change (balustrade
// selected, variant swapped, defaults applied), so none of the places that set
// the working index has to know about them. This must NOT be a plain per-refresh
// assignment: ApplyUiStateFromBalustradeRoot runs on every tick while something
// is selected and would drag the browse position back between two arrow clicks,
// so the user could never step past a locked slot.
void SyncBrowseIndices(bool force)
{
    // Picking a different balustrade is a deliberate act, so the carousels jump
    // to what that one actually carries instead of keeping a browse position
    // left over from looking at the locked styles.
    if (force)
    {
        lastVariantSeen = -1;
        lastBalusterSeen = -1;
        lastTopSeen = -1;
        lastNewIdxSeen = -1;
        lastWornIdxSeen = -1;
    }

    if ((int)selectedVariant != lastVariantSeen)
    { lastVariantSeen = (int)selectedVariant; previewVariant = selectedVariant; }

    if (balusterStyleIndex != lastBalusterSeen)
    { lastBalusterSeen = balusterStyleIndex; balusterBrowseIndex = balusterStyleIndex; }

    if (topPreviewIndex != lastTopSeen)
    { lastTopSeen = topPreviewIndex; topBrowseIndex = topPreviewIndex; }

    if (newTextureIndex != lastNewIdxSeen)
    { lastNewIdxSeen = newTextureIndex; newPreviewIndex = newTextureIndex; }

    if (wornTextureIndex != lastWornIdxSeen)
    { lastWornIdxSeen = wornTextureIndex; wornPreviewIndex = wornTextureIndex; }
}

static string VariantTagOf(ContinueVariant v) => v == ContinueVariant.V2 ? "V2" : "V1";

static string PillarNameOf(ContinueVariant v) => v == ContinueVariant.V2 ? "Variant V2" : "Variant V1";

// ---- preview textures ------------------------------------------------------
// Cached on the object reference, never on a bool. A re-import destroys the
// texture object, and a "resolved" flag would then block the reload for good.
Texture2D PillarPreviewOf(ContinueVariant v)
{
    if (v == ContinueVariant.V2)
    {
        if (variantV2Preview == null) variantV2Preview = FindAsset<Texture2D>("pillar_V2_PREVIEW");
        return variantV2Preview;
    }
    if (variantV1Preview == null) variantV1Preview = FindAsset<Texture2D>("pillar_V1_PREVIEW");
    return variantV1Preview;
}

Texture2D TopPreviewAt(int i)
{
    if (topPreviews == null || i < 0 || i >= topPreviews.Length) return null;
    if (topPreviews[i] == null) topPreviews[i] = FindAsset<Texture2D>("top" + (i + 1) + "_PREVIEW");
    return topPreviews[i];
}

Texture2D BalusterPreviewAt(int i)
{
    if (balusterStylePreviews == null || i < 0 || i >= balusterStylePreviews.Length) return null;
    if (balusterStylePreviews[i] == null)
        balusterStylePreviews[i] = FindAsset<Texture2D>("blstrs_" + (i + 1) + "_PREVIEW");
    return balusterStylePreviews[i];
}

readonly Texture2D[] newFinishPreviews = new Texture2D[4];
readonly Texture2D[] wornFinishPreviews = new Texture2D[4];

Texture2D FinishPreviewAt(bool worn, int i)
{
    var arr = worn ? wornFinishPreviews : newFinishPreviews;
    if (i < 0 || i >= arr.Length) return null;
    if (arr[i] == null) arr[i] = FindAsset<Texture2D>((worn ? "worn" : "new") + (i + 1) + "_PREVIEW");
    return arr[i];
}

// Badge sizes live in the stylesheet (.lock-badge-large / .lock-badge-small).
static readonly Color LockGoldCol = new Color(1f, 0.882f, 0.467f, 1f); // house gold #ffe177

Texture2D lockIconTex;
bool lockIconIsCustom; // shipped icon is drawn as authored, the fallback gets tinted

// Uses the padlock shipped with the package if there is one, otherwise falls
// back to Unity's built-in lock texture so the badge is never missing.
Texture2D GetLockIcon()
{
    if (lockIconTex != null) return lockIconTex;
    lockIconTex = FindAsset<Texture2D>("blstr_LOCK_ICON");
    lockIconIsCustom = lockIconTex != null;
    if (lockIconTex == null)
    {
        var c = EditorGUIUtility.IconContent("IN LockButton on");
        if (c != null) lockIconTex = c.image as Texture2D;
    }
    return lockIconTex;
}

// Resolve THIS file's folder at runtime. The UXML/USS sit next to it, so the
// pack keeps working after a buyer renames or moves the folder.
string ToolFolder()
{
    var ms = MonoScript.FromScriptableObject(this);
    var p  = ms != null ? AssetDatabase.GetAssetPath(ms) : null;
    if (!string.IsNullOrEmpty(p))
        return System.IO.Path.GetDirectoryName(p).Replace('\\', '/');

    // Fallback: find the layout asset itself. In a precompiled build there is
    // no script asset to locate, but the .uxml always ships beside the assembly.
    foreach (var guid in AssetDatabase.FindAssets("BalustradeModularWindow t:VisualTreeAsset"))
    {
        var ap = AssetDatabase.GUIDToAssetPath(guid);
        if (ap.EndsWith("/BalustradeModularWindow.uxml"))
            return System.IO.Path.GetDirectoryName(ap).Replace('\\', '/');
    }
    return null;
}

public void CreateGUI()
{
    var root = rootVisualElement;
    root.Clear();

    var dir  = ToolFolder();
    var tree = dir != null
        ? AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(dir + "/BalustradeModularWindow.uxml")
        : null;

    if (tree == null)
    {
        // Without the layout the window can do nothing - say so instead of staying empty.
        const string msg = "BalustradeModularWindow.uxml was not found next to BalustradeModularWindow.cs. "
                         + "Keep the Editor folder of Balustrade Modular System together (the .uxml and "
                         + ".uss must sit beside the script), then reopen the window.";
        root.Add(new HelpBox(msg, HelpBoxMessageType.Error));
        Debug.LogError("[Balustrade Modular System] " + msg);
        return;
    }

    tree.CloneTree(root);

    var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(dir + "/BalustradeModularWindow.uss");
    if (sheet != null) root.styleSheets.Add(sheet);

    uiBuildBtn        = root.Q<Button>("build-btn");
    uiUpsellBtn       = root.Q<Button>("upsell-btn");
    uiPillarPrev      = root.Q<Button>("pillar-prev");
    uiPillarNext      = root.Q<Button>("pillar-next");
    uiTopPrev         = root.Q<Button>("top-prev");
    uiTopNext         = root.Q<Button>("top-next");
    uiBalusterPrev    = root.Q<Button>("baluster-prev");
    uiBalusterNext    = root.Q<Button>("baluster-next");
    uiNewPrev         = root.Q<Button>("new-prev");
    uiNewNext         = root.Q<Button>("new-next");
    uiWornPrev        = root.Q<Button>("worn-prev");
    uiWornNext        = root.Q<Button>("worn-next");
    uiFullDetail      = root.Q<Toggle>("full-detail-toggle");
    uiPillarPreview   = root.Q<VisualElement>("pillar-preview");
    uiPillarLock      = root.Q<VisualElement>("pillar-lock");
    uiTopPreview      = root.Q<VisualElement>("top-preview");
    uiTopLock         = root.Q<VisualElement>("top-lock");
    uiBalusterPreview = root.Q<VisualElement>("baluster-preview");
    uiBalusterLock    = root.Q<VisualElement>("baluster-lock");
    uiNewPreview      = root.Q<VisualElement>("new-preview");
    uiNewLock         = root.Q<VisualElement>("new-lock");
    uiWornPreview     = root.Q<VisualElement>("worn-preview");
    uiWornLock        = root.Q<VisualElement>("worn-lock");
    uiPillarName      = root.Q<Label>("pillar-name");
    uiPillarDesc      = root.Q<Label>("pillar-desc");
    uiTopName         = root.Q<Label>("top-name");
    uiTopDesc         = root.Q<Label>("top-desc");
    uiTopEmpty        = root.Q<Label>("top-empty");
    uiBalusterName    = root.Q<Label>("baluster-name");
    uiNewName         = root.Q<Label>("new-name");
    uiWornName        = root.Q<Label>("worn-name");
    uiTextureDesc     = root.Q<Label>("texture-desc");
    uiLiteHint        = root.Q<Label>("lite-hint");

    uiBuildBtn.clicked  += () => { if (buildMode) StopBuildMode(); else StartBuildMode(); RefreshUi(); };
    uiUpsellBtn.clicked += () => Application.OpenURL(FullVersionUrl);

    uiPillarPrev.clicked   += () => { CyclePillarVariant(-1); RefreshUi(); };
    uiPillarNext.clicked   += () => { CyclePillarVariant(+1); RefreshUi(); };
    uiTopPrev.clicked      += () => { CycleTop(-1);           RefreshUi(); };
    uiTopNext.clicked      += () => { CycleTop(+1);           RefreshUi(); };
    uiBalusterPrev.clicked += () => { CycleBalusterStyle(-1); RefreshUi(); };
    uiBalusterNext.clicked += () => { CycleBalusterStyle(+1); RefreshUi(); };
    uiNewPrev.clicked      += () => { CycleFinish(false, -1); RefreshUi(); };
    uiNewNext.clicked      += () => { CycleFinish(false, +1); RefreshUi(); };
    uiWornPrev.clicked     += () => { CycleFinish(true,  -1); RefreshUi(); };
    uiWornNext.clicked     += () => { CycleFinish(true,  +1); RefreshUi(); };

    uiFullDetail.RegisterValueChangedCallback(evt =>
    {
        fullDetailMode = evt.newValue;
        ApplyFullDetailToAllBalustrades(fullDetailMode);
    });

    // Clicking a locked preview opens the paid listing. Clicks that originate on
    // an arrow are ignored here so the arrow only cycles.
    uiPillarPreview.RegisterCallback<ClickEvent>(evt =>
    {
        if (evt.target is Button) return;
        if (!IsPillarVariantAvailable(previewVariant)) Application.OpenURL(FullVersionUrl);
    });
    uiTopPreview.RegisterCallback<ClickEvent>(evt =>
    {
        if (evt.target is Button) return;
        if (!IsTopAvailable(topBrowseIndex)) Application.OpenURL(FullVersionUrl);
    });
    uiBalusterPreview.RegisterCallback<ClickEvent>(evt =>
    {
        if (evt.target is Button) return;
        if (!IsBalusterAvailable(balusterBrowseIndex)) Application.OpenURL(FullVersionUrl);
    });

    // The finish columns are picked by clicking their image - no radio buttons.
    uiNewPreview.RegisterCallback<ClickEvent>(evt =>
    {
        if (evt.target is Button) return;
        if (!IsFinishAvailable(false, newPreviewIndex)) { Application.OpenURL(FullVersionUrl); return; }
        SetTextureWorn(false);
    });
    uiWornPreview.RegisterCallback<ClickEvent>(evt =>
    {
        if (evt.target is Button) return;
        if (!IsFinishAvailable(true, wornPreviewIndex)) { Application.OpenURL(FullVersionUrl); return; }
        SetTextureWorn(true);
    });

    LoadPreviewTextures();
    RefreshUi();

    // IMGUI re-read the scene on every OnGUI frame. UI Toolkit does not redraw
    // by itself, so that polling moves onto a slow timer here.
    root.schedule.Execute(RefreshUi).Every(250);
}

// ---- carousel actions ------------------------------------------------------
// All four carousels step through EVERY slot so a LITE user can see what the
// full version adds. A locked slot is preview-only, and without a balustrade in
// the scene the arrows only browse - there is nothing to convert then.
void CyclePillarVariant(int dir)
{
    int n = System.Enum.GetValues(typeof(ContinueVariant)).Length;
    previewVariant = (ContinueVariant)((((int)previewVariant + dir) % n + n) % n);

    if (!IsPillarVariantAvailable(previewVariant)) return;
    if (previewVariant == selectedVariant) return;
    if (GetUiTargetBalustradeRoot() == null) return;

    var prev = selectedVariant;
    selectedVariant = previewVariant;
    lastVariantSeen = (int)selectedVariant;
    SwitchPillarVariant(prev, selectedVariant);
}

void SwitchPillarVariant(ContinueVariant from, ContinueVariant to)
{
    if (from == to) return;

    // Availability here means "the hierarchy can be converted", not "it ships":
    // V1 rails can become V2 and vice versa.
    bool canSwap = to == ContinueVariant.V2 ? variantV2Available : variantV1Available;
    if (!canSwap) return;

    var root = GetUiTargetBalustradeRoot();
    if (root == null) return;

    // ReplaceBalustradeVariant destroys every element and rebuilds it, which would
    // drop whatever the user had selected. Parent and sibling index survive the
    // swap, so remember those and reselect the new elements afterwards.
    bool rootWasSelected = false;
    var picked = new List<(Transform parent, int sibling)>();
    foreach (var o in Selection.objects)
    {
        var go = o as GameObject;
        if (!go) continue;
        if (go == root) { rootWasSelected = true; continue; }
        if (FindBalustradeRootFromSelection(go) != root) continue;
        if (go.transform.parent) picked.Add((go.transform.parent, go.transform.GetSiblingIndex()));
    }

    if (to == ContinueVariant.V2)
    {
        ReplaceBalustradeVariant("V1", "V2");
        RemoveAllTopsFromSelectedBalustrade(); // V2 must not use tops
    }
    else
    {
        ReplaceBalustradeVariant("V2", "V1");

        if (topPreviewIndex < TopPrefabNames.Length)
            ApplyTopToSelectedBalustrade(topPreviewIndex);
        else
            RemoveAllTopsFromSelectedBalustrade();
    }

    RestoreSelectionAfterVariantSwap(root, rootWasSelected, picked);
}

void RestoreSelectionAfterVariantSwap(GameObject root, bool rootWasSelected,
                                      List<(Transform parent, int sibling)> picked)
{
    if (!rootWasSelected && picked.Count == 0) return;

    var objs = new List<Object>();
    if (rootWasSelected && root) objs.Add(root);

    foreach (var (parent, sibling) in picked)
    {
        if (!parent || sibling < 0 || sibling >= parent.childCount) continue;
        var go = parent.GetChild(sibling).gameObject;
        if (go && !objs.Contains(go)) objs.Add(go);
    }

    if (objs.Count > 0) Selection.objects = objs.ToArray();
}

void CycleTop(int dir)
{
    int n = topPreviews.Length + 1; // last slot is "No Tops"
    topBrowseIndex = ((topBrowseIndex + dir) % n + n) % n;

    if (!IsTopAvailable(topBrowseIndex)) return;
    if (selectedVariant != ContinueVariant.V1) return;
    if (GetUiTargetBalustradeRoot() == null) return;

    topPreviewIndex = topBrowseIndex;
    lastTopSeen = topPreviewIndex;

    if (topPreviewIndex < TopPrefabNames.Length)
        ApplyTopToSelectedBalustrade(topPreviewIndex);
    else
        RemoveAllTopsFromSelectedBalustrade();
}

void CycleBalusterStyle(int dir)
{
    int n = balusterStylePreviews.Length;
    balusterBrowseIndex = ((balusterBrowseIndex + dir) % n + n) % n;

    if (!IsBalusterAvailable(balusterBrowseIndex)) return;
    if (balusterBrowseIndex == balusterStyleIndex) return;
    if (GetUiTargetBalustradeRoot() == null) return;

    int prev = balusterStyleIndex;
    balusterStyleIndex = balusterBrowseIndex;
    lastBalusterSeen = balusterStyleIndex;
    ReplaceBalusterStyle(prev + 1, balusterStyleIndex + 1, selectedVariant);
}

void CycleFinish(bool worn, int dir)
{
    int p = (((worn ? wornPreviewIndex : newPreviewIndex) + dir) % 4 + 4) % 4;
    if (worn) wornPreviewIndex = p; else newPreviewIndex = p;

    if (!IsFinishAvailable(worn, p)) return;

    if (worn)
    {
        string oldToken = "worn" + (wornTextureIndex + 1);
        wornTextureIndex = p;
        lastWornIdxSeen  = p;
        ApplyTextureVariantToSelectedBalustrade(oldToken, "worn" + (p + 1));
    }
    else
    {
        string oldToken = "new" + (newTextureIndex + 1);
        newTextureIndex = p;
        lastNewIdxSeen  = p;
        ApplyTextureVariantToSelectedBalustrade(oldToken, "new" + (p + 1));
    }
}

void SetTextureWorn(bool worn)
{
    // Without a balustrade there is nothing to apply to, and RefreshUi would reset
    // the state on the next tick anyway - so the click would only flicker.
    if (GetUiTargetBalustradeRoot() == null) return;
    if (textureVariantIsWorn == worn) { RefreshUi(); return; }

    string from = worn ? "new"  + (newTextureIndex  + 1) : "worn" + (wornTextureIndex + 1);
    string to   = worn ? "worn" + (wornTextureIndex + 1) : "new"  + (newTextureIndex  + 1);
    ApplyTextureVariantToSelectedBalustrade(from, to);
    textureVariantIsWorn = worn;
    RefreshUi();
}

// Everything the old OnGUI recomputed per frame, gathered in one place.
void RefreshUi()
{
    if (uiBuildBtn == null) return; // CreateGUI has not run yet

    CleanupFinalizedBalustrades();
    UpdateContinueBuildState();

    var uiRoot = GetUiTargetBalustradeRoot();
    bool hasRoot = uiRoot != null;
    bool rootChanged = uiRoot != lastUiRootSeen;
    lastUiRootSeen = uiRoot;

    if (uiRoot)
    {
        ApplyUiStateFromBalustradeRoot(uiRoot);
        UpdateVariantAvailabilityFromHierarchy();
        UpdateBalusterStyleFromHierarchy();
        UpdateTextureVariantFromHierarchy();
    }
    else if (!buildMode && !continueAnchorActive)
    {
        // Defaults only when NOT building (Continue Build clears selection on purpose)
        selectedVariant = ContinueVariant.V1;
        topPreviewIndex = topPreviews.Length;
        balusterStyleIndex = 0;
        textureVariantIsWorn = false;
        newTextureIndex = 0;
        wornTextureIndex = 0;
    }

    SyncBrowseIndices(rootChanged);

    // ---- build ----
    uiBuildBtn.text = buildMode ? "Stop Build Mode" : "Start Build Mode";
    uiBuildBtn.EnableInClassList("btn-primary", !buildMode);
    uiBuildBtn.EnableInClassList("btn-stop", buildMode);
    uiFullDetail.SetValueWithoutNotify(fullDetailMode);

    // ---- pillar variant ----
    bool pillarLocked = !IsPillarVariantAvailable(previewVariant);
    SetPreviewImage(uiPillarPreview, PillarPreviewOf(previewVariant));
    SetLockBadge(uiPillarLock, pillarLocked);
    uiPillarName.text = pillarLocked
        ? PillarNameOf(previewVariant) + "  (Full Version)"
        : PillarNameOf(previewVariant);
    uiPillarName.EnableInClassList("preview-caption-locked", pillarLocked);
    uiPillarDesc.text = hasRoot
        ? "The arrows convert the selection in place."
        : "Select a balustrade to convert it.";

    // ---- top ----
    bool topIsNone  = topBrowseIndex >= TopPrefabNames.Length;
    bool topLocked  = !IsTopAvailable(topBrowseIndex);
    bool topsUsable = selectedVariant == ContinueVariant.V1;
    SetPreviewImage(uiTopPreview, topIsNone ? null : TopPreviewAt(topBrowseIndex));
    uiTopEmpty.EnableInClassList("hidden", !topIsNone);
    SetLockBadge(uiTopLock, topLocked);
    uiTopName.text = topIsNone
        ? "No Tops"
        : "Top #" + (topBrowseIndex + 1) + (topLocked ? "  (Full Version)" : "");
    uiTopName.EnableInClassList("preview-caption-locked", topLocked);
    uiTopPrev.SetEnabled(topsUsable);
    uiTopNext.SetEnabled(topsUsable);
    uiTopDesc.text = topsUsable
        ? "Cycle to No Tops to remove them."
        : "Variant V2 carries no rail tops.";

    // ---- baluster style ----
    bool balusterLocked = !IsBalusterAvailable(balusterBrowseIndex);
    SetPreviewImage(uiBalusterPreview, BalusterPreviewAt(balusterBrowseIndex));
    SetLockBadge(uiBalusterLock, balusterLocked);
    uiBalusterName.text = "Baluster Style #" + (balusterBrowseIndex + 1)
                        + (balusterLocked ? "  (Full Version)" : "");
    uiBalusterName.EnableInClassList("preview-caption-locked", balusterLocked);

    // ---- finishes ----
    // The active column is marked by its border, since the radios are gone.
    uiNewPreview.EnableInClassList("preview-active", !textureVariantIsWorn);
    uiWornPreview.EnableInClassList("preview-active", textureVariantIsWorn);

    bool newLocked = !IsFinishAvailable(false, newPreviewIndex);
    SetPreviewImage(uiNewPreview, FinishPreviewAt(false, newPreviewIndex));
    SetLockBadge(uiNewLock, newLocked);
    uiNewName.text = "New #" + (newPreviewIndex + 1) + (newLocked ? "  (Full)" : "");
    uiNewName.EnableInClassList("preview-caption-locked", newLocked);

    bool wornLocked = !IsFinishAvailable(true, wornPreviewIndex);
    SetPreviewImage(uiWornPreview, FinishPreviewAt(true, wornPreviewIndex));
    SetLockBadge(uiWornLock, wornLocked);
    uiWornName.text = "Worn #" + (wornPreviewIndex + 1) + (wornLocked ? "  (Full)" : "");
    uiWornName.EnableInClassList("preview-caption-locked", wornLocked);

    // Only the controls are disabled without a balustrade, never the whole card -
    // the padlocks must stay at full opacity so they keep reading as a sales cue.
    uiNewPrev.SetEnabled(hasRoot && !textureVariantIsWorn);
    uiNewNext.SetEnabled(hasRoot && !textureVariantIsWorn);
    uiWornPrev.SetEnabled(hasRoot && textureVariantIsWorn);
    uiWornNext.SetEnabled(hasRoot && textureVariantIsWorn);
    uiTextureDesc.text = hasRoot
        ? "Click New or Worn to apply it. The arrows cycle that column's finish."
        : "Select a balustrade in the Scene View to change its finish.";

    // ---- upsell ----
    string lockedName = BrowsedLockedName();
    uiUpsellBtn.text = lockedName != null
        ? "Unlock " + lockedName + " - Get the Full Version"
        : "Get the Full Version  (all styles, tops & finishes)";
    uiLiteHint.text = "LITE version - " + _availBaluster.Count + " of 12 baluster styles, "
                    + _availTop.Count + " of " + TopPrefabNames.Length + " tops, "
                    + "1 finish.";
}

// Name of whatever locked slot the user is looking at, so the upsell button can
// say what that click would actually unlock. Null when nothing on screen is locked.
string BrowsedLockedName()
{
    if (!IsBalusterAvailable(balusterBrowseIndex)) return "Baluster Style #" + (balusterBrowseIndex + 1);
    if (!IsTopAvailable(topBrowseIndex))           return "Top #" + (topBrowseIndex + 1);
    if (!IsFinishAvailable(false, newPreviewIndex)) return "New #" + (newPreviewIndex + 1);
    if (!IsFinishAvailable(true, wornPreviewIndex)) return "Worn #" + (wornPreviewIndex + 1);
    if (!IsPillarVariantAvailable(previewVariant))  return PillarNameOf(previewVariant);
    return null;
}

static void SetPreviewImage(VisualElement ve, Texture2D tex)
{
    if (ve == null) return;
    ve.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground();
}

void SetLockBadge(VisualElement badge, bool locked)
{
    if (badge == null) return;
    badge.EnableInClassList("hidden", !locked);
    if (!locked) return;

    var tex = GetLockIcon();
    badge.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground();
    // The shipped padlock is drawn as authored; only the monochrome Unity
    // fallback gets tinted gold.
    badge.style.unityBackgroundImageTintColor = lockIconIsCustom ? Color.white : LockGoldCol;
}

}
} // namespace WB3DAssets.BalustradeModularSystem
