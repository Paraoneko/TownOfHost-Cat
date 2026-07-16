using System;
using AmongUs.GameOptions;
using UnityEngine;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Impostor;

public sealed class EvilJester : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(EvilJester),
            player => new EvilJester(player),
            CustomRoles.EvilJester,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            12108,
            SetupOptionItem,
            "ej",
            OptionSort: (2, 13),
            from: From.TownOfHost_Cat
        );
    public EvilJester(PlayerControl player)
        : base(RoleInfo, player)
    {


        KillCount = 0;
    }

    static OptionItem OptionEvillJesterKillCount;
    static OptionItem OptionKillCool;
    static float KillCount;
    static bool WinFlag;
    enum OptionName
    {
        EvillJesterKillCount,
    }
    static void SetupOptionItem()
    {
        SoloWinOption.Create(RoleInfo, 9);
        OptionEvillJesterKillCount = FloatOptionItem.Create(RoleInfo, 10, OptionName.EvillJesterKillCount,
            new(0, 15, 1), 4, false).SetValueFormat(OptionFormat.Times);
        OptionKillCool = FloatOptionItem.Create(RoleInfo, 11, GeneralOption.KillCooldown,
            new(0, 180, 0.5f), 30f, false).SetValueFormat(OptionFormat.Seconds);
    }
    public override string GetProgressText(bool comms = false, bool GameLog = false)
    {
        var color = KillCount >= OptionEvillJesterKillCount.GetFloat()
            ? Color.red
            : Color.white;

        return Utils.ColorString(
            color,
            $"({KillCount}/{OptionEvillJesterKillCount.GetFloat()})"
        );
    }
    public float CalculateKillCooldown() => OptionKillCool.GetFloat();

    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        KillCount++;
        if (!WinFlag)
        {
            if (KillCount >= OptionEvillJesterKillCount.GetFloat())
            {
                WinFlag = true;
            }
        }
        return;
    }
    public override void OnExileWrapUp(NetworkedPlayerInfo exiled, ref bool DecidedWinner)
    {
        if (!AmongUsClient.Instance.AmHost || Player.PlayerId != exiled.PlayerId) return;
        if (OptionEvillJesterKillCount.GetFloat() > 0)
        {
            if (!WinFlag) return;
        }
        if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Impostor, Player.PlayerId, hantrole: CustomRoles.EvilJester))
        {
            DecidedWinner = true;
        }
    }
}
