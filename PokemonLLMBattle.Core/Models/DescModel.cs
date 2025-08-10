using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PokemonLLMBattle.Core.Models
{

    public record DescModel
    {
        public string Id { get; init; }
        public string Name { get; init; }
        public string Desc { get; init; }
        public string ShortDesc { get; init; }
        [JsonIgnore]
        public string Description
        {
            get
            {
                return string.IsNullOrEmpty(Desc) ? ShortDesc : Desc;
            }
        }
    }

    public record PokemonKnowledgeModel
    {
        public List<DescModel> Items { get; init; } = new List<DescModel>();
        public List<DescModel> Abilities { get; init; } = new List<DescModel>();
        public List<DescModel> Moves { get; init; } = new List<DescModel>();
    }
    public static class KnowledgeUtils
    {
        public static PokemonKnowledgeModel PokemonKnowledgeModel = JsonSerializer.Deserialize<PokemonKnowledgeModel>(
            System.IO.File.ReadAllText("Data/pokemon-text-data.json"), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase})!;

        extension(PokemonKnowledgeModel model)
        {
            public DescModel? GetItemById(string id)
            {
                return model.Items.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            }
            public  DescModel? GetAbilityById(string id)
            {
                return model.Abilities.FirstOrDefault(ability => ability.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            }
            public DescModel? GetMoveById(string id)
            {
                return model.Moves.FirstOrDefault(move => move.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            }

            public DescModel? GetItemByName(string name)
            {
                return model.Items.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }

            public DescModel? GetAbilityByName(string name)
            {
                return model.Abilities.FirstOrDefault(ability => ability.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }

            public DescModel? GetMoveByName(string name)
            {
                return model.Moves.FirstOrDefault(move => move.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            }

        }
    }
}
