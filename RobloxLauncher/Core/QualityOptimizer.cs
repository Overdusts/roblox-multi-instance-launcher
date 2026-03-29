using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RobloxLauncher.Core;

public enum QualityPreset
{
    Potato,     // Absolute minimum - for max instances
    Low,        // Playable but ugly
    Medium,     // Balanced
    Default     // Don't touch settings
}

public static class QualityOptimizer
{
    public static string? GetRobloxPath()
    {
        // Check common Roblox install locations
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string robloxVersionsPath = Path.Combine(localAppData, "Roblox", "Versions");

        if (Directory.Exists(robloxVersionsPath))
        {
            // Find the latest version with RobloxPlayerBeta.exe
            var versionDirs = Directory.GetDirectories(robloxVersionsPath)
                .Where(d => File.Exists(Path.Combine(d, "RobloxPlayerBeta.exe")))
                .OrderByDescending(d => Directory.GetLastWriteTime(d))
                .FirstOrDefault();

            if (versionDirs != null)
                return versionDirs;
        }

        // Check Program Files
        string programFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Roblox", "Versions");
        if (Directory.Exists(programFiles))
        {
            var versionDir = Directory.GetDirectories(programFiles)
                .Where(d => File.Exists(Path.Combine(d, "RobloxPlayerBeta.exe")))
                .OrderByDescending(d => Directory.GetLastWriteTime(d))
                .FirstOrDefault();

            if (versionDir != null)
                return versionDir;
        }

        // Try registry
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\ROBLOX Corporation\Environments\roblox-player");
            string? path = key?.GetValue("")?.ToString();
            if (path != null && File.Exists(path))
                return Path.GetDirectoryName(path);
        }
        catch { }

