/*
 * Sandbox FPS — браузерная версия тестового полигона.
 * Тот же замысел, что и у Unity-проекта: закрытая арена, предметы с физикой,
 * хват/бросок/удар, манекены-болванки, смартфон в руке, живая камера,
 * тач-управление (джойстик слева, свайп и кнопки справа), альбомная ориентация.
 *
 * Это НЕ замена APK: это способ поиграть на телефоне прямо сейчас, пока
 * для сборки настоящего APK не добавлены секреты лицензии Unity.
 */
import * as THREE from 'three';
import * as CANNON from 'cannon-es';

// ---------------------------------------------------------------- константы
const ARENA_W = 26, ARENA_D = 20, WALL_H = 7;
const EYE = 1.68, WALK = 3.0, RUN = 5.2, JUMP = 4.6, GRAV = -9.81;
const REACH = 3.0, THROW_SPEED = 13, PUNCH_IMPULSE = 150;
const DUMMY_FALL_IMPULSE = 260;

// ---------------------------------------------------------------- сцена
const canvas = document.getElementById('gl');
const renderer = new THREE.WebGLRenderer({ canvas, antialias: false, powerPreference: 'high-performance' });
renderer.setPixelRatio(Math.min(devicePixelRatio, 1.75));
renderer.setSize(innerWidth, innerHeight);

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x9fb6c9);
scene.fog = new THREE.Fog(0x9fb6c9, 40, 90);

const camera = new THREE.PerspectiveCamera(66, innerWidth / innerHeight, 0.05, 140);

const hemi = new THREE.HemisphereLight(0xdfe8f2, 0x54504a, 0.85);
scene.add(hemi);
const sun = new THREE.DirectionalLight(0xfff2dd, 1.15);
sun.position.set(30, 42, -18);
scene.add(sun);

// ---------------------------------------------------------------- физмир
const world = new CANNON.World({ gravity: new CANNON.Vec3(0, GRAV, 0) });
world.broadphase = new CANNON.SAPBroadphase(world);
world.allowSleep = true;
world.solver.iterations = 10;
world.defaultContactMaterial.friction = 0.55;
world.defaultContactMaterial.restitution = 0.12;

const staticMat = new CANNON.Material('static');
const propMat = new CANNON.Material('prop');
world.addContactMaterial(new CANNON.ContactMaterial(staticMat, propMat, { friction: 0.62, restitution: 0.10 }));
world.addContactMaterial(new CANNON.ContactMaterial(propMat, propMat, { friction: 0.55, restitution: 0.18 }));

// ---------------------------------------------------------------- материалы вида
function mat(color, rough = 0.85, metal = 0.0, opacity = 1) {
  return new THREE.MeshStandardMaterial({ color, roughness: rough, metalness: metal, transparent: opacity < 1, opacity });
}
const M = {
  floor: mat(0x8d8d86, 0.95),
  wall: mat(0xa8a49c, 0.95),
  wood: mat(0x9a6b3f, 0.8),
  woodDark: mat(0x6f4a2a, 0.85),
  metal: mat(0x7d8288, 0.45, 0.75),
  rusty: mat(0x8a5a3b, 0.7, 0.4),
  brick: mat(0x9c4a38, 0.9),
  glass: mat(0x9fd4c8, 0.15, 0.1, 0.55),
  skin: mat(0xd9a066, 0.7),
  cloth: mat(0x3f5a75, 0.85),
  phone: mat(0x22262b, 0.35, 0.5),
  hazard: mat(0xc9a227, 0.9),
};

// ---------------------------------------------------------------- геометрия арены
function box(w, h, d, material, x, y, z, isStatic = true) {
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), material);
  mesh.position.set(x, y, z);
  scene.add(mesh);
  if (isStatic) {
    const body = new CANNON.Body({ mass: 0, material: staticMat, shape: new CANNON.Box(new CANNON.Vec3(w / 2, h / 2, d / 2)) });
    body.position.set(x, y, z);
    world.addBody(body);
  }
  return mesh;
}

