using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SignalNexus;

internal static class NexusLookup
{
    internal static bool TryGetEntity(PlanetFactory factory, int entityId, out EntityData entity)
    {
        entity = default;
        if (factory == null || entityId <= 0 || entityId >= factory.entityCursor) return false;
        entity = factory.entityPool[entityId];
        return entity.id == entityId && entity.protoId == Plugin.ItemId;
    }

    internal static bool TryGetFromMonitor(PlanetFactory factory, int monitorId, out EntityData entity, out MonitorComponent monitor, out MarkerComponent marker)
    {
        entity = default;
        monitor = default;
        marker = null;
        if (factory?.cargoTraffic?.monitorPool == null || monitorId <= 0 || monitorId >= factory.cargoTraffic.monitorCursor) return false;
        monitor = factory.cargoTraffic.monitorPool[monitorId];
        if (monitor.id != monitorId || !TryGetEntity(factory, monitor.entityId, out entity) || entity.markerId <= 0) return false;
        marker = factory.digitalSystem.markers[entity.markerId];
        return marker != null;
    }

    internal static void SyncMarkerFromMonitor(PlanetFactory factory, EntityData entity)
    {
        if (entity.monitorId <= 0 || entity.markerId <= 0) return;
        ref var monitor = ref factory.cargoTraffic.monitorPool[entity.monitorId];
        var marker = factory.digitalSystem.markers[entity.markerId];
        if (monitor.id != entity.monitorId || marker == null) return;
        marker.SetDigitalSignalId(monitor.digitalSignalId);
    }

    internal static void SyncMonitorFromMarker(PlanetFactory factory, MarkerComponent marker)
    {
        if (factory == null || marker == null || !TryGetEntity(factory, marker.entityId, out var entity) || entity.monitorId <= 0) return;
        ref var monitor = ref factory.cargoTraffic.monitorPool[entity.monitorId];
        if (monitor.id == entity.monitorId) monitor.SetDigitalSignalId(marker.digitalSignalId);
    }
}

[HarmonyPatch(typeof(UIGame), "OnPlayerInspecteeChange")]
internal static class NexusInspectPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(UIGame __instance, EObjectType objType, int objId)
    {
        var factory = GameMain.mainPlayer?.factory;
        if (objType != EObjectType.None || !NexusLookup.TryGetEntity(factory, objId, out var entity) || entity.monitorId <= 0)
            return true;

        // Vanilla processes every component on a multi-role entity in sequence:
        // marker, power node, then monitor. Closing the temporary power-node
        // window calls InspectNothing(), which recursively cancels the original
        // click before the monitor window can remain open. Route a Nexus straight
        // to its combined monitor/beacon window instead.
        try
        {
            // Clear these IDs before ShutAllFunctionWindow so a stale Nexus power
            // panel cannot call InspectNothing while it is being closed.
            __instance.nodeWindow.nodeId = 0;
            __instance.markerWindow.markerId = 0;
            __instance.ShutAllFunctionWindow();
            __instance.ShutPlayerInventory();

            __instance.monitorWindow.monitorId = entity.monitorId;
            var ui = Traverse.Create(__instance);
            ui.Field("inspectMarkerId").SetValue(0);
            ui.Field("inspectNodeId").SetValue(0);
            ui.Field("inspectMonitorId").SetValue(entity.monitorId);
            __instance.OpenMonitorWindow();
            Plugin.Log.LogInfo($"Opened Signal Nexus {objId} directly in the combined monitor/beacon window.");
        }
        catch (Exception error)
        {
            Plugin.Log.LogError($"Signal Nexus direct window routing failed: {error}");
            return true;
        }

        return false;
    }
}

[HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.CreateEntityLogicComponents))]
internal static class EntityComponentPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlanetFactory __instance, int entityId)
    {
        if (!NexusLookup.TryGetEntity(__instance, entityId, out var entity)) return;
        if (entity.monitorId <= 0 || entity.markerId <= 0 || entity.powerNodeId <= 0)
        {
            Plugin.Log.LogError($"Signal Nexus entity {entityId} is missing components: monitor={entity.monitorId}, marker={entity.markerId}, node={entity.powerNodeId}.");
            return;
        }
        NexusLookup.SyncMarkerFromMonitor(__instance, entity);
        NexusRenderSplitPatch.EnsureOverlay(__instance, entityId);
    }
}

