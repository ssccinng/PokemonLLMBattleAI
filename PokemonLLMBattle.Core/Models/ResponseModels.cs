using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PokemonLLMBattle.Core.Models
{
    public record OrderDecision(object Tactics, string[] Order);
    public record ActionDecision(string Reason, JsonElement[] Actions);
}
