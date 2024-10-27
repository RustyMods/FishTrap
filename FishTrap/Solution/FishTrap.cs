using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FishTrap.Solution;

public class FishTrap : MonoBehaviour
{
    private static EffectList? vfx_water_surface;
    private static readonly Dictionary<string, BaitData> m_baitConversions = new();
    private static readonly List<FishTrap> m_instances = new();
    
    public List<GameObject> m_fishVisuals = new();
    public ZNetView m_nview = null!;
    public Piece m_piece = null!;
    public Container m_container = null!;
    private Heightmap.Biome m_currentBiome;
    private GameObject[]? m_waterEffects_instances;

    private void Awake()
    {
        foreach (Transform child in transform.Find("fishes")) m_fishVisuals.Add(child.gameObject);
        m_nview = GetComponent<ZNetView>();
        m_piece = GetComponent<Piece>();
        m_container = GetComponent<Container>();
        m_currentBiome = GetCurrentBiome();
        if (!m_nview.IsValid()) return;
        
        InvokeRepeating(nameof(UpdateTrap), 0.0f, FishTrapPlugin._rateOfProduction.Value);
        m_instances.Add(this);
    }
    private void Update() => UpdateContinuousEffects();
    private void OnDestroy() => m_instances.Remove(this);

    private void UpdateTrap()
    {
        UpdateVisual();
        if (!IsInWater() || !m_container.GetInventory().HaveEmptySlot() || FishTrapPlugin._maxFish.Value <= GetFishCount()) return;
        if (!GetConversion(m_container.GetInventory().GetAllItems(), out ItemDrop.ItemData? bait, out BaitData? conversion)) return;
        if (bait is null || conversion is null) return;
        if (Random.value > FishTrapPlugin._chanceForFish.Value) return;
        m_container.GetInventory().RemoveOneItem(bait);
        if (!m_container.GetInventory().AddItem(conversion.GetFish())) return;
        if (Random.value > FishTrapPlugin._chanceForExtra.Value || FishTrapPlugin._extraDrop.Value is FishTrapPlugin.Toggle.Off) return;
        var drop = conversion.GetExtraDrop();
        if (drop is not null) m_container.GetInventory().AddItem(drop);
    }

    public static void UpdateProductionRate()
    {
        foreach (FishTrap? instance in m_instances)
        {
            instance.CancelInvoke(nameof(UpdateTrap));
            instance.InvokeRepeating(nameof(UpdateTrap), 0.0f, FishTrapPlugin._rateOfProduction.Value);
        }
    }

    private void UpdateContinuousEffects()
    {
        if (!ZoneSystem.instance || vfx_water_surface is null) return;
        if (!vfx_water_surface.HasEffects()) return;
        if (IsInWater())
        {
            Transform transform1 = transform;
            m_waterEffects_instances ??= vfx_water_surface.Create(transform1.position with {y = ZoneSystem.instance.m_waterLevel}, Quaternion.identity, transform1);
        }
        else
        {
            if (m_waterEffects_instances is null) return;
            foreach (GameObject? effect in m_waterEffects_instances)
            {
                if (!effect) continue;
                if (effect.TryGetComponent(out TimedDestruction component)) component.Trigger();
                else Destroy(effect);
            }
            m_waterEffects_instances = null;
        }
    }

    private bool IsInWater() => transform.position.y <= ZoneSystem.instance.m_waterLevel;

    private bool GetConversion(List<ItemDrop.ItemData> items, out ItemDrop.ItemData? bait, out BaitData? data)
    {
        bait = null;
        data = null;
        foreach (ItemDrop.ItemData item in items)
        {
            if (!m_baitConversions.TryGetValue(item.m_shared.m_name, out BaitData conversion)) continue;
            if (FishTrapPlugin._useBait.Value is FishTrapPlugin.Toggle.Off && conversion.m_isBait) continue;
            if (FishTrapPlugin._requireBiome.Value is FishTrapPlugin.Toggle.On && !conversion.m_biome.HasFlagFast(m_currentBiome)) continue;
            bait = item;
            data = conversion;
            break;
        }

        return bait != null && data != null;
    }

