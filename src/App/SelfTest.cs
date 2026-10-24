using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Chitin;

internal static class SelfTest
{
    private static int _failedChecks;

    public static int Run(string path)
    {
        try
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();

            const int viewportWidth = 1400;
            const int viewportHeight = 900;

            // The gate scene keeps atelier, so its export hash still proves chrome stays out.
            var world = new World();
            world.SetPalette("atelier");
            world.VanishingPoints[0] = new VanishingPoint(new Vec2(-420, -140), 0);
            world.VanishingPoints[1] = new VanishingPoint(new Vec2(380, -150), 0);
            world.VanishingPoints[2] = new VanishingPoint(new Vec2(60, -900), 8);
            world.VanishingPoints[4] = new VanishingPoint(new Vec2(1500, 60), 6);
            world.TraceLines.Add(new TraceLine(new Vec2(-500, 300), new Vec2(200, -250)));
            world.TraceLines.Add(new TraceLine(new Vec2(0, -400), new Vec2(300, 350)));
            world.ShowGrid = true;
            world.GridStyle = 0;
            world.GridSz = 40;
            world.SelectionIdx = 2;
            world.Zoom = 1.25;
            world.Camera = new Vec2((viewportWidth / 2) / world.Zoom, (viewportHeight / 2) / world.Zoom);
            world.ZoomChanged();

            string exportPath = Path.Combine(
                Path.GetDirectoryName(path) ?? ".",
                Path.GetFileNameWithoutExtension(path) + "-export" + Path.GetExtension(path));
            string shapesPath = Path.Combine(
                Path.GetDirectoryName(path) ?? ".",
                Path.GetFileNameWithoutExtension(path) + "-shapes" + Path.GetExtension(path));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

            Render(world, viewportWidth, viewportHeight, RenderMode.Interactive, path);

            world.Zoom = 1;
            world.Camera = new Vec2(world.PicturePlaneWidthHalf, world.PicturePlaneHeightHalf);
            world.ZoomChanged();
            Render(world, world.PicturePlaneWidth, world.PicturePlaneHeight, RenderMode.OpaqueExport, exportPath);

            World shapesWorld = BuildShapesWorld();
            Render(shapesWorld, 640, 480, RenderMode.OpaqueExport, shapesPath);

            bool collinear = Geom.GetLineIntersection(new Vec2(0, 0), new Vec2(10, 0),
                new Vec2(20, 0), new Vec2(30, 0), 1000, out _);
            bool parallel = Geom.GetLineIntersection(new Vec2(0, 0), new Vec2(10, 0),
                new Vec2(0, 1), new Vec2(10, 1), 1000, out _);
            bool cross = Geom.GetLineIntersection(new Vec2(0, 0), new Vec2(10, 10),
                new Vec2(0, 10), new Vec2(10, 0), 1000, out Vec2 crossPoint);
            Check("geom_collinear", !collinear);
            Check("geom_parallel", !parallel);
            Check("geom_cross", cross &&
                Math.Abs(crossPoint.X - 5) <= 1e-6 && Math.Abs(crossPoint.Y - 5) <= 1e-6);

            BuiltBox exactBox = BoxGeometry.Build(new Vec2(0, 0), new Vec2(100, 0),
                new Vec2(0, 80), new Vec2(10, -50), new Vec2(1, 1), new Vec2(-1, 1));
            Vec2[] expectedVertices =
            {
                new(0, 0), new(100, 0), new(90, -10), new(0, 80),
                new(10, -50), new(110, -50), new(100, -60), new(10, 30)
            };
            bool exactVertices = exactBox.Vertices.Length == 8 &&
                exactBox.Vertices.Zip(expectedVertices).All(pair =>
                    Math.Abs(pair.First.X - pair.Second.X) <= 1e-4 &&
                    Math.Abs(pair.First.Y - pair.Second.Y) <= 1e-4);
            BuiltBox parallelBox = BoxGeometry.Build(new Vec2(0, 0), new Vec2(100, 0),
                new Vec2(20, 80), new Vec2(0, -30), new Vec2(1, 0), new Vec2(1, 0));
            Check("box_geometry", exactVertices &&
                Math.Abs(parallelBox.Vertices[2].X - 120) <= 1e-9 &&
                Math.Abs(parallelBox.Vertices[2].Y - 80) <= 1e-9);

            Check("box_visibility", BoxVisibility());

            Vec2[] projectedQuad = ProjectedQuad();
            Homography identity = Homography.FromUnitSquare(new Vec2(0, 0), new Vec2(1, 0),
                new Vec2(1, 1), new Vec2(0, 1));
            bool identityOk = identity.IsInvertible &&
                Distance(identity.Map(0, 0), new Vec2(0, 0)) <= 1e-9 &&
                Distance(identity.Map(1, 0), new Vec2(1, 0)) <= 1e-9 &&
                Distance(identity.Map(1, 1), new Vec2(1, 1)) <= 1e-9 &&
                Distance(identity.Map(0, 1), new Vec2(0, 1)) <= 1e-9;
            Check("homography_identity", identityOk);
            Homography degenerate = Homography.FromUnitSquare(new Vec2(0, 0), new Vec2(1, 0),
                new Vec2(2, 0), new Vec2(3, 0));
            Check("homography_degenerate", !degenerate.IsInvertible);

            Homography perspective = Homography.FromUnitSquare(projectedQuad[0], projectedQuad[1],
                projectedQuad[2], projectedQuad[3]);
            Vec2 homographyCentre = perspective.Map(0.5, 0.5);
            Geom.IntersectLines(projectedQuad[0],
                projectedQuad[2] - projectedQuad[0],
                projectedQuad[1],
                projectedQuad[3] - projectedQuad[1], out Vec2 diagonalCentre);
            Check("subdivision_centre",
                perspective.IsInvertible && Distance(homographyCentre, diagonalCentre) <= 1e-6);
            Vec2? inverse = perspective.MapInverse(perspective.Map(0.3, 0.7));
            Check("homography_inverse",
                inverse is { } unit && Distance(unit, new Vec2(0.3, 0.7)) <= 1e-9);

            (Vec2 A, Vec2 B)[] subdivisionLines = SubdivisionGeometry.BuildLines(projectedQuad, 4, 3);
            Check("subdivision_counts", subdivisionLines.Length == 5 &&
                SubdivisionGeometry.CellCount(4, 3) == 12);

            Vec2 vanishingPoint = new Vec2(0, 0);
            Vec2 edgeNear = new Vec2(300, 0);
            Vec2 edgeFar = new Vec2(150, 0);
            Vec2[] edgeTwo = PerspectiveEdge.Divide(edgeNear, edgeFar, vanishingPoint, 2);
            Vec2[] edgeFour = PerspectiveEdge.Divide(edgeNear, edgeFar, vanishingPoint, 4);
            Vec2[] edgeUniform = PerspectiveEdge.Divide(edgeNear, edgeFar, null, 4);
            Check("edge_division",
                Math.Abs(edgeTwo[1].DistFrom(vanishingPoint) - 200) <= 1e-4 &&
                Math.Abs(edgeFour[1].DistFrom(vanishingPoint) - 240) <= 1e-4 &&
                Math.Abs(edgeFour[2].DistFrom(vanishingPoint) - 200) <= 1e-4 &&
                Math.Abs(edgeFour[3].DistFrom(vanishingPoint) - 171.42857142857142) <= 1e-4 &&
                Math.Abs(edgeUniform[2].X - 225) <= 1e-9);

            Vec2[] circleQuad =
            {
                new(0, 0), new(100, 0), new(100, 100), new(0, 100)
            };
            Vec2[] circlePoints = EllipseGeometry.Sample(circleQuad, 0.5, 0.5, 0.5);
            Vec2 circleCentre = new Vec2(50, 50);
            Check("ellipse_is_circle", circlePoints.Length == EllipseGeometry.SampleCount &&
                circlePoints.All(point => Math.Abs(point.DistFrom(circleCentre) - 50) <= 0.5));

            Vec2[] ellipsePoints = EllipseGeometry.Sample(projectedQuad, 0.5, 0.5, 0.5);
            bool insideQuad = ellipsePoints.Length == EllipseGeometry.SampleCount &&
                ellipsePoints.All(point => BoxGeometry.ContainsQuad(projectedQuad, point));
            bool touchesEdges = ellipsePoints.Length == EllipseGeometry.SampleCount;
            for (int i = 0; touchesEdges && i < projectedQuad.Length; i++)
            {
                Vec2 a = projectedQuad[i];
                Vec2 b = projectedQuad[(i + 1) % projectedQuad.Length];
                if (ellipsePoints.Min(point => Geom.DistanceToSegment(point, a, b)) > 1e-4)
                {
                    touchesEdges = false;
                }
            }

            Check("ellipse_perspective", insideQuad && touchesEdges);

            bool stationOk = ConeOfVision.TryBuild(new Vec2(-400, 0), new Vec2(400, 0), 640,
                out ConeOfVision cone);
            Check("station_distance", stationOk &&
                Math.Abs(cone.Distance - 400) <= 1e-6 &&
                Math.Abs(cone.FovDegrees - 77.3196) <= 0.001 &&
                Math.Abs(cone.Radius - 230.9401) <= 0.001 &&
                !ConeOfVision.TryBuild(new Vec2(-400, 0), new Vec2(-200, 0), 640, out _) &&
                !ConeOfVision.TryBuild(new Vec2(0, 0), new Vec2(400, 0), 640, out _));

            var coneWorld = new World();
            coneWorld.ShowCone = true;
            coneWorld.VanishingPoints[0] = new VanishingPoint(new Vec2(-400, 0), 0);
            coneWorld.VanishingPoints[1] = new VanishingPoint(new Vec2(400, 0), 0);
            coneWorld.BumpRevision();
            bool coneDrawn = GuideBuilder.Build(coneWorld, coneWorld.Styles)
                .Any(prim => ReferenceEquals(prim.Pen, coneWorld.Styles.ConePen));
            bool coneSvg = false;
            try
            {
                XDocument coneDocument = XDocument.Parse(SvgWriter.Write(coneWorld, 640, 480));
                coneSvg = coneDocument.Descendants().Any(element =>
                    element.Name.LocalName == "circle" &&
                    (string?)element.Attribute("fill") == "none");
            }
            catch
            {
                coneSvg = false;
            }

            coneWorld.VanishingPoints[1] = new VanishingPoint(new Vec2(-200, 0), 0);
            coneWorld.BumpRevision();
            bool coneHidden = !GuideBuilder.Build(coneWorld, coneWorld.Styles)
                .Any(prim => ReferenceEquals(prim.Pen, coneWorld.Styles.ConePen));
            Check("cone_overlay", coneDrawn && coneHidden && coneSvg);

            var paletteWorld = new World();
            paletteWorld.SetPalette("atelier");
            Palette atelier = Palette.ByName("atelier");
            bool ordered = Enumerable.Range(0, 9)
                .All(i => PenColor(paletteWorld.Styles.VPLines[i]) == atelier.VpColors[i]);
            paletteWorld.RotatePalette();
            bool rotated = Enumerable.Range(0, 9)
                .All(i => PenColor(paletteWorld.Styles.VPLines[i]) == atelier.VpColors[(i + 1) % 9]);
            Check("palette_no_random", ordered && rotated);
            Check("palette_contrast", Palette.BuiltIns.All(palette =>
                palette.VpColors.All(color => Chrome.Contrast(color, palette.Paper) >= 3)));
            Check("chrome_contrast", Palette.BuiltIns.All(palette =>
                Chrome.TextPairs(palette).All(pair => Chrome.Contrast(pair.Text, pair.Background) >= 4.5)));
            Check("font_loaded", new[] { Chrome.Mono, Chrome.MonoBold }.All(face =>
                FontManager.Current.TryGetGlyphTypeface(face, out IGlyphTypeface? glyphs) &&
                glyphs.FamilyName == "B612 Mono") &&
                FontManager.Current.TryGetGlyphTypeface(Chrome.MonoBold, out IGlyphTypeface? bold) &&
                bold.Weight == FontWeight.Bold);
            Check("default_palette_ink", new World().PaletteName == "ink" &&
                Palette.ByName("no-such-palette") == Palette.Atelier &&
                ProjectFile.Read("{\"version\":1}").Palette == "atelier" &&
                ProjectFile.Read(ProjectFile.Write(new World())).Palette == "ink");
            Check("palette_no_blue", Palette.BuiltIns.All(palette =>
                palette.VpColors.All(NotBlue) && NotBlue(palette.Grid) &&
                NotBlue(palette.Ink) && NotBlue(palette.Paper) && NotBlue(palette.Border) &&
                NotBlue(palette.Void) && NotBlue(palette.Horizon) && NotBlue(palette.Prompt)));

            string paletteHash = RenderHash(PaletteScene(), 640, 480);
            string paletteHashAgain = RenderHash(PaletteScene(), 640, 480);
            var stableWorld = PaletteScene();
            Check("palette_stable", paletteHash == paletteHashAgain &&
                paletteHash == RenderHash(stableWorld, 640, 480) &&
                paletteHash == RenderHash(stableWorld, 640, 480));

            var presetWorld = new World();
            presetWorld.ApplyPreset(3);
            bool threePoint = presetWorld.VanishingPoints.Length == 9 &&
                VpAt(presetWorld, 0, -224, 48) &&
                VpAt(presetWorld, 1, 224, 48) &&
                VpAt(presetWorld, 2, 0, -384) &&
                presetWorld.VanishingPoints.Skip(3).All(point => point == null) &&
                presetWorld.VanishingPoints.Take(3).All(point => point!.Density == 0);
            presetWorld.ApplyPreset(1);
            bool onePoint = VpAt(presetWorld, 0, 0, 48) &&
                presetWorld.VanishingPoints.Skip(1).All(point => point == null);
            presetWorld.ApplyPreset(2);
            bool twoPoint = VpAt(presetWorld, 0, -224, 48) &&
                VpAt(presetWorld, 1, 224, 48) &&
                presetWorld.VanishingPoints.Skip(2).All(point => point == null);
            var presetViewport = new ViewportControl();
            presetViewport.ApplyCameraPreset(3);
            bool presetUndo = presetViewport.History.CanUndo &&
                VpAt(presetViewport.World, 0, -224, 48);
            presetViewport.Undo();
            bool presetUndone = presetViewport.World.VanishingPoints
                .All(point => point == null);
            Check("preset_three_point",
                threePoint && onePoint && twoPoint && presetUndo && presetUndone);

            var rigWorld = new World();
            rigWorld.SetPalette("ink");
            rigWorld.ShowCone = true;
            rigWorld.VanishingPoints[0] = new VanishingPoint(new Vec2(-224, 48), 0);
            rigWorld.VanishingPoints[1] = new VanishingPoint(new Vec2(224, 48), 4);
            rigWorld.SaveRig("Bedroom");
            WorldState rigProject = ProjectFile.Read(ProjectFile.Write(rigWorld));
            var rigRestored = new World();
            rigRestored.Restore(rigProject);
            bool rigSame = rigRestored.Rigs.Count == 1 &&
                SameState(rigRestored.Capture(), rigWorld.Capture());
            rigWorld.VanishingPoints[0] = new VanishingPoint(new Vec2(-100, 48), 0);
            rigWorld.SaveRig("bedroom");
            bool rigReplaced = rigWorld.Rigs.Count == 1 &&
                rigWorld.Rigs[0].Vps[0] is { } replacedPoint &&
                Math.Abs(replacedPoint.Pos.X + 100) <= 1e-9;
            var rigViewport = new ViewportControl();
            rigViewport.World.Rigs.Add(rigProject.Rigs[0].Clone());
            rigViewport.RecallRig("Bedroom");
            bool rigUndo = rigViewport.History.CanUndo &&
                VpAt(rigViewport.World, 0, -224, 48);
            rigViewport.Undo();
            bool rigUndone = rigViewport.World.VanishingPoints.All(point => point == null);
            Check("rig_roundtrip", rigSame && rigReplaced && rigUndo && rigUndone);

            World boxWorld = shapesWorld;

            bool boxRender = false;
            using (Bitmap boxPng = RenderInteractive(boxWorld, 640, 480))
            {
                BuiltBox last = BoxGeometry.Build(boxWorld.Boxes[^1]);
                Vec2 centre = Average(last.Faces[1]);
                int x = (int)Math.Round(centre.X + boxWorld.Camera.X);
                int y = (int)Math.Round(centre.Y + boxWorld.Camera.Y);
                if (x >= 0 && x < 640 && y >= 0 && y < 480)
                {
                    (byte[] pixels, int stride) = ReadPixels(boxPng);
                    (byte b, byte g, byte r, byte a) = PixelAt(pixels, stride, x, y);
                    var paper = boxWorld.ActivePalette.Paper;
                    boxRender = a == 255 && (r != paper.R || g != paper.G || b != paper.B);
                }
            }

            Check("box_render", boxRender);
            Check("shapes_svg", ShapesSvgOk(shapesWorld));

            int cappedDensity = GuideBuilder.FanDensity(
                new VanishingPoint(new Vec2(1_000_000, 0), 0));
            int manualDensity = GuideBuilder.FanDensity(
                new VanishingPoint(new Vec2(1_000_000, 0), 200));
            Check("fan_cap", cappedDensity == 96 && cappedDensity * 2 == 192 &&
                manualDensity == 200);

            var cullWorld = new World();
            cullWorld.ShowGrid = false;
            cullWorld.VanishingPoints[0] = new VanishingPoint(new Vec2(1_000_000, 0), 0);
            int culledFanLines = GuideBuilder.Build(cullWorld, cullWorld.Styles)
                .Count(prim => ReferenceEquals(prim.Pen, cullWorld.Styles.VPLines[0]));
            Check("fan_cull", culledFanLines == 0);

            var horizonWorld = new World();
            horizonWorld.ShowGrid = false;
            horizonWorld.VanishingPoints[0] = new VanishingPoint(new Vec2(10, 10), 4);
            horizonWorld.VanishingPoints[1] = new VanishingPoint(new Vec2(10, 10), 4);
            bool degenerateHorizon = GuideBuilder.Build(horizonWorld, horizonWorld.Styles).Any(prim =>
                ReferenceEquals(prim.Pen, horizonWorld.Styles.HorizonPen) ||
                ReferenceEquals(prim.Pen, horizonWorld.Styles.HorizonVoidPen));
            Check("horizon_degenerate", !degenerateHorizon);

            var copyViewport = new ViewportControl();
            copyViewport.World.ResetDocument(320, 200);
            copyViewport.World.Zoom = 1.7;
            copyViewport.World.Camera = new Vec2(12, 34);
            copyViewport.World.ZoomChanged();
            double copyZoom = copyViewport.World.Zoom;
            Vec2 copyCamera = copyViewport.World.Camera;
            Styles copyStyles = copyViewport.World.Styles;
            using (Bitmap copied = copyViewport.RenderPlane())
            {
                Check("copy_preserves_view",
                    copied.PixelSize == new PixelSize(320, 200) &&
                    copyViewport.World.Zoom == copyZoom &&
                    copyViewport.World.Camera.X == copyCamera.X &&
                    copyViewport.World.Camera.Y == copyCamera.Y &&
                    ReferenceEquals(copyViewport.World.Styles, copyStyles));
            }

            var clampWorld = new World();
            clampWorld.ResetDocument(0, 99999);
            bool docClamped = clampWorld.PicturePlaneWidth == 1 &&
                clampWorld.PicturePlaneHeight == 8192;
            for (int i = 0; i < 30; i++)
            {
                clampWorld.SetGridSize(clampWorld.GridSz * 2);
            }

            bool gridUp = clampWorld.GridSz == 4096;
            for (int i = 0; i < 30; i++)
            {
                clampWorld.SetGridSize(clampWorld.GridSz / 2);
            }

            Check("doc_clamp", docClamped);
            Check("grid_clamp", gridUp && clampWorld.GridSz == 2);

            var imageWorld = new World();
            imageWorld.Reference.Hidden = true;
            imageWorld.SetReferenceImage(null);
            Check("reference_remove", !imageWorld.Reference.Hidden);

            var statusWorld = new World();
            int statusChanges = 0;
            statusWorld.StatusChanged += () => statusChanges++;
            statusWorld.SetStatus("visible");
            Check("status_message", statusWorld.StatusMessage == "visible" && statusChanges == 1);

            // An error stays until the next command; a command does not clear a prompt early.
            var errorViewport = new ViewportControl();
            errorViewport.World.SetError("Save failed: disk full");
            bool errorShown = errorViewport.World.StatusIsError;
            errorViewport.ProcessKey(new KeyEventArgs { Key = Key.G });
            bool errorCleared = errorViewport.World.StatusMessage == null && !errorViewport.World.StatusIsError;
            errorViewport.World.SetStatus("hint");
            errorViewport.ProcessKey(new KeyEventArgs { Key = Key.G });
            Check("error_persists", errorShown && errorCleared && errorViewport.World.StatusMessage == "hint");

            var guardWorld = new World();
            var previous = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul);
            guardWorld.SetReferenceImage(previous);
            var oversized = new WriteableBitmap(new PixelSize(8000, 6000), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul);
            guardWorld.SetReferenceImage(oversized);
            Check("image_size_guard",
                ReferenceEquals(guardWorld.BackgroundImg, previous) &&
                guardWorld.StatusMessage != null);
            guardWorld.SetReferenceImage(null);

