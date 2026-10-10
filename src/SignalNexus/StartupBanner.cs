using BepInEx.Logging;
using UnityEngine;

namespace SignalNexus;

internal static class StartupBanner
{
    private const string Reset = "\u001b[0m";
    private const string White = "\u001b[97m";
    private const string Green = "\u001b[1;38;5;46m";

    // Different purple shades create the vertical shading.
    private const string Purple1 = "\u001b[1;38;5;141m";
    private const string Purple2 = "\u001b[1;38;5;135m";
    private const string Purple3 = "\u001b[38;5;99m";
    private const string Purple4 = "\u001b[38;5;93m";
    private const string Purple5 = "\u001b[38;5;57m";
    private const string Purple6 = "\u001b[38;5;54m";

    public static void Print(ManualLogSource logger)
    {
        logger.LogMessage(string.Empty);

        logger.LogMessage(
            $"{Purple6}──────────────{Reset} " +
            $"{Green}CREATED BY{Reset} " +
            $"{Purple6}──────────────{Reset}");

        PrintRow(
            logger,
            Purple1,
            "  ██████╗  ███████╗ ██╗   ██╗ ███████╗  ",
            "Mod: ",
            $"{Plugin.PluginName} v{Plugin.PluginVersion}");

        PrintRow(
            logger,
            Purple2,
            " ██╔════╝  ██╔════╝ ██║   ██║ ██╔════╝  ",
            "Game: ",
            $"Dyson Sphere Program {Application.version}");

        PrintRow(
            logger,
            Purple3,
            " ██║  ███╗ ███████╗ ██║   ██║ ███████╗  ",
            "Plugin: ",
            Plugin.PluginGuid);

        PrintRow(
            logger,
            Purple4,
            " ██║   ██║ ╚════██║ ╚██╗ ██╔╝ ╚════██║  ",
            "Created by: ",
            "GSVS UK ACM");

        PrintRow(
            logger,
            Purple5,
            " ╚██████╔╝ ███████║  ╚████╔╝  ███████║  ",
            "Combines: ",
            "Traffic Monitor + Holo Beacon + Tesla Tower");

        logger.LogMessage(
            $"{Purple6}  ╚═════╝  ╚══════╝   ╚═══╝   ╚══════╝  {Reset}  " +
            $"{White}BepInEx startup registration{Reset}");

        logger.LogMessage(
            $"{Purple6}────────────────────────────────────────────{Reset}" +
            $"{Green}◆{Reset}" +
            $"{Purple6}────────────────────────{Reset}");
    }

    private static void PrintRow(
        ManualLogSource logger,
        string artColor,
        string art,
        string label,
        string value)
    {
        logger.LogMessage(
            $"{artColor}{art}{Reset}  " +
            $"{White}{label}{Reset}" +
            $"{Green}{value}{Reset}");
    }
}
