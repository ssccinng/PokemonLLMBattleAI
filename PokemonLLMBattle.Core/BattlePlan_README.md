# 单打对战计划系统 (Single Battle Plan System)

这个系统实现了智能的单打宝可梦对战计划功能，为大模型提供整体的对战规划能力。

This system implements an intelligent single battle plan feature for Pokémon battles, providing comprehensive battle planning capabilities for LLM-based trainers.

## 功能特性 (Features)

### 1. 对战开始时规划任务目标 (Initial Battle Planning)
- 在对战开始时自动创建综合性战斗计划
- 制定明确的胜利目标和策略路径
- 基于双方队伍分析制定针对性策略

### 2. 每回合计划更新 (Turn-by-Turn Plan Updates)
- 自动更新计划完成度和进度
- 根据当前对战局势动态调整策略
- 记录计划调整历史

### 3. 可序列化代码对象 (Serializable Code Objects)
- 计划可直接序列化为JSON格式
- 支持从JSON完全反序列化
- 便于后期调用和持久化存储

## 核心组件 (Core Components)

### 模型 (Models)
- **SingleBattlePlan**: 主要的对战计划对象
- **BattlePhase**: 战斗阶段（早期、中期、后期等）
- **PhaseObjective**: 阶段具体目标
- **RiskAssessment**: 风险评估和应对策略
- **PlanAdjustment**: 计划调整记录

### 管理器 (Manager)
- **IBattlePlanManager**: 对战计划管理接口
- **SingleBattlePlanManager**: 具体实现，包括计划创建、更新、评估

### 集成 (Integration)
- 与现有 `LLMDecisionEngine` 无缝集成
- 更新 `GameState` 包含对战计划
- 增强提示构建器包含计划上下文

## 使用方法 (Usage)

### 基本用法 (Basic Usage)

```csharp
// 创建决策引擎时会自动集成对战计划管理器
var decisionEngine = new LLMDecisionEngine(chatClient, simpleClient, promptBuilder);

// 在决策过程中自动创建和更新对战计划
var decision = await decisionEngine.MakeDecisionAsync(gameState, cancellationToken);
```

### 手动创建计划 (Manual Plan Creation)

```csharp
var battlePlanManager = new SingleBattlePlanManager(chatClient, promptBuilder);

// 创建初始计划
var initialPlan = await battlePlanManager.CreateInitialBattlePlanAsync(gameState);

// 更新计划
var updatedPlan = await battlePlanManager.UpdateBattlePlanAsync(currentPlan, gameState);

// 序列化计划
var planJson = battlePlanManager.SerializePlan(plan);

// 反序列化计划
var deserializedPlan = battlePlanManager.DeserializePlan(planJson);
```

### 示例演示 (Example Demonstration)

```csharp
using PokemonLLMBattle.Core.Examples;

// 运行完整示例
BattlePlanExample.RunExample();

// 创建示例计划
var examplePlan = BattlePlanExample.CreateExampleBattlePlan();

// 演示序列化
BattlePlanExample.DemonstratePlanSerialization();
```

## 计划结构 (Plan Structure)

### 战斗阶段 (Battle Phases)
1. **早期阶段** (Early Game): 场地控制、侦察对手
2. **中期阶段** (Mid Game): 施加压力、创造优势
3. **后期阶段** (Late Game): 执行胜利条件

### 目标类型 (Objective Types)
- 设置入场伤害（如隐形岩）
- 侦察对手策略
- 维持攻击势头
- 寻找设置机会
- 执行扫场策略

### 风险管理 (Risk Management)
- 主要威胁识别
- 应对策略制定
- 后备计划准备

## 自动化特性 (Automation Features)

### 智能评估 (Intelligent Evaluation)
- 自动评估计划执行情况
- 检测是否需要调整策略
- 推荐具体调整方案

### 适应性调整 (Adaptive Adjustments)
- 轻微调整：微调当前策略
- 重大调整：改变阶段优先级
- 完全重规划：应对根本性变化

### 上下文集成 (Context Integration)
- 所有决策提示都包含当前计划状态
- 显示当前阶段目标和进度
- 提供风险评估信息

## 数据持久化 (Data Persistence)

计划数据自动保存到战斗附加信息中：
```csharp
gameState.PSBattle.Additions["battle_plan"] = serializedPlanJson;
```

## 扩展性 (Extensibility)

系统设计支持未来扩展：
- 可添加新的阶段类型
- 支持自定义目标类型
- 可集成机器学习改进

## 性能考虑 (Performance Considerations)

- 计划评估使用中等精度以平衡质量和速度
- JSON序列化针对可读性优化
- 缓存机制减少重复计算

## 注意事项 (Notes)

1. 确保 `IChatClient` 支持推理能力
2. 计划创建需要网络调用，考虑超时处理
3. 大型计划的序列化可能影响性能
4. 建议在测试环境中验证计划逻辑

## 示例输出 (Example Output)

```json
{
  "planId": "abc-123-def",
  "overallObjective": "Establish early momentum with Garchomp lead, then sweep with setup opportunities",
  "battlePhases": [
    {
      "phaseName": "Early Game Setup",
      "description": "Establish field control and scout opponent strategy",
      "status": "InProgress",
      "priority": 9,
      "expectedTurns": 3,
      "actualTurns": 1,
      "objectives": [
        {
          "description": "Set up Stealth Rock for entry hazard damage",
          "isCompleted": false,
          "importance": 8,
          "actions": ["Use Stealth Rock on turn 1", "Maintain field control"]
        }
      ]
    }
  ],
  "keyTactics": [
    "Lead with Garchomp for immediate pressure",
    "Prioritize Stealth Rock setup"
  ],
  "status": "Active",
  "completionPercentage": 0
}
```

这个系统为宝可梦对战AI提供了强大的战略规划能力，帮助大模型制定和执行更加智能的对战策略。