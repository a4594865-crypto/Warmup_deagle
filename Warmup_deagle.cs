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
        public override string ModuleVersion => "1.0.5";
        public override string ModuleDescription => "Warmup Deagle Only + First Join IME Fix";

        private static HashSet<string> AllowedWeapons = new();

        // 記錄哪些玩家「剛進服」需要被解卡（剩餘的 Tick 數）
        private readonly Dictionary<int, int> _ticksToFix = new();

        // 🔥 新增：用來記錄「這局遊戲中，已經成功進服並解卡過的人」
        // 使用 SteamID (AuthorizedID) 來記錄最準確，斷線重連也會重新觸發
        private readonly HashSet<string> _hasBeenFixed = new();

        public override void Load(bool hotReload)
        {
            LoadConfig();
            RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);

            // 當玩家斷開連線時，把紀錄清除，這樣他如果斷線重連，進服還能再幫他解卡一次
            RegisterEventHandler<EventPlayerDisconnect>((@event, info) =>
            {
                var player = @event.Userid;
                if (player != null && player.IsValid && player.AuthorizedID != null)
                {
                    _hasBeenFixed.Remove(player.AuthorizedID.SteamId64.ToString());
                }
                return HookResult.Continue;
            });

            // 監聽伺服器每一步的輸入處理
            RegisterOnPlayerRunCmd((player, cmd, usercmd) =>
            {
                if (player == null || !player.IsValid || player.IsBot) return;

                if (_ticksToFix.TryGetValue(player.Slot, out int ticksLeft) && ticksLeft > 0)
                {
                    // 在底層硬塞一個『靜音走路（SHIFT）』的按鍵訊號
                    usercmd.Buttons |= PlayerButtons.Walk;

                    _ticksToFix[player.Slot] = ticksLeft - 1;
                    if (_ticksToFix[player.Slot] <= 0)
                    {
                        _ticksToFix.Remove(player.Slot);
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

            // 取得玩家的唯一 SteamID
            string steamId = player.AuthorizedID?.SteamId64.ToString() ?? "";

            // 🔥 核心改動：檢查這個玩家有沒有被「解卡」過
            if (!string.IsNullOrEmpty(steamId) && !_hasBeenFixed.Contains(steamId))
            {
                if (player.PlayerPawn.Value != null)
                {
                    _ticksToFix[player.Slot] = 5; // 只有「第一次進服出生」才幫他按 0.08 秒 SHIFT
                    _hasBeenFixed.Add(steamId);   // 標記為已解卡，之後每回合出生直接跳過
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
            var weaponServices = player.WeaponServices;
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
