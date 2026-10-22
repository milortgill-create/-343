#!/usr/bin/env python3
"""
Deterministic Unity project scaffolding generator.

Unity needs a `.meta` sidecar file for every asset and folder inside `Assets/`.
Each `.meta` carries a GUID that other assets (scenes, prefabs, settings) use to
reference it.  This script generates those GUIDs deterministically from the asset
path, so the whole project can be regenerated reproducibly without the Unity
Editor (which is not available in this sandbox).

It also writes the small set of hand-authored ProjectSettings files and the
bootstrap scene (`Assets/_Game/Scenes/Sandbox.unity`).

Usage:
    python3 Tools/unity_project.py            # create missing files
    python3 Tools/unity_project.py --force    # rewrite generated files
    python3 Tools/unity_project.py --check    # verify everything is present
"""
from __future__ import annotations

import argparse
import hashlib
import os
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(REPO_ROOT, "Assets")
PROJECT_SETTINGS = os.path.join(REPO_ROOT, "ProjectSettings")

# ---------------------------------------------------------------------------
# Paths that matter for cross references
# ---------------------------------------------------------------------------
SCENE_PATH = "Assets/_Game/Scenes/Sandbox.unity"
BOOTSTRAP_SCRIPT = "Assets/_Game/Scripts/Runtime/GameBootstrap.cs"

UNITY_VERSION = "2022.3.22f1"

# Layers used by the game. Index 0..7 are Unity's built-ins (3, 6, 7 unused).
LAYERS = {
    8: "Player",
    9: "Dummy",
    10: "Prop",
    11: "World",
    12: "Held",
    13: "Hands",
    14: "Debris",
}

TAGS = ["Interactable", "Dummy", "SpawnPoint"]


def guid_for(rel_path: str) -> str:
    """Stable 32-hex Unity GUID derived from the repository-relative path."""
    digest = hashlib.md5(("sandbox3d-unity:" + rel_path.replace("\\", "/")).encode("utf-8")).hexdigest()
    return digest


def rel(path: str) -> str:
    return os.path.relpath(path, REPO_ROOT).replace("\\", "/")


# ---------------------------------------------------------------------------
# .meta templates
# ---------------------------------------------------------------------------
META_FOLDER = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

META_SCRIPT = """fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

META_SCENE = """fileFormatVersion: 2
guid: {guid}
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

META_NATIVE_ASSET = """fileFormatVersion: 2
guid: {guid}
NativeFormatImporter:
  externalObjects: {{}}
  mainObjectFileID: 11400000
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

META_GENERIC = """fileFormatVersion: 2
guid: {guid}
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


def meta_for(path: str) -> str:
    g = guid_for(rel(path))
    if os.path.isdir(path):
        return META_FOLDER.format(guid=g)
    ext = os.path.splitext(path)[1].lower()
    if ext == ".cs":
        return META_SCRIPT.format(guid=g)
    if ext == ".unity":
        return META_SCENE.format(guid=g)
    if ext in (".asset", ".prefab", ".mat", ".physicMaterial"):
        return META_NATIVE_ASSET.format(guid=g)
    return META_GENERIC.format(guid=g)


