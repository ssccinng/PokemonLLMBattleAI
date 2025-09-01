using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Microsoft.Extensions.AI;
using Org.BouncyCastle.Ocsp;
using PokeCommon.Models;
using PokeCommon.PokemonShowdownTools;
using PokeCommon.Utils;
using PokemonDataAccess;
using PokemonDataAccess.Models;
using PokemonLLMBattle.Core.Models;
using Showdown;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

// 战斗要点
// 1. 配置推测，这可能要结合当场表现
// 2. 战斗要点规划，先规划出对战计划，再根据计划选择出招
// 3. 出招的结果，计算反哺


namespace PokemonLLMBattle.Core
{

    file static class SingleBattlePrompts
    {
        public static readonly string ChooseMoveSystemPrompt = File.ReadAllText("Prompts/SVSingle/ChooseMovePrompt.txt");
        public static readonly string TypeKnowledgePrompt = File.ReadAllText("Prompts/SVSingle/TypeKnowledge.txt");
    }

    public record BattlePlanStep(string PlanStep, bool IsOver);
    public record BattlePlan(List<BattlePlanStep> Steps);


    internal class SingleBattlePromptBuilder : IPromptBuilder
    {
        string BuildTeamInfoPrompt(PromptContext context)
        {
            // 如果明牌，则直接使用队伍信息
            // 如果暗牌，则用自己的队伍信息和对手部分队伍信息推理，总之 先考虑明牌的
            var getTeam =
                    (Option<GamePokemonTeam> team) =>
                            from mt in team
                            from p in mt.GamePokemons
                            let baseStats = p.MetaPokemon
                            select new
                            {
                                pokemon = p.MetaPokemon?.PSPokemon.PSName,
                                item = p.Item?.Name_Eng,
                                ability = p.Ability?.Name_Eng,
                                teraType = p.TreaType?.Name_Eng, // 如果由太晶可为null
                                moves = p.Moves.Select(s => s.MetaMove?.Name_Eng).ToList(),
                                nature = p.Nature?.Name_Eng,
                                //evs = p.EVs,
                                type = (new[] { baseStats?.Type1?.Name_Eng, baseStats?.Type2?.Name_Eng }).Where(s => !string.IsNullOrEmpty(s)),
                                // 可能还需实际数值
                            };
            var getTeam1 =
                    (Option<GamePokemonTeam> team) =>
                            from mt in team
                            from p in mt.GamePokemons
                            let baseStats = p.MetaPokemon
                            select new
                            {
                                pokemon = p.MetaPokemon?.PSPokemon.PSName,
                                item = p.Item?.Name_Eng,
                                ability = p.Ability?.Name_Eng,
                                teraType = p.TreaType?.Name_Eng, // 如果由太晶可为null
                                moves = p.Moves.Select(s => s.MetaMove?.Name_Eng).ToList(),
                                //evs = p.Evs,
                                nature = p.Nature?.Name_Eng,
                                //ivs = p.Ivs,
                                type = (new[] { baseStats?.Type1?.Name_Eng, baseStats?.Type2?.Name_Eng }).Where(s => !string.IsNullOrEmpty(s)),

                                stats = new
                                {
                                    hp = baseStats?.BaseHP,
                                    atk = baseStats?.BaseAtk,
                                    def = baseStats?.BaseDef,
                                    spa = baseStats?.BaseSpa,
                                    spd = baseStats?.BaseSpd,
                                    spe = baseStats?.BaseSpe
                                }
                            };
            var battleData = context.CurrentState?.BattleData;

            var gameState = context.CurrentState;
            //var myTeam = getTeam(gameState?.MyTeam?.ToGamePokemonTeam());
            //var oppTeam = getTeam(gameState?.CurrentState?.BattleData?.OppTeam);

            var myTeam = getTeam(battleData.PlayerDatas[battleData.MySlot].Team);
            var oppTeam = getTeam(battleData.PlayerDatas[1 - battleData.MySlot].Team);

            var prompt = new StringBuilder();
            prompt.AppendLine("Team Information:");
            prompt.AppendLine("**MY TEAM**:");
            prompt.AppendLine(JsonSerializer.Serialize(myTeam, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            }));