// пол-плита (верх на y=0)
box(ARENA_W + 2, 1, ARENA_D + 2, M.floor, 0, -0.5, 0);
// разметка
const decal = new THREE.Mesh(new THREE.PlaneGeometry(6, 6), M.hazard);
decal.rotation.x = -Math.PI / 2; decal.position.set(3.5, 0.012, 1.5);
scene.add(decal);
// стены и потолок
box(ARENA_W + 1.2, WALL_H, 0.6, M.wall, 0, WALL_H / 2, -(ARENA_D / 2 + 0.3));
box(ARENA_W + 1.2, WALL_H, 0.6, M.wall, 0, WALL_H / 2, (ARENA_D / 2 + 0.3));
box(0.6, WALL_H, ARENA_D + 1.2, M.wall, -(ARENA_W / 2 + 0.3), WALL_H / 2, 0);
box(0.6, WALL_H, ARENA_D + 1.2, M.wall, (ARENA_W / 2 + 0.3), WALL_H / 2, 0);
box(ARENA_W + 1.2, 0.5, ARENA_D + 1.2, M.wall, 0, WALL_H + 0.25, 0);
// платформа и пандус
box(3.2, 1.3, 3.2, M.wall, -8.4, 0.65, -5.2);
const rampMesh = box(2.2, 0.18, 3.4, M.wall, -8.4, 0.62, -2.6);
rampMesh.rotation.x = -0.36;
{
  const rb = new CANNON.Body({ mass: 0, material: staticMat, shape: new CANNON.Box(new CANNON.Vec3(1.1, 0.09, 1.7)) });
  rb.position.set(-8.4, 0.62, -2.6);
  rb.quaternion.setFromEuler(-0.36, 0, 0);
  world.addBody(rb);
}
// стеллаж
box(4.6, 0.1, 0.9, M.metal, 4.2, 0.95, 9.2);
box(0.14, 0.94, 0.8, M.metal, 2.1, 0.47, 9.2);
box(0.14, 0.94, 0.8, M.metal, 6.3, 0.47, 9.2);

// ---------------------------------------------------------------- предметы
const props = [];      // { mesh, body, kind, name, spawn:{p,q}, mass }
const raycastTargets = [];

function addProp(kind, name, mesh, body, mass) {
  mesh.userData.propIndex = props.length;
  props.push({ kind, name, mesh, body, mass, spawn: { p: body.position.clone(), q: body.quaternion.clone() } });
  raycastTargets.push(mesh);
  scene.add(mesh);
  world.addBody(body);
  body.material = propMat;
  body.addEventListener('collide', (e) => {
    const v = Math.abs(e.contact.getImpactVelocityAlongNormal());
    if (v > 2.2) thud(Math.min(1, v / 12), kind);
    if (kind === 'bottle' && v > 4.6) breakBottle(props[mesh.userData.propIndex]);
  });
  return props[props.length - 1];
}

function makeBoxProp(w, h, d, material, mass, x, y, z, ry = 0, name = null) {
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), material);
  const body = new CANNON.Body({ mass, shape: new CANNON.Box(new CANNON.Vec3(w / 2, h / 2, d / 2)) });
  body.position.set(x, y, z);
  body.quaternion.setFromEuler(0, ry, 0);
  body.linearDamping = 0.06; body.angularDamping = 0.14;
  return addProp('box', name || nameOf(material), mesh, body, mass);
}
function nameOf(m) {
  return m === M.wood || m === M.woodDark ? 'Ящик' : m === M.brick ? 'Кирпич' : m === M.metal ? 'Балка' : 'Доска';
}
function makeBarrel(x, y, z, rusty = false) {
  const mesh = new THREE.Mesh(new THREE.CylinderGeometry(0.29, 0.29, 0.88, 14), rusty ? M.rusty : M.metal);
  const body = new CANNON.Body({ mass: rusty ? 22 : 19, shape: new CANNON.Cylinder(0.29, 0.29, 0.88, 10) });
  body.position.set(x, y, z);
  body.linearDamping = 0.05; body.angularDamping = 0.12;
  return addProp('barrel', rusty ? 'Бочка (ржавая)' : 'Бочка', mesh, body, body.mass);
}
function makeBottle(x, y, z) {
  const mesh = new THREE.Mesh(new THREE.CylinderGeometry(0.037, 0.045, 0.3, 10), M.glass);
  const body = new CANNON.Body({ mass: 0.42, shape: new CANNON.Cylinder(0.04, 0.045, 0.3, 8) });
  body.position.set(x, y, z);
  body.linearDamping = 0.04; body.angularDamping = 0.1;
  return addProp('bottle', 'Бутылка', mesh, body, 0.42);
}