[HarmonyPatch(typeof(DigitalSystem), nameof(DigitalSystem.GameTick))]
internal static class NexusRenderSplitPatch
{
    private sealed class Overlay
    {
        internal int EntityId;
        internal int ShadowAnimId;
        internal int ModelInstId;
    }

    private struct Snapshot
    {
        internal Overlay Overlay;
        internal AnimData MonitorAnim;
    }

    private static readonly System.Collections.Generic.Dictionary<PlanetFactory, System.Collections.Generic.Dictionary<int, Overlay>> Overlays = new();
    private static long _lastReconcileTick = long.MinValue;

    internal static void EnsureOverlay(PlanetFactory factory, int entityId)
    {
        if (!NexusLookup.TryGetEntity(factory, entityId, out var entity)) return;
        var manager = factory.planet?.factoryModel?.gpuiManager;
        if (manager == null || manager.GetObjectRenderer(Plugin.MarkerModelId) == null) return;

        if (!Overlays.TryGetValue(factory, out var planetOverlays))
        {
            planetOverlays = new System.Collections.Generic.Dictionary<int, Overlay>();
            Overlays.Add(factory, planetOverlays);
        }

        if (planetOverlays.TryGetValue(entityId, out var current) && IsOverlayAlive(manager, current)) return;
        if (current != null) RemoveOverlay(factory, current);

        var shadowAnimId = AllocateShadowAnimId(factory, planetOverlays);
        if (shadowAnimId <= 0) return;
        var modelInstId = manager.AddModel(Plugin.MarkerModelId, shadowAnimId, entity.pos, entity.rot, true);
        if (modelInstId <= 0)
        {
            Plugin.Log.LogWarning($"Could not create the split Holo Beacon renderer for Signal Nexus {entityId}.");
            return;
        }

        planetOverlays[entityId] = new Overlay
        {
            EntityId = entityId,
            ShadowAnimId = shadowAnimId,
            ModelInstId = modelInstId
        };
    }

    [HarmonyPrefix]
    private static void Prefix(DigitalSystem __instance, out System.Collections.Generic.List<Snapshot> __state)
    {
        var factory = __instance?.factory;
        __state = new System.Collections.Generic.List<Snapshot>();
        if (factory == null) return;

        var tick = GameMain.gameTick;
        if (!Overlays.ContainsKey(factory) || tick - _lastReconcileTick >= 60)
        {
            Reconcile(factory);
            _lastReconcileTick = tick;
        }

        if (!Overlays.TryGetValue(factory, out var planetOverlays)) return;
        foreach (var overlay in planetOverlays.Values)
        {
            if (overlay.EntityId <= 0 || overlay.EntityId >= factory.entityAnimPool.Length ||
                overlay.ShadowAnimId <= 0 || overlay.ShadowAnimId >= factory.entityAnimPool.Length)
                continue;
            __state.Add(new Snapshot
            {
                Overlay = overlay,
                MonitorAnim = factory.entityAnimPool[overlay.EntityId]
            });
        }
    }

    [HarmonyPostfix]
    private static void Postfix(DigitalSystem __instance, System.Collections.Generic.List<Snapshot> __state)
    {
        var factory = __instance?.factory;
        if (factory == null || __state == null) return;
        foreach (var snapshot in __state)
        {
            var overlay = snapshot.Overlay;
            if (overlay.EntityId <= 0 || overlay.EntityId >= factory.entityAnimPool.Length ||
                overlay.ShadowAnimId <= 0 || overlay.ShadowAnimId >= factory.entityAnimPool.Length)
                continue;

            // DigitalSystem has just written beacon colour, radius and height to
            // the Nexus entity's AnimData. Give that result to the overlay, then
            // restore the monitor result for the physical flow display.
            factory.entityAnimPool[overlay.ShadowAnimId] = factory.entityAnimPool[overlay.EntityId];
            factory.entityAnimPool[overlay.EntityId] = snapshot.MonitorAnim;
        }
    }

