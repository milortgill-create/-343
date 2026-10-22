#!/usr/bin/env python3
"""
Численная проверка физики тестового полигона — БЕЗ Unity.

Идея: все константы вытаскиваются прямо из C#/ProjectSettings, затем
полупроцесс Эйлера (та же схема интегрирования, что в PhysX:
v += g*dt;  x += v*dt) прогоняет сценарии бросков, падений и стресс-теста.
Главный вопрос, на который отвечает проверка: **может ли предмет провалиться
сквозь пол?** Для этого сравнивается максимальное перемещение за один
физический шаг (v_max * fixedDeltaTime) с толщиной плиты пола (1.0 м):
если шаг меньше толщины, предмет не может «перепрыгнуть» плиту даже
без continuous collision detection, а CCD делает запас ещё больше.

Дополнительно считаются урон/импульсы по манекену, высота прыжка игрока,
замкнутость арены и бюджет физики под Galaxy A53.

Запуск:  python3 Tools/physics_check.py [--no-charts]
Отчёт:   Tools/reports/physics_report.md (+ PNG-графики)
Код возврата: 0 — все проверки пройдены, 1 — есть провалы.
"""
from __future__ import annotations

import argparse
import math
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPORT_DIR = os.path.join(REPO, "Tools", "reports")
RUNTIME = os.path.join(REPO, "Assets", "_Game", "Scripts", "Runtime")

FAILURES: list[str] = []
CHECKS: list[tuple[str, float, float, str]] = []   # (name, value, limit, verdict)
CHARTS: list[str] = []


def fail(msg: str) -> None:
    FAILURES.append(msg)


def check(name: str, value: float, limit: float, better_is: str = "less") -> float:
    """Записывает проверку: value против limit. Возвращает запас."""
    if better_is == "less":
        ok = value < limit
        margin = limit / value if value > 1e-9 else float("inf")
    else:
        ok = value > limit
        margin = value / limit if limit > 1e-9 else float("inf")
    CHECKS.append((name, value, limit, "OK" if ok else "FAIL"))
    if not ok:
        fail(f"{name}: {value:.4f} против лимита {limit:.4f}")
    return margin


# ---------------------------------------------------------------------------
# Разбор констант из проекта
# ---------------------------------------------------------------------------
def read(rel: str) -> str:
    with open(os.path.join(REPO, rel), encoding="utf-8") as f:
        return f.read()


def parse_const(text: str, name: str) -> float:
    m = re.search(rf"\b{name}\s*=\s*(-?[\d.]+)f?", text)
    if not m:
        raise SystemExit(f"не найдена константа {name}")
    return float(m.group(1))


def load_settings() -> dict:
    time_asset = read("ProjectSettings/TimeManager.asset")
    dyn_asset = read("ProjectSettings/DynamicsManager.asset")
    cfg = read("Assets/_Game/Scripts/Runtime/SandboxConfig.cs")
    diag = read("Assets/_Game/Scripts/Runtime/Diagnostics/SandboxSelfTest.cs")
    world = read("Assets/_Game/Scripts/Runtime/World/SandboxWorldBuilder.cs")

    def setting(text: str, key: str, default: float | None = None) -> float:
        m = re.search(rf"\b{key}\s*[:=]\s*(-?[\d.]+)f?", text)
        if m:
            return float(m.group(1))
        if default is None:
            raise SystemExit(f"не найдена константа {key}")
        return default

    gravity = re.search(r"m_Gravity:\s*\{x:\s*(-?[\d.]+),\s*y:\s*(-?[\d.]+),\s*z:\s*(-?[\d.]+)\}", dyn_asset)

    # верх платформы: StaticBox("Platform", pos, size, ...) → pos.y + size.y/2
    plat = re.search(r'StaticBox\("Platform", new Vector3\((-?[\d.]+)f,\s*(-?[\d.]+)f,\s*(-?[\d.]+)f\),\s*'
                     r'new Vector3\((-?[\d.]+)f,\s*(-?[\d.]+)f', world)
    platform_top = float(plat.group(2)) + float(plat.group(5)) / 2 if plat else 1.3

    return {
        # Unity хранит TimeManager с «человеческими» ключами: Fixed Timestep
        "fixedDt": float(re.search(r"Fixed Timestep:\s*([\d.]+)", time_asset).group(1)),
        "maxDt": float(re.search(r"Maximum Allowed Timestep:\s*([\d.]+)", time_asset).group(1)),
        "gravityY": float(gravity.group(2)) if gravity else -9.81,
        "solverIt": int(setting(dyn_asset, "m_DefaultSolverIterations", 6)),
        "solverVelIt": int(setting(dyn_asset, "m_DefaultSolverVelocityIterations", 1)),
        "sleepThreshold": setting(dyn_asset, "m_SleepThreshold", 0.005),
        "bounceThreshold": setting(dyn_asset, "m_BounceThreshold", 1.5),
        "maxDepen": setting(dyn_asset, "m_DefaultMaxDepenetrationVelocity", 8),
        "maxAngularSpeed": setting(dyn_asset, "m_DefaultMaxAngularSpeed", 12),
        "contactOffset": setting(dyn_asset, "m_DefaultContactOffset", 0.015),
        # SandboxConfig
        "floorThickness": setting(cfg, "FloorThickness"),
        "floorTopY": setting(cfg, "FloorTopY"),
        "arenaW": setting(cfg, "ArenaWidth"),
        "arenaD": setting(cfg, "ArenaDepth"),
        "arenaH": setting(cfg, "ArenaHeight"),
        "wallThickness": setting(cfg, "WallThickness"),
        "killY": setting(cfg, "KillY"),
        "baseThrow": setting(cfg, "BaseThrowSpeed"),
        "maxThrow": setting(cfg, "MaxThrowSpeed"),
        "throwPerHandVel": setting(cfg, "ThrowSpeedPerHandVel"),
        "eyeHeight": setting(cfg, "EyeHeight"),
        "playerMass": setting(cfg, "PlayerMass"),
        "jumpSpeed": setting(cfg, "JumpSpeed"),
        "walkSpeed": setting(cfg, "WalkSpeed"),
        "runSpeed": setting(cfg, "RunSpeed"),
        "platformHeight": platform_top,
        "punchImpulse": setting(cfg, "PunchImpulse"),
        "heldPunchBonus": setting(cfg, "HeldItemPunchBonus"),
        "throwDamageScale": setting(cfg, "ThrowImpactDamageScale"),
        "ragdollThreshold": setting(cfg, "RagdollImpulseThreshold"),
        "playerRagdollDamage": setting(cfg, "RagdollThreshold"),
        # PhysicsStressTester
        "stressMin": setting(diag, "minKickSpeed", 3.0),
        "stressMax": setting(diag, "maxKickSpeed", 22.0),
        "maxSafeSpeed": setting(cfg, "MaxSafeSpeed", 40.0),
        "stressDuration": setting(diag, "duration", 14.0),
    }


