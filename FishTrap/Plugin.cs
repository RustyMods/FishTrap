using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using LocalizationManager;
using RustyBuildPieces.Managers;
using ServerSync;
using CraftingTable = RustyBuildPieces.Managers.CraftingTable;

namespace FishTrap
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class FishTrapPlugin : BaseUnityPlugin
    {
        internal const string ModName = "FishTrap";
        internal const string ModVersion = "1.0.0";
        internal const string Author = "RustyMods";
        private const string ModGUID = Author + "." + ModName;
        private static readonly string ConfigFileName = ModGUID + ".cfg";
        private static readonly string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
        internal static string ConnectionError = "";
        private readonly Harmony _harmony = new(ModGUID);
        public static readonly ManualLogSource FishTrapLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
        private static readonly ConfigSync ConfigSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };
        public enum Toggle { On = 1, Off = 0 }
        
        private static ConfigEntry<Toggle> _serverConfigLocked = null!;
        public static ConfigEntry<Toggle> _requireBiome = null!;
        public static ConfigEntry<int> _maxFish = null!;
        public static ConfigEntry<Toggle> _extraDrop = null!;
        public static ConfigEntry<float> _rateOfProduction = null!;
        public static ConfigEntry<float> _chanceForExtra = null!;
        public static ConfigEntry<float> _chanceForFish = null!;
        public static ConfigEntry<Toggle> _useBait = null!;
        public static ConfigEntry<float> _chanceToLevel = null!;

        private void InitConfigs()
        {
            _serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
            _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);
            _requireBiome = config("2 - Settings", "Check Biome", Toggle.On, "If on, bait conversion checks biome match");
            _maxFish = config("2 - Settings", "Max Fish", 4, "Set maximum amount of fish in a trap, until it stops producing");
            _extraDrop = config("2 - Settings", "Extra Drops", Toggle.Off, "If on, chance to get extra drop from bait");
            _rateOfProduction = config("2 - Settings", "Production Rate", 1200f, "Set production rate in seconds");
            _rateOfProduction.SettingChanged += (_, _) => Solution.FishTrap.UpdateProductionRate();
            _chanceForExtra = config("2 - Settings", "Extra Drop Chance", 0.1f, new ConfigDescription("Set chance to get extra drops", new AcceptableValueRange<float>(0f, 1f)));
            _chanceForFish = config("2 - Settings", "Chance To Catch", 0.5f, new ConfigDescription("Set chance to convert bait to fish, if fails, still consumes bait", new AcceptableValueRange<float>(0f, 1f)));
            _useBait = config("2 - Settings", "Use Bait", Toggle.Off, "If on, baits works in trap, else use neck tails or mistland bait");
            _chanceToLevel = config("2 - Settings", "Level Up Chance", 0.1f, new ConfigDescription("Set chance to get higher quality fish", new AcceptableValueRange<float>(0f, 1f)));
        }
        public void Awake()
        {
            Localizer.Load(); 
            InitConfigs();
            BuildPiece fishTrap = new("fishtrapbundle", "FishTrap");
            fishTrap.Name.English("Fish Trap");
            fishTrap.Description.English("");
            fishTrap.Crafting.Set(CraftingTable.Workbench);
            fishTrap.Category.Set(BuildPieceCategory.Misc);
            fishTrap.RequiredItems.Add("Wood", 10, true);
            fishTrap.RequiredItems.Add("Tin", 2, true);
            fishTrap.PlaceEffects.Add("vfx_Place_workbench");
            fishTrap.PlaceEffects.Add("sfx_build_hammer_wood");
            fishTrap.DestroyedEffects.Add("vfx_SawDust");
            fishTrap.DestroyedEffects.Add("sfx_wood_destroyed");
            fishTrap.HitEffects.Add("vfx_SawDust");
            fishTrap.Prefab.AddComponent<Solution.FishTrap>();
            MaterialReplacer.RegisterGameObjectForShaderSwap(fishTrap.Prefab, MaterialReplacer.ShaderType.PieceShader);
            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
            SetupWatcher();
        }

        private void OnDestroy() => Config.Save();

        private void SetupWatcher()
        {
            FileSystemWatcher watcher = new(Paths.ConfigPath, ConfigFileName);
            watcher.Changed += ReadConfigValues;
            watcher.Created += ReadConfigValues;
            watcher.Renamed += ReadConfigValues;
            watcher.IncludeSubdirectories = true;
            watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            watcher.EnableRaisingEvents = true;
        }

        private void ReadConfigValues(object sender, FileSystemEventArgs e)
        {
            if (!File.Exists(ConfigFileFullPath)) return;
            try
            {
                FishTrapLogger.LogDebug("ReadConfigValues called");
                Config.Reload();
            }
            catch
            {
                FishTrapLogger.LogError($"There was an issue loading your {ConfigFileName}");
                FishTrapLogger.LogError("Please check your config entries for spelling and format!");
            }
        }
        
        private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
        {
            ConfigDescription extendedDescription =
                new(
                    description.Description +
                    (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"),
                    description.AcceptableValues, description.Tags);
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
            //var configEntry = Config.Bind(group, name, value, description);

            SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true)
        {
            return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
        }

        private class ConfigurationManagerAttributes
        {
            [UsedImplicitly] public int? Order;
            [UsedImplicitly] public bool? Browsable;
            [UsedImplicitly] public string? Category;
            [UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer;
        }
    }

    public static class HeightMapBiomeExtensions
    {
        public static bool HasFlagFast(this Heightmap.Biome value, Heightmap.Biome flag)
        {
            return (value & flag) != 0;
        }
    }
}

