using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using UnityEngine;

namespace TownOfHost.Roles.Impostor;

public enum EvilTrapperTrapType { Speed, Slow, Kill }

public sealed class EvilTrapper : RoleBase, IImpostor, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(EvilTrapper),
            player => new EvilTrapper(player),
            CustomRoles.EvilTrapper,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            126600,
            SetupOptionItem,
            "etr",
            OptionSort: (2, 12)
        );

    public EvilTrapper(PlayerControl player)
        : base(RoleInfo, player)
    {
        MaxTraps = OptionMaxTraps.GetInt();
        MaxKillTraps = OptionMaxKillTraps.GetInt();
        PlaceCooldown = OptionPlaceCooldown.GetFloat();
        TrapRange = OptionTrapRange.GetFloat();
        KillTrapRange = OptionKillTrapRange.GetFloat();
        EffectDuration = OptionEffectDuration.GetFloat();
        SpeedBoost = OptionSpeedBoost.GetFloat();
        SpeedDown = OptionSpeedDown.GetFloat();
        KillSoundRange = OptionKillSoundRange.GetFloat();

        traps = new();
        placedCount = 0;
        killTrapCount = 0;
        cooldownTimer = PlaceCooldown;
        currentTrapType = EvilTrapperTrapType.Speed;
        trapTypeTimer = 0f;
    }

    static OptionItem OptionMaxTraps;
    static OptionItem OptionMaxKillTraps;
    static OptionItem OptionPlaceCooldown;
    static OptionItem OptionTrapRange;
    static OptionItem OptionKillTrapRange;
    static OptionItem OptionEffectDuration;
    static OptionItem OptionSpeedBoost;
    static OptionItem OptionSpeedDown;
    static OptionItem OptionKillSoundRange;

    static int MaxTraps;
    static int MaxKillTraps;
    static float PlaceCooldown;
    static float TrapRange;
    static float KillTrapRange;
    static float EffectDuration;
    static float SpeedBoost;
    static float SpeedDown;
    static float KillSoundRange;

    enum OptionName
    {
        EvilTrapperMaxTraps,
        EvilTrapperMaxKillTraps,
        EvilTrapperPlaceCooldown,
        EvilTrapperTrapRange,
        EvilTrapperKillTrapRange,
        EvilTrapperEffectDuration,
        EvilTrapperSpeedBoost,
        EvilTrapperSpeedDown,
        EvilTrapperKillSoundRange,
    }

    static void SetupOptionItem()
    {
        OptionMaxTraps = IntegerOptionItem.Create(RoleInfo, 10, OptionName.EvilTrapperMaxTraps,
            new(1, 10, 1), 3, false).SetValueFormat(OptionFormat.Times);
        OptionMaxKillTraps = IntegerOptionItem.Create(RoleInfo, 11, OptionName.EvilTrapperMaxKillTraps,
            new(0, 10, 1), 1, false).SetValueFormat(OptionFormat.Times);
        OptionPlaceCooldown = FloatOptionItem.Create(RoleInfo, 12, OptionName.EvilTrapperPlaceCooldown,
            new(0f, 60f, 2.5f), 15f, false).SetValueFormat(OptionFormat.Seconds);
        OptionTrapRange = FloatOptionItem.Create(RoleInfo, 13, OptionName.EvilTrapperTrapRange,
            new(0.3f, 3f, 0.1f), 1.0f, false).SetValueFormat(OptionFormat.Multiplier);
        OptionKillTrapRange = FloatOptionItem.Create(RoleInfo, 14, OptionName.EvilTrapperKillTrapRange,
            new(0.3f, 3f, 0.1f), 1.0f, false).SetValueFormat(OptionFormat.Multiplier);
        OptionEffectDuration = FloatOptionItem.Create(RoleInfo, 15, OptionName.EvilTrapperEffectDuration,
            new(1f, 30f, 1f), 5f, false).SetValueFormat(OptionFormat.Seconds);
        OptionSpeedBoost = FloatOptionItem.Create(RoleInfo, 16, OptionName.EvilTrapperSpeedBoost,
            new(1.1f, 3f, 0.1f), 1.5f, false).SetValueFormat(OptionFormat.Multiplier);
        OptionSpeedDown = FloatOptionItem.Create(RoleInfo, 17, OptionName.EvilTrapperSpeedDown,
            new(0.1f, 0.9f, 0.1f), 0.5f, false).SetValueFormat(OptionFormat.Multiplier);
        OptionKillSoundRange = FloatOptionItem.Create(RoleInfo, 18, OptionName.EvilTrapperKillSoundRange,
            new(0f, 5f, 0.5f), 2.0f, false).SetValueFormat(OptionFormat.Multiplier);
        RoleAddAddons.Create(RoleInfo, 20);
    }

    class TrapData
    {
        public EvilTrapNetObject Obj;
        public EvilTrapperTrapType Type;
        public bool Active;
        public bool Broken;
        public Vector2 Position;
        public HashSet<byte> PlayersInRange = new();
    }

    readonly List<TrapData> traps;
    int placedCount;
    int killTrapCount;
    float cooldownTimer;

    EvilTrapperTrapType currentTrapType;
    float trapTypeTimer;

    readonly Dictionary<byte, float> effectTimers = new();
    readonly Dictionary<byte, float> savedSpeeds = new();

    public float CalculateKillCooldown() => TownOfHost.Options.DefaultKillCooldown;
    public bool CanUseSabotageButton() => true;
    public bool CanUseImpostorVentButton() => true;

    public bool IsPhantomRole => true;
    public bool IsresetAfterKill => false;
    public bool UseOneclickButton => true;

    public override void Add()
    {
        placedCount = 0;
        killTrapCount = 0;
        cooldownTimer = PlaceCooldown;
        currentTrapType = EvilTrapperTrapType.Speed;
        trapTypeTimer = 0f;
        traps.Clear();
        effectTimers.Clear();
        savedSpeeds.Clear();
    }

    public override void OnSpawn(bool initialState = false)
    {
        cooldownTimer = PlaceCooldown + 1.5f;
        Player.RpcResetAbilityCooldown(Sync: true);
    }

    public override void OnDestroy() => DespawnAll();

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = cooldownTimer > 0f ? cooldownTimer : 0.1f;
        AURoleOptions.PhantomDuration = 0f;
    }

    public void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = false;
        ResetCooldown = false;
        if (!Player.IsAlive()) return;
        if (!AmongUsClient.Instance.AmHost) return;
        if (placedCount >= MaxTraps) return;
        if (currentTrapType == EvilTrapperTrapType.Kill && killTrapCount >= MaxKillTraps) return;

        PlaceTrap((Vector2)Player.transform.position);
        ResetCooldown = true;
    }

    void PlaceTrap(Vector2 pos)
    {
        var data = new TrapData
        {
            Type = currentTrapType,
            Active = false,
            Broken = false,
            Position = pos,
            Obj = new EvilTrapNetObject(pos, currentTrapType, Player, activated: false)
        };
        traps.Add(data);
        placedCount++;
        if (currentTrapType == EvilTrapperTrapType.Kill) killTrapCount++;

        cooldownTimer = PlaceCooldown;

        if (currentTrapType == EvilTrapperTrapType.Kill && KillSoundRange > 0f)
        {
            foreach (var pc in PlayerCatch.AllAlivePlayerControls)
            {
                if (pc.PlayerId == Player.PlayerId) continue;
                if (Vector2.Distance(pc.transform.position, pos) <= KillSoundRange)
                    Utils.SendMessage("<color=#880000>⚠ 近くで不気味な音がした…</color>", pc.PlayerId);
            }
        }

        SendRpc();
        UtilsNotifyRoles.NotifyRoles(OnlyMeName: true);
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!GameStates.IsInTask || GameStates.IsMeeting) return;

        if (!Player.IsAlive() && traps.Count > 0) { DespawnAll(); return; }

        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.fixedDeltaTime;
            if (cooldownTimer < 0f) cooldownTimer = 0f;
        }

        trapTypeTimer += Time.fixedDeltaTime;
        if (trapTypeTimer >= 3f)
        {
            trapTypeTimer = 0f;
            currentTrapType = (EvilTrapperTrapType)(((int)currentTrapType + 1) % 3);
            UtilsNotifyRoles.NotifyRoles(OnlyMeName: true);
        }

        foreach (var pid in effectTimers.Keys.ToArray())
        {
            effectTimers[pid] -= Time.fixedDeltaTime;
            if (effectTimers[pid] <= 0f)
            {
                RemoveEffect(pid);
                effectTimers.Remove(pid);
            }
        }

        foreach (var trap in traps.ToArray())
        {
            if (!trap.Active || trap.Obj == null || trap.Broken) continue;

            float range = trap.Type == EvilTrapperTrapType.Kill ? KillTrapRange : TrapRange;
            var nowInRange = new HashSet<byte>();

            foreach (var pc in PlayerCatch.AllAlivePlayerControls)
            {
                if (Vector2.Distance(pc.transform.position, trap.Position) > range) continue;
                nowInRange.Add(pc.PlayerId);
                if (!trap.PlayersInRange.Contains(pc.PlayerId))
                    TriggerTrap(trap, pc);
                if (trap.Broken) break;
            }
            if (!trap.Broken) trap.PlayersInRange = nowInRange;
        }
    }

    void TriggerTrap(TrapData trap, PlayerControl target)
    {
        switch (trap.Type)
        {
            case EvilTrapperTrapType.Speed: ApplySpeedEffect(target, SpeedBoost); break;
            case EvilTrapperTrapType.Slow: ApplySpeedEffect(target, SpeedDown); break;
            case EvilTrapperTrapType.Kill: TryKillTrap(trap, target); break;
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

    void TryKillTrap(TrapData trap, PlayerControl target)
    {
        if (Player.killTimer > 0f) return;

        trap.Broken = true;

        var pos = trap.Position;
        var old = trap.Obj;
        _ = new LateTask(() =>
        {
            try { old?.Despawn(); } catch { }
            trap.Obj = new EvilTrapNetObject(pos, EvilTrapperTrapType.Kill,
                Player, activated: true, broken: true);
        }, 0.1f, "EvilTrapper.BrokenTrap", true);

        target.SetRealKiller(Player);
        Player.RpcMurderPlayer(target);

        _ = new LateTask(() =>
        {
            Player.ResetKillCooldown();
            Player.SyncSettings();
        }, 0.2f, "EvilTrapper.ResetKillCD", true);

        UtilsGameLog.AddGameLog("EvilTrapper",
            $"{UtilsName.GetPlayerColor(Player)} のキルトラップが {UtilsName.GetPlayerColor(target)} をキルした");

        SendRpc();
    }

    public override void OnStartMeeting()
    {
        foreach (var pid in effectTimers.Keys.ToArray()) RemoveEffect(pid);
        effectTimers.Clear();
        foreach (var trap in traps) trap.PlayersInRange.Clear();
    }

    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost) return;

        for (int i = 0; i < traps.Count; i++)
        {
            var trap = traps[i];
            var pos = trap.Position;
            var type = trap.Type;
            int idx = i;
            var old = trap.Obj;

            if (trap.Broken)
            {
                _ = new LateTask(() =>
                {
                    try { old?.Despawn(); } catch { }
                    trap.Obj = new EvilTrapNetObject(pos, type, Player,
                        activated: true, broken: true);
                }, idx * 0.6f + 1.0f, $"EvilTrapper.ReactivateBroken.{idx}", true);
            }
            else
            {
                _ = new LateTask(() =>
                {
                    try { old?.Despawn(); } catch { }
                    trap.Active = true;
                    trap.Obj = new EvilTrapNetObject(pos, type, Player, activated: true);
                }, idx * 0.6f + 1.0f, $"EvilTrapper.Activate.{idx}", true);
            }
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

    public override void OnMurderPlayerAsTarget(MurderInfo info) => DespawnAll();

    void DespawnAll()
    {
        foreach (var trap in traps.ToArray())
            try { trap.Obj?.Despawn(); } catch { }
        traps.Clear();
    }

    public override string GetProgressText(bool comms = false, bool GameLog = false)
    {
        if (!Player.IsAlive()) return "";

        string typeIcon = currentTrapType switch
        {
            EvilTrapperTrapType.Speed => "<color=#4488ff>▲</color>",
            EvilTrapperTrapType.Slow => "<color=#ff4444>▼</color>",
            EvilTrapperTrapType.Kill => "<color=#cc0000>✂</color>",
            _ => "?"
        };

        int rem = MaxTraps - placedCount;
        int killRem = MaxKillTraps - killTrapCount;

        string killInfo = "";
        if (currentTrapType == EvilTrapperTrapType.Kill)
        {
            float kcd = Player.killTimer;
            string kdStr = kcd > 0f
                ? $"<color=#ffff00>[KD:{kcd:F0}s]</color>"
                : "<color=#00ff00>[KD:OK]</color>";
            killInfo = $"[✂{killRem}]{kdStr}";
        }

        return $"<color={RoleInfo.RoleColorCode}>({rem}残){killInfo}</color>{typeIcon}";
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive() || isForMeeting) return "";

        string size = isForHud ? "" : "<size=60%>";
        string color = RoleInfo.RoleColorCode;

        string typeName = currentTrapType switch
        {
            EvilTrapperTrapType.Speed => "加速▲",
            EvilTrapperTrapType.Slow => "減速▼",
            EvilTrapperTrapType.Kill => "キル✂",
            _ => "?"
        };

        string cd = cooldownTimer > 0f ? $"CD:{cooldownTimer:F0}s" : "設置可";

        string killCD = "";
        if (currentTrapType == EvilTrapperTrapType.Kill)
        {
            float kcd = Player.killTimer;
            killCD = kcd > 0f
                ? $" | <color=#ffff00>キルCD:{kcd:F0}s</color>"
                : " | <color=#00ff00>キルCD:OK</color>";
        }

        return $"{size}<color={color}>[{typeName}] {cd}{killCD}</color>";
    }

    void SendRpc()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        using var sender = CreateSender();
        sender.Writer.Write(placedCount);
        sender.Writer.Write(killTrapCount);
        sender.Writer.Write(cooldownTimer);
        sender.Writer.Write((int)currentTrapType);
        sender.Writer.Write(traps.Count);
        foreach (var t in traps)
        {
            sender.Writer.Write((int)t.Type);
            sender.Writer.Write(t.Active);
            sender.Writer.Write(t.Broken);
            sender.Writer.Write(t.Position.x);
            sender.Writer.Write(t.Position.y);
        }
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        placedCount = reader.ReadInt32();
        killTrapCount = reader.ReadInt32();
        cooldownTimer = reader.ReadSingle();
        currentTrapType = (EvilTrapperTrapType)reader.ReadInt32();
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            reader.ReadInt32();
            reader.ReadBoolean();
            reader.ReadBoolean();
            reader.ReadSingle();
            reader.ReadSingle();
        }
    }
}