            var disposeWorld = new World();
            for (int i = 0; i < 100; i++)
            {
                disposeWorld.SetReferenceImage(new WriteableBitmap(new PixelSize(1, 1),
                    new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul));
            }

            Check("bitmap_dispose", disposeWorld.DisposedImageCount == 99);
            disposeWorld.SetReferenceImage(null);

            long cached = MeasureAlloc(world, viewportWidth, viewportHeight, rebuild: false);
            long rebuilt = MeasureAlloc(world, viewportWidth, viewportHeight, rebuild: true);
            Console.WriteLine($"ALLOC_RENDER {cached} bytes/frame cached, {rebuilt} rebuilt");
            // Relative, so it does not depend on one runtime's numbers: the guide cache saves a third.
            Check("alloc_render", cached * 3 <= rebuilt * 2);

            var keyViewport = new ViewportControl();
            World keyWorld = keyViewport.World;
            keyWorld.ShowGrid = false;
            keyWorld.BumpRevision();
            Prim[] beforeGrid = keyWorld.Prims;
            keyViewport.ProcessKey(new KeyEventArgs { Key = Key.G });
            Prim[] afterGrid = keyWorld.Prims;
            keyViewport.ProcessKey(new KeyEventArgs { Key = Key.D1 });
            Prim[] afterPoint = keyWorld.Prims;
            keyViewport.ProcessKey(new KeyEventArgs { Key = Key.Up });
            Prim[] afterDensity = keyWorld.Prims;
            keyViewport.ProcessKey(new KeyEventArgs { Key = Key.OemPlus });
            Prim[] afterGridSize = keyWorld.Prims;
            keyViewport.ProcessKey(new KeyEventArgs { Key = Key.Left, KeyModifiers = KeyModifiers.Control });
            Prim[] afterMirror = keyWorld.Prims;
            keyViewport.ProcessKey(new KeyEventArgs { Key = Key.Escape });
            Prim[] afterDeselect = keyWorld.Prims;
            keyWorld.TraceLines.Add(new TraceLine(new Vec2(-10, -10), new Vec2(10, 10)));
            keyWorld.BumpRevision();
            Prim[] afterTraceAdd = keyWorld.Prims;
            keyViewport.ProcessKey(new KeyEventArgs { Key = Key.Back });
            Prim[] afterTraceRemove = keyWorld.Prims;
            BoxState keyBox = StackedBox(0);
            keyWorld.AddBox(keyBox);
            Prim[] afterBoxAdd = keyWorld.Prims;
            keyWorld.RemoveLastBox();
            Prim[] afterBoxRemove = keyWorld.Prims;
            keyWorld.AddSubdivision(new SubdivisionState
            {
                Quad = BoxGeometry.Build(keyBox).Faces[2],
                N = 3,
                M = 2,
                Edge = false
            });
            Prim[] afterSubdivisionAdd = keyWorld.Prims;
            keyWorld.RemoveLastSubdivision();
            Prim[] afterSubdivisionRemove = keyWorld.Prims;
            keyWorld.AddEllipse(new EllipseState
            {
                Quad = BoxGeometry.Build(keyBox).Faces[1],
                Cx = 0.5,
                Cy = 0.5,
                R = 0.3
            });
            Prim[] afterEllipseAdd = keyWorld.Prims;
            keyWorld.RemoveLastEllipse();
            Prim[] afterEllipseRemove = keyWorld.Prims;
            keyViewport.ProcessKey(new KeyEventArgs { Key = Key.S });
            Prim[] afterPaletteRotate = keyWorld.Prims;
            keyViewport.ProcessKey(new KeyEventArgs { Key = Key.V });
            Prim[] afterCone = keyWorld.Prims;
            keyViewport.ProcessKey(new KeyEventArgs
            {
                Key = Key.P,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift
            });
            Prim[] afterPaletteCycle = keyWorld.Prims;
            keyViewport.ApplyCameraPreset(2);
            Prim[] afterPreset = keyWorld.Prims;
            keyViewport.ProcessKey(new KeyEventArgs { Key = Key.R, KeyModifiers = KeyModifiers.Control });
            Prim[] afterReset = keyWorld.Prims;
            Check("revision_coverage",
                !ReferenceEquals(beforeGrid, afterGrid) &&
                !ReferenceEquals(afterGrid, afterPoint) &&
                !ReferenceEquals(afterPoint, afterDensity) &&
                !ReferenceEquals(afterDensity, afterGridSize) &&
                !ReferenceEquals(afterGridSize, afterMirror) &&
                !ReferenceEquals(afterMirror, afterDeselect) &&
                !ReferenceEquals(afterDeselect, afterTraceAdd) &&
                !ReferenceEquals(afterTraceAdd, afterTraceRemove) &&
                !ReferenceEquals(afterTraceRemove, afterBoxAdd) &&
                !ReferenceEquals(afterBoxAdd, afterBoxRemove) &&
                !ReferenceEquals(afterBoxRemove, afterSubdivisionAdd) &&
                !ReferenceEquals(afterSubdivisionAdd, afterSubdivisionRemove) &&
                !ReferenceEquals(afterSubdivisionRemove, afterEllipseAdd) &&
                !ReferenceEquals(afterEllipseAdd, afterEllipseRemove) &&
                !ReferenceEquals(afterEllipseRemove, afterPaletteRotate) &&
                !ReferenceEquals(afterPaletteRotate, afterCone) &&
                !ReferenceEquals(afterCone, afterPaletteCycle) &&
                !ReferenceEquals(afterPaletteCycle, afterPreset) &&
                !ReferenceEquals(afterPreset, afterReset));

