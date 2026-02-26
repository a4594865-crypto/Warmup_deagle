using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace deagle_only
{
    public class deagle_only : BasePlugin
    {
        public override string ModuleAuthor => "GSM-RO";
        public override string ModuleName => "Warmup_deagle";
        public override string ModuleVersion => "1.0.3"; // 版本號微調
        public override string ModuleDescription => "Warmup Deagle Only - Compatible Version";

        private bool _warmupMessageSent = false;
        private static HashSet<string> AllowedWeapons = new();

        public override void Load(bool hotReload)
        {
            LoadConfig();

            // 註冊事件：玩家出生與回合開始
            RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
            RegisterEventHandler<EventRoundStart>(OnRoundStart);
            
            // 已移除 RegisterListener<Listeners.OnTick>(OnTick);
            // 這樣就不會每幀強制刪除玩家身上的其他武器，增加與其他插件的相容性。
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
                _warmupMessageSent = false;
                return HookResult.Continue;
            }

            if (_warmupMessageSent)
                return HookResult.Continue;

            Server.PrintToChatAll($" {ChatColors.Green}[ 熱身模式 ]{ChatColors.Default} 現在處於{ChatColors.Red}熱身緩場{ChatColors.Default}，換槍需打指令");
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

            // 使用 NextFrame 確保在引擎處理完預設出生邏輯後再執行
            Server.NextFrame(() =>
            {
                var pawn = player.PlayerPawn?.Value;
                if (pawn == null)
                    return;

                if ((LifeState_t)pawn.LifeState != LifeState_t.LIFE_ALIVE)
                    return;

                // 先移除身上非許可的武器
                RemoveNonAllowedWeapons(player);

                // 給予配置中允許的武器
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

                // 如果是設定檔中「不允許」的武器，才將其移除
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
