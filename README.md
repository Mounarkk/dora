# 3D Scene Exploration Through Head and Gaze Positions

## Abstract

This project explores how an observer’s head and gaze positions can be used to interact with a 3D environment, simulating a window‑like experience where moving one's head alters the point of view on the scene. Gaze data may also drive dynamic modifications to the environment.

## Project Principle

We aim to create a non‑invasive immersive setup that allows users to explore a 3D scene naturally through:

- **Head position tracking** :
   dynamically update the camera position, simulating a physical window into a 3D world.

- **Gaze tracking** :
   gather perceptual data or drive scene changes (e.g., light, material, geometry), supporting experiments on visual attention and change blindness.

This interaction opens possibilities to manipulate the visual experience subtly, leveraging effects and perceptual phenomenon.

---

## ⚙️ Setup

This repository tracks **only the project's own work**: the Unity scenes, the C# scripts,
the custom Pupil Capture plugin and the scene-specific materials. The third-party binaries
and asset packs it depends on are not redistributed here — several of them exceed GitHub's
file-size limits, and all of them are freely available upstream.

Install the following at the versions listed, then place them at the indicated paths.

| Dependency | Version | Where to get it | Install to |
|---|---|---|---|
| Unity Editor | `6000.1.1f1` | [Unity Hub](https://unity.com/download) (Archive → Unity 6) | — |
| OpenTrack | `2024.1.1` | [github.com/opentrack/opentrack/releases](https://github.com/opentrack/opentrack/releases) | `OpenTrack/` |
| Pupil Core (Pupil Capture) | `3.6.20` | [github.com/pupil-labs/pupil/releases](https://github.com/pupil-labs/pupil/releases) | `pupil/` |
| Unity HDRI Pack | latest | Unity Asset Store — *Unity HDRI Pack* by Unity Technologies (free) | `Assets/Resources/Skyboxes/UnityTechnologies/` |
| Yughues Free Metal Materials | latest | Unity Asset Store — *Yughues Free Metal Materials* (free) | `Assets/Resources/Materials/YughuesFreeMetalMaterials/` |

> **Import the Asset Store packages through the Unity Package Manager** (Window → Package Manager
> → My Assets), not by copying files in by hand. The packages carry their own `.meta` files, so
> importing them restores the exact asset GUIDs that `MainScene` references. Copying the raw
> textures in would generate fresh GUIDs and leave the scene with missing materials.

Most users only need **Pupil Capture** — the prebuilt application from the Pupil Core releases
page — rather than the full Python source tree.

### Pupil Capture configuration

Our own Pupil Capture files are tracked under `tools/` and must be copied into your local
Pupil install, which is otherwise ignored by Git:

```bash
# Custom nine-point calibration choreography
cp tools/pupil-plugins/nine_point_calibration.py pupil/capture_settings/plugins/

# Camera intrinsics, surface definitions and recorded calibration
cp tools/pupil-capture-settings/* pupil/capture_settings/
```

On Windows, `capture_settings/` lives in `%USERPROFILE%\pupil_capture_settings\` when you run
the bundled application instead of the source tree — copy the same files there.

After restarting Pupil Capture, **Nine-point Screen Marker** becomes selectable as a
calibration choreography in the Calibration plugin.

### Pupil Core source modifications

This project relies on three small patches to the Pupil Core source, kept as a patch file
because the upstream tree itself is not tracked here:

* `surface_tracker.py` — exposes `add_surface()` / `remove_surface()` and attaches the
  accuracy and precision metrics from `Accuracy_Visualizer` to surface events.
* `version_utils.py` — restores `packaging.version.LegacyVersion` in the `ParsedVersion`
  union, needed with the `packaging` release pinned by `requirements.txt`.
* `surface.py` — docstring correction.

Apply them to a clean Pupil Core `3.6.20` checkout:

```bash
cd pupil
git apply ../tools/pupil-patches/pupil-core-3.6.20-modifications.patch
```

These are only required when running Pupil Capture **from source**. The prebuilt application
does not expose these modules.

### Head tracking

OpenTrack must be configured to output over **UDP to `127.0.0.1:4242`**, which is the port
`UDPReceiver` binds to. Set *Output* to "UDP over network" in the OpenTrack main window and
configure the destination address there.

---

## 📖 Documentation

To be able to execute properly the Unity project, the documentation must be read in order to understand
the systems used and how to set them up.

### 1. Technical API reference (Doxygen)

A full C# API reference can be generated from the source comments using Doxygen.
The generated output is **not tracked in Git** — build it first (requires Doxygen and
Graphviz):

```bash
cd Documentation && doxygen Doxyfile
```

Then browse it locally:

* **Windows**

  ```bat
  start Documentation/html/index.html
  ```
* **macOS**

  ```bash
  open Documentation/html/index.html
  ```
* **Linux**

  ```bash
  xdg-open Documentation/html/index.html
  ```

The Doxygen configuration and the hand‑written main page live under `Documentation/`
(`Doxyfile` and `markdowns/main.md`); the `html/` and `latex/` output directories are
regenerated by the command above.

### 2. High-Level Documentation

A high‑level design document (in French) is available here :

* `Documentation haut-niveau du prototype.pdf` (at the repository root)

It covers systems architectures, module descriptions and user‑level workflows.

---

## 🛠️ Tools & Technologies Used

* **Unity** (C#) – real‑time 3D engine
* **OpenTrack** – non‑invasive IR‑based head tracking open-source software
* **Pupil Core** - eye-tracking platform consisting of a wearable eye-tracking headset
                    and Pupil Capture, an open-source software for gathering and processing data from the
                    headset.
---

## ⚠️ Known Issues

- Positions sent by the head-tracker system are in centimeters and used in Unity directly, without
  proper scaling to Unity units. However, this doesn't seem to create unwanted behavior during head movements since
  the used scene "MainScene" objects are also not to scale.
- Change blindness experiments like "ColorInFoV" or "ColorOutFoV" use materials of the targeted objects.
  Since we use custom materials with custom shaders for the search light, Unity don't have access to their color
  property. The search light has to be deactivated and the original materials of the targeted objects have to be used
  in order for these experiments to work.
- One of the post-process effects, "Directional Light Rotation" doesn't work.
---

## 🙋 Authors & License

* **Authors:** Pascal Barla, Jean Basset, Aymen Ali Yahia (intern), Mohammed Douidy (intern)
* **Inria – MANAO team**

