using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BoostParaPc.Services;

/// <summary>
/// Capas estilo Netflix: Steam CDN + cache local + fallback gradiente com iniciais.
/// </summary>
public static class CoverService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        // Só JPEG/PNG — o filtro de magic bytes e o decoder WPF não aceitam WebP/AVIF
        c.DefaultRequestHeaders.Accept.ParseAdd("image/jpeg,image/png,image/*;q=0.8,*/*;q=0.5");
        return c;
    }

    // Nome do jogo → Steam AppId (quando existe na Steam)
    private static readonly Dictionary<string, int> SteamAppIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Counter-Strike 2"] = 730,
        ["CS:GO (legado)"] = 730,
        ["Apex Legends"] = 1172470,
        ["Rocket League"] = 252950,
        ["Rainbow Six Siege"] = 359550,
        ["Overwatch 2"] = 2357570,
        ["PUBG: Battlegrounds"] = 578080,
        ["Dota 2"] = 570,
        ["Elden Ring"] = 1245620,
        ["The Witcher 3"] = 292030,
        ["Cyberpunk 2077"] = 1091500,
        ["Starfield"] = 1716740,
        ["Baldur's Gate 3"] = 1086940,
        ["GTA V"] = 271590,
        ["Battlefield 2042"] = 1517290,
        ["Diablo IV"] = 2344520,
        ["Rust"] = 252490,
        ["DayZ"] = 221100,
        ["Forza Horizon 5"] = 1551360,
        ["F1 24"] = 2488620,
        ["EA FC 24"] = 2195250,
        ["EA FC 25"] = 2669320,
        ["Dead by Daylight"] = 381210,
        ["Phasmophobia"] = 739630,
        ["Terraria"] = 105600,
        ["Stardew Valley"] = 413150,
        ["Hades"] = 1145360,
        ["Hades II"] = 1145350,
        ["Sekiro"] = 814380,
        ["Dark Souls III"] = 374320,
        ["Slay the Spire"] = 646570,
        ["Among Us"] = 945360,
        ["Fall Guys"] = 1097150,
        ["Destiny 2"] = 1085660,
        ["Warframe"] = 230410,
        ["Path of Exile"] = 238960,
        ["Path of Exile x64"] = 238960,
        ["Lost Ark"] = 1599340,
        ["New World"] = 1063730,
        ["Deadlock"] = 1422450,
        ["Marvel Rivals"] = 2767030,
        ["Delta Force"] = 2507950,
        ["THE FINALS"] = 2073850,
        ["Squad"] = 393380,
        ["Hell Let Loose"] = 686810,
        ["Assetto Corsa"] = 244210,
        ["iRacing"] = 384300,
        ["NBA 2K"] = 2878980,
        ["Minecraft"] = 1672970,
        ["Roblox"] = 2407510,
        // Genshin não é da Steam — sem AppId (usa fallback gradiente)
    };

    private static string CoverDir
    {
        get
        {
            var d = Path.Combine(AppPaths.DataDir, "covers");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static int? TryGetSteamAppId(string gameName, string recommendationKey)
    {
        if (SteamAppIds.TryGetValue(gameName, out var id) && id > 0)
            return id;
        return null;
    }

    private static void Log(string msg)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "projectboostx-covers.log"),
                $"{DateTime.Now:HH:mm:ss} {msg}{Environment.NewLine}");
        }
        catch { }
    }

    private static bool IsJpeg(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[2];
            using var fs = File.OpenRead(path);
            if (fs.Read(header) < 2) return false;
            return header[0] == 0xFF && header[1] == 0xD8;
        }
        catch { return false; }
    }

    /// <summary>Resolve caminho de arquivo de capa (Steam ou fallback) para o binding da UI.</summary>
    public static Task<string> ResolveCoverPathAsync(string gameName, CancellationToken ct = default)
    {
        return Task.Run(async () =>
        {
            try
            {
                var steam = await EnsureCoverAsync(gameName, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(steam) && File.Exists(steam))
                {
                    Log($"OK steam {gameName} -> {steam} ({new FileInfo(steam).Length}b)");
                    return steam;
                }
                Log($"FALLBACK {gameName}");
            }
            catch (Exception ex)
            {
                Log($"ERR {gameName}: {ex.Message}");
            }

            // Fallback em thread de UI (RenderTargetBitmap é frágil fora de STA)
            if (System.Windows.Application.Current?.Dispatcher is { } disp && !disp.CheckAccess())
            {
                return await disp.InvokeAsync(() => EnsureFallbackCoverFile(gameName));
            }
            return EnsureFallbackCoverFile(gameName);
        });
    }

    /// <summary>Garante arquivo de capa local. Retorna caminho ou null se falhar (UI usa fallback).</summary>
    public static async Task<string?> EnsureCoverAsync(string gameName, CancellationToken ct = default)
    {
        var appId = TryGetSteamAppId(gameName, "");
        if (appId is null) return null;

        var path = Path.Combine(CoverDir, $"{appId}.jpg");
        if (File.Exists(path) && new FileInfo(path).Length > 8000 && IsJpeg(path))
            return path;
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }

        // library_600x900 (retrato, melhor no card Netflix) antes do header paisagem
        string[] urls =
        [
            $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900.jpg",
            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg",
            $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/library_600x900.jpg",
            $"https://steamcdn-a.akamaihd.net/steam/apps/{appId}/library_600x900.jpg",
            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg",
            $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg",
            $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg",
            $"https://steamcdn-a.akamaihd.net/steam/apps/{appId}/header.jpg",
        ];

        foreach (var url in urls)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) continue;
                var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                if (bytes.Length < 4000) continue;
                // JPEG FF D8 ou PNG 89 50
                if (!(bytes[0] == 0xFF && bytes[1] == 0xD8) && !(bytes[0] == 0x89 && bytes[1] == 0x50))
                    continue;
                await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
                return path;
            }
            catch
            {
                // tenta próxima URL
            }
        }

        return null;
    }

    public static async Task PrefetchCoversAsync(
        IEnumerable<string> gameNames,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        foreach (var name in gameNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (ct.IsCancellationRequested) break;
            if (TryGetSteamAppId(name, "") is null) continue;
            progress?.Report($"Capa: {name}");
            await EnsureCoverAsync(name, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Gradiente + iniciais. Também grava PNG no cache para a UI carregar por path.</summary>
    public static string EnsureFallbackCoverFile(string gameName)
    {
        var safe = string.Join("_", gameName.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(CoverDir, $"fb_{safe}.png");
        if (File.Exists(path) && new FileInfo(path).Length > 500)
            return path;

        var initials = GetInitials(gameName);
        var (c1, c2) = HashColors(gameName);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var rect = new System.Windows.Rect(0, 0, 160, 220);
            var brush = new LinearGradientBrush(c1, c2, 45);
            dc.DrawRectangle(brush, null, rect);

            var glow = new RadialGradientBrush(
                Color.FromArgb(50, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255))
            {
                Center = new System.Windows.Point(0.3, 0.25),
                RadiusX = 0.7,
                RadiusY = 0.7
            };
            dc.DrawRectangle(glow, null, rect);

            var ft = new FormattedText(
                initials,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                48,
                Brushes.White,
                1.25);

            dc.DrawText(ft, new System.Windows.Point(
                (160 - ft.Width) / 2,
                (220 - ft.Height) / 2 - 8));
        }

        var bmp = new RenderTargetBitmap(160, 220, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        encoder.Save(fs);

        return path;
    }

    public static ImageSource CreateFallbackCover(string gameName)
    {
        try
        {
            var path = EnsureFallbackCoverFile(gameName);
            var img = LoadCoverImage(path);
            if (img is not null) return img;
        }
        catch { }
        // último recurso: bitmap em memória
        var bmp = new RenderTargetBitmap(160, 220, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new LinearGradientBrush(Colors.Indigo, Colors.Teal, 45), null, new System.Windows.Rect(0, 0, 160, 220));
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    public static ImageSource? LoadCoverImage(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.DecodePixelWidth = 320;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static string GetInitials(string name)
    {
        var parts = name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1)
            return parts[0].Length >= 2
                ? parts[0][..2].ToUpperInvariant()
                : parts[0].ToUpperInvariant();
        return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
    }

    private static (Color, Color) HashColors(string name)
    {
        var hash = name.ToLowerInvariant().Aggregate(0, (a, c) => a * 31 + c);
        var h1 = (hash & 0xFF) / 255.0;
        var h2 = ((hash >> 8) & 0xFF) / 255.0;
        var c1 = HsvToRgb(0.55 + h1 * 0.35, 0.65, 0.35); // cyan→violet range
        var c2 = HsvToRgb(0.75 + h2 * 0.2, 0.7, 0.18);
        return (c1, c2);
    }

    private static Color HsvToRgb(double h, double s, double v)
    {
        h = ((h % 1.0) + 1.0) % 1.0;
        var i = (int)(h * 6);
        var f = h * 6 - i;
        var p = v * (1 - s);
        var q = v * (1 - f * s);
        var t = v * (1 - (1 - f) * s);
        double r, g, b;
        switch (i % 6)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }
}
