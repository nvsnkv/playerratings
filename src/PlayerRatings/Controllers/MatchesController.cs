﻿using Microsoft.AspNet.Authorization;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Mvc;
using Microsoft.Data.Entity;
using Microsoft.Extensions.Localization;
using PlayerRatings.Models;
using PlayerRatings.Util;
using PlayerRatings.ViewModels.Match;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Csv;
using PlayerRatings.Localization;
using PlayerRatings.Services;

namespace PlayerRatings.Controllers
{
    [Authorize]
    public class MatchesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IStringLocalizer<MatchesController> _localizer;
        private readonly IInvitesService _invitesService;

        public MatchesController(ApplicationDbContext context, UserManager<ApplicationUser> userManager,
            IStringLocalizer<MatchesController> localizer, IInvitesService invitesService)
        {
            _context = context;
            _userManager = userManager;
            _localizer = localizer;
            _invitesService = invitesService;
        }

        public async Task<IActionResult> Index(Guid leagueId, int page = 0)
        {
            const int pageSize = 20;

            var currentUser = await User.GetApplicationUser(_userManager);

            var league = LeaguesController.GetAuthorizedLeague(leagueId, currentUser.Id, _context);

            if (league == null)
            {
                return HttpNotFound();
            }

            var matchesCount = _context.Match.Count(m => m.LeagueId == leagueId);
            var pagesCount = (int)Math.Ceiling((double)matchesCount / pageSize);

            if (page >= pagesCount && page != 0)
            {
                return HttpNotFound();
            }

            var matches =
                _context.Match.Where(m => m.LeagueId == leagueId)
                    .OrderByDescending(m => m.Date)
                    .Skip(pageSize*page)
                    .Take(pageSize)
                    .Include(m => m.FirstPlayer)
                    .Include(m => m.SecondPlayer)
                    .Include(m => m.League)
                    .ToList();

            return View(new MatchesViewModel(matches, leagueId, pagesCount, page));
        }

        private ICollection<League> GetLeagues(ApplicationUser currentUser, Guid? leagueId)
        {
            var query = _context.LeaguePlayers.Where(lp => lp.UserId == currentUser.Id);
            if (leagueId.HasValue)
            {
                var id = leagueId.Value;
                query = query.Where(lp => lp.LeagueId == id);
            }
            return query
                    .Select(lp => lp.League)
                    .Distinct()
                    .ToList();
        }

        private ICollection<ApplicationUser> GetUsers(IEnumerable<Guid> leagueIds)
        {
            return _context.LeaguePlayers.Where(lp => leagueIds.Contains(lp.LeagueId) && !lp.IsBlocked)
                .Select(lp => lp.User)
                .Distinct()
                .ToList();
        }