// расстановка — как в Unity-полигоне
[[-2.6, 0.32, -3.2, 0.62], [-2.6, 0.96, -3.2, 0.62], [-1.9, 0.32, -3.6, 0.62],
 [5.4, 0.30, 2.8, 0.58], [-8.4, 1.62, -5.2, 0.55], [9.6, 0.40, 5.4, 0.78],
 [10.4, 0.26, -7.6, 0.5]].forEach(([x, y, z, s], i) =>
  makeBoxProp(s, s, s, i % 2 ? M.woodDark : M.wood, 12 * (s / 0.62), x, y + s * 0.02, z, i * 0.4));
[[3.4, 0.45, -3.4, false], [4.2, 0.45, -2.6, true], [-5.8, 0.45, 1.4, false],
 [7.2, 0.45, 7.4, true], [-8.4, 1.78, -4.2, false], [7.6, 0.30, -2.6, true]].forEach(([x, y, z, r]) => makeBarrel(x, y, z, r));
for (let i = 0; i < 3; i++) makeBoxProp(0.25, 0.075, 0.12, M.brick, 2.6, -6.2 + i * 0.27, 0.04, 4.2);
[[-4.2, 1.05, 9.2], [-9.6, 0.04, 3.1], [6.2, 0.04, 4.6], [10.1, 0.04, -6.9]].forEach(([x, y, z], i) =>
  makeBoxProp(0.25, 0.075, 0.12, M.brick, 2.6, x, y, z, i));
makeBoxProp(0.17, 0.035, 2.1, M.woodDark, 5.5, 1.6, 0.03, 6.2, 0.5, 'Доска');
makeBoxProp(0.17, 0.035, 1.8, M.woodDark, 4.8, -6.6, 0.03, -7.4, -1.1, 'Доска');
[[3.2, 1.02, 9.2], [3.62, 1.02, 9.2], [4.04, 1.02, 9.2], [-3.4, 1.02, 9.2], [-4.9, 1.02, 9.2],
 [0.1, 0.92, -1.5], [-8.9, 1.32, -5.7], [10.6, 0.17, -6.4]].forEach(([x, y, z]) => makeBottle(x, y, z));

// осколки стекла
const shards = [];
function breakBottle(prop) {
  if (!prop || prop.broken) return;
  prop.broken = true;
  scene.remove(prop.mesh);
  world.removeBody(prop.body);
  raycastTargets.splice(raycastTargets.indexOf(prop.mesh), 1);
  for (let i = 0; i < 6; i++) {
    const s = 0.02 + Math.random() * 0.04;
    const mesh = new THREE.Mesh(new THREE.BoxGeometry(s, s * 0.4, s * 1.3), M.glass);
    const body = new CANNON.Body({ mass: 0.05, shape: new CANNON.Box(new CANNON.Vec3(s / 2, s * 0.2, s * 0.65)) });
    body.position.copy(prop.body.position);
    body.velocity.set((Math.random() - 0.5) * 4, Math.random() * 3, (Math.random() - 0.5) * 4);
    body.angularVelocity.set(Math.random() * 9, Math.random() * 9, Math.random() * 9);
    body.material = propMat;
    scene.add(mesh); world.addBody(body);
    shards.push({ mesh, body, t: 12 });
  }
  thud(0.9, 'glass');
  if (held === prop) held = null;
}

