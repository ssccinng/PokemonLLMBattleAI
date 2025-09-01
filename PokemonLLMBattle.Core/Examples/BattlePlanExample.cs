using PokemonLLMBattle.Core;
using PokemonLLMBattle.Core.Models;
using System;
using System.Text.Json;

namespace PokemonLLMBattle.Core.Examples
{
    /// <summary>
    /// 单打对战计划系统使用示例
    /// Example usage of the Single Battle Plan System
    /// </summary>
    public static class BattlePlanExample
    {
        /// <summary>
        /// 创建一个示例对战计划
        /// Create an example battle plan
        /// </summary>
        public static SingleBattlePlan CreateExampleBattlePlan()
        {
            return new SingleBattlePlan
            {
                OverallObjective = "Establish early momentum with Garchomp lead, then sweep with setup opportunities",
                BattlePhases = new List<BattlePhase>
                {
                    new BattlePhase
                    {
                        PhaseName = "Early Game Setup",
                        Description = "Establish field control and scout opponent strategy",
                        Priority = 9,
                        ExpectedTurns = 3,
                        Status = PhaseStatus.InProgress,
                        Objectives = new List<PhaseObjective>
                        {
                            new PhaseObjective
                            {
                                Description = "Set up Stealth Rock for entry hazard damage",
                                Importance = 8,
                                Actions = new List<string> { "Use Stealth Rock on turn 1", "Maintain field control" }
                            },
                            new PhaseObjective
                            {
                                Description = "Scout opponent's lead Pokemon and moveset",
                                Importance = 7,
                                Actions = new List<string> { "Observe opponent moves", "Predict potential switches" }
                            }
                        },
                        SuccessConditions = new List<string>
                        {
                            "Stealth Rock successfully set up",
                            "Opponent strategy identified",
                            "Garchomp health >75%"
                        },
                        FailureRisks = new List<string>
                        {
                            "Garchomp knocked out early",
                            "Opponent sets up threats",
                            "Unable to establish field control"
                        }
                    },
                    new BattlePhase
                    {
                        PhaseName = "Mid Game Pressure",
                        Description = "Apply offensive pressure and force advantageous trades",
                        Priority = 8,
                        ExpectedTurns = 4,
                        Status = PhaseStatus.Pending,
                        Objectives = new List<PhaseObjective>
                        {
                            new PhaseObjective
                            {
                                Description = "Maintain offensive momentum",
                                Importance = 8,
                                Actions = new List<string> { "Use coverage moves", "Force switches", "Apply pressure" }
                            },
                            new PhaseObjective
                            {
                                Description = "Look for setup opportunities",
                                Importance = 6,
                                Actions = new List<string> { "Identify weak opponents", "Preserve setup Pokemon", "Create openings" }
                            }
                        }
                    },
                    new BattlePhase
                    {
                        PhaseName = "Late Game Execution",
                        Description = "Execute win condition and close out the battle",
                        Priority = 10,
                        ExpectedTurns = 3,
                        Status = PhaseStatus.Pending,
                        Objectives = new List<PhaseObjective>
                        {
                            new PhaseObjective
                            {
                                Description = "Set up sweeper Pokemon",
                                Importance = 10,
                                Actions = new List<string> { "Use setup moves", "Clear threats", "Begin sweep" }
                            }
                        }
                    }
                },
                KeyTactics = new List<string>
                {
                    "Lead with Garchomp for immediate pressure",
                    "Prioritize Stealth Rock setup",
                    "Use type advantages for favorable trades",
                    "Preserve setup sweeper for late game",
                    "Maintain switch initiative"
                },
                RiskAssessment = new RiskAssessment
                {
                    OverallRiskLevel = 6,
                    MajorThreats = new List<string>
                    {
                        "Ice-type moves against Garchomp",
                        "Priority moves disrupting setup",
                        "Opposing setup sweepers",
                        "Status conditions"
                    },
                    CounterStrategies = new List<string>
                    {
                        "Switch to resist Ice moves",
                        "Use faster Pokemon for speed control",
                        "Disrupt opponent setup with pressure",
                        "Carry status healing items"
                    },
                    BackupPlans = new List<string>
                    {
                        "Pivot to defensive strategy if Garchomp falls",
                        "Use alternative win conditions",
                        "Focus on hazard stacking if sweep fails"
                    }
                }
            };
        }

