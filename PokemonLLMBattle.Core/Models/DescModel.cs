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

        public static DescModel? GetItemById(this PokemonKnowledgeModel model, string id)
        {
            return model.Items.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }
        public static DescModel? GetAbilityById(this PokemonKnowledgeModel model, string id)
        {
            return model.Abilities.FirstOrDefault(ability => ability.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }
        public static DescModel? GetMoveById(this PokemonKnowledgeModel model, string id)
        {
            return model.Moves.FirstOrDefault(move => move.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        public static DescModel? GetItemByName(this PokemonKnowledgeModel model, string name)
        {
            return model.Items.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public static DescModel? GetAbilityByName(this PokemonKnowledgeModel model, string name)
        {
            return model.Abilities.FirstOrDefault(ability => ability.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public static DescModel? GetMoveByName(this PokemonKnowledgeModel model, string name)
        {
            return model.Moves.FirstOrDefault(move => move.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