    private static void Reconcile(PlanetFactory factory)
    {
        if (!Overlays.TryGetValue(factory, out var planetOverlays))
        {
            planetOverlays = new System.Collections.Generic.Dictionary<int, Overlay>();
            Overlays.Add(factory, planetOverlays);
        }

        foreach (var pair in planetOverlays.ToArray())
        {
            if (NexusLookup.TryGetEntity(factory, pair.Key, out _)) continue;
            RemoveOverlay(factory, pair.Value);
            planetOverlays.Remove(pair.Key);
        }

        for (var entityId = 1; entityId < factory.entityCursor; entityId++)
        {
            if (factory.entityPool[entityId].id == entityId && factory.entityPool[entityId].protoId == Plugin.ItemId)
                EnsureOverlay(factory, entityId);
        }
    }

    private static int AllocateShadowAnimId(PlanetFactory factory, System.Collections.Generic.Dictionary<int, Overlay> planetOverlays)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var reserved = new System.Collections.Generic.HashSet<int>(planetOverlays.Values.Select(x => x.ShadowAnimId));
            for (var candidate = factory.entityAnimPool.Length - 1; candidate > factory.entityCursor + 64; candidate--)
            {
                if (!reserved.Contains(candidate) && factory.entityPool[candidate].id == 0)
                    return candidate;
            }

            AccessTools.Method(typeof(PlanetFactory), "SetEntityCapacity")?.Invoke(factory,
                new object[] { Math.Max(factory.entityAnimPool.Length * 2, factory.entityCursor + 512) });
        }

        Plugin.Log.LogError("Could not reserve an animation record for a Signal Nexus Holo Beacon overlay.");
        return 0;
    }

    private static bool IsOverlayAlive(GPUInstancingManager manager, Overlay overlay)
    {
        var renderer = manager.GetObjectRenderer(Plugin.MarkerModelId);
        return renderer != null && overlay.ModelInstId > 0 && overlay.ModelInstId < renderer.instCursor &&
               renderer.instPool[overlay.ModelInstId].objId == (uint)overlay.ShadowAnimId;
    }

    private static void RemoveOverlay(PlanetFactory factory, Overlay overlay)
    {
        var manager = factory?.planet?.factoryModel?.gpuiManager;
        if (manager != null && IsOverlayAlive(manager, overlay))
            manager.RemoveModel(Plugin.MarkerModelId, overlay.ModelInstId, true);
        if (factory?.entityAnimPool != null && overlay.ShadowAnimId > 0 && overlay.ShadowAnimId < factory.entityAnimPool.Length)
            factory.entityAnimPool[overlay.ShadowAnimId] = default;
    }
}

[HarmonyPatch(typeof(UIMonitorWindow))]
internal static class MonitorWindowPatch
{
    private const float HoloSectionHeight = 196f;
    private static UIMarkerDesc _embeddedMarker;
    private static Vector2 _originalMonitorSize;
    private static Transform _originalParent;
    private static Vector2 _originalAnchorMin;
    private static Vector2 _originalAnchorMax;
    private static Vector2 _originalPivot;
    private static Vector2 _originalAnchoredPosition;
    private static Vector2 _originalMarkerSize;
    private static RectTransform _advancedRect;
    private static Transform _originalAdvancedParent;
    private static Vector2 _originalAdvancedAnchorMin;
    private static Vector2 _originalAdvancedAnchorMax;
    private static Vector2 _originalAdvancedPivot;
    private static Vector2 _originalAdvancedPosition;
    private static Vector2 _originalAdvancedSize;
    private static CanvasGroup _rootCanvas;
    private static float _originalRootAlpha;
    private static bool _originalRootBlocksRaycasts;
    private static bool _originalRootInteractable;
    private static GameObject _holoHeader;
    private static RectTransform _holoHeaderRect;
    private static float _holoBaseX;
    private static float _holoBaseY;
    private static bool _embedded;

    [HarmonyPostfix]
    [HarmonyPatch("_OnOpen")]
    private static void OpenPostfix(UIMonitorWindow __instance)
    {
        try
        {
            var factory = __instance.factory;
            var monitorId = Traverse.Create(__instance).Field<int>("_monitorId").Value;
            if (!NexusLookup.TryGetFromMonitor(factory, monitorId, out _, out _, out var marker))
            {
                Hide(__instance);
                return;
            }

            EnsureEmbedded(__instance);
            if (_embeddedMarker == null)
            {
                Plugin.Log.LogWarning("Signal Nexus could not find the Holo Beacon settings panel to embed.");
                return;
            }
            _embeddedMarker.gameObject.SetActive(true);
            _embeddedMarker.advancedSettingsGo.SetActive(true);
            _embeddedMarker.SetData(factory, marker, true);
            InvokeLifecycle(_embeddedMarker, "_OnOpen");
            _embeddedMarker.advancedSettingsGo.SetActive(true);
            _embeddedMarker.advancedCanvasGroup.alpha = 1f;
            _embeddedMarker.advancedCanvasGroup.blocksRaycasts = true;
            _embeddedMarker.advancedCanvasGroup.interactable = true;
            _embeddedMarker.Refresh();
            NexusLookup.SyncMonitorFromMarker(factory, marker);
            ApplyLayout(__instance);
        }
        catch (Exception error)
        {
            Plugin.Log.LogError($"Signal Nexus isolated a combined-panel opening failure: {error}");
        }
    }

