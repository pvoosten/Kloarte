# Godot findings (4.7.2)

1. `ViewportTexture` as `light_projector` is refused; `ImageTexture.Update()` doesn't refresh it (the atlas copies a texture
   only when added): new `ImageTexture` per change. Mask image top = beam top, left = beam left, seen from behind the fixture.
2. Volumetric fog ignores `light_projector`: custom `beam_volume.gdshader` (ray/frustum clip, scene-depth clip, 48 jittered
   steps, Henyey-Greenstein). Gaps: no occlusion by scenery inside the beam (open), no extinction.
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
