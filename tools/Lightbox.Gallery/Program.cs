using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightbox.Gallery.Stories;

namespace Lightbox.Gallery;

/// <summary>
/// <c>Lightbox.Gallery</c> opens the gallery. <c>Lightbox.Gallery --snapshot
/// &lt;dir&gt;</c> renders every story under every look to
/// <c>&lt;dir&gt;/&lt;look&gt;/&lt;story&gt;.png</c> and exits — headless, on
/// Skia, so it runs on a build machine with no screen.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = Array.IndexOf(args, "--snapshot-app");
        if (app >= 0) return SnapshotApp(app + 1 < args.Length ? args[app + 1] : "app-snapshots");

        var at = Array.IndexOf(args, "--snapshot");
        if (at >= 0)
        {
            // --live renders the look files as they are on disk, unbuilt.
            if (args.Contains("--live")) Looks.SourceDir = Looks.FindSourceDir();
            return Snapshot(at + 1 < args.Length && !args[at + 1].StartsWith("--") ? args[at + 1] : "gallery-snapshots");
        }

        // Live: the look files are read from the source tree and watched.
        Looks.SourceDir = Looks.FindSourceDir();
        AppBuilder.Configure<GalleryApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .StartWithClassicDesktopLifetime(args);
        return 0;
    }

    /// <summary>Kept for the IDE previewer.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<GalleryApp>().UsePlatformDetect().WithInterFont();

    private static int Snapshot(string dir)
    {
        AppBuilder.Configure<GalleryApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .SetupWithoutStarting();

        var app = (GalleryApp)Application.Current!;
        var written = 0;
        foreach (var look in Looks.All())
        {
            app.Use(look);
            var lookDir = Path.Combine(dir, look.Slug);
            Directory.CreateDirectory(lookDir);
            foreach (var story in Catalog.All)
            {
                // 960 wide, per the project's rule for renders a runner pays for.
                var window = new Window
                {
                    Width = 960,
                    SizeToContent = SizeToContent.Height,
                    Background = (Avalonia.Media.IBrush?)app.FindResource("BackgroundSecondaryBrush"),
                    Content = new Border { Padding = new Thickness(24), Child = StoryPanel.Build(story) },
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                using (var frame = window.CaptureRenderedFrame())
                {
                    frame?.Save(Path.Combine(lookDir, Slug(story.Name) + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
                window.Close();
                written++;
            }
        }
        Console.WriteLine($"{written} snapshots in {Path.GetFullPath(dir)}");
        foreach (var error in Looks.Errors) Console.WriteLine("could not load: " + error);
        return 0;
    }

    /// <summary>
    /// Render the app's own windows, as shipped, headless — the main window and
    /// every Configure page. Two of these from two builds, diffed pixel by
    /// pixel, are how a "no visual change" refactor proves itself
    /// (docs/DESIGN-tokens.md, step 1).
    /// </summary>
    /// <remarks>
    /// Isolated the way the test suite isolates itself: settings, brushes, AI
    /// settings and logs go to a scratch folder and the workspace store saves
    /// nowhere, so a snapshot never touches the person's own setup.
    /// </remarks>
    private static int SnapshotApp(string dir)
    {
        var scratch = Path.Combine(Path.GetTempPath(), $"lightbox-gallery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);
        Lightbox.App.Docking.WorkspaceStore.Path = "";
        Lightbox.App.Services.AppSettings.Path = Path.Combine(scratch, "settings.json");
        // Autosave off: its path is the artist's own recovery copy, and is not
        // redirectable, so the timer must never run in here.
        File.WriteAllText(Lightbox.App.Services.AppSettings.Path, """{ "AutosaveMinutes": 0 }""");
        // A pipe of its own, so an agent talking to the live Lightbox can never
        // be answered by this throwaway window.
        Lightbox.App.Services.IpcServer.PipeNameOverride = $"lightbox-gallery-{Guid.NewGuid():N}";
        Lightbox.App.Services.DiagnosticLog.DirectoryOverride = Path.Combine(scratch, "logs");
        Lightbox.App.ViewModels.MainViewModel.BrushStorePath = Path.Combine(scratch, "brushes.json");
        Lightbox.Ai.AiSettings.PathOverride = Path.Combine(scratch, "ai.json");

        GalleryApp.Opening = Look.AsShipped;
        AppBuilder.Configure<GalleryApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .SetupWithoutStarting();
        Directory.CreateDirectory(dir);

        void Shoot(TopLevel window, string name)
        {
            for (var i = 0; i < 3; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }
            using var frame = window.CaptureRenderedFrame();
            frame?.Save(Path.Combine(dir, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        var main = new Lightbox.App.Views.MainWindow { Width = 1600, Height = 1000 };
        main.Show();
        Shoot(main, "main-window");

        var vm = (Lightbox.App.ViewModels.MainViewModel)main.DataContext!;
        // With a document open, so the dockers, the timeline and the canvas
        // bars are in the picture — the "Nothing open" state hides most of the
        // window's literals.
        vm.NewDocument(new Lightbox.App.ViewModels.NewDocumentSettings("Snapshot", 1280, 720, 12, 72, "#ffffff", false));
        Shoot(main, "main-window-document");

        // Every docker, one at a time at the front of its slot, and every
        // tool's options page — the default layout shows only a few of them,
        // and the dockers hold most of the window's literals.
        foreach (var id in Enum.GetValues<Lightbox.App.Docking.DockPanelId>())
        {
            vm.Workspace.SetVisible(id, true);
            vm.Workspace.Activate(id);
            Shoot(main, $"docker-{id}");
        }
        foreach (var tool in Enum.GetValues<Lightbox.App.ViewModels.ToolId>())
        {
            vm.ActiveTool = tool;
            vm.OpenToolOptionsCommand.Execute(null);
            Shoot(main, $"tool-{tool}");
        }
        var config = new Lightbox.App.Views.ConfigureWindow(new Lightbox.App.Services.ShortcutMap(), vm);
        config.Show();
        var list = config.FindControl<ListBox>("CategoryList")!;
        for (var i = 0; i < list.ItemCount; i++)
        {
            list.SelectedIndex = i;
            Shoot(config, $"configure-{i:00}");
        }
        // The project window, on an empty project: its chrome and lists.
        var project = new Lightbox.App.Views.ProjectWindow { Width = 1080, Height = 700 };
        project.Show();
        Shoot(project, "project-window");

        // Every other window and panel in the app's views that opens without
        // arguments: the dialogs, the start screen, the bars. One that needs a
        // document, a project or a service to open is skipped and named, so the
        // gap in coverage is visible rather than silent.
        var skip = new HashSet<Type> { typeof(Lightbox.App.Views.MainWindow), typeof(Lightbox.App.Views.ConfigureWindow), typeof(Lightbox.App.Views.ProjectWindow) };
        var views = typeof(Lightbox.App.Views.MainWindow).Assembly.GetTypes()
            .Where(t => t.Namespace == "Lightbox.App.Views" && typeof(Control).IsAssignableFrom(t) && !t.IsAbstract
                        && !skip.Contains(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t.Name, StringComparer.Ordinal);
        foreach (var type in views)
        {
            try
            {
                var control = (Control)Activator.CreateInstance(type)!;
                var window = control as Window ?? new Window { Width = 900, Height = 600, Content = control };
                if (control is Window w && (double.IsNaN(w.Width) || w.Width <= 0)) { w.Width = 900; w.Height = 600; }
                window.Show();
                Shoot(window, $"view-{type.Name}");
                window.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine($"skipped {type.Name}: {e.GetType().Name}");
            }
        }

        Console.WriteLine($"app snapshots in {Path.GetFullPath(dir)}");
        try { Directory.Delete(scratch, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return 0;
    }

    private static string Slug(string name) =>
        new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
}
