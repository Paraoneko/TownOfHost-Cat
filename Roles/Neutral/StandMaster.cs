using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using UnityEngine;
using HarmonyLib;

namespace TownOfHost.Roles.Neutral;

public sealed class StandMaster : RoleBase, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(StandMaster),
            player => new StandMaster(player),
            CustomRoles.StandMaster,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Neutral,
            184710,
            SetupOptionItem,
            "stm",
            "#8B4513",
            (7, 3),
            true,
            countType: CountTypes.StandMaster
        );

    public StandMaster(PlayerControl player)
        : base(RoleInfo, player, () => HasTask.False)
    {
        SummonCooldown = OptionSummonCooldown.GetFloat();

        standPlayerId = byte.MaxValue;
        standReturnPos = null;
        standSummoned = false;
        standCreated = false;
        isRevealed = false;
    }

    static OptionItem OptionSummonCooldown;
    static float SummonCooldown;

    enum OptionName
    {
        StandMasterSummonCooldown,
    }

    public byte standPlayerId;
    public Vector2? standReturnPos;
    public bool standSummoned;
    public bool standCreated;
    public bool isRevealed;

    static void SetupOptionItem()
    {
        SoloWinOption.Create(RoleInfo, 9, defo: 1);
        OptionSummonCooldown = FloatOptionItem.Create(RoleInfo, 10, OptionName.StandMasterSummonCooldown,
            new(2.5f, 60f, 2.5f), 20f, false).SetValueFormat(OptionFormat.Seconds);

        HideRoleOptions(CustomRoles.Stand);
    }

    public static void HideRoleOptions(CustomRoles role)
    {
        if (Options.CustomRoleSpawnChances != null && Options.CustomRoleSpawnChances.TryGetValue(role, out var spawnOption))
            spawnOption.SetHidden(true);
        if (Options.CustomRoleCounts != null && Options.CustomRoleCounts.TryGetValue(role, out var countOption))
            countOption.SetHidden(true);
    }

    public PlayerControl GetStand() =>
        standPlayerId == byte.MaxValue ? null : PlayerCatch.GetPlayerById(standPlayerId);

    public bool IsPhantomRole => true;
    public bool IsresetAfterKill => false;

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = SummonCooldown;
    }

    public void SyncState()
    {
        SendRpc();
        UtilsNotifyRoles.NotifyRoles();
    }

    public override void OverrideDisplayRoleNameAsSeer(PlayerControl seen, ref bool enabled, ref Color roleColor, ref string roleText, ref bool addon)
    {
        addon = false;
        if (standPlayerId != byte.MaxValue && seen.PlayerId == standPlayerId)
            enabled = true;
    }

    public override void OverrideDisplayRoleNameAsSeen(PlayerControl seer, ref bool enabled, ref Color roleColor, ref string roleText, ref bool addon)
    {
        addon = false;
        if (standPlayerId != byte.MaxValue && seer.PlayerId == standPlayerId)
            enabled = true;
    }

    public override string MeetingAddMessage()
    {
        if (Player.IsAlive() && standCreated && isRevealed)
        {
            var stand = GetStand();
            if (stand != null && stand.IsAlive())
            {
                string title = "<size=90%><color=#8B4513>【====== スタンド情報 ======】</color></size>";
                string standName = UtilsName.GetPlayerColor(stand, true);
                string msg = $"<color=#8B4513>{standName} はスタンドです。\nスタンドマスターが生きている限り死亡しません。</color>";
                return title + "\n<size=70%>" + msg + "</size>\n";
            }
        }
        return "";
    }

    public override bool OnCheckMurderAsTarget(MurderInfo info)
    {
        (var killer, var target) = info.AttemptTuple;
        if (killer.GetRoleClass() is Stand stand && stand.OwnerId == Player.PlayerId)
            return false;
        return true;
    }

    public void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = false;
        ResetCooldown = false;

        if (!Player.IsAlive()) return;
        if (!standCreated) return;

        var stand = GetStand();
        if (stand == null || !stand.IsAlive()) return;
        if (stand.inVent || stand.walkingToVent) return;
        if (stand.MyPhysics.Animations.IsPlayingAnyLadderAnimation()) return;
        if (stand.onLadder) return;
        if (stand.inMovingPlat) return;
        if ((MapNames)Main.NormalOptions.MapId == MapNames.Airship &&
            Vector2.Distance(stand.GetTruePosition(), new Vector2(7.76f, 8.56f)) <= 1.9f) return;

        if (standSummoned) return;

        standReturnPos = stand.transform.position;
        standSummoned = true;

        {
            var writer = AmongUsClient.Instance.StartRpcImmediately(
                stand.NetId, (byte)RpcCalls.SetRole, SendOption.Reliable, stand.OwnerId);
            writer.Write((ushort)RoleTypes.Impostor);
            writer.Write(true);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }

        stand.MarkDirtySettings();
        stand.RpcSnapToForced(Player.transform.position);
        try { stand.SetKillTimer(0f); } catch { }

        SyncState();

        _ = new LateTask(() =>
        {
            if (!Player.IsAlive()) return;
            AURoleOptions.PhantomCooldown = SummonCooldown;
            Player.RpcResetAbilityCooldown();
        }, 0.1f, "StandMaster.ResetCD", true);
    }

    public void ReturnStand()
    {
        var stand = GetStand();
        if (stand == null) return;

        if (standReturnPos.HasValue)
            stand.RpcSnapToForced(standReturnPos.Value);

        {
            var writer = AmongUsClient.Instance.StartRpcImmediately(
                stand.NetId, (byte)RpcCalls.SetRole, SendOption.Reliable, stand.OwnerId);
            writer.Write((ushort)RoleTypes.Crewmate);
            writer.Write(true);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }

        stand.MarkDirtySettings();

        standSummoned = false;
        standReturnPos = null;

        SyncState();
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!standSummoned) return;
        ReturnStand();
    }

    public override void OnStartMeeting()
    {
        standSummoned = false;
    }

    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!standCreated) return;

        var stand = GetStand();
        if (stand == null || !stand.IsAlive())
        {
            standCreated = false;
            standPlayerId = byte.MaxValue;
            isRevealed = false;
            SendRpc();
            return;
        }

        {
            var writer = AmongUsClient.Instance.StartRpcImmediately(
                stand.NetId, (byte)RpcCalls.SetRole, SendOption.Reliable, stand.OwnerId);
            writer.Write((ushort)RoleTypes.Crewmate);
            writer.Write(true);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }

        stand.MarkDirtySettings();
        standSummoned = false;
        SendRpc();

        _ = new LateTask(() =>
        {
            if (!Player.IsAlive()) return;
            AURoleOptions.PhantomCooldown = SummonCooldown;
            Player.MarkDirtySettings();
            Player.RpcResetAbilityCooldown(Sync: true);
        }, Main.LagTime + 0.1f, "StandMaster.AfterMeetingPhantomCD", true);
    }


    void SendRpc()
    {
        using var sender = CreateSender();
        sender.Writer.Write(standPlayerId);
        sender.Writer.Write(standCreated);
        sender.Writer.Write(standSummoned);
        sender.Writer.Write(isRevealed);
        sender.Writer.Write(standReturnPos.HasValue);
        if (standReturnPos.HasValue)
        {
            sender.Writer.Write(standReturnPos.Value.x);
            sender.Writer.Write(standReturnPos.Value.y);
        }
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        standPlayerId = reader.ReadByte();
        standCreated = reader.ReadBoolean();
        standSummoned = reader.ReadBoolean();
        isRevealed = reader.ReadBoolean();
        bool hasPos = reader.ReadBoolean();
        standReturnPos = hasPos
            ? new Vector2(reader.ReadSingle(), reader.ReadSingle())
            : null;
    }

    public float CalculateKillCooldown() => 999f;
}

