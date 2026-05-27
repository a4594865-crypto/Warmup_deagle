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
        public override string ModuleVersion => "1.0.6";
        public override string ModuleDescription => "Warmup Deagle Only + First Join IME Fix";

        private static HashSet<string> AllowedWeapons = new();

        // 記錄哪些玩家「剛進服」需要被解卡（剩餘的 Tick 數）
        private readonly Dictionary<int, int> _ticksToFix = new();

        // 🔥 修正：改用 ulong (SteamID) 儲存，避免 AuthorizedID 的語法不相容問題
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

            // 🔥 修正：使用全版本通用的 HookUserCmd 來代替 RegisterOnPlayerRunCmd
            // 這會在伺服器處理玩家鍵盤輸入時觸發
            RegisterListener<Listeners.OnTick>(() =>
            {
                foreach (var player in Utilities.GetPlayers())
                {
                    if (player == null || !player.IsValid || player.IsBot) continue;

                    if (_ticksToFix.TryGetValue(player.Slot, out int ticksLeft) && ticksLeft > 0)
                    {
                        // 取得玩家當前的按鍵狀態並強行加上 WALK (SHIFT)
                        var pawn = player.PlayerPawn?.Value;
                        if (pawn != null && pawn.MovementServices != null)
                        {
                            // 修正：在 OnTick 中直接修改玩家 Pawn 的 Buttons 訊號
                            player.Buttons |= PlayerButtons.Walk;
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

            // 🔥 修正：直接拿 player.SteamID，這在所有 CSS 版本都存在且不封裝
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
            // 🔥 修正：改回您原本正確的 player.PlayerPawn?.Value?.WeaponServices 路徑
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