// ---------------------------------------------------------------- манекены
const dummies = [];
function makeDummy(name, x, z, ry) {
  const group = new THREE.Group();
  const torso = new THREE.Mesh(new THREE.BoxGeometry(0.46, 1.05, 0.3), M.cloth);
  torso.position.y = 0.82;
  const head = new THREE.Mesh(new THREE.SphereGeometry(0.16, 12, 10), M.skin);
  head.position.y = 1.52;
  const legL = new THREE.Mesh(new THREE.BoxGeometry(0.16, 0.78, 0.2), M.cloth);
  legL.position.set(-0.13, 0.39, 0);
  const legR = legL.clone(); legR.position.x = 0.13;
  const armL = new THREE.Mesh(new THREE.BoxGeometry(0.13, 0.72, 0.16), M.cloth);
  armL.position.set(-0.32, 0.95, 0);
  const armR = armL.clone(); armR.position.x = 0.32;
  group.add(torso, head, legL, legR, armL, armR);
  group.position.set(x, 0, z);
  group.rotation.y = ry;
  scene.add(group);

  const body = new CANNON.Body({ mass: 78, material: propMat });
  body.addShape(new CANNON.Box(new CANNON.Vec3(0.23, 0.52, 0.15)), new CANNON.Vec3(0, 0.82, 0));
  body.addShape(new CANNON.Sphere(0.16), new CANNON.Vec3(0, 1.52, 0));
  body.addShape(new CANNON.Box(new CANNON.Vec3(0.08, 0.39, 0.1)), new CANNON.Vec3(-0.13, 0.39, 0));
  body.addShape(new CANNON.Box(new CANNON.Vec3(0.08, 0.39, 0.1)), new CANNON.Vec3(0.13, 0.39, 0));
  body.position.set(x, 0, z);
  body.quaternion.setFromEuler(0, ry, 0);
  body.linearDamping = 0.12; body.angularDamping = 0.25;
  world.addBody(body);

  const dummy = { name, group, body, standing: true, hp: 100, hits: 0, resetT: 0, spawn: { x, z, ry } };
  torso.userData.dummyIndex = dummies.length;
  head.userData.dummyIndex = dummies.length;
  raycastTargets.push(torso, head);
  dummies.push(dummy);
  body.addEventListener('collide', (e) => {
    const v = Math.abs(e.contact.getImpactVelocityAlongNormal());
    if (v > 2.5) thud(Math.min(1, v / 12), 'soft');
  });
  return dummy;
}
makeDummy('Манекен A', -1.6, 2.2, 2.93);
makeDummy('Манекен B', 2.6, 3.0, 3.35);

// ---------------------------------------------------------------- смартфон (стартует в руке)
const phoneMesh = new THREE.Mesh(new THREE.BoxGeometry(0.075, 0.155, 0.012), M.phone);
const phoneScreen = new THREE.Mesh(new THREE.PlaneGeometry(0.065, 0.135), new THREE.MeshStandardMaterial({ color: 0x2e6da8, emissive: 0x1c4f7c, emissiveIntensity: 0.7 }));
phoneScreen.position.z = 0.007;
phoneMesh.add(phoneScreen);
const phoneBody = new CANNON.Body({ mass: 0.19, shape: new CANNON.Box(new CANNON.Vec3(0.0375, 0.0775, 0.006)), material: propMat });
phoneBody.linearDamping = 0.05;
phoneBody.position.set(0, 1.35, -4.6);
const phoneProp = addProp('phone', 'Смартфон', phoneMesh, phoneBody, 0.19);

// ---------------------------------------------------------------- руки игрока
const hands = new THREE.Group();
function limb(x) {
  const g = new THREE.Group();
  const arm = new THREE.Mesh(new THREE.BoxGeometry(0.09, 0.09, 0.42), M.cloth);
  arm.position.set(0, 0, -0.18);
  const hand = new THREE.Mesh(new THREE.BoxGeometry(0.085, 0.1, 0.12), M.skin);
  hand.position.set(0, 0.01, -0.42);
  g.add(arm, hand);
  g.position.set(x, -0.34, -0.55);
  g.rotation.x = 0.28;
  g.rotation.y = -x * 0.55;
  return g;
}
const handL = limb(-0.26), handR = limb(0.26);
hands.add(handL, handR);
camera.add(hands);
scene.add(camera);