public sealed class Stand : RoleBase, ILNKiller, IAdditionalWinner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Stand),
            player => new Stand(player),
            CustomRoles.Stand,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            184700,
            SetupOptionItem,
            "st",
            "#8B4513",
            (7, 3),
            isDesyncImpostor: true,
            countType: CountTypes.StandMaster
        );

    public Stand(PlayerControl player) : base(RoleInfo, player, () => HasTask.False)
    {
        OwnerId = byte.MaxValue;
    }

    static void SetupOptionItem() { }

    public byte OwnerId;

    public void SetOwner(byte ownerId)
    {
        OwnerId = ownerId;
        SendRPC();
    }

    public StandMaster GetOwner()
    {
        if (OwnerId == byte.MaxValue) return null;
        return PlayerCatch.GetPlayerById(OwnerId)?.GetRoleClass() as StandMaster;
    }

    public override void OverrideDisplayRoleNameAsSeer(PlayerControl seen, ref bool enabled, ref Color roleColor, ref string roleText, ref bool addon)
    {
        addon = false;
        if (OwnerId != byte.MaxValue && seen.PlayerId == OwnerId)
            enabled = true;
    }

    public override void OverrideDisplayRoleNameAsSeen(PlayerControl seer, ref bool enabled, ref Color roleColor, ref string roleText, ref bool addon)
    {
        addon = false;
        if (OwnerId != byte.MaxValue && seer.PlayerId == OwnerId)
            enabled = true;
    }

    public override bool OnCheckMurderAsTarget(MurderInfo info)
    {
        var sm = GetOwner();
        if (sm == null || !sm.Player.IsAlive()) return true;

        (var killer, var target) = info.AttemptTuple;

        if (killer.PlayerId == target.PlayerId) return true;

        // ★ 召喚中はキル可能（ガードしない）
        if (sm.standSummoned) return true;

        // ★ 待機中はガードして告知
        killer.RpcProtectedMurderPlayer(target);
        info.GuardPower = 1;

        if (!sm.isRevealed)
        {
            sm.isRevealed = true;
            sm.SyncState();
        }

        return true;
    }

    public override bool VotingResults(ref NetworkedPlayerInfo Exiled, ref bool IsTie, Dictionary<byte, int> vote, byte[] mostVotedPlayers, bool ClearAndExile)
    {
        if (!AmongUsClient.Instance.AmHost) return false;
        if (Exiled == null) return false;

        if (Exiled.PlayerId != Player.PlayerId) return false;
        if (!Player.IsAlive()) return false;

        var sm = GetOwner();
        if (sm == null || !sm.Player.IsAlive()) return false;

        Exiled = null;
        IsTie = false;

        if (!sm.isRevealed)
        {
            sm.isRevealed = true;
            sm.SyncState();
        }

        return true;
    }

    public float CalculateKillCooldown()
    {
        var sm = GetOwner();
        if (sm != null && sm.standSummoned) return 0.1f;
        return 999f;
    }

    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => false;

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (player != Player) return;
        if (!Player.IsAlive()) return;

        if (ShouldFollowOwnerDeath())
        {
            FollowOwnerDeath();
            return;
        }
    }

    bool ShouldFollowOwnerDeath()
    {
        if (OwnerId == byte.MaxValue) return false;

        var owner = PlayerCatch.GetPlayerById(OwnerId);
        if (owner == null) return true;
        if (!owner.IsAlive()) return true;
        if (owner.GetRoleClass() is not StandMaster) return true;

        return false;
    }

    void FollowOwnerDeath()
    {
        if (!Player.IsAlive()) return;

        var state = PlayerState.GetByPlayerId(Player.PlayerId);
        if (state != null)
            state.DeathReason = CustomDeathReason.FollowingSuicide;

        var owner = OwnerId == byte.MaxValue ? null : PlayerCatch.GetPlayerById(OwnerId);

        Player.SetRealKiller(owner ?? Player);
        Player.RpcMurderPlayerV2(Player);

        UtilsGameLog.AddGameLog("StandMaster",
            $"{UtilsName.GetPlayerColor(Player)} がスタンドマスターの死亡により道連れになった");
    }

    public bool CheckWin(ref CustomRoles winnerRole)
    {
        if (CustomWinnerHolder.WinnerTeam is not CustomWinner.StandMaster) return false;
        winnerRole = Player.GetCustomRole();
        return true;
    }

    void SendRPC()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        using var sender = CreateSender();
        sender.Writer.Write(OwnerId);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        OwnerId = reader.ReadByte();
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.MurderPlayer))]
public static class StandMasterMurderPatch
{
    public static bool Prefix(PlayerControl __instance, PlayerControl target)
    {
        if (!AmongUsClient.Instance.AmHost) return true;
        if (__instance.PlayerId == target.PlayerId) return true;

        var role = __instance.GetRoleClass();

        if (role is StandMaster standMaster)
        {
            if (!standMaster.standCreated)
            {
                standMaster.standCreated = true;
                standMaster.standPlayerId = target.PlayerId;
                standMaster.standSummoned = false;
                standMaster.standReturnPos = null;
                standMaster.isRevealed = false;

                if (!Utils.RoleSendList.Contains(target.PlayerId))
                    Utils.RoleSendList.Add(target.PlayerId);

                target.RpcSetCustomRole(CustomRoles.Stand, log: null);
                target.RpcSetRole(RoleTypes.Crewmate);

                _ = new LateTask(() =>
                {
                    if (target.GetRoleClass() is Stand stand)
                        stand.SetOwner(standMaster.Player.PlayerId);
                }, 0.2f, "StandMaster.SetOwner", true);

                target.MarkDirtySettings();
                standMaster.SyncState();

                UtilsGameLog.AddGameLog("StandMaster",
                    $"{UtilsName.GetPlayerColor(standMaster.Player)} が {UtilsName.GetPlayerColor(target)} をスタンドにした");

                _ = new LateTask(() =>
                {
                    if (standMaster.Player.IsAlive())
                        standMaster.Player.RpcResetAbilityCooldown();
                }, 0.1f, "StandMaster.ResetPhantomCD", true);

                return false;
            }
        }
        else if (role is Stand standPlayer)
        {
            var sm = standPlayer.GetOwner();
            if (sm != null)
            {
                // ★ スタンドがスタンドマスターをキルしようとしてもキャンセル
                if (target.PlayerId == sm.Player.PlayerId)
                    return false;

                if (sm.standSummoned)
                    _ = new LateTask(() => sm.ReturnStand(), 0.3f, "StandMaster.ReturnAfterKill", true);
            }
        }

        return true;
    }
}