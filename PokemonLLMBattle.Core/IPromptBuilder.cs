using Microsoft.Extensions.AI;
using PokemonLLMBattle.Core.Models;
using Showdown;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PokemonLLMBattle.Core
{
    public interface IPromptBuilder
    {

        Task<IList<ChatMessage>> BuildForceSwitchPromptAsync(PromptContext context, ForceSwitchCondition forceSwitchCondition);
        Task<IList<ChatMessage>> BuildChooseMovePromptAsync(PromptContext context, ChooseCondition chooseCondition);
        Task<IList<ChatMessage>> BuildTeamOrderPromptAsync(PromptContext context, TeamOrderCondition teamOrderCondition);
        Task<IList<ChatMessage>> BuildSummaryBoBattlePromptAsync(PromptContext context);
        Task<IList<ChatMessage>> BuildSummaryTeamPromptAsync(PromptContext context);



        //Task<IList<ChatMessage>> BuildAnalysisPromptAsync(PromptContext context, PossibleAction specificAction);
    }

    public class PromptContext
    {
        //public PSBattle? Battle { get; set; }

        public GameState? CurrentState { get; set; }
        public BattleStrategy? Strategy { get; set; }
        public SingleBattlePlan? BattlePlan { get; set; }
        public List<Decision> DecisionHistory { get; set; } = new();
        public Dictionary<string, object> AdditionalContext { get; set; } = new();
    }

 
    public record GameState(
        PSBattle PSBattle,
        BattleData BattleData,
        LLMTrainerTeam MyTeam,
        StateCondition Condition,
        SingleBattlePlan? BattlePlan = null
        )
    {
        public static GameState CreateDefault(BattleData battleData)
        {
            return new GameState(null, battleData,  LLMTrainerTeam.CreateDefault(""), StateCondition.ChooseCondition, null);
        }
    }

    /// <summary>
    /// 战斗策略
    /// </summary>
    public class BattleStrategy
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double Aggressiveness { get; set; } // 0.0 - 1.0
        public double RiskTolerance { get; set; } // 0.0 - 1.0
        public List<string> AvoidedTactics { get; internal set; }
        public List<string> PreferredTactics { get; internal set; }
    }

}
