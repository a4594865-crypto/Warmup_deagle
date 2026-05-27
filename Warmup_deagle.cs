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
        public override string ModuleVersion => "1.0.9";
        public override string ModuleDescription => "Warmup Deagle Only + Universal Teleport Fix";

        private static HashSet<string> AllowedWeapons = new();

        // 用來記錄這局遊戲中，哪些玩家已經成功進服被「震醒」過了
        private readonly HashSet<ulong> _hasBeenFixed = new();

        public override void Load(bool hotReload)
        {
            LoadConfig();
            RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);

            // 玩家斷線時清除紀錄，方便重連時再次觸發
            RegisterEventHandler<EventPlayerDisconnect>((@event, info) =>
            {
                var player = @event.Userid;
                if (player != null && player.IsValid && !player.IsBot)
                {
                    _hasBeenFixed.Remove(player.SteamID);
                }
                return HookResult.Continue;
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

            // 🔥 核心解卡邏輯：如果是第一次進服出生的玩家
            if (steamId != 0 && !_hasBeenFixed.Contains(steamId))
            {
                var pawn = player.PlayerPawn?.Value;
                if (pawn != null)
                {
                    _hasBeenFixed.Add(steamId); // 標記已處理，之後每回合出生直接跳過

                    // 延遲到下一幀，等玩家實體完全載入後執行
                    Server.NextFrame(() =>
                    {
                        if (pawn.IsValid && pawn.AbsOrigin != null)
                        {
                            // 💡 萬無一失的解卡法：強制原地傳送並歸零速度
                            // 這會強迫 Sub-tick 系統發送最高優先級的座標同步封包給客戶端，瞬間解開輸入法死結！
                            pawn.Teleport(pawn.AbsOrigin, pawn.AbsRotation, new Vector(0, 0, 0));
                        }
                    });
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
