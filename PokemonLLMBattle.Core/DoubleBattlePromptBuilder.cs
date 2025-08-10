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

namespace PokemonLLMBattle.Core
{

    file static class Prompts
    {
        public static readonly string ChooseMoveSystemPrompt = File.ReadAllText("Prompts/SVDouble/ChooseMovePrompt.txt");
        public static readonly string TypeKnowledgePrompt = File.ReadAllText("Prompts/SVDouble/TypeKnowledge.txt");
    }

    internal class DoubleBattlePromptBuilder : IPromptBuilder
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
            var prompt = new StringBuilder();
            var battleData = context.CurrentState?.BattleData;
            if (battleData.OpenSheet)
            {

                var myTeam = getTeam(battleData.PlayerDatas[battleData.MySlot].Team);
                var oppTeam = getTeam(battleData.PlayerDatas[1 - battleData.MySlot].Team);
                prompt.AppendLine("**MY TEAM**:");
                prompt.AppendLine(JsonSerializer.Serialize(myTeam, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                }));
                prompt.AppendLine("**OPPONENT TEAM**:");
                prompt.AppendLine(JsonSerializer.Serialize(oppTeam, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                }));

            }
            else
            {
                prompt.AppendLine("**Our team**:");

                var myTeam = getTeam(battleData.PlayerDatas[battleData.MySlot].Team);
                prompt.AppendLine(JsonSerializer.Serialize(myTeam, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                }));
                prompt.AppendLine("**Opponent team**:");
                var oppTeam = battleData.PlayerDatas[1 - battleData.MySlot].Team;
                var oppTeamPokes = oppTeam.Map(s => s.GamePokemons)
                    .Map(p => p.Select(s => s.MetaPokemon?.PSPokemon.PSName).ToList())
                    .IfNone([]);

                prompt.AppendLine(JsonSerializer.Serialize(oppTeamPokes, new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                }));

            }
                return prompt.ToString();
        }
        // BuildTeamOrderSystemPrompt
        string BuildTeamOrderSystemPrompt(PromptContext context, TeamOrderCondition teamOrderCondition)
        {
            var battleData = context.CurrentState?.BattleData;
            var sysP1 = @"
You are a professional Pokémon double battle trainer aiming to defeat all opponent Pokémon and secure victory.
You need to select 4 Pokémon from 6 available based on your and your opponent’s team compositions. The first 2 will start, the last 2 are reserves.
Your responsibilities:
1. Analyze both teams and choose appropriate selections.
2. Evaluate all possible combinations.
3. Choose the optimal lineup.
4. Predict the opponent's remaining team and adjust decisions.
5. Provide detailed reasoning, output selection strategy and roles for each Pokémon in the 'reason' field.

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
    ""order_pokemon_details"": {
        ""pokemon_x"": {
            ""positioning"": ""Positioning: ...""
            ""expected_roles"": ""Expected roles: ...""
        }
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
You are a professional Pokémon double battle expert. Analyze the current game state and provide optimal switching decisions.
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
            // Generated by Copilot
            var prompt = $@"
You are a professional Pokémon double battle strategist. Analyze the current battle state and provide optimal actions (move or switch) for each active Pokémon.

{(battleData.OpenSheet ? "" : "This is a hidden-information battle; infer the opponent's team composition.\n")}

Always consider the possibility that your opponent might use Protect, as it can completely block your move for that turn.
Always be mindful of potential incoming damage from your opponent and avoid exposing your key Pokémon to heavy hits unless absolutely necessary.

Consider the following points:
1. Evaluate the current battle progress and situation.
2. Anticipate the opponent's potential actions (using moves or switching Pokémon).
3. Determine the optimal strategy for our team (such as switching, coordinating double battles, speed control, attacking to KO the opponent, or even sacrificing a Pokémon to hold the field).
4. Assess whether our chosen actions can further increase our advantage.
5. Analyze the risks associated with our decisions.

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
            return Prompts.TypeKnowledgePrompt;
        }


        string BuildHistoryKnowledgePrompt(PromptContext promptContext)
        {
            // 既定战术

            var gamestate = promptContext.CurrentState;

            var prompt = new StringBuilder("=== Game History Knowledge ===\n", 2000);
            var team = promptContext.CurrentState?.MyTeam;

            if (string.IsNullOrEmpty(team?.BattlePrompt))
            {

                //prompt.AppendLine("无战术");
            }
            else
            {
                prompt.AppendLine("- Team's predefined tactics: ");

                prompt.AppendLine(team.BattlePrompt);
            }

            if (string.IsNullOrEmpty(team?.TeamPrompt))
            {

                //prompt.AppendLine("无战术");
            }
            else
            {
                prompt.AppendLine("- Team's history experience: ");

                prompt.AppendLine(team.TeamPrompt);
            }

            //prompt.AppendLine($"{team?.BattlePrompt ?? "无战术"}");
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

            //prompt.AppendLine("3. 上回合推理战术: ");
            // 可能还有历史对战经验等
            return prompt.ToString();
        }

        string BuildTeamOrderHistoryKnowledgePrompt(PromptContext promptContext)
        {
            //Console.WriteLine(promptContext.CurrentState.PSBattle.BattleData.bo);

            // 如何把bo3的经验传递到这里

            // 既定战术
            var prompt = new StringBuilder("=== Team Order History Knowledge ===\n", 2000);
            prompt.AppendLine("1. Team's predefined tactics: ");
            var team = promptContext.CurrentState?.MyTeam;
            prompt.AppendLine($"{team?.BattlePrompt ?? "No tactics"}");
            prompt.AppendLine("2. Summary of previous battle experience: ");
            prompt.AppendLine($"{team?.TeamPrompt ?? "No summary"}");
            if (promptContext.CurrentState?.BattleData.BattleInfo is BoBattle bo) { 
                prompt.AppendLine("3. Previous best-of match experience: ");
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
        /// <summary>
        /// 构建当前对局信息提示
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        string BuildGameStatePrompt(PromptContext context)
        {
            var battleData = context.CurrentState?.BattleData;
            var lastTurn = battleData?.GetLastTurn();

            var prompt = new StringBuilder(" === Current battle situation ===\n", 4000);
            prompt.AppendLine($"Turn: {lastTurn?.Turn ?? 0}");
            // 我方剩余宝可梦
            prompt.AppendLine($"Our remaining Pokémon Count: {4 - lastTurn?.SideTeam[battleData.MySlot].Pokemons.Count(s => s.BattleStatus is IsDead) ?? 0}");

            // 对方剩余宝可梦
            prompt.AppendLine($"Opponent remaining Pokémon Count: {4 - lastTurn?.SideTeam[1 - battleData.MySlot].Pokemons.Count(s => s.BattleStatus is IsDead) ?? 0}");



            if (lastTurn.BattleField.Weather != Showdown.Weather.None)
            {
                prompt.AppendLine($"WEATHER: {lastTurn.BattleField.Weather}; Remaining rounds: {lastTurn.BattleField.WeatherRemain} or {lastTurn.BattleField.WeatherRemain + 3}");
            }

            if (lastTurn.BattleField.Terrain != Showdown.Terrain.None)
            {
                prompt.AppendLine($"TERRAIN: {lastTurn.BattleField.Terrain}; Remaining Turns: {lastTurn.BattleField.TerrainRemain} or {lastTurn.BattleField.TerrainRemain + 3}");
            }
            var battleFieldProps = typeof(Showdown.BattleField).GetProperties()
                .Where(p => p.Name != "Weather" && p.Name != "Terrain" 
                && p.Name != "WeatherRemain" && p.Name != "TerrainRemain")
                .Select(p => (p.Name, (int)p.GetValue(lastTurn.BattleField)))
                .Where(s => s.Item2 != 0)
                .Select(s => $"{s.Item1}: {s.Item2} turns remaining");
            if (battleFieldProps.Any())
            {
                prompt.AppendLine($"**Current battlefield conditions**: {string.Join(", ", battleFieldProps)}");
            }

            var ourSideFieldProps = lastTurn.SideField[battleData.MySlot].GetType().GetProperties()
                .Where(p => p.Name != "Weather" && p.Name != "Terrain" && p.Name != "WeatherRemain" && p.Name != "TerrainRemain")
                .Select(p => (p.Name, (int)p.GetValue(lastTurn.SideField[battleData.MySlot])))
                .Where(s => s.Item2 != 0)
                .Select(s => $"{s.Item1}: {s.Item2}");
            var opponentSideFieldProps = lastTurn.SideField[1 - battleData.MySlot].GetType().GetProperties()
                 .Where(p => p.Name != "Weather" && p.Name != "Terrain" && p.Name != "WeatherRemain" && p.Name != "TerrainRemain")
                 .Select(p => (p.Name, (int)p.GetValue(lastTurn.SideField[1 - battleData.MySlot])))
                 .Where(s => s.Item2 != 0)
                 .Select(s => $"{s.Item1}: {s.Item2}");

            prompt.AppendLine($"**Our side field status**: {string.Join(", ", ourSideFieldProps)}");
            prompt.AppendLine($"**Opponent side field status**: {string.Join(", ", opponentSideFieldProps)}\n");

            // 分两者，明牌或者非明牌
            LanguageExt.Option<PokeCommon.Models.GamePokemonTeam> myTeam = battleData.PlayerDatas[battleData.MySlot].Team;
            var mySideTeam = lastTurn.SideTeam[battleData.MySlot];
            var oppTeam = battleData.PlayerDatas[1 -battleData.MySlot].Team;

            if (battleData.OpenSheet)
            {
                var getNewTeam = 
                    (Option<GamePokemonTeam> team, BattleTeam sideTeam) =>  
                            from mt in team
                            from p in mt.GamePokemons
                            join msp in sideTeam.Pokemons
                            on p.MetaPokemon!.Id equals msp.Pokemon.MetaPokemon?.Id
                            let baseStats = p.MetaPokemon
                            select new { 
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
                                type =(new []{ baseStats?.Type1?.Name_Eng, baseStats?.Type2?.Name_Eng}).Where(s => !string.IsNullOrEmpty(s)),
                                //ivs = p.IVs, // 可能不需要
                                hpRemain = $"{msp.HpRemain}%",
                                battleStatus = msp.BattleStatus.GetStatusPrompt(),
                                teraStallized = msp.TeratallizeStatus.GetStatusPrompt(),
                                status = msp.Status.GetType().GetProperties()
                .Where(p => p.Name != "ProtectCnt" && p.Name != "InField_First_Turn")
                                    .Select(p => (p.Name, (int)p.GetValue(msp.Status)))
                                    .Where(s => s.Item2 != 0)
                                    .Select(s => $"{s.Item1}: {s.Item2}"),
                                first_turn_on_the_field = msp.Status.InField_First_Turn > 0 ? "true":"false(cant use move like fake out)",
                                protectCnt = msp.Status.ProtectCnt,
                                //.Select(s => new { status = s.Item1, value = s.Item2 })
                            }
            ;
                var myNewTeam = getNewTeam(myTeam, mySideTeam);
                var oppNewTeam = getNewTeam(oppTeam, lastTurn.SideTeam[1 - battleData.MySlot]);

                prompt.AppendLine("**MY TEAM**:");
                // 是否可以太晶化

                prompt.AppendLine($"Can Teratallize: {mySideTeam.CanTerastallize}");

                prompt.AppendLine(JsonSerializer.Serialize(myNewTeam, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                }));

                prompt.AppendLine("**OPPONENT TEAM**:");
                prompt.AppendLine($"Can Teratallize: {lastTurn.SideTeam[1 - battleData.MySlot].CanTerastallize}");

                prompt.AppendLine(JsonSerializer.Serialize(oppNewTeam, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                }));

            }
            else
            {

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
            
            var battleData = context.CurrentState?.BattleData;
            var teamPokes = battleData.PlayerDatas
                .Choose(s => s.Team)
                .SelectMany(s => s.GamePokemons);
            var moves = teamPokes.SelectMany(s => s.Moves).
                Where(s => s is not null)
                .DistinctBy(s => s.MoveId)
                .Select(s => s.NameEng)
                .ToList();

            var moveDict = teamPokes.SelectMany(s => s.Moves).
                Where(s => s is not null)
                .DistinctBy(s => s.MoveId).ToDictionary(s => s.MetaMove.Name_Eng, s => s);
            var items = teamPokes.Select(s => s.Item)
                .Where(s => s is not null)
                .DistinctBy(s => s.ItemId)
                .Select(s => s.Name_Eng).ToList();
            var abilities = teamPokes.Select(s => s.Ability)
                .Where(s => s is not null)
                .DistinctBy(s => s.AbilityId)
                .Select(s => s.Name_Eng).ToList();

            var lastTurn = battleData.GetLastTurn();

            var opponentPokes = lastTurn.SideTeam[1 - battleData.MySlot].Pokemons
                .Where(s => s.Position >= 0);

            var opponentMoves = opponentPokes
                .SelectMany(s => s.Moves)
                .Where(s => s is not null)
                .DistinctBy(s => s.MoveId)
                .Select(s => s.NameEng)
                .Distinct().ToList();

            var opponentItems = opponentPokes.Select(s => s.Pokemon.Item)
                .Where(s => s is not null)
                .DistinctBy(s => s.ItemId)
                .Select(s => s.Name_Eng)
                .Distinct().ToList();
            var opponentAbilities = opponentPokes
                .Select(s => s.Pokemon.Ability)
                .Where(s => s is not null)
                .DistinctBy(s => s.AbilityId)
                .Select(s => s.Name_Eng).Distinct().ToList();

            var prompt = new StringBuilder("KEEP THESE MOVE/ITEM/ABILITY KNOWLEDGE IN MIND WHEN EVALUATING MOVES AND EFFECTS\n", 2000);
            prompt.AppendLine("1. Move Knowledge:");
            var psmove = PokemonDBInMemory.PSMoveData;
            var moveKnowledge = moves.Append(opponentMoves)
                .Distinct()
                .Select(s => new {
                    move = s, 
                    description = KnowledgeUtils.PokemonKnowledgeModel.GetMoveByName(s)?.Desc ?? "",
                    type = moveDict[s].MetaMove?.MoveType?.Name_Eng ?? "",
                    power = moveDict[s].MetaMove?.Pow ?? 0,
                    accuracy = moveDict[s].MetaMove?.Acc ?? 0,
                    priority = psmove[PokemonToolsWithoutDBNorm.NormalizeString(s)].GetProperty("priority").GetInt32(),
                });
            prompt.AppendLine(JsonSerializer.Serialize(moveKnowledge, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            }));

            prompt.AppendLine("2. Item Knowledge:");
            var itemKnowledge = items.Append(opponentItems)
                .Distinct()
                .Select(s => new { item = s, description = KnowledgeUtils.PokemonKnowledgeModel.GetItemByName(s)?.Desc ?? "" });

            prompt.AppendLine(JsonSerializer.Serialize(itemKnowledge, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            }));

            prompt.AppendLine("3. Ability Knowledge:");
            var abilityKnowledge = abilities.Append(opponentAbilities)
                .Distinct()
                .Select(s => new { ability = s, description = KnowledgeUtils.PokemonKnowledgeModel.GetAbilityByName(s)?.Description ?? "" });
            prompt.AppendLine(JsonSerializer.Serialize(abilityKnowledge, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            }));

            return prompt.ToString();
            // 加入showdown的数据源

            //var opponentItems = opponentPokes.Select(s => s.it).Distinct().ToList();




            // 对局中存在的特性的知识
            // 对局中存在的道具的知识
            // 对局中存在的技能的知识 
        }

        public string BuildTeamOrderDecisionRequestPrompt(PromptContext context, TeamOrderCondition teamOrderCondition)
        {
            var battleData = context.CurrentState?.BattleData;

            var prompt = $$""""
                === Please make a decision ===
                Please analyze the current situation and choose the best order(4 pokemons)
                PLEASE STRICTLY FOLLOW THE EXAMPLE_OUTPUT_FORMAT
                Do not include extra output like ```json
                **No other extra output allowed!!!**
                EXAMPLE_OUTPUT_FORMAT:
                {
                    {{(battleData.OpenSheet ? "" : @"""opponent_pokemon_prediction"": ""<opponent_team_JSON>"",")}}
                    "tactics": "<tactics>",
                    "order": ["<pokemon_1>", "<pokemon_2>", "<pokemon_3>", "<pokemon_4>"]
                }
                """";
            return prompt.ToString();

        }
        public string BuildForceSwitchDecisionRequestPrompt(PromptContext context, ForceSwitchCondition forceSwitchCondition)
        {
            var battleData = context.CurrentState?.BattleData;
            var req = battleData.GetLastTurn().Requests.Last();
            var canSwitch = string.Join(",", req.side.pokemon.Skip(2).Where(s => !s.condition.Contains("fnt")).Select(s => s.ident.Split(":")[1].Trim()));
            if (string.IsNullOrEmpty(canSwitch))
            {
                canSwitch = "No switchable Pokémon";
            }

            var pokesActive = string.Join(",",
              req.side.pokemon.Zip(forceSwitchCondition.ForceSwitch)
                   .Where(s => s.Item2)
                  .Select(s => s.Item1.ident.Split(":")[1].Trim())
                  .Select(s => $"<{s}_action>")
              );
            var prompt = @$"=== Please make a decision ===
Please analyze the current situation and choose the best action
This turn must switch in {forceSwitchCondition.ForceSwitch.Count(s => s)} Pokémon from the back row to the front row.
actions format: ""actions"": [{pokesActive}]
POKÉMON YOU CAN SWITCH IN: [{canSwitch}]
PLEASE STRICTLY FOLLOW THE EXAMPLE_OUTPUT_FORMAT
Do not include extra output like ```json
**No other extra output allowed!!!**

EXAMPLE_OUTPUT_FORMAT:
{{
    {(battleData.OpenSheet ? "" : @"""opponent_pokemon_prediction"": ""<opponent_team_JSON>"",")}
    ""think"": ""<your_think>"",
    ""actions"": [
        {{""switch"": ""<switch_in_pokemon>""}}}}
    ]
}}
";
                

            return prompt.ToString();
        }
        public string BuildChooseMoveDecisionRequestPrompt(PromptContext context, ChooseCondition chooseCondition)
        {
            var battleData = context.CurrentState?.BattleData;
            var req = battleData.GetLastTurn().Requests.Last();
            var canSwitch = string.Join(",", req.side.pokemon.Skip(2)
                .Where(s => !s.condition.Contains("fnt"))
                .Select(s => $"\"{s.ident.Split(":")[1].Trim()}\""));
            var pokesActive = string.Join(",",
              req.side.pokemon
                  .Where(p => p.active && !p.condition.Contains("fnt"))
                  .Select(s => s.ident.Split(":")[1].Trim())
                  .Select(s => $"<{s}_action>")
              );
            var activeMoves =
                  string.Join(", ",
                  req.side.pokemon.Zip(req.active)
                                          .Where(r => !r.Item1.condition.Contains("fnt") && r.Item1.active)
                                          .Select(r => r.Item2)
                                          .Select(r => string.Join(",", r.moves.Where(s => !s.disabled).Select(s => s.move)))
                                          .Select(r => string.IsNullOrEmpty(r) ? "No available moves" : r)
                                          .Select(r => "[" + r + "]")
                                          );
            var ac = 
                from p in req.side.pokemon.Zip(req.active)
                where p.Item1.active && !p.Item1.condition.Contains("fnt")
                let np = new
                {
                    pokemon = p.Item1.ident.Split(":")[1].Trim(),
                    moves = 
                    string.Join(",", p.Item2.moves.Where(s => !s.disabled).Select(s => "\"" +s.move+ "\""))
                }
                select $"{np.pokemon}: [{(string.IsNullOrEmpty(np.moves) ? "No available moves" : np.moves)}]";

            activeMoves = string.Join(", ", ac);
            if (string.IsNullOrEmpty(canSwitch))
            {
                canSwitch = "No switchable Pokémon";
            }

            var prompt = @$"=== Please make a decision ===
Please analyze the current situation and choose the best action
actions format: ""actions"": [{pokesActive}]
AVAILABLE MOVES THIS TURN: [{activeMoves}]
ONLY THOSE POKEMON YOU CAN SWITCH IN FIELD: [{canSwitch}]
PLEASE STRICTLY FOLLOW THE EXAMPLE_OUTPUT_FORMAT
Do not include extra output like ```json
**No other extra output allowed!!!**

EXAMPLE_OUTPUT_FORMAT:
{{
    {(battleData.OpenSheet ?  "" : @"""opponent_pokemon_prediction"": ""<opponent_team_JSON>"",")}
    ""think"": ""<your_think>"",
    ""actions"": [
        {{""switch"": ""<switch_in_pokemon>""}}}},
        {{""move"": {{""name"": ""<move_name>"", ""terastallize"": <true_or_false>, ""move_target_pokemon"": ""<target_pokemon_name>"", ""target_side"": ""<ally|opp>""}}}}
    ]
}}
";

            return prompt;
  
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
You are a professional Pokémon battle master conducting a best‐of‐three series. Summarize your experience from the last match to improve future performance.
Your name in this match was {context.CurrentState?.BattleData.MyName}, and you {(context.CurrentState?.BattleData.Win ?? false ? "won" : "lost")} this match.
Please provide insights on:
- The key factors that determined the outcome of the battle.
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
You are a professional Pokémon battle master. Summarize your team's performance to improve future success.
Your name in this match was {context.CurrentState?.BattleData.MyName}, and you {(context.CurrentState?.BattleData.Win ?? false ? "won" : "lost")} this match.
1. Primary Usage (recommended lead Pokémon and their roles)    
2. Matchup Notes (specific pairings or threats and how to handle them)    
3. Avoided Selections (which Pokémon or lineups to avoid in particular scenarios, and why)    
4. Overall Strategy (summarize your main win conditions, speed control, and potential trick-room or tailwind pivots)  
";
            var battleLog = GetBattleLog(context.CurrentState?.BattleData ?? new());
            // 可能还需要之前的对战经验
            List<ChatMessage> messages = [
               new ChatMessage(ChatRole.System, $"{prompt}\n{teamInfoPrompt}\n{teamKnowledgePrompt}"),
               new ChatMessage(ChatRole.System, $"Example:"),
               new ChatMessage(ChatRole.System, @"Use Miraidon and Whimsicott as your primary lead. Miraidon pressures foes with Electro Drift and can also use Snarl to weaken key threats, while Whimsicott sets Tailwind to boost the team’s speed or surprises opponents with Light Screen and Encore. In the late game, Ice Rider Calyrex can take advantage of the remaining Tailwind turns or shift to Trick Room when Tailwind ends, ensuring strong board control.

Matchup Notes

Vs. Shadow Rider Calyrex + Zamazenta: Lead Miraidon + Whimsicott; bring Volcarona and Urshifu (Rapid Strike) in the back.
Vs. Kyogre + Ice Rider Calyrex: Lead Miraidon + Incineroar; keep Ice Rider Calyrex and Urshifu (Rapid Strike) as reserves.
Avoided Selections

Do not bring Incineroar or Urshifu (Rapid Strike) into Zamazenta unless Miraidon is also present for additional coverage.
Only select Miraidon over Urshifu (Rapid Strike) if opponents have strong Electric Seed or specific threats like Iron Treads, where you need specialized electric-type pressure.

Overall, focus on Whimsicott’s utility (Tailwind, Encore, Light Screen) and adjust your backline (Ice Rider Calyrex, Urshifu (Rapid Strike), Volcarona, Incineroar) to counter the opponent’s restricted Pokémon. Use Trick Room from Ice Rider Calyrex once Tailwind expires to maintain an offensive edge."),
                new ChatMessage(ChatRole.User, battleLog)
           ];

            return messages;

        }


    }
}