def write_metas(force: bool = False) -> int:
    created = 0
    # Collect every directory and file under Assets/
    targets = []
    for root, dirs, files in os.walk(ASSETS):
        dirs[:] = sorted(d for d in dirs if d not in {".git"})
        for d in dirs:
            targets.append(os.path.join(root, d))
        for f in sorted(files):
            if f.endswith(".meta"):
                continue
            targets.append(os.path.join(root, f))

    for path in targets:
        meta_path = path + ".meta"
        if os.path.exists(meta_path) and not force:
            continue
        with open(meta_path, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(meta_for(path))
        created += 1
    return created


# ---------------------------------------------------------------------------
# Scene
# ---------------------------------------------------------------------------
SCENE_TEMPLATE = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!29 &1
OcclusionCullingSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_OcclusionBakeSettings:
    smallestOccluder: 5
    smallestHole: 0.25
    backfaceThreshold: 100
  m_SceneGUID: {scene_guid}
  m_OcclusionCullingData: {{fileID: 0}}
--- !u!104 &2
RenderSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 9
  m_Fog: 0
  m_FogColor: {{r: 0.5, g: 0.5, b: 0.5, a: 1}}
  m_FogMode: 3
  m_FogDensity: 0.01
  m_LinearFogStart: 0
  m_LinearFogEnd: 300
  m_AmbientSkyColor: {{r: 0.42, g: 0.47, b: 0.55, a: 1}}
  m_AmbientEquatorColor: {{r: 0.25, g: 0.26, b: 0.28, a: 1}}
  m_AmbientGroundColor: {{r: 0.12, g: 0.11, b: 0.1, a: 1}}
  m_AmbientIntensity: 1
  m_AmbientMode: 1
  m_SubtractiveShadowColor: {{r: 0.42, g: 0.478, b: 0.627, a: 1}}
  m_SkyboxMaterial: {{fileID: 0}}
  m_HaloStrength: 0.5
  m_FlareStrength: 1
  m_FlareFadeSpeed: 3
  m_HaloTexture: {{fileID: 0}}
  m_SpotCookie: {{fileID: 10001, guid: 0000000000000000e000000000000000, type: 0}}
  m_DefaultReflectionMode: 0
  m_DefaultReflectionResolution: 128
  m_ReflectionBounces: 1
  m_ReflectionIntensity: 1
  m_CustomReflection: {{fileID: 0}}
  m_Sun: {{fileID: 0}}
  m_IndirectSpecularColor: {{r: 0, g: 0, b: 0, a: 1}}
  m_UseRadianceAmbientProbe: 0
--- !u!157 &3
LightmapSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 12
  m_GIWorkflowMode: 1
  m_GISettings:
    serializedVersion: 2
    m_BounceScale: 1
    m_IndirectOutputScale: 1
    m_AlbedoBoost: 1
    m_EnvironmentLightingMode: 0
    m_EnableBakedLightmaps: 0
    m_EnableRealtimeLightmaps: 0
  m_LightmapEditorSettings:
    serializedVersion: 12
    m_Resolution: 2
    m_BakeResolution: 40
    m_AtlasSize: 1024
    m_AO: 0
    m_AOMaxDistance: 1
    m_CompAOExponent: 1
    m_CompAOExponentDirect: 0
    m_ExtractAmbientOcclusion: 0
    m_Padding: 2
    m_LightmapParameters: {{fileID: 0}}
    m_LightmapsBakeMode: 1
    m_TextureCompression: 1
    m_FinalGather: 0
    m_FinalGatherFiltering: 1
    m_FinalGatherRayCount: 256
    m_ReflectionCompression: 2
    m_MixedBakeMode: 2
    m_BakeBackend: 1
    m_PVRSampling: 1
    m_PVRDirectSampleCount: 32
    m_PVRSampleCount: 512
    m_PVRBounces: 2
    m_PVREnvironmentSampleCount: 256
    m_PVREnvironmentReferencePointCount: 2048
    m_PVRFilteringMode: 1
    m_PVRDenoiserTypeDirect: 1
    m_PVRDenoiserTypeIndirect: 1
    m_PVRDenoiserTypeAO: 1
    m_PVRFilterTypeDirect: 0
    m_PVRFilterTypeIndirect: 0
    m_PVRFilterTypeAO: 0
    m_PVREnvironmentMIS: 1
    m_PVRCulling: 1
    m_PVRFilteringGaussRadiusDirect: 1
    m_PVRFilteringGaussRadiusIndirect: 5
    m_PVRFilteringGaussRadiusAO: 2
    m_PVRFilteringAtrousPositionSigmaDirect: 0.5
    m_PVRFilteringAtrousPositionSigmaIndirect: 2
    m_PVRFilteringAtrousPositionSigmaAO: 1
    m_ExportTrainingData: 0
    m_TrainingDataDestination: TrainingData
    m_LightProbeSampleCountMultiplier: 4
  m_LightingDataAsset: {{fileID: 0}}
  m_LightingSettings: {{fileID: 0}}
--- !u!196 &4
NavMeshSettings:
  serializedVersion: 2
  m_ObjectHideFlags: 0
  m_BuildSettings:
    serializedVersion: 3
    agentTypeID: 0
    agentRadius: 0.5
    agentHeight: 2
    agentSlope: 45
    agentClimb: 0.4
    ledgeDropHeight: 0
    maxJumpAcrossDistance: 0
    minRegionArea: 2
    manualCellSize: 0
    cellSize: 0.16666667
    manualTileSize: 0
    tileSize: 256
    buildHeightMesh: 0
    maxJobWorkers: 0
    preserveTilesOutsideBounds: 0
    debug:
      m_Flags: 0
  m_NavMeshData: {{fileID: 0}}
--- !u!1 &519420000
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: 519420001}}
  - component: {{fileID: 519420002}}
  m_Layer: 0
  m_Name: Bootstrap
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &519420001
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 519420000}}
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 0, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {{fileID: 0}}
  m_RootOrder: 0
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
--- !u!114 &519420002
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 519420000}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {bootstrap_guid}, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: 
--- !u!20 &519420003
Camera:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 519420004}}
  m_Enabled: 1
  serializedVersion: 2
  m_ClearFlags: 2
  m_BackGroundColor: {{r: 0.11, g: 0.12, b: 0.14, a: 1}}
  m_projectionMatrixMode: 1
  m_GateFitMode: 2
  m_FOVAxisMode: 0
  m_Iso: 200
  m_ShutterSpeed: 0.005
  m_Aperture: 16
  m_FocusDistance: 10
  m_FocalLength: 50
  m_BladeCount: 5
  m_Curvature: {{x: 2, y: 11}}
  m_BarrelClipping: 0.25
  m_Anamorphism: 0
  m_SensorSize: {{x: 36, y: 24}}
  m_LensShift: {{x: 0, y: 0}}
  m_NormalizedViewPortRect:
    serializedVersion: 2
    x: 0
    y: 0
    width: 1
    height: 1
  near clip plane: 0.05
  far clip plane: 120
  field of view: 65
  orthographic: 0
  orthographic size: 5
  m_Depth: 0
  m_CullingMask:
    serializedVersion: 2
    m_Bits: 4294967295
  m_RenderingPath: -1
  m_TargetTexture: {{fileID: 0}}
  m_TargetDisplay: 0
  m_TargetEye: 3
  m_HDR: 0
  m_AllowMSAA: 0
  m_AllowDynamicResolution: 0
  m_ForceIntoRT: 0
  m_OcclusionCulling: 1
  m_StereoConvergence: 10
  m_StereoSeparation: 0.022