            var toolViewport = new ViewportControl();
            toolViewport.ProcessKey(new KeyEventArgs { Key = Key.B });
            bool boxTool = toolViewport.World.Overlay is BoxTool;
            toolViewport.ProcessKey(new KeyEventArgs { Key = Key.D });
            bool subdivisionTool = toolViewport.World.Overlay is SubdivisionTool;
            toolViewport.ProcessKey(new KeyEventArgs { Key = Key.E });
            bool ellipseTool = toolViewport.World.Overlay is EllipseTool;
            toolViewport.ProcessKey(new KeyEventArgs { Key = Key.Escape });
            Check("tool_toggle", boxTool && subdivisionTool && ellipseTool &&
                toolViewport.World.Overlay == null);

            Check("key_handled_before_await", KeyHandledBeforeAwait());
            Check("undo_reference_image", UndoKeepsReferenceImage());
            Check("open_clears_history", OpenClearsHistory());
            Check("confirm_discard", ConfirmBeforeDiscard());
            Check("pointer_undo", PointerUndo());
            Check("pointer_navigation", PointerNavigation());
            Check("menu_gestures", MenuGesturesBound());
            Check("window_keys_once", WindowKeysRunOnce());
            Check("menu_state", MenuState());
            Check("commands_unique_gestures", CommandsUniqueGestures());
            Check("help_matches_commands", HelpMatchesCommands());
            List<Command> found = Commands.Find(new ViewportControl().Commands, "grid sty");
            Check("find_matches_words", found.Count == 1 && found[0].Name == "Cycle grid style" &&
                Commands.Find(new ViewportControl().Commands, "GRID view").Count == 4);
            Check("window_title", MainWindow.WindowTitle(null, false) == "Untitled - Chitin" &&
                MainWindow.WindowTitle("room.chitin.json", true) == "room.chitin.json * - Chitin");
            var export = new ExportDialog(640, 480);
            export.FindControl<NumericUpDown>("WidthBox")!.Value = 1280;
            bool heightFollows = export.FindControl<NumericUpDown>("HeightBox")!.Value == 960;
            export.FindControl<NumericUpDown>("HeightBox")!.Value = 120;
            bool widthFollows = export.FindControl<NumericUpDown>("WidthBox")!.Value == 160;
            var tall = new ExportDialog(100, 400);
            Check("export_aspect", heightFollows && widthFollows &&
                tall.FindControl<NumericUpDown>("WidthBox")!.Maximum == 2048 &&
                tall.FindControl<NumericUpDown>("HeightBox")!.Maximum == 8192);
            Check("tool_keys_pass", ToolKeysPass());
            Check("tool_pick_quad", ToolPickQuad());
            Check("density_clamp", DensityClamped());
            Check("image_header_guard", ImageHeaderGuard());
            Check("rig_cancel_silent", RigCancelSilent());
            Check("rig_manage", RigManage());
            Check("reference_cancel_clean", ReferenceCancelClean());
            Check("key_modifiers", KeyModifiersExact());
            bool newerRefused;
            try
            {
                ProjectFile.Read("{\"version\":2}");
                newerRefused = false;
            }
            catch (InvalidDataException exception)
            {
                newerRefused = exception.Message.Contains("version 2");
            }

