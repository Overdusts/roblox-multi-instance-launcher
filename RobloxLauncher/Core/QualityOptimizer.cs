using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RobloxLauncher.Core;

public enum QualityPreset
{
    Potato,
    Low,
    Medium,
    Default,
    MarvelRivals,       // Optimized for Marvel Rivals — balanced visuals + perf
    MarvelRivalsPotato, // Marvel Rivals absolute minimum
}

public static class QualityOptimizer
{
    public static string? GetRobloxPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string robloxVersionsPath = Path.Combine(localAppData, "Roblox", "Versions");

        if (Directory.Exists(robloxVersionsPath))
        {
            var versionDir = Directory.GetDirectories(robloxVersionsPath)
                .Where(d => File.Exists(Path.Combine(d, "RobloxPlayerBeta.exe")))
                .OrderByDescending(d => Directory.GetLastWriteTime(d))
                .FirstOrDefault();

            if (versionDir != null)
                return versionDir;
        }

        // Program Files
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

        // Registry
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
            QualityPreset.MarvelRivals => GetMarvelRivalsFlags(),
            QualityPreset.MarvelRivalsPotato => GetMarvelRivalsPotatoFlags(),
            _ => new Dictionary<string, object>()
        };
    }

    /// <summary>
    /// Marvel Rivals optimized — keeps ability effects visible at 30fps,
    /// disables cosmetic fluff, keeps textures readable for hero identification.
    /// </summary>
    private static Dictionary<string, object> GetMarvelRivalsFlags()
    {
        return new Dictionary<string, object>
        {
            // 30 FPS — smooth enough for combat, saves CPU
            ["DFIntTaskSchedulerTargetFps"] = 30,
            ["DFIntDebugFRMQualityLevelOverride"] = 3,
            // D3D11 is more stable for multi-instance
            ["FFlagDebugGraphicsPreferVulkan"] = false,
            ["FFlagDebugGraphicsPreferD3D11FL10"] = true,
            // Shadows off — huge perf gain, abilities still visible
            ["FIntRenderShadowIntensity"] = 0,
            ["FFlagDebugDisableShadows"] = true,
            // Keep textures at low-med so you can identify heroes
            ["DFIntTextureQualityOverride"] = 1,
            // Reduce particles but keep ability effects visible
            ["DFIntMaxParticleSpriteCount"] = 50,
            ["DFIntMaxParticleMeshCount"] = 25,
            ["FIntEmitterMaxSpawnedPerFrame"] = 8,
            // Disable post-processing (bloom, DOF) — cleaner combat view
            ["FFlagDisablePostFx"] = true,
            // Disable grass/wind — not needed for Marvel Rivals maps
            ["FIntRenderGrassDetailStrands"] = 0,
            ["FFlagGrassMovement"] = false,
            ["FFlagGlobalWindRendering"] = false,
            // Reduce draw distance slightly
            ["DFIntDebugRestrictGCDistance"] = 500,
            // Lighting — use voxel for speed
            ["DFFlagDebugRenderForceTechnologyVoxel"] = true,
            ["FFlagFastGPULightCulling3"] = true,
            ["FIntRenderLocalLightUpdatesMax"] = 4,
            ["FIntRenderLocalLightUpdatesMin"] = 2,
            // Mesh/memory limits
            ["DFIntMaxMeshDataBufferSizeMB"] = 32,
            ["DFIntCSGLevelOfDetailSwitchingDistance"] = 100,
            ["DFIntCSGLevelOfDetailSwitchingDistanceL12"] = 200,
            ["DFIntCSGLevelOfDetailSwitchingDistanceL23"] = 400,
            ["DFIntCSGLevelOfDetailSwitchingDistanceL34"] = 600,
            // Terrain
            ["FIntTerrainOctreeMaxDepth"] = 4,
            // Keep water on (some maps have it)
            ["FFlagDebugDisableWater"] = false,
            // Background FPS limit when unfocused
            ["FIntRenderWindowManagerFrameRateManagerBackgroundFps"] = 5,
            // Disable telemetry — saves CPU + network
            ["FFlagDebugDisableTelemetryEphemeralCounter"] = true,
            ["FFlagDebugDisableTelemetryEphemeralStat"] = true,
            ["FFlagDebugDisableTelemetryEventIngest"] = true,
            ["FFlagDebugDisableTelemetryPoint"] = true,
            ["FFlagDebugDisableTelemetryV2Counter"] = true,
            ["FFlagDebugDisableTelemetryV2Event"] = true,
            ["FFlagDebugDisableTelemetryV2Stat"] = true,
            // Disable ads
            ["FFlagAdServiceEnabled"] = false,
            // Reduce HTTP cache to save RAM
            ["DFIntHttpCurlConnectionCacheSize"] = 10,
            ["DFIntMaxImagesCacheSize"] = 64,
        };
    }

    /// <summary>
    /// Marvel Rivals absolute potato — max instances, minimum resources.
    /// Only use for AFK farming or accounts that don't need to play actively.
    /// </summary>
    private static Dictionary<string, object> GetMarvelRivalsPotatoFlags()
    {
        return new Dictionary<string, object>
        {
            ["DFIntTaskSchedulerTargetFps"] = 1,
            ["FFlagDebugGraphicsPreferVulkan"] = false,
            ["FFlagDebugGraphicsPreferD3D11FL10"] = true,
            ["DFIntDebugFRMQualityLevelOverride"] = 1,
            ["FIntRenderLocalLightUpdatesMax"] = 1,
            ["FIntRenderLocalLightUpdatesMin"] = 1,
            ["FIntRenderShadowIntensity"] = 0,
            ["FFlagDebugDisableShadows"] = true,
            ["FFlagDisablePostFx"] = true,
            ["FFlagDebugSkyGray"] = true,
            ["DFFlagDebugRenderForceTechnologyVoxel"] = true,
            ["FFlagFastGPULightCulling3"] = true,
            ["DFIntTextureQualityOverride"] = 0,
            ["DFIntTextureCompositorActiveJobs"] = 0,
            ["DFIntTextureCompositorQueueSize"] = 0,
            ["DFIntCSGLevelOfDetailSwitchingDistance"] = 0,
            ["DFIntCSGLevelOfDetailSwitchingDistanceL12"] = 0,
            ["DFIntCSGLevelOfDetailSwitchingDistanceL23"] = 0,
            ["DFIntCSGLevelOfDetailSwitchingDistanceL34"] = 0,
            ["DFIntMaxMeshDataBufferSizeMB"] = 8,
            ["DFIntMaxParticleSpriteCount"] = 0,
            ["DFIntMaxParticleMeshCount"] = 0,
            ["FIntEmitterMaxSpawnedPerFrame"] = 0,
            ["FFlagEnableParticleEmitterCustomRate"] = false,
            ["FIntTerrainOctreeMaxDepth"] = 1,
            ["FIntRenderGrassDetailStrands"] = 0,
            ["FFlagGrassMovement"] = false,
            ["FIntGrassMovementReducedMotionFactor"] = 0,
            ["FFlagGlobalWindRendering"] = false,
            ["FFlagDebugDisableWater"] = true,
            ["DFIntDebugRestrictGCDistance"] = 50,
            ["DFIntRenderingThrottleDelayInMS"] = 500,
            ["FFlagDebugDisableTelemetryEphemeralCounter"] = true,
            ["FFlagDebugDisableTelemetryEphemeralStat"] = true,
            ["FFlagDebugDisableTelemetryEventIngest"] = true,
            ["FFlagDebugDisableTelemetryPoint"] = true,
            ["FFlagDebugDisableTelemetryV2Counter"] = true,
            ["FFlagDebugDisableTelemetryV2Event"] = true,
            ["FFlagDebugDisableTelemetryV2Stat"] = true,
            ["DFIntHttpCurlConnectionCacheSize"] = 5,
            ["DFIntMaxImagesCacheSize"] = 32,
            ["DFIntAnimationLodFadeInDistance"] = 0,
            ["DFIntAnimationLodFadeOutDistance"] = 0,
            ["FFlagDebugDisableVoiceChat"] = true,
            ["DFIntMaxSoundsPerFrame"] = 0,
            ["FFlagDebugSimPhysicsSingleStepping"] = true,
            ["DFIntPhysicsAnalyticsHighFrequencyIntervalInSeconds"] = 9999,
            ["DFIntCanHideGuiGroupId"] = 0,
            ["FFlagAdServiceEnabled"] = false,
            ["FIntRenderWindowManagerFrameRateManagerBackgroundFps"] = 1,
        };
    }

    private static Dictionary<string, object> GetPotatoFlags()
    {
        return new Dictionary<string, object>
        {
            ["DFIntTaskSchedulerTargetFps"] = 1,
            ["FFlagDebugGraphicsPreferVulkan"] = false,
            ["FFlagDebugGraphicsPreferD3D11FL10"] = true,
            ["DFIntDebugFRMQualityLevelOverride"] = 1,
            ["FIntRenderLocalLightUpdatesMax"] = 1,
            ["FIntRenderLocalLightUpdatesMin"] = 1,
            ["FIntRenderShadowIntensity"] = 0,
            ["FFlagDebugDisableShadows"] = true,
            ["FFlagDisablePostFx"] = true,
            ["FFlagDebugSkyGray"] = true,
            ["DFFlagDebugRenderForceTechnologyVoxel"] = true,
            ["FFlagFastGPULightCulling3"] = true,
            ["DFIntTextureQualityOverride"] = 0,
            ["DFIntTextureCompositorActiveJobs"] = 0,
            ["DFIntTextureCompositorQueueSize"] = 0,
            ["DFIntCSGLevelOfDetailSwitchingDistance"] = 0,
            ["DFIntCSGLevelOfDetailSwitchingDistanceL12"] = 0,
            ["DFIntCSGLevelOfDetailSwitchingDistanceL23"] = 0,
            ["DFIntCSGLevelOfDetailSwitchingDistanceL34"] = 0,
            ["DFIntMaxMeshDataBufferSizeMB"] = 8,
            ["DFIntMaxParticleSpriteCount"] = 0,
            ["DFIntMaxParticleMeshCount"] = 0,
            ["FIntEmitterMaxSpawnedPerFrame"] = 0,
            ["FFlagEnableParticleEmitterCustomRate"] = false,
            ["FIntTerrainOctreeMaxDepth"] = 1,
            ["FIntRenderGrassDetailStrands"] = 0,
            ["FFlagGrassMovement"] = false,
            ["FIntGrassMovementReducedMotionFactor"] = 0,
            ["FFlagGlobalWindRendering"] = false,
            ["FFlagDebugDisableWater"] = true,
            ["DFIntDebugRestrictGCDistance"] = 50,
            ["DFIntRenderingThrottleDelayInMS"] = 500,
            ["FFlagDebugDisableTelemetryEphemeralCounter"] = true,
            ["FFlagDebugDisableTelemetryEphemeralStat"] = true,
            ["FFlagDebugDisableTelemetryEventIngest"] = true,
            ["FFlagDebugDisableTelemetryPoint"] = true,
            ["FFlagDebugDisableTelemetryV2Counter"] = true,
            ["FFlagDebugDisableTelemetryV2Event"] = true,
            ["FFlagDebugDisableTelemetryV2Stat"] = true,
            ["DFIntHttpCurlConnectionCacheSize"] = 5,
            ["DFIntMaxImagesCacheSize"] = 32,
            ["DFIntAnimationLodFadeInDistance"] = 0,
            ["DFIntAnimationLodFadeOutDistance"] = 0,
            ["FFlagDebugDisableVoiceChat"] = true,
            ["DFIntMaxSoundsPerFrame"] = 0,
            ["FFlagDebugSimPhysicsSingleStepping"] = true,
            ["DFIntPhysicsAnalyticsHighFrequencyIntervalInSeconds"] = 9999,
            ["DFIntCanHideGuiGroupId"] = 0,
            ["FFlagAdServiceEnabled"] = false,
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

        var existing = new JObject();
        if (File.Exists(settingsFile))
        {
            try { existing = JObject.Parse(File.ReadAllText(settingsFile)); }
            catch { }
        }

        foreach (var kvp in flags)
            existing[kvp.Key] = JToken.FromObject(kvp.Value);

        File.WriteAllText(settingsFile, existing.ToString(Formatting.Indented));
    }

    public static void ResetFFlags(string robloxPath)
    {
        string settingsFile = Path.Combine(robloxPath, "ClientSettings", "ClientAppSettings.json");
        if (File.Exists(settingsFile))
            File.Delete(settingsFile);
    }
}