"""

# NOTE: the camera above is an *editor preview* camera only. At runtime
# GameBootstrap builds its own camera rig (with hands + ragdoll parenting).
# The preview camera lives on its own GameObject so the scene is not empty
# when opened in the Editor.
SCENE_PREVIEW_CAMERA_GO = """--- !u!1 &519420004
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: 519420005}}
  - component: {{fileID: 519420003}}
  m_Layer: 0
  m_Name: EditorPreviewCamera
  m_TagString: MainCamera
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &519420005
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 519420004}}
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 1.65, z: -4}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {{fileID: 0}}
  m_RootOrder: 1
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
"""


def scene_text() -> str:
    body = SCENE_TEMPLATE.format(
        scene_guid=guid_for(SCENE_PATH),
        bootstrap_guid=guid_for(BOOTSTRAP_SCRIPT),
    )
    # Append the preview camera GameObject/Transform so both roots are complete.
    # Блок тоже проходит через format(), иначе в файл попадут двойные {{ }}.
    return body + SCENE_PREVIEW_CAMERA_GO.format()


def write_scene(force: bool = False) -> bool:
    path = os.path.join(REPO_ROOT, SCENE_PATH)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    if os.path.exists(path) and not force:
        return False
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(scene_text())
    return True


# ---------------------------------------------------------------------------
# ProjectSettings
# ---------------------------------------------------------------------------
def _layers_yaml() -> str:
    rows = []
    builtin = {0: "Default", 1: "TransparentFX", 2: "Ignore Raycast", 4: "Water", 5: "UI"}
    for i in range(32):
        name = LAYERS.get(i, builtin.get(i, ""))
        rows.append(f"  - {name}" if name else "  - ")
    tags = "\n".join(f"  - {t}" for t in TAGS)
    return (
        "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n"
        "--- !u!78 &1\nTagManager:\n  serializedVersion: 2\n  tags:\n"
        + tags
        + "\n  layers:\n"
        + "\n".join(rows)
        + "\n  m_SortingLayers:\n  - name: Default\n    uniqueID: 0\n    locked: 0\n"
    )


def _editor_build_settings() -> str:
    return (
        "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!1045 &1\nEditorBuildSettings:\n"
        "  m_ObjectHideFlags: 0\n  serializedVersion: 2\n  m_Scenes:\n  - enabled: 1\n"
        f"    path: {SCENE_PATH}\n    guid: {guid_for(SCENE_PATH)}\n  m_configObjects: {{}}\n"
    )


TIME_MANAGER = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!5 &1
TimeManager:
  m_ObjectHideFlags: 0
  Fixed Timestep: 0.02
  Maximum Allowed Timestep: 0.1
  m_TimeScale: 1
  Maximum Particle Timestep: 0.03
"""

