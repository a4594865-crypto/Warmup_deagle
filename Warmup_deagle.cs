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
        public override string ModuleVersion => "1.0.4"; // 版本號微調
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
            // 🎯 最關鍵改動：新回合一開始，直接無條件解鎖！
            // 這樣不管是伺服器剛開、換地圖，還是中途指令重置暖場，開關都會被精準擦拭成 false
            _warmupMessageSent = false;

            // 如果現在「不是」暖場，直接跳出，絕對不印訊息
            if (!IsWarmupActive())
            {
                return HookResult.Continue;
            }

            // 如果這一局已經印過了，直接跳出 (因為最上面設為了 false，所以暖場第一回合這裡一定能通過)
            if (_warmupMessageSent)
                return HookResult.Continue;

            // 當玩家進入遊戲、重置暖場的第一局，完美印出這行
            Server.PrintToChatAll($"[ {ChatColors.Green}熱身模式{ChatColors.Default} ] 現 在 處 於 {ChatColors.Lime}熱 身 緩 場 {ChatColors.Default} 換 槍 需 打 指 令");
            
            // 印完立刻上鎖，保證在「這一局暖場內」玩家因為自殺、時間到而重新復活時，不會重複刷屏
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