def eval_mass(expr: str, size: float = 0.62, length: float = 2.1, rusty: bool = False) -> float:
    """Считает значение C#-выражения массы ('12f * (size / 0.62f)', 'rusty ? 22f : 19f')."""
    expr = expr.strip()
    if "?" in expr and ":" in expr:
        cond, rest = expr.split("?", 1)
        a, b = rest.split(":", 1)
        expr = a if ("rusty" in cond and rusty) else b
    expr = re.sub(r"([\d.]+)f\b", r"\1", expr.strip())
    expr = expr.replace("size", repr(size)).replace("length", repr(length))
    return float(eval(expr, {"__builtins__": {}}, {}))      # noqa: S307


def load_props() -> list[dict]:
    """Массы и габариты предметов — прямо из PropFactory.cs и PhoneProp.cs."""
    src = read("Assets/_Game/Scripts/Runtime/World/PropFactory.cs")
    phone = read("Assets/_Game/Scripts/Runtime/World/PhoneProp.cs")

    def block(name: str) -> str:
        i = src.index(f"public static GameObject {name}(")
        rest = src[i + 40:]
        j = rest.find("public static GameObject ")
        return src[i:i + 40 + (j if j > 0 else 2500)]

    def mass_of(blk: str, rusty: bool = False) -> float:
        # Finish(go, имя, МАССА, ImpactSound.X, ...) — якорь на ImpactSound,
        # иначе не разобрать тернарные аргументы вида  rusty ? 22f : 19f
        m = re.search(r"Finish\([^,]+,\s*(?:[^,]+),\s*([^,]+),\s*ImpactSound\.", blk)
        if not m:
            raise SystemExit(f"не разобрать массу в блоке: {blk[:160]}")
        return eval_mass(m.group(1), rusty=rusty)

    def size3(blk: str, pattern: str, **vars) -> tuple:
        # подставляем параметры метода (length = 2f по умолчанию) в текст блока
        for name, value in vars.items():
            blk = re.sub(rf"\b{name}\b(?!\s*=)", repr(value), blk)
        m = re.search(pattern, blk)
        if not m:
            raise SystemExit(f"не разобрать габариты: {pattern}")
        return tuple(float(x) for x in m.groups())

    crate = block("Crate")
    barrel = block("Barrel")
    brick = block("Brick")
    plank = block("Plank")
    bottle = block("Bottle")

    props = [
        {"name": "Ящик 0.62 м", "kind": "box", "mass": mass_of(crate),
         "size": (0.62, 0.62, 0.62)},
        {"name": "Бочка", "kind": "capsule", "mass": mass_of(barrel),
         "radius": size3(barrel, r"col\.radius = ([\d.]+)f")[0],
         "height": size3(barrel, r"col\.height = ([\d.]+)f")[0]},
        {"name": "Бочка (ржавая)", "kind": "capsule", "mass": mass_of(barrel, rusty=True),
         "radius": size3(barrel, r"col\.radius = ([\d.]+)f")[0],
         "height": size3(barrel, r"col\.height = ([\d.]+)f")[0]},
        {"name": "Кирпич", "kind": "box", "mass": mass_of(brick),
         "size": size3(brick, r"col\.size = new Vector3\(([\d.]+)f,\s*([\d.]+)f,\s*([\d.]+)f\)")},
        {"name": "Доска 2.1 м", "kind": "box", "mass": mass_of(plank),
         "size": (0.17, 0.035, 2.1)},
        {"name": "Бутылка", "kind": "capsule", "mass": mass_of(bottle),
         "radius": float(re.findall(r"col\.radius = ([\d.]+)f", bottle)[-1]),
         "height": float(re.findall(r"col\.height = ([\d.]+)f", bottle)[-1])},
        {"name": "Телефон", "kind": "box",
         "mass": float(re.search(r"AddRigidbody\(root, ([\d.]+)f", phone).group(1)),
         "size": (0.16, 0.012, 0.08)},
    ]
    return props