DYNAMICS_MANAGER = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!55 &1
PhysicsManager:
  m_ObjectHideFlags: 0
  serializedVersion: 14
  m_Gravity: {x: 0, y: -9.81, z: 0}
  m_DefaultMaterial: {fileID: 0}
  m_BounceThreshold: 1.5
  m_DefaultMaxDepenetrationVelocity: 8
  m_SleepThreshold: 0.005
  m_DefaultContactOffset: 0.015
  m_DefaultSolverIterations: 6
  m_DefaultSolverVelocityIterations: 1
  m_QueriesHitBackfaces: 0
  m_QueriesHitTriggers: 0
  m_EnableAdaptiveForce: 0
  m_ClothInterCollisionDistance: 0
  m_ClothInterCollisionStiffness: 0
  m_ContactsGeneration: 1
  m_LayerCollisionMatrix: ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff
  m_SimulationMode: 0
  m_AutoSyncTransforms: 0
  m_ReuseCollisionCallbacks: 1
  m_InvokeCollisionCallbacks: 1
  m_ClothInterCollisionSettingsToggle: 0
  m_ClothGravity: {x: 0, y: -9.81, z: 0}
  m_ContactPairsMode: 0
  m_BroadphaseType: 0
  m_WorldBounds:
    m_Center: {x: 0, y: 0, z: 0}
    m_Extent: {x: 250, y: 250, z: 250}
  m_WorldSubdivisions: 8
  m_FrictionType: 0
  m_EnableEnhancedDeterminism: 0
  m_EnableUnifiedHeightmaps: 1
  m_ImprovedPatchFriction: 1
  m_SolverType: 0
  m_DefaultMaxAngularSpeed: 12
  m_FastMotionThreshold: 3.4028235e+38
"""

AUDIO_MANAGER = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!11 &1
AudioManager:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_Volume: 1
  Rolloff Scale: 1
  Doppler Factor: 1
  Default Speaker Mode: 2
  m_SampleRate: 0
  m_DSPBufferSize: 1024
  m_VirtualVoiceCount: 24
  m_RealVoiceCount: 16
  m_EnableOutputSuspension: 1
  m_SpatializerPlugin: 
  m_AmbisonicDecoderPlugin: 
  m_DisableAudio: 0
  m_VirtualizeEffects: 1
  m_RequestedDSPBufferSize: 1024
"""

