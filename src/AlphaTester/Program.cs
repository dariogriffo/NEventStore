﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NEventStore;
using NLog;

namespace AlphaTester
{
	class Program
	{
		private static readonly Logger _log = LogManager.GetCurrentClassLogger(typeof(Program));

		static void Main(string[] args)
		{
			var repoType = eRepositoryType.AzureBlob;
			if (args.Length > 0)
			{
				if (args[0].Equals("sql", StringComparison.OrdinalIgnoreCase))
				{ repoType = eRepositoryType.Sql; }
			}

			var eventsPerAggregate = 30;
			var aggregatesToMake = 5;
			if (args.Length > 1)
			{ aggregatesToMake = Convert.ToInt32(args[1]); }

			var history = new ConcurrentBag<Tuple<Guid, ConcurrentBag<int>>>();

			var startTime = DateTime.UtcNow;
			var options = new ParallelOptions() { MaxDegreeOfParallelism = Math.Min(15, aggregatesToMake / 5 + 5) };
			Parallel.For(0, aggregatesToMake, options, (i) =>
			{
				var aggregateId = Guid.NewGuid();
				var values = new ConcurrentBag<int>();
				Stopwatch sw = new Stopwatch();
				sw.Start();
				
				var repo = new TestRepository(repoType);

				Stopwatch creationTimer = new Stopwatch();
				creationTimer.Start();
				values.Add(42);
				Thread.Sleep(new Random().Next(0, 1000));
				var aggy = SimpleAggregate.CreateNew(DateTime.Now, aggregateId, 42);
				repo.Save(aggy, Guid.NewGuid(), null);
				creationTimer.Stop();
				_log.Trace("Create aggy in [{0}]", creationTimer.Elapsed);

				Random random = new Random();
				var commitOptions = new ParallelOptions() { MaxDegreeOfParallelism = 10 };
				Parallel.For(0, eventsPerAggregate, options, (j) =>
				{
					values.Add(j);
					try
					{ RetryWhileConcurrent(repoType, aggy.Id, j); }
					catch (Exception ex)
					{ _log.Error("error iteration {0}-{1}, {2}", i, j, ex.ToString()); }

				});

				history.Add(new Tuple<Guid, ConcurrentBag<int>>(aggregateId, values));
				_log.Trace(string.Format("Iteration [{0}] took me [{1}] ms", i, sw.ElapsedMilliseconds));
			});

			var totalTime = DateTime.UtcNow.Subtract(startTime);
			_log.Info("checking aggregates for errors");
			Parallel.ForEach(history, (historyItem) =>
			{
				var repository = new TestRepository(repoType);
				CheckAggregate(repository, historyItem.Item1, historyItem.Item2);
			});
			_log.Info("Took [{0}] to source all aggregates", totalTime);
		}

		private static void RetryWhileConcurrent(eRepositoryType repoType, Guid aggyId, int value)
		{
			while (true)
			{
				try
				{
					Stopwatch sw = new Stopwatch();
					sw.Start();

					_log.Trace("Getting Aggregate [{0}]", aggyId);
					var repo = new TestRepository(repoType);
					var aggy = repo.GetSimpleAggregateById(aggyId, 0);
					aggy.ChangeFoo(value);

					_log.Trace("Saving Aggregate [{0}]", aggyId);
					repo.Save(aggy, Guid.NewGuid(), null);

					_log.Trace("Saved Aggregate [{0}]", aggyId);
					break;
				}
				catch (ConcurrencyException)
				{
					_log.Trace("Concurrency Detected, will retry shortly");
					var rand = new Random();
					Thread.Sleep(rand.Next(0, 200));	// this is to increase race condition likelyhood
				}
				catch (Exception ex)
				{
					_log.Trace("BAD!!!!!! Generic Exception, [{0}], we will let the aggregate retry though", ex.Message);
					var rand = new Random();
					Thread.Sleep(rand.Next(0, 200));	// this is to increase race condition likelyhood
				}
			}
		}

		private static void CheckAggregate(TestRepository repository, Guid aggregateId, IEnumerable<int> valuesItShouldHave)
		{
			var isGood = true;
			var aggy = repository.GetSimpleAggregateById(aggregateId, 0);
			foreach (var valueItShouldHave in valuesItShouldHave)
			{
				if (!aggy.FooHolder.Contains(valueItShouldHave))
				{
					_log.Error("Aggy [{0}] missing value [{1}].  There is something wrong!!!", aggregateId, valueItShouldHave);
					isGood = false;
				}
			}

			_log.Info("Aggregate [{0}] is {1}good", aggregateId, isGood ? string.Empty : "NOT ");
		}
	}
}
