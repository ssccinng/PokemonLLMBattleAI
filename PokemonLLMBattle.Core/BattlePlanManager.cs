using Microsoft.Extensions.AI;
using PokemonLLMBattle.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PokemonLLMBattle.Core
{
    /// <summary>
    /// 单打对战计划管理器
    /// </summary>
    public interface IBattlePlanManager
    {
        /// <summary>
        /// 创建初始对战计划
        /// </summary>
        Task<SingleBattlePlan> CreateInitialBattlePlanAsync(GameState gameState, CancellationToken cancellationToken = default);

        /// <summary>
        /// 更新对战计划
        /// </summary>
        Task<SingleBattlePlan> UpdateBattlePlanAsync(SingleBattlePlan currentPlan, GameState gameState, CancellationToken cancellationToken = default);

        /// <summary>
        /// 序列化计划为JSON
        /// </summary>
        string SerializePlan(SingleBattlePlan plan);

        /// <summary>
        /// 从JSON反序列化计划
        /// </summary>
        SingleBattlePlan? DeserializePlan(string planJson);

        /// <summary>
        /// 评估计划执行情况
        /// </summary>
        Task<PlanEvaluationResult> EvaluatePlanProgressAsync(SingleBattlePlan plan, GameState gameState);
    }

    /// <summary>
    /// 计划评估结果
    /// </summary>
    public record PlanEvaluationResult
    {
        public bool NeedsAdjustment { get; init; } = false;
        public string Reason { get; init; } = string.Empty;
        public AdjustmentType RecommendedAdjustmentType { get; init; } = AdjustmentType.Minor;
        public List<string> SuggestedChanges { get; init; } = new();
    }

    /// <summary>
    /// 单打对战计划管理器实现
    /// </summary>
    public class SingleBattlePlanManager : IBattlePlanManager
    {
        private readonly IChatClient _chatClient;
        private readonly IPromptBuilder _promptBuilder;

        public SingleBattlePlanManager(IChatClient chatClient, IPromptBuilder promptBuilder)
        {
            _chatClient = chatClient;
            _promptBuilder = promptBuilder;
        }

        public async Task<SingleBattlePlan> CreateInitialBattlePlanAsync(GameState gameState, CancellationToken cancellationToken = default)
        {
            var prompt = BuildInitialPlanPrompt(gameState);
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, prompt)
            };

            var chatOptions = new ChatOptions
            {
                AdditionalProperties = { ["reasoning"] = new { effort = "high" } }
            };

            var response = await _chatClient.GetResponseAsync(messages, chatOptions, cancellationToken);
            
            // 解析LLM响应并创建结构化的对战计划
            var planData = ParseLLMResponseToPlan(response.Text, gameState);

            return new SingleBattlePlan
            {
                OverallObjective = planData.OverallObjective,
                BattlePhases = planData.BattlePhases,
                KeyTactics = planData.KeyTactics,
                RiskAssessment = planData.RiskAssessment,
                Status = BattlePlanStatus.Active,
                CreatedAt = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };
        }

        public async Task<SingleBattlePlan> UpdateBattlePlanAsync(SingleBattlePlan currentPlan, GameState gameState, CancellationToken cancellationToken = default)
        {
            // 评估当前计划执行情况
            var evaluation = await EvaluatePlanProgressAsync(currentPlan, gameState);

            if (!evaluation.NeedsAdjustment)
            {
                // 只更新进度状态
                return UpdatePlanProgress(currentPlan, gameState);
            }

            // 需要调整计划
            var adjustmentPrompt = BuildPlanAdjustmentPrompt(currentPlan, gameState, evaluation);
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, adjustmentPrompt)
            };

            var chatOptions = new ChatOptions
            {
                AdditionalProperties = { ["reasoning"] = new { effort = "medium" } }
            };

            var response = await _chatClient.GetResponseAsync(messages, chatOptions, cancellationToken);
            var adjustedPlanData = ParseLLMResponseToPlan(response.Text, gameState);

            // 创建调整记录
            var adjustment = new PlanAdjustment
            {
                AdjustmentTime = DateTime.UtcNow,
                Reason = evaluation.Reason,
                Type = evaluation.RecommendedAdjustmentType,
                Changes = string.Join("; ", evaluation.SuggestedChanges),
                TurnNumber = gameState.BattleData.Turn
            };

            // 返回更新后的计划
            return currentPlan with
            {
                OverallObjective = adjustedPlanData.OverallObjective,
                BattlePhases = adjustedPlanData.BattlePhases,
                KeyTactics = adjustedPlanData.KeyTactics,
                RiskAssessment = adjustedPlanData.RiskAssessment,
                LastUpdated = DateTime.UtcNow,
                AdjustmentHistory = currentPlan.AdjustmentHistory.Concat(new[] { adjustment }).ToList()
            };
        }

        public string SerializePlan(SingleBattlePlan plan)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            return JsonSerializer.Serialize(plan, options);
        }

        public SingleBattlePlan? DeserializePlan(string planJson)
        {
            if (string.IsNullOrWhiteSpace(planJson))
                return null;

            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                return JsonSerializer.Deserialize<SingleBattlePlan>(planJson, options);
            }
            catch
            {
                return null;
            }
        }

        public async Task<PlanEvaluationResult> EvaluatePlanProgressAsync(SingleBattlePlan plan, GameState gameState)
        {
            var evaluationPrompt = BuildPlanEvaluationPrompt(plan, gameState);
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, evaluationPrompt)
            };

            var chatOptions = new ChatOptions
            {
                AdditionalProperties = { ["reasoning"] = new { effort = "medium" } }
            };

            var response = await _chatClient.GetResponseAsync(messages, chatOptions);
            
            // 解析评估结果
            return ParseEvaluationResponse(response.Text);
        }

        #region Private Helper Methods

        private string BuildInitialPlanPrompt(GameState gameState)
        {
            var battleData = gameState.BattleData;
            var myTeam = GetTeamSummary(battleData, battleData.MySlot);
            var oppTeam = GetTeamSummary(battleData, 1 - battleData.MySlot);

            return $@"
You are a professional Pokémon single battle strategist. Create a comprehensive battle plan to achieve victory.

Current Battle Context:
- Turn: {battleData.Turn}
- My Team: {myTeam}
- Opponent Team: {oppTeam}
- Battle Format: Single Battle

Create a detailed battle plan with the following structure:

1. Overall Objective: A clear statement of your main goal to win the battle
2. Battle Phases: Break down the battle into 3-5 strategic phases (Early Game, Mid Game, Late Game, etc.)
3. Key Tactics: List 3-5 core tactical approaches
4. Risk Assessment: Identify major threats and counter-strategies

For each Battle Phase, include:
- Phase Name and Description
- Specific Objectives (what you want to achieve)
- Success Conditions (how you know the phase succeeded)
- Expected Duration (number of turns)
- Failure Risks (what could go wrong)

Respond in a structured format that can be easily parsed. Focus on concrete, actionable strategies.

Example Response Format:
Overall Objective: Establish early game momentum with lead Pokémon, then sweep with setup sweeper

Battle Phases:
1. Early Game Setup (Turns 1-3)
   - Establish field control with Stealth Rock
   - Scout opponent's lead and potential switches
   - Preserve key Pokémon health
   
2. Mid Game Pressure (Turns 4-8)
   - Apply consistent offensive pressure
   - Force opponent into unfavorable positions
   - Look for setup opportunities

Key Tactics:
- Priority move usage for speed control
- Type advantage exploitation
- Prediction-based switching

Risk Assessment:
- Major Threats: Opposing setup sweepers, priority moves
- Counter-Strategies: Maintain switch initiative, use status moves
";
        }

        private string BuildPlanAdjustmentPrompt(SingleBattlePlan currentPlan, GameState gameState, PlanEvaluationResult evaluation)
        {
            var battleData = gameState.BattleData;
            var planJson = SerializePlan(currentPlan);

            return $@"
You are a professional Pokémon battle strategist. The current battle plan needs adjustment based on the battle situation.

Current Battle State:
- Turn: {battleData.Turn}
- Battle Progress: {GetBattleProgressSummary(gameState)}

Current Plan:
{planJson}

Evaluation Results:
- Needs Adjustment: {evaluation.NeedsAdjustment}
- Reason: {evaluation.Reason}
- Adjustment Type: {evaluation.RecommendedAdjustmentType}
- Suggested Changes: {string.Join(", ", evaluation.SuggestedChanges)}

Adjust the battle plan accordingly. Maintain the same structure but update:
1. Phase statuses and objectives based on current situation
2. Risk assessment based on new threats
3. Key tactics if strategy needs to change
4. Overall objective if the situation has fundamentally changed

Provide the adjusted plan in the same structured format.
";
        }

        private string BuildPlanEvaluationPrompt(SingleBattlePlan plan, GameState gameState)
        {
            var battleData = gameState.BattleData;
            var planJson = SerializePlan(plan);

            return $@"
You are evaluating a Pokémon battle plan's progress and determining if adjustments are needed.

Current Battle State:
- Turn: {battleData.Turn}
- My Team Status: {GetMyTeamStatus(gameState)}
- Opponent Team Status: {GetOpponentTeamStatus(gameState)}
- Field Conditions: {GetFieldConditions(gameState)}

Current Plan:
{planJson}

Evaluate the plan and respond with:
1. NeedsAdjustment: true/false
2. Reason: Brief explanation
3. AdjustmentType: Minor/Major/Complete
4. SuggestedChanges: List of specific changes needed

Consider:
- Are the plan objectives still achievable?
- Has the opponent's strategy invalidated parts of our plan?
- Are we ahead/behind schedule compared to expected progress?
- Have new threats or opportunities emerged?

Respond in JSON format:
{{
  ""needsAdjustment"": true/false,
  ""reason"": ""explanation"",
  ""adjustmentType"": ""Minor""/""Major""/""Complete"",
  ""suggestedChanges"": [""change1"", ""change2""]
}}
";
        }

        private (string OverallObjective, List<BattlePhase> BattlePhases, List<string> KeyTactics, RiskAssessment RiskAssessment) ParseLLMResponseToPlan(string response, GameState gameState)
        {
            // 这是一个简化的解析器，实际实现中可能需要更复杂的NLP处理
            // 或者要求LLM返回结构化的JSON格式

            var objective = ExtractSection(response, "Overall Objective");
            var keyTactics = ExtractListItems(response, "Key Tactics");
            var phases = ParseBattlePhases(response);
            var riskAssessment = ParseRiskAssessment(response);

            return (objective, phases, keyTactics, riskAssessment);
        }

        private PlanEvaluationResult ParseEvaluationResponse(string response)
        {
            try
            {
                // 尝试解析JSON格式的响应
                var cleanedResponse = response.Replace("```json", "").Replace("```", "").Trim();
                var evaluationData = JsonSerializer.Deserialize<JsonElement>(cleanedResponse);

                return new PlanEvaluationResult
                {
                    NeedsAdjustment = evaluationData.GetProperty("needsAdjustment").GetBoolean(),
                    Reason = evaluationData.GetProperty("reason").GetString() ?? "",
                    RecommendedAdjustmentType = Enum.Parse<AdjustmentType>(evaluationData.GetProperty("adjustmentType").GetString() ?? "Minor"),
                    SuggestedChanges = evaluationData.GetProperty("suggestedChanges").EnumerateArray()
                        .Select(x => x.GetString() ?? "").ToList()
                };
            }
            catch
            {
                // 如果解析失败，返回保守的评估结果
                return new PlanEvaluationResult
                {
                    NeedsAdjustment = false,
                    Reason = "Unable to parse evaluation response"
                };
            }
        }

        private SingleBattlePlan UpdatePlanProgress(SingleBattlePlan currentPlan, GameState gameState)
        {
            // 更新阶段状态和目标完成情况
            var updatedPhases = currentPlan.BattlePhases.Select(phase => 
                UpdatePhaseProgress(phase, gameState)).ToList();

            return currentPlan with
            {
                BattlePhases = updatedPhases,
                LastUpdated = DateTime.UtcNow
            };
        }

        private BattlePhase UpdatePhaseProgress(BattlePhase phase, GameState gameState)
        {
            // 根据当前战斗状态更新阶段进度
            // 这里可以添加更复杂的逻辑来判断阶段是否完成
            return phase with
            {
                ActualTurns = Math.Max(phase.ActualTurns, gameState.BattleData.Turn)
            };
        }

        // 各种辅助方法用于获取战斗状态信息
        private string GetTeamSummary(BattleData battleData, int slot) => "Team summary placeholder";
        private string GetBattleProgressSummary(GameState gameState) => "Battle progress placeholder";
        private string GetMyTeamStatus(GameState gameState) => "My team status placeholder";
        private string GetOpponentTeamStatus(GameState gameState) => "Opponent team status placeholder";
        private string GetFieldConditions(GameState gameState) => "Field conditions placeholder";
        
        // 文本解析辅助方法
        private string ExtractSection(string text, string sectionName) => $"Extracted {sectionName}";
        private List<string> ExtractListItems(string text, string sectionName) => new() { "Tactic 1", "Tactic 2" };
        private List<BattlePhase> ParseBattlePhases(string text) => new();
        private RiskAssessment ParseRiskAssessment(string text) => new();

        #endregion
    }
}