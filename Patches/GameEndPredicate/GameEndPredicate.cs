using System.Linq;
using HarmonyLib;

using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Neutral;

namespace TownOfHost;

public abstract class GameEndPredicate
{
    /// <summary>繧ｲ繝ｼ繝縺ｮ邨ゆｺ・擅莉ｶ繧偵メ繧ｧ繝・け縺励，ustomWinnerHolder縺ｫ蛟､繧呈ｼ邏阪＠縺ｾ縺吶・/summary>
    /// <params name="reason">繝舌ル繝ｩ縺ｮ繧ｲ繝ｼ繝邨ゆｺ・・逅・↓菴ｿ逕ｨ縺吶ｋGameOverReason</params>
    /// <returns>繧ｲ繝ｼ繝邨ゆｺ・・譚｡莉ｶ繧呈ｺ縺溘＠縺ｦ縺・ｋ縺九←縺・°</returns>
    public abstract bool CheckForEndGame(out GameOverReason reason);

    /// <summary>GameData.TotalTasks縺ｨCompletedTasks繧偵ｂ縺ｨ縺ｫ繧ｿ繧ｹ繧ｯ蜍晏茜縺悟庄閭ｽ縺九ｒ蛻､螳壹＠縺ｾ縺吶・/summary>
    public virtual bool CheckGameEndByTask(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;
        if (Options.DisableTaskWin.GetBool() || TaskState.InitialTotalTasks == 0 || Fox.BlockTaskWin()) return false;

        if (GameData.Instance.TotalTasks <= GameData.Instance.CompletedTasks)
        {
            reason = GameOverReason.CrewmatesByTask;
            CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Crewmate, byte.MaxValue);
            return true;
        }
        return false;
    }
    /// <summary>ShipStatus.Systems蜀・・隕∫ｴ繧偵ｂ縺ｨ縺ｫ繧ｵ繝懊ち繝ｼ繧ｸ繝･蜍晏茜縺悟庄閭ｽ縺九ｒ蛻､螳壹＠縺ｾ縺吶・/summary>
    public virtual bool CheckGameEndBySabotage(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;
        if (ShipStatus.Instance.Systems == null) return false;
        if (GameStates.IsMeeting) return false;

        // TryGetValue縺ｯ菴ｿ逕ｨ荳榊庄
        var systems = ShipStatus.Instance.Systems;
        LifeSuppSystemType LifeSupp;
        if (systems.ContainsKey(SystemTypes.LifeSupp) && // 繧ｵ繝懊ち繝ｼ繧ｸ繝･蟄伜惠遒ｺ隱・
            (LifeSupp = systems[SystemTypes.LifeSupp].TryCast<LifeSuppSystemType>()) != null && // 繧ｭ繝｣繧ｹ繝亥庄閭ｽ遒ｺ隱・
            LifeSupp.Countdown < 0f) // 繧ｿ繧､繝繧｢繝・・遒ｺ隱・
        {
            // 驟ｸ邏繧ｵ繝懊ち繝ｼ繧ｸ繝･
            if (Options.ChangeSabotageWinRole.GetBool())
            {
                var pc = PlayerCatch.GetPlayerById(Main.LastSab);
                var role = pc.GetCustomRole();

                switch (role)
                {
                    case CustomRoles.Jackal:
                    case CustomRoles.JackalMafia:
                    case CustomRoles.JackalAlien:
                    case CustomRoles.JackalHadouHo:
                    case CustomRoles.JackalWolf:
                        CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Jackal, byte.MaxValue);
                        CustomWinnerHolder.WinnerRoles.Add(CustomRoles.Jackal);
                        CustomWinnerHolder.WinnerRoles.Add(CustomRoles.JackalMafia);
                        CustomWinnerHolder.WinnerRoles.Add(CustomRoles.JackalAlien);
                        CustomWinnerHolder.WinnerRoles.Add(CustomRoles.Jackaldoll);
                        CustomWinnerHolder.WinnerRoles.Add(CustomRoles.JackalHadouHo);
                        CustomWinnerHolder.WinnerRoles.Add(CustomRoles.Tama);
                        CustomWinnerHolder.WinnerRoles.Add(CustomRoles.JackalWolf);
                        if (Options.OptionSabotageFinAllKill.GetBool())
                            PlayerCatch.AllAlivePlayerControls.Do(pc =>
                            {
                                if (pc.Is(CountTypes.Jackal) is false)
                                {
                                    pc.GetPlayerState().DeathReason = CustomDeathReason.Kill;
                                    pc.RpcMurderPlayer(pc);
                                }
                            });
                        break;
                    case CustomRoles.GrimReaper:
                        CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.GrimReaper, byte.MaxValue);
                        CustomWinnerHolder.WinnerRoles.Add(CustomRoles.GrimReaper);
                        CustomWinnerHolder.NeutralWinnerIds.Add(PlayerCatch.AllPlayerControls.FirstOrDefault(pc => pc.GetCustomRole() is CustomRoles.GrimReaper)?.PlayerId ?? byte.MaxValue);
                        if (Options.OptionSabotageFinAllKill.GetBool())
                            PlayerCatch.AllAlivePlayerControls.Do(pc =>
                                {
                                    if (pc.Is(CountTypes.GrimReaper) is false)
                                    {
                                        pc.GetPlayerState().DeathReason = CustomDeathReason.Kill;
                                        pc.RpcMurderPlayer(pc);
                                    }
                                });
                        break;
                    case CustomRoles.Egoist:
                        CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Egoist, byte.MaxValue);
                        CustomWinnerHolder.WinnerRoles.Add(CustomRoles.Egoist);
                        CustomWinnerHolder.NeutralWinnerIds.Add(PlayerCatch.AllPlayerControls.FirstOrDefault(pc => pc.GetCustomRole() is CustomRoles.Egoist)?.PlayerId ?? byte.MaxValue);
                        if (Options.OptionSabotageFinAllKill.GetBool())
                            PlayerCatch.AllAlivePlayerControls.Do(pc =>
                            {
                                if (pc.Is(CustomRoles.Egoist) is false)
                                {
                                    pc.GetPlayerState().DeathReason = CustomDeathReason.Kill;
                                    pc.RpcMurderPlayer(pc);
                                }
                            }); break;
                    case CustomRoles.MadBetrayer:
                        CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.MadBetrayer, byte.MaxValue);
                        CustomWinnerHolder.WinnerRoles.Add(CustomRoles.MadBetrayer);
                        CustomWinnerHolder.NeutralWinnerIds.Add(PlayerCatch.AllPlayerControls.FirstOrDefault(pc => pc.GetCustomRole() is CustomRoles.MadBetrayer)?.PlayerId ?? byte.MaxValue);
                        if (Options.OptionSabotageFinAllKill.GetBool())
                            PlayerCatch.AllAlivePlayerControls.Do(pc =>
                            {
                                if (pc.Is(CustomRoles.MadBetrayer) is false)
                                {
                                    pc.GetPlayerState().DeathReason = CustomDeathReason.Kill;
                                    pc.RpcMurderPlayer(pc);
                                }
                            });
                        break;
                    default:
                        CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Impostor, byte.MaxValue);
                        if (Options.OptionSabotageFinAllKill.GetBool())
                            PlayerCatch.AllAlivePlayerControls.Do(pc =>
                            {
                                if (pc.Is(CustomRoleTypes.Impostor) is false)
                                {
                                    pc.GetPlayerState().DeathReason = CustomDeathReason.Kill;
                                    pc.RpcMurderPlayer(pc);
                                }
                            });
                        break;
                }
                reason = GameOverReason.ImpostorsBySabotage;
                Main.IsActiveSabotage = false;
                LifeSupp.Countdown = 10000f;
                return true;
            }
            CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Impostor, byte.MaxValue);
            Main.IsActiveSabotage = false;
            reason = GameOverReason.ImpostorsBySabotage;
            LifeSupp.Countdown = 10000f;
            return true;
        }

        ISystemType sys = null;
        if (systems.ContainsKey(SystemTypes.Reactor)) sys = systems[SystemTypes.Reactor];
        else if (systems.ContainsKey(SystemTypes.Laboratory)) sys = systems[SystemTypes.Laboratory];
        else if (systems.ContainsKey(SystemTypes.HeliSabotage)) sys = systems[SystemTypes.HeliSabotage];
        ICriticalSabotage critical;
        if (sys != null && // 繧ｵ繝懊ち繝ｼ繧ｸ繝･蟄伜惠遒ｺ隱・
            (critical = sys.TryCast<ICriticalSabotage>()) != null && // 繧ｭ繝｣繧ｹ繝亥庄閭ｽ遒ｺ隱・
            critical.Countdown < 0f) // 繧ｿ繧､繝繧｢繝・・遒ｺ隱・
        {
            if (Options.CurrentGameMode is CustomGameMode.SuddenDeath or CustomGameMode.MurderMystery)
            {
                PlayerCatch.AllAlivePlayerControls.Do(p => p.RpcMurderPlayerV2(p));
                CustomWinnerHolder.ResetAndSetWinner(CustomWinner.None);
                Main.IsActiveSabotage = false;
                reason = GameOverReason.ImpostorsBySabotage;
                critical.ClearSabotage();
                return true;
            }
            // 繝ｪ繧｢繧ｯ繧ｿ繝ｼ繧ｵ繝懊ち繝ｼ繧ｸ繝･
            if (Options.ChangeSabotageWinRole.GetBool())
            {
                var pc = PlayerCatch.GetPlayerById(Main.LastSab);
                var role = pc.GetCustomRole();

                switch (role)
                {
                    case CustomRoles.Jackal:
                    case CustomRoles.JackalMafia:
                    case CustomRoles.JackalAlien:
                    case CustomRoles.JackalHadouHo:
                    case CustomRoles.JackalWolf:
                        CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Jackal, byte.MaxValue);
                        CustomWinnerHolder.WinnerRoles.Add(CustomRoles.Jackal);
                        CustomWinnerHolder.WinnerRoles.Add(CustomRoles.JackalMafia);
                        CustomWinnerHolder.WinnerRoles.Add(CustomRoles.JackalAlien);
                        CustomWinnerHolder.WinnerRoles.Add(CustomRoles.Jackaldoll);
                        CustomWinnerHolder.WinnerRoles.Add(CustomRoles.JackalHadouHo);
                        CustomWinnerHolder.WinnerRoles.Add(CustomRoles.Tama);
                        CustomWinnerHolder.WinnerRoles.Add(CustomRoles.JackalWolf);
                        if (Options.OptionSabotageFinAllKill.GetBool())
                            PlayerCatch.AllAlivePlayerControls.Do(pc =>
                            {
                                if (pc.Is(CountTypes.Jackal) is false)
                                {
                                    pc.GetPlayerState().DeathReason = CustomDeathReason.Kill;
                                    pc.RpcMurderPlayer(pc);
                                }
                            });
                        break;
                    case CustomRoles.GrimReaper:
                        CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.GrimReaper, byte.MaxValue);
                        CustomWinnerHolder.WinnerRoles.Add(CustomRoles.GrimReaper);
                        CustomWinnerHolder.NeutralWinnerIds.Add(PlayerCatch.AllPlayerControls.FirstOrDefault(pc => pc.GetCustomRole() is CustomRoles.GrimReaper)?.PlayerId ?? byte.MaxValue);
                        if (Options.OptionSabotageFinAllKill.GetBool())
                            PlayerCatch.AllAlivePlayerControls.Do(pc =>
                            {
                                if (pc.Is(CountTypes.GrimReaper) is false)
                                {
                                    pc.GetPlayerState().DeathReason = CustomDeathReason.Kill;
                                    pc.RpcMurderPlayer(pc);
                                }
                            });
                        break;
                    case CustomRoles.Egoist:
                        CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Egoist, byte.MaxValue);
                        CustomWinnerHolder.WinnerRoles.Add(CustomRoles.Egoist);
                        CustomWinnerHolder.NeutralWinnerIds.Add(PlayerCatch.AllPlayerControls.FirstOrDefault(pc => pc.GetCustomRole() is CustomRoles.Egoist)?.PlayerId ?? byte.MaxValue);
                        if (Options.OptionSabotageFinAllKill.GetBool())
                            PlayerCatch.AllAlivePlayerControls.Do(pc =>
                            {
                                if (pc.Is(CustomRoles.Egoist) is false)
                                {
                                    pc.GetPlayerState().DeathReason = CustomDeathReason.Kill;
                                    pc.RpcMurderPlayer(pc);
                                }
                            });
                        break;
                    case CustomRoles.MadBetrayer:
                        CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.MadBetrayer, byte.MaxValue);
                        CustomWinnerHolder.WinnerRoles.Add(CustomRoles.MadBetrayer);
                        CustomWinnerHolder.NeutralWinnerIds.Add(PlayerCatch.AllPlayerControls.FirstOrDefault(pc => pc.GetCustomRole() is CustomRoles.MadBetrayer)?.PlayerId ?? byte.MaxValue);
                        if (Options.OptionSabotageFinAllKill.GetBool())
                            PlayerCatch.AllAlivePlayerControls.Do(pc =>
                            {
                                if (pc.Is(CustomRoles.MadBetrayer) is false)
                                {
                                    pc.GetPlayerState().DeathReason = CustomDeathReason.Kill;
                                    pc.RpcMurderPlayer(pc);
                                }
                            }); break;
                    default:
                        CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Impostor, byte.MaxValue);
                        if (Options.OptionSabotageFinAllKill.GetBool())
                            PlayerCatch.AllAlivePlayerControls.Do(pc =>
                            {
                                if (pc.Is(CustomRoleTypes.Impostor) is false)
                                {
                                    pc.GetPlayerState().DeathReason = CustomDeathReason.Kill;
                                    pc.RpcMurderPlayer(pc);
                                }
                            });
                        break;
                }
                Main.IsActiveSabotage = false;
                reason = GameOverReason.ImpostorsBySabotage;
                critical.ClearSabotage();
                return true;
            }
            CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Impostor, byte.MaxValue);
            Main.IsActiveSabotage = false;
            reason = GameOverReason.ImpostorsBySabotage;
            critical.ClearSabotage();
            return true;
        }

        return false;
    }
}