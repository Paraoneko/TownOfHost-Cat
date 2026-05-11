using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Modules;
using TownOfHost.Patches;
using TownOfHost.Roles.Core;
using UnityEngine;

namespace TownOfHost.Roles.Crewmate;

public enum NiceTrapperTrapType { Speed, Slow, Notify }

public sealed class NiceTrapper : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(NiceTrapper),
            player => new NiceTrapper(player),
            CustomRoles.NiceTrapper,
            () => RoleTypes.Engineer,
            CustomRoleTypes.Crewmate,
            100300,
            SetupOptionItem,
            "nt",
            "#66ddaa",
            (6, 3),
            from: From.SuperNewRoles
        );

    public NiceTrapper(PlayerControl player)
        : base(RoleInfo, player)
    {
        MaxTraps = OptionMaxTraps.GetInt();
        PlaceCooldown = OptionPlaceCooldown.GetFloat();
        TrapRange = OptionTrapRange.GetFloat();
        EffectDuration = OptionEffectDuration.GetFloat();
        SpeedBoost = OptionSpeedBoost.GetFloat();
        SpeedDown = OptionSpeedDown.GetFloat();

        traps = new();
        placedCount = 0;
        cooldownTimer = PlaceCooldown;
    }

    static OptionItem OptionMaxTraps;
    static OptionItem OptionPlaceCooldown;
    static OptionItem OptionTrapRange;
    static OptionItem OptionEffectDuration;
    static OptionItem OptionSpeedBoost;
    static OptionItem OptionSpeedDown;

    static int MaxTraps;
    static float PlaceCooldown;
    static float TrapRange;
    static float EffectDuration;
    static float SpeedBoost;
    static float SpeedDown;

    enum OptionName
    {
        NiceTrapperMaxTraps,
        NiceTrapperPlaceCooldown,
        NiceTrapperTrapRange,
        NiceTrapperEffectDuration,
        NiceTrapperSpeedBoost,
        NiceTrapperSpeedDown,
    }

    static void SetupOptionItem()
    {
        OptionMaxTraps = IntegerOptionItem.Create(RoleInfo, 10, OptionName.NiceTrapperMaxTraps,
            new(1, 10, 1), 3, false).SetValueFormat(OptionFormat.Times);
        OptionPlaceCooldown = FloatOptionItem.Create(RoleInfo, 11, OptionName.NiceTrapperPlaceCooldown,
            new(0f, 60f, 2.5f), 15f, false).SetValueFormat(OptionFormat.Seconds);
        OptionTrapRange = FloatOptionItem.Create(RoleInfo, 12, OptionName.NiceTrapperTrapRange,
            new(0.3f, 3f, 0.1f), 1.0f, false).SetValueFormat(OptionFormat.Multiplier);
        OptionEffectDuration = FloatOptionItem.Create(RoleInfo, 13, OptionName.NiceTrapperEffectDuration,
            new(1f, 30f, 1f), 5f, false).SetValueFormat(OptionFormat.Seconds);
        OptionSpeedBoost = FloatOptionItem.Create(RoleInfo, 14, OptionName.NiceTrapperSpeedBoost,
            new(1.1f, 3f, 0.1f), 1.5f, false).SetValueFormat(OptionFormat.Multiplier);
        OptionSpeedDown = FloatOptionItem.Create(RoleInfo, 15, OptionName.NiceTrapperSpeedDown,
            new(0.1f, 0.9f, 0.1f), 0.5f, false).SetValueFormat(OptionFormat.Multiplier);
    }

    class TrapData
    {
        public TrapNetObject Obj;
        public NiceTrapperTrapType Type;
        public bool Active;
        public Vector2 Position;
        public HashSet<byte> PlayersInRange = new();
    }

    readonly List<TrapData> traps;
    int placedCount;
    float cooldownTimer;

    readonly Dictionary<byte, float> effectTimers = new();
    readonly Dictionary<byte, float> savedSpeeds = new();

    // ─── Add / OnSpawn ───────────────────────────────────────────
    public override void Add()
    {
        placedCount = 0;
        cooldownTimer = PlaceCooldown;
        traps.Clear();
        effectTimers.Clear();
        savedSpeeds.Clear();
        PetActionManager.Register(Player.PlayerId, OnPetAction);
    }

    public override void OnSpawn(bool initialState = false)
    {
        cooldownTimer = PlaceCooldown + 1.5f;
        Player.RpcResetAbilityCooldown(Sync: true);
    }

    public override void OnDestroy()
    {
        PetActionManager.Unregister(Player.PlayerId);
        DespawnAll();
    }
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.EngineerCooldown = cooldownTimer > 0f ? cooldownTimer : 0.1f;
        AURoleOptions.EngineerInVentMaxTime = 0f;
    }

    public override bool CanClickUseVentButton => false;
    public override bool OnEnterVent(PlayerPhysics physics, int ventId) => false;

    // ─── ペットアクション → 設置 ──────────────────────────────────
    void OnPetAction()
    {
        if (!Player.IsAlive()) return;
        if (placedCount >= MaxTraps) return;
        if (cooldownTimer > 0f) return;
        if (!AmongUsClient.Instance.AmHost) return;
        PlaceTrap(Player.GetTruePosition());
    }

    void PlaceTrap(Vector2 pos)
    {
        var type = (NiceTrapperTrapType)IRandom.Instance.Next(0, 3);
        var data = new TrapData
        {
            Type = type,
            Active = false,
            Position = pos,
        };
        // ★ 設置直後はオーナーのみ見える（activated=false）
        data.Obj = new TrapNetObject(pos, type, Player, activated: false);
        traps.Add(data);

        placedCount++;
        cooldownTimer = PlaceCooldown;
        Player.RpcResetAbilityCooldown(Sync: true);

        SendRpc();
        UtilsNotifyRoles.NotifyRoles(OnlyMeName: true);
        Logger.Info($"{Player.Data.GetLogPlayerName()} トラップ設置 [{type}]", "NiceTrapper");
    }

    // ─── FixedUpdate ─────────────────────────────────────────────
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!GameStates.IsInTask || GameStates.IsMeeting) return;
        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.fixedDeltaTime;
            if (cooldownTimer < 0f) cooldownTimer = 0f;
        }

        // 速度効果タイマー
        foreach (var pid in effectTimers.Keys.ToArray())
        {
            effectTimers[pid] -= Time.fixedDeltaTime;
            if (effectTimers[pid] <= 0f)
            {
                RemoveEffect(pid);
                effectTimers.Remove(pid);
            }
        }

        // トラップ判定
        foreach (var trap in traps.ToArray())
        {
            if (!trap.Active || trap.Obj == null) continue;

            var nowInRange = new HashSet<byte>();
            foreach (var pc in PlayerCatch.AllAlivePlayerControls)
            {
                if (Vector2.Distance(pc.GetTruePosition(), trap.Position) > TrapRange) continue;
                nowInRange.Add(pc.PlayerId);
                if (!trap.PlayersInRange.Contains(pc.PlayerId))
                    TriggerTrap(trap, pc);
            }
            trap.PlayersInRange = nowInRange;
        }
    }

    void TriggerTrap(TrapData trap, PlayerControl target)
    {
        switch (trap.Type)
        {
            case NiceTrapperTrapType.Speed:
                ApplySpeedEffect(target, SpeedBoost);
                UtilsGameLog.AddGameLog("NiceTrapper",
                    $"{UtilsName.GetPlayerColor(target)} が加速トラップを踏んだ");
                break;
            case NiceTrapperTrapType.Slow:
                ApplySpeedEffect(target, SpeedDown);
                UtilsGameLog.AddGameLog("NiceTrapper",
                    $"{UtilsName.GetPlayerColor(target)} が減速トラップを踏んだ");
                break;
            case NiceTrapperTrapType.Notify:
                NotifyTrapper(target);
                UtilsGameLog.AddGameLog("NiceTrapper",
                    $"{UtilsName.GetPlayerColor(target)} が通知トラップを踏んだ");
                break;
        }
    }

    void ApplySpeedEffect(PlayerControl target, float multiplier)
    {
        byte id = target.PlayerId;
        if (!savedSpeeds.ContainsKey(id))
            savedSpeeds[id] = Main.AllPlayerSpeed.TryGetValue(id, out float s) ? s : 1f;
        Main.AllPlayerSpeed[id] = savedSpeeds[id] * multiplier;
        target.MarkDirtySettings();
        effectTimers[id] = EffectDuration;
    }

    void RemoveEffect(byte playerId)
    {
        if (!savedSpeeds.TryGetValue(playerId, out float orig)) return;
        Main.AllPlayerSpeed[playerId] = orig;
        PlayerCatch.GetPlayerById(playerId)?.MarkDirtySettings();
        savedSpeeds.Remove(playerId);
    }

    void NotifyTrapper(PlayerControl target)
    {
        var targetPos = target.GetTruePosition();
        GetArrow.Add(Player.PlayerId, targetPos);
        UtilsNotifyRoles.NotifyRoles(OnlyMeName: true, SpecifySeer: Player);

        _ = new LateTask(() =>
        {
            GetArrow.Remove(Player.PlayerId, targetPos);
            UtilsNotifyRoles.NotifyRoles(OnlyMeName: true, SpecifySeer: Player);
        }, 2f, "NiceTrapper.RemoveArrow", true);

        Utils.SendMessage(
            $"<color=#66ddaa>【通知トラップ】{UtilsName.GetPlayerColor(target, true)}がトラップを踏みました！</color>",
            Player.PlayerId);
    }

    // ─── 会議 ─────────────────────────────────────────────────────
    public override void OnStartMeeting()
    {
        foreach (var pid in effectTimers.Keys.ToArray()) RemoveEffect(pid);
        effectTimers.Clear();
        foreach (var trap in traps) trap.PlayersInRange.Clear();
    }

    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost) return;

        // ★ 各トラップをActivate：古いダミーを消して新しいダミーを作り直す
        //   会議後に位置ズレ・名前消えを防ぐためダミーを再生成
        //   Speed/SlowはActivated=trueで全員に見える、NotifyはOwnerのみ
        for (int i = 0; i < traps.Count; i++)
        {
            var trap = traps[i];
            trap.Active = true;
            var oldObj = trap.Obj;
            var pos = trap.Position;
            var type = trap.Type;
            int idx = i;

            _ = new LateTask(() =>
            {
                // 古いダミーを消す
                try { oldObj?.Despawn(); } catch { }

                // ★ 新しいダミーを作り直す（activated=trueで適切な表示）
                trap.Obj = new TrapNetObject(pos, type, Player, activated: true);

            }, idx * 0.6f + 1.0f, $"NiceTrapper.Activate.{idx}", true);
        }

        cooldownTimer = PlaceCooldown;
        Player.RpcResetAbilityCooldown(Sync: true);

        SendRpc();
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        foreach (var pid in effectTimers.Keys.ToArray()) RemoveEffect(pid);
        effectTimers.Clear();
        foreach (var trap in traps) trap.PlayersInRange.Clear();
    }

    void DespawnAll()
    {
        foreach (var trap in traps.ToArray())
            try { trap.Obj?.Despawn(); } catch { }
        traps.Clear();
    }

    // ─── テキスト ─────────────────────────────────────────────────
    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive()) return "";
        if (isForMeeting) return "";

        string size = isForHud ? "" : "<size=60%>";
        string color = RoleInfo.RoleColorCode;
        int remaining = MaxTraps - placedCount;

        if (remaining <= 0)
            return $"{size}<color={color}>設置数が上限です</color>";

        if (cooldownTimer > 0f)
            return $"{size}<color=#888888>設置CD中...</color>";

        return $"{size}<color={color}>ペットなで → トラップ設置 (残り{remaining}個)</color>";
    }

    public override string GetProgressText(bool comms = false, bool GameLog = false)
    {
        if (!Player.IsAlive()) return "";
        int active = traps.Count(t => t.Active);
        int inactive = traps.Count(t => !t.Active);
        string txt = $"({MaxTraps - placedCount}残)";
        if (active > 0) txt += $" <color=#66ff99>有:{active}</color>";
        if (inactive > 0) txt += $" <color=#888888>待:{inactive}</color>";
        return $"<color={RoleInfo.RoleColorCode}>{txt}</color>";
    }

    // ─── RPC ─────────────────────────────────────────────────────
    void SendRpc()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        using var sender = CreateSender();
        sender.Writer.Write(placedCount);
        sender.Writer.Write(cooldownTimer);
        sender.Writer.Write(traps.Count);
        foreach (var t in traps)
        {
            sender.Writer.Write((int)t.Type);
            sender.Writer.Write(t.Active);
            sender.Writer.Write(t.Position.x);
            sender.Writer.Write(t.Position.y);
        }
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        placedCount = reader.ReadInt32();
        cooldownTimer = reader.ReadSingle();
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            reader.ReadInt32();
            reader.ReadBoolean();
            reader.ReadSingle();
            reader.ReadSingle();
        }
    }
}

