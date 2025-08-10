using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PokemonLLMBattle.Core.Models
{
    public interface Decision;

    public record PassDecision(): Decision;
    public record SwitchDecision(string SwitchInPokemon): Decision;
    public record ChooseMoveDecision(int ChoosePosition, string TargetPokemon, string Move, FieldSide FieldSide, bool Tera) : Decision;

    public record TeamOrderDecision(ImmutableArray<string> Pokemons): Decision;

    public record MultipleDecision(ImmutableArray<Decision> Decisions) : Decision;


    public enum FieldSide
    {
        Aliy,
        Opponent
    }

}