    private int GetFishCount()
    {
        List<ItemDrop.ItemData> items = m_container.GetInventory().GetAllItems();
        int count = 0;
        foreach (ItemDrop.ItemData? item in items)
        {
            if (item.m_shared.m_itemType is not ItemDrop.ItemData.ItemType.Fish) continue;
            count += item.m_stack;
        }

        return count;
    }

    private void UpdateVisual()
    {
        ClearVisuals();
        List<ItemDrop.ItemData> data = m_container.GetInventory().GetAllItems()
            .Where(item => item.m_shared.m_itemType is ItemDrop.ItemData.ItemType.Fish)
            .SelectMany(fish => Enumerable.Range(0, fish.m_stack).Select(_ => fish))
            .ToList();
        
        for (int index = 0; index < m_fishVisuals.Count; index++)
        {
            GameObject visual = m_fishVisuals[index];
            if (!visual.TryGetComponent(out MeshFilter meshFilter)) continue;
            if (!visual.TryGetComponent(out MeshRenderer meshRenderer)) continue;
            try
            {
                GameObject fish = data[index].m_dropPrefab;
                SetScale(data[index].m_quality, ref visual);
                MeshFilter? fishFilter = fish.GetComponentInChildren<MeshFilter>(true);
                MeshRenderer? fishRenderer = fish.GetComponentInChildren<MeshRenderer>(true);
                if (fishFilter is null || fishRenderer is null) continue;
                meshFilter.mesh = fishFilter.mesh;
                meshFilter.sharedMesh = fishFilter.sharedMesh;
                meshRenderer.material = fishRenderer.material;
                meshRenderer.sharedMaterial = fishRenderer.sharedMaterial;
            }
            catch
            {
                break;
            }
        }
    }

    private void SetScale(int quality, ref GameObject visual)
    {
        float scaleFactor = Mathf.Lerp(1f, 2f, Mathf.InverseLerp(1, 4, quality));
        visual.transform.localScale = new Vector3(scaleFactor, scaleFactor, scaleFactor);
    }

    private void ClearVisuals()
    {
        foreach (GameObject visual in m_fishVisuals)
        {
            if (!visual.TryGetComponent(out MeshFilter filter)) continue;
            filter.mesh = null;
            filter.sharedMesh = null;
        }
    }

    private Heightmap.Biome GetCurrentBiome() => Heightmap.FindBiome(transform.position);

    private string GetHoverName() => m_piece.m_name;

