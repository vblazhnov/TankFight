using MongoDB.Bson.Serialization.Attributes;

namespace StorageService.Models
{
	[BsonIgnoreExtraElements]
	public sealed class BattleInfo
	{
		public string BattleId { get; set; }
		public string Map { get; set; }
	}
}