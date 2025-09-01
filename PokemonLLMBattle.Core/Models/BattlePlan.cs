using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace PokemonLLMBattle.Core.Models
{
    /// <summary>
    /// 单打对战计划 - 具体的战略步骤和目标
    /// </summary>
    public record SingleBattlePlan
    {
        /// <summary>
        /// 计划ID
        /// </summary>
        public string PlanId { get; init; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 战斗整体目标
        /// </summary>
        public string OverallObjective { get; init; } = string.Empty;

        /// <summary>
        /// 战斗阶段计划
        /// </summary>
        public List<BattlePhase> BattlePhases { get; init; } = new();

        /// <summary>
        /// 当前计划状态
        /// </summary>
        public BattlePlanStatus Status { get; init; } = BattlePlanStatus.Active;

        /// <summary>
        /// 计划创建时间
        /// </summary>
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdated { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// 计划完成度（0-100）
        /// </summary>
        public int CompletionPercentage => BattlePhases.Count == 0 ? 0 : 
            (int)(BattlePhases.Count(p => p.Status == PhaseStatus.Completed) * 100.0 / BattlePhases.Count);

        /// <summary>
        /// 核心战术重点
        /// </summary>
        public List<string> KeyTactics { get; init; } = new();

        /// <summary>
        /// 风险评估
        /// </summary>
        public RiskAssessment RiskAssessment { get; init; } = new();

        /// <summary>
        /// 调整历史
        /// </summary>
        public List<PlanAdjustment> AdjustmentHistory { get; init; } = new();
    }

    /// <summary>
    /// 战斗阶段
    /// </summary>
    public record BattlePhase
    {
        /// <summary>
        /// 阶段名称
        /// </summary>
        public string PhaseName { get; init; } = string.Empty;

        /// <summary>
        /// 阶段描述
        /// </summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>
        /// 阶段目标
        /// </summary>
        public List<PhaseObjective> Objectives { get; init; } = new();

        /// <summary>
        /// 阶段状态
        /// </summary>
        public PhaseStatus Status { get; init; } = PhaseStatus.Pending;

        /// <summary>
        /// 阶段优先级（1-10，10最高）
        /// </summary>
        public int Priority { get; init; } = 5;

        /// <summary>
        /// 预计持续回合数
        /// </summary>
        public int ExpectedTurns { get; init; } = 1;

        /// <summary>
        /// 实际使用回合数
        /// </summary>
        public int ActualTurns { get; init; } = 0;

        /// <summary>
        /// 成功条件
        /// </summary>
        public List<string> SuccessConditions { get; init; } = new();

        /// <summary>
        /// 失败风险
        /// </summary>
        public List<string> FailureRisks { get; init; } = new();
    }

    /// <summary>
    /// 阶段目标
    /// </summary>
    public record PhaseObjective
    {
        /// <summary>
        /// 目标描述
        /// </summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>
        /// 是否已完成
        /// </summary>
        public bool IsCompleted { get; init; } = false;

        /// <summary>
        /// 目标重要性（1-10）
        /// </summary>
        public int Importance { get; init; } = 5;

        /// <summary>
        /// 具体行动
        /// </summary>
        public List<string> Actions { get; init; } = new();
    }

    /// <summary>
    /// 风险评估
    /// </summary>
    public record RiskAssessment
    {
        /// <summary>
        /// 整体风险等级（1-10）
        /// </summary>
        public int OverallRiskLevel { get; init; } = 5;

        /// <summary>
        /// 主要威胁
        /// </summary>
        public List<string> MajorThreats { get; init; } = new();

        /// <summary>
        /// 应对策略
        /// </summary>
        public List<string> CounterStrategies { get; init; } = new();

        /// <summary>
        /// 后备计划
        /// </summary>
        public List<string> BackupPlans { get; init; } = new();
    }

    /// <summary>
    /// 计划调整记录
    /// </summary>
    public record PlanAdjustment
    {
        /// <summary>
        /// 调整时间
        /// </summary>
        public DateTime AdjustmentTime { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// 调整原因
        /// </summary>
        public string Reason { get; init; } = string.Empty;

        /// <summary>
        /// 调整类型
        /// </summary>
        public AdjustmentType Type { get; init; } = AdjustmentType.Minor;

        /// <summary>
        /// 调整内容
        /// </summary>
        public string Changes { get; init; } = string.Empty;

        /// <summary>
        /// 当前回合数
        /// </summary>
        public int TurnNumber { get; init; } = 0;
    }

    /// <summary>
    /// 计划状态
    /// </summary>
    public enum BattlePlanStatus
    {
        Active,      // 激活中
        Completed,   // 已完成
        Failed,      // 失败
        Adjusting    // 调整中
    }

    /// <summary>
    /// 阶段状态
    /// </summary>
    public enum PhaseStatus
    {
        Pending,     // 待执行
        InProgress,  // 执行中
        Completed,   // 已完成
        Failed,      // 失败
        Skipped      // 跳过
    }

    /// <summary>
    /// 调整类型
    /// </summary>
    public enum AdjustmentType
    {
        Minor,       // 轻微调整
        Major,       // 重大调整
        Complete     // 完全重新规划
    }
}