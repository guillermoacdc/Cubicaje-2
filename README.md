# HoloLens Baseline

## Purpose

This branch establishes a stable baseline for the HoloLens 2 application before introducing new packing and interaction features.

The objective is to provide a reproducible starting point that has been validated on-device and can be used for future development and regression testing.

---

## Environment

| Component | Version |
|----------|---------|
| Unity | 2022.3.62f1 LTS |
| MRTK | MRTK3 |
| OpenXR | Enabled |
| Target Device | Microsoft HoloLens 2 |
| Build Target | UWP ARM64 |

---

## Current Workflow

The application starts directly without displaying the initial menu.

Current execution flow:

1. Application starts.
2. QR marker tracking is enabled.
3. Wait for the **PALLET** marker.
4. Create and anchor the virtual pallet.
5. Start the predefined packing sequence.
6. Guide the user through the assembly process.
7. Finish after the last predefined piece has been placed.

The predefined packing sequence does not require loading external JSON files.

---

## Main Components

### CubicajeARManager

Responsible for:

- Application startup.
- Waiting for the pallet marker.
- Creating the virtual pallet.
- Managing the predefined assembly sequence.
- Providing visual guidance for piece placement.

### ARMarkerManager

Responsible for:

- QR marker detection.
- Marker pose estimation.
- Marker event notifications.

### PieceScanManager

Currently disabled in this baseline because predefined packing is used instead of scanning individual pieces.

---

## Scene Configuration

The scene is configured as follows:

- Start Menu disabled.
- ARMarkerManager enabled.
- CubicajeARManager active.
- PieceScanManager component disabled.
- Direct startup enabled.

---

## Validation Status

Validated on HoloLens 2.

Successful tests include:

- Application startup.
- PALLET QR detection.
- Virtual pallet creation.
- Pallet anchoring.
- Predefined packing sequence.
- Guided assembly visualization.

---

## Known Limitations

- Only predefined packing is supported.
- Dynamic piece acquisition is disabled.
- JSON-based packing generation is not used.
- Single pallet workflow.

---

## Future Work

Planned improvements include:

- Restore dynamic piece scanning.
- Support loading packing definitions from external files.
- Improve interaction feedback.
- Optimize HoloLens memory usage.
- Add automated regression tests.

---

## Branch Purpose

This branch serves as the reference baseline for future HoloLens development.

All new features should be implemented from this validated state.