#!/usr/bin/env python3
"""
Статическая проверка Unity-проекта БЕЗ Unity Editor.

В песочнице нет Unity, поэтому проект проверяется независимым инструментом:
  1. у каждого ассета есть .meta, GUID уникальны и детерминированы;
  2. сцена и ProjectSettings-файлы парсятся как YAML, ссылки на скрипты валидны;
  3. Packages/manifest.json валиден и содержит URP;
  4. C# файлы сбалансированы по скобкам (с учётом строк, символов и комментариев);
  5. все объявленные типы уникальны и не конфликтуют с типами UnityEngine;
  6. каждый тип, используемый в коде, либо объявлен в проекте, либо в белом списке;
  7. слои в коде совпадают со слоями в TagManager.asset;
  8. файлы в UTF-8, без BOM, без CJK-вкраплений и без TODO/FIXME.

Запуск:  python3 Tools/validate_project.py [--strict]
Код возврата 0 — всё хорошо, 1 — найдены ошибки.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(REPO, "Assets")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import unity_project as up  # noqa: E402

ERRORS: list[str] = []
WARNINGS: list[str] = []
NOTES: list[str] = []


def err(msg: str) -> None:
    ERRORS.append(msg)


def warn(msg: str) -> None:
    WARNINGS.append(msg)


def note(msg: str) -> None:
    NOTES.append(msg)


# ---------------------------------------------------------------------------
# YAML: Unity использует собственные теги !u!NNN, учим PyYAML их понимать
# ---------------------------------------------------------------------------
def load_unity_yaml(text: str):
    import yaml

    class UnityLoader(yaml.SafeLoader):
        pass

    def multi_constructor(loader, tag_suffix, node):
        if isinstance(node, yaml.MappingNode):
            return loader.construct_mapping(node, deep=True)
        if isinstance(node, yaml.SequenceNode):
            return loader.construct_sequence(node, deep=True)
        return loader.construct_scalar(node)

    UnityLoader.add_multi_constructor("tag:unity3d.com,2011:", multi_constructor)
    UnityLoader.add_multi_constructor("!", multi_constructor)
    return yaml.load(text, Loader=UnityLoader)


# ---------------------------------------------------------------------------
def iter_asset_files():
    for root, dirs, files in os.walk(ASSETS):
        dirs[:] = sorted(dirs)
        for f in sorted(files):
            yield os.path.join(root, f)


def iter_asset_dirs():
    for root, dirs, _files in os.walk(ASSETS):
        dirs[:] = sorted(dirs)
        for d in dirs:
            yield os.path.join(root, d)


def check_metas() -> dict[str, str]:
    """Проверяет .meta и возвращает карту guid -> путь."""
    guid_map: dict[str, str] = {}
    seen_paths: set[str] = set()

    for d in iter_asset_dirs():
        seen_paths.add(os.path.relpath(d, REPO).replace("\\", "/"))

    for path in iter_asset_files():
        rel = os.path.relpath(path, REPO).replace("\\", "/")
        seen_paths.add(rel)
        if path.endswith(".meta"):
            continue
        meta = path + ".meta"
        if not os.path.exists(meta):
            err(f"нет .meta для ассета: {rel}")
            continue
        text = open(meta, encoding="utf-8").read()
        m = re.search(r"^guid: ([0-9a-f]{32})$", text, re.M)
        if not m:
            err(f"в {rel}.meta нет корректного guid")
            continue
        guid = m.group(1)
        if guid in guid_map:
            err(f"дублирующийся GUID {guid}: {rel} и {guid_map[guid]}")
        guid_map[guid] = rel

        expected = up.guid_for(rel)
        if guid != expected:
            warn(f"{rel}: GUID {guid} != детерминированный {expected} (не критично, но пересоздастся)")

    # лишние .meta
    for path in iter_asset_files():
        if not path.endswith(".meta"):
            continue
        rel = os.path.relpath(path, REPO).replace("\\", "/")
        owner = rel[: -len(".meta")]
        if owner not in seen_paths:
            err(f"лишний .meta без ассета: {rel}")

    # .meta у папок
    for root, dirs, _files in os.walk(ASSETS):
        for d in dirs:
            p = os.path.join(root, d)
            rel = os.path.relpath(p, REPO).replace("\\", "/")
            if not os.path.exists(p + ".meta"):
                err(f"нет .meta для папки: {rel}")
    return guid_map


def check_scene(guid_map: dict[str, str]) -> None:
    scene = os.path.join(REPO, up.SCENE_PATH)
    if not os.path.exists(scene):
        err(f"сцена не найдена: {up.SCENE_PATH}")
        return
    text = open(scene, encoding="utf-8").read()
    if not text.startswith("%YAML 1.1"):
        err("сцена не начинается с '%YAML 1.1'")
    try:
        docs = list(yaml_docs(text))
    except Exception as e:  # noqa: BLE001
        err(f"сцена не парсится как YAML: {e}")
        return

    kinds = [d[0] for d in docs]
    for required in (29, 104, 157, 196, 1, 4, 114):
        if required not in kinds:
            err(f"в сцене нет обязательного блока !u!{required}")

    scripts = re.findall(r"m_Script: \{fileID: 11500000, guid: ([0-9a-f]{32}), type: 3\}", text)
    if not scripts:
        err("в сцене нет ни одного MonoBehaviour со ссылкой на скрипт")
    for g in scripts:
        if g not in guid_map:
            err(f"сцена ссылается на неизвестный GUID скрипта: {g}")
        else:
            note(f"сцена: MonoBehaviour → {guid_map[g]}")

    bootstrap_guid = up.guid_for(up.BOOTSTRAP_SCRIPT)
    if bootstrap_guid not in scripts:
        err("в сцене нет компонента GameBootstrap — при запуске ничего не построится")

    # у каждого GameObject должен быть Transform, и наоборот
    gos = re.findall(r"^--- !u!1 &(\d+)$", text, re.M)
    transforms = re.findall(r"^--- !u!4 &(\d+)$", text, re.M)
    if len(gos) != len(transforms):
        warn(f"в сцене {len(gos)} GameObject и {len(transforms)} Transform")
    for m in re.finditer(r"m_GameObject: \{fileID: (\d+)\}", text):
        if m.group(1) not in gos:
            err(f"ссылка на несуществующий GameObject fileID={m.group(1)}")


def yaml_docs(text: str):
    """Разбирает Unity-сцену на (classID, fileID, body) без PyYAML-тегов."""
    parts = re.split(r"^--- !u!(\d+) &(\d+)(?: stripped)?$", text, flags=re.M)
    # parts[0] — заголовок, дальше тройки
    for i in range(1, len(parts), 3):
        class_id = int(parts[i])
        file_id = int(parts[i + 1])
        body = parts[i + 2] if i + 2 < len(parts) else ""
        # проверка, что тело — валидный YAML-маппинг
        try:
            load_unity_yaml(body)
        except Exception as e:  # noqa: BLE001
            err(f"блок !u!{class_id} &{file_id} не парсится: {e}")
        yield class_id, file_id, body


def check_project_settings(guid_map: dict[str, str]) -> None:
    ps = os.path.join(REPO, "ProjectSettings")
    for name in ("ProjectVersion.txt", "TagManager.asset", "EditorBuildSettings.asset",
                 "TimeManager.asset", "DynamicsManager.asset", "GraphicsSettings.asset",
                 "InputManager.asset", "EditorSettings.asset"):
        if not os.path.exists(os.path.join(ps, name)):
            err(f"нет ProjectSettings/{name}")

    pv = os.path.join(ps, "ProjectVersion.txt")
    if os.path.exists(pv):
        m = re.search(r"m_EditorVersion: (\d+\.\d+\.\d+[a-z]\d+)", open(pv, encoding="utf-8").read())
        if not m:
            err("ProjectVersion.txt без m_EditorVersion")
        else:
            note("Unity версия: " + m.group(1))

    ebs = os.path.join(ps, "EditorBuildSettings.asset")
    if os.path.exists(ebs):
        text = open(ebs, encoding="utf-8").read()
        pm = re.search(r"path: (\S+)", text)
        gm = re.search(r"guid: ([0-9a-f]{32})", text)
        if not pm:
            err("EditorBuildSettings: нет сцены в сборке")
        elif pm.group(1) != up.SCENE_PATH:
            err(f"EditorBuildSettings указывает на {pm.group(1)}, ожидалось {up.SCENE_PATH}")
        elif not os.path.exists(os.path.join(REPO, pm.group(1))):
            err(f"сцена сборки не существует: {pm.group(1)}")
        if gm and gm.group(1) != up.guid_for(up.SCENE_PATH):
            err("EditorBuildSettings: GUID сцены не совпадает с .meta сцены")

    tag = os.path.join(ps, "TagManager.asset")
    if os.path.exists(tag):
        try:
            data = load_unity_yaml(open(tag, encoding="utf-8").read().split("--- !u!78 &1")[-1])
            layers = data.get("TagManager", {}).get("layers", [])
            for index, name in up.LAYERS.items():
                actual = layers[index] if index < len(layers) else ""
                if actual != name:
                    err(f"TagManager: слой {index} = '{actual}', ожидалось '{name}'")
            note(f"TagManager: слоёв {len(layers)} ({', '.join(up.LAYERS.values())})")
        except Exception as e:  # noqa: BLE001
            err(f"TagManager.asset не парсится: {e}")

    for name in ("TimeManager.asset", "DynamicsManager.asset", "GraphicsSettings.asset", "InputManager.asset"):
        p = os.path.join(ps, name)
        if not os.path.exists(p):
            continue
        try:
            load_unity_yaml(open(p, encoding="utf-8").read())
        except Exception as e:  # noqa: BLE001
            err(f"{name} не парсится как YAML: {e}")


def check_manifest() -> None:
    p = os.path.join(REPO, "Packages", "manifest.json")
    if not os.path.exists(p):
        err("нет Packages/manifest.json")
        return
    try:
        data = json.load(open(p, encoding="utf-8"))
    except Exception as e:  # noqa: BLE001
        err(f"manifest.json невалиден: {e}")
        return
    deps = data.get("dependencies", {})
    if "com.unity.render-pipelines.universal" not in deps:
        err("в manifest.json нет URP (com.unity.render-pipelines.universal)")
    else:
        v = deps["com.unity.render-pipelines.universal"]
        if not v.startswith("14."):
            warn(f"URP версии {v} — для Unity 2022.3 LTS ожидается 14.x")
        note(f"URP {v}")
    if "com.unity.inputsystem" in deps:
        warn("подключён Input System, но код использует legacy Input — возможен конфликт activeInputHandler")
    for mod, ver in deps.items():
        if mod.startswith("com.unity.modules.") and ver != "1.0.0":
            err(f"модуль {mod} должен иметь версию 1.0.0, а не {ver}")
    note(f"Пакетов в manifest.json: {len(deps)}")


# ---------------------------------------------------------------------------
# C#
# ---------------------------------------------------------------------------
KEYWORDS_TYPES = re.compile(
    r"\b(?:public|private|protected|internal|static|sealed|abstract|partial|readonly)\b")


def strip_cs(source: str) -> str:
    """Убирает комментарии и содержимое строк, сохраняя скобки кода."""
    out = []
    i = 0
    n = len(source)
    while i < n:
        c = source[i]
        nxt = source[i + 1] if i + 1 < n else ""
        if c == "/" and nxt == "/":
            while i < n and source[i] != "\n":
                i += 1
            continue
        if c == "/" and nxt == "*":
            i += 2
            while i < n - 1 and not (source[i] == "*" and source[i + 1] == "/"):
                i += 1
            i += 2
            continue
        if c == "@" and nxt == '"':
            i += 2
            while i < n:
                if source[i] == '"':
                    if i + 1 < n and source[i + 1] == '"':
                        i += 2
                        continue
                    i += 1
                    break
                i += 1
            out.append('""')
            continue
        if c == "$" and nxt == '"':
            # интерполированная строка: учитываем вложенные фигурные скобки кода
            i += 2
            depth = 0
            buf = []
            while i < n:
                ch = source[i]
                if ch == '"' and depth == 0:
                    i += 1
                    break
                if ch == "{":
                    if i + 1 < n and source[i + 1] == "{":
                        i += 2
                        continue
                    depth += 1
                    i += 1
                    continue
                if ch == "}":
                    depth -= 1
                    i += 1
                    continue
                if depth > 0:
                    buf.append(ch)
                i += 1
            out.append('""' + "".join(buf))
            continue
        if c == '"':
            i += 1
            while i < n:
                if source[i] == "\\":
                    i += 2
                    continue
                if source[i] == '"':
                    i += 1
                    break
                i += 1
            out.append('""')
            continue
        if c == "'":
            i += 1
            while i < n:
                if source[i] == "\\":
                    i += 2
                    continue
                if source[i] == "'":
                    i += 1
                    break
                i += 1
            out.append("''")
            continue
        out.append(c)
        i += 1
    return "".join(out)


UNITY_TYPES = set("""
GameObject Transform RectTransform Component MonoBehaviour Behaviour ScriptableObject Object
Camera Light ReflectionProbe Rigidbody Rigidbody2D CharacterController Collider BoxCollider
SphereCollider CapsuleCollider MeshCollider ConfigurableJoint CharacterJoint JointDrive
SoftJointLimit SoftJointLimitSpring JointProjectionMode ConfigurableJointMotion RotationDriveMode
Vector2 Vector3 Vector4 Quaternion Color Color32 Bounds Ray RaycastHit Matrix4x4 Mathf Rect
RectOffset GUIContent GUIStyle GUIStyleState GUIContent TempCanvas CameraClearFlags
PhysicMaterial FrictionCombine FrictionCombineMode ContactPoint PhysicsScene PhysicsMaterial
RigidbodyConstraints RigidbodySleepMode ParticleSystemCollisionType
Material Shader Texture Texture2D TextureFormat Sprite Mesh MeshRenderer MeshFilter Renderer
AnimationCurve WaitForSeconds WaitForSecondsRealtime Coroutine Canvas CanvasScaler GraphicRaycaster
Image Text Button Slider Toggle EventSystem StandaloneInputModule RectTransformUtility
ParticleSystem ParticleSystemRenderer AudioListener AudioSource AudioClip AudioRolloffMode
Screen SleepTimeout Application SystemInfo QualitySettings RenderSettings Physics Time Debug
Random Resources Cursor CursorLockMode PrimitiveType LayerMask QueryTriggerInteraction
CollisionDetectionMode RigidbodyInterpolation ForceMode ShadowCastingMode ShadowQuality
AmbientMode RenderPipelineAsset GraphicsSettings ReflectionProbeMode ReflectionProbeRefreshMode
ReflectionProbeTimeSlicingMode ReflectionProbeClearFlags LightType LightShadows LightRenderMode
MaterialPropertyBlock MaterialGlobalIlluminationFlags TextureWrapMode FilterMode RenderQueue
BlendMode HorizontalWrapMode VerticalWrapMode TextAnchor CanvasGroup Font HideFlags Scene
""".split())

EDITOR_TYPES = set("""
EditorApplication EditorUtility AssetDatabase SerializedObject SerializedProperty MenuItem
EditorBuildSettings EditorBuildSettingsScene EditorSceneManager SceneView BuildPipeline
BuildPlayerOptions BuildReport BuildSummary BuildResult BuildTarget BuildTargetGroup BuildOptions
PlayerSettings UIOrientation AndroidSdkVersions AndroidArchitecture ScriptingImplementation
ManagedStrippingLevel GraphicsDeviceType ColorSpace AccelerometerFrequency EditorUserBuildSettings
MobileTextureSubtarget BlitType AndroidCreateSymbols
""".split())

DOTNET_TYPES = set("""
String Int32 Single Boolean Exception Action Func List Dictionary HashSet StringBuilder Math
Type BindingFlags PropertyInfo MethodInfo IDisposable IEnumerable DateTime Stopwatch Guid
ArgumentNullException ArgumentException InvalidOperationException NullReferenceException
NotSupportedException StringComparison CultureInfo
""".split())


def check_csharp() -> None:
    declared: dict[str, str] = {}
    files = [p for p in iter_asset_files() if p.endswith(".cs")]
    if not files:
        err("в Assets нет ни одного .cs файла")
        return

    for path in files:
        rel = os.path.relpath(path, REPO).replace("\\", "/")
        raw = open(path, "rb").read()
        try:
            text = raw.decode("utf-8")
        except UnicodeDecodeError as e:
            err(f"{rel}: не UTF-8 ({e})")
            continue
        if raw.startswith(b"\xef\xbb\xbf"):
            err(f"{rel}: BOM в начале файла")
        if b"\r\n" in raw:
            warn(f"{rel}: CRLF-переводы строк (Unity предпочитает LF)")
        if re.search(r"[\u4e00-\u9fff\u3040-\u30ff]", text):
            err(f"{rel}: найдены CJK-символы")
        if re.search(r"\b(TODO|FIXME|XXX)\b", text):
            warn(f"{rel}: есть TODO/FIXME")

        code = strip_cs(text)

        # баланс скобок
        for open_c, close_c, label in (("{", "}", "фигурные"), ("(", ")", "круглые"), ("[", "]", "квадратные")):
            bal = 0
            minimum = 0
            for ch in code:
                if ch == open_c:
                    bal += 1
                elif ch == close_c:
                    bal -= 1
                    minimum = min(minimum, bal)
            if bal != 0:
                err(f"{rel}: несбалансированные {label} скобки (баланс {bal:+d})")
            if minimum < 0:
                err(f"{rel}: {label} скобки закрываются раньше, чем открываются")

        # пространства имён
        namespaces = set(re.findall(r"\bnamespace\s+([\w.]+)", code))
        for ns in namespaces:
            if ns not in {"Sandbox", "Sandbox.EditorTools"}:
                err(f"{rel}: неожиданное пространство имён '{ns}' (ожидается Sandbox / Sandbox.EditorTools)")
            for bad in ("Physics", "Audio", "UI", "Random", "Debug", "Application", "Input"):
                if ns.endswith("." + bad) or ns == bad:
                    err(f"{rel}: пространство имён '{ns}' конфликтует с UnityEngine.{bad}")

        # объявления типов
        for m in re.finditer(r"\b(?:class|struct|interface|enum)\s+([A-Z]\w*)", code):
            name = m.group(1)
            if name in declared:
                err(f"тип '{name}' объявлен дважды: {declared[name]} и {rel}")
            declared[name] = rel
            if name in UNITY_TYPES or name in EDITOR_TYPES:
                err(f"{rel}: тип '{name}' конфликтует с UnityEngine/UnityEditor")

        # using'и только из известного набора
        for m in re.finditer(r"^\s*using\s+([\w.]+)\s*;", code, re.M):
            u = m.group(1)
            allowed_prefixes = ("System", "UnityEngine", "UnityEditor", "Sandbox")
            if not u.startswith(allowed_prefixes):
                warn(f"{rel}: необычный using '{u}'")

    # использование необъявленных типов
    used: dict[str, set[str]] = {}
    patterns = [
        re.compile(r"\bAddComponent<\s*([A-Z]\w*)\s*>"),
        re.compile(r"\bGetComponent<\s*([A-Z]\w*)\s*>"),
        re.compile(r"\bGetComponentInParent<\s*([A-Z]\w*)\s*>"),
        re.compile(r"\bnew\s+([A-Z]\w*)\s*\("),
        re.compile(r"\b:\s*([A-Z]\w*)\s*(?:,|\{|$)", re.M),
        re.compile(r"\bpublic\s+([A-Z]\w*)\s+\w+\s*[;=]"),
    ]
    for path in files:
        rel = os.path.relpath(path, REPO).replace("\\", "/")
        code = strip_cs(open(path, encoding="utf-8").read())
        for pat in patterns:
            for m in pat.finditer(code):
                name = m.group(1)
                if name in declared or name in UNITY_TYPES or name in EDITOR_TYPES or name in DOTNET_TYPES:
                    continue
                if re.match(r"^(I[A-Z]\w*|T|K|V)$", name):
                    continue
                if re.match(r"^[A-Z]\d$", name):  # F0/F2 — спецификаторы формата, не типы
                    continue
                if name == "PhysicsMaterial":
                    err(f"{rel}: 'PhysicsMaterial' есть только в Unity 6; в 2022.3 LTS — 'PhysicMaterial'")
                used.setdefault(name, set()).add(rel)

    if used:
        for name, where in sorted(used.items()):
            warn(f"тип '{name}' используется, но не объявлен в проекте и не в белом списке: {', '.join(sorted(where))}")

    note(f"C# файлов: {len(files)}, объявлено типов: {len(declared)}")
    check_layers_consistency(files)


def check_layers_consistency(files) -> None:
    """Сверяет имена слоёв в коде со слоями в TagManager.asset."""
    tag_path = os.path.join(REPO, "ProjectSettings", "TagManager.asset")
    if not os.path.exists(tag_path):
        return
    text = open(tag_path, encoding="utf-8").read()
    layers_block = text.split("layers:")[-1].split("m_SortingLayers")[0]
    configured = {i: line.strip()[2:].strip() for i, line in enumerate(layers_block.strip().splitlines())}
    configured_names = {v for v in configured.values() if v}

    for path in files:
        rel = os.path.relpath(path, REPO).replace("\\", "/")
        text = open(path, encoding="utf-8").read()
        for m in re.finditer(r'NameToLayer\(\s*"([^"]+)"\s*\)', text):
            if m.group(1) not in configured_names and m.group(1) != "Ignore Raycast":
                err(f"{rel}: используется слой '{m.group(1)}', которого нет в TagManager.asset")
        if re.search(r"\bclass\s+Layers\b", text):
            for m in re.finditer(r'public const string (\w+) = "([^"]+)"', text):
                if m.group(2) not in configured_names:
                    err(f"{rel}: константа слоя {m.group(1)} = '{m.group(2)}' отсутствует в TagManager.asset")


def check_enum_and_static_access() -> None:
    """
    Ловит опечатки вида InteractionKind.StrrikeWithItem и
    SandboxConfig.NonExistingConst: обращения к членам enum и static-классов
    проекта должны существовать.
    """
    files = [f for f in iter_asset_files() if f.endswith(".cs")]
    enums: dict[str, set] = {}
    statics: dict[str, set] = {}

    member_re = re.compile(
        r"public\s+(?:static\s+|readonly\s+|const\s+|override\s+|virtual\s+|abstract\s+|sealed\s+)*"
        r"[\w<>\[\]\.,\s\?]+?\s+(\w+)\s*(?:\(|=>|;|=|\{)")

    for path in files:
        text = open(path, encoding="utf-8").read()
        for m in re.finditer(r"enum\s+(\w+)\s*\{([^}]*)\}", text):
            body = m.group(2) + ","
            enums[m.group(1)] = set(re.findall(r"(\w+)\s*(?:=[^,]*)?,", body))
        for m in re.finditer(r"static\s+class\s+(\w+)", text):
            body = text[text.index("{", m.end()):]
            statics[m.group(1)] = set(member_re.findall(body))

    for path in files:
        rel = os.path.relpath(path, REPO).replace("\\", "/")
        text = open(path, encoding="utf-8").read()
        for name, members in enums.items():
            for m in re.finditer(rf"\b{name}\.(\w+)", text):
                if m.group(1) not in members:
                    err(f"{rel}: {name}.{m.group(1)} — нет такого члена enum "
                        f"(есть: {', '.join(sorted(members))})")
        for name, members in statics.items():
            for m in re.finditer(rf"\b{name}\.(\w+)", text):
                if m.group(1) not in members:
                    err(f"{rel}: {name}.{m.group(1)} — нет такого члена static-класса")


def check_workflows() -> None:
    wf_dir = os.path.join(REPO, ".github", "workflows")
    if not os.path.isdir(wf_dir):
        err("нет .github/workflows")
        return
    import yaml

    for name in sorted(os.listdir(wf_dir)):
        if not name.endswith((".yml", ".yaml")):
            continue
        p = os.path.join(wf_dir, name)
        try:
            data = yaml.safe_load(open(p, encoding="utf-8"))
        except Exception as e:  # noqa: BLE001
            err(f"{name}: невалидный YAML ({e})")
            continue
        if not isinstance(data, dict) or "jobs" not in data:
            err(f"{name}: нет секции jobs")
        # ключ 'on' в YAML 1.1 парсится как True
        trigger = data.get("on", data.get(True))
        if not trigger:
            err(f"{name}: нет триггеров (on:)")
        note(f"workflow {name}: jobs={', '.join(data.get('jobs', {}).keys())}")


def check_gitignore() -> None:
    p = os.path.join(REPO, ".gitignore")
    if not os.path.exists(p):
        err("нет .gitignore")
        return
    text = open(p, encoding="utf-8").read()
    normalized = re.sub(r"\[(\w)\w\]", r"\1", text.lower())
    for required in ("/library/", "/temp/", "/build/", "/obj/", "/logs/"):
        if required not in normalized:
            err(f".gitignore не содержит правила для {required}")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--strict", action="store_true", help="считать предупреждения ошибками")
    args = ap.parse_args()

    guid_map = check_metas()
    check_scene(guid_map)
    check_project_settings(guid_map)
    check_manifest()
    check_csharp()
    check_enum_and_static_access()
    check_workflows()
    check_gitignore()

    print("=" * 72)
    print("ПРОВЕРКА UNITY-ПРОЕКТА (без Unity Editor)")
    print("=" * 72)
    for n in NOTES:
        print("  [info]", n)
    for w in WARNINGS:
        print("  [warn]", w)
    for e in ERRORS:
        print("  [FAIL]", e)
    print("-" * 72)
    failed = len(ERRORS) + (len(WARNINGS) if args.strict else 0)
    print(f"Итог: ошибок {len(ERRORS)}, предупреждений {len(WARNINGS)} → "
          + ("FAIL" if failed else "PASS"))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
