using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PokemonLLMBattle.Core.Models
{
    
    public record LLMTrainerTeam(string PsTeam, 
        string OrderPrompt, 
        string BattlePrompt
        , string TeamPrompt // 既定战术
        )
    {
        public static LLMTrainerTeam CreateDefault(string psTeam)
        {
            return new LLMTrainerTeam(psTeam, "", "", "");
        }
    }

    // 对手队伍知识库

}
