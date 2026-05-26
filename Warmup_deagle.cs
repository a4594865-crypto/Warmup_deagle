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
        public override string ModuleAuthor => "GSM-RO";
        public override string ModuleName => "Warmup_deagle";
        public override string ModuleVersion => "1.0.5"; // 升級版本號
        public override string ModuleDescription => "Warmup Deagle Only - Fixed Event Name";

        private bool _warmupMessageSent = false;
        private static HashSet<string> AllowedWeapons = new();

        public override void Load(bool hotReload)
        {
            LoadConfig();

            // 註冊事件：玩家出生與回合開始
            RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
            RegisterEventHandler<EventRoundStart>(OnRoundStart);
            
            // 💡 修正：官方正確的事件名稱為 EventWarmupPeriodStart
            RegisterEventHandler<EventWarmupPeriodStart>((@event, info) => {
                _warmupMessageSent = false;
                return HookResult.Continue;
            });

            // 換地圖或地圖重載時也重置旗標
            RegisterEventHandler<EventMapTransition>((@event, info) => {
                _warmupMessageSent = false;
                return HookResult.Continue;
            });
        }

        private void LoadConfig()
        {
            var path = Path.Combine(ModuleDirectory, "config.cfg");

            if (!File.Exists(path))
            {
                File.WriteAllText(path,
                    "allowed_weapons = weapon_deagle, weapon_knife\n");
            }

            var lines = File.ReadAllLines(path);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                if (!line.StartsWith("allowed_weapons"))
                    continue;

                var parts = line.Split('=');
                if (parts.Length < 2)
                    continue;

                AllowedWeapons = parts[1]
                    .Split(',')
                    .Select(w => w.Trim())
                    .Where(w => !string.IsNullOrEmpty(w))
                    .ToHashSet();
            }

            Logger.LogInformation(
                $"[Warmup_deagle] Allowed weapons loaded: {string.Join(", ", AllowedWeapons)}"
            );
        }

        private static bool IsWarmupActive()
        {
            var gameRulesEnt = Utilities
                .FindAllEntitiesByDesignerName<CBaseEntity>("cs_gamerules")
                .SingleOrDefault();

            if (gameRulesEnt == null)
                return false;

            var proxy = gameRulesEnt.As<CCSGameRulesProxy>();
            return proxy?.GameRules?.WarmupPeriod == true;
        }

        private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
        {
            if (!IsWarmupActive())
            {
                return HookResult.Continue;
            }

            if (_warmupMessageSent)
                return HookResult.Continue;

            Server.PrintToChatAll($"[ {ChatColors.Green}熱身模式{ChatColors.Default} ] 現 在 處 於 {ChatColors.Lime}熱 身 緩 場 {ChatColors.Default} 換 槍 需 打 指 指令");
            _warmupMessageSent = true; 
            return HookResult.Continue;
        }

        private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
        {
            if (!IsWarmupActive())
                return HookResult.Continue;

            var player = @event.Userid;
            if (player == null || !player.IsValid)
                return HookResult.Continue;

            Server.NextFrame(() =>
            {
                var pawn = player.PlayerPawn?.Value;
                if (pawn == null)
                    return;

                if ((LifeState_t)pawn.LifeState != LifeState_t.LIFE_ALIVE)
                    return;

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
            if (weaponServices?.MyWeapons == null)
                return;

            foreach (var weapon in weaponServices.MyWeapons)
            {
                if (weapon?.IsValid != true || weapon.Value == null)
                    continue;

                var name = weapon.Value.DesignerName;

                if (!AllowedWeapons.Contains(name))
                {
                    weapon.Value.AddEntityIOEvent(
                        "Kill",
                        weapon.Value,
                        null,
                        "",
                        0.0f
                    );
                }
            }
        }
    }
}