// ---------------------------------------------------------------- звук (процедурный)
let AC = null;
function audio() { if (!AC) AC = new (window.AudioContext || window.webkitAudioContext)(); return AC; }
let noiseBuf = null;
function thud(vol, kind) {
  try {
    const ac = audio();
    if (ac.state === 'suspended') ac.resume();
    if (!noiseBuf) {
      noiseBuf = ac.createBuffer(1, ac.sampleRate * 0.12, ac.sampleRate);
      const d = noiseBuf.getChannelData(0);
      for (let i = 0; i < d.length; i++) d[i] = (Math.random() * 2 - 1) * (1 - i / d.length);
    }
    const src = ac.createBufferSource(); src.buffer = noiseBuf;
    const flt = ac.createBiquadFilter();
    flt.type = 'lowpass';
    flt.frequency.value = kind === 'glass' ? 5200 : kind === 'metal' || kind === 'barrel' ? 1500 : kind === 'soft' ? 700 : 950;
    const g = ac.createGain();
    g.gain.value = 0.35 * vol;
    src.connect(flt); flt.connect(g); g.connect(ac.destination);
    src.start();
  } catch (e) { /* звук не критичен */ }
}

// ---------------------------------------------------------------- ввод
const input = { move: { x: 0, y: 0 }, look: { x: 0, y: 0 }, jump: false, run: false, action: false, actionEdge: false };
const ui = {
  stick: document.getElementById('stick'), knob: document.getElementById('knob'),
  prompt: document.getElementById('prompt'), stats: document.getElementById('stats'),
  cross: document.getElementById('cross'), toast: document.getElementById('toast'),
};
let stickId = null, stickOrigin = { x: 0, y: 0 }, lookId = null, lookLast = { x: 0, y: 0 };
const leftZone = () => innerWidth * 0.42;

addEventListener('touchstart', (e) => {
  for (const t of e.changedTouches) {
    if (t.target.closest('button')) continue;
    if (t.clientX < leftZone() && stickId === null) {
      stickId = t.identifier; stickOrigin = { x: t.clientX, y: t.clientY };
      ui.stick.style.left = (t.clientX - 60) + 'px'; ui.stick.style.top = (t.clientY - 60) + 'px';
      ui.stick.style.opacity = 1;
    } else if (lookId === null) { lookId = t.identifier; lookLast = { x: t.clientX, y: t.clientY }; }
  }
  try { screen.orientation.lock('landscape').catch(() => {}); } catch (e2) {}
}, { passive: false });
addEventListener('touchmove', (e) => {
  for (const t of e.changedTouches) {
    if (t.identifier === stickId) {
      let dx = t.clientX - stickOrigin.x, dy = t.clientY - stickOrigin.y;
      const len = Math.hypot(dx, dy), max = 62;
      if (len > max) { dx *= max / len; dy *= max / len; }
      ui.knob.style.transform = `translate(${dx}px, ${dy}px)`;
      input.move.x = dx / max; input.move.y = -dy / max;
    } else if (t.identifier === lookId) {
      input.look.x += (t.clientX - lookLast.x); input.look.y += (t.clientY - lookLast.y);
      lookLast = { x: t.clientX, y: t.clientY };
    }
  }
  e.preventDefault();
}, { passive: false });
addEventListener('touchend', (e) => {
  for (const t of e.changedTouches) {
    if (t.identifier === stickId) { stickId = null; input.move.x = 0; input.move.y = 0; ui.stick.style.opacity = 0; ui.knob.style.transform = 'translate(0,0)'; }
    if (t.identifier === lookId) lookId = null;
  }
});

// клавиатура/мышь для ПК
const keys = {};
addEventListener('keydown', (e) => { keys[e.code] = true; if (e.code === 'KeyE') input.actionEdge = true; if (e.code === 'Space') input.jump = true; });
addEventListener('keyup', (e) => { keys[e.code] = false; });
canvas.addEventListener('click', () => { if (!('ontouchstart' in window)) canvas.requestPointerLock(); });
addEventListener('mousemove', (e) => { if (document.pointerLockElement === canvas) { input.look.x += e.movementX; input.look.y += e.movementY; } });
addEventListener('mousedown', (e) => { if (document.pointerLockElement === canvas && e.button === 0) input.actionEdge = true; });