GRAPHICS_SETTINGS = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!30 &1
GraphicsSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 15
  m_Deferred:
    m_Mode: 1
    m_Shader: {fileID: 69, guid: 0000000000000000f000000000000000, type: 0}
  m_DeferredReflections:
    m_Mode: 1
    m_Shader: {fileID: 74, guid: 0000000000000000f000000000000000, type: 0}
  m_ScreenSpaceShadows:
    m_Mode: 1
    m_Shader: {fileID: 64, guid: 0000000000000000f000000000000000, type: 0}
  m_DepthNormals:
    m_Mode: 1
    m_Shader: {fileID: 62, guid: 0000000000000000f000000000000000, type: 0}
  m_MotionVectors:
    m_Mode: 1
    m_Shader: {fileID: 75, guid: 0000000000000000f000000000000000, type: 0}
  m_LightHalo:
    m_Mode: 1
    m_Shader: {fileID: 105, guid: 0000000000000000f000000000000000, type: 0}
  m_LensFlare:
    m_Mode: 1
    m_Shader: {fileID: 102, guid: 0000000000000000f000000000000000, type: 0}
  m_VideoShadersIncludeMode: 2
  m_AlwaysIncludedShaders: []
  m_PreloadedShaders: []
  m_PreloadShadersBatchTimeLimit: -1
  m_SpritesDefaultMaterial: {fileID: 10754, guid: 0000000000000000f000000000000000, type: 0}
  m_CustomRenderPipeline: {fileID: 0}
  m_TransparencySortMode: 0
  m_TransparencySortAxis: {x: 0, y: 0, z: 1}
  m_DefaultRenderingPath: 1
  m_DefaultMobileRenderingPath: 1
  m_TierSettings: []
  m_LightmapStripping: 0
  m_FogStripping: 0
  m_InstancingStripping: 0
  m_BrgStripping: 0
  m_LightmapKeepPlain: 1
  m_LightmapKeepDirCombined: 1
  m_LightmapKeepDynamicPlain: 1
  m_LightmapKeepDynamicDirCombined: 1
  m_LightmapKeepShadowMask: 1
  m_LightmapKeepSubtractive: 1
  m_FogKeepLinear: 1
  m_FogKeepExp: 1
  m_FogKeepExp2: 1
  m_AlbedoSwatchInfos: []
  m_LightsUseLinearIntensity: 1
  m_LightsUseColorTemperature: 1
  m_DefaultRenderingLayerMask: 1
  m_LogWhenShaderIsCompiled: 0
  m_SRPDefaultSettings: {}
  m_CameraRelativeLightCulling: 0
  m_CameraRelativeShadowCulling: 0
"""

PHYSICS2D_SETTINGS = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!19 &1
Physics2DSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 5
  m_Gravity: {x: 0, y: -9.81}
  m_DefaultMaterial: {fileID: 0}
  m_VelocityIterations: 8
  m_PositionIterations: 3
  m_VelocityThreshold: 1
  m_MaxLinearCorrection: 0.2
  m_MaxAngularCorrection: 8
  m_MaxTranslationSpeed: 100
  m_MaxRotationSpeed: 360
  m_BaumgarteScale: 0.2
  m_BaumgarteTimeOfImpactScale: 0.75
  m_TimeToSleep: 0.5
  m_LinearSleepTolerance: 0.01
  m_AngularSleepTolerance: 2
  m_DefaultContactOffset: 0.01
  m_JobOptions:
    serializedVersion: 2
    useMultithreading: 0
    useConsistencySorting: 0
    m_InterpolationPosesPerJob: 100
    m_NewContactsPerJob: 30
    m_CollideContactsPerJob: 100
    m_ClearFlagsPerJob: 200
    m_ClearBodyForcesPerJob: 200
    m_SyncDiscreteFixturesPerJob: 50
    m_SyncContinuousFixturesPerJob: 50
    m_FindNearestContactsPerJob: 100
    m_UpdateTriggerContactsPerJob: 100
    m_IslandSolverCostThreshold: 100
    m_IslandSolverBodyCostScale: 1
    m_IslandSolverContactCostScale: 10
    m_IslandSolverJointCostScale: 10
    m_IslandSolverBodiesPerJob: 50
    m_IslandSolverContactsPerJob: 50
  m_SimulationMode: 0
  m_QueriesHitTriggers: 1
  m_QueriesStartInColliders: 1
  m_CallbacksOnDisable: 1
  m_ReuseCollisionCallbacks: 1
  m_AutoSyncTransforms: 0
  m_AlwaysShowColliders: 0
  m_ShowColliderSleep: 1
  m_ShowColliderContacts: 0
  m_ShowColliderAABB: 0
  m_ContactArrowScale: 0.2
  m_ColliderAwakeColor: {r: 0.5686275, g: 0.95686275, b: 0.54509807, a: 0.7529412}
  m_ColliderAsleepColor: {r: 0.5686275, g: 0.95686275, b: 0.54509807, a: 0.36078432}
  m_ColliderContactColor: {r: 1, g: 0, b: 1, a: 0.6862745}
  m_ColliderAABBColor: {r: 1, g: 1, b: 0, a: 0.2509804}
  m_LayerCollisionMatrix: ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff
"""