    [HarmonyFinalizer]
    [HarmonyPatch("_OnOpen")]
    private static Exception OpenFinalizer(UIMonitorWindow __instance, Exception __exception)
    {
        if (__exception == null) return null;
        if (!(__exception is NullReferenceException)) return __exception;

        // DSP 0.10.34 can throw while opening the optional speaker subpanel.
        // Without a finalizer ManualBehaviour leaves the monitor window wedged:
        // later clicks report no error but never reopen the menu.  The monitor
        // controls are already initialized by this point, so recover the Nexus
        // extension and suppress only this known null-reference failure.
        try
        {
            OpenPostfix(__instance);
        }
        catch (Exception recoveryError)
        {
            Plugin.Log.LogError($"Signal Nexus monitor-window recovery failed: {recoveryError}");
        }

        Plugin.Log.LogWarning($"Signal Nexus suppressed DSP's null speaker-panel failure while opening monitor {Traverse.Create(__instance).Field<int>("_monitorId").Value}.");
        return null;
    }

    [HarmonyPrefix]
    [HarmonyPatch("_OnClose")]
    private static void ClosePrefix(UIMonitorWindow __instance)
    {
        Hide(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch("_OnUpdate")]
    private static void UpdatePostfix(UIMonitorWindow __instance)
    {
        if (!_embedded || _embeddedMarker == null) return;
        InvokeLifecycle(_embeddedMarker, "_OnUpdate");
        ApplyLayout(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch("AdaptSpeaker")]
    private static void AdaptSpeakerPostfix(UIMonitorWindow __instance)
    {
        if (!_embedded) return;
        _originalMonitorSize = __instance.monitorWindowRect.sizeDelta;
        ApplyLayout(__instance);
    }

    private static void EnsureEmbedded(UIMonitorWindow monitorWindow)
    {
        if (_embedded) return;
        var original = UIRoot.instance?.uiGame?.markerWindow?.markerDesc;
        if (original == null) return;

        var monitorRect = monitorWindow.monitorWindowRect;
        _originalMonitorSize = monitorRect.sizeDelta;
        _embeddedMarker = original;
        var rect = _embeddedMarker.rectTrans;
        _originalParent = rect.parent;
        _originalAnchorMin = rect.anchorMin;
        _originalAnchorMax = rect.anchorMax;
        _originalPivot = rect.pivot;
        _originalAnchoredPosition = rect.anchoredPosition;
        _originalMarkerSize = rect.sizeDelta;

        _advancedRect = _embeddedMarker.advancedCanvasGroup.transform as RectTransform;
        if (_advancedRect == null) return;
        _originalAdvancedParent = _advancedRect.parent;
        _originalAdvancedAnchorMin = _advancedRect.anchorMin;
        _originalAdvancedAnchorMax = _advancedRect.anchorMax;
        _originalAdvancedPivot = _advancedRect.pivot;
        _originalAdvancedPosition = _advancedRect.anchoredPosition;
        _originalAdvancedSize = _advancedRect.sizeDelta;

        _rootCanvas = rect.GetComponent<CanvasGroup>() ?? rect.gameObject.AddComponent<CanvasGroup>();
        _originalRootAlpha = _rootCanvas.alpha;
        _originalRootBlocksRaycasts = _rootCanvas.blocksRaycasts;
        _originalRootInteractable = _rootCanvas.interactable;
        _rootCanvas.alpha = 0f;
        _rootCanvas.blocksRaycasts = false;
        _rootCanvas.interactable = false;

        var alarmLabel = monitorWindow.GetComponentsInChildren<Text>(true)
            .FirstOrDefault(text => text != null && text.text != null && text.text.IndexOf("Alarm settings", StringComparison.OrdinalIgnoreCase) >= 0);
        if (alarmLabel == null)
            alarmLabel = monitorWindow.GetComponentsInChildren<Text>(true)
                .FirstOrDefault(text => text != null && text.gameObject.name.IndexOf("alarm", StringComparison.OrdinalIgnoreCase) >= 0);

        if (alarmLabel != null)
        {
            _holoHeader = UnityEngine.Object.Instantiate(alarmLabel.gameObject, alarmLabel.transform.parent);
            _holoHeader.name = "signal-nexus-holo-settings-header";
            _holoHeaderRect = _holoHeader.transform as RectTransform;
            var alarmRect = alarmLabel.rectTransform;
            _holoHeaderRect.SetParent(monitorRect, true);
            _holoHeaderRect.position = alarmRect.position;
            _holoBaseX = _holoHeaderRect.anchoredPosition.x;
            _holoBaseY = _holoHeaderRect.anchoredPosition.y;
            _holoHeader.GetComponent<Text>().text = "SignalNexusHoloSettings".Translate();
        }

        rect.SetParent(monitorRect, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.localScale = Vector3.one;

        _advancedRect.SetParent(monitorRect, false);
        _advancedRect.anchorMin = new Vector2(0f, 1f);
        _advancedRect.anchorMax = new Vector2(0f, 1f);
        _advancedRect.pivot = new Vector2(0f, 1f);
        _advancedRect.localScale = Vector3.one;
        _embedded = true;
        ApplyLayout(monitorWindow);
        Plugin.Log.LogInfo("Attached Holo Beacon settings beneath the Traffic Monitor alarm settings.");
    }

    private static void ApplyLayout(UIMonitorWindow monitorWindow)
    {
        if (!_embedded || _advancedRect == null) return;
        var monitorRect = monitorWindow.monitorWindowRect;
        // The native speaker panel is bottom-anchored. Expanding the monitor for
        // this section already moves it below us, so adding the speaker height a
        // second time creates a blank gap and overlaps the controls.
        const float sectionOffset = 92f;
        var headerX = _holoHeaderRect != null ? _holoBaseX : 20f;
        var headerY = (_holoHeaderRect != null ? _holoBaseY : -344f) - sectionOffset;

        if (_holoHeaderRect != null)
        {
            _holoHeader.SetActive(true);
            _holoHeaderRect.anchoredPosition = new Vector2(headerX, headerY);
        }

        _advancedRect.anchoredPosition = new Vector2(headerX + 10f, headerY - 22f);
        _advancedRect.sizeDelta = new Vector2(Math.Max(_originalAdvancedSize.x, monitorRect.rect.width - 78f), Math.Max(_originalAdvancedSize.y, 176f));

        var markerRect = _embeddedMarker.rectTrans;
        markerRect.anchoredPosition = _advancedRect.anchoredPosition;
        markerRect.sizeDelta = _advancedRect.sizeDelta;

        monitorRect.sizeDelta = new Vector2(_originalMonitorSize.x, _originalMonitorSize.y + HoloSectionHeight);
    }

    private static void Hide(UIMonitorWindow monitorWindow)
    {
        if (_embeddedMarker == null || !_embedded) return;
        if (_embeddedMarker.gameObject.activeSelf) InvokeLifecycle(_embeddedMarker, "_OnClose");

        if (_advancedRect != null)
        {
            _advancedRect.SetParent(_originalAdvancedParent, false);
            _advancedRect.anchorMin = _originalAdvancedAnchorMin;
            _advancedRect.anchorMax = _originalAdvancedAnchorMax;
            _advancedRect.pivot = _originalAdvancedPivot;
            _advancedRect.anchoredPosition = _originalAdvancedPosition;
            _advancedRect.sizeDelta = _originalAdvancedSize;
        }

        var rect = _embeddedMarker.rectTrans;
        rect.SetParent(_originalParent, false);
        rect.anchorMin = _originalAnchorMin;
        rect.anchorMax = _originalAnchorMax;
        rect.pivot = _originalPivot;
        rect.anchoredPosition = _originalAnchoredPosition;
        rect.sizeDelta = _originalMarkerSize;
        if (_rootCanvas != null)
        {
            _rootCanvas.alpha = _originalRootAlpha;
            _rootCanvas.blocksRaycasts = _originalRootBlocksRaycasts;
            _rootCanvas.interactable = _originalRootInteractable;
        }
        _embeddedMarker.advancedSettingsGo.SetActive(false);
        _embeddedMarker.gameObject.SetActive(false);
        if (_holoHeader != null) _holoHeader.SetActive(false);
        if (_originalMonitorSize != Vector2.zero) monitorWindow.monitorWindowRect.sizeDelta = _originalMonitorSize;
        _embedded = false;
    }

    private static void InvokeLifecycle(object target, string method)
    {
        try
        {
            AccessTools.Method(target.GetType(), method)?.Invoke(target, null);
        }
        catch (Exception error)
        {
            Plugin.Log.LogWarning($"Signal Nexus UI lifecycle {method} failed: {error.GetBaseException().Message}");
        }
    }
}

[HarmonyPatch(typeof(UIMonitorWindow), "TryOpenSpeakerPanel")]
internal static class MonitorSpeakerPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(UIMonitorWindow __instance)
    {
        try
        {
            OpenSafeSpeakerPanel(__instance);
        }
        catch (Exception error)
        {
            Plugin.Log.LogError($"Signal Nexus isolated a monitor/speaker opening failure: {error}");
        }
        // DSP 0.10.34's vanilla helper can dereference a missing speaker panel.
        // We fully replace it for every monitor so that this cannot abort _OnOpen.
        return false;
    }

    private static void OpenSafeSpeakerPanel(UIMonitorWindow __instance)
    {
        var factory = __instance.factory;
        var cargoTraffic = __instance.cargoTraffic ?? factory?.cargoTraffic;
        var monitorId = Traverse.Create(__instance).Field<int>("_monitorId").Value;
        if (factory == null || cargoTraffic?.monitorPool == null || monitorId <= 0 || monitorId >= cargoTraffic.monitorCursor)
            return;

        ref var monitor = ref cargoTraffic.monitorPool[monitorId];
        if (monitor.id != monitorId) return;

        var speakerPanel = __instance.speakerPanel;
        if (speakerPanel == null)
        {
            Plugin.Log.LogWarning($"Monitor {monitorId} opened without a UISpeakerPanel; skipping only the speaker subpanel.");
            return;
        }

        var adaptMethod = AccessTools.Method(typeof(UIMonitorWindow), "AdaptSpeaker");
        var adapt = adaptMethod == null ? null : (Action)Delegate.CreateDelegate(typeof(Action), __instance, adaptMethod);
        if (monitor.alarmMode != 0)
        {
            if (monitor.entityId <= 0 || monitor.entityId >= factory.entityCursor) return;
            ref var entity = ref factory.entityPool[monitor.entityId];
            if (entity.id != monitor.entityId) return;

            var digitalSystem = factory.digitalSystem;
            var speakerId = entity.speakerId;
            if (digitalSystem != null && (speakerId <= 0 || digitalSystem.speakerPool == null || speakerId >= digitalSystem.speakerCursor || digitalSystem.speakerPool[speakerId].id != speakerId))
            {
                speakerId = digitalSystem.NewSpeakerComponent(entity.id);
                entity.speakerId = speakerId;
                monitor.speakerId = speakerId;
                Plugin.Log.LogInfo($"Repaired missing speaker component for monitor entity {entity.id} (speaker {speakerId}).");
            }

            if (speakerId <= 0) return;
            monitor.speakerId = speakerId;
            var speaker = Traverse.Create(speakerPanel);
            speaker.Field("factory").SetValue(factory);
            speaker.Field("digitalSystem").SetValue(digitalSystem);
            speaker.Field("player").SetValue(GameMain.mainPlayer);
            speakerPanel.speakerId = speakerId;
            if (adapt != null)
            {
                speakerPanel.onWindowChange -= adapt;
                speakerPanel.onWindowChange += adapt;
            }
            speakerPanel._Open();
        }
        else
        {
            if (adapt != null) speakerPanel.onWindowChange -= adapt;
            speakerPanel._Close();
        }
        adapt?.Invoke();
    }
}

[HarmonyPatch(typeof(UIMonitorWindow), "OnDigitalSignalIdEndEdit")]
internal static class MonitorIpPatch
{
    [HarmonyPostfix]
    private static void Postfix(UIMonitorWindow __instance)
    {
        var factory = Traverse.Create(__instance).Field<PlanetFactory>("factory").Value;
        var monitorId = Traverse.Create(__instance).Field<int>("_monitorId").Value;
        if (NexusLookup.TryGetFromMonitor(factory, monitorId, out var entity, out _, out _))
            NexusLookup.SyncMarkerFromMonitor(factory, entity);
    }
}

[HarmonyPatch(typeof(UIMarkerDesc), "OnDigitalSignalIdEndEdit")]
internal static class MarkerIpPatch
{
    [HarmonyPostfix]
    private static void Postfix(UIMarkerDesc __instance)
    {
        var factory = Traverse.Create(__instance).Field<PlanetFactory>("factory").Value;
        var marker = Traverse.Create(__instance).Field<MarkerComponent>("marker").Value;
        NexusLookup.SyncMonitorFromMarker(factory, marker);
    }
}

[HarmonyPatch(typeof(UIMarkerTipIcons), "_OnLateUpdate")]
internal static class MarkerTipGuardPatch
{
    [HarmonyPrefix]
    private static void Prefix(UIMarkerTipIcons __instance)
    {
        var ui = Traverse.Create(__instance);
        if (ui.Field<UIMarkerTip>("markerTip").Value != null) return;

        // DSP 0.10.34 can instantiate a marker-icon container without wiring
        // its optional hover-tip object. Keep icons and clicks alive while
        // preventing the tooltip-only block from dereferencing null.
        ui.Field("hoverIdx").SetValue(-1);
        ui.Field("showTipIdx").SetValue(-1);
        ui.Field("timer").SetValue(0f);
    }
}

[HarmonyPatch(typeof(BuildingParameters), nameof(BuildingParameters.CopyFromFactoryObject))]
internal static class BuildingCopyPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref BuildingParameters __instance, int objectId, PlanetFactory factory, bool __result)
    {
        if (!__result || !NexusLookup.TryGetEntity(factory, objectId, out var entity) || entity.markerId <= 0 || entity.monitorId <= 0) return;
        var marker = factory.digitalSystem.markers[entity.markerId];
        if (marker == null) return;

