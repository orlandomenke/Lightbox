using System.Globalization;
using System.Text;
using SkiaSharp;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Hand-written Unity text assets in the shape a real project serializes them
/// (S8: made for the tests, never copied from a game).
/// </summary>
internal static class UnityFixture
{
    public sealed record Key(double Time, string? Guid, long FileId);

    public sealed record Track(string Path, params Key[] Keys);

    public sealed record Sprite(string Name, long Id, int X, int Y, int W, int H, int Alignment = 7,
        double PivotX = 0.5, double PivotY = 0);

    private static string F(double d) => d.ToString("0.########", CultureInfo.InvariantCulture);

    public static string Anim(string name, double sampleRate, bool loop, double stopTime,
        Track[] tracks, string extraCurves = "", int events = 0)
    {
        var sb = new StringBuilder();
        sb.Append("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!74 &7400000\nAnimationClip:\n");
        sb.Append("  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n");
        sb.Append($"  m_Name: {name}\n  serializedVersion: 8\n  m_Legacy: 0\n");
        sb.Append("  m_RotationCurves: []\n  m_CompressedRotationCurves: []\n  m_EulerCurves: []\n");
        sb.Append(extraCurves.Length > 0 ? extraCurves : "  m_PositionCurves: []\n");
        sb.Append("  m_ScaleCurves: []\n  m_FloatCurves: []\n");
        sb.Append(tracks.Length == 0 ? "  m_PPtrCurves: []\n" : "  m_PPtrCurves:\n");
        foreach (var t in tracks)
        {
            sb.Append("  - serializedVersion: 2\n    curve:\n");
            foreach (var k in t.Keys)
            {
                sb.Append($"    - time: {F(k.Time)}\n");
                sb.Append(k.Guid is null
                    ? "      value: {fileID: 0}\n"
                    : $"      value: {{fileID: {k.FileId}, guid: {k.Guid}, type: 3}}\n");
            }
            sb.Append("    attribute: m_Sprite\n");
            sb.Append($"    path: {t.Path}\n");
            sb.Append("    classID: 212\n    script: {fileID: 0}\n    flags: 2\n");
        }
        sb.Append($"  m_SampleRate: {F(sampleRate)}\n  m_WrapMode: 0\n");
        sb.Append("  m_Bounds:\n    m_Center: {x: 0, y: 0, z: 0}\n    m_Extent: {x: 0, y: 0, z: 0}\n");
        sb.Append("  m_AnimationClipSettings:\n    serializedVersion: 2\n");
        sb.Append("    m_AdditiveReferencePoseClip: {fileID: 0}\n    m_AdditiveReferencePoseTime: 0\n");
        sb.Append($"    m_StartTime: 0\n    m_StopTime: {F(stopTime)}\n    m_OrientationOffsetY: 0\n");
        sb.Append($"    m_LoopTime: {(loop ? 1 : 0)}\n    m_LoopBlend: 0\n");
        if (events == 0) sb.Append("  m_Events: []\n");
        else
        {
            sb.Append("  m_Events:\n");
            for (var e = 0; e < events; e++)
                sb.Append($"  - time: {F(e * 0.1)}\n    functionName: Hit{e}\n    data: \n    objectReferenceParameter: {{fileID: 0}}\n");
        }
        return sb.ToString();
    }

    public static string MultipleMeta(string guid, double ppu, params Sprite[] sprites)
    {
        var sb = new StringBuilder();
        sb.Append($"fileFormatVersion: 2\nguid: {guid}\nTextureImporter:\n  internalIDToNameTable: []\n");
        sb.Append("  externalObjects: {}\n  serializedVersion: 13\n  mipmaps:\n    mipMapMode: 0\n    enableMipMap: 0\n");
        sb.Append($"  spriteMode: 2\n  spriteExtrude: 1\n  alignment: 0\n  spritePivot: {{x: 0.5, y: 0.5}}\n");
        sb.Append($"  spritePixelsToUnits: {F(ppu)}\n  textureType: 8\n");
        sb.Append("  platformSettings:\n  - serializedVersion: 4\n    buildTarget: DefaultTexturePlatform\n    maxTextureSize: 8192\n");
        sb.Append("  spriteSheet:\n    serializedVersion: 2\n    sprites:\n");
        foreach (var s in sprites)
        {
            sb.Append($"    - serializedVersion: 2\n      name: {s.Name}\n      rect:\n        serializedVersion: 2\n");
            sb.Append($"        x: {s.X}\n        y: {s.Y}\n        width: {s.W}\n        height: {s.H}\n");
            sb.Append($"      alignment: {s.Alignment}\n      pivot: {{x: {F(s.PivotX)}, y: {F(s.PivotY)}}}\n");
            sb.Append("      border: {x: 0, y: 0, z: 0, w: 0}\n      customData: \n      outline: []\n      physicsShape: []\n");
            sb.Append("      tessellationDetail: -1\n      bones: []\n      spriteID: 0123456789abcdef0123456789abcdef\n");
            sb.Append($"      internalID: {s.Id}\n      vertices: []\n      indices: \n      edges: []\n      weights: []\n");
        }
        sb.Append("    outline: []\n    customData: \n    physicsShape: []\n    bones: []\n");
        sb.Append("    spriteID: 5e97eb03825dee720800000000000000\n    internalID: 0\n");
        sb.Append("  spritePackingTag: \n  userData: \n  assetBundleName: \n");
        return sb.ToString();
    }

    public static string SingleMeta(string guid, int alignment = 7, double px = 0.5, double py = 0) =>
        $"fileFormatVersion: 2\nguid: {guid}\nTextureImporter:\n  internalIDToNameTable: []\n" +
        $"  spriteMode: 1\n  alignment: {alignment}\n  spritePivot: {{x: {F(px)}, y: {F(py)}}}\n" +
        "  spritePixelsToUnits: 100\n  textureType: 8\n  spriteSheet:\n    serializedVersion: 2\n    sprites: []\n" +
        "    outline: []\n    internalID: 0\n";

    public static string Guid(int n) => n.ToString("x32", CultureInfo.InvariantCulture);

    public static byte[] Png(int w, int h)
    {
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>A throwaway folder laid out like a Unity project, deleted on dispose.</summary>
    public sealed class Project : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "lightbox-unity-" + System.Guid.NewGuid().ToString("N"));

        public Project()
        {
            Directory.CreateDirectory(Path.Combine(Root, "Assets"));
            Directory.CreateDirectory(Path.Combine(Root, "ProjectSettings"));
            File.WriteAllText(Path.Combine(Root, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 6000.0.0f1\n");
        }

        public void Write(string relative, string text) => Write(relative, Encoding.UTF8.GetBytes(text));

        public void Write(string relative, byte[] bytes)
        {
            var full = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }

        public void Texture(string relative, int w, int h, string meta)
        {
            Write(relative, Png(w, h));
            Write(relative + ".meta", meta);
        }

        public void Clip(string relative, string anim, int n)
        {
            Write(relative, anim);
            Write(relative + ".meta", $"fileFormatVersion: 2\nguid: {Guid(9000 + n)}\nNativeFormatImporter:\n  mainObjectFileID: 7400000\n");
        }

        public void Dispose()
        {
            try
            {
                // Remove links first, as links: a recursive delete must never reach
                // through one into the folder it points at.
                foreach (var d in Directory.EnumerateDirectories(Root, "*", SearchOption.AllDirectories)
                             .Where(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0).ToList())
                    Directory.Delete(d);
                Directory.Delete(Root, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