document.getElementById('btnAction').addEventListener('touchstart', (e) => { e.preventDefault(); input.actionEdge = true; });
document.getElementById('btnAction').addEventListener('click', () => { input.actionEdge = true; });
document.getElementById('btnJump').addEventListener('touchstart', (e) => { e.preventDefault(); input.jump = true; });
document.getElementById('btnJump').addEventListener('click', () => { input.jump = true; });
document.getElementById('btnRun').addEventListener('click', (e) => { input.run = !input.run; e.target.classList.toggle('on', input.run); });
document.getElementById('btnReset').addEventListener('click', () => resetAll());
document.getElementById('btnFull').addEventListener('click', () => {
  document.documentElement.requestFullscreen?.();
  try { screen.orientation.lock('landscape').catch(() => {}); } catch (e) {}
});

// ---------------------------------------------------------------- игрок
const player = { pos: new THREE.Vector3(0, EYE, -6.2), vel: new THREE.Vector3(), yaw: 0.14, pitch: 0, onGround: true, bob: 0, held: null };
let held = null;

function resetAll() {
  for (const p of props) {
    if (p.broken) continue;
    p.body.position.copy(p.spawn.p);
    p.body.quaternion.copy(p.spawn.q);
    p.body.velocity.setZero(); p.body.angularVelocity.setZero();
    p.body.wakeUp();
  }
  for (const s of shards) { scene.remove(s.mesh); world.removeBody(s.body); }
  shards.length = 0;
  for (const d of dummies) {
    d.body.position.set(d.spawn.x, 0, d.spawn.z);
    d.body.quaternion.setFromEuler(0, d.spawn.ry, 0);
    d.body.velocity.setZero(); d.body.angularVelocity.setZero();
    d.standing = true; d.hp = 100; d.resetT = 0;
  }
  player.pos.set(0, EYE, -6.2); player.vel.set(0, 0, 0); player.yaw = 0.14; player.pitch = 0;
  held = null;
  toast('Полигон сброшен');
}

function toast(text) {
  ui.toast.textContent = text;
  ui.toast.style.opacity = 1;
  clearTimeout(toast.t);
  toast.t = setTimeout(() => { ui.toast.style.opacity = 0; }, 1600);
}

// ---------------------------------------------------------------- взаимодействие
const raycaster = new THREE.Raycaster();
const center = new THREE.Vector2(0, 0);

function aim() {
  raycaster.setFromCamera(center, camera);
  raycaster.far = REACH;
  const hits = raycaster.intersectObjects(raycastTargets, false);
  if (!hits.length) return null;
  const h = hits[0];
  if (h.object.userData.propIndex !== undefined) return { type: 'prop', prop: props[h.object.userData.propIndex], point: h.point, dist: h.distance };
  if (h.object.userData.dummyIndex !== undefined) return { type: 'dummy', dummy: dummies[h.object.userData.dummyIndex], point: h.point, dist: h.distance };
  return null;
}

function doAction() {
  const target = aim();
  if (held) {                                   // бросок
    const dir = new THREE.Vector3(); camera.getWorldDirection(dir);
    held.body.velocity.set(dir.x * THROW_SPEED + player.vel.x, dir.y * THROW_SPEED + player.vel.y + 1.2, dir.z * THROW_SPEED + player.vel.z);
    held.body.angularVelocity.set((Math.random() - 0.5) * 8, (Math.random() - 0.5) * 8, (Math.random() - 0.5) * 8);
    held.body.wakeUp();
    toast(`Брошено: ${held.name} (${(THROW_SPEED * 3.6).toFixed(0)} км/ч)`);
    thud(0.25, 'whoosh');
    held = null;
    punchAnim();
    return;
  }
  if (target && target.type === 'prop') {       // хват
    held = target.prop;
    held.body.wakeUp();
    toast(`Взято: ${held.name}`);
    punchAnim();
    return;
  }
  if (target && target.type === 'dummy') {      // удар
    const d = target.dummy;
    const dir = new THREE.Vector3(); camera.getWorldDirection(dir);
    const imp = new CANNON.Vec3(dir.x * PUNCH_IMPULSE, dir.y * PUNCH_IMPULSE + 26, dir.z * PUNCH_IMPULSE);
    const rel = new CANNON.Vec3(target.point.x - d.body.position.x, target.point.y - d.body.position.y, target.point.z - d.body.position.z);
    d.body.wakeUp();
    d.body.applyImpulse(imp, rel);
    d.hits++;
    d.hp -= 17;
    thud(0.8, 'soft');
    punchAnim();
    if (d.standing && (d.hp <= 42 || PUNCH_IMPULSE >= DUMMY_FALL_IMPULSE)) {
      d.standing = false; d.resetT = 8;
      toast(`${d.name}: сбит с ног!`);
    } else {
      toast(`${d.name}: удар ${d.hits} (HP ${Math.max(0, d.hp | 0)})`);
    }
    return;
  }
  punchAnim();                                   // удар в воздух
}
let punchT = 0;
function punchAnim() { punchT = 0.22; }