EDITOR_SETTINGS = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!159 &1
EditorSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 12
  m_ExternalVersionControlSupport: Visible Meta Files
  m_SerializationMode: 2
  m_LineEndingsForNewScripts: 1
  m_DefaultBehaviorMode: 0
  m_PrefabRegularEnvironment: {fileID: 0}
  m_PrefabUIEnvironment: {fileID: 0}
  m_SpritePackerMode: 0
  m_SpritePackerPaddingPower: 1
  m_Bc7TextureCompressor: 0
  m_EtcTextureCompressorBehavior: 1
  m_EtcTextureFastCompressor: 1
  m_EtcTextureNormalCompressor: 2
  m_EtcTextureBestCompressor: 4
  m_ProjectGenerationIncludedExtensions: txt;xml;fnt;cd;asmdef;asmref;rsp
  m_ProjectGenerationRootNamespace: Sandbox
  m_EnableTextureStreamingInEditMode: 1
  m_EnableTextureStreamingInPlayMode: 1
  m_AsyncShaderCompilation: 1
  m_CachingShaderPreprocessor: 1
  m_PrefabModeAllowAutoSave: 1
  m_EnterPlayModeOptionsEnabled: 0
  m_EnterPlayModeOptions: 3
  m_GameObjectNamingDigits: 1
  m_GameObjectNamingScheme: 0
  m_AssetNamingUsesSpace: 1
  m_InspectorUseIMGUIDefaultInspector: 0
  m_UseLegacyProbeSampleCount: 0
  m_SerializeInlineMappingsOnOneLine: 1
  m_DisableCookiesInLightmapper: 1
  m_AssetPipelineMode: 1
  m_RefreshImportMode: 0
  m_CacheServerMode: 0
  m_CacheServerEndpoint: 
  m_CacheServerNamespacePrefix: default
  m_CacheServerEnableDownload: 1
  m_CacheServerEnableUpload: 1
  m_CacheServerEnableAuth: 0
  m_CacheServerEnableTls: 0
  m_CacheServerValidationMode: 2
  m_CacheServerDownloadBatchSize: 128
  m_EnableEnlightenBakedGI: 0
"""


def _axis(name, neg="", pos="", alt_neg="", alt_pos="", gravity=1000.0, dead=0.001,
          sensitivity=1000.0, snap=0, invert=0, axis_type=0, axis=0, joy_num=0):
    return f"""  - serializedVersion: 3
    m_Name: {name}
    descriptiveName: 
    descriptiveNegativeName: 
    negativeButton: {neg}
    positiveButton: {pos}
    altNegativeButton: {alt_neg}
    altPositiveButton: {alt_pos}
    gravity: {gravity}
    dead: {dead}
    sensitivity: {sensitivity}
    snap: {snap}
    invert: {invert}
    type: {axis_type}
    axis: {axis}
    joyNum: {joy_num}
