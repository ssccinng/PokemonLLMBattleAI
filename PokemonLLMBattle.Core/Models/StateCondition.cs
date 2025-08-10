using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PokemonLLMBattle.Core.Models
{

    public interface StateCondition
    {
        public static ChooseCondition ChooseCondition { get; } = new ChooseCondition();
        public static TeamOrderCondition DoubleDefault { get; } = new TeamOrderCondition(4);
    }

    public record ForceSwitchCondition(bool[] ForceSwitch) : StateCondition;
    public record TeamOrderCondition(int ChooseSize) : StateCondition;
    public record ChooseCondition() : StateCondition;
}


