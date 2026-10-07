using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
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

    private static string Slug(string name) =>
        new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
}
