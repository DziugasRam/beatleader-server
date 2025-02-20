﻿using Microsoft.EntityFrameworkCore;
using Prometheus.Client;
using System.Net;

namespace BeatLeader_Server.Services
{
    public class MinuteRefresh : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private readonly IGauge _rankedPlayerCounter;
        private readonly IGauge _rankedScoreCounter;

        private readonly IGauge _playerCounter;
        private readonly IGauge _scoreCounter;

        public MinuteRefresh(IServiceScopeFactory serviceScopeFactory, IMetricFactory metricFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;

            _rankedPlayerCounter = metricFactory.CreateGauge("ranked_player_count", "Ranked player count in the last 3 month");
            _rankedScoreCounter = metricFactory.CreateGauge("ranked_score_count", "Ranked score count");
            _playerCounter = metricFactory.CreateGauge("player_count", "Total player count");
            _scoreCounter = metricFactory.CreateGauge("score_count", "Total score count");
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            do {
                try {
                    await RefreshPrometheus();
                } catch {}

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested);
        }

        public async Task RefreshPrometheus()
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppContext>();

                int timeset = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - 60 * 60 * 24 * 30 * 3;

                _rankedPlayerCounter.Set(await _context.Players.Where(p => !p.Banned && p.ScoreStats.LastRankedScoreTime >= timeset).CountAsync());
                _playerCounter.Set(await _context.Players.CountAsync());

                _rankedScoreCounter.Set(await _context.Scores.Where(s => s.Pp > 0 && !s.Qualification && !s.Banned).CountAsync());
                _scoreCounter.Set(await _context.Scores.CountAsync());
            }
        }
    }
}