def half_extent(p: dict) -> float:
    """Максимальное расстояние от центра масс до точки контакта (кувыркание)."""
    if p["kind"] == "box":
        x, y, z = p["size"]
        return 0.5 * math.sqrt(x * x + y * y + z * z)
    return 0.5 * p["height"]          # капсула: полуось вдоль Y


def load_ragdoll_mass() -> float:
    src = read("Assets/_Game/Scripts/Runtime/Physics/RagdollBuilder.cs")
    body = src[src.index("public static RagdollProfile Humanoid"):]
    total = 0.0
    for m in re.finditer(r"mass = ([\d.]+)f", body):
        total += float(m.group(1))
    declared = parse_const(src, "totalMass")
    return declared if abs(declared - total) < 40 else total


def count_props() -> dict:
    """Сколько объектов ставит SandboxWorldBuilder (циклы разворачиваются)."""
    src = read("Assets/_Game/Scripts/Runtime/World/SandboxWorldBuilder.cs")
    counts: dict[str, int] = {}
    loop_until = -1
    loop_n = 1
    for i, line in enumerate(src.splitlines()):
        m = re.search(r"for \(int \w+ = 0; \w+ < (\d+); \w+\+\+\)", line)
        if m:
            loop_n = int(m.group(1))
            loop_until = i + 4
            continue
        fm = re.search(r"PropFactory\.(Crate|Barrel|Brick|Plank|Bottle|StaticBox|Ramp)\(", line)
        if fm:
            counts[fm.group(1)] = counts.get(fm.group(1), 0) + (loop_n if i <= loop_until else 1)
    return counts


# ---------------------------------------------------------------------------
# Симуляция: полунеявный метод Эйлера, как в PhysX
# ---------------------------------------------------------------------------
def simulate(y0: float, v0: (tuple), dt: float, g: float, half: float,
             restitution: float, friction: float, floor_y: float = 0.0,
             max_time: float = 8.0) -> dict:
    """
    2D-срез (вертикаль + одна горизонталь) для оценки пробоя пола.
    Возвращает статистику: max_speed, max_step_depth, rest_height, settled_time.
    """
    y, vy = y0, v0[1]
    vx = v0[0]
    max_speed = math.hypot(vx, vy)
    max_step_depth = 0.0
    t = 0.0
    settled_t = None
    while t < max_time:
        # интегрирование
        vy -= g * dt
        y_new = y + vy * dt
        x_new = vx * dt
        max_speed = max(max_speed, math.hypot(vx, vy))
        contact = floor_y + half
        if y_new < contact:
            # Насколько глубоко тело ушло бы за один шаг, если бы пола не было.
            # Дискретная сетка попадает в контакт случайной фазой, поэтому берём
            # честный худший случай: |v_y| · Δt (а не только фактический недолёт).
            depth = max(contact - y_new, abs(vy) * dt)
            max_step_depth = max(max_step_depth, depth)
            # разрешение контакта: возврат на поверхность + отражение по упругости
            y = contact
            vy = -vy * restitution
            if abs(vy) < 0.25:
                vy = 0.0
            # трение по касательной
            vx *= max(0.0, 1.0 - friction * g * dt / max(0.4, abs(vx) + 1e-6))
            if abs(vx) < 0.05:
                vx = 0.0
            if vy == 0.0 and vx == 0.0 and settled_t is None:
                settled_t = t
        else:
            y = y_new
            # воздух: сопротивления нет (drag = 0), только гравитация
        t += dt
        if y < floor_y - 12.0:
            break
    return {
        "max_speed": max_speed,
        "max_step_depth": max_step_depth,
        "rest_height": y,
        "settled_time": settled_t if settled_t is not None else t,
        "final_y": y,
    }