public sealed class EvilTrapNetObject : CustomNetObject
{
    static readonly int[] TrapColorIds = { 1, 12, 0 };
    const int BrokenColorId = 6;

    readonly EvilTrapperTrapType _type;
    readonly PlayerControl _owner;
    readonly Vector2 _pos;
    readonly bool _activated;
    readonly bool _broken;

    public EvilTrapNetObject(Vector2 position, EvilTrapperTrapType type,
        PlayerControl owner, bool activated, bool broken = false)
    {
        _type = type;
        _owner = owner;
        _pos = position;
        _activated = activated;
        _broken = broken;
        CreateNetObject(position);
    }

    protected override void OnCreated()
    {
        if (PlayerControl == null) return;

        var hostPlayer = PlayerControl.LocalPlayer;
        byte hostColor = (byte)(hostPlayer?.Data?.DefaultOutfit.ColorId ?? 0);
        int trapColor = _broken ? BrokenColorId : TrapColorIds[(int)_type];

        PlayerControl.RpcSetColor((byte)trapColor);
        if (hostPlayer != null)
            hostPlayer.RpcSetColor(hostColor);
        PlayerControl.RawSetColor((byte)trapColor);

        string label = _broken
            ? "<color=#888888><size=70%>残骸</size></color>"
            : _type switch
            {
                EvilTrapperTrapType.Speed => "<color=#4488ff>▲</color>",
                EvilTrapperTrapType.Slow => "<color=#ff4444>▼</color>",
                EvilTrapperTrapType.Kill => "<color=#cc0000><size=70%>罠</size></color>",
                _ => "?"
            };

        SetName(label);
        SnapToPosition(_pos);

        var capturedPC = PlayerControl;
        var capturedColor = (byte)trapColor;
        _ = new LateTask(() =>
        {
            if (capturedPC != null) capturedPC.RawSetColor(capturedColor);
        }, 0.15f, "EvilTrapper.ApplyColor", true);

        bool showAll = _broken || (_activated && _type != EvilTrapperTrapType.Kill);
        if (!showAll)
        {
            foreach (var pc in PlayerCatch.AllPlayerControls)
            {
                if (pc.notRealPlayer) continue;
                if (pc.PlayerId != _owner.PlayerId)
                    Hide(pc);
            }
        }
    }

    public override void OnMeeting() { }
}