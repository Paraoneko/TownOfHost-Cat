using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Modules;
using TownOfHost.Patches;
using TownOfHost.Roles.Core;
using UnityEngine;
using static TownOfHost.PlayerCatch;

namespace TownOfHost.Roles.Crewmate;

public sealed class StepDancer : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(StepDancer),
            player => new StepDancer(player),
            CustomRoles.StepDancer,
            () => RoleTypes.Engineer,
            CustomRoleTypes.Crewmate,
            461000,
            SetupOptionItem,
            "sd",
            "#ff88cc",
            (5, 1)
        );

    public StepDancer(PlayerControl player)
        : base(RoleInfo, player)
    {
        Range = OptionRange.GetFloat();
        Duration = OptionDuration.GetFloat();
        Cooldown = OptionCooldown.GetFloat();

        isDancing = false;
        danceTimer = 0f;
        cooldownTimer = Cooldown;
        savedPositions.Clear();

        CustomRoleManager.LowerOthers.Add(GetLowerTextOthers);
    }

    static OptionItem OptionRange;
    static OptionItem OptionDuration;
    static OptionItem OptionCooldown;

    float Range;
    float Duration;
    float Cooldown;

    bool isDancing;
    float danceTimer;
    float cooldownTimer;

    // ★ 対象プレイヤーの保存座標
    readonly Dictionary<byte, Vector2> savedPositions = new();

    enum OptionName
    {
        StepDancerRange,
        StepDancerDuration,
        StepDancerCooldown,
    }

    static void SetupOptionItem()
    {
        OptionRange = FloatOptionItem.Create(RoleInfo, 10, OptionName.StepDancerRange,
            new(1f, 15f, 0.5f), 4f, false).SetValueFormat(OptionFormat.Multiplier);
        OptionDuration = FloatOptionItem.Create(RoleInfo, 11, OptionName.StepDancerDuration,
            new(1f, 15f, 0.5f), 5f, false).SetValueFormat(OptionFormat.Seconds);
        OptionCooldown = FloatOptionItem.Create(RoleInfo, 12, OptionName.StepDancerCooldown,
            new(2.5f, 120f, 2.5f), 30f, false).SetValueFormat(OptionFormat.Seconds);
    }

    public override void Add()
    {
        cooldownTimer = Cooldown;
        PetActionManager.Register(Player.PlayerId, OnPetUsed);
    }

    public override void OnDestroy()
    {
        PetActionManager.Unregister(Player.PlayerId);
        CustomRoleManager.LowerOthers.Remove(GetLowerTextOthers);
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.EngineerCooldown = Mathf.Max(cooldownTimer, 0.1f);
        AURoleOptions.EngineerInVentMaxTime = 0f;
    }

    public override bool CanClickUseVentButton => false;
    public override bool OnEnterVent(PlayerPhysics physics, int ventId) => false;

    // ★ ペット撫で → 座標記録 & ダンス開始（CDが終わっていれば）
    private void OnPetUsed()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!Player.IsAlive()) return;
        if (isDancing) return;
        if (cooldownTimer > 0f) return;

        // ★ 範囲内のプレイヤー座標を記録
        savedPositions.Clear();
        var myPos = (Vector2)Player.transform.position;
        savedPositions[Player.PlayerId] = myPos;

        foreach (var pc in AllAlivePlayerControls)
        {
            if (pc.PlayerId == Player.PlayerId) continue;
            float dist = Vector2.Distance(myPos, (Vector2)pc.transform.position);
            if (dist <= Range)
                savedPositions[pc.PlayerId] = (Vector2)pc.transform.position;
        }

        // ★ ダンス開始
        isDancing = true;
        danceTimer = 0f;

        // ★ 対象プレイヤーのペットアクションを強制発動
        foreach (var pid in savedPositions.Keys)
        {
            if (pid == Player.PlayerId) continue;
            var pc = GetPlayerById(pid);
            if (pc == null || !pc.IsAlive()) continue;
            // ★ TryPet() でペット撫でアニメ＆ペットアクションを強制発動
            pc.TryPet();
        }

        // ★ ベントCDを更新してダンス中は長めのCD表示
        Player.MarkDirtySettings();
        _ = new LateTask(() =>
        {
            if (Player.IsAlive())
                Player.RpcResetAbilityCooldown(Sync: true);
        }, 0.1f, "StepDancer.StartReset", true);

        SendRpc();
        UtilsNotifyRoles.NotifyRoles(OnlyMeName: true, SpecifySeer: Player);
        Logger.Info($"{Player.Data.GetLogPlayerName()} がステップダンス発動 ({savedPositions.Count}人)", "StepDancer");
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        // ★ クールタイムカウント（ダンス中でなければ）
        if (!isDancing && cooldownTimer > 0f)
        {
            cooldownTimer -= Time.fixedDeltaTime;
            if (cooldownTimer < 0f) cooldownTimer = 0f;

            var now = Mathf.FloorToInt(cooldownTimer);
            if (now != Mathf.FloorToInt(cooldownTimer + Time.fixedDeltaTime))
            {
                Player.MarkDirtySettings();
                _ = new LateTask(() =>
                {
                    if (Player.IsAlive() && !isDancing)
                        Player.RpcResetAbilityCooldown(Sync: true);
                }, 0.1f, "StepDancer.CDSync", true);

                if (player != PlayerControl.LocalPlayer)
                    UtilsNotifyRoles.NotifyRoles(OnlyMeName: true, SpecifySeer: player);
            }
        }

        if (!isDancing) return;
        if (!AmongUsClient.Instance.AmHost) return;
        if (!Player.IsAlive()) { StopDance(false); return; }

        danceTimer += Time.fixedDeltaTime;

        // ★ 毎フレーム全対象を保存座標にスナップ
        foreach (var (pid, savedPos) in savedPositions)
        {
            var pc = GetPlayerById(pid);
            if (pc == null || !pc.IsAlive()) continue;
            SnapTo(pc, savedPos);
        }

        if (danceTimer >= Duration)
            StopDance(true);
    }

    private void SnapTo(PlayerControl pc, Vector2 pos)
    {
        try { pc.NetTransform.SnapTo(pos); } catch { }

        ushort sid = (ushort)(pc.NetTransform.lastSequenceId + 2U);
        var writer = AmongUsClient.Instance.StartRpcImmediately(
            pc.NetTransform.NetId, (byte)RpcCalls.SnapTo, Hazel.SendOption.None);
        NetHelpers.WriteVector2(pos, writer);
        writer.Write(sid);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }

    private void StopDance(bool resetCooldown)
    {
        if (!isDancing) return;
        isDancing = false;
        danceTimer = 0f;
        savedPositions.Clear();

        if (resetCooldown)
        {
            cooldownTimer = Cooldown;
            AURoleOptions.EngineerCooldown = Cooldown;
            Player.MarkDirtySettings();
            _ = new LateTask(() =>
            {
                if (Player.IsAlive())
                    Player.RpcResetAbilityCooldown(Sync: true);
            }, 0.1f, "StepDancer.StopReset", true);
        }

        SendRpc();
        UtilsNotifyRoles.NotifyRoles(OnlyMeName: true, SpecifySeer: Player);
        Logger.Info($"{Player.Data.GetLogPlayerName()} のステップダンス終了", "StepDancer");
    }

    public override void OnStartMeeting()
    {
        if (isDancing)
            StopDance(false);
    }

    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!Player.IsAlive()) return;

        cooldownTimer = Cooldown;
        AURoleOptions.EngineerCooldown = Cooldown;
        Player.MarkDirtySettings();
        _ = new LateTask(() =>
        {
            if (Player.IsAlive())
                Player.RpcResetAbilityCooldown(Sync: true);
        }, 0.1f, "StepDancer.AfterMeeting", true);
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive() || isForMeeting) return "";

        string size = isForHud ? "" : "<size=60%>";
        string color = RoleInfo.RoleColorCode;

        if (isDancing)
        {
            float rem = Mathf.Max(0f, Duration - danceTimer);
            return $"{size}<color={color}>【ダンス中】{rem:F1}s | {savedPositions.Count}人を固定中</color>";
        }

        if (cooldownTimer > 0f)
            return $"{size}<color={color}>CD: {Mathf.CeilToInt(cooldownTimer)}s</color>";

        return $"{size}<color={color}>ペットを撫でて → 範囲内を固定ダンス！</color>";
    }

    public string GetLowerTextOthers(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (seen != seer || isForMeeting || !Player.IsAlive()) return "";
        if (!isDancing) return "";
        if (!savedPositions.ContainsKey(seer.PlayerId)) return "";

        float rem = Mathf.Max(0f, Duration - danceTimer);
        return $"<color=#ff88cc>【ダンス固定中】{rem:F1}s</color>";
    }

    public override string GetProgressText(bool comms = false, bool GameLog = false)
    {
        if (!Player.IsAlive()) return "";
        if (isDancing)
        {
            float rem = Mathf.Max(0f, Duration - danceTimer);
            return $"<color={RoleInfo.RoleColorCode}>({rem:F1}s)</color>";
        }
        if (cooldownTimer > 0f)
            return $"<color=#888888>({Mathf.CeilToInt(cooldownTimer)}s)</color>";
        return $"<color={RoleInfo.RoleColorCode}>(READY)</color>";
    }

    void SendRpc()
    {
        using var sender = CreateSender();
        sender.Writer.Write(isDancing);
        sender.Writer.Write(danceTimer);
        sender.Writer.Write(cooldownTimer);
        sender.Writer.Write(savedPositions.Count);
        foreach (var (pid, pos) in savedPositions)
        {
            sender.Writer.Write(pid);
            sender.Writer.Write(pos.x);
            sender.Writer.Write(pos.y);
        }
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        isDancing = reader.ReadBoolean();
        danceTimer = reader.ReadSingle();
        cooldownTimer = reader.ReadSingle();
        int count = reader.ReadInt32();
        savedPositions.Clear();
        for (int i = 0; i < count; i++)
        {
            byte pid = reader.ReadByte();
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            savedPositions[pid] = new Vector2(x, y);
        }
    }
}