// ---------------------------------------------------------------- главный цикл
let last = performance.now(), fpsAcc = 0, fpsN = 0, fps = 60, statT = 0;
const tmpV = new THREE.Vector3();

function tick(now) {
  requestAnimationFrame(tick);
  let dt = Math.min(0.05, (now - last) / 1000);
  last = now;
  fpsAcc += dt; fpsN++;
  if (fpsAcc > 0.5) { fps = fpsN / fpsAcc; fpsAcc = 0; fpsN = 0; }

  // --- обзор
  const sens = 0.0032;
  player.yaw -= input.look.x * sens;
  player.pitch = Math.max(-1.45, Math.min(1.45, player.pitch - input.look.y * sens));
  input.look.x = 0; input.look.y = 0;

  // --- движение
  const kx = (keys.KeyD || keys.ArrowRight ? 1 : 0) - (keys.KeyA || keys.ArrowLeft ? 1 : 0);
  const kz = (keys.KeyW || keys.ArrowUp ? 1 : 0) - (keys.KeyS || keys.ArrowDown ? 1 : 0);
  let mx = input.move.x + kx, my = input.move.y + kz;
  const ml = Math.hypot(mx, my);
  if (ml > 1) { mx /= ml; my /= ml; }
  const speed = input.run || keys.ShiftLeft ? RUN : WALK;
  const sin = Math.sin(player.yaw), cos = Math.cos(player.yaw);
  const wx = (mx * cos - my * sin) * speed;
  const wz = (-mx * sin - my * cos) * speed;
  player.vel.x += (wx - player.vel.x) * Math.min(1, dt * 12);
  player.vel.z += (wz - player.vel.z) * Math.min(1, dt * 12);
  player.vel.y += GRAV * dt;
  player.pos.x += player.vel.x * dt;
  player.pos.z += player.vel.z * dt;
  player.pos.y += player.vel.y * dt;
  const limX = ARENA_W / 2 - 0.4, limZ = ARENA_D / 2 - 0.4;
  player.pos.x = Math.max(-limX, Math.min(limX, player.pos.x));
  player.pos.z = Math.max(-limZ, Math.min(limZ, player.pos.z));
  if (player.pos.y < EYE) {
    if (player.vel.y < -6) thud(Math.min(0.7, -player.vel.y / 14), 'soft');
    player.pos.y = EYE; player.vel.y = 0; player.onGround = true;
  } else player.onGround = false;
  if (input.jump && player.onGround) { player.vel.y = JUMP; player.onGround = false; }
  input.jump = false;

  // покачивание камеры
  const hs = Math.hypot(player.vel.x, player.vel.z);
  if (player.onGround && hs > 0.4) player.bob += dt * (6 + hs * 1.6);
  const bobY = Math.sin(player.bob * 2) * 0.035 * Math.min(1, hs / WALK);
  const bobX = Math.cos(player.bob) * 0.022 * Math.min(1, hs / WALK);

  camera.position.set(player.pos.x + bobX, player.pos.y + bobY, player.pos.z);
  camera.rotation.order = 'YXZ';
  camera.rotation.y = player.yaw;
  camera.rotation.x = player.pitch;
  camera.rotation.z = Math.sin(player.bob) * 0.006 * Math.min(1, hs / WALK);

  // --- руки: качание + анимация удара
  punchT = Math.max(0, punchT - dt);
  const pk = punchT > 0 ? Math.sin((0.22 - punchT) / 0.22 * Math.PI) : 0;
  handR.position.z = -0.55 + pk * 0.22;
  handL.position.z = -0.55 + pk * 0.10;
  hands.position.y = -bobY * 0.6;
  hands.rotation.z = -Math.sin(player.bob) * 0.02 * Math.min(1, hs / WALK);

  // --- физика
  world.step(1 / 60, dt, 4);

  // удерживаемый предмет — пружиной к точке перед камерой
  if (held) {
    if (held.broken) held = null;
    else {
      camera.getWorldDirection(tmpV);
      const target = tmpV.multiplyScalar(1.9).add(camera.position); target.y -= 0.25;
      const b = held.body;
      b.wakeUp();
      b.velocity.set((target.x - b.position.x) * 14, (target.y - b.position.y) * 14, (target.z - b.position.z) * 14);
      b.angularVelocity.scale(0.86, b.angularVelocity);
      phoneMesh.visible = true;
    }
  }

  // манекены: держат вертикаль, пока стоят; встают после кувырка
  for (const d of dummies) {
    if (d.standing) {
      d.body.angularVelocity.set(0, d.body.angularVelocity.y * 0.5, 0);
      const q = d.body.quaternion;
      const e = new CANNON.Vec3();
      q.toEuler(e);
      const tilt = Math.hypot(e.x, e.z);
      if (tilt > 0.001) {
        const k = Math.min(1, dt * 6);
        q.setFromEuler(e.x * (1 - k), e.y, e.z * (1 - k));
      }
      if (d.body.position.y < -0.4) { d.body.position.y = 0; d.body.velocity.y = 0; }
    } else if (d.resetT > 0) {
      d.resetT -= dt;
      if (d.resetT <= 0 && d.body.velocity.length() < 1.6) {
        d.body.position.set(d.spawn.x, 0, d.spawn.z);
        d.body.quaternion.setFromEuler(0, d.spawn.ry, 0);
        d.body.velocity.setZero(); d.body.angularVelocity.setZero();
        d.standing = true; d.hp = 100;
      }
    }
    d.group.position.copy(d.body.position);
    d.group.quaternion.copy(d.body.quaternion);
  }

  // синхронизация предметов
  for (const p of props) {
    if (p.broken) continue;
    p.mesh.position.copy(p.body.position);
    p.mesh.quaternion.copy(p.body.quaternion);
    if (p === held) p.mesh.position.copy(p.body.position);
  }
  for (let i = shards.length - 1; i >= 0; i--) {
    const s = shards[i];
    s.t -= dt;
    s.mesh.position.copy(s.body.position);
    s.mesh.quaternion.copy(s.body.quaternion);
    if (s.t <= 0) { scene.remove(s.mesh); world.removeBody(s.body); shards.splice(i, 1); }
  }

  // --- действие
  if (input.actionEdge) { input.actionEdge = false; doAction(); }

  // --- HUD
  const target = aim();
  if (target) {
    ui.cross.classList.add('on');
    ui.prompt.textContent = held ? `БРОСИТЬ: ${held.name}` : (target.type === 'prop' ? `ВЗЯТЬ: ${target.prop.name}` : `УДАРИТЬ: ${target.dummy.name}`);
  } else {
    ui.cross.classList.remove('on');
    ui.prompt.textContent = held ? `БРОСИТЬ: ${held.name}` : '';
  }
  statT -= dt;
  if (statT <= 0) {
    statT = 0.25;
    ui.stats.textContent =
      `FPS ${fps.toFixed(0)}  |  предметов ${props.filter(p => !p.broken).length}  |  осколков ${shards.length}\n` +
      dummies.map(d => `${d.name}: ${d.standing ? 'стоит' : 'ЛЕЖИТ'} HP${Math.max(0, d.hp | 0)}`).join('  ') +
      `\nВ руках: ${held ? held.name : '—'}`;
  }

  renderer.render(scene, camera);
}

addEventListener('resize', () => {
  camera.aspect = innerWidth / innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(innerWidth, innerHeight);
});

// смартфон стартует в руке — как в Unity-версии
held = phoneProp;
toast('Полигон готов. Джойстик слева, обзор справа, ДЕЙСТВИЕ — взять/бросить/ударить');
requestAnimationFrame(tick);
