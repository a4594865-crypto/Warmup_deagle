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
        public override string ModuleVersion => "1.1.1";
        public override string ModuleDescription => "Warmup Deagle Only + First Spawn Teleport Fix";

        private static HashSet<string> AllowedWeapons = new();

        // 🔥 核心：用來記錄「這局遊戲中，已經成功進服並被震醒過的人」
        // 使用玩家的 Slot（通道編號）來記錄，最輕量且完全不吃記憶體
        private readonly HashSet<int> _hasBeenFixed = new();

        public override void Load(bool hotReload)
        {
            LoadConfig();
            RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);

            // 當玩家完全斷開連線、離開伺服器時，才把他的編號從名單移除
            // 這樣他如果下一局重新連線進來，才能再次幫他解卡
            RegisterEventHandler<EventPlayerDisconnect>((@event, info) =>
            {
                var player = @event.Userid;
                if (player != null && player.IsValid)
                {
                    _hasBeenFixed.Remove(player.Slot);
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

            // 🔥 完美解卡機制：檢查這個玩家「進服後，有沒有被拯救過？」
            if (!_hasBeenFixed.Contains(player.Slot))
            {
                var pawn = player.PlayerPawn?.Value;
                if (pawn != null)
                {
                    // 1. 既然抓到他第一次出生了，立刻把他寫入白名單！
                    _hasBeenFixed.Add(player.Slot);

                    // 2. 延遲到下一幀（萬分之一秒後，等他的身體在遊戲世界中完全站好）執行
                    Server.NextFrame(() =>
                    {
                        if (pawn.IsValid && pawn.AbsOrigin != null)
                        {
                            // 3. 原地重置座標與速度，強制踢醒客戶端的移動預測，衝破輸入法死結！
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