// ─── トラップダミー ───────────────────────────────────────────────────
public sealed class TrapNetObject : CustomNetObject
{
    // Speed=青(1), Slow=赤(0), Notify=黄(5)
    static readonly int[] TrapColorIds = { 1, 0, 5 };

    readonly NiceTrapperTrapType _type;
    readonly PlayerControl _owner;
    readonly Vector2 _pos;
    readonly bool _activated;

    public TrapNetObject(Vector2 position, NiceTrapperTrapType type, PlayerControl owner, bool activated)
    {
        _type = type;
        _owner = owner;
        _pos = position;
        _activated = activated;
        CreateNetObject(position);
    }

    protected override void OnCreated()
    {
        if (PlayerControl == null) return;

        // ★ 色設定
        PlayerControl.RpcSetColor((byte)TrapColorIds[(int)_type]);

        // ★ スキン・ハット・バイザー・ペットを消す
        PlayerControl.RpcSetHat("");
        PlayerControl.RpcSetSkin("");
        PlayerControl.RpcSetVisor("");
        PlayerControl.RpcSetPet("");

        // ★ 名前ラベル
        string label = _type switch
        {
            NiceTrapperTrapType.Speed => "<color=#4488ff>▲</color>",
            NiceTrapperTrapType.Slow => "<color=#ff4444>▼</color>",
            NiceTrapperTrapType.Notify => "<color=#ffff00>●</color>",
            _ => "?"
        };
        SetName(label);
        SnapToPosition(_pos);

        // ★ 表示制御
        // 非Activate時: オーナーのみ見える
        // Activate後 Speed/Slow: 全員に見える
        // Activate後 Notify: オーナーのみ見える
        bool showToAll = _activated && _type != NiceTrapperTrapType.Notify;

        foreach (var pc in PlayerCatch.AllPlayerControls)
        {
            if (pc.notRealPlayer) continue;
            if (showToAll) continue; // 全員に見える → Hideしない
            if (pc.PlayerId != _owner.PlayerId)
                Hide(pc);
        }
    }

    public override void OnMeeting() { } // 会議でも消えない
}