            if (oppTeam.Any())
            {
                prompt.AppendLine("**OPPONENT TEAM**:");
                prompt.AppendLine(JsonSerializer.Serialize(oppTeam, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                }));
            }

            return prompt.ToString();
        }

        /// <summary>
        /// 构建当前对局信息知识提示
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        string BuildGameStateKnowledgePrompt(PromptContext context)
        {
            var gamestate = context.CurrentState;
            if (gamestate == null)
                throw new ArgumentNullException(nameof(context));

            var prompt = new StringBuilder();
            
            prompt.AppendLine("Game Knowledge:");
            prompt.AppendLine("1. This is a Pokémon single battle format.");
            prompt.AppendLine("2. Each player has 1 active Pokémon on the field at a time.");
            prompt.AppendLine("3. You can switch to any non-fainted Pokémon from your team.");
            prompt.AppendLine("4. Terastallization can only be used once per battle.");
            prompt.AppendLine("5. Speed determines turn order, with faster Pokémon generally moving first.");
            prompt.AppendLine("6. Consider type effectiveness, stat boosts, and status conditions when making decisions.");

            return prompt.ToString();
        }

        /// <summary>
        /// 构建当前对局信息提示
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        string BuildGameStatePrompt(PromptContext context)
        {
            var battleData = context.CurrentState?.BattleData;
            var lastTurn = battleData?.GetLastTurn();
            var prompt = new StringBuilder();

            if (battleData == null || lastTurn == null)
            {
                prompt.AppendLine("Battle data not available.");
                return prompt.ToString();
            }

            var mySideTeam = lastTurn.SideTeam[battleData.MySlot];
            var oppSideTeam = lastTurn.SideTeam[1 - battleData.MySlot];

            if (battleData.OpenSheet)
            {
                var getNewTeam =
                     (Option<GamePokemonTeam> team, BattleTeam sideTeam) =>
                             from mt in team
                             from p in mt.GamePokemons
                             join msp in sideTeam.Pokemons
                             on p.MetaPokemon!.Id equals msp.Pokemon.MetaPokemon?.Id
                             let baseStats = p.MetaPokemon
                             select new
                             {
                                 pokemon = msp.Pokemon.MetaPokemon?.PSPokemon.PSName,
                                 item = msp.Item.IsSome ? p.Item?.Name_Eng : "No Item",
                                 ability = p.Ability?.Name_Eng,
                                 teraType = p.TreaType?.Name_Eng, // 如果由太晶可为null
                                 moves = p.Moves.Select(s =>
                                     s.MetaMove?.Name_Eng
                                 ).ToList(),
                                 nature = p.Nature?.Name_Eng,
                                 lastUseMove = msp.LastMove.IsSome ? msp.LastMove.ValueUnsafe().Name_Eng : "No move used",
                                 //evs = p.EVs, //Todo 这个ev要不要加入
                                 type = (new[] { baseStats?.Type1?.Name_Eng, baseStats?.Type2?.Name_Eng }).Where(s => !string.IsNullOrEmpty(s)),
                                 //ivs = p.IVs, // 可能不需要
                                 hpRemain = $"{msp.HpRemain}%",
                                 battleStatus = msp.BattleStatus.GetStatusPrompt(),
                                 teraStallized = msp.TeratallizeStatus.GetStatusPrompt(),
                                 status = msp.Status.GetType().GetProperties()
                 .Where(p => p.Name != "ProtectCnt" && p.Name != "InField_First_Turn")
                                     .Select(p => (p.Name, (int)p.GetValue(msp.Status)))
                                     .Where(s => s.Item2 != 0)
                                     .Select(s => $"{s.Item1}: {s.Item2}"),
                                 first_turn_on_the_field = msp.Status.InField_First_Turn > 0 ? "true" : "false(cant use move like fake out)",
                                 protectCnt = msp.Status.ProtectCnt,
                                 //.Select(s => new { status = s.Item1, value = s.Item2 })
                             };
                //var myTeam = battleData.MyTeam;
                //var oppTeam = battleData.OppTeam;

                LanguageExt.Option<PokeCommon.Models.GamePokemonTeam> myTeam = battleData.PlayerDatas[battleData.MySlot].Team;
                //var mySideTeam = lastTurn.SideTeam[battleData.MySlot];
                var oppTeam = battleData.PlayerDatas[1 - battleData.MySlot].Team;
                var myNewTeam = getNewTeam(myTeam, mySideTeam);
                var oppNewTeam = getNewTeam(oppTeam, oppSideTeam);

                prompt.AppendLine("**MY TEAM**:");
                // 是否可以太晶化
                prompt.AppendLine($"Can Teratallize: {mySideTeam.CanTerastallize}");

                prompt.AppendLine(JsonSerializer.Serialize(myNewTeam, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                }));

                prompt.AppendLine("**OPPONENT TEAM**:");
                prompt.AppendLine($"Can Teratallize: {oppSideTeam.CanTerastallize}");

                prompt.AppendLine(JsonSerializer.Serialize(oppNewTeam, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                }));
            }
            else
            {
                // Hidden information battle - only show what's visible
                prompt.AppendLine("**MY TEAM**:");
                prompt.AppendLine($"Can Teratallize: {mySideTeam.CanTerastallize}");
                
                // Show my team with full information
                var myVisibleTeam = mySideTeam.Pokemons.Select(msp => new
                {
                    pokemon = msp.Pokemon.MetaPokemon?.PSPokemon.PSName,
                    //level = msp.Pokemon,
                    hp = $"{msp.HpRemain}/{100}",
                    //status = typeof(PokeCommon.Models.PokemonStatus)
                    //    .GetProperties()
                    //    .Select(p => (p.Name, (int)p.GetValue(msp.Status)))
                    //    .Where(s => s.Item2 != 0)
                    //    .Select(s => $"{s.Item1}: {s.Item2}"),
                    first_turn_on_the_field = msp.Status.InField_First_Turn > 0 ? "true" : "false(cant use move like fake out)",
                    protectCnt = msp.Status.ProtectCnt,
                });

                prompt.AppendLine(JsonSerializer.Serialize(myVisibleTeam, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                }));

                prompt.AppendLine("**OPPONENT TEAM** (visible information only):");
                prompt.AppendLine($"Can Teratallize: {oppSideTeam.CanTerastallize}");
                // 可见宝，
                // Show only visible opponent information
                var oppVisibleTeam = oppSideTeam.Pokemons
                    //.Where(p => p.Pokemon)
                    .Select(msp => new
                {
                    pokemon = msp.Pokemon.MetaPokemon?.PSPokemon.PSName,
                    //level = msp.Pokemon.Level,
                    hp = $"{msp.HpRemain}/{100}",
                    //status = typeof(PokeCommon.Models.PokemonStatus)
                    //    .GetProperties()
                    //    .Select(p => (p.Name, (int)p.GetValue(msp.Status)))
                    //    .Where(s => s.Item2 != 0)
                    //    .Select(s => $"{s.Item1}: {s.Item2}"),
                    first_turn_on_the_field = msp.Status.InField_First_Turn > 0 ? "true":"false(cant use move like fake out)",
                    protectCnt = msp.Status.ProtectCnt,
                });

                prompt.AppendLine(JsonSerializer.Serialize(oppVisibleTeam, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                }));
            }

            return prompt.ToString();
        }

        public string BuildTeamOrderDecisionRequestPrompt(PromptContext context, TeamOrderCondition teamOrderCondition)
        {
            var battleData = context.CurrentState?.BattleData;
            var myTeam = battleData?.GetMyTeam().ValueUnsafe();

            if (myTeam == null)
            {
                return "Team data not available.";
            }

            var teamList = myTeam.GamePokemons.Select(p => p.MetaPokemon?.PSPokemon.PSName).ToList();
            var prompt = $@"=== Team Order Selection ===
Please select your team order for this single battle.
You need to choose 1 lead Pokémon + 5 reserves from your 6 Pokémon team.

Available Pokémon: {string.Join(", ", teamList)}

PLEASE STRICTLY FOLLOW THE EXAMPLE_OUTPUT_FORMAT
Do not include extra output like ```json

EXAMPLE_OUTPUT_FORMAT:
{{
    ""reason"": ""<your_reasoning_for_team_order>"",
    ""tactics"": {{
        ""overall_approach"": ""Overall tactical approach for single battle: ..."",
        ""lead_pokemon"": {{
            ""pokemon"": ""<lead_pokemon_name>"",
            ""role"": ""Expected role and strategy: ...""
        }},
        ""terastallization"": ""When to use Terastallization: ...""
    }},
    ""order"": [""<pokemon1>"", ""<pokemon2>"", ""<pokemon3>"", ""<pokemon4>"", ""<pokemon5>"", ""<pokemon6>""]
}}";

            return prompt;
        }

        public string BuildForceSwitchDecisionRequestPrompt(PromptContext context, ForceSwitchCondition forceSwitchCondition)
        {
            var battleData = context.CurrentState?.BattleData;
            var req = battleData?.GetLastTurn()?.Requests?.LastOrDefault();
            
            if (req == null)
                return "Battle data not available.";

            // Get switchable Pokémon (all non-fainted Pokémon that aren't active)
            var canSwitch = string.Join(",", req.side.pokemon
                .Where(s => !s.condition.Contains("fnt") && !s.active)
                .Select(s => s.ident.Split(":")[1].Trim()));

            if (string.IsNullOrEmpty(canSwitch))
            {
                canSwitch = "No switchable Pokémon";
            }

            var prompt = $@"=== Forced Switch ===
You must switch in a Pokémon because your current Pokémon fainted or was forced out.

Available Pokémon to switch in: [{canSwitch}]

PLEASE STRICTLY FOLLOW THE EXAMPLE_OUTPUT_FORMAT
Do not include extra output like ```json
**No other extra output allowed!!!**

EXAMPLE_OUTPUT_FORMAT:
{{
    {(battleData.OpenSheet ? "" : @"""opponent_pokemon_prediction"": ""<opponent_team_JSON>"",")}
    ""think"": ""<your_reasoning_for_switch>"",
    ""switch"": ""<pokemon_name_to_switch_in>""
}}";

            return prompt;
        }

        public string BuildChooseMoveDecisionRequestPrompt(PromptContext context, ChooseCondition chooseCondition)
        {
            var battleData = context.CurrentState?.BattleData;
            var req = battleData?.GetLastTurn()?.Requests?.LastOrDefault();
            
            if (req == null)
                return "Battle data not available.";

            // Get switchable Pokémon (skipping the first one which is active, for single battles we only have 1 active)
            var canSwitch = string.Join(",", req.side.pokemon.Skip(1)
                .Where(s => !s.condition.Contains("fnt"))
                .Select(s => $"\"{s.ident.Split(":")[1].Trim()}\""));

            // Get active Pokémon moves for single battle (only 1 active Pokémon)
            var activeMoves = "";
            var activePokemon = req.side.pokemon.FirstOrDefault(p => p.active && !p.condition.Contains("fnt"));
            var activeData = req.active?.FirstOrDefault();
            
            if (activePokemon != null && activeData != null)
            {
                var moves = string.Join(",", activeData.moves.Where(s => !s.disabled).Select(s => "\"" + s.move + "\""));
                activeMoves = $"{activePokemon.ident.Split(":")[1].Trim()}: [{(string.IsNullOrEmpty(moves) ? "No available moves" : moves)}]";
            }

            if (string.IsNullOrEmpty(canSwitch))
            {
                canSwitch = "No switchable Pokémon";
            }

            var prompt = @$"=== Please make a decision ===
Please analyze the current situation and choose the best action

AVAILABLE MOVES THIS TURN: [{activeMoves}]
ONLY THOSE POKEMON YOU CAN SWITCH IN FIELD: [{canSwitch}]

PLEASE STRICTLY FOLLOW THE EXAMPLE_OUTPUT_FORMAT
Do not include extra output like ```json
**No other extra output allowed!!!**

EXAMPLE_OUTPUT_FORMAT:
{{
    {(battleData.OpenSheet ? "" : @"""opponent_pokemon_prediction"": ""<opponent_team_JSON>"",")}
    ""think"": ""<your_reasoning>"",
    ""action"": {{
        ""move"": {{
            ""name"": ""<move_name>"", 
            ""terastallize"": <true_or_false>,
            ""move_target_pokemon"": ""<target_pokemon_name>"",
            ""target_side"": ""<ally|opp>""
        }}
    }}
}}

OR for switching:

{{
    {(battleData.OpenSheet ? "" : @"""opponent_pokemon_prediction"": ""<opponent_team_JSON>"",")}
    ""think"": ""<your_reasoning>"",
    ""action"": {{
        ""switch"": ""<pokemon_name_to_switch_in>""
    }}
}}";

            return prompt;
        }

        // BuildTeamOrderSystemPrompt
        string BuildTeamOrderSystemPrompt(PromptContext context, TeamOrderCondition teamOrderCondition)
        {
            var battleData = context.CurrentState?.BattleData;
            var sysP1 = @"
You are a professional Pokémon single battle trainer aiming to defeat all opponent Pokémon and secure victory.
You need to select your team order from 6 available Pokémon based on your and your opponent's team compositions. The first Pokémon will be your lead, the remaining 5 are reserves.
Your responsibilities:
1. Analyze both teams and choose appropriate lead Pokémon.
2. Evaluate all possible lead options.
3. Choose the optimal team order.
4. Predict the opponent's strategy and adjust decisions.
5. Provide detailed reasoning, output selection strategy and role for your lead Pokémon in the 'reason' field.

Decision principles:
- Prioritize win probability.
- Assess risks and benefits logically.
- Adapt strategy based on opponent behavior.
- Maintain calm and rational analysis.

You should establish a battle strategy to guide subsequent decisions. The strategy should include:
- overall tactical purpose
- how to counter opponent strategies
- how to implement your own tactics
- how to defeat potential key opponent Pokémon

Please reply strictly following the Example Output format with no extra output.
The 'tactics' field should be formatted as:
""tactics"": {
    ""overall_approach"": ""Overall tactical approach: ...""
    ""lead_pokemon"": {
        ""pokemon"": ""<lead_pokemon_name>""
        ""role"": ""Expected role: ...""
    }
    ""terastallization"": ""When to use Terastallization: ...""
}";
            return sysP1;
        }

        string BuildForceSwitchSystemPrompt(PromptContext context)
        {
            var battleData = context.CurrentState?.BattleData;
            var lastTurn = battleData?.GetLastTurn();
            var prompt = $@"
You are a professional Pokémon single battle expert. Analyze the current game state and provide optimal switching decisions.
Now you must choose which Pokémon to switch in.

{(battleData.OpenSheet ? "" : "This is a hidden information battle; infer the opponent's team composition.\n")}
Your potential tactics:
-
{(lastTurn.SideTeam[battleData.MySlot].CanTerastallize ? "Should Terastallize and why: " : "")}
";
            return prompt;
        }

        string BuildChooseMoveSystemPrompt(PromptContext context)
        {
            var battleData = context.CurrentState?.BattleData;
            var lastTurn = battleData?.GetLastTurn();
            
            var prompt = $@"
You are a professional Pokémon single battle strategist. Analyze the current battle state and provide optimal actions (move or switch) for your active Pokémon.

{(battleData.OpenSheet ? "" : "This is a hidden-information battle; infer the opponent's team composition.\n")}

Always consider the possibility that your opponent might use Protect, as it can completely block your move for that turn.
Always be mindful of potential incoming damage from your opponent and avoid exposing your key Pokémon to heavy hits unless absolutely necessary.

Consider the following points:
1. Evaluate the current battle progress and situation.
2. Anticipate the opponent's potential actions (using moves or switching Pokémon).
3. Determine the optimal strategy for your Pokémon (such as switching, attacking to KO the opponent, setting up, or applying pressure).
4. Assess whether your chosen action can further increase your advantage.
5. Analyze the risks associated with your decisions.

Decision principles:
- Prioritize winning probability
- Reasonably assess risks and benefits
- Adjust strategy based on opponent behavior
- Maintain calm and rational analysis

output this think in ""think"" field

{(lastTurn.SideTeam[battleData.MySlot].CanTerastallize ? "Should Terastallize and provide rationale: ..." : "")}""
";
            return prompt;
        }

        string BuildTypeKnowledgePrompt()
        {
            return SingleBattlePrompts.TypeKnowledgePrompt;
        }

        string BuildHistoryKnowledgePrompt(PromptContext promptContext)
        {
            // 既定战术
            var prompt = new StringBuilder();
            var gamestate = promptContext.CurrentState;
            if (gamestate?.PSBattle == null)
                return "";

            prompt.AppendLine("Historical Knowledge:");
            prompt.AppendLine("- Tactics reasoning during team selection: ");
            if (gamestate.PSBattle.Additions.TryGetValue("order tactics", out var t))
            {
                prompt.AppendLine(t as string);
            }
            if (promptContext.CurrentState?.BattleData.BattleInfo is BoBattle bo)
            {
                prompt.AppendLine("- Previous best-of match experience: ");
                for (int i = 0; i < bo.BoExp.Length; i++)
                {
                    var exp = bo.BoExp[i];
                    if (exp is not null && exp.Length > 0)
                    {
                        prompt.AppendLine($"Game {i + 1}:\n{string.Join("\n", exp)}");
                    }
                }
            }

            return prompt.ToString();
        }

        string BuildTeamOrderHistoryKnowledgePrompt(PromptContext promptContext)
        {
            // Similar to BuildHistoryKnowledgePrompt but focused on team ordering
            var prompt = new StringBuilder();
            
            prompt.AppendLine("Team Ordering Knowledge:");
            prompt.AppendLine("- Consider previous team performance and matchup experience");
            prompt.AppendLine("- Adapt lead selection based on opponent's likely strategy");
            
            if (promptContext.CurrentState?.BattleData.BattleInfo is BoBattle bo)
            {
                prompt.AppendLine("- Previous best-of match experience for team ordering: ");
                for (int i = 0; i < bo.BoExp.Length; i++)
                {
                    var exp = bo.BoExp[i];
                    if (exp is not null && exp.Length > 0)
                    {
                        prompt.AppendLine($"Game {i + 1} insights:\n{string.Join("\n", exp)}");
                    }
                }
            }

            return prompt.ToString();
        }

        public async Task<IList<ChatMessage>> BuildChooseMovePromptAsync(PromptContext context, ChooseCondition chooseCondition)
        {
            var SystemPrompt = BuildChooseMoveSystemPrompt(context);
            var gamestate = BuildGameStatePrompt(context);
            var gamestateKnowledge = BuildGameStateKnowledgePrompt(context);

            var typeKnowledge = BuildTypeKnowledgePrompt();
            var historyKnowledge = BuildHistoryKnowledgePrompt(context);
            var decisionRequestPrompt = BuildChooseMoveDecisionRequestPrompt(context, chooseCondition);

            List<ChatMessage> messages = [
                new ChatMessage(ChatRole.System, $"{SystemPrompt}\n{typeKnowledge}\n{gamestateKnowledge}\n{historyKnowledge}"),
                new ChatMessage(ChatRole.User, gamestate),
                new ChatMessage(ChatRole.System, decisionRequestPrompt)
                ];
            return messages;
        }

        public async Task<IList<ChatMessage>> BuildForceSwitchPromptAsync(PromptContext context, ForceSwitchCondition forceSwitchCondition)
        {
            var SystemPrompt = BuildForceSwitchSystemPrompt(context);
            var gamestate = BuildGameStatePrompt(context);
            var gamestateKnowledge = BuildGameStateKnowledgePrompt(context);

            var typeKnowledge = BuildTypeKnowledgePrompt();
            var historyKnowledge = BuildHistoryKnowledgePrompt(context);
            var decisionRequestPrompt = BuildForceSwitchDecisionRequestPrompt(context, forceSwitchCondition);

            List<ChatMessage> messages = [
                new ChatMessage(ChatRole.System, $"{SystemPrompt}\n{typeKnowledge}\n{gamestateKnowledge}\n{historyKnowledge}"),
                new ChatMessage(ChatRole.User,gamestate),
                new ChatMessage(ChatRole.System, decisionRequestPrompt)
                ];
            return messages;
        }

        public async Task<IList<ChatMessage>> BuildTeamOrderPromptAsync(PromptContext context, TeamOrderCondition teamOrderCondition)
        {
            // 先构建系统提示
            var SystemPrompt = BuildTeamOrderSystemPrompt(context, teamOrderCondition);
            // 构建队伍信息提示
            var teamInfoPrompt = BuildTeamInfoPrompt(context);

            // 构建队伍知识库提示
            var teamKnowledgePrompt = BuildGameStateKnowledgePrompt(context);

            // 构建历史知识提示
            var historyKnowledgePrompt = BuildTeamOrderHistoryKnowledgePrompt(context);

            // 构建决策请求提示
            var decisionRequestPrompt = BuildTeamOrderDecisionRequestPrompt(context, teamOrderCondition);

            List<ChatMessage> messages = [
                new ChatMessage(ChatRole.System, $"{SystemPrompt}\n{teamKnowledgePrompt}\n{historyKnowledgePrompt}"),
                new ChatMessage(ChatRole.User, teamInfoPrompt),
                new ChatMessage(ChatRole.System, decisionRequestPrompt)
            ];
            return messages;

        }

        string GetBattleLog(BattleData battle)
        {
            var prompt = "Battle Log:\n" +
               string.Join("\n", battle.BattleTurns.SelectMany(s => s.TurnLog));

            return prompt;
        }

        // BuildSummaryBoBattlePromptAsync
        public async Task<IList<ChatMessage>> BuildSummaryBoBattlePromptAsync(PromptContext context)
        {
            var teamInfoPrompt = BuildTeamInfoPrompt(context);
            var teamKnowledgePrompt = BuildGameStateKnowledgePrompt(context);
            var prompt = $@"
You are a professional Pokémon battle master conducting a best‐of‐three series. Summarize your experience from the last single battle match to improve future performance.
Your name in this match was {context.CurrentState?.BattleData.MyName}, and you {(context.CurrentState?.BattleData.Win ?? false ? "won" : "lost")} this match.
Please provide insights on:
- The key factors that determined the outcome of the single battle.
- Whether you should try new tactical approaches (choosing moves/team order).
";
            var battleLog = GetBattleLog(context.CurrentState?.BattleData ?? new());
            // 可能还需要之前的对战经验
            List<ChatMessage> messages = [
               new ChatMessage(ChatRole.System, $"{prompt}\n{teamInfoPrompt}\n{teamKnowledgePrompt}"),
                new ChatMessage(ChatRole.User, battleLog)
           ];

            return messages;

        }

        // BuildSummaryTeamPromptAsync
        public async Task<IList<ChatMessage>> BuildSummaryTeamPromptAsync(PromptContext context)
        {
            var teamInfoPrompt = BuildTeamInfoPrompt(context);
            var teamKnowledgePrompt = BuildGameStateKnowledgePrompt(context);
            var prompt = $@"
You are a professional Pokémon battle master. Summarize your team's performance in single battles to improve future success.
Your name in this match was {context.CurrentState?.BattleData.MyName}, and you {(context.CurrentState?.BattleData.Win ?? false ? "won" : "lost")} this match.
1. Primary Usage (recommended lead Pokémon and their roles)    
2. Matchup Notes (specific 1v1 pairings or threats and how to handle them)    
3. Avoided Selections (which Pokémon to avoid leading in particular scenarios, and why)    
4. Overall Strategy (summarize your main win conditions, speed control, and potential setup opportunities)  
";
            var battleLog = GetBattleLog(context.CurrentState?.BattleData ?? new());
            // 可能还需要之前的对战经验
            List<ChatMessage> messages = [
               new ChatMessage(ChatRole.System, $"{prompt}\n{teamInfoPrompt}\n{teamKnowledgePrompt}"),
               new ChatMessage(ChatRole.System, $"Example:"),
               new ChatMessage(ChatRole.System, @"Use Garchomp as your primary lead. Garchomp pressures foes with Earthquake and Outrage, and can also use Stealth Rock to chip the opponent's team. In the mid game, Rotom-Heat can take advantage of electric immunity and resist common Steel-type moves, while Toxapex provides excellent defensive utility and can stall out key threats.

Matchup Notes

Vs. Dragapult: Lead with Garchomp and use Outrage for immediate pressure; bring Toxapex in the back for recovery.
Vs. Ferrothorn: Lead with Rotom-Heat to threaten with Overheat; keep Garchomp as backup for Earthquake coverage.

Avoided Selections

Do not lead with Toxapex against offensive teams unless you have guaranteed defensive synergy.
Only select Garchomp over other options if opponents lack strong Ice-type coverage or priority moves.

Overall, focus on Garchomp's offensive presence and adjust your team composition to counter the opponent's core strategy. Use Stealth Rock early to accumulate damage and maintain offensive pressure throughout the battle."),
                new ChatMessage(ChatRole.User, battleLog)
           ];

            return messages;

        }
    }
}