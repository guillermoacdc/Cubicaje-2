# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**CubicajeHL3** is a Unity Mixed Reality application for HoloLens 2. It guides a user through assembling a physical cubic puzzle by scanning QR codes on pieces and displaying holographic feedback in the correct positions.

## Build & Deploy

This is a Unity project targeting **UWP / HoloLens 2**. There is no CLI build script — all builds go through the Unity Editor:

1. Open the project in Unity (tested with the version matching `ProjectSettings/ProjectVersion.txt`)
2. Build target: **Universal Windows Platform** → ARM64
3. XR Plugin: **OpenXR** with Microsoft HoloLens feature set enabled
4. Deploy the generated Visual Studio `.sln` to the device via the HoloLens Device Portal or USB

All MRTK and Mixed Reality packages are vendored locally under `Packages/MixedReality/` as `.tgz` files — no package registry needed.

## Architecture

### Two Independent Operational Flows

**Flow 1 — Piece Catalog Scan** (`PieceScanManager.cs`)
- Scans QR codes with format `"ID;alto_m;ancho_m;fondo_m"` (e.g. `"A1;0.166;0.083;0.0415"`)
- Displays a cyan hologram at the QR location for 1 second per scan
- Auto-saves all scanned pieces to `Application.persistentDataPath/piezas.json` after each scan
- Pressing the MRTK3 "Detener" button finalizes and quits the app

**Flow 2 — Assembly Guidance** (`CubicajeARManager.cs`)
- Scans a `"PALLET"` QR to anchor a holographic hollow-box pallet in world space (via `ARAnchor`)
- Then guides the user through 12 pieces in a hardcoded sequence: `A1→A2→A3→B1→B2→B3→B4→B5→B6→A4→A5→A6`
- Correct piece: green hologram travels along a quadratic Bézier curve into its slot in the pallet
- Wrong piece: red hologram appears at the QR location for ~1 second
- Completion: back wall of the pallet pulses to bright green

### Flow Control (`StartMenuManager.cs`)
- On start, shows an MRTK3 billboard menu and disables both `PieceScanManager` and `ARMarkerManager`
- **Accept** button: fades out menu, enables `markerManager` and `scanManager`
- **Cancel** button: quits the application

### Data Model (`PieceEntry.cs`)
- `PieceEntry`: `{ id, alto_m, ancho_m, fondo_m }` — dimensions in Unity meters (Y=alto, X=ancho, Z=fondo)
- `PieceListWrapper`: JSON serialization wrapper for `List<PieceEntry>`

### Key Design Decisions
- Piece positions/rotations for all 12 slots (6×A, 6×B) are **hardcoded** in `CubicajeARManager.defs[]` as `PieceDef` structs — not loaded from the JSON
- QR spam is suppressed by two independent cooldowns: per-text (`qrCooldown`) and global (`qrGlobalCooldownSeconds`), both configurable in the Inspector
- The pallet is procedurally built from primitive Cubes at runtime (`CreateHollowPallet`), using `Legacy Shaders/Transparent/Diffuse` for semi-transparent walls
- The pallet faces the camera at the moment of first detection and does not track the QR further (locked via `ARAnchor`)

## Scenes

- `Assets/Scenes/SampleScene.unity` — main scene (assembly guidance flow)
- `Assets/Cambio con letrero.unity` — alternate scene (purpose unknown from code alone)

## QR Code Formats

| Context | Format | Example |
|---|---|---|
| Pallet marker | `PALLET` | `PALLET` |
| Assembly piece | bare ID | `A1`, `B3` |
| Scan-mode piece | `ID;alto;ancho;fondo` | `A1;0.166;0.083;0.0415` |

---

## Rol de la IA

Actúa como un desarrollador senior de Unity especializado en AR/MR con HoloLens 2 y MRTK3.
Antes de escribir cualquier línea de código:
- Explora la estructura del proyecto y los scripts existentes
- Haz las preguntas necesarias para entender el contexto
- Diseña la solución en texto antes de implementarla
- Evalúa si cada cambio puede generar efectos en cascada sobre otros scripts
- Prefiere soluciones simples, legibles y fáciles de debuggear sobre soluciones complejas

---

## Stack técnico

- Motor: Unity 2022.3.62f1
- Plataforma destino: HoloLens 2
- Framework AR: MRTK3
- Lenguaje: C#

---

## Descripción del proyecto

Se recreará una zona de empaquetado en AR. El flujo completo es:

1. Un operador (packer) con HoloLens 2 escanea un código QR de una orden
2. El sistema recupera los datos de la orden: lista de piezas con sus dimensiones y datos del pallet destino
3. Un algoritmo de cubicaje calcula la orientación y posición óptima (x, y, z) de cada pieza dentro del pallet
4. El algoritmo genera una secuencia de acomodación: primero las piezas con menor Y (piso), priorizando las más cercanas al origen (0,0,0) del pallet, llenando de atrás hacia adelante
5. El operador escanea el QR de cada caja física (ej: ID "A1")
6. Al escanear, aparece un holograma de esa pieza que viaja a su posición y orientación correcta dentro del pallet virtual
7. Si la pieza escaneada no corresponde a la siguiente en la secuencia, el holograma aparece en rojo y no se desplaza
8. Al completar toda la secuencia, aparece un mensaje de finalización

El script CubicajeAR existente tiene un ejemplo hardcodeado de este flujo.
Ese es el punto de partida y la guía de referencia de estilo y estructura.
El objetivo es hacer ese flujo dinámico, no reemplazar la lógica sino alimentarla con datos reales.

---

## Flujo de trabajo

- Claude propone → Julian despliega en HoloLens 2 → Julian da feedback → se ajusta → siguiente componente
- No avanzar al siguiente componente sin confirmación
- Si se detecta riesgo de romper algo existente, avisar antes de proceder
- Si se necesita ver un script específico, pedirlo explícitamente

---

## Estilo de código

- Comentarios en cada método explicando qué hace y por qué
- Variables con nombres descriptivos, consistentes en todo el proyecto
- Métodos cortos con responsabilidad única
- Sin lógica compleja en un solo bloque; preferir funciones auxiliares con nombres claros
- Si algo no está claro, preguntar antes de asumir
- Nunca generar scripts con demasiada complejidad; priorizar que sean legibles y debuggeables

---

## Paso 0 — Inicio de cada sesión

Antes de cualquier implementación:
1. Leer todos los scripts existentes del proyecto
2. Identificar qué está implementado vs qué falta
3. Presentar un mapa de componentes: qué script hace qué y qué se conecta con qué
4. Proponer el plan por fases antes de escribir código

No escribir ningún script hasta que Julian apruebe el plan.
