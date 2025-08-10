using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Identity.Client.Platforms.Features.DesktopOs.Kerberos;
using OpenAI;
using OpenTelemetry.Trace;
using PokeCommon.PokemonShowdownTools;
using PokemonLLMBattle.Core.Models;
using Showdown;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace PokemonLLMBattle.Core
{

    public class BattleStorage
    {
        public ImmutableArray<PSBattle> NowBattles { get; set; } = [];
        public ImmutableArray<PSBattle> HistoryBattles { get; set; } = [];
        public ImmutableArray<PSBattle> WaitingBattles { get; set; } = [];
    }

    //public record BoExp(string Tag, string Exp, )

    public record LLMTrainer(
        LLMTrainerConfig Config,
        LLMTrainerTeam LLMTrainerTeam

        )
    {
        public ShowdownClient? Client;
        public TrainerStatus TrainerStatus = new(false, []);

        public Dictionary<string, string[]> BoExp { get; init; } = [];

        public LLMDecisionEngine DecisionEngine { get; init; }
        public bool IsLoggedIn { get; init; }

        public static LLMTrainer Create(LLMTrainerConfig config, LLMTrainerTeam team)
        {

         
            IChatClient GetClient()
            {
                if (config.IsAzure)
                {
                    return new AzureOpenAIClient(new Uri(config.ApiUrl), new ApiKeyCredential(config.ApiKey))
                        .GetChatClient(config.ModelName).AsIChatClient();
                }
                else
                {
                    return new OpenAIClient(new ApiKeyCredential(config.ApiKey))
                        .GetChatClient(config.ModelName).AsIChatClient();
                }
            }

            //var credential = new ApiKeyCredential(config.ApiKey);
            //var azureClient = new AzureOpenAIClient(new Uri(config.ApiUrl), credential);
            //var openClient = new OpenAIClient(credential);

            //var chatClient = azureClient.GetChatClient(config.ModelName).AsIChatClient();

            var chatClient = GetClient();
            //var chatClient1 = azureClient.GetChatClient("grok3").AsIChatClient();


            var decisionEngine = new LLMDecisionEngine(chatClient, chatClient, new DoubleBattlePromptBuilder());

            return new LLMTrainer(config, team) { DecisionEngine = decisionEngine, 
                //Client = new ShowdownClient(config.ClientInfo)
            };
        }

        public async Task SearchBattleAsync(string v)
        {
            if (Client is null) return;
            var trainerTeam = await PSConverterWithoutDB.ConvertToPokemonsAsync(LLMTrainerTeam.PsTeam);
            var trainerTeamString = await PSConverterWithoutDB.ConvertToPsOneLineAsync(trainerTeam);
            //await Client.ChangeYourTeamAsync(trainerTeamString.Replace("asone", "asonespectrier"));
            await Client.ChangeYourTeamAsync(trainerTeamString.Replace("asone", "asoneglastrier"));

            await Client.SearchBattleAsync(v);
        }
    }


    public record TrainerStatus (
        bool AcceptChallage, 
        ImmutableArray<PSBattle> NowBattles
        
        
        );

    public record ShowdownClientConfig(
        string Username,
        string Password,
        string Server = "sim.smogon.com",
        //int Port = 8000,
        bool UseSSL = true,
        //bool AutoReconnect = true,
        bool AutoJoinLobby = true,
        string LobbyName = "lobby"
    );

    public record LLMTrainerConfig(
        ClientInfo ClientInfo,
        string ModelName,
        string ModelVersion,
        string ApiKey,
        string ApiUrl = "https://api.openai.com/v1/chat/completions",
        int MaxTokens = 1000,
        double Temperature = 0.7,
        double TopP = 1.0,
        bool IsAzure = true // additions
    );

}