        /// <summary>
        /// 演示计划序列化和反序列化
        /// Demonstrate plan serialization and deserialization
        /// </summary>
        public static void DemonstratePlanSerialization()
        {
            var manager = new SingleBattlePlanManager(null!, null!); // Note: In real usage, inject proper dependencies
            var examplePlan = CreateExampleBattlePlan();

            // 序列化计划 (Serialize plan)
            var serializedPlan = manager.SerializePlan(examplePlan);
            Console.WriteLine("Serialized Battle Plan:");
            Console.WriteLine(serializedPlan);

            // 反序列化计划 (Deserialize plan)
            var deserializedPlan = manager.DeserializePlan(serializedPlan);
            Console.WriteLine($"\nDeserialized successfully: {deserializedPlan != null}");
            Console.WriteLine($"Plan ID: {deserializedPlan?.PlanId}");
            Console.WriteLine($"Overall Objective: {deserializedPlan?.OverallObjective}");
            Console.WriteLine($"Number of Phases: {deserializedPlan?.BattlePhases.Count}");
            Console.WriteLine($"Completion Percentage: {deserializedPlan?.CompletionPercentage}%");
        }

        /// <summary>
        /// 演示计划更新和调整
        /// Demonstrate plan updates and adjustments
        /// </summary>
        public static SingleBattlePlan DemonstratePlanUpdate(SingleBattlePlan originalPlan)
        {
            // 模拟阶段完成 (Simulate phase completion)
            var updatedPhases = originalPlan.BattlePhases.ToList();
            if (updatedPhases.Any())
            {
                // 完成第一个阶段的第一个目标 (Complete first objective of first phase)
                var firstPhase = updatedPhases[0];
                if (firstPhase.Objectives.Any())
                {
                    var updatedObjectives = firstPhase.Objectives.ToList();
                    updatedObjectives[0] = updatedObjectives[0] with { IsCompleted = true };
                    
                    updatedPhases[0] = firstPhase with 
                    { 
                        Objectives = updatedObjectives,
                        ActualTurns = 2
                    };
                }
            }

            // 添加计划调整记录 (Add plan adjustment record)
            var adjustment = new PlanAdjustment
            {
                AdjustmentTime = DateTime.UtcNow,
                Reason = "Successfully established Stealth Rock, moving to offensive phase earlier than expected",
                Type = AdjustmentType.Minor,
                Changes = "Accelerated transition to Mid Game Pressure phase",
                TurnNumber = 2
            };

            return originalPlan with
            {
                BattlePhases = updatedPhases,
                LastUpdated = DateTime.UtcNow,
                AdjustmentHistory = originalPlan.AdjustmentHistory.Concat(new[] { adjustment }).ToList()
            };
        }

        /// <summary>
        /// 运行完整的示例演示
        /// Run complete example demonstration
        /// </summary>
        public static void RunExample()
        {
            Console.WriteLine("=== Single Battle Plan System Example ===\n");

            // 创建示例计划 (Create example plan)
            var examplePlan = CreateExampleBattlePlan();
            Console.WriteLine($"Created battle plan: {examplePlan.OverallObjective}");
            Console.WriteLine($"Initial completion: {examplePlan.CompletionPercentage}%\n");

            // 演示序列化 (Demonstrate serialization)
            DemonstratePlanSerialization();

            // 演示计划更新 (Demonstrate plan update)
            Console.WriteLine("\n=== Plan Update Demonstration ===");
            var updatedPlan = DemonstratePlanUpdate(examplePlan);
            Console.WriteLine($"Updated completion: {updatedPlan.CompletionPercentage}%");
            Console.WriteLine($"Number of adjustments: {updatedPlan.AdjustmentHistory.Count}");
            
            if (updatedPlan.AdjustmentHistory.Any())
            {
                var lastAdjustment = updatedPlan.AdjustmentHistory.Last();
                Console.WriteLine($"Last adjustment: {lastAdjustment.Reason}");
            }

            Console.WriteLine("\n=== Battle Plan System Ready for Use ===");
        }
    }
}