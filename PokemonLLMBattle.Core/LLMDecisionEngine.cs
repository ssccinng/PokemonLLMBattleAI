using Google.Protobuf;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Org.BouncyCastle.Asn1.X509;
using PokeCommon.PokemonShowdownTools;
using PokeCommon.Utils;
using PokemonDataAccess.Models;
using PokemonLLMBattle.Core.Models;
using Serilog;
using Showdown;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static LanguageExt.Prelude;
using static System.Net.Mime.MediaTypeNames;

namespace PokemonLLMBattle.Core
{
    public class LLMDecisionEngine
    {
        private IChatClient _chatClient;
        private IChatClient _chatSimpleClient;
        private IPromptBuilder _promptBuilder;

        public LLMDecisionEngine(
            IChatClient chatClient,
            IChatClient chatSimpleClient,
            IPromptBuilder promptBuilder
            )
        {
            _chatClient = chatClient;
            _chatSimpleClient = chatSimpleClient;
            _promptBuilder = promptBuilder;
        }
        public async Task<Decision> MakeDecisionAsync(GameState gameState, CancellationToken token)
        {
            return gameState.Condition switch
            {
                ForceSwitchCondition fc => await MakeForceSwitchDecisionAsync(gameState, fc, token),
                TeamOrderCondition toc => await MakeTeamOrderDecisionAsync(gameState, toc, token),
                ChooseCondition cc => await MakeChooseMoveDecisionAsync(gameState, cc, token),
                _ => throw new NotSupportedException($"Unsupported condition type: {gameState.Condition.GetType().Name}")
            };
        }

        private async Task<Decision> MakeChooseMoveDecisionAsync(GameState gameState, ChooseCondition cc, CancellationToken token)
        {
            var chatOption = new ChatOptions
            {
                AdditionalProperties = []
            };

            chatOption.AdditionalProperties["reasoning"] = new
            {
                //effort = "medium",
                effort = "medium",
            };
            var lastTurn = gameState.BattleData.GetLastTurn();
            var request = lastTurn.Requests.Last();
            var battleData = gameState.BattleData;
            var messages = await _promptBuilder.BuildChooseMovePromptAsync(new PromptContext() { CurrentState = gameState }, cc);

            var response = await _chatClient.GetResponseAsync(messages, chatOption, token);
            SaveChatLog(gameState, response, messages);

            Console.WriteLine(response.Text);
            var action = JsonSerializer.Deserialize<ActionDecision>(
                response.Text.Replace("```json", "").Replace("json", ""),
                new JsonSerializerOptions 
                { PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                , Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                }
                );

            var decisions = action?.Actions.Map(a =>
            {
                if (a.TryGetProperty("switch", out var switchName))
                {
                    return new SwitchDecision(switchName.GetString()!) as Decision;
                }
                else if (a.TryGetProperty("move", out var move))
                {
                    var moveName = move.GetProperty("name").GetString();
    
                    bool tera = false;

                    if (lastTurn.SideTeam[battleData.MySlot].CanTerastallize && move.TryGetProperty("terastallize", out var teraj))
                    {
                        tera = teraj.GetBoolean();
                    }
                    string target = string.Empty;
                    if (move.TryGetProperty("move_target_pokemon", out var targetj))
                    {
                        target = targetj.GetString();
                    }
                    FieldSide fieldSide = FieldSide.Opponent;
                    if (move.TryGetProperty("side", out var fieldSidej))
                    {
                        fieldSide = fieldSidej.GetString() switch
                        {
                            "aliy" => FieldSide.Aliy,
                            "opponent" => FieldSide.Opponent,
                            _ => FieldSide.Opponent
                        };
                    }
                    // 这里加入坐标
                    return new ChooseMoveDecision(0, target, moveName, fieldSide, tera);
                  
                }

                else
                {
                    throw new NotSupportedException($"Unsupported action type in decision: {a}");
                }
            });
            // 这个纯纯双打
            if (request.side.pokemon[0].condition.Contains("fnt"))
            {
                decisions = decisions.Prepend(new PassDecision());
            }
            if (decisions.Length() ==1)
            {
                decisions = decisions.Append(new PassDecision());

            }
            decisions = decisions
                .Map((i, s) => 
                    s is ChooseMoveDecision c 
                    ? c with { ChoosePosition = i } 
                    : s
                );

            // 还要有默认执行方案

            return new MultipleDecision(decisions.ToImmutableArray());
        }

