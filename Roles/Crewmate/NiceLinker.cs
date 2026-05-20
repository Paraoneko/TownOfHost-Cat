using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Modules;
using TownOfHost.Patches;
using TownOfHost.Roles.Core;
using UnityEngine;

namespace TownOfHost.Roles.Crewmate;

public sealed class NiceLinker : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(NiceLinker),
            player => new NiceLinker(player),
            CustomRoles.NiceLinker,
            () => RoleTypes.Engineer,
            CustomRoleTypes.Crewmate,
            160400,
            SetupOptionItem,
            "nl",
            "#aaaaff",
            (1, 7)
        );

    public NiceLinker(PlayerControl player)
        : base(RoleInfo, player)
    {
        PlaceCooldown = OptionPlaceCooldown.GetFloat();
        MaxPairs = OptionMaxPairs.GetInt();
        WarpCooldown = OptionWarpCooldown.GetFloat();

        linkPairs = new();
        pendingDummy = null;
        placedCount = 0;
        cooldownTimer = PlaceCooldown;

        // プレイヤーごとのワープCD
        warpCooldowns = new();
    }

    static OptionItem OptionPlaceCooldown;
    static OptionItem OptionMaxPairs;
    static OptionItem OptionWarpCooldown;

    static float PlaceCooldown;
    static int MaxPairs;
    static float WarpCooldown;

    enum OptionName
    {
        NiceLinkerPlaceCooldown,
        NiceLinkerMaxPairs,
        NiceLinkerWarpCooldown,
    }

    static void SetupOptionItem()
    {
        OptionPlaceCooldown = FloatOptionItem.Create(RoleInfo, 10, OptionName.NiceLinkerPlaceCooldown,
            new(0f, 60f, 2.5f), 15f, false).SetValueFormat(OptionFormat.Seconds);
        OptionMaxPairs = IntegerOptionItem.Create(RoleInfo, 11, OptionName.NiceLinkerMaxPairs,
            new(1, 10, 1), 3, false).SetValueFormat(OptionFormat.Times);
        OptionWarpCooldown = FloatOptionItem.Create(RoleInfo, 12, OptionName.NiceLinkerWarpCooldown,
            new(0f, 60f, 2.5f), 10f, false).SetValueFormat(OptionFormat.Seconds);
    }

    // ─── データ ─────────────────────────────────────────────────
    // 1組 = 2つのダミー
    class LinkPair
    {
        public LinkerDummy DummyA;
        public LinkerDummy DummyB; // nullなら未完成（1個目だけ置いた状態）
        public int ColorId;
    }

    readonly List<LinkPair> linkPairs;
    LinkPair pendingDummy; // 1個目を置いた後、2個目待ちの組
    int placedCount;     // 完成した組数
    float cooldownTimer;

    // ワープクールダウン（プレイヤーIDごと）
    readonly Dictionary<byte, float> warpCooldowns;

    // ダミーに割り当てる色（組ごとに変える）
    static readonly int[] PairColors = { 1, 11, 10, 2, 5, 4, 3, 14, 17, 8 };
    // 未完成の1個目は白(7)
    const int PendingColor = 7;

    // ─── ライフサイクル ─────────────────────────────────────────
    public override void Add()
    {
        linkPairs.Clear();
        pendingDummy = null;
        placedCount = 0;
        cooldownTimer = PlaceCooldown;
        warpCooldowns.Clear();
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
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.EngineerCooldown = cooldownTimer > 0f ? cooldownTimer : 0.1f;
        AURoleOptions.EngineerInVentMaxTime = 0f;
    }

    public override bool CanClickUseVentButton => false;
    public override bool OnEnterVent(PlayerPhysics physics, int ventId) => false;

    // ─── ペット撫で → ダミー設置 ─────────────────────────────────
    void OnPetAction()
    {
        if (!Player.IsAlive()) return;
        if (!AmongUsClient.Instance.AmHost) return;
        if (cooldownTimer > 0f) return;

        var pos = Player.GetTruePosition();

        if (pendingDummy == null)
        {
            // 1個目を置く
            if (placedCount >= MaxPairs) return;

            int colorId = PairColors[placedCount % PairColors.Length];
            var pair = new LinkPair
            {
                ColorId = colorId,
                DummyA = new LinkerDummy(pos, Player, PendingColor, activated: false)
            };
            pendingDummy = pair;

            cooldownTimer = PlaceCooldown;
            Player.RpcResetAbilityCooldown(Sync: true);
            Utils.SendMessage(
                $"<color=#aaaaff>【リンカー】1個目を設置しました！\nもう一度ペットを撫でて2個目を設置してください。</color>",
                Player.PlayerId);
        }
        else
        {
            // 2個目を置く → 組完成
            int colorId = pendingDummy.ColorId;
            pendingDummy.DummyB = new LinkerDummy(pos, Player, PendingColor, activated: false);

            var pair = pendingDummy;
            pendingDummy = null;
            placedCount++;

            cooldownTimer = PlaceCooldown;
            Player.RpcResetAbilityCooldown(Sync: true);

            // ★ 次のターン（会議後）にActivateする
            //   組完成フラグを立てるだけ（AfterMeetingTasksで表示切替）
            Utils.SendMessage(
                $"<color=#aaaaff>【リンカー】組が完成しました！\n次のターンから全員に見えて、ワープできるようになります。</color>",
                Player.PlayerId);
        }

        SendRpc();
        UtilsNotifyRoles.NotifyRoles(OnlyMeName: true);
    }

    // ─── FixedUpdate：クールダウン＋ワープ判定 ────────────────────
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!GameStates.IsInTask || GameStates.IsMeeting) return;

        // 設置クールダウン
        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.fixedDeltaTime;
            if (cooldownTimer < 0f) cooldownTimer = 0f;
        }

        // ワープクールダウンの減算
        foreach (var pid in warpCooldowns.Keys.ToArray())
        {
            warpCooldowns[pid] -= Time.fixedDeltaTime;
            if (warpCooldowns[pid] <= 0f)
                warpCooldowns.Remove(pid);
        }

        // ワープ判定（完成した組のみ）
        foreach (var pair in linkPairs)
        {
            if (pair.DummyA == null || pair.DummyB == null) continue;
            if (!pair.DummyA.Activated || !pair.DummyB.Activated) continue;

            foreach (var pc in PlayerCatch.AllAlivePlayerControls)
            {
                if (warpCooldowns.ContainsKey(pc.PlayerId)) continue;

                float distA = Vector2.Distance(pc.GetTruePosition(), pair.DummyA.Position);
                float distB = Vector2.Distance(pc.GetTruePosition(), pair.DummyB.Position);
                const float range = 1.0f;

                if (distA <= range)
                {
                    // AからBへワープ
                    pc.RpcSnapToForced(pair.DummyB.Position);
                    warpCooldowns[pc.PlayerId] = WarpCooldown;
                    UtilsGameLog.AddGameLog("NiceLinker",
                        $"{UtilsName.GetPlayerColor(pc)} がリンクA→Bにワープ");
                }
                else if (distB <= range)
                {
                    // BからAへワープ
                    pc.RpcSnapToForced(pair.DummyA.Position);
                    warpCooldowns[pc.PlayerId] = WarpCooldown;
                    UtilsGameLog.AddGameLog("NiceLinker",
                        $"{UtilsName.GetPlayerColor(pc)} がリンクB→Aにワープ");
                }
            }
        }
    }

    // ─── 会議後：組を有効化（全員に見えるようにする） ─────────────
    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost) return;

        for (int i = 0; i < linkPairs.Count; i++)
        {
            var pair = linkPairs[i];
            int idx = i;

            // 完成した組（DummyBあり）で未Activate → Activate
            if (pair.DummyB != null && !pair.DummyB.Activated)
            {
                var posA = pair.DummyA.Position;
                var posB = pair.DummyB.Position;
                var colorId = pair.ColorId;
                var owner = Player;
                var oldA = pair.DummyA;
                var oldB = pair.DummyB;

                _ = new LateTask(() =>
                {
                    try { oldA?.Despawn(); } catch { }
                    pair.DummyA = new LinkerDummy(posA, owner, colorId, activated: true);
                }, idx * 0.6f + 1.0f, $"NiceLinker.ActivateA.{idx}", true);

                _ = new LateTask(() =>
                {
                    try { oldB?.Despawn(); } catch { }
                    pair.DummyB = new LinkerDummy(posB, owner, colorId, activated: true);
                }, idx * 0.6f + 1.3f, $"NiceLinker.ActivateB.{idx}", true);
            }
            // 完成した組で既にActivate済み → 再生成（会議後に位置ズレ防止）
            else if (pair.DummyB != null && pair.DummyB.Activated)
            {
                var posA = pair.DummyA.Position;
                var posB = pair.DummyB.Position;
                var colorId = pair.ColorId;
                var owner = Player;
                var oldA = pair.DummyA;
                var oldB = pair.DummyB;

                _ = new LateTask(() =>
                {
                    try { oldA?.Despawn(); } catch { }
                    pair.DummyA = new LinkerDummy(posA, owner, colorId, activated: true);
                }, idx * 0.6f + 1.0f, $"NiceLinker.ReactivateA.{idx}", true);

                _ = new LateTask(() =>
                {
                    try { oldB?.Despawn(); } catch { }
                    pair.DummyB = new LinkerDummy(posB, owner, colorId, activated: true);
                }, idx * 0.6f + 1.3f, $"NiceLinker.ReactivateB.{idx}", true);
            }
            // 1個目だけ置いた未完成の組 → 再生成（設置者のみ見える）
            else if (pair.DummyA != null && pair.DummyB == null)
            {
                var posA = pair.DummyA.Position;
                var owner = Player;
                var oldA = pair.DummyA;

                _ = new LateTask(() =>
                {
                    try { oldA?.Despawn(); } catch { }
                    pair.DummyA = new LinkerDummy(posA, owner, PendingColor, activated: false);
                }, idx * 0.6f + 1.0f, $"NiceLinker.ReactivatePending.{idx}", true);
            }
        }

        cooldownTimer = PlaceCooldown;
        Player.RpcResetAbilityCooldown(Sync: true);
        SendRpc();
    }

    public override void OnStartMeeting()
    {
        warpCooldowns.Clear();
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        warpCooldowns.Clear();
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

        if (pendingDummy != null)
            return $"{size}<color={color}>2個目を設置してください！</color>";
        if (placedCount >= MaxPairs)
            return $"{size}<color={color}>設置上限です ({placedCount}/{MaxPairs}組)</color>";
        if (cooldownTimer > 0f)
            return $"{size}<color=#888888>設置CD: {Mathf.CeilToInt(cooldownTimer)}s</color>";
        return $"{size}<color={color}>ペットなで → リンクダミー設置 ({placedCount}/{MaxPairs}組)</color>";
    }

    public override string GetProgressText(bool comms = false, bool GameLog = false)
    {
        if (!Player.IsAlive()) return "";
        string pend = pendingDummy != null ? " <color=#ffff00>1/2</color>" : "";
        return $"<color={RoleInfo.RoleColorCode}>({placedCount}/{MaxPairs}組){pend}</color>";
    }

    // ─── RPC ─────────────────────────────────────────────────────
    void SendRpc()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        using var sender = CreateSender();
        sender.Writer.Write(placedCount);
        sender.Writer.Write(cooldownTimer);
        sender.Writer.Write(pendingDummy != null);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        placedCount = reader.ReadInt32();
        cooldownTimer = reader.ReadSingle();
        bool hasPending = reader.ReadBoolean();
        // クライアントはホストの状態に従うだけ
    }
}

// ─── リンクダミー ─────────────────────────────────────────────────
public sealed class LinkerDummy : CustomNetObject
{
    readonly PlayerControl _owner;
    readonly int _colorId;
    readonly Vector2 _pos;
    public bool Activated { get; private set; }

    public LinkerDummy(Vector2 position, PlayerControl owner, int colorId, bool activated)
    {
        _owner = owner;
        _colorId = colorId;
        _pos = position;
        Activated = activated;
        CreateNetObject(position);
    }

    protected override void OnCreated()
    {
        if (PlayerControl == null) return;

        // ★ 色だけ設定（SetAppearanceは使わない）
        PlayerControl.RpcSetColor((byte)_colorId);

        SetName("リンカー");
        SnapToPosition(_pos);

        if (Activated)
        {
            // 全員に見える
        }
        else
        {
            // 設置者のみ見える
            foreach (var pc in PlayerCatch.AllPlayerControls)
            {
                if (pc.notRealPlayer) continue;
                if (pc.PlayerId != _owner.PlayerId)
                    Hide(pc);
            }
        }
    }

    // 試合終わるまで消えない
    public override void OnMeeting() { }
}