"""


def _input_manager() -> str:
    axes = [
        _axis("Horizontal", "left", "right", "a", "d", 3.0, 0.001, 3.0, 1),
        _axis("Vertical", "down", "up", "s", "w", 3.0, 0.001, 3.0, 1),
        _axis("Fire1", "", "left ctrl", "", "mouse 0", 1000.0, 0.001, 1000.0),
        _axis("Fire2", "", "left alt", "", "mouse 1", 1000.0, 0.001, 1000.0),
        _axis("Fire3", "", "left shift", "", "mouse 2", 1000.0, 0.001, 1000.0),
        _axis("Jump", "", "space", "", "", 1000.0, 0.001, 1000.0),
        # оси мыши — нужны для управления камерой на ПК (type 1 = mouse movement)
        _axis("Mouse X", gravity=0.0, dead=0.0, sensitivity=0.1, axis_type=1, axis=0),
        _axis("Mouse Y", gravity=0.0, dead=0.0, sensitivity=0.1, axis_type=1, axis=1),
        _axis("Mouse ScrollWheel", gravity=0.0, dead=0.0, sensitivity=0.1, axis_type=1, axis=2),
        _axis("Submit", "", "return", "", "space", 1000.0, 0.001, 1000.0),
        _axis("Cancel", "", "escape", "", "", 1000.0, 0.001, 1000.0),
    ]
    return ("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!13 &1\nInputManager:\n"
            "  m_ObjectHideFlags: 0\n  serializedVersion: 2\n  m_Axes:\n" + "".join(axes))


def write_project_settings(force: bool = False) -> list[str]:
    os.makedirs(PROJECT_SETTINGS, exist_ok=True)
    written = []
    files = {
        "ProjectVersion.txt": f"m_EditorVersion: {UNITY_VERSION}\n",
        "TagManager.asset": _layers_yaml(),
        "EditorBuildSettings.asset": _editor_build_settings(),
        "TimeManager.asset": TIME_MANAGER,
        "DynamicsManager.asset": DYNAMICS_MANAGER,
        "AudioManager.asset": AUDIO_MANAGER,
        "GraphicsSettings.asset": GRAPHICS_SETTINGS,
        "Physics2DSettings.asset": PHYSICS2D_SETTINGS,
        "EditorSettings.asset": EDITOR_SETTINGS,
        "InputManager.asset": _input_manager(),
    }
    for name, content in files.items():
        path = os.path.join(PROJECT_SETTINGS, name)
        if os.path.exists(path) and not force:
            continue
        with open(path, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(content)
        written.append(name)
    return written


MANIFEST = {
    "dependencies": {
        "com.unity.render-pipelines.universal": "14.0.8",
        "com.unity.ugui": "1.0.0",
        "com.unity.modules.androidjni": "1.0.0",
        "com.unity.modules.animation": "1.0.0",
        "com.unity.modules.audio": "1.0.0",
        "com.unity.modules.imageconversion": "1.0.0",
        "com.unity.modules.imgui": "1.0.0",
        "com.unity.modules.jsonserialize": "1.0.0",
        "com.unity.modules.particlesystem": "1.0.0",
        "com.unity.modules.physics": "1.0.0",
        "com.unity.modules.screencapture": "1.0.0",
        "com.unity.modules.ui": "1.0.0",
        "com.unity.modules.uielements": "1.0.0",
        "com.unity.modules.unitywebrequest": "1.0.0",
        "com.unity.modules.video": "1.0.0",
        "com.unity.modules.xr": "1.0.0",
    }
}


def write_manifest(force: bool = False) -> bool:
    import json

    path = os.path.join(REPO_ROOT, "Packages", "manifest.json")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    if os.path.exists(path) and not force:
        return False
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(MANIFEST, fh, indent=2)
        fh.write("\n")
    return True


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--force", action="store_true", help="rewrite generated files")
    ap.add_argument("--check", action="store_true", help="only verify presence")
    args = ap.parse_args(argv)

    if args.check:
        missing = []
        if not os.path.exists(os.path.join(REPO_ROOT, SCENE_PATH)):
            missing.append(SCENE_PATH)
        for root, dirs, files in os.walk(ASSETS):
            for f in files:
                if f.endswith(".meta"):
                    continue
                if not os.path.exists(os.path.join(root, f) + ".meta"):
                    missing.append(rel(os.path.join(root, f)))
        for name in ("ProjectVersion.txt", "TagManager.asset", "EditorBuildSettings.asset"):
            if not os.path.exists(os.path.join(PROJECT_SETTINGS, name)):
                missing.append("ProjectSettings/" + name)
        if missing:
            print("MISSING (%d):" % len(missing))
            for m in missing:
                print("  -", m)
            return 1
        print("OK: all assets have .meta, scene and settings present.")
        print("scene guid      =", guid_for(SCENE_PATH))
        print("bootstrap guid  =", guid_for(BOOTSTRAP_SCRIPT))
        return 0

    write_scene(force=args.force)
    write_manifest(force=args.force)
    ps = write_project_settings(force=args.force)
    n = write_metas(force=args.force)
    print(f"scene written / manifest written / project settings: {ps or 'up-to-date'} / meta files created: {n}")
    print("scene guid      =", guid_for(SCENE_PATH))
    print("bootstrap guid  =", guid_for(BOOTSTRAP_SCRIPT))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