        // Vanilla CopyFromFactoryObject sees the monitor first, then the marker.
        // The marker wins and produces a 2048-int template, but BuildTool_Addon
        // has a fixed 128-int monitor buffer. Shift-copying a Nexus therefore
        // overflows at parameter 129. Keep the build template monitor-shaped and
        // carry the complete marker state in content instead.
        ref var monitor = ref factory.cargoTraffic.monitorPool[entity.monitorId];
        var parameters = new int[128];
        parameters[0] = monitor.targetBeltId;
        parameters[1] = monitor.offset;
        parameters[2] = monitor.targetCargoBytes;
        parameters[3] = monitor.curPeriodTickCount;
        parameters[4] = monitor.passOperator;
        parameters[5] = monitor.passColorId;
        parameters[6] = monitor.failColorId;
        parameters[10] = monitor.systemWarningMode;
        parameters[12] = monitor.alarmMode;
        parameters[14] = monitor.cargoFilter;
        parameters[17] = monitor.systemWarningSignalId;
        parameters[21] = monitor.digitalSignalId;

        if (entity.speakerId > 0 && factory.digitalSystem.speakerPool != null && entity.speakerId < factory.digitalSystem.speakerCursor)
        {
            ref var speaker = ref factory.digitalSystem.speakerPool[entity.speakerId];
            if (speaker.id == entity.speakerId)
            {
                parameters[7] = speaker.tone;
                parameters[8] = speaker.volume;
                parameters[9] = speaker.pitch;
                parameters[11] = speaker.repeat ? 1 : 0;
                parameters[13] = Mathf.RoundToInt(speaker.length * 10000f);
                parameters[18] = Mathf.RoundToInt(speaker.falloffRadius0 * 10f);
                parameters[19] = Mathf.RoundToInt(speaker.falloffRadius1 * 10f);
            }
        }

