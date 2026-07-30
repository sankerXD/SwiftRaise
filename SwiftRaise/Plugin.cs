using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace SwiftRaise;

public sealed unsafe class Plugin : IDalamudPlugin
{
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;

    private const string CommandName = "/sres";

    private const uint SwiftcastActionId = 7561;
    private const uint SwiftcastStatusId = 167;
    private const uint DualcastStatusId = 1249;   // 赤魔"连续咏唱": 有此buff时赤复活同样瞬发
    private const uint RaisePendingStatusId = 148; // 目标身上的"复活"待确认状态
    private const uint RedMageJobId = 35;

    // 新月岛幻影职业·药剂师: 苏生(瞬发, 5秒复唱)。只有在新月岛携带药剂师且已习得时才可用
    private const uint ChemistReviveActionId = 41634;

    // 同一个目标两次尝试之间的最小间隔, 防止悬停期间每帧重复施放
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);

    // 职业ID -> 复活技能ID
    private static readonly Dictionary<uint, uint> RaiseActions = new()
    {
        [6]  = 125,   // 幻术师: 复活
        [24] = 125,   // 白魔法师: 复活
        [28] = 173,   // 学者: 复苏
        [33] = 3603,  // 占星术士: 生辰
        [40] = 24287, // 贤者: 复苏
        [27] = 173,   // 召唤师: 复生
        [35] = 7523,  // 赤魔法师: 赤复活
    };

    private enum State { Idle, WaitingSwiftcast, TryingRaise }

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly Configuration config;
    private readonly IDtrBarEntry dtrEntry;

    private State state = State.Idle;
    private ulong pendingTargetId;
    private uint pendingRaiseAction;
    private DateTime stateDeadline;
    private readonly Dictionary<ulong, DateTime> lastAttempt = new();

    // 记忆中的复活目标: 最近一次悬停/点选的死亡玩家, 鼠标移开后仍持续追踪, 新目标覆盖旧目标
    private ulong candidateId;
    private ulong lastHoverId;
    private ulong lastHardTargetId;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
        config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "开/关 自动即刻复活",
        });

        // 右上角原生信息栏(艾欧泽亚时间/本地时间/服务器 那一条)中的开关条目
        dtrEntry = DtrBar.Get("SwiftRaise");
        dtrEntry.OnClick = _ => Toggle();
        UpdateDtrEntry();

        Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        dtrEntry.Remove();
        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args) => Toggle();

    private void Toggle()
    {
        config.Enabled = !config.Enabled;
        pluginInterface.SavePluginConfig(config);
        UpdateDtrEntry();
        ChatGui.Print($"[即刻复活] 自动复活已{(config.Enabled ? "开启" : "关闭")}。");
        if (!config.Enabled)
            state = State.Idle;
    }

    private void UpdateDtrEntry()
    {
        var builder = new SeStringBuilder();
        if (config.Enabled)
            builder.AddUiForeground("复活:开", 45); // 绿色
        else
            builder.AddUiForeground("复活:关", 17); // 红色

        dtrEntry.Text = builder.Build();
        dtrEntry.Tooltip = $"自动即刻复活: {(config.Enabled ? "已开启" : "已关闭")}\n点击切换 (也可用 /sres)";
    }

    // ---------- 主循环 ----------

    private void OnFrameworkUpdate(IFramework framework)
    {
        var player = ObjectTable.LocalPlayer; // API 15 起 LocalPlayer 从 IClientState 移到 IObjectTable
        if (player == null)
        {
            state = State.Idle;
            return;
        }

        // 已经用了即刻, 在等buff生效
        if (state == State.WaitingSwiftcast)
        {
            if (DateTime.UtcNow > stateDeadline)
            {
                state = State.Idle; // 即刻没生效(被打断等), 放弃本次, 等下一轮检测
                return;
            }

            if (HasStatus(player, SwiftcastStatusId))
            {
                state = State.TryingRaise;
                stateDeadline = DateTime.UtcNow.AddSeconds(3.0);
            }

            return;
        }

        // 逐帧尝试施放复活, 直到游戏接受(动画锁/GCD转动期间UseAction会被拒绝)或超时
        if (state == State.TryingRaise)
        {
            if (DateTime.UtcNow > stateDeadline)
            {
                state = State.Idle;
                return;
            }

            if (ActionManager.Instance()->UseAction(ActionType.Action, pendingRaiseAction, pendingTargetId))
                state = State.Idle;

            return;
        }

        if (!config.Enabled || ClientState.IsPvP)
            return;

        if (player.CurrentHp == 0 || player.IsCasting)
            return;

        var am = ActionManager.Instance();

        // 本职业有无复活技能(治疗/赤魔/召唤); 新月岛幻影药剂师的"苏生"是否可用(已习得且不在冷却)
        var hasRaise = RaiseActions.TryGetValue(player.ClassJob.RowId, out var raiseAction);
        var reviveReady = am->GetActionStatus(ActionType.Action, ChemistReviveActionId) == 0;
        if (!hasRaise && !reviveReady)
            return;

        // 更新记忆目标(悬停或点选到新的死亡玩家时覆盖), 再校验其是否仍需复活
        UpdateCandidate();

        var target = ResolveCandidate();
        if (target == null)
            return;

        // 对同一目标限频, 避免悬停期间反复触发
        var now = DateTime.UtcNow;
        if (lastAttempt.TryGetValue(target.GameObjectId, out var last) && now - last < RetryInterval)
            return;

        lastAttempt[target.GameObjectId] = now;
        pendingTargetId = target.GameObjectId;

        // 身上已有即刻(赤魔的连续咏唱同理): 直接秒读复活
        var hasInstant = HasStatus(player, SwiftcastStatusId)
                         || (player.ClassJob.RowId == RedMageJobId && HasStatus(player, DualcastStatusId));

        if (hasRaise && hasInstant)
        {
            StartRaise(raiseAction, now);
            ChatGui.Print($"[即刻复活] 复活 {target.Name}");
        }
        else if (hasRaise && am->GetActionStatus(ActionType.Action, SwiftcastActionId) == 0)
        {
            am->UseAction(ActionType.Action, SwiftcastActionId);
            pendingRaiseAction = raiseAction;
            state = State.WaitingSwiftcast;
            stateDeadline = now.AddSeconds(3.0);
            ChatGui.Print($"[即刻复活] 即刻咏唱 → 复活 {target.Name}");
        }
        else if (reviveReady)
        {
            // 没有即刻可用: 新月岛药剂师"苏生"瞬发拉人(无复活技能的职业也走这里)
            StartRaise(ChemistReviveActionId, now);
            ChatGui.Print($"[即刻复活] 苏生 {target.Name}");
        }
        else
        {
            // 即刻冷却中且无苏生: 硬读复活
            StartRaise(raiseAction, now);
            ChatGui.Print($"[即刻复活] 即刻冷却中，读条复活 {target.Name}");
        }
    }

    /// <summary>进入 TryingRaise 状态: 逐帧尝试对记忆目标施放指定技能, 直到 GCD/动画锁允许.</summary>
    private void StartRaise(uint actionId, DateTime now)
    {
        pendingRaiseAction = actionId;
        state = State.TryingRaise;
        stateDeadline = now.AddSeconds(3.0);
    }

    // ---------- 工具 ----------

    private static bool HasStatus(IBattleChara chara, uint statusId)
    {
        foreach (var status in chara.StatusList)
        {
            if (status.StatusId == statusId)
                return true;
        }

        return false;
    }

    /// <summary>悬停或点选到"新的"死亡玩家时, 覆盖记忆目标(变化检测保证最近操作的目标优先).</summary>
    private void UpdateCandidate()
    {
        var hover = ResolveMouseOverTarget();
        var hoverId = hover?.GameObjectId ?? 0;
        if (hoverId != lastHoverId)
        {
            lastHoverId = hoverId;
            if (IsDeadPlayer(hover))
                candidateId = hoverId;
        }

        var hardTarget = TargetManager.Target;
        var hardTargetId = hardTarget?.GameObjectId ?? 0;
        if (hardTargetId != lastHardTargetId)
        {
            lastHardTargetId = hardTargetId;
            if (IsDeadPlayer(hardTarget))
                candidateId = hardTargetId;
        }
    }

    /// <summary>取出记忆目标; 目标已复活/已被拉起/不存在时清除记忆并返回 null.</summary>
    private IPlayerCharacter? ResolveCandidate()
    {
        if (candidateId == 0)
            return null;

        if (ObjectTable.SearchById(candidateId) is IPlayerCharacter pc
            && pc.CurrentHp == 0
            && !HasStatus(pc, RaisePendingStatusId))
            return pc;

        candidateId = 0;
        return null;
    }

    private static bool IsDeadPlayer(IGameObject? obj)
        => obj is IPlayerCharacter { CurrentHp: 0 };

    private IGameObject? ResolveMouseOverTarget()
    {
        // 场景中悬停人物模型/名牌
        if (TargetManager.MouseOverTarget != null)
            return TargetManager.MouseOverTarget;

        // 悬停小队列表等UI元素
        var pronoun = PronounModule.Instance();
        if (pronoun != null && pronoun->UiMouseOverTarget != null)
            return ObjectTable.CreateObjectReference((nint)pronoun->UiMouseOverTarget);

        return null;
    }
}
