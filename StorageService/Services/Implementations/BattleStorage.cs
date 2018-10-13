using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using StorageService.Exceptions;
using StorageService.Models;
using StorageService.Services.Interfaces;

namespace StorageService.Services.Implementations
{
	public sealed class BattleStorage : IBattleStorage
	{
		private static readonly string sortByFrameNumberDesc = $"{{ {nameof(Frame.FrameNumber)}: -1 }}";
		
		private readonly IMongoCollection<Frame> frameCollection;
		private readonly IMongoCollection<BattleInfo> battleCollection;

		public async Task<Frame> GetFrame(string battleId, uint frameNumber)
		{
			if (string.IsNullOrEmpty(battleId))
			{
				throw new ArgumentNullException(battleId);
			}
			
			// FIXME как-то весь метод просится переделать себя
			var result = await this.frameCollection.Find(x => x.BattleId == battleId && x.FrameNumber == frameNumber)
				.SingleOrDefaultAsync();

			if (result != null)
			{
				return result;
			}

			if (!this.IsBatleExists(battleId))
			{
				throw new BattleNotFoundException();
			}
			
			throw new FrameNotFoundException();
		}

		public async Task<IEnumerable<string>> GetWinners(string battleId)
		{
			if (string.IsNullOrEmpty(battleId))
			{
				throw new ArgumentNullException(battleId);
			}
			
			var allFrames = await this.frameCollection
				.Find(x => x.BattleId == battleId)
				.Sort(sortByFrameNumberDesc)
				.ToListAsync();

			if (allFrames.Count == 0)
			{
				throw new BattleNotFoundException();
			}
			
			// FIXME еще внимательно посмотреть на это, точно ли алгоритм верный
			// определяем, что битва закончилось, если остался 1 или 0 танков
			// победители - либо последний выживший, либо последние умершие в один кадр

			if (CountTanks(allFrames[0].GameState) > 1)
			{
				throw new BattleNotFinishedException();
			}

			foreach (var frame in allFrames)
			{
				if (CountTanks(frame.GameState) > 0)
				{
					return frame.GameState.ContentsInfo
						.Where(x => x.Type == CellContentType.Tank)
						.Select(x => x.UserId)
						.ToImmutableList();
				}
			}
			
			throw new InvalidOperationException();
		}

		public async Task PostFrame(Frame frame)
		{
			if (frame == null)
			{
				throw new ArgumentNullException(nameof(frame));
			}
			if (string.IsNullOrEmpty(frame.BattleId))
			{
				throw new ArgumentNullException(nameof(frame.BattleId));
			}
			
			var lastFrame = await this.frameCollection.Find(x => x.BattleId == frame.BattleId)
				.Sort(sortByFrameNumberDesc)
				.FirstOrDefaultAsync();

			if (lastFrame == null)
			{
				if (frame.FrameNumber != 0)
				{
					throw new InvalidOperationException(nameof(frame.FrameNumber));
				}

				if (!this.IsBatleExists(frame.BattleId))
				{
					throw new BattleNotFoundException();
				}
			}
			else if (frame.FrameNumber != lastFrame.FrameNumber + 1)
			{
				throw new InvalidOperationException(nameof(frame.FrameNumber));
			}

			try
			{
				await this.frameCollection.InsertOneAsync(frame);
			}
			catch (MongoDuplicateKeyException ex)
			{
				throw new InvalidOperationException(nameof(frame.FrameNumber), ex);
			}
		}

		public Task StartNewBattle(BattleInfo battleInfo)
		{
			if (battleInfo == null)
			{
				throw new ArgumentNullException(nameof(battleInfo));
			}
			if (string.IsNullOrEmpty(battleInfo.BattleId))
			{
				throw new ArgumentNullException(nameof(battleInfo.BattleId));
			}
			if (string.IsNullOrEmpty(battleInfo.Map))
			{
				throw new ArgumentNullException(nameof(battleInfo.Map));
			}

			try
			{
				return this.battleCollection.InsertOneAsync(battleInfo);
			}
			catch (MongoDuplicateKeyException ex)
			{
				throw new BattleAlreadyExistsException($"{nameof(battleInfo.BattleId)} == {battleInfo.BattleId} уже существует",
					ex);
			}
		}

		private bool IsBatleExists(string battleId) =>
			this.battleCollection.CountDocuments(x => x.BattleId == battleId) != 0;

		private static int CountTanks(GameState gameState) =>
			gameState.ContentsInfo.Count(x => x.Type == CellContentType.Tank);

		public BattleStorage(IMongoCollection<Frame> frameCollection, IMongoCollection<BattleInfo> battleCollection)
		{
			this.frameCollection = frameCollection;
			this.frameCollection.Indexes.CreateOne(new CreateIndexModel<Frame>(
				Builders<Frame>
					.IndexKeys
					.Ascending(x => x.BattleId)
					.Descending(x => x.FrameNumber),
				new CreateIndexOptions { Unique = true }));
			
			
			this.battleCollection = battleCollection;
			this.battleCollection.Indexes.CreateOne(new CreateIndexModel<BattleInfo>(
				Builders<BattleInfo>
					.IndexKeys
					.Ascending(x => x.BattleId),
				new CreateIndexOptions { Unique = true }));
		}
	}
}