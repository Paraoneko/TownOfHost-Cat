/*using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using static TownOfHost.PlayerCatch;
using static TownOfHost.Translator;

namespace TownOfHost.Roles.Impostor;

/// <summary>
/// イビルスタンドマスター
/// ファントムボタンでランダムなインポスター相方を自分の位置にワープさせる。
/// ワープ成功時: 相方のキルCDを設定秒数短縮（オプション）。
/// ワープ不可時（全員ベント/移動中/死亡）: 自分のキルCDを短縮（オプション）。
/// ワープ滞在時間: ワープしてきた相方を指定秒数その場に固定する。
/// </summary>
public sealed class EvilStandMaster : RoleBase, IImpostor, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(EvilStandMaster),
            player => new EvilStandMaster(player),
            CustomRoles.EvilStandMaster,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            327000,
            SetupOptionItem,
            "esm",
            OptionSort: (3, 11),
            from: From.TownOfHost_Pko
        );

    public EvilStandMaster(PlayerControl player) : base(RoleInfo, player) { }

    static OptionItem OptionWarpCooldown;
    static OptionItem OptionWarpStayDuration;
    static OptionItem OptionReduceTeammateKillCD;
    static OptionItem OptionTeammateKillCDReduce;
    static OptionItem OptionReduceOwnKillCD;
    static OptionItem OptionOwnKillCDReduce;

    static float WarpCooldown;
    static float WarpStayDuration;
    static bool ReduceTeammateKillCD;
    static float TeammateKillCDReduce;
    static bool ReduceOwnKillCD;
    static float OwnKillCDReduce;

    enum OptionName
    {
        EvilStandMasterWarpCooldown,
        EvilStandMasterWarpStayDuration,
        EvilStandMasterReduceTeammateKillCD,
        EvilStandMasterTeammateKillCDReduce,
        EvilStandMasterReduceOwnKillCD,
        EvilStandMasterOwnKillCDReduce,
    }

    static void SetupOptionItem()
    {
        OptionWarpCooldown = FloatOptionItem.Create(RoleInfo, 10, OptionName.EvilStandMasterWarpCooldown,
            new(1f, 180f, 1f), 30f, false).SetValueFormat(OptionFormat.Seconds);

        OptionWarpStayDuration = FloatOptionItem.Create(RoleInfo, 11, OptionName.EvilStandMasterWarpStayDuration,
            new(0f, 30f, 0.5f), 5f, false).SetValueFormat(OptionFormat.Seconds);

        OptionReduceTeammateKillCD = BooleanOptionItem.Create(RoleInfo, 12,
            OptionName.EvilStandMasterReduceTeammateKillCD, true, false);
        OptionTeammateKillCDReduce = FloatOptionItem.Create(RoleInfo, 13,
            OptionName.EvilStandMasterTeammateKillCDReduce,
            new(0f, 60f, 0.5f), 10f, false, OptionReduceTeammateKillCD).SetValueFormat(OptionFormat.Seconds);

        OptionReduceOwnKillCD = BooleanOptionItem.Create(RoleInfo, 14,
            OptionName.EvilStandMasterReduceOwnKillCD, true, false);
        OptionOwnKillCDReduce = FloatOptionItem.Create(RoleInfo, 15,
            OptionName.EvilStandMasterOwnKillCDReduce,
            new(0f, 60f, 0.5f), 5f, false, OptionReduceOwnKillCD).SetValueFormat(OptionFormat.Seconds);
    }

    // ─── 初期化 ──────────────────────────────────────────────────
    public override void Add()
    {
        WarpCooldown = OptionWarpCooldown.GetFloat();
        WarpStayDuration = OptionWarpStayDuration.GetFloat();
        ReduceTeammateKillCD = OptionReduceTeammateKillCD.GetBool();
        TeammateKillCDReduce = OptionTeammateKillCDReduce.GetFloat();
        ReduceOwnKillCD = OptionReduceOwnKillCD.GetBool();
        OwnKillCDReduce = OptionOwnKillCDReduce.GetFloat();
    }

    public float CalculateKillCooldown() => 30f;
    public bool CanUseKillButton() => Player.IsAlive();
    public bool CanUseSabotageButton() => true;
    public bool CanUseImpostorVentButton() => true;

    // ─── ファントムCD設定 ─────────────────────────────────────────
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = WarpCooldown;
    }

    // ─── ファントムボタン ─────────────────────────────────────────
    public void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = false;
        ResetCooldown = false;
        if (!Player.IsAlive()) return;

        var candidates = GetWarpCandidates();

        if (candidates.Count == 0)
        {
            // 全員ベント/移動中/死亡 → 自分のキルCDを短縮
            if (ReduceOwnKillCD && OwnKillCDReduce > 0f)
            {
                float newCd = Mathf.Max(0.1f, Player.killTimer - OwnKillCDReduce);
                Player.SetKillCooldown(newCd);
                Logger.Info($"[EvilStandMaster] ワープ不可→自分のキルCD {OwnKillCDReduce}秒短縮", "EvilStandMaster");
            }
            SendMessage(GetString("EvilStandMasterNoTarget"), Player.PlayerId);
            return;
        }

        // ランダムに1人選んで自分の位置にワープ
        var target = candidates[IRandom.Instance.Next(candidates.Count)];
        var pos = Player.GetTruePosition();

        target.RpcSnapToForced(pos);
        Logger.Info($"[EvilStandMaster] {target.Data?.GetLogPlayerName()} を {pos} にワープ", "EvilStandMaster");

        // ワープ滞在時間: 対象をその場に固定
        if (WarpStayDuration > 0f)
        {
            var origSpeed = Main.AllPlayerSpeed.TryGetValue(target.PlayerId, out var s)
                ? s : Main.NormalOptions.PlayerSpeedMod;

            Main.AllPlayerSpeed[target.PlayerId] = Main.MinSpeed;
            target.MarkDirtySettings();

            _ = new LateTask(() =>
            {
                if (!target.IsAlive()) return;
                Main.AllPlayerSpeed[target.PlayerId] = origSpeed;
                target.MarkDirtySettings();
            }, WarpStayDuration, $"EvilStandMaster.Unfreeze.{target.PlayerId}", true);
        }

        // ワープ成功時: 相方のキルCDを短縮
        if (ReduceTeammateKillCD && TeammateKillCDReduce > 0f)
        {
            float newCd = Mathf.Max(0.1f, target.killTimer - TeammateKillCDReduce);
            target.SetKillCooldown(newCd);
            Logger.Info($"[EvilStandMaster] {target.Data?.GetLogPlayerName()} のキルCD {TeammateKillCDReduce}秒短縮", "EvilStandMaster");
        }

        SendMessage(
            string.Format(GetString("EvilStandMasterWarped"), target.Data?.PlayerName ?? "???"),
            Player.PlayerId);
        UtilsNotifyRoles.NotifyRoles();
    }

    // ─── ワープ候補取得 ──────────────────────────────────────────
    // 自分以外の生存インポスター陣営で、ベント/ジップライン/梯子不使用のもの
    private List<PlayerControl> GetWarpCandidates()
    {
        return AllAlivePlayerControls
            .Where(pc =>
                pc.PlayerId != Player.PlayerId &&
                pc.GetCustomRole().IsImpostor() &&
                !pc.inVent &&
                !pc.inMovingPlat &&      // ジップライン/移動プラットフォーム
                !pc.walkingToVent &&
                !pc.onLadder &&
                !pc.MyPhysics.Animations.IsPlayingEnterVentAnimation() &&
                !pc.MyPhysics.Animations.IsPlayingAnyLadderAnimation())
            .ToList();
    }

    bool IUsePhantomButton.IsresetAfterKill => false;
    bool IUsePhantomButton.UseOneclickButton => true;

    public override bool OverrideAbilityButton(out string text)
    {
        text = "EvilStandMaster_Warp";
        return true;
    }

    public override string GetAbilityButtonText() => GetString("EvilStandMasterButtonText");

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive() || isForMeeting) return "";

        var count = GetWarpCandidates().Count;
        string size = isForHud ? "" : "<size=60%>";
        string color = RoleInfo.RoleColorCode;

        return count > 0
            ? $"{size}<color={color}>ワープ対象: {count}人</color>"
            : $"{size}<color=#888888>ワープ対象なし（キルCD短縮待機中）</color>";
    }
}
*/