        // GET: /<controller>/
        public async Task<IActionResult> Create(Guid? leagueId)
        {
            var currentUser = await User.GetApplicationUser(_userManager);

            var leagues = GetLeagues(currentUser, leagueId);

            var leagueIds = leagues.Select(l => l.Id).ToList();
            var players = GetUsers(leagueIds);

            return View("Create", new NewResultViewModel(leagues, players)
            {
                LeagueId = leagues.First().Id,
                FirstPlayerId = currentUser.Id,
                SecondPlayerId = players.Except(new [] { currentUser }).FirstOrDefault()?.Id
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(NewResultViewModel model, bool toRating)
        {
            var currentUser = await User.GetApplicationUser(_userManager);

            if (ModelState.IsValid)
            {
                var league = LeaguesController.GetAuthorizedLeague(model.LeagueId, currentUser.Id, _context);

                if (league == null)
                {
                    ModelState.AddModelError("", _localizer[nameof(LocalizationKey.LeagueNotFound)]);
                    var leagues = GetLeagues(currentUser, null);
                    var leagueIds = leagues.Select(l => l.Id).ToList();
                    model.Leagues = leagues;
                    model.Users = GetUsers(leagueIds);
                    return View("Create", model);
                }

                var players =
                _context.LeaguePlayers.Where(lp => lp.LeagueId == league.Id && !lp.IsBlocked).Select(lp => lp.User).ToList();

                var firstPlayer = players.FirstOrDefault(p => p.Id == model.FirstPlayerId);
                var secondPlayer = players.FirstOrDefault(p => p.Id == model.SecondPlayerId);

                if (firstPlayer == null || secondPlayer == null)
                {
                    ModelState.AddModelError("", _localizer[nameof(LocalizationKey.PlayerNotFound)]);
                    model.Leagues = new[] {league};
                    model.Users = players;
                    return View("Create", model);
                }

                _context.Match.Add(new Match
                {
                    Id = Guid.NewGuid(),
                    Date = model.Date,
                    FirstPlayer = firstPlayer,
                    SecondPlayer = secondPlayer,
                    FirstPlayerScore = model.FirstPlayerScore,
                    SecondPlayerScore = model.SecondPlayerScore,
                    League = league,
                    CreatedByUser = currentUser
                });
                _context.SaveChanges();
                if (toRating)
                {
                    return RedirectToAction(nameof(LeaguesController.Rating), "Leagues", new {id = model.LeagueId});
                }
                else
                {
                    return RedirectToAction(nameof(Create), new { leagueId = model.LeagueId });
                }
            }

            model.Leagues = GetLeagues(currentUser, null);
            model.Users = GetUsers(model.Leagues.Select(l => l.Id));
            return View("Create", model);
        }

        public async Task<IActionResult> Edit(Guid id)
        {
            var match = _context.Match.Include(m => m.League).Single(m => m.Id == id);
            if (match == null)
            {
                return HttpNotFound();
            }

            var currentUser = await User.GetApplicationUser(_userManager);

            var league = match.League;

            if (league.CreatedByUserId != currentUser.Id && match.CreatedByUserId != currentUser.Id)
            {
                return HttpNotFound();
            }

            var leagues = new[] { match.League };
            var leagueIds = leagues.Select(l => l.Id).ToList();
            var players =
                _context.LeaguePlayers.Where(lp => leagueIds.Contains(lp.LeagueId)).Select(lp => lp.User).ToList();

            return View("Create", new NewResultViewModel(leagues, players)
            {
                LeagueId = leagues.First().Id,
                FirstPlayerId = match.FirstPlayerId,
                SecondPlayerId = match.SecondPlayerId,
                Date = match.Date,
                FirstPlayerScore = match.FirstPlayerScore,
                SecondPlayerScore = match.SecondPlayerScore
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, NewResultViewModel model)
        {
            if (ModelState.IsValid)
            {
                var match = _context.Match.Single(m => m.Id == id);
                if (match == null)
                {
                    return HttpNotFound();
                }

                var currentUser = await User.GetApplicationUser(_userManager);

                var league = LeaguesController.GetAuthorizedLeague(model.LeagueId, currentUser.Id, _context);

                if (league == null)
                {
                    ModelState.AddModelError("", _localizer[nameof(LocalizationKey.LeagueNotFound)]);
                    return View("Create", model);
                }

                var players =
                _context.LeaguePlayers.Where(lp => lp.LeagueId == league.Id).Select(lp => lp.User).ToList();

                var firstPlayer = players.FirstOrDefault(p => p.Id == model.FirstPlayerId);
                var secondPlayer = players.FirstOrDefault(p => p.Id == model.SecondPlayerId);

                if (firstPlayer == null || secondPlayer == null)
                {
                    ModelState.AddModelError("", _localizer[nameof(LocalizationKey.PlayerNotFound)]);
                    return View("Create", model);
                }

                match.FirstPlayer = firstPlayer;
                match.SecondPlayer = secondPlayer;
                match.FirstPlayerScore = model.FirstPlayerScore;
                match.SecondPlayerScore = model.SecondPlayerScore;
                match.Date = model.Date;

                _context.SaveChanges();

                var leagueId = league.Id;

                return RedirectToAction(nameof(Index), new
                {
                    leagueId
                });
            }

            return View("Create", model);
        }

        public async Task<IActionResult> Delete(Guid id)
        {
            var match = _context.Match.Include(m => m.League).Include(m => m.FirstPlayer).Include(m => m.SecondPlayer).Single(m => m.Id == id);
            if (match == null)
            {
                return HttpNotFound();
            }

            var currentUser = await User.GetApplicationUser(_userManager);

            var league = match.League;

            if (league.CreatedByUserId != currentUser.Id && match.CreatedByUserId != currentUser.Id)
            {
                return HttpNotFound();
            }

            return View(match);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(Guid id)
        {
            var match = _context.Match.Single(m => m.Id == id);
            if (match == null)
            {
                return HttpNotFound();
            }

            var currentUser = await User.GetApplicationUser(_userManager);

            var league = LeaguesController.GetAuthorizedLeague(match.LeagueId, currentUser.Id, _context);

            if (league == null)
            {
                return HttpNotFound();
            }

            _context.Match.Remove(match);
            _context.SaveChanges();

            var leagueId = league.Id;

            return RedirectToAction(nameof(Index), new
            {
                leagueId
            });
        }

        public async Task<IActionResult> Import(Guid leagueId)
        {
            var currentUser = await User.GetApplicationUser(_userManager);

            var league = LeaguesController.GetAuthorizedLeague(leagueId, currentUser.Id, _context);

            if (league == null)
            {
                return HttpNotFound();
            }

            return View(new ImportViewModel
            {
                DateIndex = 1,
                FirstPlayerEmailIndex = 2,
                FirstPlayerScoreIndex = 3,
                SecondPlayerEmailIndex = 4,
                SecondPlayerScoreIndex = 5,
                LeagueId = leagueId,
                DateTimeFormat = "dd.MM.yyyy H:mm:ss"
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Import(ImportViewModel model)
        {
            var currentUser = await User.GetApplicationUser(_userManager);

            var league = LeaguesController.GetAuthorizedLeague(model.LeagueId, currentUser.Id, _context);

            if (league == null)
            {
                return HttpNotFound();
            }
            var matches = new[] {model.File}.SelectMany(f =>
            {
                using (var stream = f.OpenReadStream())
                {
                    return CsvReader.ReadFromStream(stream).Select(line => new
                    {
                        Date = DateTimeOffset.ParseExact(line[model.DateIndex - 1], model.DateTimeFormat, null),
                        FirstPlayerEmail = line[model.FirstPlayerEmailIndex - 1].ToLower().Trim(),
                        FirstPlayerScore = Convert.ToInt32(line[model.FirstPlayerScoreIndex - 1]),
                        SecondPlayerEmail = line[model.SecondPlayerEmailIndex - 1].ToLower().Trim(),
                        SecondPlayerScore = Convert.ToInt32(line[model.SecondPlayerScoreIndex - 1]),
                        Factor =
                            model.FactorIndex.HasValue && !string.IsNullOrEmpty(line[model.FactorIndex.Value - 1])
                                ? Convert.ToDouble(line[model.FactorIndex.Value - 1])
                                : (double?) null
                    }).ToList();
                }
            }).ToList();

            var uniqueEmails =
                matches.Select(m => m.FirstPlayerEmail).Concat(matches.Select(m => m.SecondPlayerEmail)).Distinct();

            var users = await GetUsers(uniqueEmails, currentUser, league);

            foreach (var match in matches)
            {
                _context.Match.Add(new Match
                {
                    Id = Guid.NewGuid(),
                    CreatedByUser = currentUser,
                    Date = match.Date,
                    FirstPlayer = users[match.FirstPlayerEmail],
                    FirstPlayerScore = match.FirstPlayerScore,
                    SecondPlayer = users[match.SecondPlayerEmail],
                    SecondPlayerScore = match.SecondPlayerScore,
                    Factor = match.Factor,
                    League = league
                });
            }

            _context.SaveChanges();

            var leagueId = league.Id;
            return RedirectToAction(nameof(Index), new
            {
                leagueId
            });
        }

        private async Task<Dictionary<string, ApplicationUser>> GetUsers(IEnumerable<string> emails, ApplicationUser currentUser, League league)
        {
            var result = new Dictionary<string, ApplicationUser>();

            foreach (var email in emails)
            {
                result[email] = await _invitesService.Invite(email, currentUser, league);
            }

            return result;
        }
    }
}