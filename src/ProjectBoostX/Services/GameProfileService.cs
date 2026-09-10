using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using BoostParaPc.Models;
using Microsoft.Win32;

namespace BoostParaPc.Services;

/// <summary>
/// Detecção ampliada de jogos + recomendações por título.
/// </summary>
public static class GameProfileService
{
    /// <summary>Exe|Nome|Plataforma|Perfil — nomes específicos (não usar "cod", "java", "zzz")</summary>
    private static readonly string[] KnownGames =
    [
        "cs2|Counter-Strike 2|Steam|competitive",
        "csgo|CS:GO (legado)|Steam|competitive",
        "valorant|Valorant|Riot|competitive",
        "valorant-win64-shipping|Valorant|Riot|competitive",
        "FortniteClient-Win64-Shipping|Fortnite|Epic|competitive",
        "Fortnite|Fortnite|Epic|competitive",
        // Launcher do Fortnite não vira card separado
        "r5apex|Apex Legends|Steam/EA|competitive",
        "RocketLeague|Rocket League|Epic/Steam|competitive",
        "RainbowSix|Rainbow Six Siege|Steam/Ubisoft|competitive",
        "Overwatch|Overwatch 2|Battle.net|competitive",
        "PUBG|PUBG: Battlegrounds|Steam|competitive",
        "dota2|Dota 2|Steam|esports",
        "LeagueClient|League of Legends|Riot|esports",
        "eldenring|Elden Ring|Steam|openworld",
        "witcher3|The Witcher 3|Steam|openworld",
        "Cyberpunk2077|Cyberpunk 2077|Steam/GOG|openworld",
        "Starfield|Starfield|Steam|openworld",
        "bg3|Baldur's Gate 3|Steam|openworld",
        "GTA5|GTA V|Steam/Rockstar|openworld",
        "GTA5_GTAOnline|GTA Online|Rockstar|openworld",
        "bf2042|Battlefield 2042|Steam/EA|competitive",
        "ModernWarfare|MW / Warzone|Steam|competitive",
        "CallOfDuty|Call of Duty HQ|Steam/Battle.net|competitive",
        "BlackOps6|Black Ops 6|Steam/Battle.net|competitive",
        "Diablo IV|Diablo IV|Battle.net|casual",
        "DiabloIV|Diablo IV|Battle.net|casual",
        "RustClient|Rust|Steam|openworld",
        "DayZ|DayZ|Steam|openworld",
        "EscapeFromTarkov|Escape from Tarkov|Standalone|competitive",
        "Minecraft|Minecraft|Microsoft|casual",
        "Minecraft-Win64|Minecraft Bedrock|Microsoft|casual",
        "RobloxPlayerBeta|Roblox|Standalone|casual",
        "ForzaHorizon5|Forza Horizon 5|Steam/MS|openworld",
        "F1_24|F1 24|Steam|competitive",
        "EAFC24|EA FC 24|EA|competitive",
        "EAFC25|EA FC 25|EA|competitive",
        "NBA2K|NBA 2K|Steam|casual",
        "DeadByDaylight|Dead by Daylight|Steam|casual",
        "Phasmophobia|Phasmophobia|Steam|casual",
        "Terraria|Terraria|Steam|casual",
        "Stardew Valley|Stardew Valley|Steam|casual",
        "Hades|Hades|Steam|casual",
        "Hades2|Hades II|Steam|casual",
        "sekiro|Sekiro|Steam|openworld",
        "DarkSoulsIII|Dark Souls III|Steam|openworld",
        "SlayTheSpire|Slay the Spire|Steam|casual",
        "Among Us|Among Us|Steam|casual",
        "FallGuys|Fall Guys|Epic|casual",
        "Destiny2|Destiny 2|Steam|openworld",
        "Warframe|Warframe|Steam|openworld",
        "PathOfExile|Path of Exile|Steam|openworld",
        "PathOfExile_x64|Path of Exile x64|Steam|openworld",
        "lostark|Lost Ark|Steam|openworld",
        "NewWorld|New World|Steam|openworld",
        "YuanShen|Genshin Impact|Standalone|casual",
        "GenshinImpact|Genshin Impact|Standalone|casual",
        "StarRail|Honkai: Star Rail|Standalone|casual",
        "ZenlessZoneZero|Zenless Zone Zero|Standalone|casual",
        "Deadlock|Deadlock|Steam|competitive",
        "MarvelRivals|Marvel Rivals|Steam|competitive",
        "DeltaForce|Delta Force|Steam|competitive",
        "TheFinals|THE FINALS|Steam|competitive",
        "SquadGame|Squad|Steam|openworld",
        "HellLetLoose|Hell Let Loose|Steam|openworld",
        "iRacing|iRacing|Standalone|competitive",
        "AssettoCorsa|Assetto Corsa|Steam|competitive",
    ];

