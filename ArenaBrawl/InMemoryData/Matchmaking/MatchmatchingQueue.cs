﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ArenaBrawl.InMemoryData.Matchmaking
{
    public class MatchmatchingQueue
    {
        private readonly ConcurrentBag<PlayerWaitingForGame> _queue = new ConcurrentBag<PlayerWaitingForGame>();
        private readonly ConcurrentDictionary<Guid, PotentialMatch> _matches = new ConcurrentDictionary<Guid, PotentialMatch>();

        private CancellationTokenSource _pollingCancellationToken;

        public MatchmatchingQueue()
        {
            AttemptToMatchPlayers();
        }

        public async Task<bool> Add(PlayerWaitingForGame playerWaitingForGame)
        {
            _queue.Add(playerWaitingForGame);
            return true;
        }

        public async Task<bool> AcceptGame(Guid matchId, Guid playerId)
        {
            _matches.TryGetValue(matchId, out var result);

            if (result == null) return false;

            result.PlayersWaitingForGames.ForEach(p =>
            {
                if (p.Id == playerId) p.Accepted = true;
            });


            if (result.PlayersWaitingForGames.Select(p => p.Accepted).Any(a => a == false))
            {
                var updated = new PotentialMatch(result.PlayersWaitingForGames)
                {
                    Id = result.Id,
                };
                _matches.TryUpdate(matchId, updated, result);
                return true;
            }

            _matches.TryRemove(result.Id, out var x);
            MatchAcceptedByBothPlayers?.Invoke(result);
            return true;
        }


        private async void AttemptToMatchPlayers()
        {
            _pollingCancellationToken = new CancellationTokenSource();
            while (!_pollingCancellationToken.IsCancellationRequested)
            {
                if (_queue.Count > 1)
                {
                    _queue.TryTake(out var playerOne);
                    _queue.TryTake(out var playerTwo);
                    var potentialMatch = new PotentialMatch(new List<PlayerWaitingForGame>
                    {
                        playerOne,
                        playerTwo
                    });
                    MatchFound?.Invoke(potentialMatch);
                    _matches.TryAdd(potentialMatch.Id, potentialMatch);
                    Task.Run(() => TimeToAcceptGame(potentialMatch));
                }

                await Task.Delay(5000);
            }
        }

        private async void TimeToAcceptGame(PotentialMatch potentialMatch)
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            _matches.TryRemove(potentialMatch.Id, out var result);
            MatchAbandoned?.Invoke(result);
        }

        public event Action<PotentialMatch> MatchFound;
        public event Action<PotentialMatch> MatchAcceptedByBothPlayers;
        public event Action<PotentialMatch> MatchAbandoned;

        ~MatchmatchingQueue()
        {
            _pollingCancellationToken.Cancel();
        }

    }
}