        __instance.type = BuildingType.Monitor;
        __instance.mode0 = monitor.spawnItemOperator;
        __instance.parameters = parameters;
        __instance.content = MarkerSettings.Encode(marker);
    }
}

[HarmonyPatch(typeof(BuildingParameters), nameof(BuildingParameters.PasteToFactoryObject))]
internal static class BuildingPastePatch
{
    [HarmonyPostfix]
    private static void Postfix(BuildingParameters __instance, int objectId, PlanetFactory factory, bool __result)
    {
        if (!__result || !NexusLookup.TryGetEntity(factory, objectId, out var entity) || entity.markerId <= 0) return;
        var marker = factory.digitalSystem.markers[entity.markerId];
        if (marker != null) MarkerSettings.TryApply(__instance.content, marker, factory.entityPool);
    }
}

[HarmonyPatch(typeof(BuildTool_Addon), nameof(BuildTool_Addon.CreatePrebuilds))]
internal static class AddonPrebuildContentPatch
{
    [HarmonyPostfix]
    private static void Postfix(BuildTool_Addon __instance)
    {
        var factory = __instance.factory;
        if (factory?.prebuildPool == null || __instance.buildPreviews == null) return;

        foreach (var preview in __instance.buildPreviews)
        {
            if (preview?.item == null || preview.item.ID != Plugin.ItemId || preview.objId >= 0 || string.IsNullOrEmpty(preview.content))
                continue;

            var prebuildId = -preview.objId;
            if (prebuildId <= 0 || prebuildId >= factory.prebuildCursor) continue;
            ref var prebuild = ref factory.prebuildPool[prebuildId];
            if (prebuild.id != prebuildId || prebuild.protoId != Plugin.ItemId) continue;

            // BuildTool_Addon copies parameters but omits BuildPreview.content.
            // Nexus stores the marker half of its combined settings there, so
            // restore it before construction consumes the prebuild data.
            prebuild.content = preview.content;
            Plugin.Log.LogInfo($"Preserved Holo settings on copied Signal Nexus prebuild {prebuildId}.");
        }
    }
}

