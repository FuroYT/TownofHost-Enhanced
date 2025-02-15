using AmongUs.Data;
using AmongUs.GameOptions;
using HarmonyLib;
using InnerNet;
using Rewired.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using TOHE.Modules;
using TOHE.Roles.Neutral;
using UnityEngine;
using static TOHE.Translator;
using Object = UnityEngine.Object;

namespace TOHE;

[HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Update))]
public static class GameStartManagerUpdatePatch
{
    public static void Prefix(GameStartManager __instance)
    {
        __instance.MinPlayers = 1;
    }
}
//タイマーとコード隠し
public class GameStartManagerPatch
{
    private static SpriteRenderer cancelButton;
    private static float timer = 600f;
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Start))]
    public class GameStartManagerStartPatch
    {
        public static TextMeshPro HideName;
        public static void Postfix(GameStartManager __instance)
        {
            __instance.GameRoomNameCode.text = GameCode.IntToGameName(AmongUsClient.Instance.GameId);
            // Reset lobby countdown timer
            timer = 600f;

            HideName = Object.Instantiate(__instance.GameRoomNameCode, __instance.GameRoomNameCode.transform);
            HideName.text = ColorUtility.TryParseHtmlString(Main.HideColor.Value, out _)
                    ? $"<color={Main.HideColor.Value}>{Main.HideName.Value}</color>"
                    : $"<color={Main.ModColor}>{Main.HideName.Value}</color>";

            cancelButton = Object.Instantiate(__instance.StartButton, __instance.transform);
            cancelButton.name = "CancelButton";
            var cancelLabel = cancelButton.GetComponentInChildren<TextMeshPro>();
            Object.Destroy(cancelLabel.GetComponent<TextTranslatorTMP>());
            cancelLabel.text = GetString("Cancel");
            cancelButton.transform.localScale = new(0.4f, 0.4f, 1f);
            cancelButton.color = Color.red;
            if (GameStates.IsLocalGame)
            {
                cancelButton.transform.localPosition = new(0f, 0.1f, 0f);
            }
            else
            {
                cancelButton.transform.localPosition = new(0f, -0.36f, 0f);
            }
            var buttonComponent = cancelButton.GetComponent<PassiveButton>();
            buttonComponent.OnClick = new();
            buttonComponent.OnClick.AddListener((Action)(() => __instance.ResetStartState()));
            cancelButton.gameObject.SetActive(false);

            if (!AmongUsClient.Instance.AmHost) return;

            // Make Public Button
            if (ModUpdater.isBroken || (ModUpdater.hasUpdate && ModUpdater.forceUpdate) || !Main.AllowPublicRoom || !VersionChecker.IsSupported)
            {
                __instance.MakePublicButton.color = Palette.DisabledClear;
                __instance.privatePublicText.color = Palette.DisabledClear;
            }

            if (GameStates.IsNormalGame)
            {
                if (Main.NormalOptions.KillCooldown == 0f)
                    Main.NormalOptions.KillCooldown = Main.LastKillCooldown.Value;

                AURoleOptions.SetOpt(Main.NormalOptions.Cast<IGameOptions>());
                if (AURoleOptions.ShapeshifterCooldown == 0f)
                    AURoleOptions.ShapeshifterCooldown = Main.LastShapeshifterCooldown.Value;

                AURoleOptions.GuardianAngelCooldown = Spiritcaller.SpiritAbilityCooldown.GetFloat();
            }
        }
    }

    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Update))]
    public class GameStartManagerUpdatePatch
    {
        private static bool update = false;
        private static string currentText = "";
        public static float exitTimer = -1f;
        private static float minWait, maxWait;
        private static int minPlayer;
        public static void Prefix(GameStartManager __instance)
        {
            minWait = Options.MinWaitAutoStart.GetFloat();
            maxWait = Options.MaxWaitAutoStart.GetFloat();
            minPlayer = Options.PlayerAutoStart.GetInt();
            minWait = 600f - minWait * 60f;
            maxWait *= 60f;
            // Lobby code
            if (DataManager.Settings.Gameplay.StreamerMode)
            {
                __instance.GameRoomNameCode.color = new(255, 255, 255, 0);
                GameStartManagerStartPatch.HideName.enabled = true;
            }
            else
            {
                __instance.GameRoomNameCode.color = new(255, 255, 255, 255);
                GameStartManagerStartPatch.HideName.enabled = false;
            }
            if (!AmongUsClient.Instance.AmHost || !GameData.Instance || AmongUsClient.Instance.NetworkMode == NetworkModes.LocalGame) return; // Not host or no instance or LocalGame
            update = GameData.Instance.PlayerCount != __instance.LastPlayerCount;

            if (Main.AutoStart.Value)
            {
                Main.updateTime++;
                if (Main.updateTime >= 50)
                {
                    Main.updateTime = 0;
                    if (((GameData.Instance.PlayerCount >= minPlayer && timer <= minWait) || timer <= maxWait) && !GameStates.IsCountDown)
                    {
                        _ = new LateTask(() =>
                        {
                            var invalidColor = Main.AllPlayerControls.Where(p => p.Data.DefaultOutfit.ColorId < 0 || Palette.PlayerColors.Length <= p.Data.DefaultOutfit.ColorId).ToArray();

                            if (invalidColor.Length > 0)
                            {
                                invalidColor.Do(p => AmongUsClient.Instance.KickPlayer(p.GetClientId(), false));

                                Logger.SendInGame(GetString("Error.InvalidColorPreventStart"));
                                var msg = GetString("Error.InvalidColor");
                                msg += "\n" + string.Join(",", invalidColor.Select(p => $"{p.GetRealName()}"));
                                Utils.SendMessage(msg);
                            }

                            if (Options.RandomMapsMode.GetBool())
                            {
                                if (GameStates.IsNormalGame)
                                    Main.NormalOptions.MapId = GameStartRandomMap.SelectRandomMap();

                                else if (GameStates.IsHideNSeek)
                                    Main.HideNSeekOptions.MapId = GameStartRandomMap.SelectRandomMap();
                            }

                            //if (GameStates.IsNormalGame && Options.IsActiveDleks)
                            //{
                            //    Logger.SendInGame(GetString("Warning.BrokenVentsInDleksSendInGame"));
                            //    Utils.SendMessage(GetString("Warning.BrokenVentsInDleksMessage"), title: Utils.ColorString(Utils.GetRoleColor(CustomRoles.NiceMini), GetString("WarningTitle")));
                            //}

                            RPC.RpcVersionCheck();

                            GameStartManager.Instance.startState = GameStartManager.StartingStates.Countdown;
                            GameStartManager.Instance.countDownTimer = Options.AutoStartTimer.GetInt();
                            __instance.StartButton.gameObject.SetActive(false);
                        }, 0.8f, "Auto Start");
                    }
                }
            }
        }
        public static void Postfix(GameStartManager __instance)
        {
            if (!AmongUsClient.Instance) return;

            string warningMessage = "";
            if (AmongUsClient.Instance.AmHost)
            {
                bool canStartGame = true;
                List<string> mismatchedPlayerNameList = [];
                foreach (var client in AmongUsClient.Instance.allClients.ToArray())
                {
                    if (client.Character == null) continue;
                    var dummyComponent = client.Character.GetComponent<DummyBehaviour>();
                    if (dummyComponent != null && dummyComponent.enabled)
                        continue;
                    if (!MatchVersions(client.Id, true))
                    {
                        canStartGame = false;
                        mismatchedPlayerNameList.Add(Utils.ColorString(Palette.PlayerColors[client.ColorId], client.Character.Data.PlayerName));
                    }
                }
                if (!canStartGame)
                {
                    __instance.StartButton.gameObject.SetActive(false);
                    warningMessage = Utils.ColorString(Color.red, string.Format(GetString("Warning.MismatchedVersion"), string.Join(" ", mismatchedPlayerNameList), $"<color={Main.ModColor}>{Main.ModName}</color>"));
                }
                cancelButton.gameObject.SetActive(__instance.startState == GameStartManager.StartingStates.Countdown);
            }
            else
            {
                if (MatchVersions(AmongUsClient.Instance.HostId, true) || Main.VersionCheat.Value || Main.IsHostVersionCheating)
                    exitTimer = 0;
                else
                {
                    exitTimer += Time.deltaTime;
                    if (exitTimer >= 5)
                    {
                        exitTimer = 0;
                        AmongUsClient.Instance.ExitGame(DisconnectReasons.ExitGame);
                        SceneChanger.ChangeScene("MainMenu");
                    }
                    if (exitTimer != 0)
                        warningMessage = Utils.ColorString(Color.red, string.Format(GetString("Warning.AutoExitAtMismatchedVersion"), $"<color={Main.ModColor}>{Main.ModName}</color>", Math.Round(5 - exitTimer).ToString()));
                }
            }
            if (warningMessage != "")
            {
                __instance.GameStartText.text = warningMessage;
                __instance.GameStartText.transform.localPosition = __instance.StartButton.transform.localPosition + Vector3.up * 2;
            }
            else
            {
                __instance.GameStartText.transform.localPosition = __instance.StartButton.transform.localPosition;
            }

            // Lobby timer
            if (!AmongUsClient.Instance.AmHost || !GameData.Instance || AmongUsClient.Instance.NetworkMode == NetworkModes.LocalGame) return;

            if (update) currentText = __instance.PlayerCounter.text;

            timer = Mathf.Max(0f, timer -= Time.deltaTime);
            int minutes = (int)timer / 60;
            int seconds = (int)timer % 60;
            string suffix = $" ({minutes:00}:{seconds:00})";
            if (timer <= 60) suffix = Utils.ColorString(Color.red, suffix);

            __instance.PlayerCounter.text = currentText + suffix;
            __instance.PlayerCounter.autoSizeTextContainer = true;
        }
        private static bool MatchVersions(int clientId, bool acceptVanilla = false)
        {
            if (!Main.playerVersion.TryGetValue(clientId, out var version)) return acceptVanilla;
            return Main.ForkId == version.forkId
                && Main.version.CompareTo(version.version) == 0
                && version.tag == $"{ThisAssembly.Git.Commit}({ThisAssembly.Git.Branch})";
        }
    }
    [HarmonyPatch(typeof(TextBoxTMP), nameof(TextBoxTMP.SetText))]
    public static class HiddenTextPatch
    {
        private static void Postfix(TextBoxTMP __instance)
        {
            if (__instance.name == "GameIdText") __instance.outputText.text = new string('*', __instance.text.Length);
        }
    }
}
[HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.BeginGame))]
public class GameStartRandomMap
{
    public static bool Prefix(GameStartManager __instance)
    {
        var invalidColor = Main.AllPlayerControls.Where(p => p.Data.DefaultOutfit.ColorId < 0 || Palette.PlayerColors.Length <= p.Data.DefaultOutfit.ColorId).ToArray();
        if (invalidColor.Length > 0)
        {
            Logger.SendInGame(GetString("Error.InvalidColorPreventStart"));
            var msg = GetString("Error.InvalidColor");
            msg += "\n" + string.Join(",", invalidColor.Select(p => $"{p.GetRealName()}"));
            Utils.SendMessage(msg);
            return false;
        }

        //if (GameStates.IsNormalGame && Options.IsActiveDleks)
        //{
        //    Logger.SendInGame(GetString("Warning.BrokenVentsInDleksSendInGame"));
        //    Utils.SendMessage(GetString("Warning.BrokenVentsInDleksMessage"), title: Utils.ColorString(Utils.GetRoleColor(CustomRoles.NiceMini), GetString("WarningTitle")));
        //}

        IGameOptions opt = GameStates.IsNormalGame 
            ? Main.NormalOptions.Cast<IGameOptions>()
            : Main.HideNSeekOptions.Cast<IGameOptions>();
        
        if (GameStates.IsNormalGame)
        {
            Options.DefaultKillCooldown = Main.NormalOptions.KillCooldown;
            Main.LastKillCooldown.Value = Main.NormalOptions.KillCooldown;
            Main.NormalOptions.KillCooldown = 0f;

            AURoleOptions.SetOpt(opt);
            Main.LastShapeshifterCooldown.Value = AURoleOptions.ShapeshifterCooldown;
            AURoleOptions.ShapeshifterCooldown = 0f;
            AURoleOptions.ImpostorsCanSeeProtect = false;
        }

        PlayerControl.LocalPlayer.RpcSyncSettings(GameOptionsManager.Instance.gameOptionsFactory.ToBytes(opt));
        RPC.RpcVersionCheck();

        __instance.ReallyBegin(false);
        return false;
    }
    public static bool Prefix()
    {
        bool continueStart = true;
        if (Options.RandomMapsMode.GetBool())
        {
            if (GameStates.IsNormalGame)
                Main.NormalOptions.MapId = SelectRandomMap();

            else if (GameStates.IsHideNSeek)
                Main.HideNSeekOptions.MapId = SelectRandomMap();
        }
        return continueStart;
    }
    public static byte SelectRandomMap()
    {
        var rand = IRandom.Instance;
        List<byte> randomMaps = [];
        /*
            The Skeld    = 0
            MIRA HQ      = 1
            Polus        = 2
            Dleks        = 3
            The Airship  = 4
            The Fungle   = 5
        */

        if (Options.UseMoreRandomMapSelection.GetBool())
        {
            if (rand.Next(1, 100) <= Options.SkeldChance.GetInt()) randomMaps.Add(0);
            if (rand.Next(1, 100) <= Options.MiraChance.GetInt()) randomMaps.Add(1);
            if (rand.Next(1, 100) <= Options.PolusChance.GetInt()) randomMaps.Add(2);
            if (rand.Next(1, 100) <= Options.DleksChance.GetInt()) randomMaps.Add(3);
            if (rand.Next(1, 100) <= Options.AirshipChance.GetInt()) randomMaps.Add(4);
            if (rand.Next(1, 100) <= Options.FungleChance.GetInt()) randomMaps.Add(5);
        }
        else
        {
            var tempRand = rand.Next(1, 100);

            if (tempRand <= Options.SkeldChance.GetInt()) randomMaps.Add(0);
            if (tempRand <= Options.MiraChance.GetInt()) randomMaps.Add(1);
            if (tempRand <= Options.PolusChance.GetInt()) randomMaps.Add(2);
            if (tempRand <= Options.DleksChance.GetInt()) randomMaps.Add(3);
            if (tempRand <= Options.AirshipChance.GetInt()) randomMaps.Add(4);
            if (tempRand <= Options.FungleChance.GetInt()) randomMaps.Add(5);
        }

        if (randomMaps.Count > 0)
        {
            var mapsId = randomMaps[rand.Next(randomMaps.Count)];

            Logger.Info($"{mapsId}", "Chance Select MapId");
            return mapsId;
        }
        else
        {
            if (Options.SkeldChance.GetInt() > 0) randomMaps.Add(0);
            if (Options.MiraChance.GetInt() > 0) randomMaps.Add(1);
            if (Options.PolusChance.GetInt() > 0) randomMaps.Add(2);
            if (Options.DleksChance.GetInt() > 0) randomMaps.Add(3);
            if (Options.AirshipChance.GetInt() > 0) randomMaps.Add(4);
            if (Options.FungleChance.GetInt() > 0) randomMaps.Add(5);

            var mapsId = randomMaps[rand.Next(randomMaps.Count)];

            Logger.Info($"{mapsId}", "Random Select MapId");
            return mapsId;
        }
    }
}
[HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.ResetStartState))]
class ResetStartStatePatch
{
    public static void Prefix()
    {
        if (GameStates.IsCountDown)
        {
            if (GameStates.IsNormalGame)
                Main.NormalOptions.KillCooldown = Options.DefaultKillCooldown;
            
            PlayerControl.LocalPlayer.RpcSyncSettings(GameOptionsManager.Instance.gameOptionsFactory.ToBytes(GameOptionsManager.Instance.CurrentGameOptions));
        }
    }
}
[HarmonyPatch(typeof(IGameOptionsExtensions), nameof(IGameOptionsExtensions.GetAdjustedNumImpostors))]
class UnrestrictedNumImpostorsPatch
{
    public static bool Prefix(ref int __result)
    {
        if (GameStates.IsNormalGame)
        {
            __result = Main.NormalOptions.NumImpostors;
        }
        else if (GameStates.IsHideNSeek)
        {
            __result = Main.HideNSeekOptions.NumImpostors;
        }
        return false;
    }
}