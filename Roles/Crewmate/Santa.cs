using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Patches;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using UnityEngine;
using static TownOfHost.Translator;

namespace TownOfHost.Roles.Crewmate;

public sealed class Santa : RoleBase, IKiller
{
    bool IKiller.IsKiller => true;
    bool IKiller.CanKill => true;

    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Santa),
            player => new Santa(player),
            CustomRoles.Santa,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            34600,
            SetupOptionItem,
            "st",
            "#f29c9f",
            (6, 0),
            from: From.SuperNewRoles,
            isDesyncImpostor: true
        );

    public Santa(PlayerControl player)
        : base(RoleInfo, player)
    {
        KillCooldown = OptKillCooldown.GetFloat();
        giftMode = false;
        giftCount = 0;
        tasksCompleted = false;
        nowcool = KillCooldown;
        LastCooltime = (int)nowcool;
    }

    static OptionItem OptKillCooldown;
    static float KillCooldown;
    static OptionItem OptBalancerRate;
    static OptionItem OptSheriffRate;
    static OptionItem OptLighterRate;
    static OptionItem OptUltraStarRate;
    static OptionItem OptExpressRate;
    static OptionItem OptNiceGuesserRate;
    static OptionItem OptGiftLimit;
    static OptionItem OptCanGiftLovers;
    static OptionItem OptCanGiftMadmate;

    bool giftMode;
    int giftCount;
    bool tasksCompleted;
    float nowcool;
    int LastCooltime;

    private static readonly Dictionary<byte, int> RememberedColorByPlayerId = new();

    private enum OptionName
    {
        SantaGiftRateBalancer, SantaGiftRateSheriff,
        SantaGiftRateLighter, SantaGiftRateUltraStar,
        SantaGiftRateExpress, SantaGiftRateNiceGuesser,
        SantaGiftLimit, SantaCanGiftLovers,
        SantaCanGiftMadmate,
    }

    private static void SetupOptionItem()
    {
        OptBalancerRate = IntegerOptionItem.Create(RoleInfo, 11, OptionName.SantaGiftRateBalancer, new(0, 100, 5), 20, false).SetValueFormat(OptionFormat.Percent);
        OptSheriffRate = IntegerOptionItem.Create(RoleInfo, 12, OptionName.SantaGiftRateSheriff, new(0, 100, 5), 20, false).SetValueFormat(OptionFormat.Percent);
        OptLighterRate = IntegerOptionItem.Create(RoleInfo, 13, OptionName.SantaGiftRateLighter, new(0, 100, 5), 20, false).SetValueFormat(OptionFormat.Percent);
        OptUltraStarRate = IntegerOptionItem.Create(RoleInfo, 14, OptionName.SantaGiftRateUltraStar, new(0, 100, 5), 20, false).SetValueFormat(OptionFormat.Percent);
        OptExpressRate = IntegerOptionItem.Create(RoleInfo, 15, OptionName.SantaGiftRateExpress, new(0, 100, 5), 20, false).SetValueFormat(OptionFormat.Percent);
        OptNiceGuesserRate = IntegerOptionItem.Create(RoleInfo, 16, OptionName.SantaGiftRateNiceGuesser, new(0, 100, 5), 20, false).SetValueFormat(OptionFormat.Percent);
        OptKillCooldown = FloatOptionItem.Create(RoleInfo, 10, "SantaKillCooldown", new(0.5f, 60f, 0.5f), 25f, false).SetValueFormat(OptionFormat.Seconds);
        OptGiftLimit = IntegerOptionItem.Create(RoleInfo, 17, OptionName.SantaGiftLimit, new(1, 100, 1), 15, false).SetValueFormat(OptionFormat.Times);
        OptCanGiftLovers = BooleanOptionItem.Create(RoleInfo, 18, OptionName.SantaCanGiftLovers, false, false);
        OptCanGiftMadmate = BooleanOptionItem.Create(RoleInfo, 19, OptionName.SantaCanGiftMadmate, false, false);
        OverrideTasksData.Create(RoleInfo, 20);
    }

    public override void Add()
    {
        giftMode = false;
        giftCount = 0;
        tasksCompleted = false;
        KillCooldown = OptKillCooldown.GetFloat();
        nowcool = KillCooldown;
        LastCooltime = (int)nowcool;
        PetActionManager.Register(Player.PlayerId, OnPetUsed);
    }

    public override void OnDestroy()
    {
        PetActionManager.Unregister(Player.PlayerId);
    }

    private void OnPetUsed()
    {
        if (!Player.IsAlive()) return;
        if (tasksCompleted) return;

        bool switching = !giftMode;

        if (switching)
        {
            giftMode = true;
            ApplyModeDesync(true);
            Player.SetKillCooldown(Mathf.Max(nowcool, 0.1f), delay: true);
        }
        else
        {
            nowcool = Player.killTimer;
            giftMode = false;
            ApplyModeDesync(false);
        }

        LastCooltime = (int)nowcool;
        SendRPC();
        UtilsNotifyRoles.NotifyRoles(OnlyMeName: true, SpecifySeer: Player);
    }

    public override bool OnCompleteTask(uint taskid)
    {
        if (!AmongUsClient.Instance.AmHost) return true;
        if (tasksCompleted) return true;
        if (!MyTaskState.IsTaskFinished) return true;

        tasksCompleted = true;

        if (!giftMode)
        {
            giftMode = true;
            ApplyModeDesync(true);
            Player.SetKillCooldown(Mathf.Max(nowcool, 0.1f), delay: true);
        }

        SendRPC();
        UtilsNotifyRoles.NotifyRoles(OnlyMeName: true, SpecifySeer: Player);
        Utils.SendMessage(
            $"<color={RoleInfo.RoleColorCode}>全タスク完了！ずっとギフトモードになります。</color>",
            Player.PlayerId);
        return true;
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (GameStates.CalledMeeting || GameStates.Intro) return;
        if (!player.IsAlive()) return;

        if (giftMode)
        {
            nowcool = player.killTimer;
        }
        else
        {
            if (nowcool > 0)
                nowcool -= Time.fixedDeltaTime;
            else
                nowcool = 0;
        }

        var now = (int)nowcool;
        if (now == LastCooltime) return;

        LastCooltime = now;

        if (!giftMode && now <= 0)
            player.SetKillCooldown(0.5f, delay: true);

        if (player != PlayerControl.LocalPlayer)
            UtilsNotifyRoles.NotifyRoles(OnlyMeName: true, SpecifySeer: player);
    }

    private void ApplyModeDesync(bool toGiftMode)
    {
        if (Is(PlayerControl.LocalPlayer)) return;
        if (!Player.IsAlive()) return;

        var roleType = toGiftMode ? RoleTypes.Impostor : RoleTypes.Crewmate;
        foreach (var pc in PlayerCatch.AllAlivePlayerControls)
        {
            var role = pc.GetCustomRole();
            if (role.IsImpostor())
                pc.RpcSetRoleDesync(toGiftMode ? RoleTypes.Scientist : role.GetRoleTypes(), Player.GetClientId());
            if (Is(pc))
                pc.RpcSetRoleDesync(roleType, Player.GetClientId());
        }
    }

    private void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(giftMode);
        sender.Writer.Write(giftCount);
        sender.Writer.Write(tasksCompleted);
        sender.Writer.Write(nowcool);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        giftMode = reader.ReadBoolean();
        giftCount = reader.ReadInt32();
        tasksCompleted = reader.ReadBoolean();
        nowcool = reader.ReadSingle();
        LastCooltime = (int)nowcool;
    }

    public float CalculateKillCooldown() => KillCooldown;

    public bool CanUseKillButton()
    {
        if (!Player.IsAlive() || !giftMode) return false;
        var limit = OptGiftLimit?.GetInt() ?? 3;
        if (limit == 0) return true;
        return giftCount < limit;
    }

    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => false;

    public override RoleTypes? AfterMeetingRole
        => giftMode ? RoleTypes.Impostor : RoleTypes.Crewmate;

    public override void AfterMeetingTasks()
    {
        if (!Player.IsAlive()) return;
        _ = new LateTask(() =>
        {
            nowcool = KillCooldown;
            LastCooltime = (int)nowcool;
            ApplyModeDesync(giftMode);
            Player.RpcResetAbilityCooldown();
            SendRPC();
        }, Main.LagTime, "Reset-Santa");
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        opt.SetVision(false);
    }

    public override void ChengeRoleAdd()
    {
        base.ChengeRoleAdd();
        if (giftMode && Player.IsAlive() && AmongUsClient.Instance.AmHost)
            ApplyModeDesync(true);
    }

    private static int GetGiftRate(CustomRoles role) => role switch
    {
        CustomRoles.Balancer => OptBalancerRate?.GetInt() ?? 0,
        CustomRoles.Sheriff => OptSheriffRate?.GetInt() ?? 0,
        CustomRoles.Lighter => OptLighterRate?.GetInt() ?? 0,
        CustomRoles.UltraStar => OptUltraStarRate?.GetInt() ?? 0,
        CustomRoles.Express => OptExpressRate?.GetInt() ?? 0,
        CustomRoles.NiceGuesser => OptNiceGuesserRate?.GetInt() ?? 0,
        _ => 0
    };

    private static CustomRoles RollGiftRole(CustomRoles[] giftRoles)
    {
        var weightedRoles = giftRoles
            .Select(r => (Role: r, Weight: Mathf.Clamp(GetGiftRate(r), 0, 100)))
            .Where(x => x.Weight > 0)
            .ToArray();

        if (weightedRoles.Length == 0)
            return giftRoles[IRandom.Instance.Next(giftRoles.Length)];

        var total = weightedRoles.Sum(x => x.Weight);
        var roll = IRandom.Instance.Next(total);
        var acc = 0;

        foreach (var entry in weightedRoles)
        {
            acc += entry.Weight;
            if (roll < acc) return entry.Role;
        }
        return weightedRoles[^1].Role;
    }

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        var (killer, target) = info.AttemptTuple;
        info.DoKill = false;

        if (target.PlayerId == killer.PlayerId) return;

        var targetRoleType = target.GetCustomRole().GetCustomRoleTypes();
        bool isLovers = target.Is(CustomRoles.Lovers) || target.Is(CustomRoles.MadonnaLovers) || target.Is(CustomRoles.OneLove);
        bool isMadmate = targetRoleType == CustomRoleTypes.Madmate;
        bool isCrew = targetRoleType == CustomRoleTypes.Crewmate;
        bool canGift = isCrew;
        if (!canGift && isLovers && (OptCanGiftLovers?.GetBool() ?? false)) canGift = true;
        if (!canGift && isMadmate && (OptCanGiftMadmate?.GetBool() ?? false)) canGift = true;

        if (!canGift)
        {
            PlayerState.GetByPlayerId(Player.PlayerId).DeathReason = CustomDeathReason.Suicide;
            killer.RpcMurderPlayerV2(killer);
            return;
        }

        var limit = OptGiftLimit?.GetInt() ?? 3;
        if (limit > 0 && giftCount >= limit) return;

        CustomRoles[] giftRoles = { CustomRoles.Balancer, CustomRoles.Sheriff, CustomRoles.Lighter,
                                    CustomRoles.UltraStar, CustomRoles.Express, CustomRoles.NiceGuesser };

        var role = RollGiftRole(giftRoles);
        var beforeRole = target.GetCustomRole();

        if (Walkure.TryRejectRoleChange(Player, target, Walkure.RoleChangeSource.Crewmate)) return;

        if (role == CustomRoles.UltraStar && beforeRole != CustomRoles.UltraStar)
            RememberedColorByPlayerId[target.PlayerId] = target.Data.DefaultOutfit.ColorId;

        bool resetExpressSpeed = beforeRole == CustomRoles.Express && role != CustomRoles.Express;
        if (resetExpressSpeed)
            Main.AllPlayerSpeed[target.PlayerId] = Main.NormalOptions.PlayerSpeedMod;

        if (!Utils.RoleSendList.Contains(target.PlayerId))
            Utils.RoleSendList.Add(target.PlayerId);

        target.RpcSetCustomRole(role, log: null);

        if (beforeRole == CustomRoles.UltraStar && role != CustomRoles.UltraStar &&
            RememberedColorByPlayerId.TryGetValue(target.PlayerId, out var originalColorId))
        {
            target.RpcSetColor((byte)originalColorId);
            RememberedColorByPlayerId.Remove(target.PlayerId);
        }

        if (role == CustomRoles.UltraStar)
        {
            var field = typeof(UltraStar).GetField("CanseeAllplayer", BindingFlags.NonPublic | BindingFlags.Static);
            field?.SetValue(null, true);
        }

        if (resetExpressSpeed) UtilsOption.MarkEveryoneDirtySettings();

        giftCount++;

        nowcool = KillCooldown;
        LastCooltime = (int)nowcool;
        SendRPC();

        killer.ResetKillCooldown();
        killer.SetKillCooldown(KillCooldown);
        killer.RpcResetAbilityCooldown();

        _ = new LateTask(() => UtilsNotifyRoles.NotifyRoles(ForceLoop: true), 0.2f, "Santa Gift");
    }

    public override string GetProgressText(bool comms = false, bool GameLog = false)
    {
        var limit = OptGiftLimit?.GetInt() ?? 3;

        string countText = tasksCompleted
            ? (limit == 0 ? $"({giftCount}) ∞" : $"({giftCount}/{limit}) ∞")
            : (limit == 0 ? $"({giftCount})" : $"({giftCount}/{limit})");

        var progress = $"<color={RoleInfo.RoleColorCode}>{countText}</color>";

        if (!GameStates.CalledMeeting && !GameLog)
        {
            progress += Utils.ColorString(
                giftMode ? new Color(1f, 0.7f, 0.7f) : Color.gray,
                giftMode
                    ? $" [Gift]<color=#ffffff>({LastCooltime})</color>"
                    : $" [Task]<color=#ffffff>({LastCooltime})</color>"
            );
        }

        return progress;
    }

    public bool OverrideKillButtonText(out string text)
    {
        text = "プレゼント";
        return true;
    }

    public bool OverrideKillButton(out string text)
    {
        text = "Santa_Gift";
        return true;
    }
}