            Check("project_version", newerRefused &&
                ProjectFile.Write(new World()).Contains($"\"version\": {ProjectFile.Version}"));
            var hiddenViewport = new ViewportControl();
            hiddenViewport.CenterCamera();
            Check("center_camera_empty", hiddenViewport.World.Zoom == 1 &&
                hiddenViewport.World.Camera.X == 0 && hiddenViewport.World.Camera.Y == 0);
            Check("save_atomic",SaveIsAtomic(Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(path))!, "save-atomic.json")));

            Prim[] first = world.Prims;
            Prim[] second = world.Prims;
            world.GridSz += 1;
            world.BumpRevision();
            Prim[] third = world.Prims;
            Check("guide_cache", ReferenceEquals(first, second) && !ReferenceEquals(first, third));
            world.GridSz -= 1;
            world.BumpRevision();

            var undoWorld = new World();
            undoWorld.ShowGrid = false;
            BoxState undoBox = StackedBox(0);
            undoWorld.AddBox(undoBox);
            undoWorld.AddSubdivision(new SubdivisionState
            {
                Quad = BoxGeometry.Build(undoBox).Faces[2],
                N = 2,
                M = 2,
                Edge = false
            });
            undoWorld.AddEllipse(new EllipseState
            {
                Quad = BoxGeometry.Build(undoBox).Faces[1],
                Cx = 0.5,
                Cy = 0.5,
                R = 0.3
            });
            var undoStack = new UndoStack();
            for (int i = 0; i < 3; i++)
            {
                undoStack.Push(undoWorld.Capture(), "test");
                undoWorld.VanishingPoints[i] = new VanishingPoint(new Vec2(10 + i, 20 + i), 0);
                undoWorld.BumpRevision();
            }

            WorldState beforeUndo = undoWorld.Capture();
            for (int i = 0; i < 3; i++)
            {
                WorldState? state = undoStack.Undo(undoWorld.Capture());
                if (state != null)
                {
                    undoWorld.Restore(state);
                }
            }

            bool undone = undoWorld.VanishingPoints.All(point => point == null);
            for (int i = 0; i < 3; i++)
            {
                WorldState? state = undoStack.Redo(undoWorld.Capture());
                if (state != null)
                {
                    undoWorld.Restore(state);
                }
            }

            Check("undo_restores", undone && SameState(beforeUndo, undoWorld.Capture()));

            var roundWorld = new World();
            roundWorld.ResetDocument(320, 200);
            roundWorld.Zoom = 1.5;
            roundWorld.Camera = new Vec2(40, 60);
            roundWorld.ZoomChanged();
            roundWorld.VanishingPoints[0] = new VanishingPoint(new Vec2(-80, 30), 4);
            roundWorld.VanishingPoints[1] = new VanishingPoint(new Vec2(60, 30), 0);
            roundWorld.VanishingPoints[2] = new VanishingPoint(new Vec2(10, -400), 8);
            roundWorld.TraceLines.Add(new TraceLine(new Vec2(-100, 50), new Vec2(90, -40)));
            var roundBox = new BoxState
            {
                P0 = new Vec2(-120, 80),
                P1 = new Vec2(20, 60),
                P2 = new Vec2(-140, -10),
                P3 = new Vec2(0, -30),
                H = new Vec2(0, -40)
            };
            roundWorld.AddBox(roundBox);
            roundWorld.AddSubdivision(new SubdivisionState
            {
                Quad = BoxGeometry.Build(roundBox).Faces[2],
                N = 4,
                M = 3,
                Edge = false
            });
            roundWorld.AddEllipse(new EllipseState
            {
                Quad = BoxGeometry.Build(roundBox).Faces[1],
                Cx = 0.5,
                Cy = 0.5,
                R = 0.3
            });
            roundWorld.ShowGrid = true;
            roundWorld.GridStyle = 2;
            roundWorld.GridSz = 16;
            roundWorld.SetReferenceImage(TestBitmap());
            roundWorld.Reference = new ReferenceSettings
            {
                Opacity = 0.4,
                FlipH = true,
                Scale = 1.3,
                Offset = new Vec2(10, -15)
            };
            roundWorld.SetPalette("charcoal");
            roundWorld.RotatePalette();
            roundWorld.ShowCone = true;
            roundWorld.BumpRevision();

            WorldState original = roundWorld.Capture();
            string projectJson = ProjectFile.Write(roundWorld);
            WorldState loadedProject = ProjectFile.Read(projectJson);
            var restoredWorld = new World();
            restoredWorld.Restore(loadedProject);
            bool stateEqual = SameState(original, restoredWorld.Capture());
            bool renderEqual = RenderHash(roundWorld, 800, 600) ==
                RenderHash(restoredWorld, 800, 600);
            Check("project_roundtrip", stateEqual && renderEqual);

            bool malformedOk;
            try
            {
                WorldState malformed = ProjectFile.Read(
                    "{\"doc\":{\"w\":640,\"h\":480},\"grid\":{\"show\":false,\"style\":0,\"size\":32}," +
                    "\"reference\":{},\"vps\":[null,null,null,null,null,null,null,null,null]," +
                    "\"traceLines\":[],\"boxes\":[null]," +
                    "\"subdivisions\":[{\"quad\":null,\"n\":0,\"m\":0}]," +
                    "\"ellipses\":[{\"quad\":[],\"r\":0}]}");
                var malformedWorld = new World();
                malformedWorld.Restore(malformed);
                malformedOk = malformedWorld.Boxes.Count == 0 &&
                    malformedWorld.Subdivisions.Count == 0 &&
                    malformedWorld.Ellipses.Count == 0;
            }
            catch
            {
                malformedOk = false;
            }

            Check("project_malformed", malformedOk);
            roundWorld.SetReferenceImage(null);
            restoredWorld.SetReferenceImage(null);

            bool svgOk = false;
            try
            {
                string svg = SvgWriter.Write(world, 1920, 1080);
                XDocument document = XDocument.Parse(svg);
                List<XElement> lines = document.Descendants()
                    .Where(element => element.Name.LocalName == "line")
                    .ToList();
                var hex = new Regex("^#[0-9A-Fa-f]{6}$");
                svgOk = lines.Count >= 100 && lines.All(line =>
                    line.Attribute("stroke") is { } stroke && hex.IsMatch(stroke.Value));
            }
            catch
            {
                svgOk = false;
            }

            Check("svg_well_formed", svgOk);

            var pngViewport = new ViewportControl();
            World pngWorld = pngViewport.World;
            pngWorld.ShowGrid = true;
            pngWorld.GridSz = 50;
            using (Bitmap transparentPng = pngViewport.RenderPlane(640, 480, true))
            {
                (byte[] pixels, int stride) = ReadPixels(transparentPng);
                int pngWidth = transparentPng.PixelSize.Width;
                int pngHeight = transparentPng.PixelSize.Height;
                bool corners = PixelAt(pixels, stride, 0, 0).A == 0 &&
                    PixelAt(pixels, stride, pngWidth - 1, 0).A == 0 &&
                    PixelAt(pixels, stride, 0, pngHeight - 1).A == 0 &&
                    PixelAt(pixels, stride, pngWidth - 1, pngHeight - 1).A == 0;
                long opaque = 0;
                for (int y = 0; y < pngHeight; y++)
                {
                    for (int x = 0; x < pngWidth; x++)
                    {
                        if (PixelAt(pixels, stride, x, y).A > 0)
                        {
                            opaque++;
                        }
                    }
                }

                Check("png_transparent",
                    corners && opaque * 100 >= (long)pngWidth * pngHeight);
            }

            var referenceWorld = new World();
            referenceWorld.Zoom = 1;
            referenceWorld.Camera = new Vec2(320, 240);
            referenceWorld.ZoomChanged();
            referenceWorld.SetReferenceImage(TestBitmap());
            referenceWorld.SetReferenceSettings(new ReferenceSettings { Opacity = 0.5, Scale = 1 });
            bool referenceTransform;
            using (Bitmap flat = RenderInteractive(referenceWorld, 640, 480))
            {
                (byte[] pixels, int stride) = ReadPixels(flat);
                (byte tlB, byte tlG, byte tlR, byte tlA) = PixelAt(pixels, stride, 160, 120);
                (byte trB, byte trG, byte trR, byte trA) = PixelAt(pixels, stride, 480, 120);
                bool normal = tlA == 255 && trA == 255 &&
                    tlR > 200 && tlG < 160 && tlB < 160 &&
                    trG > 200 && trR < 160 && trB < 160;
                var paper = referenceWorld.ActivePalette.Paper;
                bool opacity = Math.Abs(tlR - (255 + paper.R) / 2) <= 2 &&
                    Math.Abs(tlG - paper.G / 2) <= 2 &&
                    Math.Abs(tlB - paper.B / 2) <= 2;

                referenceWorld.SetReferenceSettings(new ReferenceSettings
                {
                    Opacity = 0.5,
                    Scale = 1,
                    FlipH = true
                });
                using Bitmap flipped = RenderInteractive(referenceWorld, 640, 480);
                (byte[] flippedPixels, int flippedStride) = ReadPixels(flipped);
                (byte fTlB, byte fTlG, byte fTlR, _) = PixelAt(flippedPixels, flippedStride, 160, 120);
                (byte fTrB, byte fTrG, byte fTrR, _) = PixelAt(flippedPixels, flippedStride, 480, 120);
                bool swap = fTlG > 200 && fTlR < 160 && fTlB < 160 &&
                    fTrR > 200 && fTrG < 160 && fTrB < 160;
                referenceTransform = normal && opacity && swap;
            }

            referenceWorld.SetReferenceImage(null);
            Check("reference_transform", referenceTransform);

            // Chrome never reaches an export: both hashes are the ones from before the sheet frame.
            Check("export_unchanged",
                FileHash(exportPath) == "0120d0b8255f486129c0ca37d8e34e8ceafb4f97c45177f81a2998bce2e5defa" &&
                FileHash(shapesPath) == "af9cc42976507afa331fbf78a34b6c404d595bca6c0b052cb6e51b3e4aaccb69");

            List<RulerTick> atOne = Sheet.Ticks(1, 0, 400);
            List<RulerTick> atTwo = Sheet.Ticks(2, 0, 400);
            List<RulerTick> panned = Sheet.Ticks(2, 3, 400);
            Check("ruler_ticks_follow_zoom",
                atOne.Count == 41 && atOne[5] == new RulerTick(50, 2, 50) && atOne[1].Level == 0 &&
                atTwo[1] == new RulerTick(10, 0, 5) && atTwo[5] == new RulerTick(50, 2, 25) &&
                panned.First(tick => tick.Value == 0).Pos == 6);

            Check("ctrl_k_toggles_points", CtrlKTogglesPoints());
            Check("resize_keeps_centre", ResizeKeepsCentre());
            Check("text_field_keys", TextFieldKeys());
            Check("find_closes", FindCloses());
            Check("points_panel_edits", PointsPanelEdits());

            WriteScreens(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "screens"));

            if (!FileOk(path) || !FileOk(exportPath) || !FileOk(shapesPath))
            {
                Console.Error.WriteLine("SELFTEST_FAIL missing or short output file");
                return 1;
            }

            if (_failedChecks > 0)
            {
                Console.WriteLine("SELFTEST_FAIL");
                return 1;
            }

            Console.WriteLine($"SELFTEST_OK {path} {exportPath} {shapesPath}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    // The same box below and above the horizon: below shows its top, above its underside.
    private static bool BoxVisibility()
    {
        var left = new Vec2(-300, 0);
        var right = new Vec2(300, 0);
        BuiltBox Box(double y)
        {
            var p0 = new Vec2(0, y);
            Vec2 p1 = p0 + (left - p0) * 0.2;
            Vec2 p2 = p0 + (right - p0) * 0.2;
            return BoxGeometry.Build(p0, p1, p2, new Vec2(0, -40),
                (right - p1).Normalized(), (left - p2).Normalized());
        }

        bool Hidden(BuiltBox box, int a, int b) => box.HiddenEdges.Contains((box.Vertices[a], box.Vertices[b]));
        BuiltBox below = Box(100);
        BuiltBox above = Box(-140);
        // Vertices: p0 p1 p3 p2 q0 q1 q3 q2.
        return below.VisibleFaces.SequenceEqual(new[] { 1, 2, 3 }) && below.HiddenEdges.Length == 3 &&
            Hidden(below, 1, 2) && Hidden(below, 3, 2) && Hidden(below, 2, 6) &&
            above.VisibleFaces.SequenceEqual(new[] { 0, 2, 3 }) && above.HiddenEdges.Length == 3 &&
            Hidden(above, 5, 6) && Hidden(above, 6, 7) && Hidden(above, 2, 6);
    }

    // Every shortcut the menu shows must exist in the key table, or the menu item does nothing.
    // Real key presses on the main window: each shortcut runs once, though the menu shows it too.
    private static bool WindowKeysRunOnce()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var viewport = window.FindControl<ViewportControl>("Viewport")!;
            World world = viewport.World;
            viewport.Focus();
            window.KeyPress(Key.G, RawInputModifiers.None, PhysicalKey.G, "g");
            window.KeyRelease(Key.G, RawInputModifiers.None, PhysicalKey.G, "g");
            bool gridOnce = world.ShowGrid && viewport.History.CanUndo;

            window.KeyPress(Key.G, RawInputModifiers.Control, PhysicalKey.G, "g");
            window.KeyRelease(Key.G, RawInputModifiers.Control, PhysicalKey.G, "g");
            bool styleOnce = world.GridStyle == 1;

            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
            window.KeyRelease(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
            bool undoOnce = world.GridStyle == 0 && world.ShowGrid;

            // A menu click runs the key's entry; a shortcut the viewport did not receive is forwarded.
            MenuItem gridItem = window.FindControl<Menu>("MainMenu")!.GetLogicalDescendants()
                .OfType<MenuItem>().Single(item => (string?)item.Header == "Toggle grid");
            gridItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            bool menuClick = !world.ShowGrid;
            // Raised on the window itself, as when focus is outside the viewport.
            window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.G,
                KeyModifiers = KeyModifiers.Control
            });
            bool forwarded = world.GridStyle == 1;
            return gridOnce && styleOnce && undoOnce && menuClick && forwarded;
        }
        finally
        {
            window.Close();
        }
    }

    // Edit menu enables Undo/Redo from the history; the Camera menu lists rigs with a working submenu.
    private static bool MenuState()
    {
        var window = new MainWindow();
        var viewport = window.FindControl<ViewportControl>("Viewport")!;
        List<MenuItem> top = window.FindControl<Menu>("MainMenu")!.Items.OfType<MenuItem>().ToList();
        MenuItem edit = top.Single(item => (string?)item.Header == "Edit");
        MenuItem camera = top.Single(item => (string?)item.Header == "Camera");
        MenuItem undo = window.MenuItemFor("Undo");
        MenuItem redo = window.MenuItemFor("Redo");

        edit.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
        bool emptyDisabled = !undo.IsEnabled && !redo.IsEnabled;
        viewport.ToggleGrid();
        edit.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
        bool undoEnabled = undo.IsEnabled && !redo.IsEnabled && (string?)undo.Header == "Undo Toggle grid";
        viewport.Undo();
        edit.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
        bool redoNamed = (string?)redo.Header == "Redo Toggle grid" && (string?)undo.Header == "Undo";

        viewport.World.SaveRig("left_wall");
        camera.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
        camera.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
        MenuItem rig = camera.Items.OfType<MenuItem>().Last();
        bool listedOnce = camera.Items.OfType<MenuItem>().Count(item => item.Header is TextBlock) == 1 &&
            rig.Header is TextBlock { Text: "left_wall" };
        rig.Items.OfType<MenuItem>().Single(item => (string?)item.Header == "Delete")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        bool deleted = viewport.World.Rigs.Count == 0;
        return emptyDisabled && undoEnabled && redoNamed && listedOnce && deleted;
    }

    // No key runs two commands; the window's own commands are in the same table.
    private static bool CommandsUniqueGestures()
    {
        var window = new MainWindow();
        var viewport = window.FindControl<ViewportControl>("Viewport")!;
        List<(Key, KeyModifiers)> keys = viewport.Commands
            .SelectMany(command => command.Keys)
            .Select(gesture => (gesture.Key, gesture.KeyModifiers)).ToList();
        bool namesUnique = viewport.Commands.Select(command => command.Name).Distinct().Count() ==
            viewport.Commands.Count;
        // A command without a key must be reachable from the menu.
        bool reachable = viewport.Commands.All(command => command.Keys.Length > 0 || command.InMenu);
        return keys.Count > 60 && keys.Distinct().Count() == keys.Count && namesUnique && reachable;
    }

    // The F1 list shows every command with a key exactly once, with that command's gesture.
    private static bool HelpMatchesCommands()
    {
        var viewport = new ViewportControl();
        List<HelpRow> rows = Commands.HelpRows(viewport.Commands);
        return viewport.Commands.Where(command => command.Keys.Length > 0).All(command =>
            rows.Count(row => row.Text == command.Name && row.Keys == command.Gesture) == 1) &&
            !rows.Any(row => viewport.Commands.Any(command => command.Keys.Length == 0 && command.Name == row.Text));
    }

    private static bool MenuGesturesBound()
    {
        var window = new MainWindow();
        var viewport = window.FindControl<ViewportControl>("Viewport")!;
        List<KeyGesture> gestures = window.FindControl<Menu>("MainMenu")!.GetLogicalDescendants()
            .OfType<MenuItem>().Select(item => item.InputGesture).OfType<KeyGesture>().ToList();
        return gestures.Count >= 30 &&
            gestures.All(gesture => viewport.HasKey(gesture.Key, gesture.KeyModifiers));
    }

    private static bool SameState(WorldState left, WorldState right)
    {
        return ProjectFile.ToJson(left) == ProjectFile.ToJson(right);
    }

    private static bool KeyHandledBeforeAwait()
    {
        var viewport = new ViewportControl();
        var window = new Window { Content = viewport };
        window.Show();
        try
        {
            var args = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.C,
                KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt
            };
            viewport.ProcessKey(args);
            return args.Handled && window.OwnedWindows.Count == 1;
        }
        finally
        {
            CloseDialogs(window);
            window.Close();
        }
    }

    private static bool UndoKeepsReferenceImage()
    {
        var viewport = new ViewportControl();
        World world = viewport.World;
        Bitmap image = TestBitmap();
        world.SetReferenceImage(image);

        viewport.RemoveReference();
        bool removed = world.BackgroundImg == null;
        viewport.Undo();
        bool restored = ReferenceEquals(world.BackgroundImg, image);
        viewport.ClearDocument();
        viewport.Undo();
        bool clearUndone = ReferenceEquals(world.BackgroundImg, image) && world.DisposedImageCount == 0;

        // Push the snapshot that holds the image off the 50-step stack: then it is disposed once.
        viewport.RemoveReference();
        for (int i = 0; i < UndoStack.MaxDepth; i++)
        {
            viewport.ToggleGrid();
        }

        return removed && restored && clearUndone && world.DisposedImageCount == 1;
    }

    private static bool OpenClearsHistory()
    {
        var viewport = new ViewportControl();
        viewport.ToggleGrid();
        var opened = new WorldState();
        opened.VanishingPoints[0] = new VanishingPoint(new Vec2(5, 6), 0);
        viewport.LoadProject(opened, null);
        viewport.Undo();
        return !viewport.History.CanUndo && !viewport.World.IsDirty &&
            VpAt(viewport.World, 0, 5, 6);
    }

    private static bool ConfirmBeforeDiscard()
    {
        var viewport = new ViewportControl();
        var window = new Window { Content = viewport };
        window.Show();
        try
        {
            viewport.World.VanishingPoints[0] = new VanishingPoint(new Vec2(1, 2), 0);
            viewport.World.BumpRevision();

            Task cancelled = viewport.NewDocumentAsync();
            bool asked = window.OwnedWindows.Count == 1;
            CloseDialogs(window);
            bool kept = cancelled.IsCompleted && viewport.World.VanishingPoints[0] != null;

            Button Choice(string name) => window.OwnedWindows.Single().GetLogicalDescendants().OfType<Button>()
                .Single(button => (string?)button.Content == name);

            // Save first: the save picker gives no file here, so the document stays.
            Task saveFirst = viewport.NewDocumentAsync();
            bool saveDefault = Choice("Save").IsDefault && Choice("Cancel").IsCancel;
            Choice("Save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            CloseDialogs(window);
            bool keptUnsaved = saveFirst.IsCompleted && viewport.World.VanishingPoints[0] != null;

            Task discarded = viewport.NewDocumentAsync();
            Choice("Don't save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            bool cleared = discarded.IsCompleted && viewport.World.VanishingPoints[0] == null;

            viewport.World.MarkSaved();
            bool cleanSkipsDialog = viewport.NewDocumentAsync().IsCompleted &&
                window.OwnedWindows.Count == 0;
            return asked && kept && saveDefault && keptUnsaved && cleared && cleanSkipsDialog;
        }
        finally
        {
            CloseDialogs(window);
            window.Close();
        }
    }

    private static bool PointerUndo()
    {
        var viewport = new ViewportControl();
        var window = new Window { Width = 800, Height = 600, Content = viewport };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            World world = viewport.World;
            world.VanishingPoints[0] = new VanishingPoint(new Vec2(100, 100), 0);
            Point Screen(double x, double y) =>
                new((x + world.Camera.X) * world.Zoom, (y + world.Camera.Y) * world.Zoom);

            Point point = Screen(100, 100);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            window.MouseDown(Screen(-50, -50), MouseButton.Right);
            window.MouseUp(Screen(-50, -50), MouseButton.Right);
            bool clicksSilent = !viewport.History.CanUndo && world.SelectionIdx == 0;

            window.MouseDown(Screen(0, 0), MouseButton.Middle);
            window.MouseMove(new Point(Screen(0, 0).X + 40, Screen(0, 0).Y + 30));
            window.MouseUp(Screen(0, 0), MouseButton.Middle);
            bool panSilent = !viewport.History.CanUndo;

            // A sideways swipe pans, it does not zoom.
            double zoom = world.Zoom;
            window.MouseWheel(Screen(0, 0), new Vector(1, 0));
            bool sidewaysSilent = world.Zoom == zoom && !viewport.History.CanUndo;
            Vec2 pannedCamera = world.Camera;

            point = Screen(100, 100);
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(new Point(point.X + 20, point.Y));
            window.MouseUp(new Point(point.X + 20, point.Y), MouseButton.Left);
            bool dragged = viewport.History.CanUndo && !VpAt(world, 0, 100, 100);
            viewport.Undo();
            bool undone = VpAt(world, 0, 100, 100) &&
                world.Camera.X == pannedCamera.X && world.Camera.Y == pannedCamera.Y;
            return clicksSilent && panSilent && sidewaysSilent && dragged && undone;
        }
        finally
        {
            window.Close();
        }
    }

    // Wheel pans, Ctrl + wheel zooms, Space + drag pans; the nearest of two overlapping points wins.
    private static bool PointerNavigation()
    {
        var viewport = new ViewportControl();
        var window = new Window { Width = 800, Height = 600, Content = viewport };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            World world = viewport.World;
            world.VanishingPoints[0] = new VanishingPoint(new Vec2(0, 0), 0);
            world.VanishingPoints[1] = new VanishingPoint(new Vec2(6, 0), 0);
            Point Screen(double x, double y) =>
                new((x + world.Camera.X) * world.Zoom, (y + world.Camera.Y) * world.Zoom);

            window.MouseMove(Screen(6, 0));
            bool hovered = world.HoverIdx == 1;
            window.MouseDown(Screen(6, 0), MouseButton.Left);
            window.MouseUp(Screen(6, 0), MouseButton.Left);
            bool nearest = world.SelectionIdx == 1;

            double zoom = world.Zoom;
            Vec2 camera = world.Camera;
            window.MouseWheel(Screen(0, 0), new Vector(0, -1));
            bool wheelPans = world.Zoom == zoom && world.Camera.X == camera.X && world.Camera.Y < camera.Y;
            window.MouseWheel(Screen(0, 0), new Vector(0, 1), RawInputModifiers.Control);
            bool ctrlZooms = Math.Abs(world.Zoom - zoom * 1.1) < 1e-9;

            camera = world.Camera;
            Point start = Screen(6, 0);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(new Point(start.X + 30, start.Y + 10));
            window.MouseUp(new Point(start.X + 30, start.Y + 10), MouseButton.Left);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            bool spacePans = world.Camera.X > camera.X && VpAt(world, 1, 6, 0) && !viewport.History.CanUndo;
            return hovered && nearest && wheelPans && ctrlZooms && spacePans;
        }
        finally
        {
            window.Close();
        }
    }

    private static bool ToolPickQuad()
    {
        var viewport = new ViewportControl();
        var window = new Window { Width = 800, Height = 600, Content = viewport };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            World world = viewport.World;
            Point Screen(double x, double y) =>
                new((x + world.Camera.X) * world.Zoom, (y + world.Camera.Y) * world.Zoom);
            void Click(double x, double y)
            {
                window.MouseDown(Screen(x, y), MouseButton.Left);
                window.MouseUp(Screen(x, y), MouseButton.Left);
            }

            // Four corners, then Enter: one subdivision with those corners.
            viewport.ProcessKey(new KeyEventArgs { Key = Key.D });
            Click(-100, -100);
            Click(100, -100);
            Click(100, 100);
            Click(-100, 100);
            viewport.ProcessKey(new KeyEventArgs { Key = Key.Enter });
            bool corners = world.Subdivisions.Count == 1 &&
                world.Subdivisions[0].Quad[2].X == 100 && world.Subdivisions[0].Quad[3].Y == 100;

            // The same corners in Z order cross each other: refused, nothing to commit.
            Click(-100, -100);
            Click(100, -100);
            Click(-100, 100);
            Click(100, 100);
            viewport.ProcessKey(new KeyEventArgs { Key = Key.Enter });
            bool crossingRefused = world.Subdivisions.Count == 1 &&
                world.StatusMessage?.StartsWith("Corners refused") == true;

            // One click on a box face picks a face of that box; a drag adds the ellipse on it.
            world.AddBox(StackedBox(0));
            Vec2[] face = BoxGeometry.Build(world.Boxes[0]).Faces[1];
            Vec2 centre = Average(face);
            viewport.ProcessKey(new KeyEventArgs { Key = Key.E });
            Click(centre.X, centre.Y);
            window.MouseDown(Screen(centre.X, centre.Y), MouseButton.Left);
            window.MouseMove(Screen(centre.X + 10, centre.Y));
            window.MouseUp(Screen(centre.X + 10, centre.Y), MouseButton.Left);
            bool facePicked = world.Ellipses.Count == 1 &&
                BoxGeometry.Build(world.Boxes[0]).Faces.Any(boxFace =>
                    boxFace.SequenceEqual(world.Ellipses[0].Quad));
            return corners && crossingRefused && facePicked;
        }
        finally
        {
            window.Close();
        }
    }

    private static bool ToolKeysPass()
    {
        var viewport = new ViewportControl();
        World world = viewport.World;
        world.VanishingPoints[0] = new VanishingPoint(new Vec2(10, 10), 0);
        world.VanishingPoints[1] = new VanishingPoint(new Vec2(20, 20), 0);
        world.TraceLines.Add(new TraceLine(new Vec2(0, 0), new Vec2(5, 5)));
        void Key(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
            viewport.ProcessKey(new KeyEventArgs { Key = key, KeyModifiers = modifiers });

        Key(Avalonia.Input.Key.B);
        Key(Avalonia.Input.Key.D1, KeyModifiers.Control);
        bool selected = world.SelectionIdx == 0;
        Key(Avalonia.Input.Key.Back, KeyModifiers.Alt);
        bool tracesCleared = world.TraceLines.Count == 0;

        Key(Avalonia.Input.Key.D);
        Key(Avalonia.Input.Key.D2, KeyModifiers.Alt);
        bool deleted = world.VanishingPoints[1] == null;
        Key(Avalonia.Input.Key.D2);
        bool cellsKeptByTool = world.VanishingPoints[1] == null;

        Key(Avalonia.Input.Key.B);
        Key(Avalonia.Input.Key.D2);
        bool droppedInBoxTool = world.VanishingPoints[1] != null;
        return selected && tracesCleared && deleted && cellsKeptByTool && droppedInBoxTool &&
            world.Overlay is BoxTool;
    }

    private static bool DensityClamped()
    {
        WorldState loaded = ProjectFile.Read(
            "{\"doc\":{\"w\":640,\"h\":480},\"grid\":{},\"reference\":{},\"traceLines\":[]," +
            "\"boxes\":[],\"subdivisions\":[],\"ellipses\":[],\"rigs\":[]," +
            "\"vps\":[{\"x\":0,\"y\":0,\"density\":100000000},{\"x\":9,\"y\":9,\"density\":-5}," +
            "null,null,null,null,null,null,null]}");
        var viewport = new ViewportControl();
        World world = viewport.World;
        world.Restore(loaded);
        bool fileClamped = world.VanishingPoints[0]!.Density == VanishingPoint.MaxDensity &&
            world.VanishingPoints[1]!.Density == 0;

        world.SelectionIdx = 0;
        viewport.ProcessKey(new KeyEventArgs { Key = Key.Up });
        bool keyClamped = world.VanishingPoints[0]!.Density == VanishingPoint.MaxDensity;

        world.VanishingPoints[0]!.Density = int.MaxValue;
        bool fanClamped = GuideBuilder.FanDensity(world.VanishingPoints[0]!) == VanishingPoint.MaxDensity;
        return fileClamped && keyClamped && fanClamped;
    }

    private static bool RigManage()
    {
        var viewport = new ViewportControl();
        var window = new Window { Content = viewport };
        window.Show();
        World world = viewport.World;
        void Rename(string name, string newName)
        {
            Task rename = viewport.RenameRigAsync(name);
            Window dialog = window.OwnedWindows.Single();
            dialog.GetLogicalDescendants().OfType<TextBox>().Single().Text = newName;
            dialog.GetLogicalDescendants().OfType<Button>().Single(button => (string?)button.Content == "Save")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        try
        {
            world.SaveRig("Front");
            world.SaveRig("Side");
            viewport.DeleteRig("side");
            bool deleted = world.Rigs.Count == 1 && viewport.History.CanUndo;
            viewport.Undo();
            bool restored = world.Rigs.Count == 2 && world.FindRig("Side") == 1;

            Rename("Front", "Top_down");
            bool renamed = world.Rigs[0].Name == "Top_down";
            Rename("Top_down", "side");
            bool clashRefused = world.Rigs[0].Name == "Top_down" &&
                world.StatusMessage == "A rig named side already exists.";
            return deleted && restored && renamed && clashRefused;
        }
        finally
        {
            CloseDialogs(window);
            window.Close();
        }
    }

    private static bool RigCancelSilent()
    {
        var viewport = new ViewportControl();
        var window = new Window { Content = viewport };
        window.Show();
        try
        {
            Task save = viewport.SaveRigAsync();
            bool asked = window.OwnedWindows.Count == 1;
            CloseDialogs(window);
            return asked && save.IsCompleted && viewport.World.StatusMessage == null &&
                viewport.World.Rigs.Count == 0;
        }
        finally
        {
            CloseDialogs(window);
            window.Close();
        }
    }

    private static bool KeyModifiersExact()
    {
        var viewport = new ViewportControl();
        World world = viewport.World;
        world.VanishingPoints[0] = new VanishingPoint(new Vec2(5, 5), 8);
        world.SelectionIdx = 0;
        viewport.ProcessKey(new KeyEventArgs { Key = Key.Up, KeyModifiers = KeyModifiers.Shift });
        viewport.ProcessKey(new KeyEventArgs { Key = Key.H, KeyModifiers = KeyModifiers.Alt });
        viewport.ProcessKey(new KeyEventArgs
        {
            Key = Key.Left,
            KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift
        });
        bool unchanged = world.VanishingPoints[0]!.Density == 8 && VpAt(world, 0, 5, 5) &&
            !viewport.History.CanUndo;

        viewport.ProcessKey(new KeyEventArgs
        {
            Key = Key.D3,
            KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift
        });
        return unchanged && world.VanishingPoints[2] == null &&
            world.StatusMessage?.StartsWith("Trace lines do not cross") == true;
    }

    private static bool ReferenceCancelClean()
    {
        var viewport = new ViewportControl();
        var window = new Window { Content = viewport };
        window.Show();
        try
        {
            viewport.World.MarkSaved();
            Task dialog = viewport.ShowReferenceDialogAsync();
            Slider opacity = window.OwnedWindows.Single().GetLogicalDescendants().OfType<Slider>()
                .Single(slider => slider.Name == "OpacitySlider");
            opacity.Value = 40;
            bool live = viewport.World.IsDirty && Math.Abs(viewport.World.Reference.Opacity - 0.4) < 1e-9;
            CloseDialogs(window);
            return live && dialog.IsCompleted && !viewport.World.IsDirty &&
                viewport.World.Reference.Opacity == 1 && !viewport.History.CanUndo;
        }
        finally
        {
            CloseDialogs(window);
            window.Close();
        }
    }

    private static bool ImageHeaderGuard()
    {
        byte[] png;
        using (var stream = new MemoryStream())
        {
            TestBitmap().Save(stream);
            png = stream.ToArray();
        }

        bool small;
        using (Bitmap decoded = World.DecodeImage(png))
        {
            small = decoded.PixelSize == new PixelSize(2, 2);
        }

        // Patch IHDR to 60000 x 60000 and fix its CRC: a valid header for a 3.6-gigapixel image.
        byte[] bomb = (byte[])png.Clone();
        byte[] side = { 0, 0, 0xEA, 0x60 };
        side.CopyTo(bomb, 16);
        side.CopyTo(bomb, 20);
        uint crc = Crc32(bomb.AsSpan(12, 17));
        bomb[29] = (byte)(crc >> 24);
        bomb[30] = (byte)(crc >> 16);
        bomb[31] = (byte)(crc >> 8);
        bomb[32] = (byte)crc;
        try
        {
            World.DecodeImage(bomb).Dispose();
            return false;
        }
        catch (InvalidDataException exception)
        {
            return small && exception.Message.Contains("40 megapixels");
        }
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }

    private static bool SaveIsAtomic(string target)
    {
        string temp = target + ".tmp";
        try
        {
            Task.Run(() => ViewportControl.WriteFileAtomicAsync(target, "old"u8.ToArray())).GetAwaiter().GetResult();
            Task.Run(() => ViewportControl.WriteFileAtomicAsync(target, "new"u8.ToArray())).GetAwaiter().GetResult();
            bool replaced = File.ReadAllText(target) == "new" && !File.Exists(temp);

            // A directory in the temp file's place makes the write fail before the rename.
            Directory.CreateDirectory(temp);
            bool failed = false;
            try
            {
                Task.Run(() => ViewportControl.WriteFileAtomicAsync(target, "lost"u8.ToArray())).GetAwaiter().GetResult();
            }
            catch (IOException)
            {
                failed = true;
            }
            catch (UnauthorizedAccessException)
            {
                failed = true;
            }

            return replaced && failed && File.ReadAllText(target) == "new";
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp);
            }

            File.Delete(target);
        }
    }

    private static void CloseDialogs(Window window)
    {
        foreach (Window dialog in window.OwnedWindows.ToList())
        {
            dialog.Close();
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static long MeasureAlloc(World world, int width, int height, bool rebuild)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        for (int i = 0; i < 5; i++)
        {
            using (var context = bitmap.CreateDrawingContext())
            {
                Renderer.Draw(context, world, new Size(width, height), RenderMode.Interactive);
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        const int frames = 20;
        for (int i = 0; i < frames; i++)
        {
            if (rebuild)
            {
                world.BumpRevision(false);
            }

            using (var context = bitmap.CreateDrawingContext())
            {
                Renderer.Draw(context, world, new Size(width, height), RenderMode.Interactive);
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        return (after - before) / frames;
    }

    // Ctrl+K on the window hides and shows the points panel; it is view state, not history.
    private static bool CtrlKTogglesPoints()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var viewport = window.FindControl<ViewportControl>("Viewport")!;
            var panel = window.FindControl<Border>("PointsPanel")!;
            bool shown = panel.IsVisible;
            viewport.Focus();
            window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
            window.KeyRelease(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
            bool hidden = !panel.IsVisible;
            window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
            window.KeyRelease(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
            return shown && hidden && panel.IsVisible && !viewport.History.CanUndo && !viewport.World.IsDirty;
        }
        finally
        {
            window.Close();
        }
    }

    // Ctrl+F opens Find; Enter runs the highlighted command and Escape cancels; either way it closes.
    private static bool FindCloses()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var viewport = window.FindControl<ViewportControl>("Viewport")!;
            var find = window.FindControl<TextBox>("FindBox")!;
            var overlay = window.FindControl<Border>("FindOverlay")!;
            void OpenFind()
            {
                viewport.Focus();
                window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
                window.KeyRelease(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
                Dispatcher.UIThread.RunJobs();
            }

            OpenFind();
            bool opened = overlay.IsVisible && find.IsFocused;
            find.Text = "toggle grid";
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            bool ran = viewport.World.ShowGrid && !overlay.IsVisible && find.Text == "";
            OpenFind();
            find.Text = "grid";
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            return opened && ran && !overlay.IsVisible && find.Text == "" && viewport.IsFocused;
        }
        finally
        {
            window.Close();
        }
    }

    // Typing in Find: Ctrl+Z stays with the field, Ctrl+K still hides the points panel.
    private static bool TextFieldKeys()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var viewport = window.FindControl<ViewportControl>("Viewport")!;
            viewport.ApplyCameraPreset(2);
            viewport.Focus();
            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
            Dispatcher.UIThread.RunJobs();
            bool focused = window.FindControl<TextBox>("FindBox")!.IsFocused;
            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
            window.KeyRelease(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
            bool kept = viewport.World.VanishingPoints[1] != null && viewport.History.CanUndo;
            window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
            window.KeyRelease(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
            return focused && kept && !window.FindControl<Border>("PointsPanel")!.IsVisible;
        }
        finally
        {
            window.Close();
        }
    }

    // The world point at the viewport centre stays within half a pixel across a 300 px resize.
    private static bool ResizeKeepsCentre()
    {
        Vec2 camera = Sheet.KeepCentre(new Vec2(10, 20), 2, 800, 600, 1100, 600);
        bool pure = camera == new Vec2(10 + 300 / 2.0 / 2, 20);

        var window = new MainWindow { Width = 1000, Height = 700 };
        window.Show();
        try
        {
            var viewport = window.FindControl<ViewportControl>("Viewport")!;
            World world = viewport.World;
            window.CaptureRenderedFrame()?.Dispose();
            Vec2 Centre() => new(viewport.Bounds.Width / 2 / world.Zoom - world.Camera.X,
                viewport.Bounds.Height / 2 / world.Zoom - world.Camera.Y);
            double width = viewport.Bounds.Width;
            Vec2 before = Centre();
            window.Width = 1300;
            window.CaptureRenderedFrame()?.Dispose();
            Vec2 after = Centre();
            return pure && Math.Abs(viewport.Bounds.Width - width - 300) < 1 &&
                before.DistFrom(after) * world.Zoom < 0.5;
        }
        finally
        {
            window.Close();
        }
    }

    // A position typed in the points panel moves the point: one named undo step. Escape drops an edit.
    private static bool PointsPanelEdits()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var viewport = window.FindControl<ViewportControl>("Viewport")!;
            World world = viewport.World;
            world.VanishingPoints[0] = new VanishingPoint(new Vec2(0, 0), 0);
            world.BumpRevision();
            window.UpdateChrome();
            TextBox X() => window.FindControl<StackPanel>("PointsList")!.GetLogicalDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetName(box) == "Point 1 X");
            bool shown = X().Text == "320" && X().IsEffectivelyVisible;

            X().Focus();
            X().Text = "400";
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            bool moved = VpAt(world, 0, 80, 0) && viewport.History.UndoName == "Move vanishing point" &&
                viewport.IsFocused;

            X().Focus();
            X().Text = "999";
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            window.UpdateChrome();
            bool dropped = VpAt(world, 0, 80, 0) && X().Text == "400";

            viewport.Undo();
            window.UpdateChrome();
            return shown && moved && dropped && VpAt(world, 0, 0, 0) && X().Text == "320" &&
                !viewport.History.CanUndo;
        }
        finally
        {
            window.Close();
        }
    }

    private static string FileHash(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    }

    // The real window, headless, for review: a 2-point scene with both points off the viewport.
    private static void WriteScreens(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (string old in Directory.GetFiles(directory, "window-*.png"))
        {
            File.Delete(old);
        }

        var window = new MainWindow { Width = 1280, Height = 800 };
        window.Show();
        try
        {
            var viewport = window.FindControl<ViewportControl>("Viewport")!;
            World world = viewport.World;
            world.ApplyPreset(2);
            world.VanishingPoints[0]!.Pos = new Vec2(-1100, -60);
            world.VanishingPoints[1]!.Pos = new Vec2(1250, -60);
            world.TraceLines.Add(new TraceLine(new Vec2(-200, 150), new Vec2(150, -120)));
            world.TraceLines.Add(new TraceLine(new Vec2(-180, -140), new Vec2(160, 130)));
            world.ShowGrid = true;
            world.SelectionIdx = 0;
            world.BumpRevision();
            foreach (Palette palette in Palette.BuiltIns)
            {
                world.SetPalette(palette.Name);
                Capture(window, Path.Combine(directory, $"window-{palette.Name}.png"));
            }

            world.SetPalette("ink");
            viewport.RunCommandAsync("Find a command").Wait();
            Dispatcher.UIThread.RunJobs();
            window.FindControl<TextBox>("FindBox")!.Text = "grid";
            Capture(window, Path.Combine(directory, "window-find.png"));
        }
        finally
        {
            window.Close();
        }
    }

    private static void Capture(MainWindow window, string path)
    {
        window.UpdateChrome();
        window.FindControl<ViewportControl>("Viewport")!.InvalidateVisual();
        // Let the 100 ms panel transition and the restyle finish.
        for (int i = 0; i < 12; i++)
        {
            Thread.Sleep(15);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        window.UpdateChrome();
        Dispatcher.UIThread.RunJobs();
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        frame?.Save(path);
    }

    private static bool FileOk(string path)
    {
        var file = new FileInfo(path);
        return file.Exists && file.Length > 1024;
    }

    private static string RenderHash(World world, int width, int height)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            Renderer.Draw(context, world, new Size(width, height), RenderMode.Interactive);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static Bitmap RenderInteractive(World world, int width, int height)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            Renderer.Draw(context, world, new Size(width, height), RenderMode.Interactive);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }

    private static (byte[] Pixels, int Stride) ReadPixels(Bitmap bitmap)
    {
        using var buffer = new WriteableBitmap(bitmap.PixelSize, new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (ILockedFramebuffer framebuffer = buffer.Lock())
        {
            bitmap.CopyPixels(framebuffer, AlphaFormat.Unpremul);
            int length = framebuffer.RowBytes * framebuffer.Size.Height;
            var pixels = new byte[length];
            Marshal.Copy(framebuffer.Address, pixels, 0, length);
            return (pixels, framebuffer.RowBytes);
        }
    }

    private static (byte B, byte G, byte R, byte A) PixelAt(byte[] pixels, int stride, int x, int y)
    {
        int offset = y * stride + x * 4;
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
    }

    private static World BuildShapesWorld()
    {
        var world = new World();
        world.SetPalette("atelier");
        world.ShowGrid = false;
        world.Zoom = 1;
        world.Camera = new Vec2(320, 240);
        world.ZoomChanged();

        BoxState top = StackedBox(0);
        for (int i = 0; i < 3; i++)
        {
            top = StackedBox(i);
            world.AddBox(top);
        }

        BuiltBox built = BoxGeometry.Build(top);
        world.AddSubdivision(new SubdivisionState
        {
            Quad = built.Faces[2],
            N = 4,
            M = 3,
            Edge = false
        });
        world.AddEllipse(new EllipseState
        {
            Quad = built.Faces[1],
            Cx = 0.5,
            Cy = 0.5,
            R = 0.35
        });
        return world;
    }

    private static bool ShapesSvgOk(World world)
    {
        try
        {
            string svg = SvgWriter.Write(world, 640, 480);
            XDocument document = XDocument.Parse(svg);
            return document.Descendants().Any(element => element.Name.LocalName == "polygon") &&
                   document.Descendants().Any(element => element.Name.LocalName == "line");
        }
        catch
        {
            return false;
        }
    }

    private static Vec2[] ProjectedQuad()
    {
        Vec2 vp0 = new Vec2(-400, -100);
        Vec2 vp1 = new Vec2(400, -100);
        Vec2 a = new Vec2(0, 200);
        Vec2 b = a + (vp0 - a) * 0.35;
        Vec2 d = a + (vp1 - a) * 0.35;
        Geom.IntersectLines(b, vp1 - b,
            d, vp0 - d, out Vec2 c);
        return new[] { a, b, c, d };
    }

    private static double Distance(Vec2 left, Vec2 right)
    {
        return left.DistFrom(right);
    }

    private static BoxState StackedBox(int index)
    {
        Vec2 p0 = new Vec2(-100, 100 - index * 70);
        Vec2 p1 = new Vec2(100, 70 - index * 70);
        Vec2 p2 = new Vec2(-130, -20 - index * 70);
        return new BoxState
        {
            P0 = p0,
            P1 = p1,
            P2 = p2,
            P3 = p1 + p2 - p0,
            H = new Vec2(0, -70)
        };
    }

    private static Vec2 Average(Vec2[] points)
    {
        double x = 0;
        double y = 0;
        foreach (Vec2 point in points)
        {
            x += point.X;
            y += point.Y;
        }

        return new Vec2(x / points.Length, y / points.Length);
    }

    private static WriteableBitmap TestBitmap()
    {
        var bitmap = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (ILockedFramebuffer buffer = bitmap.Lock())
        {
            byte[] bottom = { 255, 0, 0, 255, 255, 255, 255, 255 };
            byte[] top = { 0, 0, 255, 255, 0, 255, 0, 255 };
            Marshal.Copy(top, 0, buffer.Address, top.Length);
            Marshal.Copy(bottom, 0, IntPtr.Add(buffer.Address, buffer.RowBytes), bottom.Length);
        }

        return bitmap;
    }

    private static World PaletteScene()
    {
        var world = new World();
        world.SetPalette("atelier");
        world.ShowGrid = true;
        world.GridSz = 40;
        world.VanishingPoints[0] = new VanishingPoint(new Vec2(-300, -40), 0);
        world.VanishingPoints[1] = new VanishingPoint(new Vec2(300, -40), 0);
        world.ShowCone = true;
        world.BumpRevision();
        return world;
    }

    private static bool VpAt(World world, int index, double x, double y)
    {
        return world.VanishingPoints[index] is { } point &&
               Math.Abs(point.Pos.X - x) <= 1e-9 &&
               Math.Abs(point.Pos.Y - y) <= 1e-9;
    }

    private static Color PenColor(IPen pen)
    {
        return ((ISolidColorBrush)pen.Brush!).Color;
    }

    private static bool NotBlue(Color color)
    {
        return color.B <= color.R || color.B <= color.G;
    }

    private static void Check(string name, bool condition)
    {
        Console.WriteLine($"CHECK {name} {(condition ? "PASS" : "FAIL")}");
        if (!condition)
        {
            _failedChecks++;
        }
    }

    private static void Render(World world, int width, int height, RenderMode mode, string path)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            Renderer.Draw(context, world, new Size(width, height), mode);
        }

        bitmap.Save(path);
    }
}
