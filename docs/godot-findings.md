# Godot findings (4.7.2)

1. `ViewportTexture` as `light_projector` is refused; `ImageTexture.Update()` doesn't refresh it (the atlas copies a texture
   only when added): new `ImageTexture` per change. Mask image top = beam top, left = beam left, seen from behind the fixture.
2. Volumetric fog ignores `light_projector`: custom `beam_volume.gdshader` (ray/frustum clip, scene-depth clip, 48 jittered
   steps, Henyey-Greenstein). Occlusion by scenery: finding 12. Gap: no extinction.
3. **A spot with a projector and `ShadowEnabled = false` gives no light.** Use `ShadowEnabled = true` + `ShadowCasterMask = 0`.
   The spot shadow map also cuts ~5° inside a very wide cone (floods use 89° and fade out by 85°).
4. `Camera3D.UnprojectPosition` returns canvas coordinates, not image pixels: scale by image size / `GetVisibleRect().Size`.
   The window doesn't stretch (`window/stretch/mode = disabled`); UI scale is a preference.
5. A Control's `_Ready` runs immediately when added to a tree that is already running; don't subscribe to `Ready` afterwards.
6. SubViewports sharing the root's `World3D` show the same scene; set their positional shadow atlas like the root's. The root
   has `Disable3D` while the view host shows 3D views.
7. Shadow atlas: Godot's default (88 places) leaves every lamp after the 88th dark. `ShowBuilder.ConfigureShadowAtlas` (4096,
   340 places) on every viewport that renders the stage (window, `View3D`, `ExportShooter`). Lamps keep a place once they have one.
8. **Exit with a native file dialog open hangs** (`~DisplayServerWindows` joins the dialog thread, but its owner is a hidden
   window that gets no message). `AutoAcceptQuit = false`; `FixturePanel._Notification` refuses a close while
   `NativeFileDialogs.AnyOpen` (a second close within 10 s kills the process). Open every native dialog via `NativeFileDialogs.Popup`.
9. `FileDialog` replaces its `Title` unless `ModeOverridesTitle = false` (set before `FileMode`). A wrapping `Label` in a dialog
   needs a minimum width or the dialog grows as tall as a one-character column.
10. Native tool windows (`Window.ForceNative`): set it while hidden. The `Window` node gets the *parent* viewport's unhandled
    input, nodes inside it get the window's own: key handlers must be child nodes (`ToolWindowKeys`). `GetTexture()` of a native
    window gives its own picture.
11. **An export template refuses a scene on the command line**: `Kloarte.exe ... res://render/show_render.tscn` logs "Scene
    path was specified on the command line, but this Godot binary was compiled without support for path overrides. Aborting."
    (release templates have `disable_path_overrides`), so the movie process quit without an AVI. The exported build's main
    scene is `boot.tscn` (`HostBoot`, `run/main_scene.dist`): with `-- --render-movie` it adds the render scene (no frame
    lost), else it changes to the show scene. `--write-movie` / `--fixed-fps` still work in a template.
12. **Occlusion of haze beams by scenery** (`BeamOcclusion`, `ProfileFixture.Occlusion.cs`, 27 Oct 2026). Per lamp a 128x128
    `SubViewport` sharing the World3D, camera at the lens (fov = full cone, aspect 1, far = beam length, `CullMask` =
    `RenderLayers.Occluders` only, own `Environment`, `DebugDraw = Unshaded`, no shadow atlas / MSAA / TAA), rendered
    `UpdateMode.Once`. Depth: a full-screen quad (`occlusion_depth.gdshader`, transparent, reads `hint_depth_texture`)
    writes linear depth / range; `UseHdr2D` makes the target RGBA16F *linear* (no sRGB, no clamp: read back exact to 1e-4),
    so the `ViewportTexture` is a ShaderMaterial uniform as is (only `light_projector` refuses one). Materials can't be
    overridden per camera, hence the quad: it sits on the Occluders layer and collapses in its vertex shader unless
    `CAMERA_VISIBLE_LAYERS` is exactly that layer and the camera stands at the quad (= its own lamp), and is `Visible` only
    in its render frame. The quad lies behind the near plane: give it a `CustomAabb` in front or it's culled. The map's
    image top = beam top, like the mask: the beam shader samples both at the same uv.
    Cost control: `RenderingServer.InstancesCullConvex(camera.GetFrustum())` on the CPU decides whether any Occluders
    instance is in the beam (none: no map, no lookup) and gives the nearest AABB depth (no lookup nearer the lens); maps
    re-render only on aim / cone / length change or `InvalidateOcclusion()`, 4 per frame (FIFO), a moving lamp at most
    every 4 frames. **Measured** (`tests/occlusion_bench.tscn`, Risquons-Tout RT2025_044: 74 lamps, 65 beams at full, haze 50 %,
    4 borders + 8 legs cutting 49 beams, 1600x900, GTX 1650 *shared with another Godot process*, off / on alternated in
    30-frame blocks): frame time +13 % (median of 16 pairs; 256² maps +15 %, without the near / cull skips +23-25 %), moving
    heads panning +12 %; a full re-render of 49 maps = 16 frames at 4 per frame, all at once +10-100 ms in one frame (noisy).
    Re-measured on a quiet GPU with the real Kruimels rig (Kruimels_011: 18 lamps, 8 moving heads, 13 maps): steady
    +5.5 % (9.5 -> 10.1 ms), moving heads panning +13 %, a full re-render 6 frames (worst ~55 ms at 4 per frame, ~14 ms all
    at once). Within the budget for the real show, so **on by default** (`BeamOcclusion.Enabled`). `ShowBuilder` invalidates
    every lamp's map whenever the occluders change (`NotifyOccludersChanged`: build, scenery rebuild, a piece in / out, a
    curtain moving).
