using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using CommonAPI.Systems.ModLocalization;
using HarmonyLib;
using UnityEngine;
using xiaoye97;

namespace SignalNexus;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("me.xiaoye97.plugin.Dyson.LDBTool", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("dsp.common-api.CommonAPI", BepInDependency.DependencyFlags.HardDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "codex.dsp.signal-nexus";
    public const string PluginName = "Signal Nexus";
    public const string PluginVersion = "0.2.15";

    // Stay in the established mod-prototype bands. MoreMegaStructure maintains
    // fixed-size item lookup arrays and cannot safely consume very large IDs.
    private const int PreferredItemId = 9600;
    private const int PreferredRecipeId = 9600;
    // Proliferator Mk.IV rebuilds the vanilla model index without LDBTool's
    // high-ID expansion. It owns 720/721, making 722 the first compatible slot.
    private const int PreferredModelId = 722;
    private const int PreferredMarkerModelId = 723;

    public static int ItemId { get; private set; } = PreferredItemId;
    public static int RecipeId { get; private set; } = PreferredRecipeId;
    public static int ModelId { get; private set; } = PreferredModelId;
    public static int MarkerModelId { get; private set; } = PreferredMarkerModelId;

    internal static ManualLogSource Log;
    internal static ItemProto TrafficMonitor;
    internal static ItemProto HoloBeacon;
    internal static ItemProto TeslaTower;
    internal static ItemProto NexusItem;
    internal static RecipeProto NexusRecipe;
    internal static ModelProto NexusModel;
    internal static ModelProto MarkerModel;

    private Harmony _harmony;

    private void Awake()
    {
        Log = Logger;
        StartupBanner.Print(Logger);
        RegisterStrings();
        LDBTool.PreAddDataAction += AddPrototypes;
        LDBTool.PostAddDataAction += FinishPrototypes;

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll(typeof(EntityComponentPatch));
        _harmony.PatchAll(typeof(NexusRenderSplitPatch));
        _harmony.PatchAll(typeof(NexusInspectPatch));
        _harmony.PatchAll(typeof(MonitorWindowPatch));
        _harmony.PatchAll(typeof(MonitorSpeakerPatch));
        _harmony.PatchAll(typeof(MonitorIpPatch));
        _harmony.PatchAll(typeof(MarkerIpPatch));
        _harmony.PatchAll(typeof(MarkerTipGuardPatch));
        _harmony.PatchAll(typeof(BuildingCopyPatch));
        _harmony.PatchAll(typeof(BuildingPastePatch));
        _harmony.PatchAll(typeof(AddonPrebuildContentPatch));
        _harmony.PatchAll(typeof(BuildingApplyPatch));
        Logger.LogInfo("Signal Nexus prototype and combined monitor/beacon UI patches registered.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        LDBTool.PreAddDataAction -= AddPrototypes;
        LDBTool.PostAddDataAction -= FinishPrototypes;
    }

    private static void RegisterStrings()
    {
        LocalizationModule.RegisterTranslation("SignalNexusName", "Signal Nexus", "信号枢纽", "Signal Nexus");
        LocalizationModule.RegisterTranslation(
            "SignalNexusDesc",
            "A belt traffic monitor, Holo Beacon and Tesla Tower integrated into one compact facility. Its shared IP drives the beacon colour from the monitor signal, while the built-in power node connects and powers nearby facilities.",
            "将传送带流速监测器、全息信标与电力感应塔集成为一体。共享 IP 会使用监测信号控制信标颜色，内置电力节点还能连接并供电给附近设施。",
            "A belt traffic monitor, Holo Beacon and Tesla Tower integrated into one compact facility."
        );
        LocalizationModule.RegisterTranslation("SignalNexusRecipeDesc", "Combine three vanilla utility buildings into one Signal Nexus.", "将三种原版功能建筑组合成一个信号枢纽。", "Combine three vanilla utility buildings into one Signal Nexus.");
        LocalizationModule.RegisterTranslation("SignalNexusHoloSettings", "Holo settings", "全息设置", "Holo settings");
    }

    private static void AddPrototypes()
    {
        ItemId = FindFreeId(PreferredItemId, LDB.items.dataArray.Where(proto => proto != null).Select(proto => proto.ID));
        RecipeId = FindFreeId(PreferredRecipeId, LDB.recipes.dataArray.Where(proto => proto != null).Select(proto => proto.ID));
        ModelId = FindFreeId(PreferredModelId, LDB.models.dataArray.Where(proto => proto != null).Select(proto => proto.ID));
        MarkerModelId = FindFreeId(PreferredMarkerModelId, LDB.models.dataArray.Where(proto => proto != null).Select(proto => proto.ID).Append(ModelId));

        TrafficMonitor = FindItem("流速器", "Traffic Monitor");
        HoloBeacon = FindItem("激光灯塔", "Holo Beacon");
        TeslaTower = FindItem("电力感应塔", "Tesla Tower");
        if (TrafficMonitor == null || HoloBeacon == null || TeslaTower == null)
        {
            Log.LogError("Signal Nexus could not locate all three vanilla source prototypes.");
            return;
        }

        // Use temporary free slots while prototypes are being collected. The
        // final slots are selected in FinishPrototypes, after every mod has had
        // a chance to register, and vanilla Traffic Monitor is never moved.
        var nexusGridIndex = FindFreeCraftingGridIndex(TrafficMonitor, ItemId);
        var nexusBuildIndex = FindFreeBuildIndex(TrafficMonitor.BuildIndex / 100, ItemId);

        var sourceModel = LDB.models.Select(TrafficMonitor.ModelIndex);
        var markerSourceModel = LDB.models.Select(HoloBeacon.ModelIndex);
        if (sourceModel == null || markerSourceModel == null)
        {
            Log.LogError("Signal Nexus could not locate its source model prototypes.");
            return;
        }

        NexusModel = ShallowClone(sourceModel);
        NexusModel.ID = ModelId;
        NexusModel.Name = "SignalNexusModel";
        NexusModel.prefabDesc = ShallowClone(sourceModel.prefabDesc);
        NexusModel.prefabDesc.modelIndex = ModelId;
        Traverse.Create(NexusModel).Field("prewarmed").SetValue(false);
        LDBTool.PreAddProto(NexusModel);

        // The beacon overlay is rendered as a second GPU instance with its own
        // animation record. Traffic Monitor and Holo Beacon both use
        // AnimData.working_length (flow versus height), so combining their
        // submeshes into one instance makes whichever system updates last win.
        MarkerModel = ShallowClone(markerSourceModel);
        MarkerModel.ID = MarkerModelId;
        MarkerModel.Name = "SignalNexusMarkerModel";
        MarkerModel.prefabDesc = ShallowClone(markerSourceModel.prefabDesc);
        MarkerModel.prefabDesc.modelIndex = MarkerModelId;
        Traverse.Create(MarkerModel).Field("prewarmed").SetValue(false);
        LDBTool.PreAddProto(MarkerModel);

        NexusItem = ShallowClone(TrafficMonitor);
        NexusItem.ID = ItemId;
        NexusItem.Name = "SignalNexusName";
        NexusItem.name = "Signal Nexus";
        NexusItem.Description = "SignalNexusDesc";
        NexusItem.description = "SignalNexusDesc";
        NexusItem.ModelIndex = ModelId;
        NexusItem.ModelCount = 1;
        NexusItem.BuildIndex = nexusBuildIndex;
        NexusItem.GridIndex = nexusGridIndex;
        NexusItem.StackSize = Math.Max(TrafficMonitor.StackSize, 50);
        NexusItem.prefabDesc = NexusModel.prefabDesc;
        NexusItem.handcraft = null;
        NexusItem.maincraft = null;
        NexusItem.handcrafts = new List<RecipeProto>();
        NexusItem.recipes = new List<RecipeProto>();
        NexusItem.makes = new List<RecipeProto>();
        LDBTool.PreAddProto(NexusItem);

        NexusRecipe = new RecipeProto
        {
            ID = RecipeId,
            Name = "SignalNexusName",
            name = "Signal Nexus",
            Description = "SignalNexusRecipeDesc",
            description = "SignalNexusRecipeDesc",
            Type = ERecipeType.Assemble,
            Handcraft = true,
            Explicit = false,
            TimeSpend = 120,
            Items = new[] { TrafficMonitor.ID, HoloBeacon.ID, TeslaTower.ID },
            ItemCounts = new[] { 1, 1, 1 },
            Results = new[] { ItemId },
            ResultCounts = new[] { 1 },
            GridIndex = NexusItem.GridIndex,
            preTech = HoloBeacon.preTech ?? TrafficMonitor.preTech
        };
        LDBTool.PreAddProto(NexusRecipe);

        NexusItem.handcraft = NexusRecipe;
        NexusItem.maincraft = NexusRecipe;
        NexusItem.handcrafts.Add(NexusRecipe);
        NexusItem.recipes.Add(NexusRecipe);
        Log.LogInfo($"Registered Signal Nexus item {ItemId}, recipe {RecipeId}, model {ModelId}; temporary grid {NexusItem.GridIndex}, build {NexusItem.BuildIndex}. Traffic Monitor remains untouched.");
    }

    private static int FindFreeId(int preferred, IEnumerable<int> occupiedIds)
    {
        var occupied = new HashSet<int>(occupiedIds);
        const int searchWindow = 300;
        for (var candidate = preferred; candidate < preferred + searchWindow; candidate++)
        {
            if (!occupied.Contains(candidate)) return candidate;
        }

        throw new InvalidOperationException($"Signal Nexus could not find a free prototype ID in {preferred}-{preferred + searchWindow - 1}.");
    }

    private static void FinishPrototypes()
    {
        NexusItem = LDB.items.Select(ItemId);
        NexusRecipe = LDB.recipes.Select(RecipeId);
        NexusModel = LDB.models.Select(ModelId);
        MarkerModel = LDB.models.Select(MarkerModelId);
        if (NexusItem == null || NexusRecipe == null || NexusModel == null || MarkerModel == null || TrafficMonitor == null || HoloBeacon == null || TeslaTower == null)
            return;

        // Signal Nexus deliberately replaces the vanilla Traffic Monitor in both
        // user-facing menus. Move Traffic Monitor to the safe slot Nexus found
        // after every mod registered, then give Nexus the original selector slot
        // and Logistics hotbar key. This explicit swap avoids the old 2106 Solar
        // Panel collision and keeps the agreed F9 placement deterministic.
        var trafficGridIndex = TrafficMonitor.GridIndex;
        var trafficBuildIndex = TrafficMonitor.BuildIndex;
        var relocatedTrafficGridIndex = FindFreeCraftingGridIndex(TrafficMonitor, ItemId);

        TrafficMonitor.GridIndex = relocatedTrafficGridIndex;
        foreach (var recipe in LDB.recipes.dataArray.Where(recipe => recipe?.Results != null && recipe.Results.Contains(TrafficMonitor.ID)))
            recipe.GridIndex = relocatedTrafficGridIndex;
        TrafficMonitor.BuildIndex = 0;

        NexusItem.GridIndex = trafficGridIndex;
        NexusRecipe.GridIndex = trafficGridIndex;
        NexusItem.BuildIndex = trafficBuildIndex;
        LDBTool.SetBuildBar(trafficBuildIndex / 100, trafficBuildIndex % 100, ItemId);

        var trafficDesc = TrafficMonitor.prefabDesc;
        var markerDesc = HoloBeacon.prefabDesc;
        var powerDesc = TeslaTower.prefabDesc;
        var desc = ShallowClone(trafficDesc);

        desc.modelIndex = ModelId;
        // A combined monitor/beacon/power node is a single-place facility.
        // Avoid the add-on drag-preview array used by BuildTool_Addon; extended
        // drag ranges from BuildToolOpt can overflow that fixed vanilla array.
        desc.dragBuild = false;
        desc.isMonitor = true;
        desc.isSpeaker = true;
        desc.isMarker = true;
        desc.markerPointDefaultHeight = markerDesc.markerPointDefaultHeight;
        desc.markerPointDefaultRadius = markerDesc.markerPointDefaultRadius;
        desc.isPowerNode = true;
        desc.powerConnectDistance = powerDesc.powerConnectDistance;
        desc.powerCoverRadius = powerDesc.powerCoverRadius;
        desc.powerPoint = new Vector3(0f, Math.Max(1.7f, trafficDesc.roughHeight), 0f);
        desc.isPowerConsumer = true;
        desc.workEnergyPerTick = Math.Max(1L, trafficDesc.workEnergyPerTick + markerDesc.workEnergyPerTick);
        desc.idleEnergyPerTick = Math.Max(1L, trafficDesc.idleEnergyPerTick + markerDesc.idleEnergyPerTick);

        BuildMarkerOverlayModel(MarkerModel, markerDesc, trafficDesc);
        NexusModel.prefabDesc = desc;
        NexusModel.meshBounds = desc.mesh != null ? desc.mesh.bounds : NexusModel.meshBounds;
        NexusItem.prefabDesc = desc;
        NexusItem.ModelIndex = ModelId;

        var icon = CreateIcon();
        Traverse.Create(NexusItem).Field("_iconSprite").SetValue(icon);
        Traverse.Create(NexusRecipe).Field("_iconSprite").SetValue(icon);
        NexusRecipe.IconPath = string.Empty;

        Log.LogInfo($"Signal Nexus ready at Traffic Monitor grid {trafficGridIndex}, build {trafficBuildIndex}: monitor + marker + {desc.powerConnectDistance:0.#}m power connection / {desc.powerCoverRadius:0.#}m coverage. Traffic Monitor moved to grid {relocatedTrafficGridIndex} and removed from the occupied F9 hotbar slot.");
    }

    private static ItemProto FindItem(params string[] names)
    {
        return LDB.items.dataArray.FirstOrDefault(item => item != null && names.Any(name =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.name, name, StringComparison.OrdinalIgnoreCase)));
    }

    private static int FindFreeCraftingGridIndex(ItemProto template, int excludedItemId)
    {
        var occupied = new HashSet<int>(LDB.items.dataArray
            .Where(item => item != null && item.ID != excludedItemId && item.BuildIndex > 0)
            .Select(item => item.GridIndex));
        var baseRow = Math.Max(1, Math.Min(8, (template.GridIndex - 2000) / 100));
        var baseColumn = Math.Max(1, Math.Min(14, template.GridIndex % 100));
        var candidates = Enumerable.Range(1, 8)
            .SelectMany(row => Enumerable.Range(1, 14).Select(column => new { row, column }))
            .OrderBy(slot => Math.Abs(slot.row - baseRow) + Math.Abs(slot.column - baseColumn))
            .ThenBy(slot => slot.row)
            .ThenBy(slot => slot.column);
        foreach (var slot in candidates)
        {
            var candidate = 2000 + slot.row * 100 + slot.column;
            if (!occupied.Contains(candidate)) return candidate;
        }
        return 2814;
    }

    private static int NextBuildIndex()
    {
        return LDB.items.dataArray.Where(x => x != null).Select(x => x.BuildIndex).DefaultIfEmpty(700).Max() + 1;
    }

    private static int FindFreeBuildIndex(int preferredCategory, int excludedItemId = 0)
    {
        var occupied = new HashSet<int>(LDB.items.dataArray.Where(item => item != null && item.ID != excludedItemId && item.BuildIndex > 0).Select(item => item.BuildIndex));
        if (preferredCategory >= 1 && preferredCategory <= 15)
        {
            for (var slot = 10; slot >= 1; slot--)
            {
                var candidate = preferredCategory * 100 + slot;
                if (!occupied.Contains(candidate)) return candidate;
            }
        }

        for (var category = 12; category >= 1; category--)
        for (var slot = 10; slot >= 1; slot--)
        {
            var candidate = category * 100 + slot;
            if (!occupied.Contains(candidate)) return candidate;
        }

        return NextBuildIndex();
    }

    internal static T ShallowClone<T>(T source) where T : class
    {
        if (source == null) return null;
        return (T)AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(source, null);
    }

    private static void BuildMarkerOverlayModel(ModelProto targetModel, PrefabDesc marker, PrefabDesc traffic)
    {
        try
        {
            if (marker.lodMeshes == null) return;
            var target = ShallowClone(marker);
            target.modelIndex = MarkerModelId;
            var lodCount = marker.lodMeshes.Length;
            target.lodMeshes = new Mesh[lodCount];
            target.lodMaterials = new Material[lodCount][];
            target.lodBlueprintMaterials = new Material[lodCount][];
            target.lodSubmeshIgnores = new bool[lodCount][];

            var markerTransform = Matrix4x4.TRS(new Vector3(0f, Math.Max(0.75f, traffic.roughHeight * 0.55f), 0f), Quaternion.identity, Vector3.one * 0.34f);
            for (var lod = 0; lod < lodCount; lod++)
            {
                var markerMesh = marker.lodMeshes[Math.Min(lod, marker.lodMeshes.Length - 1)];
                if (markerMesh == null) continue;

                var combines = new List<CombineInstance>();
                AddSubmeshes(combines, markerMesh, markerTransform);
                var mesh = new Mesh { name = $"Signal Nexus Marker Overlay LOD {lod}" };
                mesh.CombineMeshes(combines.ToArray(), false, true);
                mesh.RecalculateBounds();
                target.lodMeshes[lod] = mesh;

                var markerMaterials = GetLodMaterials(marker.lodMaterials, lod);
                target.lodMaterials[lod] = markerMaterials;
                var markerBlueprint = GetLodMaterials(marker.lodBlueprintMaterials, lod, markerMaterials);
                target.lodBlueprintMaterials[lod] = markerBlueprint;
                target.lodSubmeshIgnores[lod] = new bool[target.lodMaterials[lod].Length];
            }

            target.mesh = target.lodMeshes.FirstOrDefault(x => x != null) ?? marker.mesh;
            target.meshes = target.mesh != null ? new[] { target.mesh } : marker.meshes;
            target.materials = target.lodMaterials.FirstOrDefault(x => x != null) ?? marker.materials;
            target.roughHeight = Math.Max(traffic.roughHeight, markerTransform.MultiplyPoint3x4(Vector3.up * marker.roughHeight).y);
            target.cullingHeight = Math.Max(traffic.cullingHeight, target.roughHeight);
            targetModel.prefabDesc = target;
            targetModel.meshBounds = target.mesh != null ? target.mesh.bounds : targetModel.meshBounds;
            Log.LogInfo($"Built split Holo Beacon overlay model {MarkerModelId}; Traffic Monitor and beacon now have independent animation records.");
        }
        catch (Exception error)
        {
            Log.LogWarning($"Holo Beacon overlay model creation failed: {error.Message}");
        }
    }

    private static void AddSubmeshes(List<CombineInstance> output, Mesh mesh, Matrix4x4 transform)
    {
        for (var i = 0; i < mesh.subMeshCount; i++)
            output.Add(new CombineInstance { mesh = mesh, subMeshIndex = i, transform = transform });
    }

    private static Material[] GetLodMaterials(Material[][] source, int lod, Material[] fallback = null)
    {
        if (source == null || source.Length == 0) return fallback ?? Array.Empty<Material>();
        return source[Math.Min(lod, source.Length - 1)] ?? fallback ?? Array.Empty<Material>();
    }

    private static Sprite CreateIcon()
    {
        const int size = 80;
        var texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
        {
            name = "Signal Nexus Icon",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = Enumerable.Repeat(new Color(0f, 0f, 0f, 0f), size * size).ToArray();
        void Pixel(int x, int y, Color color)
        {
            if ((uint)x < size && (uint)y < size) pixels[y * size + x] = color;
        }
        void Rect(int x0, int y0, int x1, int y1, Color color)
        {
            for (var y = y0; y <= y1; y++) for (var x = x0; x <= x1; x++) Pixel(x, y, color);
        }
        void Disc(int cx, int cy, int radius, Color color)
        {
            for (var y = -radius; y <= radius; y++)
            for (var x = -radius; x <= radius; x++)
                if (x * x + y * y <= radius * radius) Pixel(cx + x, cy + y, color);
        }

        var dark = new Color(0.035f, 0.12f, 0.15f, 1f);
        var cyan = new Color(0.15f, 0.9f, 1f, 1f);
        var green = new Color(0.22f, 1f, 0.42f, 1f);
        var white = new Color(0.9f, 1f, 1f, 1f);
        Rect(13, 10, 66, 35, dark);
        Rect(10, 15, 69, 30, dark);
        Rect(17, 14, 62, 31, cyan);
        Rect(21, 18, 58, 27, new Color(0.02f, 0.2f, 0.24f, 1f));
        for (var x = 24; x < 57; x += 7) Rect(x, 19, x + 2, 26, green);
        Rect(35, 35, 44, 51, white);
        Disc(40, 57, 12, new Color(0.1f, 0.55f, 0.38f, 1f));
        Disc(40, 57, 8, green);
        Disc(40, 57, 3, white);
        for (var r = 16; r <= 27; r += 6)
        for (var a = 0; a < 360; a += 8)
        {
            var rad = a * Mathf.Deg2Rad;
            Pixel(40 + Mathf.RoundToInt(Mathf.Cos(rad) * r), 57 + Mathf.RoundToInt(Mathf.Sin(rad) * r * 0.45f), new Color(0.15f, 1f, 0.45f, 0.72f));
        }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