        return null;
    }

    public static Dictionary<string, object> GetFFlags(QualityPreset preset)
    {
        return preset switch
        {
            QualityPreset.Potato => GetPotatoFlags(),
            QualityPreset.Low => GetLowFlags(),
            QualityPreset.Medium => GetMediumFlags(),
            _ => new Dictionary<string, object>()
        };
    }

    private static Dictionary<string, object> GetPotatoFlags()
    {
        return new Dictionary<string, object>
        {
            // ═══ FPS — 1 FPS is enough for Blade Ball AFK ═══
            ["DFIntTaskSchedulerTargetFps"] = 1,

            // ═══ Rendering — force lowest possible ═══
            ["FFlagDebugGraphicsPreferVulkan"] = false,
            ["FFlagDebugGraphicsPreferD3D11FL10"] = true,
            ["DFIntDebugFRMQualityLevelOverride"] = 1,
            ["FIntRenderLocalLightUpdatesMax"] = 1,
            ["FIntRenderLocalLightUpdatesMin"] = 1,
            ["FIntRenderShadowIntensity"] = 0,
            ["FFlagDebugDisableShadows"] = true,
            ["FFlagDisablePostFx"] = true,
            ["FFlagDebugSkyGray"] = true,

            // ═══ Force voxel lighting (cheapest) ═══
            ["DFFlagDebugRenderForceTechnologyVoxel"] = true,
            ["FFlagFastGPULightCulling3"] = true,

            // ═══ Textures — absolute minimum ═══
            ["DFIntTextureQualityOverride"] = 0,
            ["DFIntTextureCompositorActiveJobs"] = 0,
            ["DFIntTextureCompositorQueueSize"] = 0,

            // ═══ Meshes & models — lowest LOD ═══
            ["DFIntCSGLevelOfDetailSwitchingDistance"] = 0,
            ["DFIntCSGLevelOfDetailSwitchingDistanceL12"] = 0,
            ["DFIntCSGLevelOfDetailSwitchingDistanceL23"] = 0,
            ["DFIntCSGLevelOfDetailSwitchingDistanceL34"] = 0,
            ["DFIntMaxMeshDataBufferSizeMB"] = 8,

            // ═══ Particles — kill everything ═══
            ["DFIntMaxParticleSpriteCount"] = 0,
            ["DFIntMaxParticleMeshCount"] = 0,
            ["FIntEmitterMaxSpawnedPerFrame"] = 0,
            ["FFlagEnableParticleEmitterCustomRate"] = false,

            // ═══ Terrain & grass — nuke it ═══
            ["FIntTerrainOctreeMaxDepth"] = 1,
            ["FIntRenderGrassDetailStrands"] = 0,
            ["FFlagGrassMovement"] = false,
            ["FIntGrassMovementReducedMotionFactor"] = 0,

            // ═══ Wind & environment ═══
            ["FFlagGlobalWindRendering"] = false,
            ["FFlagDebugDisableWater"] = true,

            // ═══ Draw distance — very short ═══
            ["DFIntDebugRestrictGCDistance"] = 50,
            ["DFIntRenderingThrottleDelayInMS"] = 500,

            // ═══ Disable all telemetry ═══
            ["FFlagDebugDisableTelemetryEphemeralCounter"] = true,
            ["FFlagDebugDisableTelemetryEphemeralStat"] = true,
            ["FFlagDebugDisableTelemetryEventIngest"] = true,
            ["FFlagDebugDisableTelemetryPoint"] = true,
            ["FFlagDebugDisableTelemetryV2Counter"] = true,
            ["FFlagDebugDisableTelemetryV2Event"] = true,
            ["FFlagDebugDisableTelemetryV2Stat"] = true,

            // ═══ Network — reduce overhead ═══
            ["DFIntHttpCurlConnectionCacheSize"] = 5,

            // ═══ Memory — minimize everything ═══
            ["DFIntMaxImagesCacheSize"] = 32,
            ["DFIntAnimationLodFadeInDistance"] = 0,
            ["DFIntAnimationLodFadeOutDistance"] = 0,

            // ═══ Audio — disable (saves CPU) ═══
            ["FFlagDebugDisableVoiceChat"] = true,
            ["DFIntMaxSoundsPerFrame"] = 0,

            // ═══ Physics — reduce CPU ═══
            ["FFlagDebugSimPhysicsSingleStepping"] = true,
            ["DFIntPhysicsAnalyticsHighFrequencyIntervalInSeconds"] = 9999,

            // ═══ GUI — lighten ═══
            ["DFIntCanHideGuiGroupId"] = 0,
            ["FFlagAdServiceEnabled"] = false,

            // ═══ Unfocused throttling — near-zero when alt-tabbed ═══
            ["FIntRenderWindowManagerFrameRateManagerBackgroundFps"] = 1,
        };
    }

    private static Dictionary<string, object> GetLowFlags()
    {
        return new Dictionary<string, object>
        {
            ["DFIntTaskSchedulerTargetFps"] = 15,
            ["DFIntDebugFRMQualityLevelOverride"] = 2,
            ["FIntRenderShadowIntensity"] = 0,
            ["FFlagDebugDisableShadows"] = true,
            ["DFIntTextureQualityOverride"] = 1,
            ["DFIntMaxParticleSpriteCount"] = 10,
            ["FFlagDisablePostFx"] = true,
            ["FIntRenderGrassDetailStrands"] = 0,
            ["DFIntDebugRestrictGCDistance"] = 300,
            ["FFlagDebugDisableTelemetryEphemeralCounter"] = true,
            ["FFlagDebugDisableTelemetryEphemeralStat"] = true,
            ["FFlagDebugDisableTelemetryEventIngest"] = true,
            ["FFlagDebugDisableTelemetryPoint"] = true,
            ["FFlagDebugDisableTelemetryV2Counter"] = true,
            ["FFlagDebugDisableTelemetryV2Event"] = true,
            ["FFlagDebugDisableTelemetryV2Stat"] = true,
        };
    }

    private static Dictionary<string, object> GetMediumFlags()
    {
        return new Dictionary<string, object>
        {
            ["DFIntTaskSchedulerTargetFps"] = 30,
            ["DFIntDebugFRMQualityLevelOverride"] = 4,
            ["FIntRenderShadowIntensity"] = 50,
            ["DFIntTextureQualityOverride"] = 2,
            ["FFlagDebugDisableTelemetryEphemeralCounter"] = true,
            ["FFlagDebugDisableTelemetryEphemeralStat"] = true,
            ["FFlagDebugDisableTelemetryEventIngest"] = true,
            ["FFlagDebugDisableTelemetryPoint"] = true,
            ["FFlagDebugDisableTelemetryV2Counter"] = true,
            ["FFlagDebugDisableTelemetryV2Event"] = true,
            ["FFlagDebugDisableTelemetryV2Stat"] = true,
        };
    }

    public static void ApplyFFlags(string robloxPath, QualityPreset preset)
    {
        if (preset == QualityPreset.Default)
            return;

        string settingsDir = Path.Combine(robloxPath, "ClientSettings");
        if (!Directory.Exists(settingsDir))
            Directory.CreateDirectory(settingsDir);

        string settingsFile = Path.Combine(settingsDir, "ClientAppSettings.json");
        var flags = GetFFlags(preset);

        // Merge with existing flags if any
        var existing = new JObject();
        if (File.Exists(settingsFile))
        {
            try
            {
                existing = JObject.Parse(File.ReadAllText(settingsFile));
            }
            catch { }
        }

        foreach (var kvp in flags)
        {
            existing[kvp.Key] = JToken.FromObject(kvp.Value);
        }

        File.WriteAllText(settingsFile, existing.ToString(Formatting.Indented));
    }

    public static void ResetFFlags(string robloxPath)
    {
        string settingsFile = Path.Combine(robloxPath, "ClientSettings", "ClientAppSettings.json");
        if (File.Exists(settingsFile))
            File.Delete(settingsFile);
    }
}