    private string GetHoverText()
    {
        StringBuilder stringBuilder = new StringBuilder();
        stringBuilder.Append(GetHoverName());
        stringBuilder.Append($" ({GetFishCount()}/{FishTrapPlugin._maxFish.Value})");
        if (!PrivateArea.CheckAccess(transform.position)) stringBuilder.Append("\n$piece_noaccess");
        else
        {
            stringBuilder.Append("\n[<color=yellow><b>$KEY_Use</b></color>] $piece_container_open");
            stringBuilder.Append(" $msg_stackall_hover");
            if (!IsInWater()) stringBuilder.Append("\n<color=red>$msg_notinwater</color>");
        }

        return Localization.instance.Localize(stringBuilder.ToString());
    }

    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverName))]
    private static class Container_GetHoverName_Patch
    {
        private static void Postfix(Container __instance, ref string __result)
        {
            if (!__instance.TryGetComponent(out FishTrap component)) return;
            __result = component.GetHoverName();
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
    private static class Container_GetHoverText_Patch
    {
        private static void Postfix(Container __instance, ref string __result)
        {
            if (!__instance.TryGetComponent(out FishTrap component)) return;
            __result = component.GetHoverText();
        }
    }
    
    
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
    private static class ObjectDB_Awake_Patch
    {
        private static void Postfix(ObjectDB __instance)
        {
            if (!__instance || !ZNetScene.instance) return;
            foreach (ItemDrop fish in __instance.GetAllItems(ItemDrop.ItemData.ItemType.Fish, ""))
            {
                if (!fish.TryGetComponent(out Fish component)) continue;
                foreach (Fish.BaitSetting bait in component.m_baits)
                {
                    if (m_baitConversions.TryGetValue(bait.m_bait.m_itemData.m_shared.m_name, out BaitData data))
                    {
                        data.Add(fish);
                        foreach(var drop in component.m_extraDrops.m_drops) data.AddExtra(drop.m_item);
                    }
                    else
                    {
                        var baitData = new BaitData(fish, true);
                        foreach (var drop in component.m_extraDrops.m_drops) baitData.AddExtra(drop.m_item);
                        m_baitConversions[bait.m_bait.m_itemData.m_shared.m_name] = baitData;
                    }
                }
            }

            BaitData neckTail = new("$item_necktail");
            neckTail.Add("Fish1");
            BaitData mistlandBait = new("$item_fishingbait_mistlands");
            mistlandBait.Add("Fish9");
        }
    }
    

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
    private static class ZNetScene_Awake_Patch
    {
        private static void Postfix(ZNetScene __instance)
        {
            if (!__instance) return;
            GameObject player = __instance.GetPrefab("Player");
            if (!player || !player.TryGetComponent(out Player component)) return;
            vfx_water_surface = component.m_waterEffects;
        }
    }

    private class BaitData
    {
        public readonly Heightmap.Biome m_biome;
        private readonly List<ItemDrop.ItemData> m_fishes = new();
        private readonly List<ItemDrop.ItemData> m_extra = new();
        public readonly bool m_isBait;

        public BaitData(ItemDrop fish, bool isBait = false)
        {
            Add(fish);
            m_biome = GetBiome(fish);
            m_isBait = isBait;
        }

        public BaitData(string baitName)
        {
            m_biome = Heightmap.Biome.All;
            m_baitConversions[baitName] = this;
        }

        public void Add(string prefabName)
        {
            var prefab = ObjectDB.instance.GetItemPrefab(prefabName);
            if (!prefab || !prefab.TryGetComponent(out ItemDrop component)) return;
            Add(component);
        }

        public void Add(ItemDrop fish)
        {
            if (m_fishes.Contains(fish.m_itemData)) return;
            ItemDrop.ItemData data = fish.m_itemData;
            data.m_dropPrefab = fish.gameObject;
            m_fishes.Add(data);
        }

        public void AddExtra(GameObject prefab)
        {
            if (!prefab.TryGetComponent(out ItemDrop component)) return;
            var data = component.m_itemData.Clone();
            data.m_dropPrefab = prefab;
            m_extra.Add(data);
        }

        public ItemDrop.ItemData GetFish()
        {
            ItemDrop.ItemData item = m_fishes[Random.Range(0, m_fishes.Count)].Clone();
    
            int quality = 1;
            for (int index = 1; index < item.m_shared.m_maxQuality; ++index)
            {
                if (Random.value <= FishTrapPlugin._chanceToLevel.Value)
                {
                    quality = index + 1;
                }
                else
                {
                    break;
                }
            }

            item.m_quality = quality;

            return item;
        }

        public ItemDrop.ItemData? GetExtraDrop()
        {
            if (m_extra.Count <= 0) return null;
            return m_extra[Random.Range(0, m_extra.Count)];
        }

        private Heightmap.Biome GetBiome(ItemDrop fish)
        {
            return fish.name switch
            {
                "Fish1" => Heightmap.Biome.Meadows | Heightmap.Biome.BlackForest,
                "Fish2" => Heightmap.Biome.Meadows | Heightmap.Biome.BlackForest | Heightmap.Biome.Swamp,
                "Fish3" => Heightmap.Biome.Ocean,
                "Fish4_cave" => Heightmap.Biome.Mountain,
                "Fish5" => Heightmap.Biome.BlackForest,
                "Fish6" => Heightmap.Biome.Swamp,
                "Fish7" => Heightmap.Biome.Plains,
                "Fish8" => Heightmap.Biome.Plains | Heightmap.Biome.Ocean,
                "Fish9" => Heightmap.Biome.Mistlands,
                "Fish10" => Heightmap.Biome.DeepNorth,
                "Fish11" => Heightmap.Biome.AshLands,
                "Fish12" => Heightmap.Biome.Mistlands | Heightmap.Biome.AshLands | Heightmap.Biome.DeepNorth | Heightmap.Biome.Ocean,
                _ => Heightmap.Biome.All
            };
        }
    }
}