        private async Task<Decision> MakeTeamOrderDecisionAsync(GameState gameState, TeamOrderCondition toc, CancellationToken token)
        {
            var chatOption = new ChatOptions
            {
                AdditionalProperties = []
            };

            chatOption.AdditionalProperties["reasoning"] = new
            {
                effort = "medium",
            };

            var messages = await _promptBuilder.BuildTeamOrderPromptAsync(new PromptContext() { CurrentState = gameState }, toc);
            var response = await _chatClient.GetResponseAsync(messages, chatOption, token);
            Console.WriteLine(response.Text);

            SaveChatLog(gameState, response, messages);

            var action = JsonSerializer.Deserialize<OrderDecision>
                (response.Text.Replace("```json", "").Replace("json", ""), new JsonSerializerOptions {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                , Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping

                });

            //battle.Additions["orderreason"] = dd.Reason; // 想想怎么加入
            var battleData = gameState.BattleData;

            var teams = battleData.GetMyTeam().ValueUnsafe()!; // myteam 安全

            //var dict = teams.GamePokemons.Select(s => PokemonToolsWithoutDB.GetPsPokemonAsync(s.MetaPokemon.Id).Result.PSName);
            //    //.Select((s, i) => (RemoveNonAlphanumeric(PokemonToolsWithoutDB.GetPsPokemonAsync(s.MetaPokemon.Id).Result.PSName), i + 1))
            //    //.ToList();
            Log.Logger.Information("选择理由: {Reason}", JsonSerializer.Serialize(action.Tactics));
            gameState.PSBattle.Additions["order tactics"] = JsonSerializer.Serialize( action.Tactics);
            Log.Logger.Information("选择顺序: {Order}", string.Join(',', action.Order.Select(s => (s.ToString()))));
            return new TeamOrderDecision(
                [.. action.Order.Select(s => s.ToString())]
            );

            //var order = action.Order.Select(s => dict.FirstOrDefault(d => d.Item1.Contains(RemoveNonAlphanumeric(s))).Item2).ToArray();


            //battle.BattleData = battle.BattleData.SetMyOrderTeam(dd.Order);

        }

        public static Unit SaveChatLog(GameState gameState, ChatResponse chatResponse, IList<ChatMessage> messages)
        {
            var save = new { messages, response = chatResponse.Text };

            var battleData = gameState.BattleData;
            var fileName = $"output_log/{gameState.PSBattle.Tag}/{gameState.PSBattle.Tag}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            File.WriteAllText(fileName, JsonSerializer.Serialize(save, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            return Unit.Default;
        }

        public static string RemoveNonAlphanumeric(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var sb = new StringBuilder(input.Length);
            foreach (var c in input)
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(c);
            }
            return sb.ToString();
        }
        private async Task<Decision> MakeForceSwitchDecisionAsync(GameState gameState, ForceSwitchCondition fc, CancellationToken token)
        {
            var chatOption = new ChatOptions
            {
                AdditionalProperties = []
            };

            chatOption.AdditionalProperties["reasoning"] = new
            {
                effort = "medium",
            };
            var lastTurn = gameState.BattleData.GetLastTurn();
            var request = lastTurn.Requests.Last();
            var battleData = gameState.BattleData;
            var messages = await _promptBuilder.BuildForceSwitchPromptAsync(new PromptContext() { CurrentState = gameState }, fc);
            var response = await _chatClient.GetResponseAsync(messages, chatOption, token);
            Console.WriteLine(response.Text);
            SaveChatLog(gameState, response, messages);

            var action = JsonSerializer.Deserialize<ActionDecision>(response.Text.Replace("```json", "").Replace("json", "")
                
                , new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                , Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping


                }

                );

            List<Decision> decisions = [];
            int idx = 0;
            foreach (var f in fc.ForceSwitch)
            {
                if (!f)
                {
                    decisions.Add(new PassDecision());
                }
                else
                {
                    if (idx >= action.Actions.Length)
                    {
                        decisions.Add(new PassDecision());

                    }
                    if (action.Actions[idx++].TryGetProperty("switch", out var switchName))
                    {
                        decisions.Add(new SwitchDecision(switchName.GetString()!));
                    }
                    else
                    {
                        Console.WriteLine("forchswitch error");
                    }
                }
            }

            return new MultipleDecision([..decisions]);

        }

        public async Task<string> GetBoExpAsync(GameState gameState, CancellationToken token)
        {
            var chatOption = new ChatOptions
            {
                AdditionalProperties = []
            };

            chatOption.AdditionalProperties["reasoning"] = new
            {
                effort = "medium",
            };
            var messages = await _promptBuilder.BuildSummaryBoBattlePromptAsync(new PromptContext() { CurrentState = gameState });
            var response = await _chatClient.GetResponseAsync(messages, chatOption, token);
            Console.WriteLine(response.Text);
            return response.Text;
        }

        public async Task<string> GetTeamExpAsync(GameState gameState, CancellationToken token)
        {
            var chatOption = new ChatOptions
            {
                AdditionalProperties = []
            };

            chatOption.AdditionalProperties["reasoning"] = new
            {
                effort = "medium",
            };
            var messages = await _promptBuilder.BuildSummaryTeamPromptAsync(new PromptContext() { CurrentState = gameState });
            var response = await _chatClient.GetResponseAsync(messages, chatOption, token);
            Console.WriteLine(response.Text);
            return response.Text;
        }
    }
}