    private static readonly string[] ExcludedPathFragments =
    [
        @"\microsoft vs code",
        @"\visual studio code",
        @"\vscode",
        @"\codex",
        @"\openai",
        @"\microsoft\edgewebview",
        @"\windows defender",
        @"\dotnet\",
        @"\nodejs\",
        @"\python",
        @"\mozilla firefox\",
        @"\google\chrome\",
        @"\brave-software\",
        @"\opera software\",
        @"\appdata\local\programs",
        @"\appdata\local\microsoft\",
        @"\appdata\roaming\microsoft\",
        @"\system32",
        @"\syswow64",
        @"\steamworks shared",
        @"\redistributables",
        @"\easyanticheat",
        @"\battleye",
        @"\vcredist",
        @"\_commonredist",
        @"\crashreport",
        @"\unitycrashhandler",
        @"\nvidia corporation",
        @"\geforce",
        @"\shadowplay",
        @"\shadoplay",
        @"\obs studio",
        @"\obs-studio",
    ];

    private static readonly string[] ExcludedNameFragments =
    [
        "shadoplay",
        "shadowplay",
        "nvidia",
        "geforce",
        "overlay",
        "installer",
        "unins",
        "redist",
        "vcredist",
        "crashhandler",
        "crashreport",
        "benchmark",
        "dedicated",
        "playtest",
    ];