[HarmonyPatch(typeof(BuildingParameters), nameof(BuildingParameters.ApplyPrebuildParametersToEntity))]
internal static class BuildingApplyPatch
{
    [HarmonyPostfix]
    private static void Postfix(int entityId, string content, PlanetFactory factory)
    {
        if (!NexusLookup.TryGetEntity(factory, entityId, out var entity) || entity.markerId <= 0) return;
        var marker = factory.digitalSystem.markers[entity.markerId];
        if (marker == null || !MarkerSettings.TryApply(content, marker, factory.entityPool))
            NexusLookup.SyncMarkerFromMonitor(factory, entity);
    }
}

internal static class MarkerSettings
{
    private const string Prefix = "SIGNAL_NEXUS_1:";

    internal static string Encode(MarkerComponent marker)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(marker.icon);
            writer.Write(marker.word ?? string.Empty);
            writer.Write((int)marker.visibility);
            writer.Write((int)marker.detailLevel);
            writer.Write(marker.offline);
            writer.Write(marker.color);
            writer.Write(marker.height);
            writer.Write(marker.radius);
            writer.Write(marker.digitalSignalId);
            writer.Write(marker.dfAttractionFlags);
            writer.Write(marker.name ?? string.Empty);
            writer.Write(marker.tags ?? string.Empty);
        }
        return Prefix + Convert.ToBase64String(stream.ToArray());
    }

    internal static bool TryApply(string content, MarkerComponent marker, EntityData[] entities)
    {
        if (string.IsNullOrEmpty(content) || !content.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        try
        {
            var bytes = Convert.FromBase64String(content.Substring(Prefix.Length));
            using var reader = new BinaryReader(new MemoryStream(bytes));
            marker.icon = reader.ReadInt32();
            marker.word = reader.ReadString();
            marker.visibility = (EMarkerVisibility)reader.ReadInt32();
            marker.detailLevel = (EMarkerDetailLevel)reader.ReadInt32();
            marker.offline = reader.ReadBoolean();
            marker.color = reader.ReadByte();
            marker.SetHeight(entities, reader.ReadSingle());
            marker.SetRadius(reader.ReadSingle());
            marker.SetDigitalSignalId(reader.ReadInt32());
            marker.SetDFAttractionFlags(reader.ReadInt32());
            marker.name = reader.ReadString();
            marker.tags = reader.ReadString();
            return true;
        }
        catch (Exception error)
        {
            Plugin.Log.LogWarning($"Could not restore Signal Nexus beacon settings: {error.Message}");
            return false;
        }
    }
}
