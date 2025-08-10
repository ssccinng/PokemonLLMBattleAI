using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using OpenTelemetry.Trace;
using PokeCommon.PokemonShowdownTools;
using PokeCommon.Utils;
using PokemonDataAccess.Models;
using PokemonLLMBattle.Core.Models;
using Serilog;
using Showdown;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PokemonLLMBattle.Core
{
    public static class LLMTrainerExtension
    {
        public static ConcurrentBag<LLMTrainerTeam> Teams = [];

        

        extension(PSBattle battle)
        {
            public GameState CreateState(LLMTrainerTeam lLMTrainerTeam, StateCondition stateCondition)
            {
                var battleData = battle.BattleData;
                var myTeam = battleData.PlayerDatas[battleData.MySlot].Team;
                return new GameState(battle, battle.BattleData, lLMTrainerTeam, stateCondition);
            }
        }
       
        
        extension(LLMTrainer trainer)
        {
            public async Task<LLMTrainerTeam> UpdateLLMTeam(LLMTrainerTeam team, PSBattle pSBattle)
            {

                GameState gameState = pSBattle.CreateState(trainer.LLMTrainerTeam, new ChooseCondition());
                var exp = await trainer.DecisionEngine.GetTeamExpAsync(gameState, CancellationToken.None);
                return team with { TeamPrompt = exp };
            }
            public async void OnBattleStarted(PSBattle battle)
            {
                if (!Directory.Exists("output_log/" + battle.Tag))
                {
                    Directory.CreateDirectory("output_log/" + battle.Tag);

                }
                //UpdateTeamCache(battle.Tag, Team);

                await battle.SendAcceptOpenTeamSheetsAsync();

                battle.Additions["orderreason"] = "";
                trainer.SetBattleEventHanlder(battle);
                //CurrentBattle = battle;
                //CurrentBattleDecisions.Clear();


            }


            public void SetBattleEventHanlder(PSBattle pSBattle)
            {
                var doDesi =  (StateCondition condition) => async (PSBattle battle) => 
                {
                    try
                    {
                        var nowTurn = battle.Turn;
                        CancellationTokenSource cts = new CancellationTokenSource();
                        GameState gameState = battle.CreateState(trainer.LLMTrainerTeam, condition);
                        var desi = await trainer.DecisionEngine.MakeDecisionAsync(gameState, cts.Token);
                        _ = Task.Run(() =>
                        {
                            while (nowTurn == battle.Turn && !cts.IsCancellationRequested)
                            {
                                Thread.Sleep(1000);
                            }
                            cts.Cancel();
                        });

                        // 如果turn不合规 不执行
                        if (nowTurn != battle.Turn)
                        {
                            Console.WriteLine("你想的太慢了！！");
                            return;
                        }

                        
                        await LLMTrainer.ExecuteDecisionAsync(battle, gameState, desi);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                        Console.WriteLine(ex.StackTrace);
                    }
                    

                };

                pSBattle.OnChooseMove += battle => doDesi(new ChooseCondition())(battle);

                pSBattle.OnForceSwitch += (battle, bools) => doDesi(new ForceSwitchCondition(bools))(battle);

                pSBattle.OnTeampreview += async battle =>
                {
                    battle.BattleData = battle.BattleData.SetMyName(trainer.Config.ClientInfo.Name);

                    while (!pSBattle.BattleData.OpenSheet || pSBattle.BattleData.PlayerDatas.Any(s => s.Team.IsNone))
                    {
                        await Task.Delay(1000); // 等待对方打开队伍
                    }

                    if (battle.BattleData.BattleInfo is BoBattle bo)
                    {
                        battle.BattleData = battle.BattleData with
                        {
                            BattleInfo = bo with { BoExp = trainer.BoExp.GetValueOrDefault(bo.BoTag, []) }
                        };
                    }
                  

                    await doDesi(new TeamOrderCondition(4))(battle);
                };
            }
            public async Task<LLMTrainer> LoginAsync()
            {
                var newTrainer = trainer with { Client = new Showdown.ShowdownClient(trainer.Config.ClientInfo) };

                var res = await newTrainer.Client?.ConnectAsync();
                if (res)
                {
                    await newTrainer.Client.LoginAsync();
                    newTrainer = newTrainer.SetupEventHandlers();        
                }
                if (newTrainer.Client != null)
                {
                    return  newTrainer with { IsLoggedIn = res };
                }
                else
                {
                    throw new Exception("Failed to connect to Showdown server.");
                }
            } 


            public LLMTrainer AcceptChallenges(int cnt)
            {
                if (trainer.Client == null)
                {
                    throw new InvalidOperationException("Client is not initialized. Please login first.");
                }
                trainer.Client.OnChallenge += async (player, rule) =>
                {
                    if (rule == "gen9vgc2025regibo3")
                    {
                        //await trainer.Client.ChatWithIdAsync(player, "随机战斗，玩了");
                        //await trainer.Client.ChatWithIdAsync(player, "就决定是你了");
                        // await pc.ChangeYourTeamAsync("null");
                        var trainerTeam = await PSConverterWithoutDB.ConvertToPokemonsAsync(trainer.LLMTrainerTeam.PsTeam);
                        var trainerTeamString = await PSConverterWithoutDB.ConvertToPsOneLineAsync(trainerTeam);
                        //await trainer.Client.ChangeYourTeamAsync(trainerTeamString.Replace("asone", "asonespectrier"));
                        await trainer.Client.ChangeYourTeamAsync(trainerTeamString.Replace("asone", "asoneglastrier"));
                        //isInBattle = true;
                        await trainer.Client.AcceptChallengeAsync(player);
                    }
                    else
                    {
                        await trainer.Client.CancelChallengeAsync(player, rule);
                    }
                    // 检查一下当前的挑战数量
                };
                return trainer with { TrainerStatus = trainer.TrainerStatus with { AcceptChallage = true } };

            }

            public LLMTrainer SetupEventHandlers()
            {
                // 战斗开始事件
                trainer.Client.OnBattleStart += trainer.OnBattleStarted;

                // 战斗结束事件
                trainer.Client.OnBattleEnd += trainer.OnBattleEnded;
                //trainer.Client.OnBattleError += Client_OnBattleError;
                //trainer.Client.OnSetTeam += Client_OnSetTeam; ;

                return trainer;
            }

            public async Task<string> GetBoExp(PSBattle psBattle, bool win)
            {
                GameState gameState = psBattle.CreateState(trainer.LLMTrainerTeam, new ChooseCondition());
                return await trainer.DecisionEngine.GetBoExpAsync(gameState, CancellationToken.None);
            }
            
            public async void OnBattleEnded(PSBattle battle, bool win)
            {
                // 处理战斗结束事件
                Console.WriteLine($"战斗结束，房间ID: {battle.Tag}");
                await battle.LeaveRoomAsync();


                if (battle.BattleData.BattleInfo is BoBattle bo)
                {
                    var exp = await trainer.GetBoExp(battle, win);
                    if (!trainer.BoExp.ContainsKey(bo.BoTag))
                    {
                        trainer.BoExp.Add(bo.BoTag, [exp]);
                    }
                    else
                    {
                        trainer.BoExp[bo.BoTag] = [..trainer.BoExp[bo.BoTag], exp];
                    }
                }

                File.WriteAllText(

                $"output_log/{battle.Tag}/{battle.Turn}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_history.json",
                JsonSerializer.Serialize(trainer.LLMTrainerTeam, 
                new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                 
                var newTrainerTeam = await trainer.UpdateLLMTeam(trainer.LLMTrainerTeam, battle);
                File.WriteAllText(

                $"output_log/{battle.Tag}/{battle.Turn}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_new.json",
                JsonSerializer.Serialize(newTrainerTeam,
                new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

                // 保存对局记录

                File.WriteAllText($"output_log/{battle.Tag}/{battle.Turn}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_log.json",
                    string.Join("\n", battle.BattleData.BattleTurns.SelectMany(s => s.TurnLog))
                    );


                //trainer = trainer with { BattleMap = trainer.BattleMap.Remove(battle.Tag), LLMTrainerTeam = await trainer.LLMTrainerTeam.UpdateLLMTeam(battle) };

                // 可以在这里添加更多的处理逻辑，比如记录战斗结果等
            }


            public static ChooseData[] ParseDecision(GameState gameState, Decision decision) // gamestate
            {
                return decision switch
                {
                    ChooseMoveDecision chooseMoveDecision => [MakeChooseMoveData(gameState, chooseMoveDecision)],
                    SwitchDecision switchDecision => [MakeSwitchData(gameState, switchDecision)],
                    PassDecision passDecision => [new SVChooseData { IsPass = true}],
                     TeamOrderDecision teamOrderDecision => [MakeTeamOrderData(gameState, teamOrderDecision)],// 这不对 要怎么处理,
                    MultipleDecision multipleDecision =>[ ..multipleDecision.Decisions.SelectMany(s => ParseDecision(gameState, s))],
                    _ => throw new NotSupportedException($"Decision type {decision.GetType().Name} is not supported.")
                }; 
            }
            
            public static SVChooseData MakeChooseMoveData(GameState gameState, ChooseMoveDecision chooseDecision)
            {
                var battleData = gameState.BattleData;
                var lastTurn = battleData.GetLastTurn();
                var request = lastTurn.Requests.Last();
                var pokes = request.side.pokemon.Select(s => s.ident);
                var moveIdx = request.active[chooseDecision.ChoosePosition].moves.Index().Where(m => m.Item.move.Equals(chooseDecision.Move, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                var opppokes = lastTurn.SideTeam[1 - battleData.MySlot].Pokemons.Where(s => s.Position >= 0).OrderBy(s => s.Position).Select(s => s.PsName);
                if (moveIdx == default)
                {
                    moveIdx = request.active[chooseDecision.ChoosePosition].moves.Index().FirstOrDefault(); ;
                    //return new SVChooseData { IsPass = true };
                }
                var name = chooseDecision.TargetPokemon;
                var moveTarget = moveIdx.Item.target;
                

                if (moveTarget == "any" || moveTarget == "normal" || moveTarget == "adjacentFoe")
                {
                    string target = chooseDecision.TargetPokemon;
                    var target_idx = opppokes.ToList()
                    .FindIndex(s => RemoveNonAlphanumeric(s).Contains(RemoveNonAlphanumeric(target), StringComparison.OrdinalIgnoreCase)) + 1;
                    // target 1 2 -1 -2
                    if (target_idx == 0)
                    {
                        Log.Logger.Warning("move target is 0, this is not expected, please check the move target logic.");
                        target_idx = 1;
                    }

                    return new SVChooseData
                    {
                        MoveId = moveIdx.Index + 1,
                        Target = target_idx,
                        Terastallize = chooseDecision.Tera
                    };

                }
                else
                {
                    return new SVChooseData
                    {
                        MoveId = moveIdx.Index + 1,
                        Terastallize = chooseDecision.Tera
                    } ;
                }
            }
            public static SwitchData MakeSwitchData(GameState gameState, SwitchDecision switchDecision)
            {
                var lastTurn = gameState.BattleData.GetLastTurn();
                var request = lastTurn.Requests.Last();
                var pokes = request.side.pokemon.Select(s => s.ident);

                var opppokes = lastTurn.SideTeam[1 - gameState.BattleData.MySlot].Pokemons.Where(s => s.Position >= 0).OrderBy(s => s.Position).Select(s => s.PsName);

                var name = switchDecision.SwitchInPokemon;
                var idx = pokes.ToList().FindIndex(s => RemoveNonAlphanumeric(s).Contains(RemoveNonAlphanumeric(name), StringComparison.OrdinalIgnoreCase)) + 1;

                return new SwitchData { PokeId = idx };
            }

            public static TeamOrderData MakeTeamOrderData(GameState gameState, TeamOrderDecision orderDecision)
            {
                var battleData = gameState.BattleData;

                var teams = battleData.GetMyTeam().ValueUnsafe()!;
                var dict = teams.GamePokemons
                        .Select((s, i) => (RemoveNonAlphanumeric(PokemonToolsWithoutDB.GetPsPokemonAsync(s.MetaPokemon.Id).Result.PSName), i + 1))
                        .ToList();
                var order = orderDecision.Pokemons.Select(s => dict.FirstOrDefault(d => d.Item1.Contains(RemoveNonAlphanumeric(s))).Item2).ToArray();

                return new TeamOrderData(string.Concat(order));

            }


            public static async Task<Unit> ExecuteDecisionAsync(PSBattle psBattle, GameState gameState, Decision decision)
            {
                ChooseData[] command = ParseDecision(gameState, decision);
     
                await psBattle.SendMoveAsunc(command);
                return Unit.Default; 
            }


            //public LLMTrainer ChallengeLadder(int cnt)
            //{

            //}


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
    }
}
