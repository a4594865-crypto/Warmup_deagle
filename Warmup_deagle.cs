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
        public override string ModuleName => "Warmup_deagle_with_IME_Fix";
        public override string ModuleVersion => "1.1.5";
        public override string ModuleDescription => "Warmup Deagle Only + Force Clean IME Context";

        private static HashSet<string> AllowedWeapons = new();

        // 核心：用來記錄哪些玩家進服已經被伺服器「強行清洗過輸入法」了
        // 使用 Slot (通道 ID) 紀錄，比 SteamID 快且不吃任何記憶體空間
        private readonly HashSet<int> _imeFixedPlayers = new();

        public override void Load(bool hotReload)
        {
            LoadConfig();
            RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);

            // 當玩家中途換線、斷線離開伺服器時，清除他的紀錄
            // 這樣他如果下次重新連進來，伺服器能再次幫他強制清洗輸入法環境
            RegisterEventHandler<EventPlayerDisconnect>((@event, info) =>
            {
                var player = @event.Userid;
                if (player != null && player.IsValid)
                {
                    _imeFixedPlayers.Remove(player.Slot);
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

            // 🔥 根本解決機制：
            // 當玩家進服第一次出生（這時候他還沒開始按任何鍵盤按鍵）
            if (!_imeFixedPlayers.Contains(player.Slot))
            {
                _imeFixedPlayers.Add(player.Slot); // 立刻鎖定白名單，之後每回合出生直接秒 return 跳過，效能消耗為 0

                // 延遲到下一幀執行，確保玩家的客戶端控制台網絡通道（NetChan）已經準備就緒
                Server.NextFrame(() =>
                {
                    if (player.IsValid)
                    {
                        // 💡 伺服器主動出擊：強行在玩家電腦後台執行官方清理指令
                        // 效果等同於幫他在背景永久按住那下 SHIFT，走路時輸入法絕不干擾，按 Y 依然能打中文！
                        player.ExecuteClientCommand("cl_input_clean_input_methods 1");
                    }
                });
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
