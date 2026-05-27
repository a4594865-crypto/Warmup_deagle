using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace deagle_only
{
    public class deagle_only : BasePlugin
    {
        public override string ModuleAuthor => "GSM-RO & Custom Fix";
        public override string ModuleName => "Warmup_deagle_with_Fix";
        public override string ModuleVersion => "1.0.8";
        public override string ModuleDescription => "Warmup Deagle Only + First Join IME Fix";

        private static HashSet<string> AllowedWeapons = new();

        // 記錄哪些玩家「剛進服」需要被解卡（剩餘的 Tick 數）
        private readonly Dictionary<int, int> _ticksToFix = new();

        // 用來記錄「這局遊戲中，已經成功進服並解卡過的人」
        private readonly HashSet<ulong> _hasBeenFixed = new();

        public override void Load(bool hotReload)
        {
            LoadConfig();
            RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);

            // 當玩家斷開連線時，清除紀錄
            RegisterEventHandler<EventPlayerDisconnect>((@event, info) =>
            {
                var player = @event.Userid;
                if (player != null && player.IsValid && !player.IsBot)
                {
                    _hasBeenFixed.Remove(player.SteamID);
                }
                return HookResult.Continue;
            });

            // 🔥 完美修正 L63 報錯：使用官方標準的 SetButtons 方法
            RegisterListener<Listeners.OnTick>(() =>
            {
                foreach (var player in Utilities.GetPlayers())
                {
                    if (player == null || !player.IsValid || player.IsBot) continue;

                    if (_ticksToFix.TryGetValue(player.Slot, out int ticksLeft) && ticksLeft > 0)
                    {
                        // 1. 先抓出玩家目前的按鍵狀態
                        var currentButtons = player.Buttons;

                        // 2. 如果玩家目前還沒按著 SHIFT，我們就幫他加上去
                        if ((currentButtons & PlayerButtons.Walk) == 0)
                        {
                            // 💡 核心：使用官方提供的方法強制寫入按鍵，避開所有唯讀與結構命名問題
                            player.SetButtons(currentButtons | PlayerButtons.Walk, player.Buttons);
                        }

                        _ticksToFix[player.Slot] = ticksLeft - 1;
                        if (_ticksToFix[player.Slot] <= 0)
                        {
                            _ticksToFix.Remove(player.Slot);
                        }
                    }
                }
            });
        }

        private void LoadConfig()
        {
            var path = Path.Combine(ModuleDirectory, "config.cfg");
            if (!File.Exists(path)) File.WriteAllText(path, "allowed_weapons = weapon_deagle, weapon_knife\n");
            
            var lines = File.ReadAllLines(path);
            AllowedWeapons = lines.FirstOrDefault(l => l.StartsWith("allowed_weapons"))
                ?.Split('=')[1].Split(',').Select(w => w.Trim()).Where(w => !string.IsNullOrEmpty(w)).ToHashSet() 
                ?? new HashSet<string> { "weapon_deagle", "weapon_knife" };
        }

        private static bool IsWarmupActive()
        {
            var gameRulesEnt = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("cs_gamerules").SingleOrDefault();
            return gameRulesEnt?.As<CCSGameRulesProxy>()?.GameRules?.WarmupPeriod == true;
        }

        private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;

            ulong steamId = player.SteamID;

            // 檢查這個玩家有沒有被「解卡」過
            if (steamId != 0 && !_hasBeenFixed.Contains(steamId))
            {
                if (player.PlayerPawn.Value != null)
                {
                    _ticksToFix[player.Slot] = 5; // 只有「第一次進服出生」才幫他按 5 次 Tick 的 SHIFT
                    _hasBeenFixed.Add(steamId);   // 標記為已解卡
                }
            }

            // 以下維持您原本的熱身賽 Deagle 限制邏輯
            if (!IsWarmupActive()) return HookResult.Continue;

            Server.NextFrame(() =>
            {
                var pawn = player.PlayerPawn?.Value;
                if (pawn == null || (LifeState_t)pawn.LifeState != LifeState_t.LIFE_ALIVE) return;

                RemoveNonAllowedWeapons(player);
                foreach (var weapon in AllowedWeapons)
                {
                    player.GiveNamedItem(weapon);
                }
            });
            return HookResult.Continue;
        }

        private static void RemoveNonAllowedWeapons(CCSPlayerController player)
        {
            var weaponServices = player.PlayerPawn?.Value?.WeaponServices;
            if (weaponServices?.MyWeapons == null) return;

            foreach (var weapon in weaponServices.MyWeapons)
            {
                if (weapon?.IsValid != true || weapon.Value == null) continue;
                if (!AllowedWeapons.Contains(weapon.Value.DesignerName))
                {
                    weapon.Value.AddEntityIOEvent("Kill", weapon.Value, null, "", 0.0f);
                }
            }
        }
    }
}