    private static readonly Dictionary<string, GameRecommendation> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["competitive"] = new(
            "Competitivo / FPS",
            "Tela cheia clássica, prioridade alta, GPU dedicada, sem DPI override. Foco em input lag mínimo.",
            Fso: true, Dpi: false, HighPriority: true, Gpu: true,
            Tips:
            [
                "Use resolução nativa ou 4:3 esticado se preferir",
                "V-Sync OFF no jogo; G-Sync/FreeSync só se o tear incomodar",
                "Mouse raw input no jogo (não use o do Windows)",
                "Feche Discord overlay e navegador se o FPS oscilar",
                "Monitores 240 Hz+: mantenha o plano Alto Desempenho ativo"
            ]),
        ["esports"] = new(
            "eSports / MOBA",
            "Prioridade alta e GPU preferida. FSO off ajuda em team fights.",
            Fso: true, Dpi: false, HighPriority: true, Gpu: true,
            Tips:
            [
                "Cap de FPS um pouco abaixo do refresh evita tearing com V-Sync off",
                "Desative overlays de terceiros no campeão/partida",
                "Prefira cabo de rede em ranked"
            ]),
        ["openworld"] = new(
            "Mundo aberto / AAA",
            "GPU de alto desempenho e prioridade alta. FSO off pode ajudar em stutter; se instável, reative.",
            Fso: true, Dpi: true, HighPriority: true, Gpu: true,
            Tips:
            [
                "Em SSD, mantenha o jogo no mesmo disco do sistema se possível",
                "HAGS ON costuma ajudar em GPUs recentes",
                "Se travar ao compilar shaders, rode uma vez com prioridade Normal",
                "Texturas altas exigem VRAM — monitore no Monitor do app"
            ]),
        ["casual"] = new(
            "Casual / Indie",
            "GPU preferida e FSO off se o jogo for em tela cheia. Prioridade Normal já basta.",
            Fso: true, Dpi: false, HighPriority: false, Gpu: true,
            Tips:
            [
                "Jogos leves não precisam de prioridade alta",
                "Limpeza de RAM antes de sessões longas ajuda em 8 GB",
                "Minecraft/Roblox: priorize GPU dedicada no Windows"
            ]),
    };

    public static IReadOnlyList<GameProfile> DetectGames()
    {
        var found = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in CollectSearchRoots())
        {
            if (!Directory.Exists(root)) continue;
            ScanDirectory(root, found, depth: 0, maxDepth: 5);
        }

        DetectFromSteamAcf(found);
        DetectFromEpicManifests(found);
        DetectFromUninstallRegistry(found);

        return DeduplicateByName(found.Values)
            .Where(g => !IsBlockedGame(g.Name, g.ExecutablePath))
            .OrderBy(g => g.Name)
            .ToList();
    }

    private static bool IsBlockedGame(string name, string exePath)
    {
        var hay = (name + " " + Path.GetFileName(exePath)).ToLowerInvariant();
        return ExcludedNameFragments.Any(f => hay.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Um card por título (Fortnite não vira Client + Launcher + Shipping).</summary>
    private static IReadOnlyList<GameProfile> DeduplicateByName(IEnumerable<GameProfile> games)
    {
        var best = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);

        foreach (var g in games)
        {
            var key = NormalizeTitle(g.Name);
            if (!best.TryGetValue(key, out var existing))
            {
                best[key] = g;
                continue;
            }

            // Prefere exe “principal” (maior arquivo / não-launcher)
            var gScore = ScoreExe(g);
            var eScore = ScoreExe(existing);
            if (gScore > eScore)
                best[key] = g;
        }

        return best.Values.OrderBy(g => g.Name).ToList();
    }

    private static string NormalizeTitle(string name)
    {
        var n = name.Trim();
        // Agrupa variações óbvias do Fortnite e launchers
        if (n.Contains("Fortnite", StringComparison.OrdinalIgnoreCase)) return "Fortnite";
        if (n.Contains("Valorant", StringComparison.OrdinalIgnoreCase)) return "Valorant";
        if (n.Contains("Counter-Strike", StringComparison.OrdinalIgnoreCase) || n.Equals("CS2", StringComparison.OrdinalIgnoreCase))
            return "Counter-Strike 2";
        return n;
    }

    private static int ScoreExe(GameProfile g)
    {
        var file = g.FileName;
        var score = 0;
        try
        {
            if (File.Exists(g.ExecutablePath))
                score += (int)Math.Min(new FileInfo(g.ExecutablePath).Length / 1_000_000, 1000);
        }
        catch { }

        if (file.Contains("Launcher", StringComparison.OrdinalIgnoreCase)) score -= 500;
        if (file.Contains("Shipping", StringComparison.OrdinalIgnoreCase)) score += 200;
        if (file.Contains("Client", StringComparison.OrdinalIgnoreCase)) score += 100;
        if (file.Contains("AntiCheat", StringComparison.OrdinalIgnoreCase) ||
            file.Contains("Crash", StringComparison.OrdinalIgnoreCase)) score -= 800;
        return score;
    }

    public static GameRecommendation GetRecommendation(string profileKey)
        => Profiles.TryGetValue(profileKey, out var r) ? r : Profiles["casual"];

    public static string DescribeRecommendation(GameProfile g)
    {
        var rec = GetRecommendation(g.RecommendationKey);
        return $"{rec.Title} — {rec.Summary}";
    }

    private static List<string> CollectSearchRoots()
    {
        var roots = new List<string>();

        void AddSteam(string steamPath)
        {
            if (string.IsNullOrEmpty(steamPath)) return;
            roots.Add(steamPath);
            roots.Add(Path.Combine(steamPath, "steamapps", "common"));
            try
            {
                var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    {
                        var lib = m.Groups[1].Value.Replace("\\\\", "\\");
                        roots.Add(lib);
                        roots.Add(Path.Combine(lib, "steamapps", "common"));
                    }
                }
            }
            catch { }
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            AddSteam(key?.GetValue("SteamPath")?.ToString());
            using var lm = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            AddSteam(lm?.GetValue("InstallPath")?.ToString());
        }
        catch { }

        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        roots.Add(Path.Combine(pf, "Epic Games"));
        roots.Add(Path.Combine(pf86, "Epic Games"));
        roots.Add(Path.Combine(pf, "Riot Games"));
        roots.Add(Path.Combine(pf86, "Riot Games"));
        roots.Add(Path.Combine(pf86, "Steam"));
        roots.Add(Path.Combine(pf, "Steam"));
        roots.Add(Path.Combine(pf86, "Origin Games"));
        roots.Add(Path.Combine(pf86, "EA Games"));
        roots.Add(Path.Combine(pf86, "Battle.net"));
        roots.Add(Path.Combine(pf, "Battle.net"));
        roots.Add(Path.Combine(pf86, "Ubisoft"));
        roots.Add(Path.Combine(pf, "Ubisoft Game Launcher"));
        roots.Add(Path.Combine(pf86, "GOG Galaxy"));
        roots.Add(Path.Combine(pf, "GOG Galaxy"));
        roots.Add(Path.Combine(local, "Roblox"));
        // NÃO varrer Programs/WindowsApps genéricos — pega VS Code, Codex etc.

        // Outros discos comuns
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            var letter = drive.Name.TrimEnd('\\');
            roots.Add($@"{letter}\SteamLibrary\steamapps\common");
            roots.Add($@"{letter}\Steam\steamapps\common");
            roots.Add($@"{letter}\Epic Games");
            roots.Add($@"{letter}\Games");
            roots.Add($@"{letter}\Jogos");
            roots.Add($@"{letter}\Program Files\Epic Games");
            roots.Add($@"{letter}\Program Files (x86)\Steam\steamapps\common");
            roots.Add($@"{letter}\XboxGames");
        }

        return roots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ScanDirectory(string dir, Dictionary<string, GameProfile> found, int depth, int maxDepth)
    {
        if (depth > maxDepth || found.Count > 250) return;
        if (IsExcludedPath(dir)) return;

        try
        {
            foreach (var exe in Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly))
            {
                TryRegister(exe, found);
            }

            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var leaf = Path.GetFileName(sub);
                if (leaf.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                    leaf.StartsWith('.') ||
                    leaf.Equals("Redist", StringComparison.OrdinalIgnoreCase) ||
                    leaf.Equals("Engine", StringComparison.OrdinalIgnoreCase) ||
                    leaf.Equals("DotNet", StringComparison.OrdinalIgnoreCase) ||
                    leaf.Equals("EasyAntiCheat", StringComparison.OrdinalIgnoreCase) ||
                    leaf.Equals("BattlEye", StringComparison.OrdinalIgnoreCase))
                    continue;
                ScanDirectory(sub, found, depth + 1, maxDepth);
            }
        }
        catch { }
    }

    private static bool IsExcludedPath(string path)
    {
        var p = path.Replace('/', '\\');
        return ExcludedPathFragments.Any(f => p.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Match preciso: igual, prefixo do nome, ou nome completo com separadores. Nunca substring curta.</summary>
    private static bool MatchesExecutable(string fileNameNoExt, string keyName)
    {
        if (string.IsNullOrWhiteSpace(fileNameNoExt) || string.IsNullOrWhiteSpace(keyName))
            return false;

        var name = fileNameNoExt;
        var key = keyName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? keyName[..^4]
            : keyName;

        // 1) Igualdade exata (case-insensitive)
        if (name.Equals(key, StringComparison.OrdinalIgnoreCase))
            return true;

        // 2) Nome do exe começa com a chave e a chave tem 7+ chars (evita Hades→Hades2)
        if (key.Length >= 7 && name.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            return true;

        // 3) Chave contém o nome completo (ex: "League of Legends" dentro do nome)
        if (name.Length >= 6 && key.Contains(name, StringComparison.OrdinalIgnoreCase))
            return false; // genérico demais ao contrário — não aceita

        // 4) Nome contém a chave como token (separado por -_ ou fim)
        if (key.Length >= 6)
        {
            var idx = name.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var beforeOk = idx == 0 || name[idx - 1] is '-' or '_' or ' ';
                var afterIdx = idx + key.Length;
                var afterOk = afterIdx >= name.Length || name[afterIdx] is '-' or '_' or ' ' or '.';
                if (beforeOk && afterOk) return true;
            }
        }

        return false;
    }

    private static void TryRegister(string exe, Dictionary<string, GameProfile> found)
    {
        if (IsExcludedPath(exe)) return;

        var name = Path.GetFileNameWithoutExtension(exe);
        if (name.Length < 4) return;
        if (IsBlockedGame(name, exe)) return;

        foreach (var known in KnownGames)
        {
            var parts = known.Split('|');
            if (parts.Length < 4) continue;
            var keyName = parts[0];

            if (!MatchesExecutable(name, keyName)) continue;

            var key = exe.ToLowerInvariant();
            if (found.ContainsKey(key)) continue;

            found[key] = new GameProfile
            {
                Name = parts[1],
                ExecutablePath = exe,
                Platform = parts[2],
                RecommendationKey = parts[3]
            };
            return;
        }
    }

    private static void DetectFromSteamAcf(Dictionary<string, GameProfile> found)
    {
        try
        {
            var commonDirs = new List<string>();

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                var steamPath = key?.GetValue("SteamPath")?.ToString()?.Replace('/', '\\');
                if (!string.IsNullOrEmpty(steamPath))
                {
                    commonDirs.Add(Path.Combine(steamPath, "steamapps", "common"));
                    var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(vdf))
                    {
                        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        {
                            var lib = m.Groups[1].Value.Replace("\\\\", "\\");
                            commonDirs.Add(Path.Combine(lib, "steamapps", "common"));
                        }
                    }
                }
            }
            catch { }

            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                commonDirs.Add(Path.Combine(drive.Name, "SteamLibrary", "steamapps", "common"));
                commonDirs.Add(Path.Combine(drive.Name, "Steam", "steamapps", "common"));
                commonDirs.Add(Path.Combine(drive.Name, "Jogos", "steamapps", "common"));
                commonDirs.Add(Path.Combine(drive.Name, "Program Files (x86)", "Steam", "steamapps", "common"));
                commonDirs.Add(Path.Combine(drive.Name, "Program Files", "Steam", "steamapps", "common"));
            }

            foreach (var common in commonDirs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(common)) continue;

                    var acfDir = Path.GetDirectoryName(common)!;
                    foreach (var acf in Directory.EnumerateFiles(acfDir, "*.acf"))
                    {
                        try
                        {
                            var text = File.ReadAllText(acf);
                            var nameMatch = Regex.Match(text, "\"name\"\\s+\"([^\"]+)\"");
                            if (!nameMatch.Success) continue;
                            var gameName = nameMatch.Groups[1].Value;

                            int? steamAppId = null;
                            var appIdMatch = Regex.Match(text, "\"appid\"\\s+\"?(\\d+)\"?");
                            if (appIdMatch.Success && int.TryParse(appIdMatch.Groups[1].Value, out var parsedId))
                                steamAppId = parsedId;
                            else
                            {
                                var fileMatch = Regex.Match(Path.GetFileNameWithoutExtension(acf), @"(\d+)$");
                                if (fileMatch.Success && int.TryParse(fileMatch.Groups[1].Value, out var fromFile))
                                    steamAppId = fromFile;
                            }

                            // Procura .exe na pasta do jogo
                            var installDirMatch = Regex.Match(text, "\"installdir\"\\s+\"([^\"]+)\"");
                            var folder = installDirMatch.Success
                                ? Path.Combine(common, installDirMatch.Groups[1].Value)
                                : Path.Combine(common, Path.GetFileNameWithoutExtension(acf));

                            if (!Directory.Exists(folder)) continue;

                            foreach (var exe in Directory.EnumerateFiles(folder, "*.exe", SearchOption.AllDirectories)
                                         .Take(40))
                            {
                                if (IsExcludedPath(exe)) continue;
                                var fname = Path.GetFileNameWithoutExtension(exe);
                                if (fname.Contains("UnityCrash", StringComparison.OrdinalIgnoreCase) ||
                                    fname.Contains("CrashReport", StringComparison.OrdinalIgnoreCase) ||
                                    fname.Contains("UnrealCEF", StringComparison.OrdinalIgnoreCase) ||
                                    fname.Equals("UnityCrashHandler64", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                var key = exe.ToLowerInvariant();
                                if (found.ContainsKey(key)) continue;
                                if (new FileInfo(exe).Length < 100_000) continue; // ignora stubs

                                var matchKnown = KnownGames.FirstOrDefault(k =>
                                    MatchesExecutable(fname, k.Split('|')[0]));
                                var recKey = matchKnown is null
                                    ? GuessProfile(gameName)
                                    : matchKnown.Split('|')[3];
                                var display = matchKnown is null ? gameName : matchKnown.Split('|')[1];

                                found[key] = new GameProfile
                                {
                                    Name = display,
                                    ExecutablePath = exe,
                                    Platform = "Steam",
                                    RecommendationKey = recKey,
                                    SteamAppId = steamAppId
                                };
                                break; // um exe principal por ACF
                            }
                        }
                        catch { }
                    }
                }
        }
        catch { }
    }

    private static void DetectFromEpicManifests(Dictionary<string, GameProfile> found)
    {
        try
        {
            var manifests = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (!Directory.Exists(manifests)) return;

            foreach (var file in Directory.EnumerateFiles(manifests, "*.item"))
            {
                try
                {
                    var json = JsonDocument.Parse(File.ReadAllText(file));
                    var root = json.RootElement;
                    var displayName = root.TryGetProperty("DisplayName", out var dn) ? dn.GetString() : null;
                    var installLoc = root.TryGetProperty("InstallLocation", out var il) ? il.GetString() : null;
                    if (string.IsNullOrEmpty(installLoc) || string.IsNullOrEmpty(displayName)) continue;
                    if (!Directory.Exists(installLoc)) continue;

                    foreach (var exe in Directory.EnumerateFiles(installLoc, "*.exe", SearchOption.AllDirectories).Take(40))
                    {
                        var fname = Path.GetFileNameWithoutExtension(exe);
                        if (fname.Contains("Crash", StringComparison.OrdinalIgnoreCase) ||
                            fname.Contains("EasyAntiCheat", StringComparison.OrdinalIgnoreCase) ||
                            fname.Contains("EOS", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (new FileInfo(exe).Length < 200_000) continue;

                        var key = exe.ToLowerInvariant();
                        if (found.ContainsKey(key)) continue;

                        found[key] = new GameProfile
                        {
                            Name = displayName,
                            ExecutablePath = exe,
                            Platform = "Epic",
                            RecommendationKey = GuessProfile(displayName)
                        };
                        break;
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private static void DetectFromUninstallRegistry(Dictionary<string, GameProfile> found)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var uninstall = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;

                foreach (var subName in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = uninstall.OpenSubKey(subName);
                        var display = sub?.GetValue("DisplayName")?.ToString();
                        var loc = sub?.GetValue("InstallLocation")?.ToString();
                        if (string.IsNullOrEmpty(display) || string.IsNullOrEmpty(loc)) continue;
                        if (!Directory.Exists(loc)) continue;
                        if (!LooksLikeGame(display)) continue;

                        foreach (var exe in Directory.EnumerateFiles(loc, "*.exe", SearchOption.TopDirectoryOnly).Take(15))
                        {
                            var key = exe.ToLowerInvariant();
                            if (found.ContainsKey(key)) continue;
                            if (new FileInfo(exe).Length < 150_000) continue;

                            found[key] = new GameProfile
                            {
                                Name = display,
                                ExecutablePath = exe,
                                Platform = "Registro",
                                RecommendationKey = GuessProfile(display)
                            };
                            break;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    private static bool LooksLikeGame(string name)
    {
        string[] hints = ["game", "jogo", "play", "steam", "epic", "racing", "war", "quest", "saga", "legends", "online"];
        return hints.Any(h => name.Contains(h, StringComparison.OrdinalIgnoreCase));
    }

    private static string GuessProfile(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("counter") || n.Contains("valorant") || n.Contains("cs2") || n.Contains("fortnite")
            || n.Contains("apex") || n.Contains("warzone") || n.Contains("call of duty") || n.Contains("black ops")
            || n.Contains("overwatch") || n.Contains("pubg") || n.Contains("tarkov") || n.Contains("rivals")
            || n.Contains("rainbow") || n.Contains("battlefield"))
            return "competitive";
        if (n.Contains("dota") || n.Contains("league") || n.Contains("lol"))
            return "esports";
        if (n.Contains("elden") || n.Contains("cyberpunk") || n.Contains("witcher") || n.Contains("gta")
            || n.Contains("starfield") || n.Contains("destiny") || n.Contains("baldur") || n.Contains("world")
            || n.Contains("racing") || n.Contains("forza") || n.Contains("souls"))
            return "openworld";
        return "casual";
    }

    public static void ApplyProfile(GameProfile game, bool useRecommendation = true)
    {
        var exe = game.ExecutablePath;
        if (!File.Exists(exe)) return;

        var rec = useRecommendation ? GetRecommendation(game.RecommendationKey) : null;
        var fso = game.IsFullscreenOptDisabled || (rec?.Fso ?? false);
        var dpi = game.IsHighDpiOverridden || (rec?.Dpi ?? false);
        var highPrio = game.IsHighPriority || (rec?.HighPriority ?? false);
        var gpu = game.IsGpuPreferred || (rec?.Gpu ?? false);

        using (var key = Registry.CurrentUser.CreateSubKey(
                   @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", writable: true))
        {
            var flags = new List<string>();
            if (fso)
            {
                flags.Add("~ DISABLEDXMAXIMIZEDWINDOWEDMODE");
                flags.Add("~ DISABLEFULLSCREENOPTIMIZATIONS");
            }
            if (dpi)
                flags.Add("~ HIGHDPIAWARE");

            if (flags.Count > 0)
                key.SetValue(exe, string.Join(" ", flags), RegistryValueKind.String);
            else
                key.DeleteValue(exe, throwOnMissingValue: false);
        }

        if (gpu)
        {
            try
            {
                var gpKey = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\DirectX\UserGpuPreferences", writable: true);
                gpKey?.SetValue(exe, "GpuPreference=2;", RegistryValueKind.String);
            }
            catch { }
        }

        if (highPrio)
        {
            try
            {
                using var ifeo = Registry.LocalMachine.CreateSubKey(
                    $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{game.FileName}",
                    writable: true);
                ifeo?.SetValue("CpuPriorityClass", 3, RegistryValueKind.DWord);
            }
            catch { }
        }

        game.IsFullscreenOptDisabled = fso;
        game.IsHighDpiOverridden = dpi;
        game.IsHighPriority = highPrio;
        game.IsGpuPreferred = gpu;
        game.Status = useRecommendation ? $"Recomendado ({rec?.Title})" : "Perfil aplicado";
    }

    public static void RevertProfile(GameProfile game)
    {
        var exe = game.ExecutablePath;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", writable: true);
            key?.DeleteValue(exe, throwOnMissingValue: false);
        }
        catch { }

        try
        {
            using var gp = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\DirectX\UserGpuPreferences", writable: true);
            gp?.DeleteValue(exe, throwOnMissingValue: false);
        }
        catch { }

        try
        {
            using var ifeo = Registry.LocalMachine.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{game.FileName}",
                writable: true);
            ifeo?.DeleteValue("CpuPriorityClass", throwOnMissingValue: false);
        }
        catch { }

        game.IsFullscreenOptDisabled = false;
        game.IsHighDpiOverridden = false;
        game.IsHighPriority = false;
        game.IsGpuPreferred = false;
        game.Status = "Revertido";
    }

    public static string BoostRunningGames()
    {
        var boosted = 0;
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                var name = proc.ProcessName;
                if (name.Length < 4) continue;
                if (name.Equals("Code", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Codex", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Code - Insiders", StringComparison.OrdinalIgnoreCase))
                {
                    proc.Dispose();
                    continue;
                }

                bool isGame = KnownGames.Any(k => MatchesExecutable(name, k.Split('|')[0]));
                if (!isGame)
                {
                    proc.Dispose();
                    continue;
                }

                proc.PriorityClass = ProcessPriorityClass.High;
                boosted++;
            }
            catch { }
            finally { proc.Dispose(); }
        }
        return boosted > 0 ? $"{boosted} processos de jogo em prioridade alta" : "Nenhum jogo rodando detectado";
    }

    private static bool IsLikelyGameProcess(string name)
    {
        string[] hints = ["game", "shipping", "client-win64", "unity", "unreal", "dx11", "dx12"];
        return hints.Any(h => name.Contains(h, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record GameRecommendation(
    string Title,
    string Summary,
    bool Fso,
    bool Dpi,
    bool HighPriority,
    bool Gpu,
    IReadOnlyList<string> Tips);