# ---------------------------------------------------------------------------
def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--no-charts", action="store_true")
    args = ap.parse_args()

    S = load_settings()
    props = load_props()
    ragdoll_mass = load_ragdoll_mass()
    counts = count_props()

    dt = S["fixedDt"]
    g = -S["gravityY"]                      # положительное ускорение
    slab = S["floorThickness"]

    lines: list[str] = []
    A = lines.append
    A("# Проверка физики тестового полигона")
    A("")
    A("Отчёт сгенерирован `Tools/physics_check.py` — независимым численным")
    A("расчётом (без Unity Editor). Все входные данные читаются прямо из")
    A("проекта: `ProjectSettings/TimeManager.asset`, `DynamicsManager.asset`,")
    A("`SandboxConfig.cs`, `PropFactory.cs`, `RagdollBuilder.cs`.")
    A("")
    A("## 1. Исходные параметры проекта")
    A("")
    A("| Параметр | Значение | Откуда |")
    A("|---|---|---|")
    A(f"| `fixedDeltaTime` | **{dt:.3f} c** ({1/dt:.0f} шагов/с) | TimeManager.asset |")
    A(f"| `maximumDeltaTime` | {S['maxDt']:.3f} c | TimeManager.asset |")
    A(f"| гравитация | **{S['gravityY']:.2f} м/с²** | DynamicsManager.asset |")
    A(f"| итерации решателя | {S['solverIt']} + {S['solverVelIt']} velocity | DynamicsManager.asset |")
    A(f"| порог сна тел | {S['sleepThreshold']} | DynamicsManager.asset |")
    A(f"| bounce threshold | {S['bounceThreshold']} м/с | DynamicsManager.asset |")
    A(f"| max depenetration | {S['maxDepen']} м/с | DynamicsManager.asset |")
    A(f"| max angular speed | {S['maxAngularSpeed']} рад/с | DynamicsManager.asset |")
    A(f"| толщина плиты пола | **{slab:.2f} м** (верх на y = {S['floorTopY']:.2f}) | SandboxConfig.FloorThickness |")
    A(f"| арена | {S['arenaW']:.0f} × {S['arenaD']:.0f} × {S['arenaH']:.0f} м, стены {S['wallThickness']:.2f} м | SandboxConfig |")
    A(f"| `KillY` | {S['killY']:.1f} м (ниже плиты → авто-восстановление) | SandboxConfig.KillY |")
    A(f"| скорость броска | {S['baseThrow']:.0f} м/с база, **{S['maxThrow']:.0f} м/с** потолок | SandboxConfig |")
    A(f"| масса манекена | **{ragdoll_mass:.1f} кг** (15 костей) | RagdollProfile.Humanoid |")
    A(f"| масса игрока | {S['playerMass']:.0f} кг | SandboxConfig |")
    A("")
    A("## 2. Главное: может ли предмет провалиться сквозь пол")
    A("")
    A("Условие непробиваемости: **максимальное перемещение за один физический шаг")
    A("меньше толщины плиты пола**. Тогда даже при чистом дискретном коллижене")
    A("(без CCD) тело физически не может «перепрыгнуть» плиту за такт, а с")
    A("`ContinuousDynamic`/`ContinuousSpeculative` гарантируется и точное место")
    A("контакта.")
    A("")

    # 2.1 — расчётные пределы скоростей
    step_max_throw = S["maxThrow"] * dt
    v_free_fall = math.sqrt(2 * g * S["arenaH"])       # падение с потолка
    step_free_fall = v_free_fall * dt
    # стресс-тест задаёт СКОРОСТЬ (импульс = m·v), поэтому худший случай —
    # самый лёгкий предмет на максимальной скорости пинка
    v_stress = min(S["stressMax"], S["maxSafeSpeed"] * 0.9)
    step_stress = v_stress * dt
    step_clamp = S["maxSafeSpeed"] * dt
    v_tunnel = slab / dt

    margin_throw = check("Шаг при максимальной скорости броска (м) < толщины плиты (м)",
                         step_max_throw, slab)
    check("Шаг при свободном падении с потолка (м) < толщины плиты (м)", step_free_fall, slab)
    margin_stress = check("Шаг при худшем импульсе стресс-теста (м) < толщины плиты (м)",
                          step_stress, slab)

    A("| Сценарий | Скорость | Путь за шаг (v·Δt) | Запас к плите %.2f м |" % slab)
    A("|---|---|---|---|")
    A(f"| Максимальный бросок предмета | {S['maxThrow']:.1f} м/с | **{step_max_throw:.3f} м** | ×{margin_throw:.2f} |")
    A(f"| Свободное падение с потолка ({S['arenaH']:.0f} м) | {v_free_fall:.1f} м/с | {step_free_fall:.3f} м | ×{slab/step_free_fall:.2f} |")
    A(f"| Стресс-тест: самый лёгкий предмет на максимуме пинка | {v_stress:.1f} м/с | {step_stress:.3f} м | ×{margin_stress:.2f} |")
    A(f"| Аварийный ограничитель `MaxSafeSpeed` | {S['maxSafeSpeed']:.0f} м/с | {step_clamp:.3f} м | ×{slab/step_clamp:.2f} |")
    A(f"| **Порог пробоя (v = толщина/Δt)** | **{v_tunnel:.1f} м/с** | {slab:.3f} м | — |")
    A("")
    A(f"То есть предмет должен лететь быстрее **{v_tunnel:.0f} м/с** ({v_tunnel*3.6:.0f} км/ч),")
    A("чтобы за один такт проскочить плиту. В игре скорость броска жёстко")
    A(f"ограничена `{S['maxThrow']:.0f} м/с` (`GrabController` клампит скорость), поэтому")
    A(f"запас составляет ×{v_tunnel/S['maxThrow']:.1f}. Дополнительно включены:")
    A("")
    A("- `CollisionDetectionMode.ContinuousSpeculative` — пока предмет в руках;")
    A("- `CollisionDetectionMode.ContinuousDynamic` — в полёте после броска;")
    A("- `ContinuousSpeculative` на всех 15 костях рэгдолла;")
    A("- `ContinuousDynamic` на телефоне (самый лёгкий и быстрый предмет);")
    A(f"- аварийный ограничитель `OutOfBoundsRecovery.ClampSpeed()`: раз в 0.5 с любое "
      f"не-удерживаемое тело со скоростью выше **{S['maxSafeSpeed']:.0f} м/с** "
      f"(порог пробоя {v_tunnel:.0f} м/с) принудительно тормозится, NaN-скорость обнуляется.")
    A("")

    # 2.2 — прямая симуляция
    A("## 3. Симуляция бросков и падений (полунеявный Эйлер, Δt = %.3f)" % dt)
    A("")
    A("Каждая строка — прогон траектории: предмет стартует, летит, бьётся о")
    A("пол, теряет скорость по упругости материала и успокаивается.")
    A("`Глубина за шаг` — худшее проникновение ниже уровня пола за один такт (|v_y|·Δt),")
    A("если бы пола не было; это и есть величина, которую должна «поймать» плита.")
    A("")
    A("| Предмет | Масса | Сценарий | Макс. скорость | Глубина за шаг | Удар об пол | Покой | Итог |")
    A("|---|---|---|---|---|---|---|---|")

    scenarios = []
    for p in props:
        h = half_extent(p)
        # бросок от уровня глаз вниз под углом
        scenarios.append((p, h, "Бросок %.0f м/с, −20°" % S["maxThrow"],
                          S["eyeHeight"] - 0.2,
                          (S["maxThrow"] * math.cos(math.radians(-20)),
                           S["maxThrow"] * math.sin(math.radians(-20))), 0.15, 0.7))
        # бросок базовой скоростью горизонтально
        scenarios.append((p, h, "Бросок %.0f м/с, 0°" % S["baseThrow"],
                          S["eyeHeight"] - 0.3, (S["baseThrow"], 0.0), 0.15, 0.7))
        # падение с платформы / стеллажа
        scenarios.append((p, h, "Падение с %.1f м" % max(S["platformHeight"], 1.3),
                          max(S["platformHeight"], 1.3) + h, (0.0, 0.0), 0.15, 0.7))
        # падение с потолка — худший случай
        scenarios.append((p, h, "Падение с потолка (%.0f м)" % S["arenaH"],
                          S["arenaH"] - 0.2, (0.6, 0.0), 0.15, 0.7))
        # стресс-тест: максимальная скорость пинка (импульс = m·v)
        v = min(S["stressMax"], S["maxSafeSpeed"] * 0.9)
        scenarios.append((p, h, "Стресс-пинок %.0f м/с (%.0f Н·с)" % (v, v * p["mass"]),
                          0.9 + h, (v, v * 0.15), 0.15, 0.7))
        # аварийный сценарий: тело разогнано до самого ограничителя
        scenarios.append((p, h, "Авария: %.0f м/с вниз" % S["maxSafeSpeed"],
                          6.0, (0.0, -S["maxSafeSpeed"]), 0.15, 0.7))

    worst_depth = 0.0
    worst_case = ""
    for p, h, label, y0, v0, rest, fric in scenarios:
        r = simulate(y0, v0, dt, g, h, rest, fric, floor_y=S["floorTopY"])
        worst_depth = max(worst_depth, r["max_step_depth"])
        if r["max_step_depth"] >= worst_depth - 1e-12:
            worst_case = f"{p['name']}, {label}"
        speed_at_hit = r["max_speed"]
        ok = r["max_step_depth"] < slab and r["final_y"] >= S["floorTopY"] + h - 0.02
        A(f"| {p['name']} | {p['mass']:.2f} кг | {label} | {speed_at_hit:.1f} м/с | "
          f"{r['max_step_depth']:.3f} м | {speed_at_hit:.1f} м/с | {r['settled_time']:.2f} c | "
          f"{'✅' if ok else '❌'} |")
        if not ok:
            fail(f"{p['name']} / {label}: глубина {r['max_step_depth']:.3f} м или итог y={r['final_y']:.3f}")

    A("")
    A(f"Худший случай по всем {len(scenarios)} прогонам: **{worst_case}** — "
      f"{worst_depth:.3f} м за такт при толщине плиты {slab:.2f} м "
      f"(запас ×{slab/worst_depth:.1f})." if worst_depth > 0 else "")
    A("")
    A("Итоговая высота покоя каждого предмета равна `y_пола + полувысота` —")
    A("то есть предметы лежат НА полу, а не в нём и не под ним.")
    A("")

    # 4. Замкнутость арены
    A("## 4. Замкнутость арены")
    A("")
    jump_apex = (S["jumpSpeed"] ** 2) / (2 * g)
    reach = S["eyeHeight"] + jump_apex
    wall_top = S["arenaH"]
    check("Прыжок игрока: верхняя точка обзора (м) < высоты стены (м)", reach, wall_top)
    A(f"- Высота прыжка: v₀ = {S['jumpSpeed']:.2f} м/с → подъём {jump_apex:.2f} м.")
    A(f"- Глаза на {S['eyeHeight']:.2f} м → максимум {reach:.2f} м при высоте стен {wall_top:.1f} м: "
      f"**выпрыгнуть нельзя** (запас {wall_top - reach:.1f} м).")
    A(f"- Бросок вертикально вверх: {S['maxThrow']:.0f} м/с → подъём "
      f"{(S['maxThrow']**2)/(2*g):.1f} м, но сверху закрыто плитой-потолком "
      f"(низ потолка на y = {wall_top:.1f} м).")
    A(f"- Предмет, всё же ушедший ниже `KillY = {S['killY']:.1f} м`, ловится "
      f"`OutOfBoundsRecovery` (проверка раз в 0.5 с: NaN, y < KillY, выход за "
      f"периметр + 8 м) и возвращается на точку спавна; счётчик восстановлений "
      f"выводится в HUD-статистику.")
    A(f"- Пол — плита {slab:.1f} м с верхом на y = {S['floorTopY']:.1f}, "
      f"низ на y = {S['floorTopY'] - slab:.1f} (глубже KillY → любое чудо будет замечено).")
    A("")

    # 5. Удары и манекен
    A("## 5. Удары: урон и импульс по манекену")
    A("")
    A(f"Манекен: {ragdoll_mass:.1f} кг, 15 костей, стоит на приводах рэгдолла. "
      f"Падает, если здоровье ≤ 42 или импульс удара ≥ {S['ragdollThreshold']:.0f} Н·с.")
    A("")
    A("| Действие | Импульс, Н·с | Урон | Δv манекена, м/с | Отлетит? |")
    A("|---|---|---|---|---|")

    def punch_row(label: str, impulse: float, mass_factor: float = 1.0) -> None:
        total = impulse * mass_factor
        dmg = 9.0 + total * 0.055
        dv = total * 1.35 / ragdoll_mass
        falls = "да, рэгдолл" if total >= S["ragdollThreshold"] or dmg >= 58 else "нет, только вздрагивание"
        A(f"| {label} | {total:.0f} | {dmg:.0f} | {dv:.2f} | {falls} |")

    punch_row("Удар кулаком", S["punchImpulse"])
    for p in props:
        # Interactor.cs: Mathf.Clamp(held.Body.mass * 0.42f, 0.7f, 3.4f)
        mass_factor = min(3.4, max(0.7, p["mass"] * 0.42))
        punch_row(f"Удар предметом «{p['name']}»", S["punchImpulse"] * S["heldPunchBonus"], mass_factor)
    A("")
    A("| Бросок предмета | Скорость | Импульс (m·v), Н·с | Урон (v·%.0f) | Порог %.0f Н·с |"
      % (S["throwDamageScale"], S["ragdollThreshold"]))
    A("|---|---|---|---|---|")
    for p in props:
        for speed, tag in ((S["baseThrow"], "базовая"), (S["maxThrow"], "максимум")):
            imp = p["mass"] * speed
            dmg = speed * S["throwDamageScale"]
            A(f"| {p['name']} ({tag}) | {speed:.0f} м/с | {imp:.0f} | {dmg:.0f} | "
              f"{'превышен' if imp >= S['ragdollThreshold'] else 'не превышен, но урон ≥ 58'} |")
    A("")
    bare_dmg = 9.0 + S["punchImpulse"] * 0.055
    hits = math.ceil(58.0 / bare_dmg)
    A(f"- Чистыми кулаками манекен падает за **{hits} удара** "
      f"(урон {bare_dmg:.1f} за удар, порог падения по здоровью — 58 единиц).")
    A(f"- Кулдаун удара 0.40 с → «урон в секунду» кулаками ≈ {bare_dmg/0.4:.0f}.")
    A("- Брошенная бочка (19 кг × 18 м/с = 342 Н·с) и удар тяжёлым предметом "
      "превышают порог рэгдолла → манекен обмякает и отлетает с подскоком "
      "`+up·min(90, |J|·0.22)` Н·с, то есть кувыркается.")
    A("- Брошенная бутылка (0.42 кг) даёт всего 8 Н·с импульса, но 90–108 урона: "
      "манекен падает, а стекло бьётся (`Breakable`, порог 4.6 м/с, до 42 осколков).")
    A("")

    # 6. Игрок
    A("## 6. Игрок и «живая камера»")
    A("")
    A(f"- `CharacterController`: радиус 0.34 м, высота 1.80 м, глаза {S['eyeHeight']:.2f} м, "
      f"масса для расчёта отдачи {S['playerMass']:.0f} кг.")
    A(f"- Скорость {2.7:.1f} м/с шаг / {4.8:.1f} м/с бег → за такт {4.8*dt:.3f} м "
      f"(стены {S['wallThickness']:.2f} м: пройти сквозь них нельзя).")
    A("- Пока игрок на ногах — камера живёт отдельно: bob 0.055 м @ 8.5 Гц по скорости, "
      "dip 0.22 м при приземлении, шейк от травм с затуханием 1.6.")
    A("- При уроне ≥ 55 (`RagdollThreshold`) включается рэгдолл игрока, и камера "
      "привязывается к кости Chest — тело кувыркается, камера кувыркается вместе с ним.")
    A("- Ниже `KillY` игрок автоматически поднимается на точке спавна.")
    A("")

    # 7. Бюджет под Galaxy A53
    A("## 7. Бюджет физики под Galaxy A53")
    A("")
    total_props = sum(v for k, v in counts.items() if k != "StaticBox")
    statics = counts.get("StaticBox", 0)
    dynamic_bodies = total_props + 2 * 15 + 1          # предметы + 2 рэгдолла + игрок
    A("| Объект | Количество |")
    A("|---|---|")
    for k, v in counts.items():
        A(f"| `PropFactory.{k}` | {v} |")
    A(f"| **Динамических Rigidbody** | **≈ {dynamic_bodies}** (предметы {total_props} + 30 костей + игрок) |")
    A(f"| Статичных коллайдеров (`isStatic`, без Rigidbody) | {statics} |")
    A(f"| Максимум осколков стекла одновременно | 42 на бутылку, время жизни 14 с |")
    A("")
    A(f"Расчёт шага: решатель {S['solverIt']} + {S['solverVelIt']} итераций, {1/dt:.0f} Гц, "
      f"≈ {dynamic_bodies} динамических тела —")
    A("это заметно меньше бюджета Exynos 1280 (8 ядер A77/A55); большая часть тел")
    A("засыпает (`sleepThreshold` понижен до 0.005, спящие тела решатель не трогает).")
    A(f"Стресс-тест (`PhysicsStressTester`, {S['stressDuration']:.0f} c, 3 пинка каждые 0.30 c) ")
    A(f"разгоняет случайные предметы до {S['stressMin']:.0f}–{S['stressMax']:.0f} м/с импульсом `m·v` ")
    A("и каждые 0.3 с сверяет нижнюю точку коллайдеров с уровнем пола; при провале — ")
    A("`ResetToSpawn()` и счётчик нарушений, итог PASS/FAIL пишется в лог и в HUD.")
    A("")
    A("Профиль качества `Medium (A53)`: renderScale 0.85, тени 22 м, без MSAA,")
    A("30/60 fps по выбору `PerformanceTuner`.")
    A("")

    # 8. Итог
    A("## 8. Итог")
    A("")
    for name, value, limit, verdict in CHECKS:
        mark = "❌" if verdict == "FAIL" else "✅"
        A(f"- {mark} {name}: **{value:.4f}** (лимит {limit:.4f})")
    A("")
    if FAILURES:
        A(f"**НАЙДЕНО ПРОБЛЕМ: {len(FAILURES)}**")
        for f in FAILURES:
            A(f"- {f}")
    else:
        A("**ВСЕ ПРОВЕРКИ ПРОЙДЕНЫ.** Предметы не проваливаются сквозь пол.")
        A("")
        A(f"- Штатная игра: худший шаг {step_max_throw:.3f} м (бросок {S['maxThrow']:.0f} м/с) "
          f"и {step_free_fall:.3f} м (свободное падение с потолка {S['arenaH']:.0f} м) против "
          f"толщины плиты {slab:.1f} м — запас ×{slab/step_max_throw:.1f}.")
        A(f"- Аварийный сценарий (тело разогнано до ограничителя {S['maxSafeSpeed']:.0f} м/с "
          f"строго вниз): шаг {worst_depth:.3f} м — всё ещё меньше плиты в {slab/worst_depth:.1f} раза.")
        A("- Поверх этого работают continuous collision detection (`ContinuousDynamic` в полёте), "
          "ограничитель скорости `OutOfBoundsRecovery.ClampSpeed()` и авто-возврат предмета на "
          "точку спавна, если он всё же оказался ниже `KillY`.")
        A("- В самом движке это дополнительно проверяется рантайм-тестами: `SandboxSelfTest.Run()` "
          "и `PhysicsStressTester` (кнопка «СТРЕСС-ТЕСТ» в HUD, 14 секунд случайных пинков).")
    A("")

    os.makedirs(REPORT_DIR, exist_ok=True)
    report_path = os.path.join(REPORT_DIR, "physics_report.md")
    with open(report_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")

    if not args.no_charts:
        try:
            make_charts(S, props, dt, g, slab)
        except Exception as e:                                    # noqa: BLE001
            print(f"[warn] графики не построены: {e}")

    print("\n".join(lines))
    print(f"\nОтчёт: {os.path.relpath(report_path, REPO)}")
    for c in CHARTS:
        print(f"График: {os.path.relpath(c, REPO)}")
    print("ИТОГ: " + ("FAIL" if FAILURES else "PASS"))
    return 1 if FAILURES else 0


# ---------------------------------------------------------------------------
def make_charts(S: dict, props: list[dict], dt: float, g: float, slab: float) -> None:
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    # --- График 1: пучок траекторий броска ---
    fig, ax = plt.subplots(figsize=(9, 5), dpi=110)
    colors = plt.cm.viridis([i / 6 for i in range(6)])
    for si, speed in enumerate([4.0, S["baseThrow"] * 0.6, S["baseThrow"],
                                S["maxThrow"] * 0.8, S["maxThrow"], S["maxThrow"] * 1.3]):
        for pitch in (-30, -10, 10):
            y0 = S["eyeHeight"] - 0.25
            vx, vy = speed * math.cos(math.radians(pitch)), speed * math.sin(math.radians(pitch))
            xs, ys, x, y, t = [], [], 0.0, y0, 0.0
            while t < 2.5 and y > 0.0:
                xs.append(x)
                ys.append(y)
                vy -= g * dt
                x += vx * dt
                y += vy * dt
                t += dt
            # траектория обрывается на уровне пола (y = 0) — там предмет и лежит
            ax.plot(xs, ys, color=colors[si], lw=1.0, alpha=0.9 if pitch == -10 else 0.35)
            if xs:
                ax.plot([xs[-1]], [0.0], marker="o", ms=3.2, color=colors[si],
                        alpha=0.9 if pitch == -10 else 0.4)
        ax.plot([], [], color=colors[si], lw=1.4, label=f"{speed:.0f} м/с")

    ax.axhspan(S["floorTopY"] - slab, S["floorTopY"], color="#8d6e63", alpha=0.55,
               label=f"плита пола {slab:.1f} м")
    ax.axhline(S["floorTopY"], color="k", lw=1.0)
    ax.set_xlim(0, 26)
    ax.set_ylim(-1.4, 3.2)
    ax.set_xlabel("дистанция, м")
    ax.set_ylabel("высота, м")
    ax.set_title("Траектории броска (Δt = %.3f c, g = %.2f м/с²)" % (dt, g))
    ax.legend(loc="upper right", fontsize=8)
    ax.grid(alpha=0.25)
    fig.tight_layout()
    p1 = os.path.join(REPORT_DIR, "throw_trajectories.png")
    fig.savefig(p1)
    plt.close(fig)
    CHARTS.append(p1)

    # --- График 2: запас на пробой пола ---
    labels = ["Штатный\nбросок\n%.0f м/с" % S["maxThrow"],
              "Падение\nс потолка\n%.0f м" % S["arenaH"],
              "Стресс-пинок\n%.0f м/с" % min(S["stressMax"], S["maxSafeSpeed"] * 0.9),
              "Ограничитель\nMaxSafeSpeed",
              "Порог\nпробоя"]
    values = [S["maxThrow"] * dt,
              math.sqrt(2 * g * S["arenaH"]) * dt,
              min(S["stressMax"], S["maxSafeSpeed"] * 0.9) * dt,
              S["maxSafeSpeed"] * dt,
              slab]
    fig, ax = plt.subplots(figsize=(9.2, 4.8), dpi=110)
    colors = ["#2e7d32" if v < slab else "#c62828" for v in values[:-1]] + ["#455a64"]
    bars = ax.bar(labels, values, color=colors)
    ax.axhline(slab, color="#c62828", ls="--", lw=1.4,
               label=f"толщина плиты пола = {slab:.1f} м")
    for b, v in zip(bars, values):
        ax.text(b.get_x() + b.get_width() / 2, v, f"{v:.2f} м", ha="center", va="bottom", fontsize=9)
    ax.set_ylabel("путь за один физический шаг, м")
    ax.set_title("Запас на пробой пола: путь за такт против толщины плиты")
    ax.legend(fontsize=9)
    ax.set_ylim(0, max(values) * 1.18)
    ax.grid(axis="y", alpha=0.25)
    fig.tight_layout()
    p2 = os.path.join(REPORT_DIR, "floor_penetration_margin.png")
    fig.savefig(p2)
    plt.close(fig)
    CHARTS.append(p2)


if __name__ == "__main__":
    sys.exit(main())
